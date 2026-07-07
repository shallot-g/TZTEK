namespace TZTEK.VispecCMM.Import.Interfaces.Interfaces;

/// <summary>
/// 几何基元基接口。
/// </summary>
public interface IPrimitive : IMeasurableElement
{
    PrimitiveType PrimitiveType { get; }
    string SourceElementId { get; }
    string? CoordSystemId { get; }
    string? AssignedProbeId { get; }
}

public interface IPointPrimitive : IPrimitive
{
    double X { get; }
    double Y { get; }
    double Z { get; }
}

public interface ILinePrimitive : IPrimitive
{
    double StartX { get; }
    double StartY { get; }
    double StartZ { get; }
    double DirX { get; }
    double DirY { get; }
    double DirZ { get; }
}

public interface ICirclePrimitive : IPrimitive
{
    double CenterX { get; }
    double CenterY { get; }
    double CenterZ { get; }
    double Radius { get; }
    double NormalX { get; }
    double NormalY { get; }
    double NormalZ { get; }
}

public interface IArcPrimitive : IPrimitive
{
    double CenterX { get; }
    double CenterY { get; }
    double CenterZ { get; }
    double Radius { get; }
    double StartAngleRad { get; }
    double EndAngleRad { get; }
    double NormalX { get; }
    double NormalY { get; }
    double NormalZ { get; }
}

public interface IPlanePrimitive : IPrimitive
{
    double PointX { get; }
    double PointY { get; }
    double PointZ { get; }
    double NormalX { get; }
    double NormalY { get; }
    double NormalZ { get; }
}

public interface ICylinderPrimitive : IPrimitive
{
    double AxisPointX { get; }
    double AxisPointY { get; }
    double AxisPointZ { get; }
    double AxisDirX { get; }
    double AxisDirY { get; }
    double AxisDirZ { get; }
    double Radius { get; }
}

public interface ISpherePrimitive : IPrimitive
{
    double CenterX { get; }
    double CenterY { get; }
    double CenterZ { get; }
    double Radius { get; }
}

public interface IConePrimitive : IPrimitive
{
    double ApexX { get; }
    double ApexY { get; }
    double ApexZ { get; }
    double AxisDirX { get; }
    double AxisDirY { get; }
    double AxisDirZ { get; }
    double HalfAngleRad { get; }
}

public interface ICurve2DPrimitive : IPrimitive
{
    IReadOnlyList<(double X, double Y)> Points { get; }
    bool IsClosed { get; }
    int Degree { get; }
}

public interface ISurface3DPrimitive : IPrimitive
{
    IReadOnlyList<(double X, double Y, double Z)> Vertices { get; }
    IReadOnlyList<(int V0, int V1, int V2)> Triangles { get; }
    string? SurfaceType { get; }
}
