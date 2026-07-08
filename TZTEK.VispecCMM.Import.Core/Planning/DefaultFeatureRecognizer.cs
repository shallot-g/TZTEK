namespace TZTEK.VispecCMM.Import.Core.Planning;

internal sealed class DefaultFeatureRecognizer : IFeatureRecognizer
{
    public IReadOnlyList<PrimitiveToleranceItem> Recognize(
        IReadOnlyList<PrimitiveToleranceItem> items,
        MeasurementPlanOptions options)
    {
        var filtered = options.EnableFeatureFiltering
            ? items.Where(item => IsMeasurable(item.Primitive, options)).ToList()
            : items.ToList();

        return options.EnableSameFeatureGrouping
            ? MergeSameCylinderCandidates(filtered, options)
            : filtered;
    }

    private static bool IsMeasurable(Primitive primitive, MeasurementPlanOptions options)
    {
        return primitive switch
        {
            PlanePrimitive plane => plane.SourceAreaMm2 is null || plane.SourceAreaMm2 >= options.MinPlaneAreaMm2,
            CylinderPrimitive cylinder => cylinder.Radius >= options.MinCylinderRadiusMm
                && Length((cylinder.AxisDirX, cylinder.AxisDirY, cylinder.AxisDirZ)) > 1e-9,
            CirclePrimitive circle => circle.Radius >= options.MinCylinderRadiusMm,
            ArcPrimitive arc => arc.Radius >= options.MinCylinderRadiusMm,
            SpherePrimitive sphere => sphere.Radius >= options.MinCylinderRadiusMm,
            ConePrimitive cone => cone.HalfAngleRad > 1e-9
                && Length((cone.AxisDirX, cone.AxisDirY, cone.AxisDirZ)) > 1e-9,
            Surface3DPrimitive surface => surface.Vertices.Count > 0,
            _ => true
        };
    }

    private static IReadOnlyList<PrimitiveToleranceItem> MergeSameCylinderCandidates(
        IReadOnlyList<PrimitiveToleranceItem> items,
        MeasurementPlanOptions options)
    {
        var result = new List<PrimitiveToleranceItem>();
        var cylinderItems = new List<PrimitiveToleranceItem>();
        var radiusTolerance = Math.Max(options.Tolerance * 10, 0.01);
        var axisTolerance = Math.Max(options.Tolerance * 20, 0.05);

        foreach (var item in items)
        {
            if (item.Primitive is not CylinderPrimitive cylinder)
            {
                result.Add(item);
                continue;
            }

            var duplicate = cylinderItems.Any(existing =>
                existing.Primitive is CylinderPrimitive existingCylinder
                && IsSameCylinder(cylinder, existingCylinder, radiusTolerance, axisTolerance));

            if (!duplicate)
            {
                cylinderItems.Add(item);
                result.Add(item);
            }
        }

        return result;
    }

    private static bool IsSameCylinder(
        CylinderPrimitive left,
        CylinderPrimitive right,
        double radiusTolerance,
        double axisTolerance)
    {
        if (Math.Abs(left.Radius - right.Radius) > radiusTolerance)
            return false;

        var leftDir = Normalize((left.AxisDirX, left.AxisDirY, left.AxisDirZ));
        var rightDir = Normalize((right.AxisDirX, right.AxisDirY, right.AxisDirZ));
        if (Math.Abs(Dot(leftDir, rightDir)) < 0.995)
            return false;

        var pointDelta = (
            right.AxisPointX - left.AxisPointX,
            right.AxisPointY - left.AxisPointY,
            right.AxisPointZ - left.AxisPointZ);

        return Length(Cross(pointDelta, leftDir)) <= axisTolerance;
    }

    private static (double X, double Y, double Z) Normalize((double X, double Y, double Z) value)
    {
        var length = Length(value);
        return length < 1e-12 ? (0, 0, 1) : (value.X / length, value.Y / length, value.Z / length);
    }

    private static double Length((double X, double Y, double Z) value) =>
        Math.Sqrt(value.X * value.X + value.Y * value.Y + value.Z * value.Z);

    private static double Dot((double X, double Y, double Z) left, (double X, double Y, double Z) right) =>
        left.X * right.X + left.Y * right.Y + left.Z * right.Z;

    private static (double X, double Y, double Z) Cross(
        (double X, double Y, double Z) left,
        (double X, double Y, double Z) right) =>
        (
            left.Y * right.Z - left.Z * right.Y,
            left.Z * right.X - left.X * right.Z,
            left.X * right.Y - left.Y * right.X);
}
