namespace TZTEK.VispecCMM.Import.Interfaces.Models;

/// <summary>
/// 测量场景对象类型。
/// </summary>
public enum SceneObjectKind
{
    WorkpieceMesh,
    PrimitiveOutline,
    MeasurementPoint,
    PathSegment,
    GotoPoint,
    SafetyPlane
}

/// <summary>
/// 场景三维向量。
/// </summary>
public sealed class SceneVector3
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }

    public SceneVector3() { }

    public SceneVector3(double x, double y, double z)
    {
        X = x;
        Y = y;
        Z = z;
    }
}

/// <summary>
/// 场景颜色。
/// </summary>
public sealed class SceneColor
{
    public byte R { get; set; }
    public byte G { get; set; }
    public byte B { get; set; }
    public byte A { get; set; } = 255;

    public SceneColor() { }

    public SceneColor(byte r, byte g, byte b, byte a = 255)
    {
        R = r;
        G = g;
        B = b;
        A = a;
    }
}

/// <summary>
/// 场景包围盒。
/// </summary>
public sealed class SceneBounds
{
    public SceneVector3 Min { get; set; } = new();
    public SceneVector3 Max { get; set; } = new();
}

/// <summary>
/// 单个场景可视化对象。
/// </summary>
public sealed class MeasurementSceneObject
{
    public string Id { get; set; } = string.Empty;
    public SceneObjectKind Kind { get; set; }
    public string? Label { get; set; }
    public SceneColor Color { get; set; } = new(200, 200, 200);
    public double Opacity { get; set; } = 1.0;
    public IReadOnlyList<SceneVector3> Points { get; set; } = [];
    public IReadOnlyList<double> Vertices { get; set; } = [];
    public IReadOnlyList<int> Triangles { get; set; } = [];
    public IReadOnlyList<double> Normals { get; set; } = [];
    public string? RelatedPrimitiveId { get; set; }
    public string? RelatedStepName { get; set; }
}

/// <summary>
/// 测量规划可视化场景。
/// </summary>
public sealed class MeasurementScene
{
    public string Title { get; set; } = string.Empty;
    public string SourceFilePath { get; set; } = string.Empty;
    public SceneBounds Bounds { get; set; } = new();
    public IReadOnlyList<MeasurementSceneObject> Objects { get; set; } = [];
}

/// <summary>
/// 场景构建选项。
/// </summary>
public sealed class MeasurementSceneOptions
{
    public bool IncludeWorkpieceMeshes { get; set; } = true;
    public bool IncludePrimitiveOutlines { get; set; } = true;
    public bool IncludeMeasurementPoints { get; set; } = true;
    public bool IncludePathSegments { get; set; } = true;
    public bool IncludeGotoPoints { get; set; } = true;
    public bool IncludeSafetyPlane { get; set; } = true;
    public double PrimitiveOutlineScale { get; set; } = 1.0;
}
