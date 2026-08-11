namespace TZTEK.VispecCMM.Import.Core.Planning.Path;

using TZTEK.VispecCMM.Import.Core.Planning.SafePlane;

internal sealed record FeatureSafetyProjection(
    string PrimitiveId,
    PrimitiveToleranceItem Item,
    (double X, double Y, double Z) RepresentativePoint,
    SafetyPlaneFace AssignedFace,
    (double X, double Y, double Z) ProjectionOnPlane);

internal static class FeatureSafetyProjectionBuilder
{
    public static IReadOnlyList<FeatureSafetyProjection> Build(
        SafetyPlaneBox envelope,
        IReadOnlyList<PrimitiveToleranceItem> items,
        IReadOnlyList<MeasurementStep> measurementSteps,
        SafetyPlaneAssignmentContext context)
    {
        SafetyPlaneFace? previousFace = null;
        var projections = new List<FeatureSafetyProjection>(items.Count);

        foreach (var item in items)
        {
            var primitive = item.Primitive;
            var representative = primitive.GetRepresentativePoint();
            var probePoint = ResolveProbePoint(item, measurementSteps, representative);
            var itemContext = new SafetyPlaneAssignmentContext
            {
                Envelope = context.Envelope,
                CollisionPrimitives = context.CollisionPrimitives,
                Options = context.Options,
                TargetItem = item
            };

            var face = SafetyPlaneAssigner.SelectBestFace(envelope, probePoint, previousFace, itemContext);
            var projection = envelope.ProjectPoint(face, representative);
            previousFace = face;

            projections.Add(new FeatureSafetyProjection(
                primitive.Id,
                item,
                representative,
                face,
                projection));
        }

        return projections;
    }

    private static MeasurementPoint ResolveProbePoint(
        PrimitiveToleranceItem item,
        IReadOnlyList<MeasurementStep> measurementSteps,
        (double X, double Y, double Z) representative)
    {
        var step = measurementSteps.FirstOrDefault(candidate =>
            string.Equals(candidate.TargetItem?.Primitive.Id, item.Primitive.Id, StringComparison.OrdinalIgnoreCase));
        if (step?.MeasurementPoints is { Count: > 0 } points)
            return points[0];

        var direction = item.Primitive.GetDirection();
        return new MeasurementPoint
        {
            X = representative.X,
            Y = representative.Y,
            Z = representative.Z,
            NormalX = direction.I,
            NormalY = direction.J,
            NormalZ = direction.K,
            ApproachDistance = 5.0
        };
    }

    public static FeatureSafetyProjection? FindByPrimitiveId(
        IReadOnlyList<FeatureSafetyProjection> projections,
        string primitiveId) =>
        projections.FirstOrDefault(projection =>
            string.Equals(projection.PrimitiveId, primitiveId, StringComparison.OrdinalIgnoreCase));
}
