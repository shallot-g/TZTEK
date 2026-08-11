namespace TZTEK.VispecCMM.Import.Core.Planning.Path;

using TZTEK.VispecCMM.Import.Core.Planning.SafePlane;

internal sealed class DefaultSafePathPlanner : ISafePathPlanner
{
    private readonly IPathCollisionResolver _collisionResolver;

    public DefaultSafePathPlanner(IPathCollisionResolver collisionResolver)
    {
        _collisionResolver = collisionResolver;
    }

    public MeasurementTask ApplySafetyPath(MeasurementTask task, MeasurementPlanOptions options)
    {
        if (!options.EnableCollisionAvoidance)
            return task;

        EnsureSafetyEnvelope(task, options);

        var measurementSteps = task.Steps
            .Where(step => step.StepType == MeasurementStepType.Measurement && step.MeasurementPoints is { Count: > 0 })
            .ToList();

        if (measurementSteps.Count == 0 || task.SafetyEnvelope is not { } envelope)
            return _collisionResolver.Resolve(task, options);

        var items = measurementSteps
            .Select(step => step.TargetItem)
            .OfType<PrimitiveToleranceItem>()
            .ToList();

        AssignMeasurementPointSafetyPlanes(task, envelope, options);

        var assignmentContext = new SafetyPlaneAssignmentContext
        {
            Envelope = envelope,
            CollisionPrimitives = task.CollisionPrimitives,
            Options = options
        };
        var projections = FeatureSafetyProjectionBuilder.Build(
            envelope,
            items,
            measurementSteps,
            assignmentContext);
        var tour = SafetyPlaneTourOptimizer.Optimize(envelope, projections, options);
        var orderedSteps = tour.OrderedIndices
            .Where(index => index >= 0 && index < measurementSteps.Count)
            .Select(index => measurementSteps[index])
            .ToList();
        var orderedProjections = tour.OrderedIndices
            .Where(index => index >= 0 && index < projections.Count)
            .Select(index => projections[index])
            .ToList();

        var expandedSteps = new List<MeasurementStep>();
        var current = tour.StartPoint;
        var isFirstFeature = true;

        for (var featureIndex = 0; featureIndex < orderedSteps.Count; featureIndex++)
        {
            var sourceStep = orderedSteps[featureIndex];
            var points = sourceStep.MeasurementPoints!.ToList();
            points = isFirstFeature
                ? DefaultPathOptimizer.OptimizeFirstFeaturePoints(points, options).ToList()
                : DefaultPathOptimizer.OptimizePointOrderInternal(points, PathGeometryHelper.ToTuple(current.ToVec3()), options).ToList();

            if (points.Count == 0)
                continue;

            if (isFirstFeature)
            {
                var firstSafe = SafetyPlaneSurfaceRouter.ToSurfacePoint(points[0]);
                AddSurfaceTransit(expandedSteps, sourceStep, envelope, current, firstSafe, "Move to first feature on safety plane");
                current = firstSafe;
            }

            var firstApproach = PathGeometryHelper.GetApproachPoint(points[0]);
            AddDirectMove(expandedSteps, sourceStep, envelope, current.ToVec3(), firstApproach, "Descend to approach point");

            for (var pointIndex = 0; pointIndex < points.Count; pointIndex++)
            {
                var point = points[pointIndex];
                var approach = PathGeometryHelper.GetApproachPoint(point);

                if (pointIndex > 0)
                {
                    var previousApproach = PathGeometryHelper.GetApproachPoint(points[pointIndex - 1]);
                    AddDirectMove(
                        expandedSteps,
                        sourceStep,
                        envelope,
                        previousApproach,
                        approach,
                        "Move to next approach point");
                }

                AddDirectMove(expandedSteps, sourceStep, envelope, approach, PathGeometryHelper.ToVec3((point.X, point.Y, point.Z)), "Approach measurement point");
                expandedSteps.Add(PathStepFactory.CreateMeasurementStep(
                    sourceStep,
                    expandedSteps.Count + 1,
                    point,
                    envelope,
                    $"Measure {sourceStep.TargetItem?.Primitive.Name ?? "feature"} #{point.Index}"));
                AddDirectMove(expandedSteps, sourceStep, envelope, PathGeometryHelper.ToVec3((point.X, point.Y, point.Z)), approach, "Retract to approach point");

                current = SafetyPlaneSurfaceRouter.ToSurfacePoint(point);
            }

            if (featureIndex < orderedSteps.Count - 1)
            {
                var lastPoint = points[^1];
                var lastSafe = SafetyPlaneSurfaceRouter.ToSurfacePoint(lastPoint);
                var lastApproach = PathGeometryHelper.GetApproachPoint(lastPoint);
                ReturnFromApproachToSafetyPlane(
                    expandedSteps,
                    sourceStep,
                    envelope,
                    lastPoint,
                    lastApproach,
                    lastSafe);

                var nextProjection = orderedProjections[featureIndex + 1];
                var nextSafe = SafetyPlaneSurfaceRouter.ToSurfacePoint(envelope, nextProjection);
                current = lastSafe;
                AddSurfaceTransit(
                    expandedSteps,
                    orderedSteps[featureIndex + 1],
                    envelope,
                    current,
                    nextSafe,
                    "Transit to next feature on safety plane");
                current = nextSafe;
            }
            else
            {
                current = SafetyPlaneSurfaceRouter.ToSurfacePoint(points[^1]);
            }

            isFirstFeature = false;
        }

        var totalLength = CalculateTotalLength(expandedSteps);
        var safeTask = new MeasurementTask
        {
            TaskId = task.TaskId,
            Name = task.Name,
            SourceFilePath = task.SourceFilePath,
            CreatedAt = task.CreatedAt,
            ToleranceStandard = task.ToleranceStandard,
            LengthUnit = task.LengthUnit,
            Steps = expandedSteps,
            CollisionPrimitives = task.CollisionPrimitives,
            ProbeConfigurations = task.ProbeConfigurations,
            GlobalSafetyPlane = task.GlobalSafetyPlane,
            SafetyEnvelope = task.SafetyEnvelope,
            PathOptimizationStrategy = task.PathOptimizationStrategy,
            TotalPathLengthMm = totalLength,
            EstimatedTotalTimeSeconds = PathGeometryHelper.EstimateTimeSeconds(totalLength)
        };

        return _collisionResolver.Resolve(safeTask, options);
    }

    private static void EnsureSafetyEnvelope(MeasurementTask task, MeasurementPlanOptions options)
    {
        if (task.SafetyEnvelope is not null)
            return;

        SafetyPlaneBoxBuilder.ApplyToTask(task, options);
    }

    private static void AssignMeasurementPointSafetyPlanes(
        MeasurementTask task,
        SafetyPlaneBox envelope,
        MeasurementPlanOptions options)
    {
        SafetyPlaneAssigner.AssignTaskPoints(task, options);
    }

    private static void ReturnFromApproachToSafetyPlane(
        ICollection<MeasurementStep> steps,
        MeasurementStep sourceStep,
        SafetyPlaneBox envelope,
        MeasurementPoint point,
        PlanningVectors.Vec3 approach,
        SurfacePoint safe)
    {
        var face = point.AssignedSafetyPlaneFace ?? safe.Face;
        var onFaceCoords = envelope.ProjectPoint(face, PathGeometryHelper.ToTuple(approach));
        var onFace = new SurfacePoint(face, onFaceCoords.X, onFaceCoords.Y, onFaceCoords.Z);

        AddDirectMove(
            steps,
            sourceStep,
            envelope,
            approach,
            onFace.ToVec3(),
            "Return to safety plane");

        if (onFace.Face != safe.Face
            || Math.Abs(onFace.X - safe.X) > 1e-6
            || Math.Abs(onFace.Y - safe.Y) > 1e-6
            || Math.Abs(onFace.Z - safe.Z) > 1e-6)
        {
            AddSurfaceTransit(steps, sourceStep, envelope, onFace, safe, "Return to safety plane");
        }
    }

    private static void AddSurfaceTransit(
        ICollection<MeasurementStep> steps,
        MeasurementStep sourceStep,
        SafetyPlaneBox envelope,
        SurfacePoint from,
        SurfacePoint to,
        string name)
    {
        var waypoints = SafetyPlaneSurfaceRouter.BuildSurfaceWaypoints(envelope, from, to);
        var current = from.ToVec3();
        for (var i = 0; i < waypoints.Count; i++)
        {
            var waypoint = waypoints[i];
            var segmentName = i == waypoints.Count - 1 ? name : "SafePlaneSurfaceMove";
            AddDirectMove(
                steps,
                sourceStep,
                envelope,
                current,
                waypoint.ToVec3(),
                segmentName);
            current = waypoint.ToVec3();
        }

        if (waypoints.Count == 0)
            AddDirectMove(steps, sourceStep, envelope, current, to.ToVec3(), name);
    }

    private static void AddDirectMove(
        ICollection<MeasurementStep> steps,
        MeasurementStep sourceStep,
        SafetyPlaneBox envelope,
        PlanningVectors.Vec3 from,
        PlanningVectors.Vec3 to,
        string movementKind)
    {
        var distance = PlanningVectors.Distance(from, to);
        if (distance < 1e-6)
            return;

        var step = PathStepFactory.CreateMovementStep(
            sourceStep,
            steps.Count + 1,
            to,
            ResolvePlaneForPoint(envelope, to),
            movementKind,
            movementKind);
        step.TravelDistanceMm = distance;
        step.EstimatedTimeSeconds = PathGeometryHelper.EstimateTimeSeconds(distance);
        steps.Add(step);
    }

    private static ISafetyPlane? ResolvePlaneForPoint(SafetyPlaneBox envelope, PlanningVectors.Vec3 point)
    {
        if (Math.Abs(point.Z - envelope.MaxZ) < 1e-3)
            return envelope.GetPlane(SafetyPlaneFace.Top);
        if (Math.Abs(point.Z - envelope.MinZ) < 1e-3)
            return envelope.GetPlane(SafetyPlaneFace.Bottom);
        if (Math.Abs(point.X - envelope.MaxX) < 1e-3)
            return envelope.GetPlane(SafetyPlaneFace.PosX);
        if (Math.Abs(point.X - envelope.MinX) < 1e-3)
            return envelope.GetPlane(SafetyPlaneFace.NegX);
        if (Math.Abs(point.Y - envelope.MaxY) < 1e-3)
            return envelope.GetPlane(SafetyPlaneFace.PosY);
        if (Math.Abs(point.Y - envelope.MinY) < 1e-3)
            return envelope.GetPlane(SafetyPlaneFace.NegY);

        return envelope.GetPlane(SafetyPlaneFace.Top);
    }

    private static double CalculateTotalLength(IReadOnlyList<MeasurementStep> steps) =>
        steps.Sum(step => step.TravelDistanceMm ?? 0);
}
