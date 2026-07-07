using TZTEK.VispecCMM.Import.Core.External;

namespace TZTEK.VispecCMM.Import.Core.Importers;

public sealed class StepImporter : FileFormatImporterBase
{
    private readonly IPythonParserClient _pythonParserClient;

    public StepImporter(IPythonParserClient pythonParserClient)
    {
        _pythonParserClient = pythonParserClient;
    }

    public override IReadOnlySet<string> SupportedExtensions { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".stp", ".step" };

    public override Task<RawDocument> ParseAsync(
        string filePath,
        ImportOptions options,
        CancellationToken ct = default)
    {
        return _pythonParserClient.ParseAsync("step", filePath, options, ct);
    }
}
