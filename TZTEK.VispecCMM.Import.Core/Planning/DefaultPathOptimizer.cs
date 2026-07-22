namespace TZTEK.VispecCMM.Import.Core.Planning;

/// <summary>
/// 基于最近邻初解与 2-opt 局部搜索的特征/测点访问顺序优化。
/// </summary>
internal sealed class DefaultPathOptimizer : IPathOptimizer
{
    public IReadOnlyList<PrimitiveToleranceItem> OptimizeFeatureOrder(
        IReadOnlyList<PrimitiveToleranceItem> items,
        MeasurementPlanOptions options)
    {
        if (items.Count <= 1)
            return items;

        return options.PathStrategy switch
        {
            PathOptimizationStrategy.ImportOrder => items,
            PathOptimizationStrategy.NearestNeighbor => Reorder(items, BuildNearestNeighborTour(items, options)),
            PathOptimizationStrategy.TwoOpt => Reorder(items, BuildTwoOptTour(items, options)),
            _ => items
        };
    }

    public IReadOnlyList<PrimitiveToleranceItem> OptimizeFeatureOrderByPointProximity(
        IReadOnlyList<PrimitiveToleranceItem> items,
        IReadOnlyDictionary<string, IReadOnlyList<MeasurementPoint>> pointsByFeatureId,
        MeasurementPlanOptions options)
    {
        if (items.Count <= 1)
            return items;

        return options.PathStrategy switch
        {
            PathOptimizationStrategy.ImportOrder => items,
            PathOptimizationStrategy.NearestNeighbor => Reorder(
                items,
                BuildNearestNeighborFeatureTourByPoints(items, pointsByFeatureId, options)),
            PathOptimizationStrategy.TwoOpt => Reorder(
                items,
                BuildTwoOptFeatureTourByPoints(items, pointsByFeatureId, options)),
            _ => items
        };
    }

    public IReadOnlyList<MeasurementPoint> OptimizePointOrder(
        IReadOnlyList<MeasurementPoint> points,
        (double X, double Y, double Z)? referencePoint,
        MeasurementPlanOptions options)
    {
        if (points.Count <= 1)
            return ReindexPoints(points);

        if (options.PathStrategy == PathOptimizationStrategy.ImportOrder)
            return ReorderPointsFromNearest(points, referencePoint);

        var positions = points.Select(ToPoint).ToList();
        var tour = options.PathStrategy == PathOptimizationStrategy.TwoOpt
            ? BuildTwoOptOpenTour(positions, referencePoint, options.PathTimeout)
            : BuildNearestNeighborOpenTour(positions, referencePoint);

        return ReorderPoints(points, tour);
    }

    private static IReadOnlyList<PrimitiveToleranceItem> Reorder(
        IReadOnlyList<PrimitiveToleranceItem> items,
        IReadOnlyList<int> tour)
    {
        return tour.Select(index => items[index]).ToList();
    }

    private static IReadOnlyList<int> BuildTwoOptTour(
        IReadOnlyList<PrimitiveToleranceItem> items,
        MeasurementPlanOptions options)
    {
        var tour = BuildNearestNeighborTour(items, options).ToList();
        if (tour.Count <= 2)
            return tour;

        var points = items.Select(item => ToPoint(item.Primitive.GetRepresentativePoint())).ToList();
        ImproveClosedTour(points, tour, options.StartPoint, options.PathTimeout);
        return tour;
    }

    private static IReadOnlyList<int> BuildNearestNeighborTour(
        IReadOnlyList<PrimitiveToleranceItem> items,
        MeasurementPlanOptions options)
    {
        var points = items.Select(item => ToPoint(item.Primitive.GetRepresentativePoint())).ToList();
        var remaining = Enumerable.Range(0, items.Count).ToList();
        var tour = new List<int>(items.Count);
        var current = options.StartPoint ?? points[remaining[0]];

        while (remaining.Count > 0)
        {
            var nextIndex = remaining
                .OrderBy(index => Distance(current, points[index]))
                .First();
            remaining.Remove(nextIndex);
            tour.Add(nextIndex);
            current = points[nextIndex];
        }

        return tour;
    }

    private static IReadOnlyList<int> BuildTwoOptFeatureTourByPoints(
        IReadOnlyList<PrimitiveToleranceItem> items,
        IReadOnlyDictionary<string, IReadOnlyList<MeasurementPoint>> pointsByFeatureId,
        MeasurementPlanOptions options)
    {
        var tour = BuildNearestNeighborFeatureTourByPoints(items, pointsByFeatureId, options).ToList();
        if (tour.Count <= 2)
            return tour;

        ImproveClosedFeatureTour(items, pointsByFeatureId, tour, options.StartPoint, options.PathTimeout);
        return tour;
    }

    private static IReadOnlyList<int> BuildNearestNeighborFeatureTourByPoints(
        IReadOnlyList<PrimitiveToleranceItem> items,
        IReadOnlyDictionary<string, IReadOnlyList<MeasurementPoint>> pointsByFeatureId,
        MeasurementPlanOptions options)
    {
        var remaining = Enumerable.Range(0, items.Count).ToList();
        var tour = new List<int>(items.Count);
        (double X, double Y, double Z)? current = options.StartPoint;

        while (remaining.Count > 0)
        {
            var nextIndex = remaining
                .OrderBy(index => MinDistanceToFeaturePoints(current, items[index], pointsByFeatureId))
                .First();
            remaining.Remove(nextIndex);
            tour.Add(nextIndex);
            current = SelectNearestPoint(current, items[nextIndex], pointsByFeatureId);
        }

        return tour;
    }

    private static IReadOnlyList<int> BuildNearestNeighborOpenTour(
        IReadOnlyList<(double X, double Y, double Z)> points,
        (double X, double Y, double Z)? referencePoint)
    {
        if (points.Count == 0)
            return [];

        var startIndex = SelectNearestIndex(points, referencePoint);
        var remaining = Enumerable.Range(0, points.Count).Where(index => index != startIndex).ToList();
        var tour = new List<int> { startIndex };
        var current = points[startIndex];

        while (remaining.Count > 0)
        {
            var nextIndex = remaining
                .OrderBy(index => Distance(current, points[index]))
                .First();
            remaining.Remove(nextIndex);
            tour.Add(nextIndex);
            current = points[nextIndex];
        }

        return tour;
    }

    private static IReadOnlyList<int> BuildTwoOptOpenTour(
        IReadOnlyList<(double X, double Y, double Z)> points,
        (double X, double Y, double Z)? referencePoint,
        TimeSpan timeout)
    {
        var tour = BuildNearestNeighborOpenTour(points, referencePoint).ToList();
        if (tour.Count <= 2)
            return tour;

        ImproveOpenTour(points, tour, referencePoint, timeout);
        return tour;
    }

    private static void ImproveClosedTour(
        IReadOnlyList<(double X, double Y, double Z)> points,
        List<int> tour,
        (double X, double Y, double Z)? startPoint,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        var improved = true;

        while (improved && DateTime.UtcNow < deadline)
        {
            improved = false;
            var currentLength = ClosedTourLength(points, tour, startPoint);
            var bestGain = 0.0;
            var bestStart = -1;
            var bestEnd = -1;

            for (var i = 0; i < tour.Count - 2; i++)
            {
                for (var j = i + 2; j < tour.Count; j++)
                {
                    var candidate = tour.ToList();
                    ReverseSegment(candidate, i + 1, j);
                    var candidateLength = ClosedTourLength(points, candidate, startPoint);
                    var gain = currentLength - candidateLength;
                    if (gain <= bestGain + 1e-9)
                        continue;

                    bestGain = gain;
                    bestStart = i + 1;
                    bestEnd = j;
                }
            }

            if (bestStart >= 0)
            {
                ReverseSegment(tour, bestStart, bestEnd);
                improved = true;
            }
        }
    }

    private static void ImproveClosedFeatureTour(
        IReadOnlyList<PrimitiveToleranceItem> items,
        IReadOnlyDictionary<string, IReadOnlyList<MeasurementPoint>> pointsByFeatureId,
        List<int> tour,
        (double X, double Y, double Z)? startPoint,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        var improved = true;

        while (improved && DateTime.UtcNow < deadline)
        {
            improved = false;
            var currentLength = ClosedFeatureTourLength(items, pointsByFeatureId, tour, startPoint);
            var bestGain = 0.0;
            var bestStart = -1;
            var bestEnd = -1;

            for (var i = 0; i < tour.Count - 2; i++)
            {
                for (var j = i + 2; j < tour.Count; j++)
                {
                    var candidate = tour.ToList();
                    ReverseSegment(candidate, i + 1, j);
                    var candidateLength = ClosedFeatureTourLength(items, pointsByFeatureId, candidate, startPoint);
                    var gain = currentLength - candidateLength;
                    if (gain <= bestGain + 1e-9)
                        continue;

                    bestGain = gain;
                    bestStart = i + 1;
                    bestEnd = j;
                }
            }

            if (bestStart >= 0)
            {
                ReverseSegment(tour, bestStart, bestEnd);
                improved = true;
            }
        }
    }

    private static void ImproveOpenTour(
        IReadOnlyList<(double X, double Y, double Z)> points,
        List<int> tour,
        (double X, double Y, double Z)? referencePoint,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        var improved = true;

        while (improved && DateTime.UtcNow < deadline)
        {
            improved = false;
            var currentLength = OpenTourLength(points, tour, referencePoint);
            var bestGain = 0.0;
            var bestStart = -1;
            var bestEnd = -1;

            for (var i = 0; i < tour.Count - 2; i++)
            {
                for (var j = i + 2; j < tour.Count; j++)
                {
                    var candidate = tour.ToList();
                    ReverseSegment(candidate, i + 1, j);
                    var candidateLength = OpenTourLength(points, candidate, referencePoint);
                    var gain = currentLength - candidateLength;
                    if (gain <= bestGain + 1e-9)
                        continue;

                    bestGain = gain;
                    bestStart = i + 1;
                    bestEnd = j;
                }
            }

            if (bestStart >= 0)
            {
                ReverseSegment(tour, bestStart, bestEnd);
                improved = true;
            }
        }
    }

    private static IReadOnlyList<MeasurementPoint> ReorderPointsFromNearest(
        IReadOnlyList<MeasurementPoint> points,
        (double X, double Y, double Z)? referencePoint)
    {
        if (points.Count <= 1 || referencePoint is null)
            return ReindexPoints(points);

        var startIndex = SelectNearestIndex(points.Select(ToPoint).ToList(), referencePoint);
        if (startIndex == 0)
            return ReindexPoints(points);

        var reordered = points.Skip(startIndex).Concat(points.Take(startIndex)).ToList();
        return ReindexPoints(reordered);
    }

    private static IReadOnlyList<MeasurementPoint> ReorderPoints(
        IReadOnlyList<MeasurementPoint> points,
        IReadOnlyList<int> tour)
    {
        return ReindexPoints(tour.Select(index => points[index]).ToList());
    }

    private static IReadOnlyList<MeasurementPoint> ReindexPoints(IReadOnlyList<MeasurementPoint> points)
    {
        return points
            .Select((point, index) => new MeasurementPoint
            {
                Index = index + 1,
                X = point.X,
                Y = point.Y,
                Z = point.Z,
                NormalX = point.NormalX,
                NormalY = point.NormalY,
                NormalZ = point.NormalZ,
                ApproachDistance = point.ApproachDistance,
                RetractDistance = point.RetractDistance,
                SearchDistance = point.SearchDistance
            })
            .ToList();
    }

    private static double ClosedFeatureTourLength(
        IReadOnlyList<PrimitiveToleranceItem> items,
        IReadOnlyDictionary<string, IReadOnlyList<MeasurementPoint>> pointsByFeatureId,
        IReadOnlyList<int> tour,
        (double X, double Y, double Z)? startPoint)
    {
        if (tour.Count == 0)
            return 0;

        var length = MinDistanceToFeaturePoints(startPoint, items[tour[0]], pointsByFeatureId);
        for (var i = 0; i < tour.Count - 1; i++)
        {
            length += FeatureTransitionDistance(
                items[tour[i]],
                items[tour[i + 1]],
                pointsByFeatureId);
        }

        return length;
    }

    private static double FeatureTransitionDistance(
        PrimitiveToleranceItem from,
        PrimitiveToleranceItem to,
        IReadOnlyDictionary<string, IReadOnlyList<MeasurementPoint>> pointsByFeatureId)
    {
        if (!pointsByFeatureId.TryGetValue(from.Primitive.Id, out var fromPoints)
            || !pointsByFeatureId.TryGetValue(to.Primitive.Id, out var toPoints)
            || fromPoints.Count == 0
            || toPoints.Count == 0)
        {
            return Distance(
                ToPoint(from.Primitive.GetRepresentativePoint()),
                ToPoint(to.Primitive.GetRepresentativePoint()));
        }

        var min = double.PositiveInfinity;
        foreach (var fromPoint in fromPoints)
        {
            var fromPosition = ToPoint(fromPoint);
            foreach (var toPoint in toPoints)
            {
                min = Math.Min(min, Distance(fromPosition, ToPoint(toPoint)));
            }
        }

        return min;
    }

    private static double MinDistanceToFeaturePoints(
        (double X, double Y, double Z)? referencePoint,
        PrimitiveToleranceItem item,
        IReadOnlyDictionary<string, IReadOnlyList<MeasurementPoint>> pointsByFeatureId)
    {
        if (!pointsByFeatureId.TryGetValue(item.Primitive.Id, out var points) || points.Count == 0)
        {
            var representative = ToPoint(item.Primitive.GetRepresentativePoint());
            return referencePoint is null ? 0 : Distance(referencePoint.Value, representative);
        }

        if (referencePoint is null)
            return 0;

        return points.Min(point => Distance(referencePoint.Value, ToPoint(point)));
    }

    private static (double X, double Y, double Z) SelectNearestPoint(
        (double X, double Y, double Z)? referencePoint,
        PrimitiveToleranceItem item,
        IReadOnlyDictionary<string, IReadOnlyList<MeasurementPoint>> pointsByFeatureId)
    {
        if (!pointsByFeatureId.TryGetValue(item.Primitive.Id, out var points) || points.Count == 0)
            return ToPoint(item.Primitive.GetRepresentativePoint());

        if (referencePoint is null)
            return ToPoint(points[0]);

        return points
            .Select(ToPoint)
            .OrderBy(point => Distance(referencePoint.Value, point))
            .First();
    }

    private static int SelectNearestIndex(
        IReadOnlyList<(double X, double Y, double Z)> points,
        (double X, double Y, double Z)? referencePoint)
    {
        if (referencePoint is null)
            return 0;

        var bestIndex = 0;
        var bestDistance = double.PositiveInfinity;
        for (var i = 0; i < points.Count; i++)
        {
            var distance = Distance(referencePoint.Value, points[i]);
            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            bestIndex = i;
        }

        return bestIndex;
    }

    private static void ReverseSegment(IList<int> tour, int start, int end)
    {
        while (start < end)
        {
            (tour[start], tour[end]) = (tour[end], tour[start]);
            start++;
            end--;
        }
    }

    private static double ClosedTourLength(
        IReadOnlyList<(double X, double Y, double Z)> points,
        IReadOnlyList<int> tour,
        (double X, double Y, double Z)? startPoint)
    {
        if (tour.Count == 0)
            return 0;

        var length = startPoint is not null
            ? Distance(startPoint.Value, points[tour[0]])
            : 0;

        for (var i = 0; i < tour.Count - 1; i++)
            length += Distance(points[tour[i]], points[tour[i + 1]]);

        return length;
    }

    private static double OpenTourLength(
        IReadOnlyList<(double X, double Y, double Z)> points,
        IReadOnlyList<int> tour,
        (double X, double Y, double Z)? referencePoint)
    {
        if (tour.Count == 0)
            return 0;

        var length = referencePoint is not null
            ? Distance(referencePoint.Value, points[tour[0]])
            : 0;

        for (var i = 0; i < tour.Count - 1; i++)
            length += Distance(points[tour[i]], points[tour[i + 1]]);

        return length;
    }

    private static (double X, double Y, double Z) ToPoint(MeasurementPoint point) =>
        (point.X, point.Y, point.Z);

    private static (double X, double Y, double Z) ToPoint((double X, double Y, double Z) value) => value;

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
