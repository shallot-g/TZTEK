namespace TZTEK.VispecCMM.Import.Interfaces.Interfaces;

/// <summary>
/// 公差基接口。
/// </summary>
public interface ITolerance : IMeasurableElement
{
    /// <summary>公差类型</summary>
    Models.ToleranceType ToleranceType { get; }

    /// <summary>源标注 ID，用于和基元关联</summary>
    string SourceElementId { get; }

    /// <summary>公差值</summary>
    double ToleranceValue { get; }

    /// <summary>公差值</summary>
    double Value { get; }

    /// <summary>公差评定标准</summary>
    Models.ToleranceStandard ToleranceStandard { get; }

    /// <summary>关联基元</summary>
    IPrimitive? TargetPrimitive { get; }
}

/// <summary>尺寸公差</summary>
public interface IDimensionalTolerance : ITolerance
{
    /// <summary>尺寸公差子类型</summary>
    Models.DimensionalToleranceKind Kind { get; }

    /// <summary>名义值</summary>
    double NominalValue { get; }

    /// <summary>上偏差</summary>
    double UpperDeviation { get; }

    /// <summary>下偏差</summary>
    double LowerDeviation { get; }

    /// <summary>尺寸类型</summary>
    Models.DimensionType DimensionType { get; }
}

/// <summary>几何公差</summary>
public interface IGeometricTolerance : ITolerance
{
    /// <summary>几何公差子类型</summary>
    Models.GeometricToleranceKind Kind { get; }

    /// <summary>GD&T 特征符号</summary>
    Models.GdntCharacteristic Characteristic { get; }

    /// <summary>公差带形状</summary>
    Models.ToleranceZoneShape ZoneShape { get; }

    /// <summary>材料条件</summary>
    Models.MaterialCondition MaterialCondition { get; }

    /// <summary>基准参考列表</summary>
    IReadOnlyList<IDatumReference> DatumReferences { get; }
}

/// <summary>基准参考</summary>
public interface IDatumReference
{
    /// <summary>基准标识</summary>
    string DatumId { get; }

    /// <summary>基准标签（如 A、B、C）</summary>
    string Label { get; }

    /// <summary>基准修饰符</summary>
    string? Modifier { get; }

    /// <summary>关联基元</summary>
    IPrimitive? DatumPrimitive { get; }
}
