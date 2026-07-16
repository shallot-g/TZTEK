namespace TZTEK.VispecCMM.Import.Interfaces.Models;

/// <summary>
/// 工件原始三角网格，用于可视化与碰撞粗检缓存。
/// </summary>
public sealed class WorkpieceMesh
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int SolidIndex { get; set; }
    public IReadOnlyList<double> Vertices { get; set; } = [];
    public IReadOnlyList<int> Triangles { get; set; } = [];
    public IReadOnlyList<double> Normals { get; set; } = [];
    public int VertexCount => Vertices.Count / 3;
    public int TriangleCount => Triangles.Count / 3;
}
