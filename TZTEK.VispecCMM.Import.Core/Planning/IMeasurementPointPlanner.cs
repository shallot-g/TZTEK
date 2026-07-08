namespace TZTEK.VispecCMM.Import.Core.Planning;

internal interface IMeasurementPointPlanner
{
    IReadOnlyList<MeasurementPoint> PlanPoints(Primitive primitive, MeasurementPlanOptions options);
}
