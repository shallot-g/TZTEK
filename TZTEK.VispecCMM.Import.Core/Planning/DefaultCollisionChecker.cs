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
        if (IsAboveGlobalSafeHeight(start, end, boxes, options))
        {
            return new CollisionResult
            {
                HasCollision = false,
                TotalSegmentsChecked = 1
            };
        }

        var collisions = new List<CollisionEvent>();

        foreach (var box in boxes)
        {
            if (!IntersectsSegment(box, start, end))
                continue;

            if (options.EnablePrimitiveNarrowPhaseCollisionCheck
                && box.Primitive is not null
                && !IntersectsPrimitive(box.Primitive, start, end, box.Margin))
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
                Severity = 1,
                SuggestedAvoidancePoint = new GotoPoint
                {
                    Id = $"collision_{segmentIndex}",
                    X = (start.X + end.X) / 2,
                    Y = (start.Y + end.Y) / 2,
                    Z = (start.Z + end.Z) / 2,
                    Reason = "Collision detected; use feature anchor or user GOTO point"
                }
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
            ConePrimitive cone => BuildPointBox(cone, cone.ApexX, cone.ApexY, cone.ApexZ, 10 + margin, margin),
            SpherePrimitive sphere => BuildSphereBox(sphere, margin),
            Surface3DPrimitive surface => BuildSurfaceBox(surface, margin),
            CirclePrimitive circle => BuildPointBox(circle, circle.CenterX, circle.CenterY, circle.CenterZ, circle.Radius + margin, margin),
            ArcPrimitive arc => BuildPointBox(arc, arc.CenterX, arc.CenterY, arc.CenterZ, arc.Radius + margin, margin),
            LinePrimitive line => BuildLineBox(line, margin),
            PointPrimitive point => BuildPointBox(point, point.X, point.Y, point.Z, margin, margin),
            _ => null
        };
    }

    private static CollisionBox BuildPlaneBox(PlanePrimitive plane, double margin)
    {
        var spread = Math.Max(1.0, Math.Sqrt(plane.SourceAreaMm2 ?? 100.0) * 0.5) + margin;
        return BuildPointBox(plane, plane.PointX, plane.PointY, plane.PointZ, spread, margin);
    }

    private static CollisionBox BuildCylinderBox(CylinderPrimitive cylinder, double margin)
    {
        var axis = Normalize((cylinder.AxisDirX, cylinder.AxisDirY, cylinder.AxisDirZ));
        var length = cylinder.Length
            ?? Math.Max(cylinder.Radius, Math.Sqrt(cylinder.SourceAreaMm2 ?? 0) / Math.Max(cylinder.Radius * Math.PI * 2, 1));
        var center = (cylinder.AxisPointX, cylinder.AxisPointY, cylinder.AxisPointZ);
        var p0 = Add(center, Scale(axis, -length / 2));
        var p1 = Add(center, Scale(axis, length / 2));
        var radius = cylinder.Radius + margin;

        return new CollisionBox(
            cylinder.Id,
            cylinder,
            margin,
            Math.Min(p0.X, p1.X) - radius,
            Math.Min(p0.Y, p1.Y) - radius,
            Math.Min(p0.Z, p1.Z) - radius,
            Math.Max(p0.X, p1.X) + radius,
            Math.Max(p0.Y, p1.Y) + radius,
            Math.Max(p0.Z, p1.Z) + radius);
    }

    private static CollisionBox BuildSphereBox(SpherePrimitive sphere, double margin)
    {
        return BuildPointBox(sphere, sphere.CenterX, sphere.CenterY, sphere.CenterZ, sphere.Radius + margin, margin);
    }

    private static CollisionBox? BuildSurfaceBox(Surface3DPrimitive surface, double margin)
    {
        if (surface.Vertices.Count == 0)
            return null;

        return new CollisionBox(
            surface.Id,
            surface,
            margin,
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
            line,
            margin,
            Math.Min(line.StartX, end.Item1) - margin,
            Math.Min(line.StartY, end.Item2) - margin,
            Math.Min(line.StartZ, end.Item3) - margin,
            Math.Max(line.StartX, end.Item1) + margin,
            Math.Max(line.StartY, end.Item2) + margin,
            Math.Max(line.StartZ, end.Item3) + margin);
    }

    private static CollisionBox BuildPointBox(Primitive primitive, double x, double y, double z, double radius, double margin)
    {
        return new CollisionBox(primitive.Id, primitive, margin, x - radius, y - radius, z - radius, x + radius, y + radius, z + radius);
    }

    private static bool IsAboveGlobalSafeHeight(
        (double X, double Y, double Z) start,
        (double X, double Y, double Z) end,
        IReadOnlyList<CollisionBox> boxes,
        MeasurementPlanOptions options)
    {
        if (boxes.Count == 0)
            return false;

        var safeZ = boxes.Max(box => box.MaxZ) + Math.Max(0, options.AutoSafeGotoExtraClearanceMm);
        return start.Z >= safeZ - 1e-6 && end.Z >= safeZ - 1e-6;
    }

    private static bool IntersectsPrimitive(
        Primitive primitive,
        (double X, double Y, double Z) start,
        (double X, double Y, double Z) end,
        double margin)
    {
        return primitive switch
        {
            CylinderPrimitive cylinder => IntersectsCylinder(cylinder, start, end, margin),
            SpherePrimitive sphere => IntersectsSphere(sphere, start, end, margin),
            _ => true
        };
    }

    private static bool IntersectsCylinder(
        CylinderPrimitive cylinder,
        (double X, double Y, double Z) start,
        (double X, double Y, double Z) end,
        double margin)
    {
        var axis = Normalize((cylinder.AxisDirX, cylinder.AxisDirY, cylinder.AxisDirZ));
        var center = (cylinder.AxisPointX, cylinder.AxisPointY, cylinder.AxisPointZ);
        var radius = cylinder.Radius + margin;
        var halfLength = (cylinder.Length ?? double.PositiveInfinity) / 2 + margin;
        var samples = 8;

        for (var i = 0; i <= samples; i++)
        {
            var t = (double)i / samples;
            var point = Lerp(start, end, t);
            var delta = Subtract(point, center);
            if (Math.Abs(Dot(delta, axis)) > halfLength)
                continue;
            var radial = Length(Cross(delta, axis));
            if (radial <= radius)
                return true;
        }

        return false;
    }

    private static bool IntersectsSphere(
        SpherePrimitive sphere,
        (double X, double Y, double Z) start,
        (double X, double Y, double Z) end,
        double margin)
    {
        var center = (sphere.CenterX, sphere.CenterY, sphere.CenterZ);
        var radius = sphere.Radius + margin;
        var samples = 8;

        for (var i = 0; i <= samples; i++)
        {
            var point = Lerp(start, end, (double)i / samples);
            if (Length(Subtract(point, center)) <= radius)
                return true;
        }

        return false;
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

    private static double ResolveProbeRadius(MeasurementStep step)
    {
        var diameter = step.ProbeAssignment?.TipDiameter ?? 2.0;
        return double.IsFinite(diameter) && diameter > 0 ? diameter / 2.0 : 1.0;
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

    private static (double X, double Y, double Z) Subtract(
        (double X, double Y, double Z) left,
        (double X, double Y, double Z) right) =>
        (left.X - right.X, left.Y - right.Y, left.Z - right.Z);

    private static (double X, double Y, double Z) Scale((double X, double Y, double Z) value, double scale) =>
        (value.X * scale, value.Y * scale, value.Z * scale);

    private static (double X, double Y, double Z) Lerp(
        (double X, double Y, double Z) start,
        (double X, double Y, double Z) end,
        double t) =>
        (
            start.X + (end.X - start.X) * t,
            start.Y + (end.Y - start.Y) * t,
            start.Z + (end.Z - start.Z) * t);

    private static double Length((double X, double Y, double Z) value) =>
        Math.Sqrt(value.X * value.X + value.Y * value.Y + value.Z * value.Z);

    private static double Dot(
        (double X, double Y, double Z) left,
        (double X, double Y, double Z) right) =>
        left.X * right.X + left.Y * right.Y + left.Z * right.Z;

    private static (double X, double Y, double Z) Cross(
        (double X, double Y, double Z) left,
        (double X, double Y, double Z) right) =>
        (
            left.Y * right.Z - left.Z * right.Y,
            left.Z * right.X - left.X * right.Z,
            left.X * right.Y - left.Y * right.X);

    private readonly record struct CollisionBox(
        string ElementId,
        Primitive? Primitive,
        double Margin,
        double MinX,
        double MinY,
        double MinZ,
        double MaxX,
        double MaxY,
        double MaxZ);
}
