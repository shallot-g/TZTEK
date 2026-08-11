namespace TZTEK.VispecCMM.Import.Core.Planning.Collision;

internal sealed class DefaultCollisionChecker : ICollisionChecker
{
    public CollisionResult Check(
        MeasurementTask task,
        (double X, double Y, double Z) start,
        (double X, double Y, double Z) end,
        MeasurementStep movementStep,
        MeasurementPlanOptions options,
        int segmentIndex)
    {
        if (!PathMovementClassifier.RequiresCollisionCheck(movementStep))
        {
            return new CollisionResult
            {
                HasCollision = false,
                TotalSegmentsChecked = 1
            };
        }

        if (IsAboveSafeHeight(task, start, end))
        {
            return new CollisionResult
            {
                HasCollision = false,
                TotalSegmentsChecked = 1
            };
        }

        var boxes = CollisionBoxBuilder.Build(task, movementStep, options).ToList();
        var collisions = new List<CollisionEvent>();

        foreach (var box in boxes)
        {
            if (!SegmentCollisionChecker.IntersectsBox(box, start, end))
                continue;

            if (options.EnablePrimitiveNarrowPhaseCollisionCheck
                && box.Primitive is not null
                && !SegmentCollisionChecker.IntersectsPrimitiveNarrowPhase(box.Primitive, start, end, box.Margin))
            {
                continue;
            }

            collisions.Add(new CollisionEvent
            {
                X = (start.X + end.X) / 2,
                Y = (start.Y + end.Y) / 2,
                Z = (start.Z + end.Z) / 2,
                SegmentIndex = segmentIndex,
                InvolvedElementIds = [box.ElementId],
                Severity = 1
            });
        }

        return new CollisionResult
        {
            HasCollision = collisions.Count > 0,
            Collisions = collisions,
            TotalSegmentsChecked = 1
        };
    }

    internal static bool IsSegmentClear(
        MeasurementTask task,
        (double X, double Y, double Z) start,
        (double X, double Y, double Z) end,
        MeasurementStep movementStep,
        MeasurementPlanOptions options,
        ICollisionChecker checker,
        int segmentIndex) =>
        !checker.Check(task, start, end, movementStep, options, segmentIndex).HasCollision;

    private static bool IsAboveSafeHeight(
        MeasurementTask task,
        (double X, double Y, double Z) start,
        (double X, double Y, double Z) end)
    {
        var safeZ = task.SafetyEnvelope?.MaxZ ?? task.GlobalSafetyPlane?.GetPosition().Z;
        if (safeZ is null || !double.IsFinite(safeZ.Value))
            return false;

        return start.Z >= safeZ.Value - 1e-3 && end.Z >= safeZ.Value - 1e-3;
    }
}
