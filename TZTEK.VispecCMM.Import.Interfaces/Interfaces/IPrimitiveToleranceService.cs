using TZTEK.VispecCMM.Import.Interfaces.Models;

namespace TZTEK.VispecCMM.Import.Interfaces.Interfaces;

/// <summary>
/// 基元-公差服务，唯一对外入口。
/// </summary>
public interface IPrimitiveToleranceService
{
    Task<MeasurementTask> GenerateMeasurementPlanAsync(
        string filePath,
        MeasurementPlanOptions options,
        IProgress<ImportProgress>? progress = null,
        CancellationToken ct = default);

    Task<IReadOnlyList<IMeasurableElement>> OptimizeMeasurementPath(
        IReadOnlyList<IMeasurableElement> elements,
        PathOptimizationOptions? pathOptions = null,
        CancellationToken ct = default);
}
