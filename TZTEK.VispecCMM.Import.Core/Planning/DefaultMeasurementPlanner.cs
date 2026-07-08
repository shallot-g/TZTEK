namespace TZTEK.VispecCMM.Import.Core.Planning;

internal sealed class DefaultMeasurementPlanner : IMeasurementPlanner
{
    private readonly IFeatureRecognizer _featureRecognizer;
    private readonly IMeasurementPointPlanner _pointPlanner;
    private readonly IProbeAssigner _probeAssigner;
    private readonly IMeasurementPresetProvider _presetProvider;

    public DefaultMeasurementPlanner(
        IFeatureRecognizer featureRecognizer,
        IMeasurementPointPlanner pointPlanner,
        IProbeAssigner probeAssigner,
        IMeasurementPresetProvider presetProvider)
    {
        _featureRecognizer = featureRecognizer;
        _pointPlanner = pointPlanner;
        _probeAssigner = probeAssigner;
        _presetProvider = presetProvider;
    }

    public IReadOnlyList<MeasurementTask> Plan(
        IReadOnlyList<PrimitiveToleranceItem> items,
        IReadOnlyList<IProbe> probes,
        MeasurementPlanOptions options)
    {
        var candidates = _featureRecognizer.Recognize(items, options).ToList();
        if (candidates.Count == 0)
            return [];

        ApplyNames(candidates, options.NamingRule ?? NamingRule.CreateDefault());

        var primitives = candidates.Select(item => (IPrimitive)item.Primitive).ToList();
        var assignedProbes = _probeAssigner.Assign(primitives, probes);
        var steps = new List<MeasurementStep>();
        var allPoints = new List<MeasurementPoint>();

        for (var i = 0; i < candidates.Count; i++)
        {
            var item = candidates[i];
            var primitive = item.Primitive;
            var probe = assignedProbes[Math.Min(i, assignedProbes.Count - 1)];
            var points = _pointPlanner.PlanPoints(primitive, options);

            primitive.AssignedProbeId = probe.Id;
            allPoints.AddRange(points);

            steps.Add(new MeasurementStep
            {
                SequenceNumber = steps.Count + 1,
                StepType = MeasurementStepType.Measurement,
                Name = $"Measure {primitive.Name}",
                TargetItem = item,
                ProbeAssignment = probe,
                MeasurementPoints = points,
                FittingMethod = DefaultMeasurementTaskAssembler.SelectFittingMethod(primitive),
                TravelDistanceMm = EstimatePointPathLength(points),
                EstimatedTimeSeconds = EstimatePointPathLength(points) / 20.0
            });
        }

        var totalLength = EstimateTaskPathLength(steps, options.StartPoint);
        var preset = _presetProvider.GetPreset(options);

        return
        [
            new MeasurementTask
            {
                TaskId = $"task_{Guid.NewGuid():N}",
                Name = "Generated CMM Measurement Task",
                CreatedAt = DateTime.UtcNow,
                ToleranceStandard = options.ToleranceStandard,
                LengthUnit = options.Unit,
                Steps = steps,
                ProbeConfigurations = assignedProbes,
                GlobalSafetyPlane = CreateSafetyPlane(allPoints, preset),
                PathOptimizationStrategy = options.PathStrategy,
                TotalPathLengthMm = totalLength,
                EstimatedTotalTimeSeconds = totalLength / 20.0
            }
        ];
    }

    private static void ApplyNames(IReadOnlyList<PrimitiveToleranceItem> items, NamingRule namingRule)
    {
        var counters = new Dictionary<PrimitiveType, int>();
        var globalCounter = namingRule.StartNumber;

        foreach (var item in items)
        {
            var primitive = item.Primitive;
            var number = namingRule.NumberByType
                ? NextTypeNumber(counters, primitive.PrimitiveType, namingRule.StartNumber)
                : globalCounter++;

            if (namingRule.PrimitiveNamePatterns.TryGetValue(primitive.PrimitiveType, out var pattern))
                primitive.Name = string.Format(pattern, number);
            else
                primitive.Name = $"{primitive.PrimitiveType}{number}";

            foreach (var tolerance in item.Tolerances)
            {
                var characteristic = tolerance is GeometricTolerance geometric
                    ? geometric.Characteristic.ToString()
                    : tolerance.ToleranceType.ToString();
                tolerance.Name = namingRule.ToleranceNamePattern
                    .Replace("{Characteristic}", characteristic, StringComparison.Ordinal)
                    .Replace("{PrimitiveName}", primitive.Name, StringComparison.Ordinal);
                tolerance.TargetPrimitive = primitive;
            }
        }
    }

    private static int NextTypeNumber(
        IDictionary<PrimitiveType, int> counters,
        PrimitiveType primitiveType,
        int startNumber)
    {
        if (!counters.TryGetValue(primitiveType, out var current))
            current = startNumber - 1;

        current++;
        counters[primitiveType] = current;
        return current;
    }

    private static SafetyPlane CreateSafetyPlane(IReadOnlyList<MeasurementPoint> points, MeasurementPreset preset)
    {
        var z = points.Count == 0 ? preset.SafetyClearanceMm : points.Max(point => point.Z) + preset.SafetyClearanceMm;
        return new SafetyPlane
        {
            Id = "global_safety_plane",
            Name = "Global Safety Plane",
            PointX = 0,
            PointY = 0,
            PointZ = z,
            NormalX = 0,
            NormalY = 0,
            NormalZ = 1,
            OffsetMm = 0
        };
    }

    private static double EstimateTaskPathLength(
        IReadOnlyList<MeasurementStep> steps,
        (double X, double Y, double Z)? startPoint)
    {
        (double X, double Y, double Z)? previous = startPoint;
        var length = 0.0;

        foreach (var step in steps)
        {
            foreach (var point in step.MeasurementPoints ?? [])
            {
                var current = (point.X, point.Y, point.Z);
                if (previous is not null)
                    length += Distance(previous.Value, current);
                previous = current;
            }
        }

        return length;
    }

    private static double EstimatePointPathLength(IReadOnlyList<MeasurementPoint> points)
    {
        var length = 0.0;
        for (var i = 1; i < points.Count; i++)
            length += Distance((points[i - 1].X, points[i - 1].Y, points[i - 1].Z), (points[i].X, points[i].Y, points[i].Z));
        return length;
    }

    private static double Distance(
        (double X, double Y, double Z) left,
        (double X, double Y, double Z) right)
    {
        var dx = left.X - right.X;
        var dy = left.Y - right.Y;
        var dz = left.Z - right.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}
