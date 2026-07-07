using TZTEK.VispecCMM.Import.Interfaces.Models;

namespace TZTEK.VispecCMM.Import.Interfaces.Interfaces.Pipeline;

/// <summary>
/// 测量规划器。
/// </summary>
public interface IMeasurementPlanner
{
    /// <summary>根据基元、公差与探针生成测量计划</summary>
    IReadOnlyList<MeasurementTask> Plan(
        IReadOnlyList<PrimitiveToleranceItem> items,
        IReadOnlyList<IProbe> probes,
        MeasurementPlanOptions options);
}
