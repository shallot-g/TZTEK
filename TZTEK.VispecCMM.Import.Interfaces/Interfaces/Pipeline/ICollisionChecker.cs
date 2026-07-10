using TZTEK.VispecCMM.Import.Interfaces.Models;

namespace TZTEK.VispecCMM.Import.Interfaces.Interfaces.Pipeline;

/// <summary>
/// 碰撞检测器，负责判断路径线段是否与简化碰撞体相交。
/// </summary>
public interface ICollisionChecker
{
    CollisionResult Check(
        MeasurementTask task,
        (double X, double Y, double Z) start,
        (double X, double Y, double Z) end,
        MeasurementStep movementStep,
        MeasurementPlanOptions options,
        int segmentIndex);
}
