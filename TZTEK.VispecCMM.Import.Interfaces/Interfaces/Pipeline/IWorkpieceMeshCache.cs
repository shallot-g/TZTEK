using TZTEK.VispecCMM.Import.Interfaces.Models;

namespace TZTEK.VispecCMM.Import.Interfaces.Interfaces.Pipeline;

/// <summary>
/// 工件原始网格磁盘缓存。
/// </summary>
public interface IWorkpieceMeshCache
{
    Task<IReadOnlyList<WorkpieceMesh>?> TryLoadAsync(
        string sourceFilePath,
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        string sourceFilePath,
        IReadOnlyList<WorkpieceMesh> meshes,
        CancellationToken cancellationToken = default);

    string GetCacheFilePath(string sourceFilePath);
}
