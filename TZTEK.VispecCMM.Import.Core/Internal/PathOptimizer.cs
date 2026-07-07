namespace TZTEK.VispecCMM.Import.Core.Internal;

/// <summary>
/// 测量路径优化（TSP）内部引擎占位实现。
/// </summary>
internal sealed class PathOptimizer
{
    public IReadOnlyList<IMeasurableElement> Optimize(
        IReadOnlyList<IMeasurableElement> elements,
        PathOptimizationOptions? options)
    {
        if (elements.Count <= 1)
            return elements;

        return options?.Strategy switch
        {
            PathOptimizationStrategy.NearestNeighbor => NearestNeighbor(elements, options),
            _ => TwoOpt(elements, options)
        };
    }

    private static IReadOnlyList<IMeasurableElement> NearestNeighbor(
        IReadOnlyList<IMeasurableElement> elements,
        PathOptimizationOptions? options)
    {
        var remaining = elements.ToList();
        var result = new List<IMeasurableElement>();
        var current = ResolveStartPoint(options);

        while (remaining.Count > 0)
        {
            var next = remaining
                .OrderBy(e => Distance(current, e.GetPosition()))
                .First();

            result.Add(next);
            remaining.Remove(next);
            current = next.GetPosition();
        }

        if (options?.ReturnToStart == true && result.Count > 0)
            result.Add(result[0]);

        return result;
    }

    private static IReadOnlyList<IMeasurableElement> TwoOpt(
        IReadOnlyList<IMeasurableElement> elements,
        PathOptimizationOptions? options)
    {
        // 骨架阶段：先按 NearestNeighbor 排序，后续替换为完整 2-opt 实现
        return NearestNeighbor(elements, options);
    }

    private static (double X, double Y, double Z) ResolveStartPoint(PathOptimizationOptions? options)
    {
        if (options?.StartPoint is { } start)
            return start;

        return (0, 0, 0);
    }

    private static double Distance(
        (double X, double Y, double Z) a,
        (double X, double Y, double Z) b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        var dz = a.Z - b.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}
