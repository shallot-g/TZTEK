namespace TZTEK.VispecCMM.Import.Core.Planning.Collision;

internal static class PrimitiveSegmentCollider
{
    public static bool Intersects(
        Primitive primitive,
        (double X, double Y, double Z) start,
        (double X, double Y, double Z) end,
        double inflation,
        HollowCollisionIndex? holes)
    {
        var a = (CollisionVectors.Vec3)start;
        var b = (CollisionVectors.Vec3)end;
        inflation = Math.Max(0, inflation);
        holes ??= HollowCollisionIndex.Empty;

        return primitive switch
        {
            PlanePrimitive plane => IntersectsPlane(plane, a, b, inflation, holes),
            CylinderPrimitive cylinder => IntersectsCylinder(cylinder, a, b, inflation, holes),
            ConePrimitive cone => IntersectsCone(cone, a, b, inflation, holes),
            SpherePrimitive sphere => IntersectsSphere(sphere, a, b, inflation),
            Surface3DPrimitive surface => IntersectsSurface(surface, a, b),
            _ => false
        };
    }

    private static bool IntersectsPlane(
        PlanePrimitive plane,
        CollisionVectors.Vec3 start,
        CollisionVectors.Vec3 end,
        double inflation,
        HollowCollisionIndex holes)
    {
        if (holes.IsOpeningFace(plane))
            return false;

        var origin = new CollisionVectors.Vec3(plane.PointX, plane.PointY, plane.PointZ);
        var normal = CollisionVectors.Normalize(new CollisionVectors.Vec3(plane.NormalX, plane.NormalY, plane.NormalZ));
        var radius = CollisionGeometry.FaceRadius(plane);

        return CollisionGeometry.SegmentHitsInfinitePlaneDisk(
            start,
            end,
            origin,
            normal,
            radius,
            inflation,
            point => !holes.IsInsideHole(plane, point));
    }

    private static bool IntersectsCylinder(
        CylinderPrimitive cylinder,
        CollisionVectors.Vec3 start,
        CollisionVectors.Vec3 end,
        double inflation,
        HollowCollisionIndex holes)
    {
        if (!CollisionGeometry.TryGetCylinderAxis(cylinder, out var origin, out _, out var axis, out var length))
            return false;

        if (cylinder.Radius <= CollisionGeometry.Epsilon)
            return false;

        CollisionVectors.Vec3? radialReference = cylinder.RadialReferenceX is not null
            ? new CollisionVectors.Vec3(
                cylinder.RadialReferenceX.Value,
                cylinder.RadialReferenceY ?? 0,
                cylinder.RadialReferenceZ ?? 0)
            : null;

        bool OnFace(CollisionVectors.Vec3 point) =>
            CollisionGeometry.IsWithinAngularSpan(
                point,
                origin,
                axis,
                cylinder.StartAngleRad,
                cylinder.EndAngleRad,
                cylinder.AngularSpanRad,
                radialReference);

        var treatAsSolid = cylinder.IsInnerSurface != true
            && !CollisionGeometry.IsPartialFace(cylinder.AngularSpanRad)
            && !holes.IsHollowedOuter(origin, axis, length, cylinder.Radius);

        if (treatAsSolid)
        {
            return CollisionGeometry.SegmentHitsFiniteSolid(
                start,
                end,
                origin,
                axis,
                length,
                _ => cylinder.Radius,
                inflation,
                OnFace);
        }

        return CollisionGeometry.SegmentHitsFiniteWall(
            start,
            end,
            origin,
            axis,
            length,
            _ => cylinder.Radius,
            inflation,
            OnFace);
    }

    private static bool IntersectsCone(
        ConePrimitive cone,
        CollisionVectors.Vec3 start,
        CollisionVectors.Vec3 end,
        double inflation,
        HollowCollisionIndex holes)
    {
        if (!CollisionGeometry.TryGetConeAxis(
                cone,
                out var origin,
                out _,
                out var axis,
                out var length,
                out var radiusStart,
                out var radiusEnd))
            return false;

        double RadiusAt(double axial)
        {
            var t = length < CollisionGeometry.Epsilon ? 0 : axial / length;
            return radiusStart + (radiusEnd - radiusStart) * CollisionVectors.Clamp(t, 0, 1);
        }

        var maxRadius = Math.Max(radiusStart, radiusEnd);
        var treatAsSolid = cone.IsInnerSurface != true
            && !CollisionGeometry.IsPartialFace(cone.AngularSpanRad)
            && !holes.IsHollowedOuter(origin, axis, length, maxRadius);

        if (treatAsSolid)
        {
            return CollisionGeometry.SegmentHitsFiniteSolid(
                start,
                end,
                origin,
                axis,
                length,
                RadiusAt,
                inflation);
        }

        return CollisionGeometry.SegmentHitsFiniteWall(
            start,
            end,
            origin,
            axis,
            length,
            RadiusAt,
            inflation);
    }

    private static bool IntersectsSphere(
        SpherePrimitive sphere,
        CollisionVectors.Vec3 start,
        CollisionVectors.Vec3 end,
        double inflation)
    {
        var center = new CollisionVectors.Vec3(sphere.CenterX, sphere.CenterY, sphere.CenterZ);
        return CollisionGeometry.SegmentHitsSphereSurface(
            start,
            end,
            center,
            sphere.Radius,
            inflation,
            solid: false);
    }

    private static bool IntersectsSurface(
        Surface3DPrimitive surface,
        CollisionVectors.Vec3 start,
        CollisionVectors.Vec3 end)
    {
        if (surface.Vertices.Count < 3)
            return !CollisionGeometry.IsHollowSurface(surface);

        var triangles = surface.Triangles.Count > 0
            ? surface.Triangles
            : BuildGridTriangles(surface);

        if (triangles.Count == 0)
            return !CollisionGeometry.IsHollowSurface(surface);

        foreach (var (v0, v1, v2) in triangles)
        {
            if (v0 < 0 || v1 < 0 || v2 < 0
                || v0 >= surface.Vertices.Count
                || v1 >= surface.Vertices.Count
                || v2 >= surface.Vertices.Count)
                continue;

            var a = ToVec(surface.Vertices[v0]);
            var b = ToVec(surface.Vertices[v1]);
            var c = ToVec(surface.Vertices[v2]);
            if (CollisionGeometry.SegmentHitsTriangle(start, end, a, b, c))
                return true;
        }

        return false;
    }

    private static List<(int V0, int V1, int V2)> BuildGridTriangles(Surface3DPrimitive surface)
    {
        var sampleU = surface.SampleU ?? 0;
        var sampleV = surface.SampleV ?? 0;
        if (sampleU < 2 || sampleV < 2 || surface.Vertices.Count != sampleU * sampleV)
            return [];

        var closedU = surface.SampleClosedU == true || IsClosedUSurface(surface.SurfaceType);
        var triangles = new List<(int V0, int V1, int V2)>(sampleU * (sampleV - 1) * 2);
        var uCount = closedU ? sampleU : sampleU - 1;
        for (var ui = 0; ui < uCount; ui++)
        {
            var uNext = closedU ? (ui + 1) % sampleU : ui + 1;
            for (var vi = 0; vi < sampleV - 1; vi++)
            {
                var i00 = ui * sampleV + vi;
                var i10 = uNext * sampleV + vi;
                var i01 = ui * sampleV + vi + 1;
                var i11 = uNext * sampleV + vi + 1;
                triangles.Add((i00, i10, i11));
                triangles.Add((i00, i11, i01));
            }
        }

        return triangles;
    }

    private static bool IsClosedUSurface(string? surfaceType) =>
        surfaceType is "EXTRUSION" or "REVOLUTION" or "TORUS";

    private static CollisionVectors.Vec3 ToVec((double X, double Y, double Z) value) =>
        new(value.X, value.Y, value.Z);
}
