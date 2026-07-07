namespace TZTEK.VispecCMM.Import.Core.External;

public interface ILocalOcrClient
{
    Task<string> RecognizeAsync(string filePath, CancellationToken ct = default);
}
