namespace TZTEK.VispecCMM.Import.Core.Planning.Collision;

/// <summary>
/// 镂空通道（内孔/内锥）索引：开口面不碰撞，平面被孔洞穿过的区域也不碰撞。
/// </summary>
internal sealed class HollowCollisionIndex
{
    internal const double CoplanarToleranceMm = 0.05;
    private const double AxisAlignment = 0.95;
    private const double OpeningRadiusSlack = 1.2;

    private readonly IReadOnlyList<HollowPassage> _passages;

    private HollowCollisionIndex(IReadOnlyList<HollowPassage> passages)
    {
        _passages = passages;
    }

    public static HollowCollisionIndex Empty { get; } = new([]);

    public static HollowCollisionIndex Build(IEnumerable<Primitive> primitives)
    {
        var passages = new List<HollowPassage>();
        foreach (var primitive in primitives)
        {
            if (TryCreate(primitive) is { } passage)
                passages.Add(passage);
        }

        return passages.Count == 0 ? Empty : new HollowCollisionIndex(passages);
    }

    public bool IsOpeningFace(PlanePrimitive plane)
    {
        if (_passages.Count == 0)
            return false;

        var origin = new CollisionVectors.Vec3(plane.PointX, plane.PointY, plane.PointZ);
        var normal = CollisionVectors.Normalize(new CollisionVectors.Vec3(plane.NormalX, plane.NormalY, plane.NormalZ));
        var faceRadius = CollisionGeometry.FaceRadius(plane);

        foreach (var passage in _passages)
        {
            if (Math.Abs(CollisionVectors.Dot(normal, passage.Axis)) < AxisAlignment)
                continue;

            if (!TryIntersectAxis(origin, normal, passage, out var axial, out var radiusAtPlane))
                continue;

            if (!IsNearPassageEnd(passage, axial))
                continue;

            var axisPoint = CollisionVectors.Add(passage.Origin, CollisionVectors.Scale(passage.Axis, axial));
            if (CollisionVectors.Distance(origin, axisPoint) > radiusAtPlane + CoplanarToleranceMm)
                continue;

            if (faceRadius <= radiusAtPlane * OpeningRadiusSlack + CoplanarToleranceMm
                && IsHoleSizedFace(plane, passage, axial))
                return true;
        }

        return false;
    }

    public bool IsInsideHole(PlanePrimitive plane, CollisionVectors.Vec3 point)
    {
        if (_passages.Count == 0)
            return false;

        var origin = new CollisionVectors.Vec3(plane.PointX, plane.PointY, plane.PointZ);
        var normal = CollisionVectors.Normalize(new CollisionVectors.Vec3(plane.NormalX, plane.NormalY, plane.NormalZ));

        foreach (var passage in _passages)
        {
            if (Math.Abs(CollisionVectors.Dot(normal, passage.Axis)) < AxisAlignment)
                continue;

            if (!TryIntersectAxis(origin, normal, passage, out var axial, out var radiusAtPlane, HolePunchSlack(passage)))
                continue;

            if (passage.ContainsRadial(point, axial, radiusAtPlane))
                return true;
        }

        return false;
    }

    public bool IsHollowedOuter(
        CollisionVectors.Vec3 origin,
        CollisionVectors.Vec3 axis,
        double length,
        double outerRadius)
    {
        foreach (var passage in _passages)
        {
            if (Math.Abs(CollisionVectors.Dot(axis, passage.Axis)) < AxisAlignment)
                continue;

            var delta = CollisionVectors.Subtract(passage.Origin, origin);
            var axisDistance = CollisionVectors.Length(CollisionVectors.Cross(delta, axis));
            if (axisDistance > CoplanarToleranceMm)
                continue;

            var innerRadius = Math.Max(passage.RadiusStart, passage.RadiusEnd);
            if (innerRadius >= outerRadius - 1e-6)
                continue;

            var passageStart = CollisionGeometry.AxialCoordinate(
                CollisionVectors.Add(passage.Origin, CollisionVectors.Scale(passage.Axis, passage.AxialStart)),
                origin,
                axis);
            var passageEnd = CollisionGeometry.AxialCoordinate(
                CollisionVectors.Add(passage.Origin, CollisionVectors.Scale(passage.Axis, passage.AxialEnd)),
                origin,
                axis);
            var overlapStart = Math.Max(0, Math.Min(passageStart, passageEnd));
            var overlapEnd = Math.Min(length, Math.Max(passageStart, passageEnd));
            if (overlapEnd - overlapStart > CoplanarToleranceMm)
                return true;
        }

        return false;
    }

    private static HollowPassage? TryCreate(Primitive primitive) =>
        primitive switch
        {
            CylinderPrimitive cylinder when cylinder.IsInnerSurface == true =>
                CollisionGeometry.TryCreateCylinderPassage(cylinder),
            ConePrimitive cone when cone.IsInnerSurface == true =>
                CollisionGeometry.TryCreateConePassage(cone),
            Surface3DPrimitive surface when CollisionGeometry.IsHollowSurface(surface) =>
                CollisionGeometry.TryCreateSurfacePassage(surface),
            _ => null
        };

    private static bool TryIntersectAxis(
        CollisionVectors.Vec3 planePoint,
        CollisionVectors.Vec3 planeNormal,
        HollowPassage passage,
        out double axial,
        out double radiusAtPlane,
        double axialSlack = CoplanarToleranceMm)
    {
        axial = 0;
        radiusAtPlane = 0;

        var denom = CollisionVectors.Dot(planeNormal, passage.Axis);
        if (Math.Abs(denom) < 1e-12)
            return false;

        axial = CollisionVectors.Dot(planeNormal, CollisionVectors.Subtract(planePoint, passage.Origin)) / denom;
        if (axial < passage.AxialStart - axialSlack || axial > passage.AxialEnd + axialSlack)
            return false;

        radiusAtPlane = passage.RadiusAt(axial);
        return radiusAtPlane > 0;
    }

    private static double HolePunchSlack(HollowPassage passage) =>
        Math.Max(2.0, 0.5 * Math.Abs(passage.AxialEnd - passage.AxialStart));

    private static bool IsHoleSizedFace(PlanePrimitive plane, HollowPassage passage, double axial)
    {
        var holeArea = passage.OpeningAreaAt(axial);
        var faceArea = plane.SourceAreaMm2 is > 0
            ? plane.SourceAreaMm2.Value
            : Math.PI * CollisionGeometry.FaceRadius(plane) * CollisionGeometry.FaceRadius(plane);
        if (holeArea < 1e-12)
            return false;

        return Math.Abs(faceArea - holeArea) / holeArea <= 0.15;
    }

    private static bool IsNearPassageEnd(HollowPassage passage, double axial)
    {
        var toStart = Math.Abs(axial - passage.AxialStart);
        var toEnd = Math.Abs(axial - passage.AxialEnd);
        return Math.Min(toStart, toEnd) <= CoplanarToleranceMm;
    }
}

internal readonly record struct HollowPassage(
    CollisionVectors.Vec3 Origin,
    CollisionVectors.Vec3 Axis,
    double AxialStart,
    double AxialEnd,
    double RadiusStart,
    double RadiusEnd,
    CollisionVectors.Vec3 MajorDir = default,
    double MinorRadiusStart = 0,
    double MinorRadiusEnd = 0)
{
    public bool IsElliptical =>
        CollisionVectors.LengthSquared(MajorDir) > 0.25
        && Math.Max(MinorRadiusStart, MinorRadiusEnd) > CollisionGeometry.Epsilon;

    public double RadiusAt(double axial) => LerpRadius(RadiusStart, RadiusEnd, axial);

    public double MinorRadiusAt(double axial)
    {
        var minor = LerpRadius(MinorRadiusStart, MinorRadiusEnd, axial);
        return minor > CollisionGeometry.Epsilon ? minor : RadiusAt(axial);
    }

    public double OpeningAreaAt(double axial) =>
        Math.PI * RadiusAt(axial) * MinorRadiusAt(axial);

    public bool ContainsRadial(CollisionVectors.Vec3 point, double axial, double majorRadius)
    {
        if (majorRadius <= CollisionGeometry.Epsilon)
            return false;

        if (!IsElliptical)
            return CollisionGeometry.RadialDistance(point, Origin, Axis) <= majorRadius + 1e-9;

        var delta = CollisionVectors.Subtract(point, Origin);
        var radial = CollisionVectors.Subtract(delta, CollisionVectors.Scale(Axis, CollisionVectors.Dot(delta, Axis)));
        var major = CollisionVectors.Normalize(MajorDir);
        var minor = CollisionVectors.Normalize(CollisionVectors.Cross(Axis, major));
        var x = CollisionVectors.Dot(radial, major) / majorRadius;
        var y = CollisionVectors.Dot(radial, minor) / MinorRadiusAt(axial);
        return x * x + y * y <= 1 + 1e-9;
    }

    private double LerpRadius(double start, double end, double axial)
    {
        var length = AxialEnd - AxialStart;
        if (Math.Abs(length) < 1e-12)
            return Math.Max(start, end);

        var t = CollisionVectors.Clamp((axial - AxialStart) / length, 0, 1);
        return start + (end - start) * t;
    }
}
