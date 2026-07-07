namespace TZTEK.VispecCMM.Import.Core.Pipeline;

/// <summary>
/// <see cref="IFileImportPipeline"/> 默认实现（骨架）。
/// </summary>
public sealed class FileImportPipeline : IFileImportPipeline
{
    private static readonly string[] Extensions =
    [
        ".dxf", ".dwg", ".pdf",
        ".step", ".stp", ".iges", ".igs",
        ".stl", ".obj"
    ];

    /// <inheritdoc />
    public IReadOnlyList<string> SupportedExtensions { get; } = Extensions;

    /// <inheritdoc />
    public bool CanImport(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public Task<ImportResult> ImportAsync(
        string filePath,
        ImportOptions options,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (!CanImport(filePath))
            throw new NotSupportedException($"Unsupported file format: {Path.GetExtension(filePath)}");

        // TODO: 解析文件 → RawElement → Primitive/Tolerance → Items
        var sourceType = ResolveSourceType(filePath);
        var result = new ImportResult
        {
            SourceType = sourceType,
            FilePath = filePath,
            RawElements = [],
            Items = []
        };

        return Task.FromResult(result);
    }

    private static ImportSourceType ResolveSourceType(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension is ".dxf" or ".dwg" or ".pdf"
            ? ImportSourceType.Drawing2D
            : ImportSourceType.Model3D;
    }
}
