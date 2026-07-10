namespace TZTEK.VispecCMM.Import.Core.Planning;

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
        var boxes = BuildCollisionBoxes(task, movementStep, options).ToList();
        var collisions = new List<CollisionEvent>();

        foreach (var box in boxes)
        {
            if (!IntersectsSegment(box, start, end))
                continue;

            collisions.Add(new CollisionEvent
            {
                X = (start.X + end.X) / 2,
                Y = (start.Y + end.Y) / 2,
                Z = (start.Z + end.Z) / 2,
                SegmentIndex = segmentIndex,
                InvolvedElementIds = [box.ElementId],
                Severity = 1,
                SuggestedAvoidancePoint = BuildSuggestedAvoidancePoint(movementStep, start, options)
            });
        }

        return new CollisionResult
        {
            HasCollision = collisions.Count > 0,
            Collisions = collisions,
            TotalSegmentsChecked = 1
        };
    }

    private static IEnumerable<CollisionBox> BuildCollisionBoxes(
        MeasurementTask task,
        MeasurementStep movementStep,
        MeasurementPlanOptions options)
    {
        var margin = ResolveProbeRadius(movementStep) + Math.Max(0, options.CollisionSafetyMarginMm);
        return task.Steps
            .Select(step => step.TargetItem?.Primitive)
            .OfType<Primitive>()
            .Where(primitive => movementStep.TargetItem?.Primitive?.Id != primitive.Id)
            .DistinctBy(primitive => primitive.Id)
            .Select(primitive => TryBuildBox(primitive, margin))
            .Where(box => box is not null)
            .Select(box => box!.Value);
    }

    private static CollisionBox? TryBuildBox(Primitive primitive, double margin)
    {
        return primitive switch
        {
            PlanePrimitive plane => BuildPlaneBox(plane, margin),
            CylinderPrimitive cylinder => BuildCylinderBox(cylinder, margin),
            ConePrimitive cone => BuildPointBox(cone.Id, cone.ApexX, cone.ApexY, cone.ApexZ, 10 + margin),
            SpherePrimitive sphere => BuildSphereBox(sphere, margin),
            Surface3DPrimitive surface => BuildSurfaceBox(surface, margin),
            CirclePrimitive circle => BuildPointBox(circle.Id, circle.CenterX, circle.CenterY, circle.CenterZ, circle.Radius + margin),
            ArcPrimitive arc => BuildPointBox(arc.Id, arc.CenterX, arc.CenterY, arc.CenterZ, arc.Radius + margin),
            LinePrimitive line => BuildLineBox(line, margin),
            PointPrimitive point => BuildPointBox(point.Id, point.X, point.Y, point.Z, margin),
            _ => null
        };
    }

    private static CollisionBox BuildPlaneBox(PlanePrimitive plane, double margin)
    {
        var spread = Math.Max(1.0, Math.Sqrt(plane.SourceAreaMm2 ?? 100.0) * 0.5) + margin;
        return BuildPointBox(plane.Id, plane.PointX, plane.PointY, plane.PointZ, spread);
    }

    private static CollisionBox BuildCylinderBox(CylinderPrimitive cylinder, double margin)
    {
        var axis = Normalize((cylinder.AxisDirX, cylinder.AxisDirY, cylinder.AxisDirZ));
        var length = Math.Max(cylinder.Radius, Math.Sqrt(cylinder.SourceAreaMm2 ?? 0) / Math.Max(cylinder.Radius * Math.PI * 2, 1));
        var center = (cylinder.AxisPointX, cylinder.AxisPointY, cylinder.AxisPointZ);
        var p0 = Add(center, Scale(axis, -length / 2));
        var p1 = Add(center, Scale(axis, length / 2));
        var radius = cylinder.Radius + margin;

        return new CollisionBox(
            cylinder.Id,
            Math.Min(p0.X, p1.X) - radius,
            Math.Min(p0.Y, p1.Y) - radius,
            Math.Min(p0.Z, p1.Z) - radius,
            Math.Max(p0.X, p1.X) + radius,
            Math.Max(p0.Y, p1.Y) + radius,
            Math.Max(p0.Z, p1.Z) + radius);
    }

    private static CollisionBox BuildSphereBox(SpherePrimitive sphere, double margin)
    {
        return BuildPointBox(sphere.Id, sphere.CenterX, sphere.CenterY, sphere.CenterZ, sphere.Radius + margin);
    }

    private static CollisionBox? BuildSurfaceBox(Surface3DPrimitive surface, double margin)
    {
        if (surface.Vertices.Count == 0)
            return null;

        return new CollisionBox(
            surface.Id,
            surface.Vertices.Min(vertex => vertex.X) - margin,
            surface.Vertices.Min(vertex => vertex.Y) - margin,
            surface.Vertices.Min(vertex => vertex.Z) - margin,
            surface.Vertices.Max(vertex => vertex.X) + margin,
            surface.Vertices.Max(vertex => vertex.Y) + margin,
            surface.Vertices.Max(vertex => vertex.Z) + margin);
    }

    private static CollisionBox BuildLineBox(LinePrimitive line, double margin)
    {
        var end = (line.StartX + line.DirX, line.StartY + line.DirY, line.StartZ + line.DirZ);
        return new CollisionBox(
            line.Id,
            Math.Min(line.StartX, end.Item1) - margin,
            Math.Min(line.StartY, end.Item2) - margin,
            Math.Min(line.StartZ, end.Item3) - margin,
            Math.Max(line.StartX, end.Item1) + margin,
            Math.Max(line.StartY, end.Item2) + margin,
            Math.Max(line.StartZ, end.Item3) + margin);
    }

    private static CollisionBox BuildPointBox(string id, double x, double y, double z, double radius)
    {
        return new CollisionBox(id, x - radius, y - radius, z - radius, x + radius, y + radius, z + radius);
    }

    private static bool IntersectsSegment(
        CollisionBox box,
        (double X, double Y, double Z) start,
        (double X, double Y, double Z) end)
    {
        var tMin = 0.0;
        var tMax = 1.0;
        return IntersectsAxis(start.X, end.X, box.MinX, box.MaxX, ref tMin, ref tMax)
            && IntersectsAxis(start.Y, end.Y, box.MinY, box.MaxY, ref tMin, ref tMax)
            && IntersectsAxis(start.Z, end.Z, box.MinZ, box.MaxZ, ref tMin, ref tMax);
    }

    private static bool IntersectsAxis(
        double start,
        double end,
        double min,
        double max,
        ref double tMin,
        ref double tMax)
    {
        var delta = end - start;
        if (Math.Abs(delta) < 1e-12)
            return start >= min && start <= max;

        var inv = 1.0 / delta;
        var t1 = (min - start) * inv;
        var t2 = (max - start) * inv;
        if (t1 > t2)
            (t1, t2) = (t2, t1);

        tMin = Math.Max(tMin, t1);
        tMax = Math.Min(tMax, t2);
        return tMin <= tMax;
    }

    private static GotoPoint BuildSuggestedAvoidancePoint(
        MeasurementStep step,
        (double X, double Y, double Z) start,
        MeasurementPlanOptions options)
    {
        var normal = ResolveSafetyNormal(step.SafetyPlane);
        return new GotoPoint
        {
            Id = $"avoid_{step.SequenceNumber}",
            X = start.X + normal.X * options.CollisionLiftClearanceMm,
            Y = start.Y + normal.Y * options.CollisionLiftClearanceMm,
            Z = start.Z + normal.Z * options.CollisionLiftClearanceMm,
            Reason = "Suggested collision avoidance point"
        };
    }

    private static double ResolveProbeRadius(MeasurementStep step)
    {
        var diameter = step.ProbeAssignment?.TipDiameter ?? 2.0;
        return double.IsFinite(diameter) && diameter > 0 ? diameter / 2.0 : 1.0;
    }

    private static (double X, double Y, double Z) ResolveSafetyNormal(ISafetyPlane? safetyPlane)
    {
        if (safetyPlane is null)
            return (0, 0, 1);

        var direction = safetyPlane.GetDirection();
        return Normalize((direction.I, direction.J, direction.K));
    }

    private static (double X, double Y, double Z) Normalize((double X, double Y, double Z) value)
    {
        var length = Math.Sqrt(value.X * value.X + value.Y * value.Y + value.Z * value.Z);
        return length < 1e-12 ? (0, 0, 1) : (value.X / length, value.Y / length, value.Z / length);
    }

    private static (double X, double Y, double Z) Add(
        (double X, double Y, double Z) left,
        (double X, double Y, double Z) right) =>
        (left.X + right.X, left.Y + right.Y, left.Z + right.Z);

    private static (double X, double Y, double Z) Scale((double X, double Y, double Z) value, double scale) =>
        (value.X * scale, value.Y * scale, value.Z * scale);

    private readonly record struct CollisionBox(
        string ElementId,
        double MinX,
        double MinY,
        double MinZ,
        double MaxX,
        double MaxY,
        double MaxZ);
}
