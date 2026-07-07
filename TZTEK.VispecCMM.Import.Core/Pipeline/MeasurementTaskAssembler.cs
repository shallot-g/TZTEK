namespace TZTEK.VispecCMM.Import.Core.Pipeline;

/// <summary>
/// <see cref="IMeasurementTaskAssembler"/> 默认实现（骨架）。
/// </summary>
public sealed class MeasurementTaskAssembler : IMeasurementTaskAssembler
{
    private readonly Dictionary<PrimitiveType, int> _typeCounters = [];

    /// <inheritdoc />
    public NamingRule CurrentRule { get; private set; } = NamingRule.CreateDefault();

    /// <inheritdoc />
    public void SetRule(NamingRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        CurrentRule = rule;
        ResetCounters();
    }

    /// <inheritdoc />
    public string GeneratePrimitiveName(IPrimitive primitive, ISet<string>? existingNames = null)
    {
        ArgumentNullException.ThrowIfNull(primitive);

        if (!CurrentRule.PrimitiveNamePatterns.TryGetValue(primitive.PrimitiveType, out var pattern))
            pattern = "{0}";

        var number = NextNumber(primitive.PrimitiveType);
        var name = string.Format(pattern, number);

        if (existingNames is not null)
        {
            while (existingNames.Contains(name))
            {
                number = NextNumber(primitive.PrimitiveType);
                name = string.Format(pattern, number);
            }

            existingNames.Add(name);
        }

        return name;
    }

    /// <inheritdoc />
    public string GenerateToleranceName(
        ITolerance tolerance,
        string primitiveName,
        ISet<string>? existingNames = null)
    {
        ArgumentNullException.ThrowIfNull(tolerance);

        var characteristic = tolerance is IGeometricTolerance geometric
            ? geometric.Characteristic.ToString()
            : "Dim";

        var name = CurrentRule.ToleranceNamePattern
            .Replace("{Characteristic}", characteristic, StringComparison.Ordinal)
            .Replace("{PrimitiveName}", primitiveName, StringComparison.Ordinal);

        if (existingNames is not null)
        {
            var suffix = 1;
            var candidate = name;
            while (existingNames.Contains(candidate))
            {
                candidate = $"{name}_{suffix++}";
            }

            existingNames.Add(candidate);
            return candidate;
        }

        return name;
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<IPrimitive, string> GeneratePrimitiveNamesBatch(
        IReadOnlyList<IPrimitive> primitives)
    {
        ArgumentNullException.ThrowIfNull(primitives);

        var existingNames = new HashSet<string>(StringComparer.Ordinal);
        return primitives.ToDictionary(
            primitive => primitive,
            primitive => GeneratePrimitiveName(primitive, existingNames));
    }

    /// <inheritdoc />
    public void ResetCounters() => _typeCounters.Clear();

    /// <inheritdoc />
    public Task<MeasurementTask> GenerateAsync(
        ImportResult importResult,
        IReadOnlyList<IPrimitive> optimizedPath,
        NamingRule? namingRule = null,
        ToleranceStandard toleranceStandard = ToleranceStandard.ASME,
        IProgress<ImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (namingRule is not null)
            SetRule(namingRule);

        var names = GeneratePrimitiveNamesBatch(optimizedPath);
        var steps = new List<MeasurementStep>();
        var sequence = 1;

        foreach (var primitive in optimizedPath)
        {
            var stepName = names.TryGetValue(primitive, out var name) ? name : primitive.Name;
            steps.Add(new MeasurementStep
            {
                SequenceNumber = sequence++,
                StepType = MeasurementStepType.Measurement,
                Name = stepName,
                TargetItem = importResult.Items.FirstOrDefault(item => item.Primitive.Id == primitive.Id),
                MeasurementPoints = primitive.PlanPoints()
            });
        }

        progress?.Report(new ImportProgress
        {
            Stage = "TaskAssembly",
            PercentComplete = 90,
            PrimitiveCount = optimizedPath.Count,
            ToleranceCount = importResult.Items.Sum(item => item.Tolerances.Count)
        });

        var task = new MeasurementTask
        {
            TaskId = Guid.NewGuid().ToString("N"),
            Name = Path.GetFileNameWithoutExtension(importResult.FilePath),
            SourceFilePath = importResult.FilePath,
            CreatedAt = DateTime.UtcNow,
            ToleranceStandard = toleranceStandard,
            LengthUnit = LengthUnit.Millimeter,
            Steps = steps
        };

        return Task.FromResult(task);
    }

    private int NextNumber(PrimitiveType type)
    {
        if (!CurrentRule.NumberByType)
        {
            if (!_typeCounters.TryGetValue(PrimitiveType.Point, out var global))
                global = CurrentRule.StartNumber - 1;

            global++;
            _typeCounters[PrimitiveType.Point] = global;
            return global;
        }

        if (!_typeCounters.TryGetValue(type, out var number))
            number = CurrentRule.StartNumber - 1;

        number++;
        _typeCounters[type] = number;
        return number;
    }
}
