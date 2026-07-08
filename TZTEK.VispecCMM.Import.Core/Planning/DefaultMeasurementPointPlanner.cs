namespace TZTEK.VispecCMM.Import.Core.Planning;

internal sealed class DefaultMeasurementPointPlanner : IMeasurementPointPlanner
{
    private readonly IMeasurementPresetProvider _presetProvider;

    public DefaultMeasurementPointPlanner(IMeasurementPresetProvider presetProvider)
    {
        _presetProvider = presetProvider;
    }

    public IReadOnlyList<MeasurementPoint> PlanPoints(Primitive primitive, MeasurementPlanOptions options)
    {
        var preset = _presetProvider.GetPreset(options);
        var points = primitive switch
        {
            PointPrimitive point => PlanPoint(point),
            LinePrimitive line => PlanLine(line),
            CirclePrimitive circle => PlanCircle(circle, options),
            ArcPrimitive arc => PlanArc(arc, options),
            PlanePrimitive plane => PlanPlane(plane, options),
            CylinderPrimitive cylinder => PlanCylinder(cylinder, options),
            ConePrimitive cone => PlanCone(cone, options),
            SpherePrimitive sphere => PlanSphere(sphere),
            Surface3DPrimitive surface => PlanSurface(surface),
            _ => []
        };

        for (var index = 0; index < points.Count; index++)
        {
            points[index].Index = index + 1;
            points[index].ApproachDistance = preset.ApproachDistanceMm;
            points[index].RetractDistance = preset.RetractDistanceMm;
            points[index].SearchDistance = preset.SearchDistanceMm;
        }

        return points;
    }

    private static List<MeasurementPoint> PlanPoint(PointPrimitive point) =>
    [
        CreatePoint(point.X, point.Y, point.Z, 0, 0, 1)
    ];

    private static List<MeasurementPoint> PlanLine(LinePrimitive line)
    {
        var direction = Normalize((line.DirX, line.DirY, line.DirZ));
        var length = Math.Max(10.0, Length((line.DirX, line.DirY, line.DirZ)));
        return
        [
            CreatePoint(line.StartX, line.StartY, line.StartZ, 0, 0, 1),
            CreatePoint(
                line.StartX + direction.X * length,
                line.StartY + direction.Y * length,
                line.StartZ + direction.Z * length,
                0,
                0,
                1)
        ];
    }

    private static List<MeasurementPoint> PlanCircle(CirclePrimitive circle, MeasurementPlanOptions options)
    {
        var normal = Normalize((circle.NormalX, circle.NormalY, circle.NormalZ));
        var (u, v) = BuildBasis(normal);
        return PlanCircularPoints(
            (circle.CenterX, circle.CenterY, circle.CenterZ),
            normal,
            u,
            v,
            circle.Radius,
            Math.Max(3, options.CirclePointCount),
            0,
            Math.PI * 2);
    }

    private static List<MeasurementPoint> PlanArc(ArcPrimitive arc, MeasurementPlanOptions options)
    {
        var normal = Normalize((arc.NormalX, arc.NormalY, arc.NormalZ));
        var (u, v) = BuildBasis(normal);
        var count = Math.Max(3, Math.Min(options.CirclePointCount, 8));
        return PlanCircularPoints(
            (arc.CenterX, arc.CenterY, arc.CenterZ),
            normal,
            u,
            v,
            arc.Radius,
            count,
            arc.StartAngleRad,
            arc.EndAngleRad);
    }

    private static List<MeasurementPoint> PlanPlane(PlanePrimitive plane, MeasurementPlanOptions options)
    {
        var normal = Normalize((plane.NormalX, plane.NormalY, plane.NormalZ));
        var (u, v) = BuildBasis(normal);
        var spread = Math.Max(1.0, Math.Sqrt(plane.SourceAreaMm2 ?? 100.0) * 0.25);
        var center = (plane.PointX, plane.PointY, plane.PointZ);
        var count = Math.Max(3, options.PlanePointCount);

        var points = new List<MeasurementPoint>
        {
            CreatePoint(center.PointX, center.PointY, center.PointZ, normal.X, normal.Y, normal.Z)
        };

        var offsets = new[]
        {
            Scale(u, spread),
            Scale(u, -spread),
            Scale(v, spread),
            Scale(v, -spread)
        };

        foreach (var offset in offsets.Take(count - 1))
        {
            var point = Add(center, offset);
            points.Add(CreatePoint(point.X, point.Y, point.Z, normal.X, normal.Y, normal.Z));
        }

        while (points.Count < count)
        {
            var angle = Math.PI * 2 * points.Count / count;
            var offset = Add(Scale(u, Math.Cos(angle) * spread), Scale(v, Math.Sin(angle) * spread));
            var point = Add(center, offset);
            points.Add(CreatePoint(point.X, point.Y, point.Z, normal.X, normal.Y, normal.Z));
        }

        return points;
    }

    private static List<MeasurementPoint> PlanCylinder(CylinderPrimitive cylinder, MeasurementPlanOptions options)
    {
        var axis = Normalize((cylinder.AxisDirX, cylinder.AxisDirY, cylinder.AxisDirZ));
        var (u, v) = BuildBasis(axis);
        var radialCount = Math.Max(3, options.CylinderRadialPointCount);
        var levelCount = Math.Max(1, options.CylinderLevelCount);
        var levelSpan = Math.Max(cylinder.Radius, 1.0);
        var axisPoint = (cylinder.AxisPointX, cylinder.AxisPointY, cylinder.AxisPointZ);
        var points = new List<MeasurementPoint>();

        for (var level = 0; level < levelCount; level++)
        {
            var offset = levelCount == 1
                ? 0
                : -levelSpan / 2 + levelSpan * level / (levelCount - 1);
            var center = Add(axisPoint, Scale(axis, offset));

            for (var i = 0; i < radialCount; i++)
            {
                var angle = Math.PI * 2 * i / radialCount;
                var radial = Normalize(Add(Scale(u, Math.Cos(angle)), Scale(v, Math.Sin(angle))));
                var point = Add(center, Scale(radial, cylinder.Radius));
                points.Add(CreatePoint(point.X, point.Y, point.Z, radial.X, radial.Y, radial.Z));
            }
        }

        return points;
    }

    private static List<MeasurementPoint> PlanCone(ConePrimitive cone, MeasurementPlanOptions options)
    {
        var axis = Normalize((cone.AxisDirX, cone.AxisDirY, cone.AxisDirZ));
        var (u, v) = BuildBasis(axis);
        var count = Math.Max(3, options.CirclePointCount);
        var distanceFromApex = 5.0;
        var radius = Math.Max(0.5, Math.Tan(cone.HalfAngleRad) * distanceFromApex);
        var center = Add((cone.ApexX, cone.ApexY, cone.ApexZ), Scale(axis, distanceFromApex));

        return PlanCircularPoints(center, axis, u, v, radius, count, 0, Math.PI * 2);
    }

    private static List<MeasurementPoint> PlanSphere(SpherePrimitive sphere)
    {
        var center = (sphere.CenterX, sphere.CenterY, sphere.CenterZ);
        var directions = new[]
        {
            (1.0, 0.0, 0.0),
            (-1.0, 0.0, 0.0),
            (0.0, 1.0, 0.0),
            (0.0, -1.0, 0.0),
            (0.0, 0.0, 1.0),
            (0.0, 0.0, -1.0)
        };

        return directions
            .Select(direction =>
            {
                var point = Add(center, Scale(direction, sphere.Radius));
                return CreatePoint(point.X, point.Y, point.Z, direction.Item1, direction.Item2, direction.Item3);
            })
            .ToList();
    }

    private static List<MeasurementPoint> PlanSurface(Surface3DPrimitive surface)
    {
        var point = surface.GetRepresentativePoint();
        return
        [
            CreatePoint(point.X, point.Y, point.Z, 0, 0, 1)
        ];
    }

    private static List<MeasurementPoint> PlanCircularPoints(
        (double X, double Y, double Z) center,
        (double X, double Y, double Z) normal,
        (double X, double Y, double Z) u,
        (double X, double Y, double Z) v,
        double radius,
        int count,
        double startAngle,
        double endAngle)
    {
        var points = new List<MeasurementPoint>();
        var closed = Math.Abs(endAngle - startAngle) >= Math.PI * 2 - 1e-6;

        for (var i = 0; i < count; i++)
        {
            var fraction = closed || count == 1 ? (double)i / count : (double)i / (count - 1);
            var angle = startAngle + (endAngle - startAngle) * fraction;
            var offset = Add(Scale(u, Math.Cos(angle) * radius), Scale(v, Math.Sin(angle) * radius));
            var point = Add(center, offset);
            points.Add(CreatePoint(point.X, point.Y, point.Z, normal.X, normal.Y, normal.Z));
        }

        return points;
    }

    private static MeasurementPoint CreatePoint(
        double x,
        double y,
        double z,
        double normalX,
        double normalY,
        double normalZ)
    {
        var normal = Normalize((normalX, normalY, normalZ));
        return new MeasurementPoint
        {
            X = x,
            Y = y,
            Z = z,
            NormalX = normal.X,
            NormalY = normal.Y,
            NormalZ = normal.Z
        };
    }

    private static ((double X, double Y, double Z) U, (double X, double Y, double Z) V) BuildBasis(
        (double X, double Y, double Z) normal)
    {
        var reference = Math.Abs(normal.Z) < 0.9 ? (0.0, 0.0, 1.0) : (1.0, 0.0, 0.0);
        var u = Normalize(Cross(reference, normal));
        var v = Normalize(Cross(normal, u));
        return (u, v);
    }

    private static (double X, double Y, double Z) Normalize((double X, double Y, double Z) value)
    {
        var length = Length(value);
        return length < 1e-12 ? (0, 0, 1) : (value.X / length, value.Y / length, value.Z / length);
    }

    private static double Length((double X, double Y, double Z) value) =>
        Math.Sqrt(value.X * value.X + value.Y * value.Y + value.Z * value.Z);

    private static (double X, double Y, double Z) Add(
        (double X, double Y, double Z) left,
        (double X, double Y, double Z) right) =>
        (left.X + right.X, left.Y + right.Y, left.Z + right.Z);

    private static (double X, double Y, double Z) Scale((double X, double Y, double Z) value, double scale) =>
        (value.X * scale, value.Y * scale, value.Z * scale);

    private static (double X, double Y, double Z) Cross(
        (double X, double Y, double Z) left,
        (double X, double Y, double Z) right) =>
        (
            left.Y * right.Z - left.Z * right.Y,
            left.Z * right.X - left.X * right.Z,
            left.X * right.Y - left.Y * right.X);
}
