namespace TZTEK.VispecCMM.Import.Interfaces.Models;

/// <summary>
/// 命名规则。
/// </summary>
public sealed class NamingRule
{
    public string RuleName { get; set; } = "Default";
    public IReadOnlyDictionary<PrimitiveType, string> PrimitiveNamePatterns { get; set; } =
        new Dictionary<PrimitiveType, string>();
    public string ToleranceNamePattern { get; set; } = "{Characteristic}_{PrimitiveName}";
    public int StartNumber { get; set; } = 1;
    public bool NumberByType { get; set; } = true;
    public bool IncludeSourceInfo { get; set; }

    public static NamingRule CreateDefault() => new()
    {
        RuleName = "Default",
        PrimitiveNamePatterns = new Dictionary<PrimitiveType, string>
        {
            [PrimitiveType.Circle] = "C{0}",
            [PrimitiveType.Plane] = "PL{0}",
            [PrimitiveType.Cylinder] = "CY{0}",
            [PrimitiveType.Point] = "PT{0}",
            [PrimitiveType.Line] = "LN{0}",
            [PrimitiveType.Arc] = "A{0}",
            [PrimitiveType.Sphere] = "SP{0}",
            [PrimitiveType.Cone] = "CN{0}",
            [PrimitiveType.Curve2D] = "CV{0}",
            [PrimitiveType.Surface3D] = "SF{0}"
        },
        ToleranceNamePattern = "{Characteristic}_{PrimitiveName}",
        StartNumber = 1,
        NumberByType = true
    };
}
