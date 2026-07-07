namespace TZTEK.VispecCMM.Import.Interfaces.Models;

/// <summary>
/// 导入进度报告。
/// </summary>
public sealed class ImportProgress
{
    public string Stage { get; set; } = string.Empty;
    public double PercentComplete { get; set; }
    public int PrimitiveCount { get; set; }
    public int ToleranceCount { get; set; }
}
