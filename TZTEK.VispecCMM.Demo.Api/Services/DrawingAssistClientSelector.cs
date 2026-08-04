using TZTEK.VispecCMM.Demo.Api.Models;

namespace TZTEK.VispecCMM.Demo.Api.Services;

public sealed class DrawingAssistClientSelector : IDrawingAssistClient
{
    private readonly IReadOnlyDictionary<string, IDrawingAssistProvider> _providers;

    public DrawingAssistClientSelector(IEnumerable<IDrawingAssistProvider> providers)
    {
        _providers = providers.ToDictionary(item => item.Provider, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<DrawingAssistProviderInfo> GetProviders() =>
        _providers.Values.Select(item => item.GetInfo()).OrderBy(item => item.Id).ToList();

    public Task<DrawingAssistModelResult> AnalyzeAsync(
        string provider,
        string pdfPath,
        IReadOnlyList<VisualizationFeatureDto> features,
        Action<DrawingAssistProgressUpdate>? reportProgress,
        CancellationToken cancellationToken)
    {
        var normalized = string.IsNullOrWhiteSpace(provider) ? "volcengine" : provider.Trim();
        if (!_providers.TryGetValue(normalized, out var client))
            throw new ArgumentException($"不支持的 AI 供应商：{normalized}。可选值为 volcengine 或 openai。", nameof(provider));
        return client.AnalyzeAsync(pdfPath, features, reportProgress, cancellationToken);
    }
}
