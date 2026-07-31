namespace TZTEK.VispecCMM.Import.Core.Planning;

internal sealed class DefaultPathCollisionResolver : IPathCollisionResolver
{
    private const double DefaultMachineSpeedMmPerSecond = 20.0;

    private readonly ICollisionChecker _collisionChecker;

    public DefaultPathCollisionResolver(ICollisionChecker collisionChecker)
    {
        _collisionChecker = collisionChecker;
    }

    public MeasurementTask Resolve(MeasurementTask task, MeasurementPlanOptions options)
    {
        var anchors = BuildFeatureAnchors(task.Steps);
        var resolvedSteps = new List<MeasurementStep>();
        (double X, double Y, double Z)? previous = options.StartPoint;
        (double X, double Y, double Z)? previousApproach = null;
        var segmentIndex = 0;

        foreach (var step in task.Steps)
        {
            if (step.StepType != MeasurementStepType.Movement || step.GotoTarget is null)
            {
                var cloned = CloneStep(step, resolvedSteps.Count + 1);
                resolvedSteps.Add(cloned);
                previous = ResolveStepEnd(previous, cloned);
                continue;
            }

            var target = ToPoint(step.GotoTarget);
            var collisionStart = previous;

            if (collisionStart is null || ShouldSkipCollisionCheck(step))
            {
                AddMovement(resolvedSteps, ref previous, step, target);
                UpdateApproachTracking(step, target, ref previousApproach);
                continue;
            }

            segmentIndex++;
            var collision = options.EnableDirectTransitionShortcut
                ? _collisionChecker.Check(task, collisionStart.Value, target, step, options, segmentIndex)
                : new CollisionResult();

            if (!collision.HasCollision)
            {
                step.CollisionValidated = true;
                if (IsInterFeatureTransition(step))
                    step.MovementKind = "DirectTransition";
                AddMovement(resolvedSteps, ref previous, step, target);
                UpdateApproachTracking(step, target, ref previousApproach);
                continue;
            }

            if (IsInterFeatureTransition(step))
            {
                if (TryCreateValidatedSafePlanePath(
                    task,
                    collisionStart.Value,
                    target,
                    step,
                    options,
                    segmentIndex,
                    resolvedSteps,
                    ref previous,
                    out var safePathFailure))
                {
                    UpdateApproachTracking(step, target, ref previousApproach);
                    continue;
                }

                AddManualGotoRequiredMovement(
                    resolvedSteps,
                    ref previous,
                    step,
                    target,
                    $"{BuildCollisionReason(collision)}; {safePathFailure}");
                UpdateApproachTracking(step, target, ref previousApproach);
                continue;
            }

            if (step.Name.Contains("Return to global safety plane", StringComparison.OrdinalIgnoreCase))
            {
                if (TryCreateValidatedFinalSafetyReturn(
                    task,
                    collisionStart.Value,
                    step,
                    options,
                    segmentIndex,
                    resolvedSteps,
                    ref previous,
                    out var returnFailure))
                {
                    continue;
                }

                AddManualGotoRequiredMovement(resolvedSteps, ref previous, step, target, returnFailure);
                continue;
            }

            if (ShouldTryAutoGoto(step))
            {
                if (TryResolveInterFeatureCollision(
                    task,
                    collisionStart.Value,
                    target,
                    step,
                    options,
                    segmentIndex,
                    resolvedSteps,
                    ref previous,
                    ref previousApproach))
                {
                    continue;
                }

                if (options.RequireUserGotoWhenAnchorTransitionCollides)
                    AddManualGotoRequiredMovement(resolvedSteps, ref previous, step, target);
                else
                    AddCollisionRiskAcceptedMovement(resolvedSteps, ref previous, step, target);
                UpdateApproachTracking(step, target, ref previousApproach);
                continue;
            }

            if (options.EnableGotoAvoidance
                && IsFeatureAnchorTransition(step, anchors))
            {
                if (options.EnableAutoGlobalSafeGoto
                    && TryCreateAutoGlobalSafeGoto(task, collisionStart.Value, target, step, options, segmentIndex, out var autoGoto))
                {
                    AddAutoGlobalSafeGotoPath(resolvedSteps, ref previous, step, target, autoGoto);
                    UpdateApproachTracking(step, target, ref previousApproach);
                    continue;
                }

                if (TrySelectUserGoto(task, collisionStart.Value, target, step, options, segmentIndex, out var userGoto))
                {
                    AddUserGotoPath(resolvedSteps, ref previous, step, target, userGoto);
                    UpdateApproachTracking(step, target, ref previousApproach);
                    continue;
                }
            }

            if (options.RequireUserGotoWhenAnchorTransitionCollides)
                AddManualGotoRequiredMovement(resolvedSteps, ref previous, step, target);
            else
                AddCollisionRiskAcceptedMovement(resolvedSteps, ref previous, step, target);
            UpdateApproachTracking(step, target, ref previousApproach);
        }

        var totalLength = resolvedSteps.Where(step => step.IsExecutable).Sum(step => step.TravelDistanceMm ?? 0);
        return new MeasurementTask
        {
            TaskId = task.TaskId,
            Name = task.Name,
            SourceFilePath = task.SourceFilePath,
            CreatedAt = task.CreatedAt,
            ToleranceStandard = task.ToleranceStandard,
            LengthUnit = task.LengthUnit,
            Steps = resolvedSteps,
            CollisionPrimitives = task.CollisionPrimitives,
            ProbeConfigurations = task.ProbeConfigurations,
            GlobalSafetyPlane = task.GlobalSafetyPlane,
            PathOptimizationStrategy = task.PathOptimizationStrategy,
            TotalPathLengthMm = totalLength,
            EstimatedTotalTimeSeconds = totalLength / DefaultMachineSpeedMmPerSecond
        };
    }

    private static IReadOnlyDictionary<string, FeaturePathAnchor> BuildFeatureAnchors(IReadOnlyList<MeasurementStep> steps)
    {
        var anchors = new Dictionary<string, FeaturePathAnchor>(StringComparer.OrdinalIgnoreCase);
        var current = new List<MeasurementStep>();
        string? currentKey = null;

        foreach (var step in steps)
        {
            var key = GetFeatureKey(step);
            if (key is null)
            {
                Flush();
                continue;
            }

            if (currentKey is not null && !string.Equals(currentKey, key, StringComparison.OrdinalIgnoreCase))
                Flush();

            currentKey = key;
            current.Add(step);
        }

        Flush();
        return anchors;

        void Flush()
        {
            if (currentKey is null || current.Count == 0)
            {
                current.Clear();
                currentKey = null;
                return;
            }

            var movements = current
                .Where(step => step.StepType == MeasurementStepType.Movement && step.GotoTarget is not null)
                .ToList();
            if (movements.Count > 0)
            {
                anchors[currentKey] = new FeaturePathAnchor(
                    currentKey,
                    current.FirstOrDefault(step => step.TargetItem is not null)?.TargetItem,
                    movements.First().GotoTarget!,
                    movements.Last().GotoTarget!);
            }

            current.Clear();
            currentKey = null;
        }
    }

    private static bool IsFeatureAnchorTransition(
        MeasurementStep step,
        IReadOnlyDictionary<string, FeaturePathAnchor> anchors)
    {
        var key = GetFeatureKey(step);
        if (key is null || !anchors.TryGetValue(key, out var anchor))
            return false;

        return step.GotoTarget is not null
            && AreSamePoint(step.GotoTarget, anchor.EntryGoto);
    }

    private static void UpdateApproachTracking(
        MeasurementStep step,
        (double X, double Y, double Z) target,
        ref (double X, double Y, double Z)? previousApproach)
    {
        var name = step.Name;
        if (name.Contains("Return to approach point", StringComparison.OrdinalIgnoreCase))
        {
            previousApproach = target;
            return;
        }

        if (name.Contains("next", StringComparison.OrdinalIgnoreCase)
            && name.Contains("approach point", StringComparison.OrdinalIgnoreCase))
        {
            previousApproach = target;
            return;
        }

        if (name.Contains("Enter", StringComparison.OrdinalIgnoreCase)
            && name.Contains("measurement path", StringComparison.OrdinalIgnoreCase))
        {
            previousApproach = target;
            return;
        }

        if (name.Contains("entry approach point", StringComparison.OrdinalIgnoreCase))
        {
            previousApproach = target;
            return;
        }

        if (name.Contains("Move to approach point", StringComparison.OrdinalIgnoreCase))
        {
            previousApproach = target;
            return;
        }

        if (!name.Contains("measurement path", StringComparison.OrdinalIgnoreCase))
            previousApproach = null;
    }

    private static bool IsApproachToApproachTransit(MeasurementStep step)
    {
        var name = step.Name;
        if (name.Contains("Return to approach point", StringComparison.OrdinalIgnoreCase))
            return false;

        return name.Contains("entry approach point", StringComparison.OrdinalIgnoreCase)
            || (name.Contains("next", StringComparison.OrdinalIgnoreCase)
                && name.Contains("approach point", StringComparison.OrdinalIgnoreCase))
            || name.Contains("Move to approach point", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsInterFeatureTransition(MeasurementStep step)
    {
        return step.Name.Contains("entry approach point", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldTryAutoGoto(MeasurementStep step)
    {
        return IsApproachToApproachTransit(step);
    }

    private static bool ShouldSkipCollisionCheck(MeasurementStep step) => step.CollisionValidated;

    private bool TryCreateValidatedSafePlanePath(
        MeasurementTask task,
        (double X, double Y, double Z) start,
        (double X, double Y, double Z) target,
        MeasurementStep sourceStep,
        MeasurementPlanOptions options,
        int segmentIndex,
        List<MeasurementStep> resolvedSteps,
        ref (double X, double Y, double Z)? previous,
        out string failureReason)
    {
        if (!options.EnableGotoAvoidance || !options.EnableAutoGlobalSafeGoto)
        {
            failureReason = "automatic safety-plane transition is disabled";
            return false;
        }

        var safeZ = ResolveAutoSafeZ(task, sourceStep, options);
        if (!double.IsFinite(safeZ))
        {
            failureReason = "safety-plane height is invalid";
            return false;
        }

        var clearance = ResolveTransitionClearance(sourceStep, options);
        var previousFeatureStep = ResolvePreviousFeatureStep(task, sourceStep) ?? sourceStep;
        var escapeTask = BuildEscapeCollisionTask(task, previousFeatureStep);
        var departureCandidates = BuildTransitionCandidates(
                start,
                ResolvePreviousFeatureDirection(task, sourceStep),
                clearance,
                options.DefaultRetractDistanceMm)
            .Concat(BuildCylinderPortalCandidates(previousFeatureStep.TargetItem?.Primitive, clearance))
            .Distinct()
            .ToList();
        var arrivalCandidates = BuildTransitionCandidates(
                target,
                ResolveFeatureDirection(task, sourceStep),
                clearance,
                options.DefaultApproachDistanceMm)
            .Concat(BuildCylinderPortalCandidates(sourceStep.TargetItem?.Primitive, clearance))
            .Distinct()
            .ToList();
        var workpieceBounds = ResolveWorkpieceBoundsXY(task, clearance);

        ((double X, double Y, double Z) Point, MeasurementTask CheckTask, MeasurementStep CheckStep, string Kind, string Name)[]? route = null;
        var lastFailure = "no collision-free safety-plane route candidate";
        var directCandidates = departureCandidates.SelectMany(departure => arrivalCandidates.Select(arrival =>
            (Departure: departure, DepartureSide: departure, Arrival: arrival, ArrivalSide: arrival, UseSideCorridor: false)));
        var sideCandidates = departureCandidates
            .SelectMany(departure => ResolveSidePoints(departure, workpieceBounds)
                .SelectMany(departureSide => arrivalCandidates
                    .SelectMany(arrival => ResolveSidePoints(arrival, workpieceBounds)
                        .Select(arrivalSide => (Departure: departure, DepartureSide: departureSide, Arrival: arrival, ArrivalSide: arrivalSide, UseSideCorridor: true)))));
        foreach (var candidate in directCandidates.Concat(sideCandidates)
            .OrderBy(pair => Distance(start, pair.Departure)
                + Distance(pair.Departure, pair.DepartureSide)
                + Distance(pair.DepartureSide, WithZ(pair.DepartureSide, safeZ))
                + Distance(WithZ(pair.DepartureSide, safeZ), WithZ(pair.ArrivalSide, safeZ))
                + Distance(WithZ(pair.ArrivalSide, safeZ), pair.ArrivalSide)
                + Distance(pair.ArrivalSide, pair.Arrival)
                + Distance(pair.Arrival, target)))
        {
            var candidateRoute = candidate.UseSideCorridor
                ? new[]
                {
                    (Point: candidate.Departure, CheckTask: escapeTask, CheckStep: previousFeatureStep, Kind: "FeatureRetract", Name: "Auto feature retract"),
                    (Point: candidate.DepartureSide, CheckTask: task, CheckStep: previousFeatureStep, Kind: "SafeEnvelopeExit", Name: "Auto move to workpiece exterior"),
                    (Point: WithZ(candidate.DepartureSide, safeZ), CheckTask: task, CheckStep: previousFeatureStep, Kind: "SafePlaneLift", Name: "Auto safety-plane lift"),
                    (Point: WithZ(candidate.ArrivalSide, safeZ), CheckTask: task, CheckStep: sourceStep, Kind: "SafePlaneTraverse", Name: "Auto safety-plane traverse"),
                    (Point: candidate.ArrivalSide, CheckTask: task, CheckStep: sourceStep, Kind: "SafeEnvelopeEntry", Name: "Auto descend outside workpiece"),
                    (Point: candidate.Arrival, CheckTask: task, CheckStep: sourceStep, Kind: "FeatureApproach", Name: "Auto move to feature exterior"),
                    (Point: target, CheckTask: task, CheckStep: sourceStep, Kind: "FeatureApproach", Name: "Auto enter next feature")
                }
                : new[]
                {
                    (Point: candidate.Departure, CheckTask: escapeTask, CheckStep: previousFeatureStep, Kind: "FeatureRetract", Name: "Auto feature retract"),
                    (Point: WithZ(candidate.Departure, safeZ), CheckTask: task, CheckStep: previousFeatureStep, Kind: "SafePlaneLift", Name: "Auto safety-plane lift"),
                    (Point: WithZ(candidate.Arrival, safeZ), CheckTask: task, CheckStep: sourceStep, Kind: "SafePlaneTraverse", Name: "Auto safety-plane traverse"),
                    (Point: candidate.Arrival, CheckTask: task, CheckStep: sourceStep, Kind: "FeatureApproach", Name: "Auto move to feature exterior"),
                    (Point: target, CheckTask: task, CheckStep: sourceStep, Kind: "FeatureApproach", Name: "Auto enter next feature")
                };

            var cursor = start;
            var valid = true;
            foreach (var leg in candidateRoute)
            {
                var check = _collisionChecker.Check(leg.CheckTask, cursor, leg.Point, leg.CheckStep, options, segmentIndex);
                if (check.HasCollision)
                {
                    var ids = string.Join(", ", check.Collisions.SelectMany(item => item.InvolvedElementIds).Distinct().Take(5));
                    lastFailure = $"{leg.Kind} collision{(string.IsNullOrWhiteSpace(ids) ? string.Empty : $" with {ids}")}";
                    valid = false;
                    break;
                }
                cursor = leg.Point;
            }

            if (valid)
            {
                route = candidateRoute;
                break;
            }
        }

        if (route is null)
        {
            failureReason = lastFailure;
            return false;
        }

        foreach (var leg in route)
        {
            AddGeneratedMovement(
                resolvedSteps,
                ref previous,
                sourceStep,
                leg.Point,
                leg.Name,
                "Collision-free automatic safety-plane transition",
                leg.Kind,
                collisionValidated: true);
        }

        failureReason = string.Empty;
        return true;
    }

    private bool TryCreateValidatedFinalSafetyReturn(
        MeasurementTask task,
        (double X, double Y, double Z) start,
        MeasurementStep sourceStep,
        MeasurementPlanOptions options,
        int segmentIndex,
        List<MeasurementStep> resolvedSteps,
        ref (double X, double Y, double Z)? previous,
        out string failureReason)
    {
        var safeZ = ResolveAutoSafeZ(task, sourceStep, options);
        var clearance = ResolveTransitionClearance(sourceStep, options);
        var candidates = BuildTransitionCandidates(
            start,
            ResolvePreviousFeatureDirection(task, sourceStep),
            clearance,
            options.DefaultRetractDistanceMm);
        var lastFailure = "no collision-free final safety return";

        foreach (var departure in candidates.OrderBy(point => Distance(start, point)))
        {
            var safeTarget = (departure.X, departure.Y, safeZ);
            var retractCheck = _collisionChecker.Check(task, start, departure, sourceStep, options, segmentIndex);
            if (retractCheck.HasCollision)
            {
                lastFailure = "final feature retract collides";
                continue;
            }

            var liftCheck = _collisionChecker.Check(task, departure, safeTarget, sourceStep, options, segmentIndex);
            if (liftCheck.HasCollision)
            {
                lastFailure = "final safety-plane lift collides";
                continue;
            }

            AddGeneratedMovement(
                resolvedSteps,
                ref previous,
                sourceStep,
                departure,
                "Auto final feature retract",
                "Collision-free final feature retract",
                "FeatureRetract",
                collisionValidated: true);
            AddGeneratedMovement(
                resolvedSteps,
                ref previous,
                sourceStep,
                safeTarget,
                "Auto final safety-plane lift",
                "Collision-free final safety-plane return",
                "SafePlaneLift",
                collisionValidated: true);
            failureReason = string.Empty;
            return true;
        }

        failureReason = lastFailure;
        return false;
    }

    private static MeasurementTask BuildEscapeCollisionTask(
        MeasurementTask task,
        MeasurementStep sourceStep)
    {
        var excludeId = sourceStep.TargetItem?.Primitive.Id;
        var obstacles = CollisionPrimitiveSource.Resolve(task)
            .Where(primitive => excludeId is null
                || !string.Equals(primitive.Id, excludeId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return CloneCollisionContext(task, obstacles);
    }

    private static MeasurementTask CloneCollisionContext(
        MeasurementTask task,
        IReadOnlyList<Primitive> collisionPrimitives) => new()
    {
        TaskId = task.TaskId,
        Name = task.Name,
        SourceFilePath = task.SourceFilePath,
        CreatedAt = task.CreatedAt,
        ToleranceStandard = task.ToleranceStandard,
        LengthUnit = task.LengthUnit,
        Steps = [],
        CollisionPrimitives = collisionPrimitives,
        ProbeConfigurations = task.ProbeConfigurations,
        GlobalSafetyPlane = task.GlobalSafetyPlane,
        PathOptimizationStrategy = task.PathOptimizationStrategy
    };

    private bool TryResolveInterFeatureCollision(
        MeasurementTask task,
        (double X, double Y, double Z) start,
        (double X, double Y, double Z) target,
        MeasurementStep sourceStep,
        MeasurementPlanOptions options,
        int segmentIndex,
        List<MeasurementStep> resolvedSteps,
        ref (double X, double Y, double Z)? previous,
        ref (double X, double Y, double Z)? previousApproach)
    {
        if (!options.EnableGotoAvoidance)
            return false;

        if (TrySelectUserGoto(task, start, target, sourceStep, options, segmentIndex, out var userGoto))
        {
            AddInterFeatureGotoPath(resolvedSteps, ref previous, sourceStep, target, userGoto, isUserGoto: true);
            UpdateApproachTracking(sourceStep, target, ref previousApproach);
            return true;
        }

        var searchCeilingZ = ResolveAutoSafeZ(task, sourceStep, options);
        if (InterFeatureGotoFinder.TryFindShortestCollisionFreeGoto(
            _collisionChecker,
            task,
            start,
            target,
            sourceStep,
            options,
            segmentIndex,
            searchCeilingZ,
            out var autoGoto))
        {
            AddInterFeatureGotoPath(resolvedSteps, ref previous, sourceStep, target, autoGoto, isUserGoto: false);
            UpdateApproachTracking(sourceStep, target, ref previousApproach);
            return true;
        }

        return false;
    }

    private static void AddInterFeatureGotoPath(
        List<MeasurementStep> steps,
        ref (double X, double Y, double Z)? previous,
        MeasurementStep sourceStep,
        (double X, double Y, double Z) target,
        GotoPoint gotoPoint,
        bool isUserGoto)
    {
        var isInterFeature = sourceStep.Name.Contains("entry approach point", StringComparison.OrdinalIgnoreCase);
        AddGeneratedMovement(
            steps,
            ref previous,
            sourceStep,
            ToPoint(gotoPoint),
            isUserGoto ? "Collision avoidance user GOTO" : isInterFeature ? "Auto inter-feature GOTO" : "Auto intra-feature GOTO",
            gotoPoint.Reason);
        AddGeneratedMovement(
            steps,
            ref previous,
            sourceStep,
            target,
            isInterFeature ? "Return from inter-feature GOTO" : "Return from intra-feature GOTO",
            isInterFeature
                ? "Return from inter-feature GOTO to feature entry"
                : "Return from intra-feature GOTO to next approach point");
    }

    private bool TrySelectUserGoto(
        MeasurementTask task,
        (double X, double Y, double Z) start,
        (double X, double Y, double Z) target,
        MeasurementStep sourceStep,
        MeasurementPlanOptions options,
        int segmentIndex,
        out GotoPoint gotoPoint)
    {
        var candidates = options.UserGotoPoints
            .Where(point => IsFinite(point.X) && IsFinite(point.Y) && IsFinite(point.Z))
            .OrderBy(point => Distance(start, ToPoint(point)) + Distance(ToPoint(point), target))
            .ToList();

        foreach (var candidate in candidates)
        {
            var point = ToPoint(candidate);
            if (IsCollisionFreeViaGoto(task, start, point, target, sourceStep, options, segmentIndex))
            {
                gotoPoint = new GotoPoint
                {
                    Id = string.IsNullOrWhiteSpace(candidate.Id) ? $"user_goto_{segmentIndex}" : candidate.Id,
                    X = candidate.X,
                    Y = candidate.Y,
                    Z = candidate.Z,
                    Reason = string.IsNullOrWhiteSpace(candidate.Reason)
                        ? "User feature anchor GOTO"
                        : candidate.Reason
                };
                return true;
            }
        }

        gotoPoint = new GotoPoint();
        return false;
    }

    private bool TryCreateAutoGlobalSafeGoto(
        MeasurementTask task,
        (double X, double Y, double Z) start,
        (double X, double Y, double Z) target,
        MeasurementStep sourceStep,
        MeasurementPlanOptions options,
        int segmentIndex,
        out GotoPoint gotoPoint)
    {
        var baseSafeZ = ResolveAutoSafeZ(task, sourceStep, options);
        if (!double.IsFinite(baseSafeZ))
        {
            gotoPoint = new GotoPoint();
            return false;
        }

        var liftStep = Math.Max(1.0, options.SafetyClearanceMm);
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var safeZ = baseSafeZ + liftStep * attempt;
            var startAbove = (X: start.X, Y: start.Y, Z: safeZ);
            var targetAbove = (X: target.X, Y: target.Y, Z: safeZ);
            var horizontal = _collisionChecker.Check(task, startAbove, targetAbove, sourceStep, options, segmentIndex);
            if (horizontal.HasCollision)
                continue;

            gotoPoint = new GotoPoint
            {
                Id = $"auto_global_safe_goto_{segmentIndex}",
                X = targetAbove.X,
                Y = targetAbove.Y,
                Z = targetAbove.Z,
                Reason = attempt == 0
                    ? "Auto global safe GOTO"
                    : $"Auto global safe GOTO, lifted {attempt} safety step(s)"
            };
            return true;
        }

        gotoPoint = new GotoPoint();
        return false;
    }

    private bool IsCollisionFreeViaGoto(
        MeasurementTask task,
        (double X, double Y, double Z) start,
        (double X, double Y, double Z) gotoPoint,
        (double X, double Y, double Z) target,
        MeasurementStep sourceStep,
        MeasurementPlanOptions options,
        int segmentIndex)
    {
        var first = _collisionChecker.Check(task, start, gotoPoint, sourceStep, options, segmentIndex);
        if (first.HasCollision)
            return false;

        var second = _collisionChecker.Check(task, gotoPoint, target, sourceStep, options, segmentIndex);
        return !second.HasCollision;
    }

    private static void AddUserGotoPath(
        List<MeasurementStep> steps,
        ref (double X, double Y, double Z)? previous,
        MeasurementStep sourceStep,
        (double X, double Y, double Z) target,
        GotoPoint gotoPoint)
    {
        AddGeneratedMovement(
            steps,
            ref previous,
            sourceStep,
            ToPoint(gotoPoint),
            "Collision avoidance user GOTO",
            gotoPoint.Reason);
        AddGeneratedMovement(
            steps,
            ref previous,
            sourceStep,
            target,
            "Return from user GOTO to feature anchor",
            "Return from user GOTO to feature anchor");
    }

    private static void AddAutoGlobalSafeGotoPath(
        List<MeasurementStep> steps,
        ref (double X, double Y, double Z)? previous,
        MeasurementStep sourceStep,
        (double X, double Y, double Z) target,
        GotoPoint gotoPoint)
    {
        if (previous is null)
        {
            AddMovement(steps, ref previous, sourceStep, target);
            return;
        }

        var startAbove = (previous.Value.X, previous.Value.Y, gotoPoint.Z);
        var targetAbove = (gotoPoint.X, gotoPoint.Y, gotoPoint.Z);
        AddGeneratedMovement(
            steps,
            ref previous,
            sourceStep,
            startAbove,
            "Auto global safe GOTO lift",
            "Lift to auto global safe GOTO height");
        AddGeneratedMovement(
            steps,
            ref previous,
            sourceStep,
            targetAbove,
            "Auto global safe GOTO traverse",
            "Traverse on auto global safe GOTO height");
        AddGeneratedMovement(
            steps,
            ref previous,
            sourceStep,
            target,
            "Return from auto global safe GOTO",
            "Return from auto global safe GOTO to feature anchor");
    }

    private static void AddManualGotoRequiredMovement(
        List<MeasurementStep> steps,
        ref (double X, double Y, double Z)? previous,
        MeasurementStep sourceStep,
        (double X, double Y, double Z) target,
        string? collisionReason = null)
    {
        var cloned = CloneStep(sourceStep, steps.Count + 1);
        cloned.Name = $"{sourceStep.Name} - Needs manual GOTO point";
        var isApproachTransit = IsApproachToApproachTransit(sourceStep);
        var isInterFeature = sourceStep.Name.Contains("entry approach point", StringComparison.OrdinalIgnoreCase);
        cloned.GotoTarget = new GotoPoint
        {
            Id = sourceStep.GotoTarget?.Id ?? $"manual_goto_required_{steps.Count + 1}",
            X = target.X,
            Y = target.Y,
            Z = target.Z,
            Reason = isInterFeature
                ? "Inter-feature transit has collision risk; needs manual GOTO point"
                : isApproachTransit
                    ? "Approach-point transit has collision risk; needs manual GOTO point"
                    : "Anchor transition has collision risk; needs manual GOTO point"
        };
        cloned.TravelDistanceMm = previous is null ? 0 : Distance(previous.Value, target);
        cloned.EstimatedTimeSeconds = cloned.TravelDistanceMm.Value / DefaultMachineSpeedMmPerSecond;
        cloned.IsCollisionRisk = true;
        cloned.RequiresManualGoto = true;
        cloned.IsExecutable = false;
        cloned.MovementKind = "CollisionRisk";
        cloned.CollisionReason = collisionReason ?? cloned.GotoTarget.Reason;
        steps.Add(cloned);
        previous = target;
    }

    private static void AddCollisionRiskAcceptedMovement(
        List<MeasurementStep> steps,
        ref (double X, double Y, double Z)? previous,
        MeasurementStep sourceStep,
        (double X, double Y, double Z) target)
    {
        var cloned = CloneStep(sourceStep, steps.Count + 1);
        cloned.Name = $"{sourceStep.Name} - Collision risk accepted";
        var isApproachTransit = IsApproachToApproachTransit(sourceStep);
        var isInterFeature = sourceStep.Name.Contains("entry approach point", StringComparison.OrdinalIgnoreCase);
        cloned.GotoTarget = new GotoPoint
        {
            Id = sourceStep.GotoTarget?.Id ?? $"collision_risk_{steps.Count + 1}",
            X = target.X,
            Y = target.Y,
            Z = target.Z,
            Reason = isInterFeature
                ? "Inter-feature transit has collision risk; direct connection kept without safety plane detour"
                : isApproachTransit
                    ? "Approach-point transit passes through a measurable face; collision risk accepted"
                    : "Collision risk accepted without user GOTO point"
        };
        cloned.TravelDistanceMm = previous is null ? 0 : Distance(previous.Value, target);
        cloned.EstimatedTimeSeconds = cloned.TravelDistanceMm.Value / DefaultMachineSpeedMmPerSecond;
        cloned.IsCollisionRisk = true;
        cloned.IsExecutable = false;
        cloned.MovementKind = "CollisionRisk";
        cloned.CollisionReason = cloned.GotoTarget.Reason;
        steps.Add(cloned);
        previous = target;
    }

    private static void AddMovement(
        List<MeasurementStep> steps,
        ref (double X, double Y, double Z)? previous,
        MeasurementStep sourceStep,
        (double X, double Y, double Z) target)
    {
        var cloned = CloneStep(sourceStep, steps.Count + 1);
        cloned.TravelDistanceMm = previous is null ? 0 : Distance(previous.Value, target);
        cloned.EstimatedTimeSeconds = cloned.TravelDistanceMm.Value / DefaultMachineSpeedMmPerSecond;
        steps.Add(cloned);
        previous = target;
    }

    private static void AddGeneratedMovement(
        List<MeasurementStep> steps,
        ref (double X, double Y, double Z)? previous,
        MeasurementStep sourceStep,
        (double X, double Y, double Z) target,
        string name,
        string reason,
        string movementKind = "SafeTransition",
        bool collisionValidated = false)
    {
        var distance = previous is null ? 0 : Distance(previous.Value, target);
        steps.Add(new MeasurementStep
        {
            SequenceNumber = steps.Count + 1,
            StepType = MeasurementStepType.Movement,
            Name = name,
            TargetItem = sourceStep.TargetItem,
            ProbeAssignment = sourceStep.ProbeAssignment,
            SafetyPlane = sourceStep.SafetyPlane,
            GotoTarget = new GotoPoint
            {
                Id = $"goto_{steps.Count + 1}",
                X = target.X,
                Y = target.Y,
                Z = target.Z,
                Reason = reason
            },
            TravelDistanceMm = distance,
            EstimatedTimeSeconds = distance / DefaultMachineSpeedMmPerSecond,
            MovementKind = movementKind,
            CollisionValidated = collisionValidated
        });
        previous = target;
    }

    private static MeasurementStep CloneStep(MeasurementStep source, int sequenceNumber)
    {
        return new MeasurementStep
        {
            SequenceNumber = sequenceNumber,
            StepType = source.StepType,
            Name = source.Name,
            TargetItem = source.TargetItem,
            ProbeAssignment = source.ProbeAssignment,
            MeasurementPoints = source.MeasurementPoints,
            FittingMethod = source.FittingMethod,
            GotoTarget = source.GotoTarget,
            SafetyPlane = source.SafetyPlane,
            NewProbe = source.NewProbe,
            LightingInfo = source.LightingInfo,
            TravelDistanceMm = source.TravelDistanceMm,
            EstimatedTimeSeconds = source.EstimatedTimeSeconds,
            CollisionValidated = source.CollisionValidated,
            IsCollisionRisk = source.IsCollisionRisk,
            RequiresManualGoto = source.RequiresManualGoto,
            IsExecutable = source.IsExecutable,
            MovementKind = source.MovementKind,
            CollisionReason = source.CollisionReason
        };
    }

    private static (double X, double Y, double Z)? ResolveStepEnd(
        (double X, double Y, double Z)? previous,
        MeasurementStep step)
    {
        if (step.GotoTarget is not null)
            return ToPoint(step.GotoTarget);

        var point = step.MeasurementPoints?.LastOrDefault();
        return point is null ? previous : (point.X, point.Y, point.Z);
    }

    private static string? GetFeatureKey(MeasurementStep step) =>
        step.TargetItem?.Primitive.Id;

    private static double ResolveAutoSafeZ(MeasurementTask task, MeasurementStep sourceStep, MeasurementPlanOptions options)
    {
        var primitiveZ = CollisionPrimitiveSource.Resolve(task, options).SelectMany(EnumeratePrimitiveZValues);
        var maxZ = primitiveZ
            .DefaultIfEmpty(0)
            .Max();
        var probeRadius = ResolveProbeRadius(sourceStep);
        return maxZ
            + (options.TreatProbeAsPoint ? 0 : ResolveProbeLength(sourceStep) + probeRadius)
            + Math.Max(0, options.CollisionSafetyMarginMm)
            + Math.Max(0, options.SafetyClearanceMm)
            + Math.Max(0, options.AutoSafeGotoExtraClearanceMm);
    }

    private static IEnumerable<double> EnumerateStepZValues(MeasurementStep step)
    {
        if (step.GotoTarget is not null)
            yield return step.GotoTarget.Z;

        foreach (var point in step.MeasurementPoints ?? [])
            yield return point.Z;

        if (step.TargetItem?.Primitive is not Primitive primitive)
            yield break;

        foreach (var value in EnumeratePrimitiveZValues(primitive))
            yield return value;
    }

    private static IEnumerable<double> EnumeratePrimitiveZValues(Primitive primitive)
    {
        var point = primitive.GetRepresentativePoint();
        yield return point.Z;

        switch (primitive)
        {
            case CylinderPrimitive cylinder:
                var axis = Normalize((cylinder.AxisDirX, cylinder.AxisDirY, cylinder.AxisDirZ));
                var length = Math.Max(cylinder.Radius, Math.Sqrt(cylinder.SourceAreaMm2 ?? 0) / Math.Max(cylinder.Radius * Math.PI * 2, 1));
                yield return cylinder.AxisPointZ + axis.Z * length / 2 + cylinder.Radius;
                yield return cylinder.AxisPointZ - axis.Z * length / 2 - cylinder.Radius;
                break;
            case SpherePrimitive sphere:
                yield return sphere.CenterZ + sphere.Radius;
                yield return sphere.CenterZ - sphere.Radius;
                break;
            case CirclePrimitive circle:
                yield return circle.CenterZ + circle.Radius;
                yield return circle.CenterZ - circle.Radius;
                break;
            case ArcPrimitive arc:
                yield return arc.CenterZ + arc.Radius;
                yield return arc.CenterZ - arc.Radius;
                break;
            case Surface3DPrimitive surface:
                foreach (var vertex in surface.Vertices)
                    yield return vertex.Z;
                break;
        }
    }

    private static (double X, double Y, double Z) ToPoint(GotoPoint point) =>
        (point.X, point.Y, point.Z);

    private static bool AreSamePoint(GotoPoint left, GotoPoint right)
    {
        return Distance(ToPoint(left), ToPoint(right)) <= 1e-6;
    }

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    private static double ResolveProbeRadius(MeasurementStep step)
    {
        var diameter = step.ProbeAssignment?.TipDiameter ?? 2.0;
        return double.IsFinite(diameter) && diameter > 0 ? diameter / 2.0 : 1.0;
    }

    private static double ResolveProbeLength(MeasurementStep step)
    {
        var length = step.ProbeAssignment?.TipLength ?? 30.0;
        return double.IsFinite(length) && length > 0 ? length : 30.0;
    }

    private static double ResolveTransitionClearance(MeasurementStep step, MeasurementPlanOptions options) =>
        (options.TreatProbeAsPoint ? 0 : ResolveProbeLength(step) + ResolveProbeRadius(step))
        + Math.Max(0, options.CollisionSafetyMarginMm)
        + Math.Max(0, options.AutoSafeGotoExtraClearanceMm);

    private static (double X, double Y, double Z) ResolvePreviousFeatureDirection(
        MeasurementTask task,
        MeasurementStep sourceStep)
    {
        var index = -1;
        for (var i = 0; i < task.Steps.Count; i++)
        {
            if (ReferenceEquals(task.Steps[i], sourceStep))
            {
                index = i;
                break;
            }
        }
        for (var i = index - 1; i >= 0; i--)
        {
            var candidateStep = task.Steps[i];
            if (candidateStep.TargetItem?.Primitive is CylinderPrimitive cylinder)
                return Normalize((cylinder.AxisDirX, cylinder.AxisDirY, cylinder.AxisDirZ));

            var point = candidateStep.MeasurementPoints?.LastOrDefault();
            if (point is not null)
                return Normalize((point.NormalX, point.NormalY, point.NormalZ));
        }

        return (0, 0, 1);
    }

    private static MeasurementStep? ResolvePreviousFeatureStep(MeasurementTask task, MeasurementStep sourceStep)
    {
        var sourceFeatureId = sourceStep.TargetItem?.Primitive.Id;
        var sourceIndex = -1;
        for (var i = 0; i < task.Steps.Count; i++)
        {
            if (ReferenceEquals(task.Steps[i], sourceStep))
            {
                sourceIndex = i;
                break;
            }
        }

        for (var i = sourceIndex - 1; i >= 0; i--)
        {
            var candidate = task.Steps[i];
            if (candidate.TargetItem is not null
                && !string.Equals(candidate.TargetItem.Primitive.Id, sourceFeatureId, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string BuildCollisionReason(CollisionResult collision)
    {
        var sourceIds = collision.Collisions
            .SelectMany(item => item.InvolvedElementIds)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();
        return sourceIds.Count == 0
            ? "Inter-feature direct path and automatic safety-plane path both collide"
            : $"Inter-feature transition collides with {string.Join(", ", sourceIds)}; automatic safety-plane path also failed";
    }

    private static (double X, double Y, double Z) ResolveFeatureDirection(
        MeasurementTask task,
        MeasurementStep sourceStep)
    {
        var featureId = sourceStep.TargetItem?.Primitive.Id;
        if (sourceStep.TargetItem?.Primitive is CylinderPrimitive cylinder)
            return Normalize((cylinder.AxisDirX, cylinder.AxisDirY, cylinder.AxisDirZ));

        var point = task.Steps
            .Where(step => string.Equals(step.TargetItem?.Primitive.Id, featureId, StringComparison.OrdinalIgnoreCase))
            .SelectMany(step => step.MeasurementPoints ?? [])
            .FirstOrDefault();
        return point is null
            ? Normalize(sourceStep.TargetItem?.Primitive.GetDirection() is { } direction
                ? (direction.I, direction.J, direction.K)
                : (0, 0, 1))
            : Normalize((point.NormalX, point.NormalY, point.NormalZ));
    }

    private static (double X, double Y, double Z) Offset(
        (double X, double Y, double Z) point,
        (double X, double Y, double Z) direction,
        double distance) =>
        (point.X + direction.X * distance, point.Y + direction.Y * distance, point.Z + direction.Z * distance);

    private static IReadOnlyList<(double X, double Y, double Z)> BuildTransitionCandidates(
        (double X, double Y, double Z) point,
        (double X, double Y, double Z) direction,
        double fullClearance,
        double localClearance)
    {
        var normalized = Normalize(direction);
        var shortDistance = double.IsFinite(localClearance) && localClearance > 0
            ? localClearance
            : 5.0;
        return new[]
        {
            Offset(point, normalized, fullClearance),
            Offset(point, (-normalized.X, -normalized.Y, -normalized.Z), fullClearance),
            Offset(point, normalized, shortDistance),
            Offset(point, (-normalized.X, -normalized.Y, -normalized.Z), shortDistance),
            point
        }.Distinct().ToList();
    }

    private static IReadOnlyList<(double X, double Y, double Z)> BuildCylinderPortalCandidates(
        Primitive? primitive,
        double clearance)
    {
        if (primitive is not CylinderPrimitive cylinder)
            return [];

        var axis = Normalize((cylinder.AxisDirX, cylinder.AxisDirY, cylinder.AxisDirZ));
        (double X, double Y, double Z) start;
        (double X, double Y, double Z) end;
        if (cylinder is { AxisStartX: not null, AxisStartY: not null, AxisStartZ: not null,
                          AxisEndX: not null, AxisEndY: not null, AxisEndZ: not null })
        {
            start = (cylinder.AxisStartX.Value, cylinder.AxisStartY.Value, cylinder.AxisStartZ.Value);
            end = (cylinder.AxisEndX.Value, cylinder.AxisEndY.Value, cylinder.AxisEndZ.Value);
        }
        else
        {
            var halfLength = Math.Max(cylinder.Length ?? 0, cylinder.Radius * 2) / 2;
            var center = (cylinder.AxisPointX, cylinder.AxisPointY, cylinder.AxisPointZ);
            start = Offset(center, (-axis.X, -axis.Y, -axis.Z), halfLength);
            end = Offset(center, axis, halfLength);
        }

        return
        [
            Offset(start, (-axis.X, -axis.Y, -axis.Z), clearance),
            Offset(end, axis, clearance)
        ];
    }

    private static (double MinX, double MaxX, double MinY, double MaxY) ResolveWorkpieceBoundsXY(
        MeasurementTask task,
        double clearance)
    {
        var primitives = CollisionPrimitiveSource.Resolve(task);
        var points = new List<(double X, double Y)>();
        foreach (var primitive in primitives)
        {
            var point = primitive.GetRepresentativePoint();
            var radius = primitive switch
            {
                CylinderPrimitive value => value.Radius,
                CirclePrimitive value => value.Radius,
                ArcPrimitive value => value.Radius,
                SpherePrimitive value => value.Radius,
                _ => 0
            };
            points.Add((point.X - radius, point.Y - radius));
            points.Add((point.X + radius, point.Y + radius));

            if (primitive is Surface3DPrimitive surface)
                points.AddRange(surface.Vertices.Select(vertex => (vertex.X, vertex.Y)));
            if (primitive is CylinderPrimitive { AxisStartX: not null, AxisStartY: not null, AxisEndX: not null, AxisEndY: not null } cylinder)
            {
                points.Add((cylinder.AxisStartX.Value - cylinder.Radius, cylinder.AxisStartY.Value - cylinder.Radius));
                points.Add((cylinder.AxisEndX.Value + cylinder.Radius, cylinder.AxisEndY.Value + cylinder.Radius));
            }
        }

        if (points.Count == 0)
            return (-clearance, clearance, -clearance, clearance);
        return (
            points.Min(point => point.X) - clearance,
            points.Max(point => point.X) + clearance,
            points.Min(point => point.Y) - clearance,
            points.Max(point => point.Y) + clearance);
    }

    private static IReadOnlyList<(double X, double Y, double Z)> ResolveSidePoints(
        (double X, double Y, double Z) point,
        (double MinX, double MaxX, double MinY, double MaxY) bounds)
    {
        var candidates = new[]
        {
            (X: bounds.MinX, Y: point.Y, point.Z),
            (X: bounds.MaxX, Y: point.Y, point.Z),
            (X: point.X, Y: bounds.MinY, point.Z),
            (X: point.X, Y: bounds.MaxY, point.Z)
        };
        return candidates.OrderBy(candidate => Distance(point, candidate)).ToList();
    }

    private static (double X, double Y, double Z) WithZ(
        (double X, double Y, double Z) point,
        double z) =>
        (point.X, point.Y, z);

    private static (double X, double Y, double Z) Normalize((double X, double Y, double Z) value)
    {
        var length = Math.Sqrt(value.X * value.X + value.Y * value.Y + value.Z * value.Z);
        return length < 1e-12 ? (0, 0, 1) : (value.X / length, value.Y / length, value.Z / length);
    }

    private static double Distance(
        (double X, double Y, double Z) left,
        (double X, double Y, double Z) right)
    {
        var dx = left.X - right.X;
        var dy = left.Y - right.Y;
        var dz = left.Z - right.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    private sealed record FeaturePathAnchor(
        string FeatureKey,
        PrimitiveToleranceItem? TargetItem,
        GotoPoint EntryGoto,
        GotoPoint ExitGoto);
}
