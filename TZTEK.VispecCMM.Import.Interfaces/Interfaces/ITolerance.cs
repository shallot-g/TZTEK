namespace TZTEK.VispecCMM.Import.Interfaces.Interfaces;

/// <summary>
/// 公差基接口。
/// </summary>
public interface ITolerance : IMeasurableElement
{
    ToleranceType ToleranceType { get; }       // 尺寸/几何
    string SourceElementId { get; }            // 源标注 ID，用于关联
    double ToleranceValue { get; }             // 公差值
    ToleranceStandard ToleranceStandard { get; } // ASME / ISO
}

// ── 尺寸公差 ──
public interface IDimensionalTolerance : ITolerance
{
    double NominalValue { get; }              // 名义值
    double UpperDeviation { get; }            // 上偏差
    double LowerDeviation { get; }            // 下偏差
    DimensionType DimensionType { get; }      // 线性/直径/半径/角度
    double UpperLimit { get; }                // 名义值+上偏差
    double LowerLimit { get; }                // 名义值+下偏差
}

// ── 几何公差 (GD&T) ──
public interface IGeometricTolerance : ITolerance
{
    GdntCharacteristic Characteristic { get; }          // 14 种 GD&T 符号
    ToleranceZoneShape ZoneShape { get; }               // 公差带形状
    MaterialCondition MaterialCondition { get; }        // RFS/MMC/LMC
    IReadOnlyList<IDatumReference> Datums { get; }     // 基准参考列表
}

// ── 基准参考 ──
public interface IDatumReference
{
    string Label { get; }                               // "A", "B", "C"
    MaterialCondition MaterialCondition { get; }
}
