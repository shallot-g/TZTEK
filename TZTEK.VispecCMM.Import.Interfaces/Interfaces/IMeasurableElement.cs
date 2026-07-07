namespace TZTEK.VispecCMM.Import.Interfaces.Interfaces;

/// <summary>
/// 所有可测量元素的基接口。
/// </summary>
public interface IMeasurableElement
{
    string Id { get; }                                    // 元素唯一标识符
    string Name { get; }                                  // 元素显示名称
    ElementType ElementType { get; }                      // 类型判别 (Primitive/Tolerance/Probe/SafetyPlane/CoordinateSystem)
    (double X, double Y, double Z) GetPosition();         // 三维空间中的代表性位置
    (double I, double J, double K) GetDirection();        // 方向向量（法矢或逼近方向）
    IReadOnlyList<MeasurementPoint> PlanPoints();        // 规划测量点；不适用则返回空列表
}
