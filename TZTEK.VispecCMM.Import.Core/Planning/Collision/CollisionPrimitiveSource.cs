namespace TZTEK.VispecCMM.Import.Core.Planning.Collision;

/// <summary>
/// 碰撞检测使用的工件基元来源。
/// </summary>
internal static class CollisionPrimitiveSource
{
    public static IReadOnlyList<Primitive> Resolve(
        MeasurementTask task,
        MeasurementPlanOptions? options = null)
    {
        if (task.CollisionPrimitives.Count > 0)
            return task.CollisionPrimitives;

        if (options?.CollisionPrimitives.Count > 0)
            return options.CollisionPrimitives;

        return task.Steps
            .Select(step => step.TargetItem?.Primitive)
            .OfType<Primitive>()
            .DistinctBy(primitive => primitive.Id)
            .ToList();
    }
}
