using TZTEK.VispecCMM.Import.Interfaces.Models;

namespace TZTEK.VispecCMM.Import.Interfaces.Interfaces.Pipeline;

/// <summary>
/// 文件导入管道。
/// </summary>
public interface IFileImportPipeline
{
    /// <summary>当前管道支持的文件扩展名</summary>
    IReadOnlyList<string> SupportedExtensions { get; }

    /// <summary>判断指定文件是否可由当前管道导入</summary>
    bool CanImport(string filePath);

    /// <summary>执行导入管道</summary>
    Task<ImportResult> ExecuteAsync(
        string filePath,
        ImportOptions options,
        IProgress<ImportProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>执行导入管道的简化入口</summary>
    Task<ImportResult> ImportAsync(
        string filePath,
        ImportOptions options,
        CancellationToken cancellationToken = default);
}
