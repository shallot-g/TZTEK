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
            LinePrimitive line => PlanLine(line, options),
            CirclePrimitive circle => PlanCircle(circle, options),
            ArcPrimitive arc => PlanArc(arc, options),
            PlanePrimitive plane => PlanPlane(plane, options),
            CylinderPrimitive cylinder => PlanCylinder(cylinder, options),
            ConePrimitive cone => PlanCone(cone, options),
            SpherePrimitive sphere => PlanSphere(sphere, options),
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

    private static List<MeasurementPoint> PlanLine(LinePrimitive line, MeasurementPlanOptions options)
    {
        var direction = Normalize((line.DirX, line.DirY, line.DirZ));
        var length = Math.Max(10.0, Length((line.DirX, line.DirY, line.DirZ)));
        var count = Math.Max(2, options.LinePointCount);
        var points = new List<MeasurementPoint>();

        for (var i = 0; i < count; i++)
        {
            var fraction = count == 1 ? 0 : (double)i / (count - 1);
            points.Add(CreatePoint(
                line.StartX + direction.X * length * fraction,
                line.StartY + direction.Y * length * fraction,
                line.StartZ + direction.Z * length * fraction,
                0,
                0,
                1));
        }

        return points;
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
        var count = Math.Max(3, options.ArcPointCount);
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
        var uCount = Math.Max(1, options.PlaneGridUCount);
        var vCount = Math.Max(1, options.PlaneGridVCount);
        var points = new List<MeasurementPoint>();

        for (var i = 0; i < uCount; i++)
        {
            var uFraction = uCount == 1 ? 0 : (double)i / (uCount - 1) - 0.5;
            for (var j = 0; j < vCount; j++)
            {
                var vFraction = vCount == 1 ? 0 : (double)j / (vCount - 1) - 0.5;
                var offset = Add(Scale(u, uFraction * spread * 2), Scale(v, vFraction * spread * 2));
                var point = Add(center, offset);
                points.Add(CreatePoint(point.X, point.Y, point.Z, normal.X, normal.Y, normal.Z));
            }
        }

        return points;
    }

    private static List<MeasurementPoint> PlanCylinder(CylinderPrimitive cylinder, MeasurementPlanOptions options)
    {
        var axis = Normalize((cylinder.AxisDirX, cylinder.AxisDirY, cylinder.AxisDirZ));
        if (cylinder.Length is null || cylinder.Length <= 1e-9)
            return [];

        var hasReference = cylinder.RadialReferenceX is not null
            && cylinder.RadialReferenceY is not null
            && cylinder.RadialReferenceZ is not null;
        var u = hasReference
            ? Normalize((cylinder.RadialReferenceX!.Value, cylinder.RadialReferenceY!.Value, cylinder.RadialReferenceZ!.Value))
            : BuildBasis(axis).U;
        var v = Normalize(Cross(axis, u));
        var radialCount = Math.Max(3, options.CylinderRadialPointCount);
        var levelCount = Math.Max(1, options.CylinderLevelCount);
        var axisPoint = (cylinder.AxisPointX, cylinder.AxisPointY, cylinder.AxisPointZ);
        var fullCircle = (cylinder.AngularSpanRad ?? Math.PI * 2) >= Math.PI * 2 - 1e-6;
        var startAngle = fullCircle ? 0 : cylinder.StartAngleRad ?? 0;
        var endAngle = fullCircle ? Math.PI * 2 : cylinder.EndAngleRad ?? startAngle;
        var points = new List<MeasurementPoint>();

        for (var level = 0; level < levelCount; level++)
        {
            var fraction = levelCount == 1 ? 0.5 : 0.2 + 0.6 * level / (levelCount - 1);
            var offset = (fraction - 0.5) * cylinder.Length.Value;
            var center = Add(axisPoint, Scale(axis, offset));

            for (var i = 0; i < radialCount; i++)
            {
                var angleFraction = fullCircle ? (double)i / radialCount : (double)i / Math.Max(radialCount - 1, 1);
                var angle = startAngle + (endAngle - startAngle) * angleFraction;
                var radial = Normalize(Add(Scale(u, Math.Cos(angle)), Scale(v, Math.Sin(angle))));
                var point = Add(center, Scale(radial, cylinder.Radius));
                var contactNormal = cylinder.IsInnerSurface == true ? Scale(radial, -1) : radial;
                points.Add(CreatePoint(point.X, point.Y, point.Z, contactNormal.X, contactNormal.Y, contactNormal.Z));
            }
        }

        return points;
    }

    private static List<MeasurementPoint> PlanCone(ConePrimitive cone, MeasurementPlanOptions options)
    {
        var axis = Normalize((cone.AxisDirX, cone.AxisDirY, cone.AxisDirZ));
        var (u, v) = BuildBasis(axis);
        var radialCount = Math.Max(3, options.ConeRadialPointCount);
        var levelCount = Math.Max(1, options.ConeLevelCount);
        var points = new List<MeasurementPoint>();

        // Use the actual trimmed face extent when available.
        var useFaceExtent = cone.AxisStartX is not null
            && cone.AxisEndX is not null
            && cone.RadiusStart is not null
            && cone.RadiusEnd is not null
            && cone.Length is > 1e-9;

        if (useFaceExtent)
        {
            var startCenter = (cone.AxisStartX!.Value, cone.AxisStartY!.Value, cone.AxisStartZ!.Value);
            var endCenter = (cone.AxisEndX!.Value, cone.AxisEndY!.Value, cone.AxisEndZ!.Value);
            var rStart = cone.RadiusStart!.Value;
            var rEnd = cone.RadiusEnd!.Value;
            var length = cone.Length!.Value;

            for (var level = 0; level < levelCount; level++)
            {
                var fraction = levelCount == 1 ? 0.5 : 0.2 + 0.6 * level / (levelCount - 1);
                var center = Add(startCenter, Scale(Subtract(endCenter, startCenter), fraction));
                var radius = rStart + (rEnd - rStart) * fraction;
                points.AddRange(PlanConeRing(center, axis, u, v, Math.Max(radius, 0.25), rStart, rEnd, length, radialCount, cone.IsInnerSurface == true));
            }
        }
        else
        {
            // Fallback: extrapolate from apex (used when face extent is unknown).
            var distanceFromApex = 5.0;
            for (var level = 0; level < levelCount; level++)
            {
                var levelDistance = distanceFromApex * (level + 1);
                var radius = Math.Max(0.5, Math.Tan(cone.HalfAngleRad) * levelDistance);
                var center = Add((cone.ApexX, cone.ApexY, cone.ApexZ), Scale(axis, levelDistance));
                points.AddRange(PlanConeRing(center, axis, u, v, radius, 0, radius, levelDistance, radialCount, cone.IsInnerSurface == true));
            }
        }

        return points;
    }

    private static List<MeasurementPoint> PlanSphere(SpherePrimitive sphere, MeasurementPlanOptions options)
    {
        var center = (sphere.CenterX, sphere.CenterY, sphere.CenterZ);
        var count = Math.Max(6, options.SpherePointCount);
        var points = new List<MeasurementPoint>();
        var goldenAngle = Math.PI * (3 - Math.Sqrt(5));

        for (var i = 0; i < count; i++)
        {
            var y = 1 - (2.0 * i / (count - 1));
            var radial = Math.Sqrt(Math.Max(0, 1 - y * y));
            var theta = goldenAngle * i;
            var direction = (Math.Cos(theta) * radial, Math.Sin(theta) * radial, y);
            var point = Add(center, Scale(direction, sphere.Radius));
            points.Add(CreatePoint(point.X, point.Y, point.Z, direction.Item1, direction.Item2, direction.Item3));
        }

        return points;
    }

    private static List<MeasurementPoint> PlanSurface(Surface3DPrimitive surface)
    {
        if (surface.Vertices.Count == 0)
        {
            var fallback = surface.GetRepresentativePoint();
            return [CreatePoint(fallback.X, fallback.Y, fallback.Z, 0, 0, 1)];
        }

        var normals = surface.VertexNormals.Count == surface.Vertices.Count
            ? surface.VertexNormals
            : EstimateSurfaceNormals(surface.Vertices);

        return surface.Vertices
            .Select((vertex, index) =>
            {
                var normal = normals[index];
                return CreatePoint(vertex.X, vertex.Y, vertex.Z, normal.X, normal.Y, normal.Z);
            })
            .ToList();
    }

    private static List<(double X, double Y, double Z)> EstimateSurfaceNormals(
        IReadOnlyList<(double X, double Y, double Z)> vertices)
    {
        var normals = new List<(double X, double Y, double Z)>(vertices.Count);
        for (var i = 0; i < vertices.Count; i++)
        {
            var previous = vertices[i == 0 ? vertices.Count - 1 : i - 1];
            var current = vertices[i];
            var next = vertices[i == vertices.Count - 1 ? 0 : i + 1];
            var tangentA = Subtract(current, previous);
            var tangentB = Subtract(next, current);
            var estimated = Cross(tangentA, tangentB);
            normals.Add(Length(estimated) < 1e-12 ? (0, 0, 1) : Normalize(estimated));
        }

        return normals;
    }

    private static List<MeasurementPoint> PlanConeRing(
        (double X, double Y, double Z) center,
        (double X, double Y, double Z) axis,
        (double X, double Y, double Z) u,
        (double X, double Y, double Z) v,
        double radius,
        double radiusStart,
        double radiusEnd,
        double length,
        int count,
        bool inner)
    {
        var points = new List<MeasurementPoint>();
        var axial = Math.Max(length, 1e-9);
        var slope = radiusEnd - radiusStart;
        for (var i = 0; i < count; i++)
        {
            var angle = Math.PI * 2 * i / count;
            var radial = Normalize(Add(Scale(u, Math.Cos(angle)), Scale(v, Math.Sin(angle))));
            var point = Add(center, Scale(radial, radius));
            var contactNormal = Normalize(Add(Scale(radial, axial), Scale(axis, -slope)));
            if (inner)
                contactNormal = Scale(contactNormal, -1);
            points.Add(CreatePoint(point.X, point.Y, point.Z, contactNormal.X, contactNormal.Y, contactNormal.Z));
        }

        return points;
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
            var radial = Normalize(offset);
            var contactNormal = Length(offset) < 1e-12 ? normal : radial;
            points.Add(CreatePoint(point.X, point.Y, point.Z, contactNormal.X, contactNormal.Y, contactNormal.Z));
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

    private static (double X, double Y, double Z) Subtract(
        (double X, double Y, double Z) left,
        (double X, double Y, double Z) right) =>
        (left.X - right.X, left.Y - right.Y, left.Z - right.Z);

    private static (double X, double Y, double Z) Cross(
        (double X, double Y, double Z) left,
        (double X, double Y, double Z) right) =>
        (
            left.Y * right.Z - left.Z * right.Y,
            left.Z * right.X - left.X * right.Z,
            left.X * right.Y - left.Y * right.X);
}
