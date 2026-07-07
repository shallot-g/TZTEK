namespace TZTEK.VispecCMM.Import.Core.Importers;

/// <summary>
/// 单一文件格式导入器。新增格式时实现该接口，不需要改动主导入流水线。
/// </summary>
public interface IFileFormatImporter
{
    IReadOnlySet<string> SupportedExtensions { get; }

    bool CanImport(string filePath);

    Task<RawDocument> ParseAsync(
        string filePath,
        ImportOptions options,
        CancellationToken ct = default);
}
