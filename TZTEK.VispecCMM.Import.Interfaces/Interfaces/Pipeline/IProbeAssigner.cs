namespace TZTEK.VispecCMM.Import.Interfaces.Interfaces.Pipeline;

/// <summary>
/// 探针分配器。
/// </summary>
public interface IProbeAssigner
{
    IProbe AssignProbe(IPrimitive primitive, IReadOnlyList<IProbe> availableProbes);
    IReadOnlyList<IProbe> AssignProbesBatch(
        IReadOnlyList<IPrimitive> primitives, IReadOnlyList<IProbe> availableProbes);
}
