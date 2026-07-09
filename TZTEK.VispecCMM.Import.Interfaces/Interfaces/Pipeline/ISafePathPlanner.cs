using TZTEK.VispecCMM.Import.Interfaces.Models;

namespace TZTEK.VispecCMM.Import.Interfaces.Interfaces.Pipeline;

/// <summary>
/// 安全路径规划器，用于把测量步骤展开为带安全移动的执行步骤。
/// </summary>
public interface ISafePathPlanner
{
    /// <summary>应用安全平面避障路径。</summary>
    MeasurementTask ApplySafetyPath(MeasurementTask task, MeasurementPlanOptions options);
}
