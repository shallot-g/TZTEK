using TZTEK.VispecCMM.Import.Interfaces.Models;

namespace TZTEK.VispecCMM.Import.Interfaces.Interfaces.Pipeline;

/// <summary>
/// 从导入结果与测量任务构建可视化场景。
/// </summary>
public interface IMeasurementSceneBuilder
{
    MeasurementScene Build(
        ImportResult importResult,
        IReadOnlyList<MeasurementTask> tasks,
        MeasurementSceneOptions? options = null);
}
