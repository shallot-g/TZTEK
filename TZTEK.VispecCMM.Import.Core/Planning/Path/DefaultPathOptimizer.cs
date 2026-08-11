namespace TZTEK.VispecCMM.Import.Core.Planning.Path;

using TZTEK.VispecCMM.Import.Core.Planning.SafePlane;

internal sealed class DefaultPathOptimizer : IPathOptimizer
{
    public IReadOnlyList<PrimitiveToleranceItem> OptimizeFeatureOrder(
        IReadOnlyList<PrimitiveToleranceItem> items,
        MeasurementPlanOptions options) =>
        OptimizeFeatureOrderByPointProximity(items, BuildPointsByFeatureId(items, options), options);

    public IReadOnlyList<PrimitiveToleranceItem> OptimizeFeatureOrderByPointProximity(
        IReadOnlyList<PrimitiveToleranceItem> items,
        IReadOnlyDictionary<string, IReadOnlyList<MeasurementPoint>> pointsByFeatureId,
        MeasurementPlanOptions options)
    {
        if (items.Count <= 1 || options.CollisionPrimitives.Count == 0)
            return items;

        var envelope = SafetyPlaneBoxBuilder.Build(options.CollisionPrimitives, options);
        var measurementSteps = items.Select(item => new MeasurementStep
        {
            TargetItem = item,
            MeasurementPoints = pointsByFeatureId.TryGetValue(item.Primitive.Id, out var featurePoints)
                ? featurePoints
                : []
        }).ToList();
        var context = new SafetyPlaneAssignmentContext
        {
            Envelope = envelope,
            CollisionPrimitives = options.CollisionPrimitives,
            Options = options
        };
        var projections = FeatureSafetyProjectionBuilder.Build(envelope, items, measurementSteps, context);
        var tour = SafetyPlaneTourOptimizer.Optimize(envelope, projections, options);
        _ = pointsByFeatureId;

        return tour.OrderedIndices.Select(index => items[index]).ToList();
    }

    public IReadOnlyList<MeasurementPoint> OptimizePointOrder(
        IReadOnlyList<MeasurementPoint> points,
        (double X, double Y, double Z)? referencePoint,
        MeasurementPlanOptions options) =>
        OptimizePointOrderInternal(points, referencePoint, options);

    internal static IReadOnlyList<MeasurementPoint> OptimizePointOrderInternal(
        IReadOnlyList<MeasurementPoint> points,
        (double X, double Y, double Z)? referencePoint,
        MeasurementPlanOptions options)
    {
        if (points.Count <= 1)
            return PathGeometryHelper.CloneAndReindex(points);

        if (options.PathStrategy == PathOptimizationStrategy.ImportOrder)
            return PathGeometryHelper.CloneAndReindex(points);

        var startIndex = referencePoint is null
            ? Random.Shared.Next(points.Count)
            : SelectNearestIndex(points, referencePoint.Value);

        return TwoOptPointOptimizer.OptimizeOpenTour(points, startIndex, options.PathTimeout);
    }

    internal static IReadOnlyList<MeasurementPoint> OptimizeFirstFeaturePoints(
        IReadOnlyList<MeasurementPoint> points,
        MeasurementPlanOptions options)
    {
        if (points.Count <= 1)
            return PathGeometryHelper.CloneAndReindex(points);

        var randomStart = Random.Shared.Next(points.Count);
        return TwoOptPointOptimizer.OptimizeOpenTour(points, randomStart, options.PathTimeout);
    }

    private static Dictionary<string, IReadOnlyList<MeasurementPoint>> BuildPointsByFeatureId(
        IReadOnlyList<PrimitiveToleranceItem> items,
        MeasurementPlanOptions options)
    {
        return items.ToDictionary(
            item => item.Primitive.Id,
            item =>
            {
                var representative = item.Primitive.GetRepresentativePoint();
                var direction = item.Primitive.GetDirection();
                return (IReadOnlyList<MeasurementPoint>)
                [
                    new MeasurementPoint
                    {
                        Index = 1,
                        X = representative.X,
                        Y = representative.Y,
                        Z = representative.Z,
                        NormalX = direction.I,
                        NormalY = direction.J,
                        NormalZ = direction.K,
                        ApproachDistance = options.DefaultApproachDistanceMm
                    }
                ];
            },
            StringComparer.OrdinalIgnoreCase);
    }

    private static int SelectNearestIndex(
        IReadOnlyList<MeasurementPoint> points,
        (double X, double Y, double Z) referencePoint)
    {
        var bestIndex = 0;
        var bestDistance = double.PositiveInfinity;
        for (var i = 0; i < points.Count; i++)
        {
            var distance = PlanningVectors.Distance(
                PathGeometryHelper.ToVec3(referencePoint),
                PathGeometryHelper.ToVec3((points[i].X, points[i].Y, points[i].Z)));
            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            bestIndex = i;
        }

        return bestIndex;
    }
}
