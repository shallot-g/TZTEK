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
}
