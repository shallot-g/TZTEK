namespace TZTEK.VispecCMM.Import.Core.Planning;

internal sealed class MeasurementPreset
{
    public double ApproachDistanceMm { get; init; }
    public double RetractDistanceMm { get; init; }
    public double SearchDistanceMm { get; init; }
    public double SafetyClearanceMm { get; init; }
}

internal interface IMeasurementPresetProvider
{
    MeasurementPreset GetPreset(MeasurementPlanOptions options);
}

internal sealed class DefaultMeasurementPresetProvider : IMeasurementPresetProvider
{
    public MeasurementPreset GetPreset(MeasurementPlanOptions options)
    {
        return new MeasurementPreset
        {
            ApproachDistanceMm = options.DefaultApproachDistanceMm,
            RetractDistanceMm = options.DefaultRetractDistanceMm,
            SearchDistanceMm = options.DefaultSearchDistanceMm,
            SafetyClearanceMm = options.SafetyClearanceMm
        };
    }
}
