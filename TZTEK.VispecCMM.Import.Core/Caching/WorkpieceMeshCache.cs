using TZTEK.VispecCMM.Import.Core.Serialization;
using TZTEK.VispecCMM.Import.Interfaces.Interfaces.Pipeline;
using TZTEK.VispecCMM.Import.Interfaces.Models;

namespace TZTEK.VispecCMM.Import.Core.Caching;

/// <summary>
/// 将工件网格缓存到源文件旁的 <c>.vispec-mesh.json</c>。
/// </summary>
public sealed class WorkpieceMeshCache : IWorkpieceMeshCache
{
    public string GetCacheFilePath(string sourceFilePath)
    {
        return $"{sourceFilePath}.vispec-mesh.json";
    }

    public async Task<IReadOnlyList<WorkpieceMesh>?> TryLoadAsync(
        string sourceFilePath,
        CancellationToken cancellationToken = default)
    {
        var cachePath = GetCacheFilePath(sourceFilePath);
        if (!File.Exists(cachePath) || !File.Exists(sourceFilePath))
            return null;

        var sourceTime = File.GetLastWriteTimeUtc(sourceFilePath);
        var cacheTime = File.GetLastWriteTimeUtc(cachePath);
        if (cacheTime < sourceTime)
            return null;

        var json = await File.ReadAllTextAsync(cachePath, cancellationToken).ConfigureAwait(false);
        var meshes = WorkpieceMeshJsonMapper.DeserializeMeshes(json);
        return meshes.Count == 0 ? null : meshes;
    }

    public async Task SaveAsync(
        string sourceFilePath,
        IReadOnlyList<WorkpieceMesh> meshes,
        CancellationToken cancellationToken = default)
    {
        if (meshes.Count == 0)
            return;

        var cachePath = GetCacheFilePath(sourceFilePath);
        var json = WorkpieceMeshJsonMapper.SerializeMeshes(meshes);
        await File.WriteAllTextAsync(cachePath, json, cancellationToken).ConfigureAwait(false);
    }
}
