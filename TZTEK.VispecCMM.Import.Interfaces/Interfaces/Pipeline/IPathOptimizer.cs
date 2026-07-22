using TZTEK.VispecCMM.Import.Interfaces.Models;

namespace TZTEK.VispecCMM.Import.Interfaces.Interfaces.Pipeline;

/// <summary>
/// 测量特征访问顺序优化器。
/// </summary>
public interface IPathOptimizer
{
    /// <summary>
    /// 根据 <see cref="MeasurementPlanOptions.PathStrategy"/> 重排测量特征顺序。
    /// </summary>
    IReadOnlyList<PrimitiveToleranceItem> OptimizeFeatureOrder(
        IReadOnlyList<PrimitiveToleranceItem> items,
        MeasurementPlanOptions options);

    /// <summary>
    /// 按测点间最近距离重排基元访问顺序。
    /// </summary>
    IReadOnlyList<PrimitiveToleranceItem> OptimizeFeatureOrderByPointProximity(
        IReadOnlyList<PrimitiveToleranceItem> items,
        IReadOnlyDictionary<string, IReadOnlyList<MeasurementPoint>> pointsByFeatureId,
        MeasurementPlanOptions options);

    /// <summary>
    /// 优化单个基元内测点访问顺序，首个测点尽量靠近参考位置。
    /// </summary>
    IReadOnlyList<MeasurementPoint> OptimizePointOrder(
        IReadOnlyList<MeasurementPoint> points,
        (double X, double Y, double Z)? referencePoint,
        MeasurementPlanOptions options);
}
