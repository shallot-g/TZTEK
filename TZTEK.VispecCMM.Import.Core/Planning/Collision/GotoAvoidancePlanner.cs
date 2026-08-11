namespace TZTEK.VispecCMM.Import.Core.Planning.Collision;

internal static class GotoAvoidancePlanner
{
    internal sealed record AvoidanceRoute(
        IReadOnlyList<(double X, double Y, double Z)> Waypoints,
        string Strategy);

    public static AvoidanceRoute? FindMinimalRoute(
        MeasurementTask task,
        (double X, double Y, double Z) start,
        (double X, double Y, double Z) end,
        MeasurementStep movementStep,
        MeasurementPlanOptions options,
        ICollisionChecker checker,
        int segmentIndex)
    {
        if (DefaultCollisionChecker.IsSegmentClear(task, start, end, movementStep, options, checker, segmentIndex))
            return new AvoidanceRoute([], "Direct");

        var safeZ = ResolveSafeZ(task, options);
        var clearance = Math.Max(options.AutoSafeGotoExtraClearanceMm, options.SafetyClearanceMm);
        var liftZ = safeZ + clearance;

        var singleGotoCandidates = BuildSingleGotoCandidates(task, start, end, liftZ, options);
        foreach (var candidate in singleGotoCandidates.OrderBy(point => EstimateRouteLength(start, point, end)))
        {
            if (IsRouteClear(task, start, candidate, end, movementStep, options, checker, segmentIndex))
                return new AvoidanceRoute([candidate], "SingleGoto");
        }

        var twoGotoRoute = new[]
        {
            (start.X, start.Y, liftZ),
            (end.X, end.Y, liftZ)
        };
        if (IsRouteClear(task, start, twoGotoRoute[0], end, movementStep, options, checker, segmentIndex, twoGotoRoute[1]))
            return new AvoidanceRoute(twoGotoRoute, "SafePlaneDetour");

        return null;
    }

    private static IEnumerable<(double X, double Y, double Z)> BuildSingleGotoCandidates(
        MeasurementTask task,
        (double X, double Y, double Z) start,
        (double X, double Y, double Z) end,
        double liftZ,
        MeasurementPlanOptions options)
    {
        yield return (start.X, start.Y, liftZ);
        yield return (end.X, end.Y, liftZ);
        yield return ((start.X + end.X) / 2, (start.Y + end.Y) / 2, liftZ);

        if (task.SafetyEnvelope is { } envelope)
        {
            var margin = Math.Max(1.0, options.AutoSafeGotoExtraClearanceMm);
            yield return (envelope.MinX - margin, start.Y, liftZ);
            yield return (envelope.MaxX + margin, start.Y, liftZ);
            yield return (start.X, envelope.MinY - margin, liftZ);
            yield return (start.X, envelope.MaxY + margin, liftZ);
        }

        foreach (var userGoto in options.UserGotoPoints)
            yield return (userGoto.X, userGoto.Y, Math.Max(userGoto.Z, liftZ));
    }

    private static bool IsRouteClear(
        MeasurementTask task,
        (double X, double Y, double Z) start,
        (double X, double Y, double Z) waypoint,
        (double X, double Y, double Z) end,
        MeasurementStep movementStep,
        MeasurementPlanOptions options,
        ICollisionChecker checker,
        int segmentIndex,
        (double X, double Y, double Z)? secondWaypoint = null)
    {
        if (!DefaultCollisionChecker.IsSegmentClear(task, start, waypoint, movementStep, options, checker, segmentIndex))
            return false;

        if (secondWaypoint is { } mid)
        {
            if (!DefaultCollisionChecker.IsSegmentClear(task, waypoint, mid, movementStep, options, checker, segmentIndex))
                return false;

            return DefaultCollisionChecker.IsSegmentClear(task, mid, end, movementStep, options, checker, segmentIndex);
        }

        return DefaultCollisionChecker.IsSegmentClear(task, waypoint, end, movementStep, options, checker, segmentIndex);
    }

    private static double ResolveSafeZ(MeasurementTask task, MeasurementPlanOptions options)
    {
        if (task.SafetyEnvelope is { } envelope)
            return envelope.MaxZ;

        if (task.GlobalSafetyPlane is not null)
            return task.GlobalSafetyPlane.GetPosition().Z;

        return options.SafetyClearanceMm;
    }

    private static double EstimateRouteLength(
        (double X, double Y, double Z) start,
        (double X, double Y, double Z) waypoint,
        (double X, double Y, double Z) end)
    {
        return Distance(start, waypoint) + Distance(waypoint, end);
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
}
