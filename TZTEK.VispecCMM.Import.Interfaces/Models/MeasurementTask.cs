using TZTEK.VispecCMM.Import.Interfaces.Interfaces;

namespace TZTEK.VispecCMM.Import.Interfaces.Models;

/// <summary>
/// 测量任务，流水线最终输出。
/// </summary>
public sealed class MeasurementTask
{
    public string TaskId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string SourceFilePath { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ToleranceStandard ToleranceStandard { get; set; } = ToleranceStandard.ASME;
    public LengthUnit LengthUnit { get; set; } = LengthUnit.Millimeter;
    public IReadOnlyList<MeasurementStep> Steps { get; set; } = [];
    public IReadOnlyList<Primitive> CollisionPrimitives { get; set; } = [];
    public IReadOnlyList<IProbe> ProbeConfigurations { get; set; } = [];
    /// <summary>兼容旧逻辑的全局顶面安全平面；与 <see cref="SafetyEnvelope"/> 顶面一致。</summary>
    public ISafetyPlane? GlobalSafetyPlane { get; set; }

    /// <summary>六面安全平面包围盒（含底面）。</summary>
    public SafetyPlaneBox? SafetyEnvelope { get; set; }
    public PathOptimizationStrategy? PathOptimizationStrategy { get; set; }
    public double EstimatedTotalTimeSeconds { get; set; }
    public double TotalPathLengthMm { get; set; }
}

/// <summary>
/// 测量步骤。
/// </summary>
public sealed class MeasurementStep
{
    public int SequenceNumber { get; set; }
    public MeasurementStepType StepType { get; set; }
    public string Name { get; set; } = string.Empty;
    public PrimitiveToleranceItem? TargetItem { get; set; }
    public IProbe? ProbeAssignment { get; set; }
    public IReadOnlyList<MeasurementPoint>? MeasurementPoints { get; set; }
    public FittingMethodType? FittingMethod { get; set; }
    public GotoPoint? GotoTarget { get; set; }
    public ISafetyPlane? SafetyPlane { get; set; }
    public IProbe? NewProbe { get; set; }
    public LightingInfo? LightingInfo { get; set; }
    public double? TravelDistanceMm { get; set; }
    public double EstimatedTimeSeconds { get; set; }
    public bool CollisionValidated { get; set; }
    public bool IsCollisionRisk { get; set; }
    public bool RequiresManualGoto { get; set; }
    public bool IsExecutable { get; set; } = true;
    public string MovementKind { get; set; } = string.Empty;
    public string? CollisionReason { get; set; }
}

/// <summary>
/// 打光步骤信息。
/// </summary>
public sealed class LightingInfo
{
    public double Intensity { get; set; } = 1.0;
    public string Mode { get; set; } = "Default";
}
