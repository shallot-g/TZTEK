namespace TZTEK.VispecCMM.Import.Core.Planning.Collision;

internal static class SegmentCollisionChecker
{
    public static bool IntersectsBox(
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

    public static bool IntersectsPrimitiveNarrowPhase(
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
        var axis = CollisionVectors.Normalize(new CollisionVectors.Vec3(cylinder.AxisDirX, cylinder.AxisDirY, cylinder.AxisDirZ));
        var center = new CollisionVectors.Vec3(cylinder.AxisPointX, cylinder.AxisPointY, cylinder.AxisPointZ);
        var radius = cylinder.Radius + margin;
        var halfLength = (cylinder.Length ?? double.PositiveInfinity) / 2 + margin;

        for (var i = 0; i <= 8; i++)
        {
            var point = Lerp(start, end, (double)i / 8);
            var delta = CollisionVectors.Subtract(point, center);
            if (Math.Abs(CollisionVectors.Dot(delta, axis)) > halfLength)
                continue;

            if (CollisionVectors.Length(CollisionVectors.Cross(delta, axis)) <= radius)
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
        var center = new CollisionVectors.Vec3(sphere.CenterX, sphere.CenterY, sphere.CenterZ);
        var radius = sphere.Radius + margin;

        for (var i = 0; i <= 8; i++)
        {
            var point = Lerp(start, end, (double)i / 8);
            if (CollisionVectors.Distance(point, center) <= radius)
                return true;
        }

        return false;
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

    private static CollisionVectors.Vec3 Lerp(
        (double X, double Y, double Z) start,
        (double X, double Y, double Z) end,
        double t) =>
        new(
            start.X + (end.X - start.X) * t,
            start.Y + (end.Y - start.Y) * t,
            start.Z + (end.Z - start.Z) * t);
}
