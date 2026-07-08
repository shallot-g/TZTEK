namespace TZTEK.VispecCMM.Import.Core.Planning;

internal sealed class DefaultProbeAssigner : IProbeAssigner
{
    public IReadOnlyList<IProbe> Assign(
        IReadOnlyList<IPrimitive> primitives,
        IReadOnlyList<IProbe> availableProbes)
    {
        var baseProbe = availableProbes.FirstOrDefault() ?? CreateDefaultProbe();

        return primitives
            .Select((primitive, index) => CloneAssignedProbe(baseProbe, primitive, index + 1))
            .ToList();
    }

    private static Probe CreateDefaultProbe()
    {
        return new Probe
        {
            Id = "probe_default_touch",
            Name = "Default Touch Probe",
            ProbeType = ProbeType.TouchTrigger,
            TipDiameter = 2.0,
            TipLength = 20.0,
            BallDiameterMm = 2.0,
            StemLengthMm = 20.0,
            AngleADeg = 0,
            AngleBDeg = 0
        };
    }

    private static Probe CloneAssignedProbe(IProbe source, IPrimitive primitive, int index)
    {
        return new Probe
        {
            Id = string.IsNullOrWhiteSpace(source.Id)
                ? $"probe_assigned_{index}"
                : $"{source.Id}_{index}",
            Name = string.IsNullOrWhiteSpace(source.Name) ? "Assigned Probe" : source.Name,
            ProbeType = source.ProbeType,
            TipDiameter = source.TipDiameter,
            TipLength = source.TipLength,
            BallDiameterMm = source.TipDiameter,
            StemLengthMm = source.TipLength,
            AssignedPrimitive = primitive,
            AssignedPrimitives = [primitive]
        };
    }
}
