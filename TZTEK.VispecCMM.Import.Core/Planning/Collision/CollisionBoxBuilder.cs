namespace TZTEK.VispecCMM.Import.Core.Planning.Collision;

internal readonly record struct CollisionBox(
    string ElementId,
    Primitive? Primitive,
    double Margin,
    double MinX,
    double MinY,
    double MinZ,
    double MaxX,
    double MaxY,
    double MaxZ);

internal static class CollisionBoxBuilder
{
    public static IEnumerable<CollisionBox> Build(
        MeasurementTask task,
        MeasurementStep movementStep,
        MeasurementPlanOptions options,
        bool excludeTargetPrimitive = true)
    {
        var inflation = ResolveInflation(movementStep, options);
        var target = movementStep.TargetItem?.Primitive;
        var transitKind = PathMovementClassifier.Classify(movementStep);

        return CollisionPrimitiveSource.Resolve(task, options)
            .Where(primitive => !ShouldExcludePrimitive(target, primitive, transitKind, excludeTargetPrimitive))
            .Where(IsCollisionSurface)
            .DistinctBy(primitive => primitive.Id)
            .Select(primitive => TryBuildBox(primitive, inflation))
            .Where(box => box is not null)
            .Select(box => box!.Value);
    }

    public static double ResolveInflation(MeasurementStep movementStep, MeasurementPlanOptions options) =>
        (options.TreatProbeAsPoint ? 0 : ResolveProbeRadius(movementStep))
        + Math.Max(0, options.CollisionSafetyMarginMm);

    private static bool IsCollisionSurface(Primitive primitive) =>
        primitive is PlanePrimitive
            or CylinderPrimitive
            or ConePrimitive
            or SpherePrimitive
            or Surface3DPrimitive;

    private static bool ShouldExcludePrimitive(
        Primitive? target,
        Primitive candidate,
        CollisionTransitKind transitKind,
        bool excludeTargetPrimitive)
    {
        if (!excludeTargetPrimitive || target is null)
            return false;

        if (!string.Equals(target.Id, candidate.Id, StringComparison.OrdinalIgnoreCase))
            return false;

        return transitKind is CollisionTransitKind.FeatureEntry
            or CollisionTransitKind.FeatureExit
            or CollisionTransitKind.IntraFeature;
    }

    private static CollisionBox? TryBuildBox(Primitive primitive, double inflation) =>
        primitive switch
        {
            PlanePrimitive plane => BuildPlaneBox(plane, inflation),
            CylinderPrimitive cylinder => BuildCylinderBox(cylinder, inflation),
            SpherePrimitive sphere => BuildSphereBox(sphere, inflation),
            Surface3DPrimitive surface => BuildSurfaceBox(surface, inflation),
            ConePrimitive cone => BuildConeBox(cone, inflation),
            _ => null
        };

    private static CollisionBox BuildPlaneBox(PlanePrimitive plane, double inflation)
    {
        var origin = new CollisionVectors.Vec3(plane.PointX, plane.PointY, plane.PointZ);
        var normal = CollisionVectors.Normalize(new CollisionVectors.Vec3(plane.NormalX, plane.NormalY, plane.NormalZ));
        var radius = CollisionGeometry.FaceRadius(plane) + inflation;
        var pad = Math.Max(inflation, 1e-6);
        return new CollisionBox(
            plane.Id,
            plane,
            inflation,
            origin.X - CollisionVectors.DiskExtent(normal.X, radius) - pad,
            origin.Y - CollisionVectors.DiskExtent(normal.Y, radius) - pad,
            origin.Z - CollisionVectors.DiskExtent(normal.Z, radius) - pad,
            origin.X + CollisionVectors.DiskExtent(normal.X, radius) + pad,
            origin.Y + CollisionVectors.DiskExtent(normal.Y, radius) + pad,
            origin.Z + CollisionVectors.DiskExtent(normal.Z, radius) + pad);
    }

    private static CollisionBox BuildCylinderBox(CylinderPrimitive cylinder, double inflation)
    {
        if (!CollisionGeometry.TryGetCylinderAxis(cylinder, out var start, out var end, out var axis, out _))
            return BuildPointBox(cylinder.Id, cylinder, cylinder.AxisPointX, cylinder.AxisPointY, cylinder.AxisPointZ, cylinder.Radius + inflation, inflation);

        return BuildFiniteWallBox(cylinder.Id, cylinder, start, end, axis, cylinder.Radius, inflation);
    }

    private static CollisionBox? BuildConeBox(ConePrimitive cone, double inflation)
    {
        if (!CollisionGeometry.TryGetConeAxis(cone, out var start, out var end, out var axis, out _, out var radiusStart, out var radiusEnd))
            return null;

        return BuildFiniteWallBox(cone.Id, cone, start, end, axis, Math.Max(radiusStart, radiusEnd), inflation);
    }

    private static CollisionBox BuildFiniteWallBox(
        string id,
        Primitive primitive,
        CollisionVectors.Vec3 start,
        CollisionVectors.Vec3 end,
        CollisionVectors.Vec3 axis,
        double radius,
        double inflation)
    {
        var grown = radius + inflation;
        var pad = Math.Max(inflation, 1e-6);
        var extentX = Math.Abs(end.X - start.X) / 2 + CollisionVectors.DiskExtent(axis.X, grown) + pad;
        var extentY = Math.Abs(end.Y - start.Y) / 2 + CollisionVectors.DiskExtent(axis.Y, grown) + pad;
        var extentZ = Math.Abs(end.Z - start.Z) / 2 + CollisionVectors.DiskExtent(axis.Z, grown) + pad;
        var center = CollisionVectors.Scale(CollisionVectors.Add(start, end), 0.5);
        return new CollisionBox(
            id,
            primitive,
            inflation,
            center.X - extentX,
            center.Y - extentY,
            center.Z - extentZ,
            center.X + extentX,
            center.Y + extentY,
            center.Z + extentZ);
    }

    private static CollisionBox BuildSphereBox(SpherePrimitive sphere, double inflation)
    {
        var radius = sphere.Radius + inflation;
        return new CollisionBox(
            sphere.Id,
            sphere,
            inflation,
            sphere.CenterX - radius,
            sphere.CenterY - radius,
            sphere.CenterZ - radius,
            sphere.CenterX + radius,
            sphere.CenterY + radius,
            sphere.CenterZ + radius);
    }

    private static CollisionBox? BuildSurfaceBox(Surface3DPrimitive surface, double inflation)
    {
        if (surface.Vertices.Count == 0)
            return null;

        return new CollisionBox(
            surface.Id,
            surface,
            inflation,
            surface.Vertices.Min(v => v.X) - inflation,
            surface.Vertices.Min(v => v.Y) - inflation,
            surface.Vertices.Min(v => v.Z) - inflation,
            surface.Vertices.Max(v => v.X) + inflation,
            surface.Vertices.Max(v => v.Y) + inflation,
            surface.Vertices.Max(v => v.Z) + inflation);
    }

    private static CollisionBox BuildPointBox(
        string id,
        Primitive primitive,
        double x,
        double y,
        double z,
        double spread,
        double inflation) =>
        new(id, primitive, inflation, x - spread, y - spread, z - spread, x + spread, y + spread, z + spread);

    private static double ResolveProbeRadius(MeasurementStep step)
    {
        var diameter = step.ProbeAssignment?.TipDiameter ?? 2.0;
        return double.IsFinite(diameter) && diameter > 0 ? diameter / 2.0 : 1.0;
    }
}
