namespace TZTEK.VispecCMM.Demo.Api.Models;

using TZTEK.VispecCMM.Demo.Api.Services;

public sealed class DemoSessionDto
{
    public string Id { get; init; } = string.Empty;
    public string Status { get; set; } = "Queued";
    public int Progress { get; set; }
    public string Stage { get; set; } = "等待处理";
    public string? Error { get; set; }
    public string WorkflowStage { get; set; } = "FeaturesReady";
    public IReadOnlyList<string> SelectedFeatureIds { get; set; } = [];
    public bool AiAssistEnabled { get; set; }
    public DrawingFileDto? DrawingFile { get; set; }
    public string DrawingAssistStatus { get; set; } = "Idle";
    public int DrawingAssistProgress { get; set; }
    public IReadOnlyList<AiFeatureRecommendation> AiRecommendations { get; set; } = [];
    public VisualizationResultDto? Result { get; set; }
}

public sealed class DrawingFileDto
{
    public string FileName { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public DateTime UploadedAt { get; init; }
    public string Status { get; init; } = "Uploaded";
}

public sealed class DrawingAssistResult
{
    public string Status { get; init; } = "NotImplemented";
    public string Message { get; init; } = string.Empty;
    public IReadOnlyList<AiFeatureRecommendation> Recommendations { get; init; } = [];
    public int TargetCount { get; init; }
    public int RecommendedCount { get; init; }
    public int LowConfidenceCount { get; init; }
    public string? RequestId { get; init; }
    public long ElapsedMilliseconds { get; init; }
    public IReadOnlyList<DrawingAssistPageDiagnostic> PageDiagnostics { get; init; } = [];
    public string Model { get; init; } = string.Empty;
    public int PageCount { get; init; }
    public int SuccessPageCount { get; init; }
    public int FailedPageCount { get; init; }
}

public sealed class VisualizationResultDto
{
    public string SessionId { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string SourceType { get; init; } = string.Empty;
    public string? ModelUrl { get; init; }
    public IReadOnlyList<VisualizationFeatureDto> Features { get; init; } = [];
    public VisualizationPathPlanDto BaselinePlan { get; init; } = new();
    public VisualizationPathPlanDto OptimizedPlan { get; init; } = new();
    public IReadOnlyList<AiFeatureRecommendation> AiRecommendations { get; init; } = [];
    public VisualizationProbeDto? Probe { get; init; }
    public IReadOnlyList<VisualizationWarningDto> Warnings { get; init; } = [];
    public VisualizationBoundsDto Bounds { get; init; } = new();
}

public sealed class AiFeatureRecommendation
{
    public string FeatureId { get; init; } = string.Empty;
    public string Status { get; init; } = "NeedsReview";
    public double Confidence { get; init; }
    public string Reason { get; init; } = string.Empty;
    public int? PageNumber { get; init; }
    public string? AnnotationId { get; init; }
    public IReadOnlyList<string> AlternativeFeatureIds { get; init; } = [];
}

public sealed class VisualizationFeatureDto
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public double[] Position { get; init; } = [0, 0, 0];
    public double[] Direction { get; init; } = [0, 0, 1];
    public double? Radius { get; init; }
    public double? Length { get; init; }
    public double[]? AxisStart { get; init; }
    public double[]? AxisEnd { get; init; }
    public double? StartAngleRad { get; init; }
    public double? AngularSpanRad { get; init; }
    public double[]? RadialReference { get; init; }
    public bool? IsInnerSurface { get; init; }
    public IReadOnlyList<string> SourceElementIds { get; init; } = [];
    public bool IsMeasurementFeature { get; init; }
    public bool RequiresProbeReorientation { get; init; }
    public double? Area { get; init; }
    public double? AngleRad { get; init; }
    public double? ConeLength { get; init; }
    public double[]? ConeAxisStart { get; init; }
    public double[]? ConeAxisEnd { get; init; }
    public double? ConeRefRadius { get; init; }
    public double? ConeRadiusStart { get; init; }
    public double? ConeRadiusEnd { get; init; }
    public string? SurfaceType { get; init; }
    public string? FittingMethod { get; init; }
    public IReadOnlyList<string> Tolerances { get; init; } = [];
    public IReadOnlyList<VisualizationPointDto> MeasurementPoints { get; init; } = [];
}

public sealed class VisualizationPointDto
{
    public int Index { get; init; }
    public double[] Position { get; init; } = [0, 0, 0];
    public double[] Normal { get; init; } = [0, 0, 1];
    public double ApproachDistance { get; init; }
    public double RetractDistance { get; init; }
    public double SearchDistance { get; init; }
}

public sealed class VisualizationPathPlanDto
{
    public string Name { get; init; } = string.Empty;
    public IReadOnlyList<VisualizationPathSegmentDto> Segments { get; init; } = [];
    public VisualizationStatisticsDto Statistics { get; init; } = new();
}

public sealed record VisualizationPathSegmentDto
{
    public int Sequence { get; init; }
    public string Kind { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? FeatureId { get; init; }
    public double[] Start { get; init; } = [0, 0, 0];
    public double[] End { get; init; } = [0, 0, 0];
    public bool IsGoto { get; init; }
    public bool IsAutoGoto { get; init; }
    public bool HasRisk { get; init; }
    public string? Reason { get; init; }
    public double DistanceMm { get; init; }
}

public sealed class VisualizationStatisticsDto
{
    public int PrimitiveCount { get; init; }
    public int FeatureCount { get; init; }
    public int MeasurementPointCount { get; init; }
    public int MovementCount { get; init; }
    public int MeasurementCount { get; init; }
    public int GotoCount { get; init; }
    public int AutoGotoCount { get; init; }
    public int ManualGotoCount { get; init; }
    public double TotalPathLengthMm { get; init; }
    public double EstimatedTimeSeconds { get; init; }
}

public sealed class VisualizationProbeDto
{
    public string Name { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public double TipDiameterMm { get; init; }
    public double TipLengthMm { get; init; }
    public double AngleADeg { get; init; }
    public double AngleBDeg { get; init; }
}

public sealed class VisualizationWarningDto
{
    public string Level { get; init; } = "Info";
    public string Message { get; init; } = string.Empty;
}

public sealed class VisualizationBoundsDto
{
    public double[] Min { get; init; } = [0, 0, 0];
    public double[] Max { get; init; } = [0, 0, 0];
}

public sealed class DemoExampleDto
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
}
