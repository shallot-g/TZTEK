namespace TZTEK.VispecCMM.Import.Interfaces.Interfaces;

/// <summary>
/// 坐标系接口。
/// </summary>
public interface ICoordinateSystem : IMeasurableElement
{
    CoordinateSystemType CoordinateSystemType { get; }  // 工件/机器/局部
    double[] TransformMatrix { get; }                   // 4×4 齐次矩阵 (行主序，长度 16)
    string? ParentId { get; }                           // 父坐标系 ID
}
