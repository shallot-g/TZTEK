using TZTEK.VispecCMM.Import.Interfaces.Models;

namespace TZTEK.VispecCMM.Import.Interfaces.Interfaces;

/// <summary>
/// 基元-公差服务，唯一对外入口。
/// </summary>
public interface IPrimitiveToleranceService
{
    /// <summary>导入文件并生成测量计划</summary>
    Task<ImportResult> ImportAsync(
        string filePath,
        ImportOptions options,
        IProgress<ImportProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>获取所有基元</summary>
    IReadOnlyList<IPrimitive> GetPrimitives();

    /// <summary>获取所有公差</summary>
    IReadOnlyList<ITolerance> GetTolerances();

    /// <summary>获取基元-公差关联项</summary>
    IReadOnlyList<PrimitiveToleranceItem> GetPrimitiveToleranceItems();

    /// <summary>分配探针</summary>
    void AssignProbes(IReadOnlyList<IProbe> probes);

    /// <summary>生成测量任务</summary>
    IReadOnlyList<MeasurementTask> GenerateMeasurementTasks(MeasurementPlanOptions options);
}
