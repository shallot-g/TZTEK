namespace TZTEK.VispecCMM.Import.Interfaces.Interfaces;

/// <summary>
/// 探针接口（配置与分配合并）。
/// </summary>
public interface IProbe : IMeasurableElement
{
    /// <summary>探针类型</summary>
    Models.ProbeType ProbeType { get; }

    /// <summary>测头直径（mm）</summary>
    double TipDiameter { get; }

    /// <summary>测头长度（mm）</summary>
    double TipLength { get; }

    /// <summary>分配状态</summary>
    Models.ProbeAssignStatus AssignStatus { get; }

    /// <summary>已分配的基元列表</summary>
    IReadOnlyList<IPrimitive> AssignedPrimitives { get; }
}
