namespace TZTEK.VispecCMM.Import.Interfaces.Interfaces;

/// <summary>
/// 坐标系接口。
/// </summary>
public interface ICoordinateSystem : IMeasurableElement
{
    /// <summary>坐标系类型</summary>
    Models.CoordinateSystemType SystemType { get; }

    /// <summary>原点 X</summary>
    double OriginX { get; }

    /// <summary>原点 Y</summary>
    double OriginY { get; }

    /// <summary>原点 Z</summary>
    double OriginZ { get; }

    /// <summary>X 轴方向向量</summary>
    (double X, double Y, double Z) AxisX { get; }

    /// <summary>Y 轴方向向量</summary>
    (double X, double Y, double Z) AxisY { get; }

    /// <summary>Z 轴方向向量</summary>
    (double X, double Y, double Z) AxisZ { get; }
}
