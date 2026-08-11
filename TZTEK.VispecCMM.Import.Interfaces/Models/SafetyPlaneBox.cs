namespace TZTEK.VispecCMM.Import.Interfaces.Models;

/// <summary>
/// 由六个安全平面构成的工件包围盒（含底面）。
/// </summary>
public sealed class SafetyPlaneBox
{
    public double MinX { get; set; }
    public double MinY { get; set; }
    public double MinZ { get; set; }
    public double MaxX { get; set; }
    public double MaxY { get; set; }
    public double MaxZ { get; set; }
    public double ClearanceMm { get; set; }

    /// <summary>六个面的安全平面，键为 <see cref="SafetyPlaneFace"/>。</summary>
    public IReadOnlyDictionary<SafetyPlaneFace, SafetyPlane> Planes { get; set; }
        = new Dictionary<SafetyPlaneFace, SafetyPlane>();

    public double CenterX => (MinX + MaxX) / 2.0;
    public double CenterY => (MinY + MaxY) / 2.0;
    public double CenterZ => (MinZ + MaxZ) / 2.0;
    public double SizeX => MaxX - MinX;
    public double SizeY => MaxY - MinY;
    public double SizeZ => MaxZ - MinZ;

    public SafetyPlane? GetPlane(SafetyPlaneFace face) =>
        Planes.TryGetValue(face, out var plane) ? plane : null;

    /// <summary>返回所有六个面（固定顺序）。</summary>
    public IEnumerable<SafetyPlane> GetAllPlanes()
    {
        foreach (SafetyPlaneFace face in Enum.GetValues<SafetyPlaneFace>())
        {
            if (Planes.TryGetValue(face, out var plane))
                yield return plane;
        }
    }

    /// <summary>
    /// 将空间点投影到指定安全面上，得到该面上的安全位置。
    /// </summary>
    public (double X, double Y, double Z) ProjectPoint(SafetyPlaneFace face, (double X, double Y, double Z) point) =>
        face switch
        {
            SafetyPlaneFace.Top => (point.X, point.Y, MaxZ),
            SafetyPlaneFace.Bottom => (point.X, point.Y, MinZ),
            SafetyPlaneFace.PosX => (MaxX, point.Y, point.Z),
            SafetyPlaneFace.NegX => (MinX, point.Y, point.Z),
            SafetyPlaneFace.PosY => (point.X, MaxY, point.Z),
            SafetyPlaneFace.NegY => (point.X, MinY, point.Z),
            _ => point
        };

    /// <summary>
    /// 计算测点相对各面的 cubemap 投影距离，用于选择最合适的进入面。
    /// </summary>
    public IReadOnlyDictionary<SafetyPlaneFace, double> GetFaceProjectionDistances((double X, double Y, double Z) point)
    {
        return new Dictionary<SafetyPlaneFace, double>
        {
            [SafetyPlaneFace.Top] = MaxZ - point.Z,
            [SafetyPlaneFace.Bottom] = point.Z - MinZ,
            [SafetyPlaneFace.PosX] = MaxX - point.X,
            [SafetyPlaneFace.NegX] = point.X - MinX,
            [SafetyPlaneFace.PosY] = MaxY - point.Y,
            [SafetyPlaneFace.NegY] = point.Y - MinY
        };
    }
}
