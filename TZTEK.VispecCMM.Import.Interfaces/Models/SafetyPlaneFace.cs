namespace TZTEK.VispecCMM.Import.Interfaces.Models;

/// <summary>
/// 工件安全包围盒的六个面。
/// </summary>
public enum SafetyPlaneFace
{
    /// <summary>顶面 (+Z)</summary>
    Top,

    /// <summary>底面 (-Z)</summary>
    Bottom,

    /// <summary>+X 侧面</summary>
    PosX,

    /// <summary>-X 侧面</summary>
    NegX,

    /// <summary>+Y 侧面</summary>
    PosY,

    /// <summary>-Y 侧面</summary>
    NegY
}
