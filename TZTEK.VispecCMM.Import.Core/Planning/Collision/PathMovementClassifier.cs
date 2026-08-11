namespace TZTEK.VispecCMM.Import.Core.Planning.Collision;

internal enum CollisionTransitKind
{
    None,
    FeatureEntry,
    FeatureExit,
    IntraFeature
}

internal static class PathMovementClassifier
{
    public static CollisionTransitKind Classify(MeasurementStep step)
    {
        if (step.StepType != MeasurementStepType.Movement)
            return CollisionTransitKind.None;

        return step.MovementKind switch
        {
            "Descend to approach point" => CollisionTransitKind.FeatureEntry,
            "Return to safety plane" => CollisionTransitKind.FeatureExit,
            "Move to next approach point" => CollisionTransitKind.IntraFeature,
            _ => CollisionTransitKind.None
        };
    }

    public static bool RequiresCollisionCheck(MeasurementStep step) =>
        Classify(step) != CollisionTransitKind.None;
}
