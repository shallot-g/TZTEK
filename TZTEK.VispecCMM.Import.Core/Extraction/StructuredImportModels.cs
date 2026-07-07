namespace TZTEK.VispecCMM.Import.Core.Extraction;

internal sealed class StructuredImportPayload
{
    public List<StructuredPrimitiveDto> Primitives { get; set; } = [];
    public List<StructuredToleranceDto> Tolerances { get; set; } = [];
    public List<StructuredLinkDto> Links { get; set; } = [];
    public List<StructuredDatumDto> Datums { get; set; } = [];
    public List<object> CoordinateSystems { get; set; } = [];
    public List<object> UncertainItems { get; set; } = [];
}

internal sealed class StructuredPrimitiveDto
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string SourceElementId { get; set; } = string.Empty;
    public string? Name { get; set; }
    public double[]? Point { get; set; }
    public double[]? Center { get; set; }
    public double[]? Start { get; set; }
    public double[]? End { get; set; }
    public double[]? Direction { get; set; }
    public double[]? Normal { get; set; }
    public double[]? AxisPoint { get; set; }
    public double[]? AxisDirection { get; set; }
    public double[]? Apex { get; set; }
    public double? Radius { get; set; }
    public double? StartAngleRad { get; set; }
    public double? EndAngleRad { get; set; }
    public double? HalfAngleRad { get; set; }
    public List<double[]> Points { get; set; } = [];
    public bool? IsClosed { get; set; }
    public int? Degree { get; set; }
    public List<double[]> Vertices { get; set; } = [];
    public List<int[]> Triangles { get; set; } = [];
    public string? SurfaceType { get; set; }
}

internal sealed class StructuredToleranceDto
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string SourceElementId { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? DimensionType { get; set; }
    public double? NominalValue { get; set; }
    public double? UpperDeviation { get; set; }
    public double? LowerDeviation { get; set; }
    public double? ToleranceValue { get; set; }
    public string? ToleranceStandard { get; set; }
    public string? Characteristic { get; set; }
    public string? ZoneShape { get; set; }
    public string? MaterialCondition { get; set; }
    public List<StructuredDatumDto> Datums { get; set; } = [];
}

internal sealed class StructuredLinkDto
{
    public string PrimitiveId { get; set; } = string.Empty;
    public List<string> ToleranceIds { get; set; } = [];
}

internal sealed class StructuredDatumDto
{
    public string Label { get; set; } = string.Empty;
    public string? MaterialCondition { get; set; }
}
