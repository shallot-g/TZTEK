using TZTEK.VispecCMM.Import.Interfaces.Interfaces;

namespace TZTEK.VispecCMM.Import.Interfaces.Models;

/// <summary>
/// 几何基元抽象基类，实现 <see cref="IPrimitive"/>。
/// </summary>
public abstract class Primitive : IPrimitive
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public ElementType ElementType => ElementType.Primitive;
    public abstract PrimitiveType PrimitiveType { get; }
    public string SourceElementId { get; set; } = string.Empty;
    public string? CoordSystemId { get; set; }
    public string? AssignedProbeId { get; set; }
    public double? SourceAreaMm2 { get; set; }

    public (double X, double Y, double Z) GetPosition() => GetRepresentativePoint();

    public abstract (double X, double Y, double Z) GetRepresentativePoint();
    public abstract (double I, double J, double K) GetDirection();
    public abstract IReadOnlyList<MeasurementPoint> PlanPoints();

    protected static (double I, double J, double K) Normalize(double x, double y, double z)
    {
        double length = Math.Sqrt(x * x + y * y + z * z);
        if (length < 1e-12)
            return (0, 0, 1);

        return (x / length, y / length, z / length);
    }
}

/// <summary>点基元</summary>
public sealed class PointPrimitive : Primitive, IPointPrimitive
{
    public override PrimitiveType PrimitiveType => PrimitiveType.Point;
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }

    public override (double X, double Y, double Z) GetRepresentativePoint() => (X, Y, Z);
    public override (double I, double J, double K) GetDirection() => (0, 0, 1);
    public override IReadOnlyList<MeasurementPoint> PlanPoints() =>
        [new MeasurementPoint { Index = 1, X = X, Y = Y, Z = Z }];
}

/// <summary>线基元</summary>
public sealed class LinePrimitive : Primitive, ILinePrimitive
{
    public override PrimitiveType PrimitiveType => PrimitiveType.Line;
    public double StartX { get; set; }
    public double StartY { get; set; }
    public double StartZ { get; set; }
    public double DirX { get; set; }
    public double DirY { get; set; }
    public double DirZ { get; set; }

    public override (double X, double Y, double Z) GetRepresentativePoint() => (StartX, StartY, StartZ);
    public override (double I, double J, double K) GetDirection() => Normalize(DirX, DirY, DirZ);
    public override IReadOnlyList<MeasurementPoint> PlanPoints() => [];
}

/// <summary>圆基元</summary>
public sealed class CirclePrimitive : Primitive, ICirclePrimitive
{
    public override PrimitiveType PrimitiveType => PrimitiveType.Circle;
    public double CenterX { get; set; }
    public double CenterY { get; set; }
    public double CenterZ { get; set; }
    public double Radius { get; set; }
    public double NormalX { get; set; }
    public double NormalY { get; set; }
    public double NormalZ { get; set; }

    public override (double X, double Y, double Z) GetRepresentativePoint() => (CenterX, CenterY, CenterZ);
    public override (double I, double J, double K) GetDirection() => Normalize(NormalX, NormalY, NormalZ);
    public override IReadOnlyList<MeasurementPoint> PlanPoints() => [];
}

/// <summary>圆弧基元</summary>
public sealed class ArcPrimitive : Primitive, IArcPrimitive
{
    public override PrimitiveType PrimitiveType => PrimitiveType.Arc;
    public double CenterX { get; set; }
    public double CenterY { get; set; }
    public double CenterZ { get; set; }
    public double Radius { get; set; }
    public double StartAngleRad { get; set; }
    public double EndAngleRad { get; set; }
    public double NormalX { get; set; }
    public double NormalY { get; set; }
    public double NormalZ { get; set; }

    public override (double X, double Y, double Z) GetRepresentativePoint() => (CenterX, CenterY, CenterZ);
    public override (double I, double J, double K) GetDirection() => Normalize(NormalX, NormalY, NormalZ);
    public override IReadOnlyList<MeasurementPoint> PlanPoints() => [];
}

/// <summary>平面基元</summary>
public sealed class PlanePrimitive : Primitive, IPlanePrimitive
{
    public override PrimitiveType PrimitiveType => PrimitiveType.Plane;
    public double PointX { get; set; }
    public double PointY { get; set; }
    public double PointZ { get; set; }
    public double NormalX { get; set; }
    public double NormalY { get; set; }
    public double NormalZ { get; set; }

    public override (double X, double Y, double Z) GetRepresentativePoint() => (PointX, PointY, PointZ);
    public override (double I, double J, double K) GetDirection() => Normalize(NormalX, NormalY, NormalZ);
    public override IReadOnlyList<MeasurementPoint> PlanPoints() => [];
}

/// <summary>圆柱基元</summary>
public sealed class CylinderPrimitive : Primitive, ICylinderPrimitive
{
    public override PrimitiveType PrimitiveType => PrimitiveType.Cylinder;
    public double AxisPointX { get; set; }
    public double AxisPointY { get; set; }
    public double AxisPointZ { get; set; }
    public double AxisDirX { get; set; }
    public double AxisDirY { get; set; }
    public double AxisDirZ { get; set; }
    public double Radius { get; set; }

    public override (double X, double Y, double Z) GetRepresentativePoint() => (AxisPointX, AxisPointY, AxisPointZ);
    public override (double I, double J, double K) GetDirection() => Normalize(AxisDirX, AxisDirY, AxisDirZ);
    public override IReadOnlyList<MeasurementPoint> PlanPoints() => [];
}

/// <summary>球基元</summary>
public sealed class SpherePrimitive : Primitive, ISpherePrimitive
{
    public override PrimitiveType PrimitiveType => PrimitiveType.Sphere;
    public double CenterX { get; set; }
    public double CenterY { get; set; }
    public double CenterZ { get; set; }
    public double Radius { get; set; }

    public override (double X, double Y, double Z) GetRepresentativePoint() => (CenterX, CenterY, CenterZ);
    public override (double I, double J, double K) GetDirection() => (0, 0, 1);
    public override IReadOnlyList<MeasurementPoint> PlanPoints() => [];
}

/// <summary>圆锥基元</summary>
public sealed class ConePrimitive : Primitive, IConePrimitive
{
    public override PrimitiveType PrimitiveType => PrimitiveType.Cone;
    public double ApexX { get; set; }
    public double ApexY { get; set; }
    public double ApexZ { get; set; }
    public double AxisDirX { get; set; }
    public double AxisDirY { get; set; }
    public double AxisDirZ { get; set; }
    public double HalfAngleRad { get; set; }

    public override (double X, double Y, double Z) GetRepresentativePoint() => (ApexX, ApexY, ApexZ);
    public override (double I, double J, double K) GetDirection() => Normalize(AxisDirX, AxisDirY, AxisDirZ);
    public override IReadOnlyList<MeasurementPoint> PlanPoints() => [];
}

/// <summary>二维曲线基元</summary>
public sealed class Curve2DPrimitive : Primitive, ICurve2DPrimitive
{
    public override PrimitiveType PrimitiveType => PrimitiveType.Curve2D;
    public IReadOnlyList<(double X, double Y)> Points { get; set; } = [];
    public bool IsClosed { get; set; }
    public int Degree { get; set; }

    public override (double X, double Y, double Z) GetRepresentativePoint()
    {
        if (Points.Count == 0)
            return (0, 0, 0);

        double minX = Points[0].X, maxX = Points[0].X;
        double minY = Points[0].Y, maxY = Points[0].Y;
        foreach (var (x, y) in Points)
        {
            minX = Math.Min(minX, x);
            maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y);
            maxY = Math.Max(maxY, y);
        }

        return ((minX + maxX) / 2, (minY + maxY) / 2, 0);
    }

    public override (double I, double J, double K) GetDirection() => (0, 0, 1);
    public override IReadOnlyList<MeasurementPoint> PlanPoints() => [];
}

/// <summary>三维曲面基元</summary>
public sealed class Surface3DPrimitive : Primitive, ISurface3DPrimitive
{
    public override PrimitiveType PrimitiveType => PrimitiveType.Surface3D;
    public IReadOnlyList<(double X, double Y, double Z)> Vertices { get; set; } = [];
    public IReadOnlyList<(int V0, int V1, int V2)> Triangles { get; set; } = [];
    public string? SurfaceType { get; set; }

    public override (double X, double Y, double Z) GetRepresentativePoint()
    {
        if (Vertices.Count == 0)
            return (0, 0, 0);

        double sumX = 0, sumY = 0, sumZ = 0;
        foreach (var (x, y, z) in Vertices)
        {
            sumX += x;
            sumY += y;
            sumZ += z;
        }

        return (sumX / Vertices.Count, sumY / Vertices.Count, sumZ / Vertices.Count);
    }

    public override (double I, double J, double K) GetDirection() => (0, 0, 1);
    public override IReadOnlyList<MeasurementPoint> PlanPoints() => [];
}
