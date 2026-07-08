using TZTEK.VispecCMM.Import.Interfaces.Interfaces;

namespace TZTEK.VispecCMM.Import.Interfaces.Models;

/// <summary>
/// 安全平面，实现 <see cref="ISafetyPlane"/>。
/// </summary>
public sealed class SafetyPlane : ISafetyPlane
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public ElementType ElementType => ElementType.SafetyPlane;
    public double PointX { get; set; }
    public double PointY { get; set; }
    public double PointZ { get; set; }
    public double NormalX { get; set; }
    public double NormalY { get; set; }
    public double NormalZ { get; set; }
    public double OffsetMm { get; set; } = 5.0;
    public double Offset
    {
        get => OffsetMm;
        set => OffsetMm = value;
    }

    public (double X, double Y, double Z) Point => (PointX, PointY, PointZ);
    public (double X, double Y, double Z) Normal => Normalize(NormalX, NormalY, NormalZ);

    public (double X, double Y, double Z) GetPosition() => (PointX, PointY, PointZ + OffsetMm);
    public (double I, double J, double K) GetDirection() => Normal;
    public IReadOnlyList<MeasurementPoint> PlanPoints() => [];

    private static (double X, double Y, double Z) Normalize(double x, double y, double z)
    {
        double length = Math.Sqrt(x * x + y * y + z * z);
        if (length < 1e-12)
            return (0, 0, 1);

        return (x / length, y / length, z / length);
    }
}
