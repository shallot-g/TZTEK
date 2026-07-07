using TZTEK.VispecCMM.Import.Interfaces.Interfaces;

namespace TZTEK.VispecCMM.Import.Interfaces.Models;

/// <summary>
/// 公差抽象基类，实现 <see cref="ITolerance"/>。
/// </summary>
public abstract class Tolerance : ITolerance
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public ElementType ElementType => ElementType.Tolerance;
    public abstract ToleranceType ToleranceType { get; }
    public string SourceElementId { get; set; } = string.Empty;
    public virtual double ToleranceValue { get; set; }
    public ToleranceStandard ToleranceStandard { get; set; } = ToleranceStandard.ASME;

    public (double X, double Y, double Z) GetPosition() => (0, 0, 0);
    public (double I, double J, double K) GetDirection() => (0, 0, 1);
    public IReadOnlyList<MeasurementPoint> PlanPoints() => [];
}

/// <summary>尺寸公差</summary>
public sealed class DimensionalTolerance : Tolerance, IDimensionalTolerance
{
    public override ToleranceType ToleranceType => ToleranceType.Dimensional;
    public double NominalValue { get; set; }
    public double UpperDeviation { get; set; }
    public double LowerDeviation { get; set; }
    public DimensionType DimensionType { get; set; }
    public double UpperLimit => NominalValue + UpperDeviation;
    public double LowerLimit => NominalValue + LowerDeviation;
}

/// <summary>几何公差</summary>
public sealed class GeometricTolerance : Tolerance, IGeometricTolerance
{
    public override ToleranceType ToleranceType => ToleranceType.Geometric;
    public GdntCharacteristic Characteristic { get; set; }
    public ToleranceZoneShape ZoneShape { get; set; }
    public MaterialCondition MaterialCondition { get; set; }
    public IReadOnlyList<IDatumReference> Datums { get; set; } = [];
}

/// <summary>基准参考</summary>
public sealed class DatumReference : IDatumReference
{
    public string Label { get; set; } = string.Empty;
    public MaterialCondition MaterialCondition { get; set; }
}
