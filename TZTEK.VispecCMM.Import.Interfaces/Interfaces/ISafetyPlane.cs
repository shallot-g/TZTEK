namespace TZTEK.VispecCMM.Import.Interfaces.Interfaces;

/// <summary>
/// 安全平面接口。
/// </summary>
public interface ISafetyPlane : IMeasurableElement
{
    (double X, double Y, double Z) Point { get; }       // 平面上一点
    (double X, double Y, double Z) Normal { get; }      // 法向量
    double OffsetMm { get; }                           // 安全偏移距离 (mm)
}
