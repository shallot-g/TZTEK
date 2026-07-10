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

            var target = (step.GotoTarget.X, step.GotoTarget.Y, step.GotoTarget.Z);
            if (previous is null || ShouldSkipCollisionCheck(step))
            {
                AddMovement(resolvedSteps, ref previous, step, target);
                continue;
            }

            segmentIndex++;
            var collision = _collisionChecker.Check(task, previous.Value, target, step, options, segmentIndex);
            if (!collision.HasCollision)
            {
                AddMovement(resolvedSteps, ref previous, step, target);
                continue;
            }

            AddAvoidancePath(resolvedSteps, ref previous, step, target, task, options);
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

    private static bool ShouldSkipCollisionCheck(MeasurementStep step)
    {
        return step.Name.Contains("measurement path", StringComparison.OrdinalIgnoreCase)
            || step.Name.Contains("approach point", StringComparison.OrdinalIgnoreCase)
            || step.Name.Contains("Retract", StringComparison.OrdinalIgnoreCase);
    }

    private static void AddAvoidancePath(
        List<MeasurementStep> steps,
        ref (double X, double Y, double Z)? previous,
        MeasurementStep sourceStep,
        (double X, double Y, double Z) target,
        MeasurementTask task,
        MeasurementPlanOptions options)
    {
        var start = previous!.Value;
        var normal = ResolveSafetyNormal(sourceStep.SafetyPlane ?? task.GlobalSafetyPlane);
        var clearance = Math.Max(0, options.CollisionLiftClearanceMm);
        var startLift = Add(start, Scale(normal, clearance));
        var targetLift = Add(target, Scale(normal, clearance));

        AddGeneratedMovement(steps, ref previous, sourceStep, startLift, "Collision avoidance lift");
        AddGeneratedMovement(steps, ref previous, sourceStep, targetLift, "Collision avoidance traverse");
        AddGeneratedMovement(steps, ref previous, sourceStep, target, "Collision avoidance return");
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
        string reason)
    {
        var distance = previous is null ? 0 : Distance(previous.Value, target);
        steps.Add(new MeasurementStep
        {
            SequenceNumber = steps.Count + 1,
            StepType = MeasurementStepType.Movement,
            Name = reason,
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
            return (step.GotoTarget.X, step.GotoTarget.Y, step.GotoTarget.Z);

        var point = step.MeasurementPoints?.LastOrDefault();
        return point is null ? previous : (point.X, point.Y, point.Z);
    }

    private static (double X, double Y, double Z) ResolveSafetyNormal(ISafetyPlane? safetyPlane)
    {
        if (safetyPlane is null)
            return (0, 0, 1);

        var direction = safetyPlane.GetDirection();
        return Normalize((direction.I, direction.J, direction.K));
    }

    private static (double X, double Y, double Z) Normalize((double X, double Y, double Z) value)
    {
        var length = Math.Sqrt(value.X * value.X + value.Y * value.Y + value.Z * value.Z);
        return length < 1e-12 ? (0, 0, 1) : (value.X / length, value.Y / length, value.Z / length);
    }

    private static (double X, double Y, double Z) Add(
        (double X, double Y, double Z) left,
        (double X, double Y, double Z) right) =>
        (left.X + right.X, left.Y + right.Y, left.Z + right.Z);

    private static (double X, double Y, double Z) Scale((double X, double Y, double Z) value, double scale) =>
        (value.X * scale, value.Y * scale, value.Z * scale);

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
