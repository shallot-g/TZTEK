using TZTEK.VispecCMM.Demo.Api.Models;

namespace TZTEK.VispecCMM.Demo.Api.Services;

public interface IDrawingAssistClient
{
    Task<DrawingAssistModelResult> AnalyzeAsync(
        string provider,
        string pdfPath,
        IReadOnlyList<VisualizationFeatureDto> features,
        Action<DrawingAssistProgressUpdate>? reportProgress,
        CancellationToken cancellationToken);

    IReadOnlyList<DrawingAssistProviderInfo> GetProviders();
}

public interface IDrawingAssistProvider
{
    string Provider { get; }
    DrawingAssistProviderInfo GetInfo();
    Task<DrawingAssistModelResult> AnalyzeAsync(
        string pdfPath,
        IReadOnlyList<VisualizationFeatureDto> features,
        Action<DrawingAssistProgressUpdate>? reportProgress,
        CancellationToken cancellationToken);
}

public sealed class DrawingAssistProviderInfo
{
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public bool IsConfigured { get; init; }
    public string Model { get; init; } = string.Empty;
    public string? UnavailableReason { get; init; }
}

public sealed class DrawingAssistRequest
{
    public string Provider { get; init; } = "volcengine";
}

public sealed record DrawingAssistProgressUpdate(string Status, int Progress);

public sealed class DrawingAssistPageDiagnostic
{
    public string Provider { get; init; } = string.Empty;
    public string RunId { get; init; } = string.Empty;
    public string PdfHash { get; init; } = string.Empty;
    public string ImageHash { get; init; } = string.Empty;
    public string PromptVersion { get; init; } = string.Empty;
    public int PageNumber { get; init; }
    public int? HttpStatusCode { get; init; }
    public string? RequestId { get; init; }
    public string Model { get; init; } = string.Empty;
    public string ResponseFields { get; init; } = string.Empty;
    public int OutputCount { get; init; }
    public int ChoicesCount { get; init; }
    public string ContentKinds { get; init; } = string.Empty;
    public int ResponseTextLength { get; init; }
    public long ElapsedMilliseconds { get; init; }
    public bool Success { get; init; }
    public string ResponseStatus { get; init; } = string.Empty;
    public string? IncompleteDetails { get; init; }
    public string? FinishReason { get; init; }
    public long? InputTokens { get; init; }
    public long? OutputTokens { get; init; }
    public long? TotalTokens { get; init; }
    public long? CachedTokens { get; init; }
    public bool CacheHit { get; init; }
    public DateTime StartedAt { get; init; }
    public DateTime CompletedAt { get; init; }
}

internal sealed class DrawingAssistPageException : InvalidOperationException
{
    public DrawingAssistPageException(string message, DrawingAssistPageDiagnostic diagnostic) : base(message)
    {
        Diagnostic = diagnostic;
    }

    public DrawingAssistPageDiagnostic Diagnostic { get; }
}

public sealed class DrawingAssistModelResult
{
    public string Provider { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public int TargetCount { get; init; }
    public IReadOnlyList<AiFeatureRecommendation> Recommendations { get; init; } = [];
    public string? RequestId { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<DrawingAssistPageDiagnostic> PageDiagnostics { get; init; } = [];
}
