using TZTEK.VispecCMM.Import.Interfaces.Models;

namespace TZTEK.VispecCMM.Import.Interfaces.Interfaces.Pipeline;

/// <summary>
/// 测量任务组装器。
/// </summary>
public interface IMeasurementTaskAssembler
{
    /// <summary>将规划数据组装为完整测量任务</summary>
    MeasurementTask Assemble(
        IPrimitive primitive,
        ITolerance? tolerance,
        IProbe probe,
        IReadOnlyList<MeasurementPoint> points);

    /// <summary>批量组装测量任务</summary>
    IReadOnlyList<MeasurementTask> AssembleBatch(
        IReadOnlyList<(IPrimitive Primitive, ITolerance? Tolerance, IProbe Probe, IReadOnlyList<MeasurementPoint> Points)> inputs);
}
