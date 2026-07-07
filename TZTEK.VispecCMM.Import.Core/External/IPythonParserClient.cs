namespace TZTEK.VispecCMM.Import.Core.External;

public interface IPythonParserClient
{
    Task<RawDocument> ParseAsync(
        string parserName,
        string filePath,
        ImportOptions options,
        CancellationToken ct = default);
}
