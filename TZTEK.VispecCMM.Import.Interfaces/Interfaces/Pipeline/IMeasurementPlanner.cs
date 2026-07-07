using TZTEK.VispecCMM.Import.Interfaces.Models;

namespace TZTEK.VispecCMM.Import.Interfaces.Interfaces.Pipeline;

/// <summary>
/// 测量规划器。
/// </summary>
public interface IMeasurementPlanner
{
    /// <summary>根据基元、公差与探针生成测量计划</summary>
    IReadOnlyList<MeasurementPoint> PlanPoints(
    IPrimitive primitive,
    MeasurementPointDistribution? distribution = null,
    FittingMethodType? fittingMethod = null);

MeasurementPointDistribution GetDefaultDistribution(PrimitiveType type);
FittingMethodType GetDefaultFittingMethod(PrimitiveType type);

ISafetyPlane GenerateGlobalSafetyPlane(IReadOnlyList<IPrimitive> primitives, double offsetMm = 10.0);
IReadOnlyList<ISafetyPlane> GenerateLocalSafetyPlanes(IReadOnlyList<IPrimitive> primitives, double offsetMm = 5.0);

CollisionResult CheckSegment(
    (double X, double Y, double Z) fromPoint,
    (double X, double Y, double Z) toPoint,
    IProbe probe, IReadOnlyList<IPrimitive> partGeometry,
    IReadOnlyList<IPrimitive>? fixturePrimitives = null);

Task<CollisionResult> CheckFullPathAsync(
    IReadOnlyList<(double X, double Y, double Z)> path,
    IReadOnlyList<IProbe> probeAssignments,
    IReadOnlyList<IPrimitive> partGeometry,
    IReadOnlyList<IPrimitive>? fixturePrimitives = null,
    IProgress<ImportProgress>? progress = null,
    CancellationToken cancellationToken = default);

GotoPoint? GenerateAvoidancePoint(CollisionEvent collisionEvent, IProbe probe, IReadOnlyList<IPrimitive> partGeometry);
}