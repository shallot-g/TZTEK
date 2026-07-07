namespace TZTEK.VispecCMM.Import.Interfaces.Models;

/// <summary>
/// 测量点。
/// </summary>
public sealed class MeasurementPoint
{
    public int Index { get; set; } = 1;
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public double NormalX { get; set; }
    public double NormalY { get; set; }
    public double NormalZ { get; set; } = 1;
    public double ApproachDistance { get; set; } = 5.0;
    public double RetractDistance { get; set; } = 5.0;
    public double SearchDistance { get; set; } = 2.0;
}

/// <summary>测点分布模式</summary>
public enum DistributionPattern
{
    Uniform,
    Grid,
    Helix,
    Adaptive
}

/// <summary>
/// 测点分布规则。
/// </summary>
public sealed class MeasurementPointDistribution
{
    public int PointCount { get; set; } = 5;
    public double StartAngleRad { get; set; }
    public double EndAngleRad { get; set; } = Math.PI * 2;
    public DistributionPattern Pattern { get; set; } = DistributionPattern.Uniform;
}
