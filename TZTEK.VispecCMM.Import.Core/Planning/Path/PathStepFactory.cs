namespace TZTEK.VispecCMM.Import.Core.Planning.Path;

internal static class PathStepFactory
{
    internal static MeasurementStep CreateMovementStep(
        MeasurementStep sourceStep,
        int sequenceNumber,
        PlanningVectors.Vec3 target,
        ISafetyPlane? safetyPlane,
        string name,
        string movementKind)
    {
        return new MeasurementStep
        {
            SequenceNumber = sequenceNumber,
            StepType = MeasurementStepType.Movement,
            Name = name,
            TargetItem = sourceStep.TargetItem,
            ProbeAssignment = sourceStep.ProbeAssignment,
            FittingMethod = sourceStep.FittingMethod,
            GotoTarget = new GotoPoint
            {
                Id = $"goto_{sequenceNumber}",
                X = target.X,
                Y = target.Y,
                Z = target.Z,
                Reason = movementKind
            },
            SafetyPlane = safetyPlane,
            MovementKind = movementKind,
            CollisionValidated = false,
            IsExecutable = true
        };
    }

    internal static MeasurementStep CreateMeasurementStep(
        MeasurementStep sourceStep,
        int sequenceNumber,
        MeasurementPoint point,
        SafetyPlaneBox? envelope,
        string name)
    {
        return new MeasurementStep
        {
            SequenceNumber = sequenceNumber,
            StepType = MeasurementStepType.Measurement,
            Name = name,
            TargetItem = sourceStep.TargetItem,
            ProbeAssignment = sourceStep.ProbeAssignment,
            MeasurementPoints = [PathGeometryHelper.ClonePoint(point, 1)],
            FittingMethod = sourceStep.FittingMethod,
            SafetyPlane = ResolveSafetyPlane(envelope, point),
            MovementKind = "Measurement",
            IsExecutable = true
        };
    }

    private static ISafetyPlane? ResolveSafetyPlane(SafetyPlaneBox? envelope, MeasurementPoint point)
    {
        if (envelope is null || point.AssignedSafetyPlaneFace is not { } face)
            return null;

        return envelope.GetPlane(face);
    }
}
