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
            var isApproachTransit = IsApproachToApproachTransit(step);
            var collisionStart = isApproachTransit && previousApproach is not null
                ? previousApproach.Value
                : previous;

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
                AddMovement(resolvedSteps, ref previous, step, target);
                UpdateApproachTracking(step, target, ref previousApproach);
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

        var totalLength = resolvedSteps.Sum(step => step.TravelDistanceMm ?? 0);
        return new MeasurementTask
        {
            TaskId = task.TaskId,
            Name = task.Name,
            SourceFilePath = task.SourceFilePath,
            CreatedAt = task.CreatedAt,
            ToleranceStandard = task.ToleranceStandard,
            LengthUnit = task.LengthUnit,
            Steps = resolvedSteps,
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

    private static bool ShouldSkipCollisionCheck(MeasurementStep step)
    {
        var name = step.Name;

        if (name.Contains("Auto global safe GOTO", StringComparison.OrdinalIgnoreCase))
            return true;

        if (name.Contains("Auto inter-feature GOTO", StringComparison.OrdinalIgnoreCase))
            return true;

        if (name.Contains("Auto intra-feature GOTO", StringComparison.OrdinalIgnoreCase))
            return true;

        if (name.Contains("Return from inter-feature GOTO", StringComparison.OrdinalIgnoreCase))
            return true;

        if (name.Contains("Return from intra-feature GOTO", StringComparison.OrdinalIgnoreCase))
            return true;

        if (name.Contains("Enter", StringComparison.OrdinalIgnoreCase)
            && name.Contains("measurement path", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (name.Contains("Exit", StringComparison.OrdinalIgnoreCase)
            && name.Contains("measurement path", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (name.Contains("Return to approach point", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

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
        (double X, double Y, double Z) target)
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
        string reason)
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
            EstimatedTimeSeconds = distance / DefaultMachineSpeedMmPerSecond
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
            EstimatedTimeSeconds = source.EstimatedTimeSeconds
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
        var maxZ = task.Steps
            .SelectMany(step => EnumerateStepZValues(step))
            .DefaultIfEmpty(0)
            .Max();
        var probeRadius = ResolveProbeRadius(sourceStep);
        return maxZ
            + probeRadius
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
