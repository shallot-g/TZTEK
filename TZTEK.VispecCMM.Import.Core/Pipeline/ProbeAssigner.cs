namespace TZTEK.VispecCMM.Import.Core.Pipeline;

/// <summary>
/// <see cref="IProbeAssigner"/> 默认实现（骨架）。
/// </summary>
public sealed class ProbeAssigner : IProbeAssigner
{
    /// <inheritdoc />
    public IProbe AssignProbe(IPrimitive primitive, IReadOnlyList<IProbe> availableProbes)
    {
        ArgumentNullException.ThrowIfNull(primitive);
        ArgumentNullException.ThrowIfNull(availableProbes);

        if (availableProbes.Count == 0)
            throw new InvalidOperationException("No available probes to assign.");

        // TODO: 按基元类型与几何特征匹配探针
        return availableProbes[0];
    }

    /// <inheritdoc />
    public IReadOnlyList<IProbe> AssignProbesBatch(
        IReadOnlyList<IPrimitive> primitives,
        IReadOnlyList<IProbe> availableProbes)
    {
        ArgumentNullException.ThrowIfNull(primitives);
        ArgumentNullException.ThrowIfNull(availableProbes);

        return primitives
            .Select(primitive => AssignProbe(primitive, availableProbes))
            .ToList();
    }
}
