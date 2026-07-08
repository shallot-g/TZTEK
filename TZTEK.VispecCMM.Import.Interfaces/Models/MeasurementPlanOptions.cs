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

    public double DefaultApproachDistanceMm { get; set; } = 5.0;
    public double DefaultRetractDistanceMm { get; set; } = 5.0;
    public double DefaultSearchDistanceMm { get; set; } = 2.0;
    public double SafetyClearanceMm { get; set; } = 10.0;
    public double MinPlaneAreaMm2 { get; set; } = 1.0;
    public double MinCylinderRadiusMm { get; set; } = 0.1;
    public int LinePointCount { get; set; } = 3;
    public int PlaneGridUCount { get; set; } = 5;
    public int PlaneGridVCount { get; set; } = 5;
    public int PlanePointCount { get; set; } = 25;
    public int CirclePointCount { get; set; } = 8;
    public int ArcPointCount { get; set; } = 5;
    public int CylinderRadialPointCount { get; set; } = 8;
    public int CylinderLevelCount { get; set; } = 3;
    public int ConeRadialPointCount { get; set; } = 8;
    public int ConeLevelCount { get; set; } = 2;
    public int SpherePointCount { get; set; } = 15;
    public bool EnableFeatureFiltering { get; set; } = true;
    public bool EnableSameFeatureGrouping { get; set; } = true;
}
