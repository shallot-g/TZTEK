namespace TZTEK.VispecCMM.Import.Core.Extraction;

/// <summary>
/// 将格式相关的 RawDocument 转换为测量软件可消费的基元-公差关联项。
/// </summary>
public interface IPrimitiveToleranceExtractor
{
    Task<IReadOnlyList<PrimitiveToleranceItem>> ExtractAsync(
        RawDocument document,
        ImportOptions options,
        CancellationToken ct = default);
}
