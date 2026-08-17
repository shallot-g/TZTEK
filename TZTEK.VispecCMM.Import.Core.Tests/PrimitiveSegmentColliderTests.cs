using TZTEK.VispecCMM.Import.Core.Planning.Collision;
using TZTEK.VispecCMM.Import.Interfaces.Models;
using Xunit;

namespace TZTEK.VispecCMM.Import.Core.Tests;

public class PrimitiveSegmentColliderTests
{
    [Fact]
    public void InnerCylinder_AllowsAxisTransitThroughHole()
    {
        var hole = CreateInnerCylinder(radius: 10, length: 20);
        var holes = HollowCollisionIndex.Build([hole]);

        Assert.False(Hits(hole, (0, 0, -30), (0, 0, 30), holes));
    }

    [Fact]
    public void InnerCylinder_DetectsWallHit()
    {
        var hole = CreateInnerCylinder(radius: 10, length: 20);

        Assert.True(Hits(hole, (0, 0, 0), (20, 0, 0)));
    }

    [Fact]
    public void OuterCylinder_TreatsInteriorAsSolid()
    {
        var outer = CreateOuterCylinder(radius: 10, length: 20);

        Assert.True(Hits(outer, (20, 0, 0), (-20, 0, 0)));
        Assert.True(Hits(outer, (0, 0, -30), (0, 0, 30)));
        Assert.False(Hits(outer, (20, 20, 0), (20, 30, 0)));
    }

    [Fact]
    public void TubeOuter_AllowsAxisTransitWhenInnerHoleExists()
    {
        var inner = CreateInnerCylinder(radius: 5, length: 20);
        var outer = CreateOuterCylinder(radius: 10, length: 20);
        var holes = HollowCollisionIndex.Build([inner, outer]);

        Assert.False(Hits(inner, (0, 0, -30), (0, 0, 30), holes));
        Assert.False(Hits(outer, (0, 0, -30), (0, 0, 30), holes));
        Assert.True(Hits(outer, (20, 0, 0), (-20, 0, 0), holes));
    }

    [Fact]
    public void Plane_DetectsCrossing_AndIgnoresHoleOpening()
    {
        var hole = CreateInnerCylinder(radius: 10, length: 20);
        var plane = new PlanePrimitive
        {
            Id = "top",
            PointX = 0,
            PointY = 0,
            PointZ = 10,
            NormalX = 0,
            NormalY = 0,
            NormalZ = 1,
            SourceAreaMm2 = 10000 * Math.PI
        };
        var holes = HollowCollisionIndex.Build([hole]);

        Assert.True(Hits(plane, (40, 0, 20), (40, 0, 0), holes));
        Assert.False(Hits(plane, (0, 0, 20), (0, 0, 0), holes));
    }

    [Fact]
    public void HoleSizedCapFace_IsNotACollider()
    {
        var hole = CreateInnerCylinder(radius: 10, length: 20);
        var cap = new PlanePrimitive
        {
            Id = "cap",
            PointX = 0,
            PointY = 0,
            PointZ = 10,
            NormalX = 0,
            NormalY = 0,
            NormalZ = 1,
            SourceAreaMm2 = Math.PI * 100
        };
        var holes = HollowCollisionIndex.Build([hole]);

        Assert.True(holes.IsOpeningFace(cap));
        Assert.False(Hits(cap, (0, 0, 20), (0, 0, 0), holes));
        Assert.False(Hits(cap, (5, 0, 20), (5, 0, 0), holes));
    }

    [Fact]
    public void SphereSurface_DetectsCrossingWithoutInflation()
    {
        var sphere = new SpherePrimitive
        {
            Id = "s",
            CenterX = 0,
            CenterY = 0,
            CenterZ = 0,
            Radius = 10
        };

        Assert.True(Hits(sphere, (-20, 0, 0), (20, 0, 0)));
        Assert.False(Hits(sphere, (20, 20, 0), (30, 20, 0)));
    }

    [Fact]
    public void EllipticalCylinderOpening_IsNotACollider()
    {
        var wall = CreateInnerEllipticalCylinder(major: 20, minor: 10, length: 20);
        var cap = new PlanePrimitive
        {
            Id = "ellipse-cap",
            PointX = 0,
            PointY = 0,
            PointZ = 10,
            NormalX = 0,
            NormalY = 0,
            NormalZ = 1,
            SourceAreaMm2 = Math.PI * 20 * 10
        };
        var holes = HollowCollisionIndex.Build([wall]);

        Assert.True(holes.IsOpeningFace(cap));
        Assert.False(Hits(cap, (0, 0, 20), (0, 0, 0), holes));
        Assert.False(Hits(cap, (5, 0, 20), (5, 0, 0), holes));
        Assert.False(Hits(wall, (0, 0, 20), (0, 0, -20), holes));
        Assert.True(Hits(wall, (0, 0, 0), (30, 0, 0), holes));
    }

    [Fact]
    public void EllipticalBSplineHole_PunchesLargeCoplanarFace()
    {
        var wall = CreateB23EllipticalHole();
        var top = new PlanePrimitive
        {
            Id = "step_face_0012",
            PointX = 56.665,
            PointY = 37.602,
            PointZ = 0,
            NormalX = 0,
            NormalY = 0,
            NormalZ = 1,
            SourceAreaMm2 = 5441.36
        };
        var holes = HollowCollisionIndex.Build([wall, top]);

        Assert.False(Hits(top, (31.5, 20, 28.67), (32.5, 20, -3), holes));
        Assert.False(Hits(top, (30, 12.5, -3), (30, 12.5, 28.67), holes));
        Assert.True(Hits(top, (56, 38, 10), (56, 38, -10), holes));
        Assert.False(Hits(wall, (31.5, 20, 28.67), (32.5, 20, -3), holes));
    }

    private static bool Hits(
        Primitive primitive,
        (double X, double Y, double Z) start,
        (double X, double Y, double Z) end,
        HollowCollisionIndex? holes = null) =>
        SegmentCollisionChecker.IntersectsPrimitive(primitive, start, end, 0, holes);

    private static CylinderPrimitive CreateInnerCylinder(double radius, double length) =>
        CreateCylinder(radius, length, inner: true);

    private static CylinderPrimitive CreateOuterCylinder(double radius, double length) =>
        CreateCylinder(radius, length, inner: false);

    private static CylinderPrimitive CreateCylinder(double radius, double length, bool inner) =>
        new()
        {
            Id = inner ? "inner" : "outer",
            AxisPointX = 0,
            AxisPointY = 0,
            AxisPointZ = 0,
            AxisDirX = 0,
            AxisDirY = 0,
            AxisDirZ = 1,
            AxisStartX = 0,
            AxisStartY = 0,
            AxisStartZ = -length / 2,
            AxisEndX = 0,
            AxisEndY = 0,
            AxisEndZ = length / 2,
            Length = length,
            Radius = radius,
            IsInnerSurface = inner
        };

    private static Surface3DPrimitive CreateInnerEllipticalCylinder(double major, double minor, double length)
    {
        const int sampleU = 16;
        const int sampleV = 3;
        var vertices = new List<(double X, double Y, double Z)>(sampleU * sampleV);
        for (var ui = 0; ui < sampleU; ui++)
        {
            var theta = ui * 2.0 * Math.PI / sampleU;
            for (var vi = 0; vi < sampleV; vi++)
            {
                var z = -length / 2 + length * vi / (sampleV - 1);
                vertices.Add((major * Math.Cos(theta), minor * Math.Sin(theta), z));
            }
        }

        return new Surface3DPrimitive
        {
            Id = "ellipse-wall",
            SurfaceType = "EXTRUSION",
            Vertices = vertices,
            IsInnerSurface = true,
            SampleU = sampleU,
            SampleV = sampleV,
            SampleClosedU = true
        };
    }

    private static Surface3DPrimitive CreateB23EllipticalHole()
    {
        const int sampleU = 5;
        const int sampleV = 5;
        var vertices = new List<(double X, double Y, double Z)>(sampleU * sampleV);
        var normals = new List<(double X, double Y, double Z)>(sampleU * sampleV);
        for (var ui = 0; ui < sampleU; ui++)
        {
            var theta = ui * 2.0 * Math.PI / sampleU;
            var nx = -Math.Cos(theta);
            var ny = -Math.Sin(theta);
            for (var vi = 0; vi < sampleV; vi++)
            {
                var z = -3.0 - 9.0 * vi / (sampleV - 1);
                vertices.Add((30 + 7.5 * Math.Cos(theta), 20 + 12.5 * Math.Sin(theta), z));
                normals.Add((nx, ny, 0));
            }
        }

        return new Surface3DPrimitive
        {
            Id = "step_face_0013",
            SurfaceType = "BSPLINE",
            Vertices = vertices,
            VertexNormals = normals,
            IsInnerSurface = false,
            SampleU = sampleU,
            SampleV = sampleV,
            SampleClosedU = true
        };
    }
}
