namespace TZTEK.VispecCMM.Import.Interfaces.Models;

/// <summary>
/// 路径优化配置。
/// </summary>
public sealed class PathOptimizationOptions
{
    public (double X, double Y, double Z)? StartPoint { get; set; }
    public (double X, double Y, double Z)? EndPoint { get; set; }
    public PathOptimizationStrategy Strategy { get; set; } = PathOptimizationStrategy.TwoOpt;
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
    public bool ReturnToStart { get; set; }
}
