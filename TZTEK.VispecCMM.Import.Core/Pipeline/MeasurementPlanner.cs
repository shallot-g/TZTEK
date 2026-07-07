namespace TZTEK.VispecCMM.Import.Core.Pipeline;

/// <summary>
/// <see cref="IMeasurementPlanner"/> 默认实现（骨架）。
/// </summary>
public sealed class MeasurementPlanner : IMeasurementPlanner
{
    /// <inheritdoc />
    public IReadOnlyList<MeasurementPoint> PlanPoints(
        IPrimitive primitive,
        MeasurementPointDistribution? distribution = null,
        FittingMethodType? fittingMethod = null)
    {
        ArgumentNullException.ThrowIfNull(primitive);

        var config = distribution ?? GetDefaultDistribution(primitive.PrimitiveType);
        var position = primitive.GetPosition();
        var direction = primitive.GetDirection();

        // TODO: 按 DistributionPattern 生成真实测点
        return Enumerable.Range(1, Math.Max(config.PointCount, 1))
            .Select(index => new MeasurementPoint
            {
                Index = index,
                X = position.X,
                Y = position.Y,
                Z = position.Z,
                NormalX = direction.I,
                NormalY = direction.J,
                NormalZ = direction.K
            })
            .ToList();
    }

    /// <inheritdoc />
    public MeasurementPointDistribution GetDefaultDistribution(PrimitiveType type) => type switch
    {
        PrimitiveType.Point => new MeasurementPointDistribution { PointCount = 1 },
        PrimitiveType.Line => new MeasurementPointDistribution { PointCount = 3, Pattern = DistributionPattern.Uniform },
        PrimitiveType.Circle => new MeasurementPointDistribution { PointCount = 8, Pattern = DistributionPattern.Uniform },
        PrimitiveType.Arc => new MeasurementPointDistribution { PointCount = 5, Pattern = DistributionPattern.Uniform },
        PrimitiveType.Plane => new MeasurementPointDistribution { PointCount = 25, Pattern = DistributionPattern.Grid },
        PrimitiveType.Cylinder => new MeasurementPointDistribution { PointCount = 24, Pattern = DistributionPattern.Helix },
        PrimitiveType.Sphere => new MeasurementPointDistribution { PointCount = 15, Pattern = DistributionPattern.Uniform },
        PrimitiveType.Cone => new MeasurementPointDistribution { PointCount = 16, Pattern = DistributionPattern.Helix },
        _ => new MeasurementPointDistribution { PointCount = 5, Pattern = DistributionPattern.Uniform }
    };

    /// <inheritdoc />
    public FittingMethodType GetDefaultFittingMethod(PrimitiveType type) => type switch
    {
        PrimitiveType.Plane => FittingMethodType.MinimumZone,
        _ => FittingMethodType.LeastSquares
    };

    /// <inheritdoc />
    public ISafetyPlane GenerateGlobalSafetyPlane(IReadOnlyList<IPrimitive> primitives, double offsetMm = 10.0)
    {
        ArgumentNullException.ThrowIfNull(primitives);

        if (primitives.Count == 0)
        {
            return new SafetyPlane
            {
                Id = "global-safety-plane",
                Name = "GlobalSafetyPlane",
                NormalZ = 1,
                OffsetMm = offsetMm
            };
        }

        var maxZ = primitives.Max(p => p.GetPosition().Z);
        return new SafetyPlane
        {
            Id = "global-safety-plane",
            Name = "GlobalSafetyPlane",
            PointZ = maxZ,
            NormalZ = 1,
            OffsetMm = offsetMm
        };
    }

    /// <inheritdoc />
    public IReadOnlyList<ISafetyPlane> GenerateLocalSafetyPlanes(
        IReadOnlyList<IPrimitive> primitives,
        double offsetMm = 5.0)
    {
        ArgumentNullException.ThrowIfNull(primitives);

        return primitives
            .Select((primitive, index) => (ISafetyPlane)new SafetyPlane
            {
                Id = $"local-safety-plane-{index + 1}",
                Name = $"LocalSafetyPlane_{primitive.Name}",
                PointX = primitive.GetPosition().X,
                PointY = primitive.GetPosition().Y,
                PointZ = primitive.GetPosition().Z,
                NormalX = primitive.GetDirection().I,
                NormalY = primitive.GetDirection().J,
                NormalZ = primitive.GetDirection().K,
                OffsetMm = offsetMm
            })
            .ToList();
    }

    /// <inheritdoc />
    public CollisionResult CheckSegment(
        (double X, double Y, double Z) fromPoint,
        (double X, double Y, double Z) toPoint,
        IProbe probe,
        IReadOnlyList<IPrimitive> partGeometry,
        IReadOnlyList<IPrimitive>? fixturePrimitives = null)
    {
        // TODO: 实现线段碰撞检测
        _ = (fromPoint, toPoint, probe, partGeometry, fixturePrimitives);
        return new CollisionResult
        {
            HasCollision = false,
            TotalSegmentsChecked = 1
        };
    }

    /// <inheritdoc />
    public Task<CollisionResult> CheckFullPathAsync(
        IReadOnlyList<(double X, double Y, double Z)> path,
        IReadOnlyList<IProbe> probeAssignments,
        IReadOnlyList<IPrimitive> partGeometry,
        IReadOnlyList<IPrimitive>? fixturePrimitives = null,
        IProgress<ImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // TODO: 逐段检测完整路径
        _ = (path, probeAssignments, partGeometry, fixturePrimitives, progress);
        return Task.FromResult(new CollisionResult
        {
            HasCollision = false,
            TotalSegmentsChecked = Math.Max(path.Count - 1, 0)
        });
    }

    /// <inheritdoc />
    public GotoPoint? GenerateAvoidancePoint(
        CollisionEvent collisionEvent,
        IProbe probe,
        IReadOnlyList<IPrimitive> partGeometry)
    {
        // TODO: 根据碰撞事件生成避障 GOTO 点
        _ = (probe, partGeometry);
        return collisionEvent.SuggestedAvoidancePoint ?? new GotoPoint
        {
            Id = $"avoidance-{collisionEvent.SegmentIndex}",
            X = collisionEvent.X,
            Y = collisionEvent.Y,
            Z = collisionEvent.Z + 10,
            Reason = "Avoidance"
        };
    }
}
