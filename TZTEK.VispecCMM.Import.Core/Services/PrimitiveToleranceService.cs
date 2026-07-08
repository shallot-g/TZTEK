namespace TZTEK.VispecCMM.Import.Core.Services;

public sealed class PrimitiveToleranceService : IPrimitiveToleranceService
{
    private readonly IFileImportPipeline _fileImportPipeline;
    private readonly IMeasurementPlanner _measurementPlanner;
    private ImportResult? _lastImportResult;
    private IReadOnlyList<IProbe> _assignedProbes = [];

    public PrimitiveToleranceService(
        IFileImportPipeline fileImportPipeline,
        IMeasurementPlanner measurementPlanner)
    {
        _fileImportPipeline = fileImportPipeline;
        _measurementPlanner = measurementPlanner;
    }

    public async Task<ImportResult> ImportAsync(
        string filePath,
        ImportOptions options,
        IProgress<ImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report(new ImportProgress { Stage = "Importing", PercentComplete = 0 });
        var result = await _fileImportPipeline.ExecuteAsync(filePath, options, progress, cancellationToken)
            .ConfigureAwait(false);

        _lastImportResult = result;
        progress?.Report(new ImportProgress
        {
            Stage = "Imported",
            PercentComplete = 100,
            PrimitiveCount = result.Items.Count,
            ToleranceCount = result.Items.Sum(item => item.Tolerances.Count)
        });

        return result;
    }

    public IReadOnlyList<IPrimitive> GetPrimitives()
    {
        return _lastImportResult?.Items
            .Select(item => (IPrimitive)item.Primitive)
            .ToList()
            ?? [];
    }

    public IReadOnlyList<ITolerance> GetTolerances()
    {
        return _lastImportResult?.Items
            .SelectMany(item => item.Tolerances)
            .Select(tolerance => (ITolerance)tolerance)
            .ToList()
            ?? [];
    }

    public IReadOnlyList<PrimitiveToleranceItem> GetPrimitiveToleranceItems()
    {
        return _lastImportResult?.Items ?? [];
    }

    public void AssignProbes(IReadOnlyList<IProbe> probes)
    {
        _assignedProbes = probes;
    }

    public IReadOnlyList<MeasurementTask> GenerateMeasurementTasks(MeasurementPlanOptions options)
    {
        if (_lastImportResult is null)
            return [];

        var tasks = _measurementPlanner.Plan(_lastImportResult.Items, _assignedProbes, options).ToList();
        foreach (var task in tasks)
            task.SourceFilePath = _lastImportResult.FilePath;

        return tasks;
    }
}
