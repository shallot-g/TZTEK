namespace TZTEK.VispecCMM.Import.Core.Planning;

internal interface IFeatureRecognizer
{
    IReadOnlyList<PrimitiveToleranceItem> Recognize(
        IReadOnlyList<PrimitiveToleranceItem> items,
        MeasurementPlanOptions options);
}
