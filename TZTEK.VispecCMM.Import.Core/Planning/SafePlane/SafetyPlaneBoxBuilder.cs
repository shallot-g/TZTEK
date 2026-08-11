namespace TZTEK.VispecCMM.Import.Core.Planning.SafePlane;

internal static class SafetyPlaneBoxBuilder
{
    public static SafetyPlaneBox Build(
        IReadOnlyList<Primitive> collisionPrimitives,
        MeasurementPlanOptions options)
    {
        var clearance = Math.Max(0, options.SafetyClearanceMm);
        var bounds = WorkpieceBoundsCalculator.Calculate(collisionPrimitives);
        var expanded = bounds.Expand(clearance);

        var planes = new Dictionary<SafetyPlaneFace, SafetyPlane>
        {
            [SafetyPlaneFace.Top] = CreateFacePlane(
                SafetyPlaneFace.Top,
                expanded.CenterX,
                expanded.CenterY,
                expanded.MaxZ,
                0,
                0,
                1),
            [SafetyPlaneFace.Bottom] = CreateFacePlane(
                SafetyPlaneFace.Bottom,
                expanded.CenterX,
                expanded.CenterY,
                expanded.MinZ,
                0,
                0,
                -1),
            [SafetyPlaneFace.PosX] = CreateFacePlane(
                SafetyPlaneFace.PosX,
                expanded.MaxX,
                expanded.CenterY,
                expanded.CenterZ,
                1,
                0,
                0),
            [SafetyPlaneFace.NegX] = CreateFacePlane(
                SafetyPlaneFace.NegX,
                expanded.MinX,
                expanded.CenterY,
                expanded.CenterZ,
                -1,
                0,
                0),
            [SafetyPlaneFace.PosY] = CreateFacePlane(
                SafetyPlaneFace.PosY,
                expanded.CenterX,
                expanded.MaxY,
                expanded.CenterZ,
                0,
                1,
                0),
            [SafetyPlaneFace.NegY] = CreateFacePlane(
                SafetyPlaneFace.NegY,
                expanded.CenterX,
                expanded.MinY,
                expanded.CenterZ,
                0,
                -1,
                0)
        };

        return new SafetyPlaneBox
        {
            MinX = expanded.MinX,
            MinY = expanded.MinY,
            MinZ = expanded.MinZ,
            MaxX = expanded.MaxX,
            MaxY = expanded.MaxY,
            MaxZ = expanded.MaxZ,
            ClearanceMm = clearance,
            Planes = planes
        };
    }

    public static void ApplyToTask(MeasurementTask task, MeasurementPlanOptions options)
    {
        var collisionPrimitives = options.CollisionPrimitives.Count > 0
            ? options.CollisionPrimitives
            : task.CollisionPrimitives;

        if (collisionPrimitives.Count == 0)
            return;

        var envelope = Build(collisionPrimitives, options);
        task.SafetyEnvelope = envelope;
        task.GlobalSafetyPlane = envelope.GetPlane(SafetyPlaneFace.Top);
        SafetyPlaneAssigner.AssignTaskPoints(task, options);
    }

    private static SafetyPlane CreateFacePlane(
        SafetyPlaneFace face,
        double pointX,
        double pointY,
        double pointZ,
        double normalX,
        double normalY,
        double normalZ)
    {
        return new SafetyPlane
        {
            Id = $"safety_{face.ToString().ToLowerInvariant()}",
            Name = $"Safety Plane {face}",
            PointX = pointX,
            PointY = pointY,
            PointZ = pointZ,
            NormalX = normalX,
            NormalY = normalY,
            NormalZ = normalZ,
            OffsetMm = 0
        };
    }
}
