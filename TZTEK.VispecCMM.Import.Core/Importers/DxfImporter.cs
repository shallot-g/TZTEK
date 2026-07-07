using TZTEK.VispecCMM.Import.Core.External;

namespace TZTEK.VispecCMM.Import.Core.Importers;

public sealed class DxfImporter : FileFormatImporterBase
{
    private readonly IPythonParserClient _pythonParserClient;

    public DxfImporter(IPythonParserClient pythonParserClient)
    {
        _pythonParserClient = pythonParserClient;
    }

    public override IReadOnlySet<string> SupportedExtensions { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".dxf" };

    public override Task<RawDocument> ParseAsync(
        string filePath,
        ImportOptions options,
        CancellationToken ct = default)
    {
        return _pythonParserClient.ParseAsync("dxf", filePath, options, ct);
    }
}
