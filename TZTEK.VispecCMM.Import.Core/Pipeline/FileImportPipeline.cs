namespace TZTEK.VispecCMM.Import.Core.Pipeline;

using TZTEK.VispecCMM.Import.Core.Extraction;
using TZTEK.VispecCMM.Import.Core.Importers;

/// <summary>
/// <see cref="IFileImportPipeline"/> 默认实现，负责调度具体格式导入器和统一提取层。
/// </summary>
public sealed class FileImportPipeline : IFileImportPipeline
{
    private readonly IReadOnlyList<IFileFormatImporter> _importers;
    private readonly IPrimitiveToleranceExtractor _extractor;

    public FileImportPipeline(
        IEnumerable<IFileFormatImporter> importers,
        IPrimitiveToleranceExtractor extractor)
    {
        _importers = importers.ToList();
        _extractor = extractor;
        SupportedExtensions = _importers
            .SelectMany(importer => importer.SupportedExtensions)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(extension => extension, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <inheritdoc />
    public IReadOnlyList<string> SupportedExtensions { get; }

    /// <inheritdoc />
    public bool CanImport(string filePath)
    {
        return _importers.Any(importer => importer.CanImport(filePath));
    }

    /// <inheritdoc />
    public async Task<ImportResult> ImportAsync(
        string filePath,
        ImportOptions options,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var importer = _importers.FirstOrDefault(importer => importer.CanImport(filePath));
        if (importer is null)
            throw new NotSupportedException($"Unsupported file format: {Path.GetExtension(filePath)}");

        var document = await importer.ParseAsync(filePath, options, ct).ConfigureAwait(false);
        var items = await _extractor.ExtractAsync(document, options, ct).ConfigureAwait(false);

        return new ImportResult
        {
            SourceType = document.SourceType,
            FilePath = filePath,
            RawElements = document.Elements,
            Items = items
        };
    }
}
