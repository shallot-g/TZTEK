namespace TZTEK.VispecCMM.Import.Interfaces.Models;

/// <summary>
/// 基元-公差关联项。
/// </summary>
public sealed class PrimitiveToleranceItem
{
    public Primitive Primitive { get; set; } = null!;
    public IReadOnlyList<Tolerance> Tolerances { get; set; } = [];
}
