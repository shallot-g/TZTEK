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
        MeasurementPlanOptions options)
    {
        var margin = (options.TreatProbeAsPoint ? 0 : ResolveProbeRadius(movementStep))
            + Math.Max(0, options.CollisionSafetyMarginMm);
        var target = movementStep.TargetItem?.Primitive;
        var transitKind = PathMovementClassifier.Classify(movementStep);

        return CollisionPrimitiveSource.Resolve(task, options)
            .Where(primitive => !ShouldExcludePrimitive(target, primitive, transitKind))
            .DistinctBy(primitive => primitive.Id)
            .Select(primitive => TryBuildBox(primitive, margin))
            .Where(box => box is not null)
            .Select(box => box!.Value);
    }

    private static bool ShouldExcludePrimitive(
        Primitive? target,
        Primitive candidate,
        CollisionTransitKind transitKind)
    {
        if (target is null)
            return false;

        if (!string.Equals(target.Id, candidate.Id, StringComparison.OrdinalIgnoreCase))
            return false;

        return transitKind is CollisionTransitKind.FeatureEntry
            or CollisionTransitKind.FeatureExit
            or CollisionTransitKind.IntraFeature;
    }

    private static CollisionBox? TryBuildBox(Primitive primitive, double margin) =>
        primitive switch
        {
            PlanePrimitive plane => BuildPlaneBox(plane, margin),
            CylinderPrimitive cylinder => BuildCylinderBox(cylinder, margin),
            SpherePrimitive sphere => BuildSphereBox(sphere, margin),
            Surface3DPrimitive surface => BuildSurfaceBox(surface, margin),
            CirclePrimitive circle => BuildPointBox(circle.Id, circle, circle.CenterX, circle.CenterY, circle.CenterZ, circle.Radius + margin, margin),
            ArcPrimitive arc => BuildPointBox(arc.Id, arc, arc.CenterX, arc.CenterY, arc.CenterZ, arc.Radius + margin, margin),
            LinePrimitive line => BuildLineBox(line, margin),
            PointPrimitive point => BuildPointBox(point.Id, point, point.X, point.Y, point.Z, margin, margin),
            ConePrimitive cone => BuildPointBox(cone.Id, cone, cone.ApexX, cone.ApexY, cone.ApexZ, 10 + margin, margin),
            _ => null
        };

    private static CollisionBox BuildPlaneBox(PlanePrimitive plane, double margin)
    {
        var spread = Math.Max(1.0, Math.Sqrt(plane.SourceAreaMm2 ?? 100.0) * 0.5) + margin;
        var halfThickness = 2.0 + margin;
        return new CollisionBox(
            plane.Id,
            plane,
            margin,
            plane.PointX - spread,
            plane.PointY - spread,
            plane.PointZ - halfThickness,
            plane.PointX + spread,
            plane.PointY + spread,
            plane.PointZ + halfThickness);
    }

    private static CollisionBox BuildCylinderBox(CylinderPrimitive cylinder, double margin)
    {
        var radius = cylinder.Radius + margin;
        if (cylinder.AxisStartX is not null && cylinder.AxisEndX is not null)
        {
            return new CollisionBox(
                cylinder.Id,
                cylinder,
                margin,
                Math.Min(cylinder.AxisStartX.Value, cylinder.AxisEndX.Value) - radius,
                Math.Min(cylinder.AxisStartY.Value, cylinder.AxisEndY.Value) - radius,
                Math.Min(cylinder.AxisStartZ.Value, cylinder.AxisEndZ.Value) - radius,
                Math.Max(cylinder.AxisStartX.Value, cylinder.AxisEndX.Value) + radius,
                Math.Max(cylinder.AxisStartY.Value, cylinder.AxisEndY.Value) + radius,
                Math.Max(cylinder.AxisStartZ.Value, cylinder.AxisEndZ.Value) + radius);
        }

        var center = new CollisionVectors.Vec3(cylinder.AxisPointX, cylinder.AxisPointY, cylinder.AxisPointZ);
        var half = (cylinder.Length ?? cylinder.Radius * 2) / 2 + margin;
        return new CollisionBox(
            cylinder.Id,
            cylinder,
            margin,
            center.X - radius - half,
            center.Y - radius - half,
            center.Z - radius - half,
            center.X + radius + half,
            center.Y + radius + half,
            center.Z + radius + half);
    }

    private static CollisionBox BuildSphereBox(SpherePrimitive sphere, double margin)
    {
        var radius = sphere.Radius + margin;
        return new CollisionBox(
            sphere.Id,
            sphere,
            margin,
            sphere.CenterX - radius,
            sphere.CenterY - radius,
            sphere.CenterZ - radius,
            sphere.CenterX + radius,
            sphere.CenterY + radius,
            sphere.CenterZ + radius);
    }

    private static CollisionBox? BuildSurfaceBox(Surface3DPrimitive surface, double margin)
    {
        if (surface.Vertices.Count == 0)
            return null;

        return new CollisionBox(
            surface.Id,
            surface,
            margin,
            surface.Vertices.Min(v => v.X) - margin,
            surface.Vertices.Min(v => v.Y) - margin,
            surface.Vertices.Min(v => v.Z) - margin,
            surface.Vertices.Max(v => v.X) + margin,
            surface.Vertices.Max(v => v.Y) + margin,
            surface.Vertices.Max(v => v.Z) + margin);
    }

    private static CollisionBox BuildLineBox(LinePrimitive line, double margin)
    {
        var end = new CollisionVectors.Vec3(
            line.StartX + line.DirX,
            line.StartY + line.DirY,
            line.StartZ + line.DirZ);
        return new CollisionBox(
            line.Id,
            line,
            margin,
            Math.Min(line.StartX, end.X) - margin,
            Math.Min(line.StartY, end.Y) - margin,
            Math.Min(line.StartZ, end.Z) - margin,
            Math.Max(line.StartX, end.X) + margin,
            Math.Max(line.StartY, end.Y) + margin,
            Math.Max(line.StartZ, end.Z) + margin);
    }

    private static CollisionBox BuildPointBox(
        string id,
        Primitive primitive,
        double x,
        double y,
        double z,
        double spread,
        double margin) =>
        new(id, primitive, margin, x - spread, y - spread, z - spread, x + spread, y + spread, z + spread);

    private static double ResolveProbeRadius(MeasurementStep step)
    {
        var diameter = step.ProbeAssignment?.TipDiameter ?? 2.0;
        return double.IsFinite(diameter) && diameter > 0 ? diameter / 2.0 : 1.0;
    }
}
