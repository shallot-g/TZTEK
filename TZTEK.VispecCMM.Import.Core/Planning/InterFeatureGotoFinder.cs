namespace TZTEK.VispecCMM.Import.Core.Planning;

internal static class InterFeatureGotoFinder
{
    private const int MaxCandidateCount = 256;
    private const int RefinementRadiusSteps = 2;

    public static bool TryFindShortestCollisionFreeGoto(
        ICollisionChecker collisionChecker,
        MeasurementTask task,
        (double X, double Y, double Z) start,
        (double X, double Y, double Z) target,
        MeasurementStep sourceStep,
        MeasurementPlanOptions options,
        int segmentIndex,
        double searchCeilingZ,
        out GotoPoint gotoPoint)
    {
        gotoPoint = new GotoPoint();
        if (!options.EnableInterFeatureAutoGoto)
            return false;

        var candidates = GenerateCandidates(start, target, options, searchCeilingZ)
            .OrderBy(point => Distance(start, point) + Distance(point, target))
            .Take(MaxCandidateCount)
            .ToList();

        (double X, double Y, double Z)? bestPoint = null;
        var bestLength = double.PositiveInfinity;

        foreach (var candidate in candidates)
        {
            if (!IsCollisionFreeViaGoto(collisionChecker, task, start, candidate, target, sourceStep, options, segmentIndex))
                continue;

            var length = Distance(start, candidate) + Distance(candidate, target);
            if (length >= bestLength - 1e-6)
                continue;

            bestLength = length;
            bestPoint = candidate;
        }

        if (bestPoint is null)
            return false;

        foreach (var refined in RefineAround(bestPoint.Value, options))
        {
            if (!IsCollisionFreeViaGoto(collisionChecker, task, start, refined, target, sourceStep, options, segmentIndex))
                continue;

            var length = Distance(start, refined) + Distance(refined, target);
            if (length >= bestLength - 1e-6)
                continue;

            bestLength = length;
            bestPoint = refined;
        }

        gotoPoint = new GotoPoint
        {
            Id = $"auto_inter_feature_goto_{segmentIndex}",
            X = bestPoint.Value.X,
            Y = bestPoint.Value.Y,
            Z = bestPoint.Value.Z,
            Reason = $"Auto GOTO, total detour {bestLength:F1} mm"
        };
        return true;
    }

    private static IEnumerable<(double X, double Y, double Z)> GenerateCandidates(
        (double X, double Y, double Z) start,
        (double X, double Y, double Z) target,
        MeasurementPlanOptions options,
        double searchCeilingZ)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var clearance = Math.Max(1.0, Math.Max(options.CollisionSafetyMarginMm, options.SafetyClearanceMm));
        var span = Math.Max(Distance(start, target), clearance);
        var lateralStep = Math.Max(clearance, span * 0.2);
        var mid = Lerp(start, target, 0.5);
        var chord = Subtract(target, start);
        var perpA = Normalize(Cross(chord, (0, 0, 1)));
        if (Length(perpA) < 1e-6)
            perpA = Normalize(Cross(chord, (1, 0, 0)));
        var perpB = Normalize(Cross(chord, perpA));

        foreach (var fraction in new[] { 0.2, 0.33, 0.5, 0.67, 0.8 })
        {
            var anchor = Lerp(start, target, fraction);
            foreach (var point in YieldUnique(anchor, seen))
                yield return point;

            for (var scale = 0.5; scale <= 2.5; scale += 0.5)
            {
                var offset = lateralStep * scale;
                foreach (var point in YieldUnique(Add(anchor, Scale(perpA, offset)), seen)) yield return point;
                foreach (var point in YieldUnique(Add(anchor, Scale(perpA, -offset)), seen)) yield return point;
                foreach (var point in YieldUnique(Add(anchor, Scale(perpB, offset)), seen)) yield return point;
                foreach (var point in YieldUnique(Add(anchor, Scale(perpB, -offset)), seen)) yield return point;
                foreach (var point in YieldUnique(Add(anchor, Add(Scale(perpA, offset), Scale(perpB, offset))), seen)) yield return point;
                foreach (var point in YieldUnique(Add(anchor, Add(Scale(perpA, -offset), Scale(perpB, offset))), seen)) yield return point;
            }
        }

        var maxEndpointZ = Math.Max(start.Z, target.Z);
        var minEndpointZ = Math.Min(start.Z, target.Z);
        var liftStep = Math.Max(clearance, options.SafetyClearanceMm);
        var maxLift = Math.Max(liftStep, searchCeilingZ - minEndpointZ);
        for (var lift = liftStep; lift <= maxLift + 1e-6; lift += liftStep)
        {
            var z = maxEndpointZ + lift;
            foreach (var point in YieldUnique((start.X, start.Y, z), seen)) yield return point;
            foreach (var point in YieldUnique((target.X, target.Y, z), seen)) yield return point;
            foreach (var point in YieldUnique((mid.X, mid.Y, z), seen)) yield return point;
        }

        for (var ix = -3; ix <= 3; ix++)
        {
            for (var iy = -3; iy <= 3; iy++)
            {
                for (var iz = -1; iz <= 3; iz++)
                {
                    foreach (var point in YieldUnique((
                        mid.X + ix * lateralStep,
                        mid.Y + iy * lateralStep,
                        mid.Z + iz * liftStep), seen))
                    {
                        yield return point;
                    }
                }
            }
        }
    }

    private static IEnumerable<(double X, double Y, double Z)> YieldUnique(
        (double X, double Y, double Z) point,
        ISet<string> seen)
    {
        var key = $"{Math.Round(point.X, 1)}|{Math.Round(point.Y, 1)}|{Math.Round(point.Z, 1)}";
        if (!seen.Add(key))
            yield break;

        yield return point;
    }

    private static IEnumerable<(double X, double Y, double Z)> RefineAround(
        (double X, double Y, double Z) center,
        MeasurementPlanOptions options)
    {
        var step = Math.Max(0.5, options.CollisionSafetyMarginMm);
        for (var ix = -RefinementRadiusSteps; ix <= RefinementRadiusSteps; ix++)
        {
            for (var iy = -RefinementRadiusSteps; iy <= RefinementRadiusSteps; iy++)
            {
                for (var iz = -RefinementRadiusSteps; iz <= RefinementRadiusSteps; iz++)
                {
                    if (ix == 0 && iy == 0 && iz == 0)
                        continue;

                    yield return (
                        center.X + ix * step,
                        center.Y + iy * step,
                        center.Z + iz * step);
                }
            }
        }
    }

    private static bool IsCollisionFreeViaGoto(
        ICollisionChecker collisionChecker,
        MeasurementTask task,
        (double X, double Y, double Z) start,
        (double X, double Y, double Z) gotoPoint,
        (double X, double Y, double Z) target,
        MeasurementStep sourceStep,
        MeasurementPlanOptions options,
        int segmentIndex)
    {
        var first = collisionChecker.Check(task, start, gotoPoint, sourceStep, options, segmentIndex);
        if (first.HasCollision)
            return false;

        var second = collisionChecker.Check(task, gotoPoint, target, sourceStep, options, segmentIndex);
        return !second.HasCollision;
    }

    private static (double X, double Y, double Z) Lerp(
        (double X, double Y, double Z) start,
        (double X, double Y, double Z) end,
        double t) =>
        (
            start.X + (end.X - start.X) * t,
            start.Y + (end.Y - start.Y) * t,
            start.Z + (end.Z - start.Z) * t);

    private static (double X, double Y, double Z) Subtract(
        (double X, double Y, double Z) left,
        (double X, double Y, double Z) right) =>
        (left.X - right.X, left.Y - right.Y, left.Z - right.Z);

    private static (double X, double Y, double Z) Add(
        (double X, double Y, double Z) left,
        (double X, double Y, double Z) right) =>
        (left.X + right.X, left.Y + right.Y, left.Z + right.Z);

    private static (double X, double Y, double Z) Scale((double X, double Y, double Z) value, double scale) =>
        (value.X * scale, value.Y * scale, value.Z * scale);

    private static (double X, double Y, double Z) Cross(
        (double X, double Y, double Z) left,
        (double X, double Y, double Z) right) =>
        (
            left.Y * right.Z - left.Z * right.Y,
            left.Z * right.X - left.X * right.Z,
            left.X * right.Y - left.Y * right.X);

    private static (double X, double Y, double Z) Normalize((double X, double Y, double Z) value)
    {
        var length = Length(value);
        return length < 1e-12 ? (0, 0, 0) : (value.X / length, value.Y / length, value.Z / length);
    }

    private static double Length((double X, double Y, double Z) value) =>
        Math.Sqrt(value.X * value.X + value.Y * value.Y + value.Z * value.Z);

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
