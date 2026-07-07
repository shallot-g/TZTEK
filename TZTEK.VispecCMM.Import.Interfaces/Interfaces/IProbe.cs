namespace TZTEK.VispecCMM.Import.Interfaces.Interfaces;

/// <summary>
/// 探针接口（配置与分配合并）。
/// </summary>
public interface IProbe : IMeasurableElement
{
    ProbeType ProbeType { get; }                        // 触发式/扫描式/光学/激光
    double? BallDiameterMm { get; }                     // 测球直径 (mm)
    double? StemLengthMm { get; }                       // 探针杆长度 (mm)
    double AngleADeg { get; }                           // A 轴角度 (度)
    double AngleBDeg { get; }                           // B 轴角度 (度)
    IPrimitive? AssignedPrimitive { get; }              // 分配的基元
    IReadOnlyList<IProbe>? Recommendations { get; }    // 备选推荐
}
