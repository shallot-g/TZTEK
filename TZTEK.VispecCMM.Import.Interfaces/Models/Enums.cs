namespace TZTEK.VispecCMM.Import.Interfaces.Models;

/// <summary>元素类型</summary>
public enum ElementType
{
    Primitive = 0,
    Tolerance = 1,
    Probe = 2,
    SafetyPlane = 3,
    CoordinateSystem = 4
}

/// <summary>基元几何类型</summary>
public enum PrimitiveType
{
    Point,
    Line,
    Circle,
    Arc,
    Plane,
    Cylinder,
    Sphere,
    Cone,
    Curve2D,
    Surface3D
}

/// <summary>公差类型</summary>
public enum ToleranceType
{
    Dimensional,
    Geometric
}

/// <summary>尺寸类型</summary>
public enum DimensionType
{
    Linear,
    Diameter,
    Radius,
    Angle
}

/// <summary>尺寸公差子类型</summary>
public enum DimensionalToleranceKind
{
    Linear,
    Diameter,
    Radius,
    Angle
}

/// <summary>几何公差子类型</summary>
public enum GeometricToleranceKind
{
    Straightness,
    Flatness,
    Circularity,
    Cylindricity,
    ProfileOfLine,
    ProfileOfSurface,
    Parallelism,
    Perpendicularity,
    Angularity,
    Position,
    Concentricity,
    Symmetry,
    CircularRunout,
    TotalRunout
}

/// <summary>GD&amp;T 特征符号（14 种）</summary>
public enum GdntCharacteristic
{
    Straightness,
    Flatness,
    Circularity,
    Cylindricity,
    ProfileOfLine,
    ProfileOfSurface,
    Parallelism,
    Perpendicularity,
    Angularity,
    Position,
    Concentricity,
    Symmetry,
    CircularRunout,
    TotalRunout
}

/// <summary>公差带形状</summary>
public enum ToleranceZoneShape
{
    None,
    TwoParallelPlanes,
    Cylindrical,
    Spherical,
    TwoParallelLines,
    TwoConcentricCircles
}

/// <summary>材料条件</summary>
public enum MaterialCondition
{
    RFS,
    MMC,
    LMC
}

/// <summary>导入源类型</summary>
public enum ImportSourceType
{
    Drawing2D,
    Model3D
}

/// <summary>长度单位</summary>
public enum LengthUnit
{
    Millimeter,
    Inch
}

/// <summary>文件格式</summary>
public enum FileFormat
{
    DXF,
    DWG,
    PDF,
    STEP,
    IGES,
    STL,
    OBJ
}

/// <summary>路径优化策略</summary>
public enum PathOptimizationStrategy
{
    NearestNeighbor,
    TwoOpt,
    GeneticAlgorithm,
    AntColony,
    LinKernighan
}

/// <summary>拟合方法</summary>
public enum FittingMethodType
{
    LeastSquares,
    MinimumZone,
    MaximumInscribed,
    MinimumCircumscribed
}

/// <summary>公差评定标准</summary>
public enum ToleranceStandard
{
    ASME,
    ISO
}

/// <summary>测量步骤类型</summary>
public enum MeasurementStepType
{
    Measurement,
    Movement,
    ProbeChange,
    Lighting,
    SafetyPlane,
    DatumEstablishment
}

/// <summary>探针类型</summary>
public enum ProbeType
{
    TouchTrigger,
    Scanning,
    Optical,
    Laser
}

/// <summary>探针分配状态</summary>
public enum ProbeAssignStatus
{
    Unassigned,
    Assigned,
    Recommended
}

/// <summary>坐标系类型</summary>
public enum CoordinateSystemType
{
    Workpiece,
    Machine,
    Local
}
