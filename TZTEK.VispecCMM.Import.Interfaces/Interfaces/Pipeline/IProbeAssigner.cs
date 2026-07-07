namespace TZTEK.VispecCMM.Import.Interfaces.Interfaces.Pipeline;

/// <summary>
/// 探针分配器。
/// </summary>
public interface IProbeAssigner
{
    /// <summary>为基元列表分配探针</summary>
    IReadOnlyList<IProbe> Assign(
        IReadOnlyList<IPrimitive> primitives,
        IReadOnlyList<IProbe> availableProbes);
}
