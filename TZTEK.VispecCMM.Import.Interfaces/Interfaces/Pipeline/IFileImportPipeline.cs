using TZTEK.VispecCMM.Import.Interfaces.Models;

namespace TZTEK.VispecCMM.Import.Interfaces.Interfaces.Pipeline;

/// <summary>
/// 文件导入管道。
/// </summary>
public interface IFileImportPipeline
{
    IReadOnlyList<string> SupportedExtensions { get; }
    bool CanImport(string filePath);
    Task<ImportResult> ImportAsync(string filePath, ImportOptions options, CancellationToken ct = default);
}
