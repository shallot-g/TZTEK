namespace TZTEK.VispecCMM.Import.Core.External;

public interface ILocalLlmClient
{
    Task<string> GenerateStructuredJsonAsync(string prompt, CancellationToken ct = default);
}
