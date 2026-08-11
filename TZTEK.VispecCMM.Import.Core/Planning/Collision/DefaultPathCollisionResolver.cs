namespace TZTEK.VispecCMM.Import.Core.Planning.Collision;

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
        if (!options.EnableCollisionCheck && !options.EnableGotoAvoidance)
            return task;

        var resolvedSteps = new List<MeasurementStep>();
        (double X, double Y, double Z)? previous = options.StartPoint;
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

            if (!options.EnableCollisionCheck
                || !PathMovementClassifier.RequiresCollisionCheck(step)
                || previous is null)
            {
                AddMovement(resolvedSteps, ref previous, step, ToPoint(step.GotoTarget));
                continue;
            }

            segmentIndex++;
            var target = ToPoint(step.GotoTarget);
            var collision = _collisionChecker.Check(task, previous.Value, target, step, options, segmentIndex);

            if (!collision.HasCollision)
            {
                var validated = CloneStep(step, resolvedSteps.Count + 1);
                validated.CollisionValidated = true;
                resolvedSteps.Add(validated);
                previous = target;
                continue;
            }

            if (!options.EnableGotoAvoidance)
            {
                AddCollisionRiskMovement(resolvedSteps, ref previous, step, target, collision);
                continue;
            }

            var route = GotoAvoidancePlanner.FindMinimalRoute(
                task,
                previous.Value,
                target,
                step,
                options,
                _collisionChecker,
                segmentIndex);

            if (route is null)
            {
                AddManualGotoRequiredMovement(resolvedSteps, ref previous, step, target, collision);
                continue;
            }

            foreach (var waypoint in route.Waypoints)
            {
                AddGotoMovement(
                    resolvedSteps,
                    ref previous,
                    step,
                    waypoint,
                    route.Strategy,
                    collision);
            }

            var finalStep = CloneStep(step, resolvedSteps.Count + 1);
            finalStep.CollisionValidated = true;
            finalStep.MovementKind = $"{step.MovementKind}|Resolved";
            finalStep.GotoTarget = step.GotoTarget;
            finalStep.TravelDistanceMm = previous is null
                ? 0
                : Distance(previous.Value, target);
            finalStep.EstimatedTimeSeconds = (finalStep.TravelDistanceMm ?? 0) / DefaultMachineSpeedMmPerSecond;
            resolvedSteps.Add(finalStep);
            previous = target;
        }

        var totalLength = resolvedSteps.Sum(item => item.TravelDistanceMm ?? 0);
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
            SafetyEnvelope = task.SafetyEnvelope,
            PathOptimizationStrategy = task.PathOptimizationStrategy,
            TotalPathLengthMm = totalLength,
            EstimatedTotalTimeSeconds = totalLength / DefaultMachineSpeedMmPerSecond
        };
    }

    private static void AddGotoMovement(
        ICollection<MeasurementStep> resolvedSteps,
        ref (double X, double Y, double Z)? previous,
        MeasurementStep template,
        (double X, double Y, double Z) waypoint,
        string strategy,
        CollisionResult collision)
    {
        var gotoStep = CloneStep(template, resolvedSteps.Count + 1);
        gotoStep.StepType = MeasurementStepType.Movement;
        gotoStep.Name = strategy switch
        {
            "SingleGoto" => "Auto safe GOTO (single)",
            "SafePlaneDetour" => "Auto safe GOTO (detour)",
            _ => $"GOTO avoid ({strategy})"
        };
        gotoStep.MovementKind = strategy switch
        {
            "SingleGoto" => "SafePlaneLift",
            "SafePlaneDetour" => "SafePlaneTraverse",
            _ => "GotoAvoidance"
        };
        gotoStep.GotoTarget = new GotoPoint
        {
            Id = $"goto_avoid_{resolvedSteps.Count + 1}",
            X = waypoint.X,
            Y = waypoint.Y,
            Z = waypoint.Z,
            Reason = BuildCollisionReason(collision)
        };
        gotoStep.CollisionValidated = true;
        gotoStep.TravelDistanceMm = previous is null ? 0 : Distance(previous.Value, waypoint);
        gotoStep.EstimatedTimeSeconds = (gotoStep.TravelDistanceMm ?? 0) / DefaultMachineSpeedMmPerSecond;
        resolvedSteps.Add(gotoStep);
        previous = waypoint;
    }

    private static void AddMovement(
        ICollection<MeasurementStep> resolvedSteps,
        ref (double X, double Y, double Z)? previous,
        MeasurementStep step,
        (double X, double Y, double Z) target)
    {
        var cloned = CloneStep(step, resolvedSteps.Count + 1);
        cloned.TravelDistanceMm = previous is null ? 0 : Distance(previous.Value, target);
        cloned.EstimatedTimeSeconds = (cloned.TravelDistanceMm ?? 0) / DefaultMachineSpeedMmPerSecond;
        resolvedSteps.Add(cloned);
        previous = target;
    }

    private static void AddCollisionRiskMovement(
        ICollection<MeasurementStep> resolvedSteps,
        ref (double X, double Y, double Z)? previous,
        MeasurementStep step,
        (double X, double Y, double Z) target,
        CollisionResult collision)
    {
        var cloned = CloneStep(step, resolvedSteps.Count + 1);
        cloned.IsCollisionRisk = true;
        cloned.CollisionReason = BuildCollisionReason(collision);
        cloned.TravelDistanceMm = previous is null ? 0 : Distance(previous.Value, target);
        cloned.EstimatedTimeSeconds = (cloned.TravelDistanceMm ?? 0) / DefaultMachineSpeedMmPerSecond;
        resolvedSteps.Add(cloned);
        previous = target;
    }

    private static void AddManualGotoRequiredMovement(
        ICollection<MeasurementStep> resolvedSteps,
        ref (double X, double Y, double Z)? previous,
        MeasurementStep step,
        (double X, double Y, double Z) target,
        CollisionResult collision)
    {
        var cloned = CloneStep(step, resolvedSteps.Count + 1);
        cloned.RequiresManualGoto = true;
        cloned.IsCollisionRisk = true;
        cloned.IsExecutable = false;
        cloned.CollisionReason = BuildCollisionReason(collision);
        cloned.TravelDistanceMm = previous is null ? 0 : Distance(previous.Value, target);
        cloned.EstimatedTimeSeconds = (cloned.TravelDistanceMm ?? 0) / DefaultMachineSpeedMmPerSecond;
        resolvedSteps.Add(cloned);
        previous = target;
    }

    private static string BuildCollisionReason(CollisionResult collision)
    {
        var ids = collision.Collisions
            .SelectMany(item => item.InvolvedElementIds)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3);
        var joined = string.Join(", ", ids);
        return string.IsNullOrWhiteSpace(joined)
            ? "Collision detected during transit"
            : $"Collision detected with {joined}";
    }

    private static MeasurementStep CloneStep(MeasurementStep source, int sequenceNumber) => new()
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

    private static (double X, double Y, double Z)? ResolveStepEnd(
        (double X, double Y, double Z)? previous,
        MeasurementStep step)
    {
        if (step.GotoTarget is not null)
            return ToPoint(step.GotoTarget);

        if (step.MeasurementPoints is { Count: > 0 } points)
            return (points[0].X, points[0].Y, points[0].Z);

        return previous;
    }

    private static (double X, double Y, double Z) ToPoint(GotoPoint point) =>
        (point.X, point.Y, point.Z);

    private static double Distance(
        (double X, double Y, double Z) left,
        (double X, double Y, double Z) right)
    {
        var dx = left.X - right.X;
        var dy = left.Y - right.Y;
        var dz = left.Z - right.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}
