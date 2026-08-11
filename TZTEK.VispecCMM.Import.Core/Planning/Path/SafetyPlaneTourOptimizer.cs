namespace TZTEK.VispecCMM.Import.Core.Planning.Path;

internal static class SafetyPlaneTourOptimizer
{
    internal sealed record TourPlan(
        IReadOnlyList<int> OrderedIndices,
        SurfacePoint StartPoint,
        int StartProjectionIndex);

    public static TourPlan Optimize(
        SafetyPlaneBox envelope,
        IReadOnlyList<FeatureSafetyProjection> projections,
        MeasurementPlanOptions options)
    {
        if (projections.Count == 0)
            return new TourPlan([], new SurfacePoint(SafetyPlaneFace.Top, envelope.CenterX, envelope.CenterY, envelope.MaxZ), -1);

        var surfacePoints = projections
            .Select(projection => SafetyPlaneSurfaceRouter.ToSurfacePoint(envelope, projection))
            .ToList();

        var startIndex = SelectRandomTopStartIndex(projections, options);
        var startPoint = startIndex >= 0
            ? surfacePoints[startIndex]
            : new SurfacePoint(SafetyPlaneFace.Top, surfacePoints[0].X, surfacePoints[0].Y, envelope.MaxZ);

        var tour = BuildNearestNeighborTour(surfacePoints, startIndex, startPoint, envelope);
        ImproveOpenTour(surfacePoints, tour, envelope, options.PathTimeout);

        return new TourPlan(tour, startPoint, startIndex);
    }

    private static int SelectRandomTopStartIndex(
        IReadOnlyList<FeatureSafetyProjection> projections,
        MeasurementPlanOptions options)
    {
        var topIndices = projections
            .Select((projection, index) => (projection, index))
            .Where(entry => entry.projection.AssignedFace == SafetyPlaneFace.Top)
            .Select(entry => entry.index)
            .ToList();

        if (topIndices.Count == 0)
            return projections.Count > 0 ? 0 : -1;

        var seed = HashCode.Combine(options.PathStrategy, projections.Count, options.SafetyClearanceMm);
        var random = options.StartPoint is not null
            ? new Random(seed)
            : Random.Shared;

        return topIndices[random.Next(topIndices.Count)];
    }

    private static List<int> BuildNearestNeighborTour(
        IReadOnlyList<SurfacePoint> points,
        int startIndex,
        SurfacePoint startPoint,
        SafetyPlaneBox envelope)
    {
        var remaining = Enumerable.Range(0, points.Count).ToList();
        var tour = new List<int>(points.Count);

        if (startIndex >= 0 && startIndex < points.Count)
        {
            tour.Add(startIndex);
            remaining.Remove(startIndex);
        }

        var current = startIndex >= 0 ? points[startIndex] : startPoint;
        while (remaining.Count > 0)
        {
            var nextIndex = remaining
                .OrderBy(index => SafetyPlaneSurfaceRouter.SurfaceDistance(envelope, current, points[index]))
                .First();
            remaining.Remove(nextIndex);
            tour.Add(nextIndex);
            current = points[nextIndex];
        }

        return tour;
    }

    private static void ImproveOpenTour(
        IReadOnlyList<SurfacePoint> points,
        List<int> tour,
        SafetyPlaneBox envelope,
        TimeSpan timeout)
    {
        if (tour.Count <= 2)
            return;

        var deadline = DateTime.UtcNow + timeout;
        var improved = true;
        while (improved && DateTime.UtcNow < deadline)
        {
            improved = false;
            var currentLength = OpenTourLength(points, tour, envelope);
            var bestGain = 0.0;
            var bestStart = -1;
            var bestEnd = -1;

            for (var i = 0; i < tour.Count - 2; i++)
            {
                for (var j = i + 2; j < tour.Count; j++)
                {
                    var candidate = tour.ToList();
                    ReverseSegment(candidate, i + 1, j);
                    var candidateLength = OpenTourLength(points, candidate, envelope);
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

    private static double OpenTourLength(
        IReadOnlyList<SurfacePoint> points,
        IReadOnlyList<int> tour,
        SafetyPlaneBox envelope)
    {
        var length = 0.0;
        for (var i = 0; i < tour.Count - 1; i++)
            length += SafetyPlaneSurfaceRouter.SurfaceDistance(envelope, points[tour[i]], points[tour[i + 1]]);
        return length;
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
}
