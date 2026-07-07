using TZTEK.VispecCMM.Import.Interfaces.Models;

namespace TZTEK.VispecCMM.Import.Interfaces.Interfaces.Pipeline;

/// <summary>
/// 文件导入管道。
/// </summary>
public interface IFileImportPipeline
{
    /// <summary>执行导入管道</summary>
    Task<ImportResult> ExecuteAsync(
        string filePath,
        ImportOptions options,
        IProgress<ImportProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
