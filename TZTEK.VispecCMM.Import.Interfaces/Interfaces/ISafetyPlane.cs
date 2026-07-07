namespace TZTEK.VispecCMM.Import.Interfaces.Interfaces;

/// <summary>
/// 安全平面接口。
/// </summary>
public interface ISafetyPlane
{
    /// <summary>唯一标识</summary>
    string Id { get; }

    /// <summary>名称</summary>
    string Name { get; }

    /// <summary>平面法向量 X 分量</summary>
    double NormalX { get; }

    /// <summary>平面法向量 Y 分量</summary>
    double NormalY { get; }

    /// <summary>平面法向量 Z 分量</summary>
    double NormalZ { get; }

    /// <summary>沿法向偏移量（mm）</summary>
    double Offset { get; }
}
