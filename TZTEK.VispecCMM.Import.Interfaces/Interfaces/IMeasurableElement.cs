namespace TZTEK.VispecCMM.Import.Interfaces.Interfaces;

/// <summary>
/// 所有可测量元素的基接口。
/// </summary>
public interface IMeasurableElement
{
    /// <summary>唯一标识</summary>
    string Id { get; }

    /// <summary>显示名称</summary>
    string Name { get; }

    /// <summary>元素类型</summary>
    Models.ElementType ElementType { get; }

    /// <summary>三维空间中的代表位置</summary>
    (double X, double Y, double Z) GetPosition();

    /// <summary>元素方向、法向或默认逼近方向</summary>
    (double I, double J, double K) GetDirection();

    /// <summary>规划测点；不适用的元素返回空列表</summary>
    IReadOnlyList<Models.MeasurementPoint> PlanPoints();
}
