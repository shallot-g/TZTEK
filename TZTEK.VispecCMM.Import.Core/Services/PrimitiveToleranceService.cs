using TZTEK.VispecCMM.Import.Core.Internal;

namespace TZTEK.VispecCMM.Import.Core.Services;

/// <summary>
/// <see cref="IPrimitiveToleranceService"/> 默认实现，编排完整流水线。
/// </summary>
public sealed class PrimitiveToleranceService : IPrimitiveToleranceService
{
    private readonly IFileImportPipeline _importPipeline;
    private readonly IProbeAssigner _probeAssigner;
    private readonly IMeasurementPlanner _measurementPlanner;
    private readonly IMeasurementTaskAssembler _taskAssembler;
    private readonly PathOptimizer _pathOptimizer;

    public PrimitiveToleranceService(
        IFileImportPipeline importPipeline,
        IProbeAssigner probeAssigner,
        IMeasurementPlanner measurementPlanner,
        IMeasurementTaskAssembler taskAssembler)
    {
        _importPipeline = importPipeline;
        _probeAssigner = probeAssigner;
        _measurementPlanner = measurementPlanner;
        _taskAssembler = taskAssembler;
        _pathOptimizer = new PathOptimizer();
    }

    /// <inheritdoc />
    public async Task<MeasurementTask> GenerateMeasurementPlanAsync(
        string filePath,
        MeasurementPlanOptions options,
        IProgress<ImportProgress>? progress = null,
        CancellationToken ct = default)
    {
        Report(progress, "Import", 0, 0, 0);

        var importOptions = new ImportOptions
        {
            Unit = options.Unit,
            Tolerance = options.Tolerance,
            OnlyTolerancedPrimitives = options.OnlyTolerancedPrimitives,
            PrimitiveFilter = options.PrimitiveFilter
        };

        var importResult = await _importPipeline.ImportAsync(filePath, importOptions, ct)
            .ConfigureAwait(false);

        Report(progress, "Import", 25, importResult.Items.Count, 0);

        var primitives = importResult.Items.Select(item => item.Primitive).ToList();
        var optimizedPath = await OptimizeMeasurementPath(
                primitives,
                new PathOptimizationOptions
                {
                    StartPoint = options.StartPoint,
                    Strategy = options.PathStrategy,
                    Timeout = options.PathTimeout
                },
                ct)
            .ConfigureAwait(false);

        var orderedPath = optimizedPath.OfType<IPrimitive>().ToList();
        Report(progress, "PathOptimization", 50, primitives.Count, 0);

        // TODO: 接入可用探针列表与碰撞避障逻辑
        _ = _probeAssigner;
        _ = _measurementPlanner;
        _ = options.EnableCollisionAvoidance;

        if (options.NamingRule is not null)
            _taskAssembler.SetRule(options.NamingRule);

        var task = await _taskAssembler.GenerateAsync(
                importResult,
                orderedPath,
                options.NamingRule,
                options.ToleranceStandard,
                progress,
                ct)
            .ConfigureAwait(false);

        Report(progress, "Complete", 100, primitives.Count, importResult.Items.Sum(i => i.Tolerances.Count));
        return task;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<IMeasurableElement>> OptimizeMeasurementPath(
        IReadOnlyList<IMeasurableElement> elements,
        PathOptimizationOptions? pathOptions = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var optimized = _pathOptimizer.Optimize(elements, pathOptions);
        return Task.FromResult(optimized);
    }

    private static void Report(
        IProgress<ImportProgress>? progress,
        string stage,
        double percent,
        int primitiveCount,
        int toleranceCount)
    {
        progress?.Report(new ImportProgress
        {
            Stage = stage,
            PercentComplete = percent,
            PrimitiveCount = primitiveCount,
            ToleranceCount = toleranceCount
        });
    }
}
