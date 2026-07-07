using TZTEK.VispecCMM.Import.Interfaces.Models;

namespace TZTEK.VispecCMM.Import.Interfaces.Interfaces.Pipeline;

/// <summary>
/// 测量任务组装器。
/// </summary>
public interface IMeasurementTaskAssembler
{
    // ── 命名规则 ──
    NamingRule CurrentRule { get; }
    void SetRule(NamingRule rule);
    string GeneratePrimitiveName(IPrimitive primitive, ISet<string>? existingNames = null);
    string GenerateToleranceName(ITolerance tolerance, string primitiveName, ISet<string>? existingNames = null);
    IReadOnlyDictionary<IPrimitive, string> GeneratePrimitiveNamesBatch(IReadOnlyList<IPrimitive> primitives);
    void ResetCounters();

    // ── 任务组装 ──
    Task<MeasurementTask> GenerateAsync(
        ImportResult importResult,
        IReadOnlyList<IPrimitive> optimizedPath,
        NamingRule? namingRule = null,
        ToleranceStandard toleranceStandard = ToleranceStandard.ASME,
        IProgress<ImportProgress>? progress = null,
        CancellationToken cancellationToken = default);
}