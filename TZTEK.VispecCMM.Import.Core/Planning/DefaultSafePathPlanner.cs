namespace TZTEK.VispecCMM.Import.Core.Planning;

internal sealed class DefaultSafePathPlanner : ISafePathPlanner
{
    private const double DefaultMachineSpeedMmPerSecond = 20.0;

    private readonly IPathCollisionResolver _collisionResolver;

    public DefaultSafePathPlanner(IPathCollisionResolver collisionResolver)
    {
        _collisionResolver = collisionResolver;
    }

    public MeasurementTask ApplySafetyPath(MeasurementTask task, MeasurementPlanOptions options)
    {
        var safeZ = ResolveSafetyZ(task, options);
        var expandedSteps = new List<MeasurementStep>();
        (double X, double Y, double Z)? previous = options.StartPoint;

        var measurementSteps = task.Steps
            .Where(step => step.StepType == MeasurementStepType.Measurement && step.MeasurementPoints is { Count: > 0 })
            .ToList();

        for (var featureIndex = 0; featureIndex < measurementSteps.Count; featureIndex++)
        {
            var step = measurementSteps[featureIndex];
            var isFirstFeature = featureIndex == 0;
            var points = step.MeasurementPoints!;

            if (options.EnableContinuousCylinderPath
                && step.TargetItem?.Primitive is CylinderPrimitive)
            {
                AddContinuousFeaturePath(
                    expandedSteps,
                    ref previous,
                    step,
                    points,
                    safeZ,
                    options,
                    task.GlobalSafetyPlane,
                    invertApproach: false,
                    "cylinder",
                    isFirstFeature);
                continue;
            }

            if (options.EnableContinuousFeaturePath && CanUseContinuousFeaturePath(step))
            {
                AddContinuousFeaturePath(
                    expandedSteps,
                    ref previous,
                    step,
                    points,
                    safeZ,
                    options,
                    task.GlobalSafetyPlane,
                    invertApproach: false,
                    ResolveFeaturePathName(step),
                    isFirstFeature);
                continue;
            }

            if (options.EnableSinglePointSafetyPath)
            {
                AddSinglePointSafetyPath(
                    expandedSteps,
                    ref previous,
                    step,
                    points,
                    safeZ,
                    options,
                    task.GlobalSafetyPlane,
                    isFirstFeature);
                continue;
            }

            expandedSteps.Add(CloneStep(step, expandedSteps.Count + 1));
        }

        if (measurementSteps.Count > 0 && previous is not null)
        {
            var lastStep = measurementSteps[^1];
            var finalSafeAbove = (previous.Value.X, previous.Value.Y, safeZ);
            AddMovement(
                expandedSteps,
                ref previous,
                lastStep,
                finalSafeAbove,
                "Return to global safety plane",
                task.GlobalSafetyPlane);
        }

        var totalLength = expandedSteps.Sum(step => step.TravelDistanceMm ?? 0);
        var safeTask = new MeasurementTask
        {
            TaskId = task.TaskId,
            Name = task.Name,
            SourceFilePath = task.SourceFilePath,
            CreatedAt = task.CreatedAt,
            ToleranceStandard = task.ToleranceStandard,
            LengthUnit = task.LengthUnit,
            Steps = expandedSteps,
            ProbeConfigurations = task.ProbeConfigurations,
            GlobalSafetyPlane = task.GlobalSafetyPlane,
            PathOptimizationStrategy = task.PathOptimizationStrategy,
            TotalPathLengthMm = totalLength,
            EstimatedTotalTimeSeconds = totalLength / DefaultMachineSpeedMmPerSecond
        };

        return options.EnableCollisionCheck
            ? _collisionResolver.Resolve(safeTask, options)
            : safeTask;
    }

    private static void AddSinglePointSafetyPath(
        List<MeasurementStep> steps,
        ref (double X, double Y, double Z)? previous,
        MeasurementStep sourceStep,
        IReadOnlyList<MeasurementPoint> points,
        double safeZ,
        MeasurementPlanOptions options,
        ISafetyPlane? safetyPlane,
        bool isFirstFeature)
    {
        for (var index = 0; index < points.Count; index++)
        {
            var point = points[index];
            var normal = ResolveApproachNormal(point, invert: false);
            var approachDistance = PositiveOrDefault(point.ApproachDistance, options.DefaultApproachDistanceMm);
            var retractDistance = PositiveOrDefault(point.RetractDistance, options.DefaultRetractDistanceMm);

            var measure = (point.X, point.Y, point.Z);
            var safeAbove = (point.X, point.Y, safeZ);
            var approach = OffsetAlongNormal(measure, normal, approachDistance);
            var retract = OffsetAlongNormal(measure, normal, retractDistance);

            if (isFirstFeature && index == 0)
                AddMovement(steps, ref previous, sourceStep, safeAbove, "Move to safety plane", safetyPlane);

            var approachName = !isFirstFeature && index == 0
                ? $"Move to {ResolveFeaturePathName(sourceStep)} entry approach point"
                : "Move to approach point";
            AddMovement(steps, ref previous, sourceStep, approach, approachName, safetyPlane);
            AddMeasurement(steps, ref previous, sourceStep, point);
            AddMovement(steps, ref previous, sourceStep, retract, "Retract from measurement point", safetyPlane);
        }
    }

    private static void AddContinuousFeaturePath(
        List<MeasurementStep> steps,
        ref (double X, double Y, double Z)? previous,
        MeasurementStep sourceStep,
        IReadOnlyList<MeasurementPoint> points,
        double safeZ,
        MeasurementPlanOptions options,
        ISafetyPlane? safetyPlane,
        bool invertApproach,
        string featureName,
        bool isFirstFeature)
    {
        var firstPoint = points[0];
        var firstMeasure = (firstPoint.X, firstPoint.Y, firstPoint.Z);
        var firstNormal = ResolveApproachNormal(firstPoint, invertApproach);
        var firstApproachDistance = PositiveOrDefault(firstPoint.ApproachDistance, options.DefaultApproachDistanceMm);
        var firstSafeAbove = (firstPoint.X, firstPoint.Y, safeZ);
        var firstApproach = OffsetAlongNormal(firstMeasure, firstNormal, firstApproachDistance);

        if (isFirstFeature)
            AddMovement(steps, ref previous, sourceStep, firstSafeAbove, $"Move to {featureName} safety plane", safetyPlane);

        var entryName = isFirstFeature
            ? $"Enter {featureName} measurement path"
            : $"Move to {featureName} entry approach point";
        AddMovement(steps, ref previous, sourceStep, firstApproach, entryName, safetyPlane);

        foreach (var point in points)
        {
            var normal = ResolveApproachNormal(point, invertApproach);
            var approachDistance = PositiveOrDefault(point.ApproachDistance, options.DefaultApproachDistanceMm);
            var measure = (point.X, point.Y, point.Z);
            var approach = OffsetAlongNormal(measure, normal, approachDistance);

            AddMovement(steps, ref previous, sourceStep, approach, $"Move to next {featureName} approach point", safetyPlane);
            AddMeasurement(steps, ref previous, sourceStep, point);
        }

        var lastPoint = points[^1];
        var lastNormal = ResolveApproachNormal(lastPoint, invertApproach);
        var retractDistance = PositiveOrDefault(lastPoint.RetractDistance, options.DefaultRetractDistanceMm);
        var lastMeasure = (lastPoint.X, lastPoint.Y, lastPoint.Z);
        var retract = OffsetAlongNormal(lastMeasure, lastNormal, retractDistance);

        AddMovement(steps, ref previous, sourceStep, retract, $"Exit {featureName} measurement path", safetyPlane);
    }

    private static void AddMeasurement(
        List<MeasurementStep> steps,
        ref (double X, double Y, double Z)? previous,
        MeasurementStep sourceStep,
        MeasurementPoint point)
    {
        var measure = (point.X, point.Y, point.Z);
        var measurementStep = CloneStep(sourceStep, steps.Count + 1);
        measurementStep.MeasurementPoints = [point];
        measurementStep.TravelDistanceMm = previous is null ? 0 : Distance(previous.Value, measure);
        measurementStep.EstimatedTimeSeconds = measurementStep.TravelDistanceMm.Value / DefaultMachineSpeedMmPerSecond;
        steps.Add(measurementStep);
        previous = measure;
    }

    private static void AddMovement(
        List<MeasurementStep> steps,
        ref (double X, double Y, double Z)? previous,
        MeasurementStep sourceStep,
        (double X, double Y, double Z) target,
        string reason,
        ISafetyPlane? safetyPlane)
    {
        var distance = previous is null ? 0 : Distance(previous.Value, target);
        steps.Add(new MeasurementStep
        {
            SequenceNumber = steps.Count + 1,
            StepType = MeasurementStepType.Movement,
            Name = reason,
            TargetItem = sourceStep.TargetItem,
            ProbeAssignment = sourceStep.ProbeAssignment,
            GotoTarget = new GotoPoint
            {
                Id = $"goto_{steps.Count + 1}",
                X = target.X,
                Y = target.Y,
                Z = target.Z,
                Reason = reason
            },
            SafetyPlane = safetyPlane,
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

    private static double ResolveSafetyZ(MeasurementTask task, MeasurementPlanOptions options)
    {
        if (task.GlobalSafetyPlane is not null)
            return task.GlobalSafetyPlane.GetPosition().Z;

        var maxZ = task.Steps
            .SelectMany(step => step.MeasurementPoints ?? [])
            .Select(point => point.Z)
            .DefaultIfEmpty(0)
            .Max();
        return maxZ + options.SafetyClearanceMm;
    }

    private static (double X, double Y, double Z) OffsetAlongNormal(
        (double X, double Y, double Z) point,
        (double X, double Y, double Z) normal,
        double distance)
    {
        return (
            point.X + normal.X * distance,
            point.Y + normal.Y * distance,
            point.Z + normal.Z * distance);
    }

    private static (double X, double Y, double Z) ResolveApproachNormal(MeasurementPoint point, bool invert)
    {
        var normal = Normalize(point.NormalX, point.NormalY, point.NormalZ);
        return invert ? (-normal.X, -normal.Y, -normal.Z) : normal;
    }

    private static bool CanUseContinuousFeaturePath(MeasurementStep step)
    {
        return step.TargetItem?.Primitive.PrimitiveType is
            PrimitiveType.Line or
            PrimitiveType.Circle or
            PrimitiveType.Arc or
            PrimitiveType.Plane or
            PrimitiveType.Cone or
            PrimitiveType.Sphere;
    }

    private static string ResolveFeaturePathName(MeasurementStep step)
    {
        return step.TargetItem?.Primitive.PrimitiveType switch
        {
            PrimitiveType.Line => "line",
            PrimitiveType.Circle => "circle",
            PrimitiveType.Arc => "arc",
            PrimitiveType.Plane => "plane",
            PrimitiveType.Cone => "cone",
            PrimitiveType.Sphere => "sphere",
            _ => "feature"
        };
    }

    private static (double X, double Y, double Z) Normalize(double x, double y, double z)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z))
            return (0, 0, 1);

        var length = Math.Sqrt(x * x + y * y + z * z);
        return length <= double.Epsilon
            ? (0, 0, 1)
            : (x / length, y / length, z / length);
    }

    private static double PositiveOrDefault(double value, double defaultValue)
    {
        return double.IsFinite(value) && value > 0 ? value : defaultValue;
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

    private static double Dot((double X, double Y, double Z) left, (double X, double Y, double Z) right) =>
        left.X * right.X + left.Y * right.Y + left.Z * right.Z;

    private static double Length((double X, double Y, double Z) value) =>
        Math.Sqrt(value.X * value.X + value.Y * value.Y + value.Z * value.Z);

    private static (double X, double Y, double Z) Cross(
        (double X, double Y, double Z) left,
        (double X, double Y, double Z) right) =>
        (
            left.Y * right.Z - left.Z * right.Y,
            left.Z * right.X - left.X * right.Z,
            left.X * right.Y - left.Y * right.X);
}
