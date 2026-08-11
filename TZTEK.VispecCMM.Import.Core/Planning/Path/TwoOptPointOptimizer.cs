namespace TZTEK.VispecCMM.Import.Core.Planning.Path;

internal static class TwoOptPointOptimizer
{
    public static IReadOnlyList<MeasurementPoint> OptimizeOpenTour(
        IReadOnlyList<MeasurementPoint> points,
        int fixedStartIndex,
        TimeSpan timeout)
    {
        if (points.Count <= 2)
            return PathGeometryHelper.CloneAndReindex(points);

        var tour = Enumerable.Range(0, points.Count).ToList();
        if (fixedStartIndex != 0)
        {
            tour.Remove(fixedStartIndex);
            tour.Insert(0, fixedStartIndex);
        }

        var positions = points.Select(point => PathGeometryHelper.ToVec3((point.X, point.Y, point.Z))).ToList();
        var deadline = DateTime.UtcNow + timeout;
        var improved = true;

        while (improved && DateTime.UtcNow < deadline)
        {
            improved = false;
            var currentLength = OpenTourLength(positions, tour);
            var bestGain = 0.0;
            var bestStart = -1;
            var bestEnd = -1;

            for (var i = 0; i < tour.Count - 2; i++)
            {
                for (var j = i + 2; j < tour.Count; j++)
                {
                    var candidate = tour.ToList();
                    ReverseSegment(candidate, i + 1, j);
                    var candidateLength = OpenTourLength(positions, candidate);
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

        return PathGeometryHelper.CloneAndReindex(tour.Select(index => points[index]).ToList());
    }

    private static double OpenTourLength(
        IReadOnlyList<PlanningVectors.Vec3> positions,
        IReadOnlyList<int> tour)
    {
        var length = 0.0;
        for (var i = 0; i < tour.Count - 1; i++)
            length += PlanningVectors.Distance(positions[tour[i]], positions[tour[i + 1]]);
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
