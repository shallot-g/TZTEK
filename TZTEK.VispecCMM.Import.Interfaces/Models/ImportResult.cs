namespace TZTEK.VispecCMM.Import.Interfaces.Models;

/// <summary>
/// 导入结果。
/// </summary>
public sealed class ImportResult
{
    public ImportSourceType SourceType { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public IReadOnlyList<RawElement> RawElements { get; set; } = [];
    public IReadOnlyList<PrimitiveToleranceItem> Items { get; set; } = [];
}

/// <summary>
/// 原始元素，对应源文件中的一个实体。
/// </summary>
public sealed class RawElement
{
    public string Id { get; set; } = string.Empty;
    public string ElementType { get; set; } = string.Empty;
    public object? GeometryData { get; set; }
    public IReadOnlyDictionary<string, string> Annotations { get; set; } = new Dictionary<string, string>();
}

/// <summary>
/// 原始文档，导入前的完整中间表示。
/// </summary>
public sealed class RawDocument
{
    public ImportSourceType SourceType { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public IReadOnlyList<RawElement> Elements { get; set; } = [];

    public RawDocument() { }

    public RawDocument(ImportSourceType sourceType, string filePath, IReadOnlyList<RawElement> elements)
    {
        SourceType = sourceType;
        FilePath = filePath;
        Elements = elements;
    }
}
