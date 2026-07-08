namespace TZTEK.VispecCMM.Import.Core.Planning;

internal sealed class DefaultMeasurementTaskAssembler : IMeasurementTaskAssembler
{
    public MeasurementTask Assemble(
        IPrimitive primitive,
        ITolerance? tolerance,
        IProbe probe,
        IReadOnlyList<MeasurementPoint> points)
    {
        var item = new PrimitiveToleranceItem
        {
            Primitive = (Primitive)primitive,
            Tolerances = tolerance is Tolerance concreteTolerance ? [concreteTolerance] : []
        };

        return new MeasurementTask
        {
            TaskId = $"task_{primitive.Id}",
            Name = $"Measure {primitive.Name}",
            Steps =
            [
                new MeasurementStep
                {
                    SequenceNumber = 1,
                    StepType = MeasurementStepType.Measurement,
                    Name = $"Measure {primitive.Name}",
                    TargetItem = item,
                    ProbeAssignment = probe,
                    MeasurementPoints = points,
                    FittingMethod = SelectFittingMethod(primitive)
                }
            ],
            ProbeConfigurations = [probe]
        };
    }

    public IReadOnlyList<MeasurementTask> AssembleBatch(
        IReadOnlyList<(IPrimitive Primitive, ITolerance? Tolerance, IProbe Probe, IReadOnlyList<MeasurementPoint> Points)> inputs)
    {
        return inputs
            .Select(input => Assemble(input.Primitive, input.Tolerance, input.Probe, input.Points))
            .ToList();
    }

    internal static FittingMethodType SelectFittingMethod(IPrimitive primitive)
    {
        return primitive.PrimitiveType switch
        {
            PrimitiveType.Circle => FittingMethodType.LeastSquares,
            PrimitiveType.Plane => FittingMethodType.LeastSquares,
            PrimitiveType.Cylinder => FittingMethodType.LeastSquares,
            PrimitiveType.Sphere => FittingMethodType.LeastSquares,
            PrimitiveType.Cone => FittingMethodType.LeastSquares,
            _ => FittingMethodType.LeastSquares
        };
    }
}
