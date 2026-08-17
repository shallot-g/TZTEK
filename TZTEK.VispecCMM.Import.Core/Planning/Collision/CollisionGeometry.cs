namespace TZTEK.VispecCMM.Import.Core.Planning.Collision;

internal static class CollisionGeometry
{
    public const double Epsilon = 1e-9;

    public static double FaceRadius(PlanePrimitive plane)
    {
        var area = plane.SourceAreaMm2 is > 0 ? plane.SourceAreaMm2.Value : 100.0;
        return Math.Sqrt(area / Math.PI);
    }

    public static bool TryGetCylinderAxis(
        CylinderPrimitive cylinder,
        out CollisionVectors.Vec3 start,
        out CollisionVectors.Vec3 end,
        out CollisionVectors.Vec3 axis,
        out double length)
    {
        axis = CollisionVectors.Normalize(new CollisionVectors.Vec3(cylinder.AxisDirX, cylinder.AxisDirY, cylinder.AxisDirZ));
        if (cylinder.AxisStartX is not null && cylinder.AxisEndX is not null)
        {
            start = new CollisionVectors.Vec3(cylinder.AxisStartX.Value, cylinder.AxisStartY ?? 0, cylinder.AxisStartZ ?? 0);
            end = new CollisionVectors.Vec3(cylinder.AxisEndX.Value, cylinder.AxisEndY ?? 0, cylinder.AxisEndZ ?? 0);
            var delta = CollisionVectors.Subtract(end, start);
            length = CollisionVectors.Length(delta);
            if (length >= Epsilon)
                axis = CollisionVectors.Scale(delta, 1.0 / length);
        }
        else
        {
            var center = new CollisionVectors.Vec3(cylinder.AxisPointX, cylinder.AxisPointY, cylinder.AxisPointZ);
            length = cylinder.Length is > 0 ? cylinder.Length.Value : cylinder.Radius * 2;
            var half = CollisionVectors.Scale(axis, length / 2);
            start = CollisionVectors.Subtract(center, half);
            end = CollisionVectors.Add(center, half);
        }

        return length >= Epsilon;
    }

    public static HollowPassage? TryCreateCylinderPassage(CylinderPrimitive cylinder)
    {
        if (!TryGetCylinderAxis(cylinder, out var start, out _, out var axis, out var length))
            return null;

        return new HollowPassage(start, axis, 0, length, cylinder.Radius, cylinder.Radius);
    }

    public static bool TryGetConeAxis(
        ConePrimitive cone,
        out CollisionVectors.Vec3 start,
        out CollisionVectors.Vec3 end,
        out CollisionVectors.Vec3 axis,
        out double length,
        out double radiusStart,
        out double radiusEnd)
    {
        axis = CollisionVectors.Normalize(new CollisionVectors.Vec3(cone.AxisDirX, cone.AxisDirY, cone.AxisDirZ));
        radiusStart = cone.RadiusStart ?? cone.RefRadius ?? 0;
        radiusEnd = cone.RadiusEnd ?? 0;

        if (cone.AxisStartX is not null && cone.AxisEndX is not null)
        {
            start = new CollisionVectors.Vec3(cone.AxisStartX.Value, cone.AxisStartY ?? 0, cone.AxisStartZ ?? 0);
            end = new CollisionVectors.Vec3(cone.AxisEndX.Value, cone.AxisEndY ?? 0, cone.AxisEndZ ?? 0);
            var delta = CollisionVectors.Subtract(end, start);
            length = CollisionVectors.Length(delta);
            if (length >= Epsilon)
                axis = CollisionVectors.Scale(delta, 1.0 / length);
        }
        else
        {
            var apex = new CollisionVectors.Vec3(cone.ApexX, cone.ApexY, cone.ApexZ);
            length = cone.Length is > 0 ? cone.Length.Value : 0;
            start = apex;
            end = CollisionVectors.Add(apex, CollisionVectors.Scale(axis, length));
        }

        if (radiusEnd <= Epsilon && radiusStart > Epsilon && cone.HalfAngleRad > Epsilon)
            radiusEnd = Math.Max(0, radiusStart - length * Math.Tan(cone.HalfAngleRad));

        return length >= Epsilon;
    }

    public static HollowPassage? TryCreateConePassage(ConePrimitive cone)
    {
        if (!TryGetConeAxis(cone, out var start, out _, out var axis, out var length, out var radiusStart, out var radiusEnd))
            return null;

        return new HollowPassage(start, axis, 0, length, radiusStart, radiusEnd);
    }

    public static bool IsHollowSurface(Surface3DPrimitive surface) =>
        surface.IsInnerSurface == true || LooksLikeInwardTube(surface);

    public static HollowPassage? TryCreateSurfacePassage(Surface3DPrimitive surface)
    {
        if (surface.Vertices.Count < 6)
            return null;

        if (!TryInferSurfaceAxis(surface, out var origin, out var axis, out var length, out var ring))
            return null;

        if (ring.Count < 4)
            return null;

        CollisionVectors.Vec3 majorDir = default;
        var majorRadius = 0.0;
        foreach (var point in ring)
        {
            var radial = RadialVector(point, origin, axis);
            var radius = CollisionVectors.Length(radial);
            if (radius > majorRadius)
            {
                majorRadius = radius;
                majorDir = radial;
            }
        }

        if (majorRadius < Epsilon)
            return null;

        majorDir = CollisionVectors.Normalize(majorDir);
        var minorDir = CollisionVectors.Normalize(CollisionVectors.Cross(axis, majorDir));
        var minorRadius = 0.0;
        foreach (var point in ring)
        {
            var radial = RadialVector(point, origin, axis);
            minorRadius = Math.Max(minorRadius, Math.Abs(CollisionVectors.Dot(radial, minorDir)));
        }

        if (minorRadius < Epsilon)
            minorRadius = majorRadius;

        return new HollowPassage(
            origin,
            axis,
            0,
            length,
            majorRadius,
            majorRadius,
            majorDir,
            minorRadius,
            minorRadius);
    }

    public static bool LooksLikeInwardTube(Surface3DPrimitive surface)
    {
        if (surface.Vertices.Count < 6 || surface.VertexNormals.Count != surface.Vertices.Count)
            return false;

        double cx = 0, cy = 0, cz = 0;
        foreach (var (x, y, z) in surface.Vertices)
        {
            cx += x;
            cy += y;
            cz += z;
        }

        var scale = 1.0 / surface.Vertices.Count;
        cx *= scale;
        cy *= scale;
        cz *= scale;

        var inward = 0;
        for (var index = 0; index < surface.Vertices.Count; index++)
        {
            var vertex = surface.Vertices[index];
            var normal = surface.VertexNormals[index];
            var dot =
                (vertex.X - cx) * normal.X
                + (vertex.Y - cy) * normal.Y
                + (vertex.Z - cz) * normal.Z;
            if (dot < 0)
                inward++;
        }

        return inward >= 0.8 * surface.Vertices.Count;
    }

    private static CollisionVectors.Vec3 RadialVector(
        CollisionVectors.Vec3 point,
        CollisionVectors.Vec3 origin,
        CollisionVectors.Vec3 axis)
    {
        var delta = CollisionVectors.Subtract(point, origin);
        return CollisionVectors.Subtract(delta, CollisionVectors.Scale(axis, CollisionVectors.Dot(delta, axis)));
    }

    private static bool TryInferSurfaceAxis(
        Surface3DPrimitive surface,
        out CollisionVectors.Vec3 origin,
        out CollisionVectors.Vec3 axis,
        out double length,
        out List<CollisionVectors.Vec3> ring)
    {
        origin = default;
        axis = default;
        length = 0;
        ring = [];

        var sampleU = surface.SampleU ?? 0;
        var sampleV = surface.SampleV ?? 0;
        if (sampleU < 3 || sampleV < 2 || surface.Vertices.Count != sampleU * sampleV)
            return false;

        origin = RingCentroid(surface, sampleU, sampleV, 0);
        var end = RingCentroid(surface, sampleU, sampleV, sampleV - 1);
        var delta = CollisionVectors.Subtract(end, origin);
        length = CollisionVectors.Length(delta);
        if (length < Epsilon)
            return false;

        axis = CollisionVectors.Scale(delta, 1.0 / length);
        ring = new List<CollisionVectors.Vec3>(sampleU);
        for (var ui = 0; ui < sampleU; ui++)
            ring.Add(ToVec(surface.Vertices[ui * sampleV]));

        return true;
    }

    private static CollisionVectors.Vec3 RingCentroid(
        Surface3DPrimitive surface,
        int sampleU,
        int sampleV,
        int vi)
    {
        var sum = new CollisionVectors.Vec3();
        for (var ui = 0; ui < sampleU; ui++)
            sum = CollisionVectors.Add(sum, ToVec(surface.Vertices[ui * sampleV + vi]));

        return CollisionVectors.Scale(sum, 1.0 / sampleU);
    }

    private static CollisionVectors.Vec3 ToVec((double X, double Y, double Z) value) =>
        new(value.X, value.Y, value.Z);

    public static double RadialDistance(
        CollisionVectors.Vec3 point,
        CollisionVectors.Vec3 origin,
        CollisionVectors.Vec3 axis)
    {
        var delta = CollisionVectors.Subtract(point, origin);
        return CollisionVectors.Length(CollisionVectors.Cross(delta, axis));
    }

    public static double AxialCoordinate(
        CollisionVectors.Vec3 point,
        CollisionVectors.Vec3 origin,
        CollisionVectors.Vec3 axis) =>
        CollisionVectors.Dot(CollisionVectors.Subtract(point, origin), axis);

    public static bool IsWithinAngularSpan(
        CollisionVectors.Vec3 point,
        CollisionVectors.Vec3 origin,
        CollisionVectors.Vec3 axis,
        double? startAngleRad,
        double? endAngleRad,
        double? angularSpanRad,
        CollisionVectors.Vec3? radialReference)
    {
        if (angularSpanRad is null || angularSpanRad.Value >= Math.PI * 2 - 1e-3)
            return true;
        if (radialReference is null || CollisionVectors.Length(radialReference.Value) < Epsilon)
            return true;

        var radial = CollisionVectors.Subtract(point, origin);
        radial = CollisionVectors.Subtract(radial, CollisionVectors.Scale(axis, CollisionVectors.Dot(radial, axis)));
        if (CollisionVectors.Length(radial) < Epsilon)
            return true;

        var reference = CollisionVectors.Subtract(
            radialReference.Value,
            CollisionVectors.Scale(axis, CollisionVectors.Dot(radialReference.Value, axis)));
        reference = CollisionVectors.Normalize(reference);
        radial = CollisionVectors.Normalize(radial);

        var cos = CollisionVectors.Dot(reference, radial);
        var sin = CollisionVectors.Dot(axis, CollisionVectors.Cross(reference, radial));
        var angle = Math.Atan2(sin, cos);
        if (angle < 0)
            angle += Math.PI * 2;

        var start = startAngleRad ?? 0;
        var end = endAngleRad ?? (start + angularSpanRad.Value);
        if (end < start)
            end += Math.PI * 2;
        if (angle < start)
            angle += Math.PI * 2;

        return angle >= start - 1e-6 && angle <= end + 1e-6;
    }

    public static bool IsPartialFace(double? angularSpanRad) =>
        angularSpanRad is not null && angularSpanRad.Value < Math.PI * 2 - 1e-3;

    public static bool SegmentHitsFiniteSolid(
        CollisionVectors.Vec3 start,
        CollisionVectors.Vec3 end,
        CollisionVectors.Vec3 origin,
        CollisionVectors.Vec3 axis,
        double length,
        Func<double, double> radiusAt,
        double inflation,
        Func<CollisionVectors.Vec3, bool>? isOnFace = null)
    {
        if (PointInsideSolid(start, origin, axis, length, radiusAt, inflation, isOnFace)
            || PointInsideSolid(end, origin, axis, length, radiusAt, inflation, isOnFace))
        {
            return true;
        }

        if (SegmentHitsFiniteWall(start, end, origin, axis, length, radiusAt, inflation, isOnFace))
            return true;

        var startCap = origin;
        var endCap = CollisionVectors.Add(origin, CollisionVectors.Scale(axis, length));
        var startRadius = Math.Max(0, radiusAt(0) + inflation);
        var endRadius = Math.Max(0, radiusAt(length) + inflation);

        return SegmentHitsInfinitePlaneDisk(start, end, startCap, axis, startRadius, inflation, isOnFace)
            || SegmentHitsInfinitePlaneDisk(start, end, endCap, axis, endRadius, inflation, isOnFace);
    }

    public static bool SegmentHitsFiniteWall(
        CollisionVectors.Vec3 start,
        CollisionVectors.Vec3 end,
        CollisionVectors.Vec3 origin,
        CollisionVectors.Vec3 axis,
        double length,
        Func<double, double> radiusAt,
        double inflation,
        Func<CollisionVectors.Vec3, bool>? isOnFace = null)
    {
        var direction = CollisionVectors.Subtract(end, start);
        var hits = CollectWallHits(start, direction, origin, axis, length, radiusAt, inflation);
        foreach (var t in hits)
        {
            var point = CollisionVectors.Lerp(start, end, t);
            if (isOnFace is null || isOnFace(point))
                return true;
        }

        if (inflation <= Epsilon)
            return false;

        return PointNearWall(start, origin, axis, length, radiusAt, inflation, isOnFace)
            || PointNearWall(end, origin, axis, length, radiusAt, inflation, isOnFace);
    }

    public static bool SegmentHitsInfinitePlaneDisk(
        CollisionVectors.Vec3 start,
        CollisionVectors.Vec3 end,
        CollisionVectors.Vec3 planePoint,
        CollisionVectors.Vec3 planeNormal,
        double diskRadius,
        double inflation,
        Func<CollisionVectors.Vec3, bool>? isMaterial = null)
    {
        var direction = CollisionVectors.Subtract(end, start);
        var denom = CollisionVectors.Dot(planeNormal, direction);
        var startOffset = CollisionVectors.Dot(planeNormal, CollisionVectors.Subtract(start, planePoint));

        if (Math.Abs(denom) < Epsilon)
        {
            if (Math.Abs(startOffset) > inflation + Epsilon)
                return false;

            return DiskContainsSegment(start, end, planePoint, planeNormal, diskRadius, isMaterial);
        }

        var t = -startOffset / denom;
        if (t < -Epsilon || t > 1 + Epsilon)
            return false;

        t = CollisionVectors.Clamp(t, 0, 1);
        var hit = CollisionVectors.Lerp(start, end, t);
        var radial = RadialDistance(hit, planePoint, planeNormal);
        if (radial > diskRadius + inflation + Epsilon)
            return false;

        return isMaterial is null || isMaterial(hit);
    }

    public static bool SegmentHitsSphereSurface(
        CollisionVectors.Vec3 start,
        CollisionVectors.Vec3 end,
        CollisionVectors.Vec3 center,
        double radius,
        double inflation,
        bool solid)
    {
        var effective = Math.Max(0, solid ? radius + inflation : radius - inflation);
        var direction = CollisionVectors.Subtract(end, start);
        var offset = CollisionVectors.Subtract(start, center);
        var a = CollisionVectors.LengthSquared(direction);
        var b = 2 * CollisionVectors.Dot(offset, direction);
        var c = CollisionVectors.LengthSquared(offset) - effective * effective;

        if (solid)
        {
            if (CollisionVectors.LengthSquared(offset) <= effective * effective + Epsilon)
                return true;
            var endOffset = CollisionVectors.Subtract(end, center);
            if (CollisionVectors.LengthSquared(endOffset) <= effective * effective + Epsilon)
                return true;
        }

        if (a < Epsilon)
            return Math.Abs(CollisionVectors.Length(offset) - effective) <= inflation + Epsilon;

        var discriminant = b * b - 4 * a * c;
        if (discriminant < -Epsilon)
            return false;

        discriminant = Math.Max(0, discriminant);
        var root = Math.Sqrt(discriminant);
        var t1 = (-b - root) / (2 * a);
        var t2 = (-b + root) / (2 * a);
        return IsOnSegment(t1) || IsOnSegment(t2);
    }

    public static bool SegmentHitsTriangle(
        CollisionVectors.Vec3 start,
        CollisionVectors.Vec3 end,
        CollisionVectors.Vec3 v0,
        CollisionVectors.Vec3 v1,
        CollisionVectors.Vec3 v2)
    {
        var direction = CollisionVectors.Subtract(end, start);
        var edge1 = CollisionVectors.Subtract(v1, v0);
        var edge2 = CollisionVectors.Subtract(v2, v0);
        var pvec = CollisionVectors.Cross(direction, edge2);
        var det = CollisionVectors.Dot(edge1, pvec);
        if (Math.Abs(det) < Epsilon)
            return false;

        var inv = 1.0 / det;
        var tvec = CollisionVectors.Subtract(start, v0);
        var u = CollisionVectors.Dot(tvec, pvec) * inv;
        if (u < -Epsilon || u > 1 + Epsilon)
            return false;

        var qvec = CollisionVectors.Cross(tvec, edge1);
        var v = CollisionVectors.Dot(direction, qvec) * inv;
        if (v < -Epsilon || u + v > 1 + Epsilon)
            return false;

        var t = CollisionVectors.Dot(edge2, qvec) * inv;
        return IsOnSegment(t);
    }

    private static List<double> CollectWallHits(
        CollisionVectors.Vec3 start,
        CollisionVectors.Vec3 direction,
        CollisionVectors.Vec3 origin,
        CollisionVectors.Vec3 axis,
        double length,
        Func<double, double> radiusAt,
        double inflation)
    {
        var hits = new List<double>();
        CollectRadiusHits(start, direction, origin, axis, length, axial => radiusAt(axial) + inflation, hits);
        if (inflation > Epsilon)
            CollectRadiusHits(start, direction, origin, axis, length, axial => Math.Max(0, radiusAt(axial) - inflation), hits);
        return hits;
    }

    private static void CollectRadiusHits(
        CollisionVectors.Vec3 start,
        CollisionVectors.Vec3 direction,
        CollisionVectors.Vec3 origin,
        CollisionVectors.Vec3 axis,
        double length,
        Func<double, double> radiusAt,
        List<double> hits)
    {
        // radius(axial) is linear: r0 + k * axial, with axial = (P-origin)·axis.
        var r0 = radiusAt(0);
        var r1 = radiusAt(length);
        var k = (r1 - r0) / length;

        var m = CollisionVectors.Subtract(start, origin);
        var aDir = CollisionVectors.LengthSquared(direction) - Square(CollisionVectors.Dot(direction, axis));
        var bDir = 2 * (CollisionVectors.Dot(m, direction) - CollisionVectors.Dot(m, axis) * CollisionVectors.Dot(direction, axis));
        var cDir = CollisionVectors.LengthSquared(m) - Square(CollisionVectors.Dot(m, axis));

        var alpha = CollisionVectors.Dot(m, axis);
        var beta = CollisionVectors.Dot(direction, axis);

        // radial^2(t) = a t^2 + b t + cDir  (here aDir, bDir, cDir)
        // rhs^2(t) = (r0 + k (alpha + t beta))^2
        var p = r0 + k * alpha;
        var q = k * beta;
        var a = aDir - q * q;
        var b = bDir - 2 * p * q;
        var c = cDir - p * p;

        if (Math.Abs(a) < Epsilon && Math.Abs(b) < Epsilon && Math.Abs(c) < Epsilon)
        {
            var startAxial = AxialCoordinate(start, origin, axis);
            var endPoint = CollisionVectors.Add(start, direction);
            var endAxial = AxialCoordinate(endPoint, origin, axis);
            if (startAxial >= -Epsilon && startAxial <= length + Epsilon)
                hits.Add(0);
            if (endAxial >= -Epsilon && endAxial <= length + Epsilon)
                hits.Add(1);
            return;
        }

        foreach (var t in SolveQuadratic(a, b, c))
        {
            if (!IsOnSegment(t))
                continue;

            var point = CollisionVectors.Add(start, CollisionVectors.Scale(direction, t));
            var axial = AxialCoordinate(point, origin, axis);
            if (axial >= -Epsilon && axial <= length + Epsilon)
                hits.Add(t);
        }
    }

    private static bool PointInsideSolid(
        CollisionVectors.Vec3 point,
        CollisionVectors.Vec3 origin,
        CollisionVectors.Vec3 axis,
        double length,
        Func<double, double> radiusAt,
        double inflation,
        Func<CollisionVectors.Vec3, bool>? isOnFace)
    {
        var axial = AxialCoordinate(point, origin, axis);
        if (axial < -inflation - Epsilon || axial > length + inflation + Epsilon)
            return false;

        var radial = RadialDistance(point, origin, axis);
        if (radial > radiusAt(CollisionVectors.Clamp(axial, 0, length)) + inflation + Epsilon)
            return false;

        return isOnFace is null || isOnFace(point);
    }

    private static bool PointNearWall(
        CollisionVectors.Vec3 point,
        CollisionVectors.Vec3 origin,
        CollisionVectors.Vec3 axis,
        double length,
        Func<double, double> radiusAt,
        double inflation,
        Func<CollisionVectors.Vec3, bool>? isOnFace)
    {
        var axial = AxialCoordinate(point, origin, axis);
        if (axial < -Epsilon || axial > length + Epsilon)
            return false;

        var radial = RadialDistance(point, origin, axis);
        if (Math.Abs(radial - radiusAt(axial)) > inflation + Epsilon)
            return false;

        return isOnFace is null || isOnFace(point);
    }

    private static bool DiskContainsSegment(
        CollisionVectors.Vec3 start,
        CollisionVectors.Vec3 end,
        CollisionVectors.Vec3 planePoint,
        CollisionVectors.Vec3 planeNormal,
        double diskRadius,
        Func<CollisionVectors.Vec3, bool>? isMaterial)
    {
        var closest = ClosestPointOnSegment(start, end, planePoint);
        closest = ProjectToPlane(closest, planePoint, planeNormal);
        if (RadialDistance(closest, planePoint, planeNormal) > diskRadius + Epsilon)
        {
            return PointInDisk(start, planePoint, planeNormal, diskRadius, isMaterial)
                || PointInDisk(end, planePoint, planeNormal, diskRadius, isMaterial);
        }

        return isMaterial is null || isMaterial(closest);
    }

    private static bool PointInDisk(
        CollisionVectors.Vec3 point,
        CollisionVectors.Vec3 planePoint,
        CollisionVectors.Vec3 planeNormal,
        double diskRadius,
        Func<CollisionVectors.Vec3, bool>? isMaterial)
    {
        var projected = ProjectToPlane(point, planePoint, planeNormal);
        if (RadialDistance(projected, planePoint, planeNormal) > diskRadius + Epsilon)
            return false;
        return isMaterial is null || isMaterial(projected);
    }

    private static CollisionVectors.Vec3 ProjectToPlane(
        CollisionVectors.Vec3 point,
        CollisionVectors.Vec3 planePoint,
        CollisionVectors.Vec3 planeNormal)
    {
        var offset = CollisionVectors.Dot(planeNormal, CollisionVectors.Subtract(point, planePoint));
        return CollisionVectors.Subtract(point, CollisionVectors.Scale(planeNormal, offset));
    }

    private static CollisionVectors.Vec3 ClosestPointOnSegment(
        CollisionVectors.Vec3 start,
        CollisionVectors.Vec3 end,
        CollisionVectors.Vec3 point)
    {
        var direction = CollisionVectors.Subtract(end, start);
        var lengthSquared = CollisionVectors.LengthSquared(direction);
        if (lengthSquared < Epsilon)
            return start;

        var t = CollisionVectors.Clamp(
            CollisionVectors.Dot(CollisionVectors.Subtract(point, start), direction) / lengthSquared,
            0,
            1);
        return CollisionVectors.Lerp(start, end, t);
    }

    private static IEnumerable<double> SolveQuadratic(double a, double b, double c)
    {
        if (Math.Abs(a) < Epsilon)
        {
            if (Math.Abs(b) < Epsilon)
                yield break;
            yield return -c / b;
            yield break;
        }

        var discriminant = b * b - 4 * a * c;
        if (discriminant < -Epsilon)
            yield break;

        discriminant = Math.Max(0, discriminant);
        var root = Math.Sqrt(discriminant);
        yield return (-b - root) / (2 * a);
        yield return (-b + root) / (2 * a);
    }

    private static bool IsOnSegment(double t) => t >= -Epsilon && t <= 1 + Epsilon;

    private static double Square(double value) => value * value;
}
