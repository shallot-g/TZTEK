namespace TZTEK.VispecCMM.Import.Core.Planning;

/// <summary>
/// 碰撞检测必须使用全工件基元；测量路径步骤只包含已选待测基元。
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
