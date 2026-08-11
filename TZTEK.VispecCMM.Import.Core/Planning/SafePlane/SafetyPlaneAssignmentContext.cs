namespace TZTEK.VispecCMM.Import.Core.Planning.SafePlane;

internal sealed class SafetyPlaneAssignmentContext
{
    public required SafetyPlaneBox Envelope { get; init; }

    public IReadOnlyList<Primitive> CollisionPrimitives { get; init; } = [];

    public MeasurementPlanOptions Options { get; init; } = new();

    public PrimitiveToleranceItem? TargetItem { get; init; }
}
