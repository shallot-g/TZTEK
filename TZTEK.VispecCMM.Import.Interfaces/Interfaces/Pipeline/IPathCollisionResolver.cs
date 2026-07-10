using TZTEK.VispecCMM.Import.Interfaces.Models;

namespace TZTEK.VispecCMM.Import.Interfaces.Interfaces.Pipeline;

/// <summary>
/// 路径碰撞修正器，负责用特征 GOTO 锚点和用户预设 GOTO 处理有碰撞风险的移动段。
/// </summary>
public interface IPathCollisionResolver
{
    MeasurementTask Resolve(MeasurementTask task, MeasurementPlanOptions options);
}
