namespace TZTEK.VispecCMM.Demo.Api.Services;

using TZTEK.VispecCMM.Demo.Api.Models;

public sealed class DrawingAssistResponse
{
    public string Provider { get; init; } = string.Empty;
    public string Status { get; init; } = "Completed";
    public string Message { get; init; } = string.Empty;
    public int Progress { get; init; } = 100;
    public int TargetCount { get; init; }
    public int RecommendedCount { get; init; }
    public int LowConfidenceCount { get; init; }
    public IReadOnlyList<AiFeatureRecommendation> Recommendations { get; init; } = [];
    public string? RequestId { get; init; }
    public long ElapsedMilliseconds { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<DrawingAssistPageDiagnostic> PageDiagnostics { get; init; } = [];
    public string Model { get; init; } = string.Empty;
    public int PageCount { get; init; }
    public int SuccessPageCount { get; init; }
    public int FailedPageCount { get; init; }
}
