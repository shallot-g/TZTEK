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
        var cylinderGroups = new List<List<PrimitiveToleranceItem>>();
        var radiusTolerance = Math.Max(options.Tolerance * 10, 0.01);
        var axisTolerance = Math.Max(options.Tolerance * 20, 0.05);

        foreach (var item in items)
        {
            if (item.Primitive is not CylinderPrimitive cylinder)
            {
                result.Add(item);
                continue;
            }

            var group = cylinderGroups.FirstOrDefault(existing => existing.Any(candidate =>
                candidate.Primitive is CylinderPrimitive existingCylinder
                && CanMergeCylinderFaces(cylinder, existingCylinder, radiusTolerance, axisTolerance)));
            if (group is null)
                cylinderGroups.Add([item]);
            else
                group.Add(item);
        }

        result.AddRange(cylinderGroups.Select(MergeCylinderGroup));

        return result;
    }

    private static bool CanMergeCylinderFaces(
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

        if (Length(Cross(pointDelta, leftDir)) > axisTolerance)
            return false;

        if (left.IsInnerSurface != right.IsInnerSurface)
            return false;

        var leftRange = GetAxisRange(left, leftDir);
        var rightRange = GetAxisRange(right, leftDir);
        if (leftRange is not null && rightRange is not null
            && (leftRange.Value.Max + axisTolerance < rightRange.Value.Min
                || rightRange.Value.Max + axisTolerance < leftRange.Value.Min))
        {
            return false;
        }

        return AnglesTouchOrOverlap(left, right, Math.Max(axisTolerance / Math.Max(left.Radius, 1), 1e-4));
    }

    private static PrimitiveToleranceItem MergeCylinderGroup(IReadOnlyList<PrimitiveToleranceItem> group)
    {
        var firstItem = group[0];
        var first = (CylinderPrimitive)firstItem.Primitive;
        var axis = Normalize((first.AxisDirX, first.AxisDirY, first.AxisDirZ));
        var firstCenter = (first.AxisPointX, first.AxisPointY, first.AxisPointZ);
        var axisBase = Add(firstCenter, Scale(axis, -Dot(firstCenter, axis)));
        var ranges = group.Select(item => GetAxisRange((CylinderPrimitive)item.Primitive, axis))
            .Where(range => range is not null)
            .Select(range => range!.Value)
            .ToList();
        var sourceIds = group.SelectMany(item =>
            ((CylinderPrimitive)item.Primitive).SourceElementIds.Count > 0
                ? ((CylinderPrimitive)item.Primitive).SourceElementIds
                : [item.Primitive.SourceElementId])
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var startAngles = group.Select(item => ((CylinderPrimitive)item.Primitive).StartAngleRad)
            .Where(value => value is not null).Select(value => value!.Value).ToList();
        var endAngles = group.Select(item => ((CylinderPrimitive)item.Primitive).EndAngleRad)
            .Where(value => value is not null).Select(value => value!.Value).ToList();
        var angularSpans = group.Select(item => ((CylinderPrimitive)item.Primitive).AngularSpanRad)
            .Where(value => value is not null).Select(value => value!.Value).ToList();
        var areas = group.Select(item => item.Primitive.SourceAreaMm2)
            .Where(value => value is not null).Select(value => value!.Value).ToList();

        var merged = new CylinderPrimitive
        {
            Id = first.Id,
            Name = first.Name,
            SourceElementId = first.SourceElementId,
            CoordSystemId = first.CoordSystemId,
            AssignedProbeId = first.AssignedProbeId,
            SourceAreaMm2 = areas.Count > 0 ? areas.Sum() : null,
            AxisDirX = axis.X,
            AxisDirY = axis.Y,
            AxisDirZ = axis.Z,
            Radius = first.Radius,
            IsInnerSurface = first.IsInnerSurface,
            SurfaceOrientation = first.SurfaceOrientation,
            SourceElementIds = sourceIds,
            StartAngleRad = startAngles.Count > 0 ? startAngles.Min() : null,
            EndAngleRad = endAngles.Count > 0 ? endAngles.Max() : null,
            AngularSpanRad = angularSpans.Count > 0 ? Math.Min(Math.PI * 2, angularSpans.Sum()) : null,
            RadialReferenceX = first.RadialReferenceX,
            RadialReferenceY = first.RadialReferenceY,
            RadialReferenceZ = first.RadialReferenceZ
        };

        if (ranges.Count > 0)
        {
            var start = Add(axisBase, Scale(axis, ranges.Min(range => range.Min)));
            var end = Add(axisBase, Scale(axis, ranges.Max(range => range.Max)));
            var center = Scale(Add(start, end), 0.5);
            merged.AxisStartX = start.X;
            merged.AxisStartY = start.Y;
            merged.AxisStartZ = start.Z;
            merged.AxisEndX = end.X;
            merged.AxisEndY = end.Y;
            merged.AxisEndZ = end.Z;
            merged.AxisPointX = center.X;
            merged.AxisPointY = center.Y;
            merged.AxisPointZ = center.Z;
            merged.Length = ranges.Max(range => range.Max) - ranges.Min(range => range.Min);
        }
        else
        {
            merged.AxisPointX = first.AxisPointX;
            merged.AxisPointY = first.AxisPointY;
            merged.AxisPointZ = first.AxisPointZ;
            merged.Length = first.Length;
        }

        return new PrimitiveToleranceItem
        {
            Primitive = merged,
            Tolerances = group.SelectMany(item => item.Tolerances).DistinctBy(tolerance => tolerance.Id).ToList()
        };
    }

    private static (double Min, double Max)? GetAxisRange(
        CylinderPrimitive cylinder,
        (double X, double Y, double Z) axis)
    {
        if (cylinder.AxisStartX is null || cylinder.AxisStartY is null || cylinder.AxisStartZ is null
            || cylinder.AxisEndX is null || cylinder.AxisEndY is null || cylinder.AxisEndZ is null)
        {
            return null;
        }

        var start = Dot((cylinder.AxisStartX.Value, cylinder.AxisStartY.Value, cylinder.AxisStartZ.Value), axis);
        var end = Dot((cylinder.AxisEndX.Value, cylinder.AxisEndY.Value, cylinder.AxisEndZ.Value), axis);
        return (Math.Min(start, end), Math.Max(start, end));
    }

    private static bool AnglesTouchOrOverlap(CylinderPrimitive left, CylinderPrimitive right, double tolerance)
    {
        if (left.StartAngleRad is null || left.EndAngleRad is null
            || right.StartAngleRad is null || right.EndAngleRad is null)
        {
            return true;
        }

        return left.StartAngleRad.Value <= right.EndAngleRad.Value + tolerance
            && right.StartAngleRad.Value <= left.EndAngleRad.Value + tolerance;
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

    private static (double X, double Y, double Z) Add(
        (double X, double Y, double Z) left,
        (double X, double Y, double Z) right) =>
        (left.X + right.X, left.Y + right.Y, left.Z + right.Z);

    private static (double X, double Y, double Z) Scale(
        (double X, double Y, double Z) value,
        double scale) =>
        (value.X * scale, value.Y * scale, value.Z * scale);

    private static (double X, double Y, double Z) Cross(
        (double X, double Y, double Z) left,
        (double X, double Y, double Z) right) =>
        (
            left.Y * right.Z - left.Z * right.Y,
            left.Z * right.X - left.X * right.Z,
            left.X * right.Y - left.Y * right.X);
}
