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

    public static bool IntersectsPrimitive(
        Primitive primitive,
        (double X, double Y, double Z) start,
        (double X, double Y, double Z) end,
        double inflation,
        HollowCollisionIndex? holes = null) =>
        PrimitiveSegmentCollider.Intersects(primitive, start, end, inflation, holes);

    public static bool IntersectsPrimitiveNarrowPhase(
        Primitive primitive,
        (double X, double Y, double Z) start,
        (double X, double Y, double Z) end,
        double margin) =>
        IntersectsPrimitive(primitive, start, end, margin);

    public static bool HitsAny(
        IEnumerable<CollisionBox> boxes,
        (double X, double Y, double Z) start,
        (double X, double Y, double Z) end,
        bool enableNarrowPhase,
        HollowCollisionIndex? holes = null)
    {
        foreach (var box in boxes)
        {
            if (!IntersectsBox(box, start, end))
                continue;

            if (enableNarrowPhase
                && box.Primitive is not null
                && !IntersectsPrimitive(box.Primitive, start, end, box.Margin, holes))
            {
                continue;
            }

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
}
