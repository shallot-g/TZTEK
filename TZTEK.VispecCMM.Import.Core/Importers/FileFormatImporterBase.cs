namespace TZTEK.VispecCMM.Import.Core.Importers;

public abstract class FileFormatImporterBase : IFileFormatImporter
{
    public abstract IReadOnlySet<string> SupportedExtensions { get; }

    public bool CanImport(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return SupportedExtensions.Contains(extension);
    }

    public abstract Task<RawDocument> ParseAsync(
        string filePath,
        ImportOptions options,
        CancellationToken ct = default);
}
