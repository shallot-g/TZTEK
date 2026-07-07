namespace TZTEK.VispecCMM.Import.Interfaces.Models;

/// <summary>
/// 测量规划一站式配置。
/// </summary>
public sealed class MeasurementPlanOptions
{
    public LengthUnit Unit { get; set; } = LengthUnit.Millimeter;
    public double Tolerance { get; set; } = 0.001;
    public bool OnlyTolerancedPrimitives { get; set; }
    public PrimitiveType[]? PrimitiveFilter { get; set; }

    public (double X, double Y, double Z)? StartPoint { get; set; }
    public PathOptimizationStrategy PathStrategy { get; set; } = PathOptimizationStrategy.TwoOpt;
    public TimeSpan PathTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public bool EnableCollisionAvoidance { get; set; } = true;
    public ToleranceStandard ToleranceStandard { get; set; } = ToleranceStandard.ASME;
    public NamingRule? NamingRule { get; set; }
}
