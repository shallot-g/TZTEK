namespace TZTEK.VispecCMM.Import.Core.Planning.SafePlane;

internal readonly record struct WorkpieceBounds(
    double MinX,
    double MinY,
    double MinZ,
    double MaxX,
    double MaxY,
    double MaxZ)
{
    public double CenterX => (MinX + MaxX) / 2.0;
    public double CenterY => (MinY + MaxY) / 2.0;
    public double CenterZ => (MinZ + MaxZ) / 2.0;

    public WorkpieceBounds Expand(double clearance) =>
        new(
            MinX - clearance,
            MinY - clearance,
            MinZ - clearance,
            MaxX + clearance,
            MaxY + clearance,
            MaxZ + clearance);
}

internal static class WorkpieceBoundsCalculator
{
    public static WorkpieceBounds Calculate(IReadOnlyList<Primitive> primitives)
    {
        var points = new List<(double X, double Y, double Z)>();
        foreach (var primitive in primitives)
            CollectPoints(primitive, points);

        if (points.Count == 0)
            return new WorkpieceBounds(-1, -1, -1, 1, 1, 1);

        return new WorkpieceBounds(
            points.Min(point => point.X),
            points.Min(point => point.Y),
            points.Min(point => point.Z),
            points.Max(point => point.X),
            points.Max(point => point.Y),
            points.Max(point => point.Z));
    }

    private static void CollectPoints(Primitive primitive, List<(double X, double Y, double Z)> points)
    {
        var representative = primitive.GetRepresentativePoint();
        var radius = primitive switch
        {
            CylinderPrimitive cylinder => cylinder.Radius,
            CirclePrimitive circle => circle.Radius,
            ArcPrimitive arc => arc.Radius,
            SpherePrimitive sphere => sphere.Radius,
            _ => 0.0
        };

        points.Add((representative.X - radius, representative.Y - radius, representative.Z - radius));
        points.Add((representative.X + radius, representative.Y + radius, representative.Z + radius));

        switch (primitive)
        {
            case Surface3DPrimitive surface:
                points.AddRange(surface.Vertices);
                break;
            case CylinderPrimitive { AxisStartX: not null, AxisStartY: not null, AxisStartZ: not null, AxisEndX: not null, AxisEndY: not null, AxisEndZ: not null } cylinder:
                points.Add((
                    cylinder.AxisStartX.Value - cylinder.Radius,
                    cylinder.AxisStartY.Value - cylinder.Radius,
                    cylinder.AxisStartZ.Value - cylinder.Radius));
                points.Add((
                    cylinder.AxisEndX.Value + cylinder.Radius,
                    cylinder.AxisEndY.Value + cylinder.Radius,
                    cylinder.AxisEndZ.Value + cylinder.Radius));
                break;
            case ConePrimitive { AxisStartX: not null, AxisStartY: not null, AxisStartZ: not null, AxisEndX: not null, AxisEndY: not null, AxisEndZ: not null } cone:
                points.Add((cone.AxisStartX.Value, cone.AxisStartY.Value, cone.AxisStartZ.Value));
                points.Add((cone.AxisEndX.Value, cone.AxisEndY.Value, cone.AxisEndZ.Value));
                break;
            case PlanePrimitive plane:
            {
                var spread = Math.Max(1.0, Math.Sqrt(plane.SourceAreaMm2 ?? 100.0) * 0.5);
                points.Add((plane.PointX - spread, plane.PointY - spread, plane.PointZ));
                points.Add((plane.PointX + spread, plane.PointY + spread, plane.PointZ));
                break;
            }
        }
    }
}
