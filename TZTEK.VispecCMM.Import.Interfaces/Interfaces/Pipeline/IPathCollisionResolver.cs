using TZTEK.VispecCMM.Import.Interfaces.Models;

namespace TZTEK.VispecCMM.Import.Interfaces.Interfaces.Pipeline;

/// <summary>
/// 路径碰撞修正器，负责把有碰撞风险的移动段改写为绕行路径。
/// </summary>
public interface IPathCollisionResolver
{
    MeasurementTask Resolve(MeasurementTask task, MeasurementPlanOptions options);
}
