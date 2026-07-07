namespace TZTEK.VispecCMM.Import.Interfaces.Models;

/// <summary>
/// 导入配置。
/// </summary>
public sealed class ImportOptions
{
    public LengthUnit Unit { get; set; } = LengthUnit.Millimeter;
    public double[,] TargetCsys { get; set; } =
    {
        { 1, 0, 0, 0 },
        { 0, 1, 0, 0 },
        { 0, 0, 1, 0 },
        { 0, 0, 0, 1 }
    };
    public double Tolerance { get; set; } = 0.001;
    public PrimitiveType[]? PrimitiveFilter { get; set; }
    public bool OnlyTolerancedPrimitives { get; set; }
}
