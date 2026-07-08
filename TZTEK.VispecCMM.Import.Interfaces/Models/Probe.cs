using TZTEK.VispecCMM.Import.Interfaces.Interfaces;

namespace TZTEK.VispecCMM.Import.Interfaces.Models;

/// <summary>
/// 探针/测头，实现 <see cref="IProbe"/>。合并配置参数与分配关系。
/// </summary>
public sealed class Probe : IProbe
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public ElementType ElementType => ElementType.Probe;
    public ProbeType ProbeType { get; set; }
    public double? BallDiameterMm { get; set; }
    public double? StemLengthMm { get; set; }
    public double TipDiameter { get; set; }
    public double TipLength { get; set; }
    public double AngleADeg { get; set; }
    public double AngleBDeg { get; set; }
    public IPrimitive? AssignedPrimitive { get; set; }
    public IReadOnlyList<IPrimitive> AssignedPrimitives { get; set; } = [];
    public IReadOnlyList<IProbe>? Recommendations { get; set; }
    public bool IsAssigned => AssignedPrimitive is not null;
    public ProbeAssignStatus AssignStatus =>
        AssignedPrimitive is not null || AssignedPrimitives.Count > 0
            ? ProbeAssignStatus.Assigned
            : Recommendations is { Count: > 0 }
                ? ProbeAssignStatus.Recommended
                : ProbeAssignStatus.Unassigned;

    public (double X, double Y, double Z) GetPosition() => (0, 0, 0);

    public (double I, double J, double K) GetDirection()
    {
        double aRad = AngleADeg * Math.PI / 180.0;
        double bRad = AngleBDeg * Math.PI / 180.0;
        return (
            Math.Sin(aRad) * Math.Cos(bRad),
            Math.Sin(aRad) * Math.Sin(bRad),
            Math.Cos(aRad));
    }

    public IReadOnlyList<MeasurementPoint> PlanPoints() => [];
}
