namespace TZTEK.VispecCMM.Import.Interfaces.Models;

/// <summary>
/// GOTO 移动点。
/// </summary>
public sealed class GotoPoint
{
    public string Id { get; set; } = string.Empty;
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public string Reason { get; set; } = string.Empty;
}
