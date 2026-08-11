using TZTEK.VispecCMM.Import.Interfaces.Interfaces;
using TZTEK.VispecCMM.Import.Interfaces.Interfaces.Pipeline;
using TZTEK.VispecCMM.Import.Interfaces.Models;

namespace TZTEK.VispecCMM.Import.Viewer;

/// <summary>
/// 从导入结果与测量任务构建可视化场景。
/// </summary>
public sealed class MeasurementSceneBuilder : IMeasurementSceneBuilder
{
    public MeasurementScene Build(
        ImportResult importResult,
        IReadOnlyList<MeasurementTask> tasks,
        MeasurementSceneOptions? options = null)
    {
        options ??= new MeasurementSceneOptions();
        var objects = new List<MeasurementSceneObject>();

        if (options.IncludeWorkpieceMeshes)
            objects.AddRange(BuildMeshes(importResult.WorkpieceMeshes));

        if (options.IncludePrimitiveOutlines)
            objects.AddRange(BuildPrimitiveOutlines(importResult.Items, options.PrimitiveOutlineScale));

        if (options.IncludeMeasurementPoints || options.IncludePathSegments || options.IncludeGotoPoints)
            objects.AddRange(BuildTaskObjects(tasks, options));

        if (options.IncludeSafetyPlane)
            objects.AddRange(BuildSafetyPlanes(tasks));

        return new MeasurementScene
        {
            Title = Path.GetFileName(importResult.FilePath),
            SourceFilePath = importResult.FilePath,
            Bounds = ComputeBounds(objects),
            Objects = objects
        };
    }

    private static IEnumerable<MeasurementSceneObject> BuildMeshes(IReadOnlyList<WorkpieceMesh> meshes)
    {
        foreach (var mesh in meshes)
        {
            yield return new MeasurementSceneObject
            {
                Id = mesh.Id,
                Kind = SceneObjectKind.WorkpieceMesh,
                Label = mesh.Name,
                Color = new SceneColor(180, 190, 200),
                Opacity = 0.35,
                Vertices = mesh.Vertices,
                Triangles = mesh.Triangles,
                Normals = mesh.Normals
            };
        }
    }

    private static IEnumerable<MeasurementSceneObject> BuildPrimitiveOutlines(
        IReadOnlyList<PrimitiveToleranceItem> items,
        double scale)
    {
        foreach (var item in items)
        {
            var primitive = item.Primitive;
            var points = BuildPrimitiveOutline(primitive, scale);
            if (points.Count == 0)
                continue;

            yield return new MeasurementSceneObject
            {
                Id = $"outline_{primitive.Id}",
                Kind = SceneObjectKind.PrimitiveOutline,
                Label = primitive.Name,
                Color = new SceneColor(255, 200, 80),
                Opacity = 0.9,
                Points = points,
                RelatedPrimitiveId = primitive.Id
            };
        }
    }

    private static IReadOnlyList<SceneVector3> BuildPrimitiveOutline(Primitive primitive, double scale)
    {
        return primitive switch
        {
            PlanePrimitive plane => BuildPlaneOutline(plane, scale),
            CylinderPrimitive cylinder => BuildCylinderOutline(cylinder, scale),
            CirclePrimitive circle => BuildCircleOutline(
                circle.CenterX, circle.CenterY, circle.CenterZ,
                circle.Radius, circle.NormalX, circle.NormalY, circle.NormalZ),
            SpherePrimitive sphere => BuildCircleOutline(
                sphere.CenterX, sphere.CenterY, sphere.CenterZ,
                sphere.Radius, 0, 0, 1),
            LinePrimitive line => [new(line.StartX, line.StartY, line.StartZ)],
            PointPrimitive point => [new(point.X, point.Y, point.Z)],
            _ => [new(primitive.GetRepresentativePoint().X, primitive.GetRepresentativePoint().Y, primitive.GetRepresentativePoint().Z)]
        };
    }

    private static IReadOnlyList<SceneVector3> BuildPlaneOutline(PlanePrimitive plane, double scale)
    {
        var half = 10.0 * scale;
        var (nx, ny, nz) = plane.GetDirection();
        var (ux, uy, uz) = OrthogonalVector(nx, ny, nz);
        var (vx, vy, vz) = Cross(nx, ny, nz, ux, uy, uz);
        var cx = plane.PointX;
        var cy = plane.PointY;
        var cz = plane.PointZ;

        return
        [
            Offset(cx, cy, cz, ux, uy, uz, vx, vy, vz, -half, -half),
            Offset(cx, cy, cz, ux, uy, uz, vx, vy, vz, half, -half),
            Offset(cx, cy, cz, ux, uy, uz, vx, vy, vz, half, half),
            Offset(cx, cy, cz, ux, uy, uz, vx, vy, vz, -half, half),
            Offset(cx, cy, cz, ux, uy, uz, vx, vy, vz, -half, -half)
        ];
    }

    private static IReadOnlyList<SceneVector3> BuildCylinderOutline(CylinderPrimitive cylinder, double scale)
    {
        var height = 40.0 * scale;
        var radius = Math.Max(cylinder.Radius, 1.0);
        var (ax, ay, az) = cylinder.GetDirection();
        var (ux, uy, uz) = OrthogonalVector(ax, ay, az);
        var (vx, vy, vz) = Cross(ax, ay, az, ux, uy, uz);
        var cx = cylinder.AxisPointX;
        var cy = cylinder.AxisPointY;
        var cz = cylinder.AxisPointZ;
        var segments = 24;
        var points = new List<SceneVector3>();

        for (var i = 0; i <= segments; i++)
        {
            var angle = i * Math.PI * 2 / segments;
            var cos = Math.Cos(angle) * radius;
            var sin = Math.Sin(angle) * radius;
            points.Add(new(
                cx + ux * cos + vx * sin,
                cy + uy * cos + vy * sin,
                cz + uz * cos + vz * sin));
        }

        for (var i = 0; i <= segments; i++)
        {
            var angle = i * Math.PI * 2 / segments;
            var cos = Math.Cos(angle) * radius;
            var sin = Math.Sin(angle) * radius;
            points.Add(new(
                cx + ax * height + ux * cos + vx * sin,
                cy + ay * height + uy * cos + vy * sin,
                cz + az * height + uz * cos + vz * sin));
        }

        return points;
    }

    private static IReadOnlyList<SceneVector3> BuildCircleOutline(
        double cx, double cy, double cz,
        double radius, double nx, double ny, double nz)
    {
        var (ux, uy, uz) = OrthogonalVector(nx, ny, nz);
        var (vx, vy, vz) = Cross(nx, ny, nz, ux, uy, uz);
        var segments = 32;
        var points = new List<SceneVector3>(segments + 1);
        var r = Math.Max(radius, 0.5);

        for (var i = 0; i <= segments; i++)
        {
            var angle = i * Math.PI * 2 / segments;
            var cos = Math.Cos(angle) * r;
            var sin = Math.Sin(angle) * r;
            points.Add(new(cx + ux * cos + vx * sin, cy + uy * cos + vy * sin, cz + uz * cos + vz * sin));
        }

        return points;
    }

    private static IEnumerable<MeasurementSceneObject> BuildTaskObjects(
        IReadOnlyList<MeasurementTask> tasks,
        MeasurementSceneOptions options)
    {
        foreach (var task in tasks)
        {
            (double X, double Y, double Z)? previous = null;
            foreach (var step in task.Steps.OrderBy(step => step.SequenceNumber))
            {
                if (options.IncludeMeasurementPoints && step.MeasurementPoints is { Count: > 0 })
                {
                    foreach (var point in step.MeasurementPoints)
                    {
                        yield return new MeasurementSceneObject
                        {
                            Id = $"point_{task.TaskId}_{step.SequenceNumber}_{point.Index}",
                            Kind = SceneObjectKind.MeasurementPoint,
                            Label = $"{step.Name} P{point.Index}",
                            Color = new SceneColor(80, 220, 120),
                            Points =
                            [
                                new(point.X, point.Y, point.Z),
                                new(point.X + point.NormalX * 3, point.Y + point.NormalY * 3, point.Z + point.NormalZ * 3)
                            ],
                            RelatedStepName = step.Name,
                            RelatedPrimitiveId = step.TargetItem?.Primitive.Id
                        };

                        if (options.IncludePathSegments)
                        {
                            var current = (point.X, point.Y, point.Z);
                            if (previous is not null)
                            {
                                yield return new MeasurementSceneObject
                                {
                                    Id = $"path_{task.TaskId}_{step.SequenceNumber}_{point.Index}",
                                    Kind = SceneObjectKind.PathSegment,
                                    Color = new SceneColor(70, 140, 255),
                                    Points =
                                    [
                                        new(previous.Value.X, previous.Value.Y, previous.Value.Z),
                                        new(current.X, current.Y, current.Z)
                                    ],
                                    RelatedStepName = step.Name
                                };
                            }

                            previous = current;
                        }
                    }
                }

                if (options.IncludeGotoPoints && step.GotoTarget is not null)
                {
                    var gotoPoint = step.GotoTarget;
                    var color = ClassifyGotoColor(gotoPoint.Reason);
                    yield return new MeasurementSceneObject
                    {
                        Id = $"goto_{task.TaskId}_{step.SequenceNumber}",
                        Kind = SceneObjectKind.GotoPoint,
                        Label = gotoPoint.Reason,
                        Color = color,
                        Points = [new(gotoPoint.X, gotoPoint.Y, gotoPoint.Z)],
                        RelatedStepName = step.Name
                    };

                    if (options.IncludePathSegments && previous is not null)
                    {
                        yield return new MeasurementSceneObject
                        {
                            Id = $"goto_path_{task.TaskId}_{step.SequenceNumber}",
                            Kind = SceneObjectKind.PathSegment,
                            Color = color,
                            Points =
                            [
                                new(previous.Value.X, previous.Value.Y, previous.Value.Z),
                                new(gotoPoint.X, gotoPoint.Y, gotoPoint.Z)
                            ],
                            RelatedStepName = step.Name
                        };
                    }

                    previous = (gotoPoint.X, gotoPoint.Y, gotoPoint.Z);
                }
            }
        }
    }

    private static IEnumerable<MeasurementSceneObject> BuildSafetyPlanes(IReadOnlyList<MeasurementTask> tasks)
    {
        foreach (var task in tasks)
        {
            if (task.SafetyEnvelope is { } envelope)
            {
                foreach (var plane in envelope.GetAllPlanes())
                {
                    var (halfU, halfV) = ResolvePlaneHalfSize(envelope, plane);
                    yield return CreateSafetyPlaneObject(task.TaskId, plane, halfU, halfV, ResolveFaceColor(plane.Id));
                }

                continue;
            }

            if (task.GlobalSafetyPlane is ISafetyPlane legacyPlane)
                yield return CreateSafetyPlaneObject(task.TaskId, legacyPlane, 50.0, 50.0, new SceneColor(120, 180, 255));
        }
    }

    private static (double HalfU, double HalfV) ResolvePlaneHalfSize(SafetyPlaneBox envelope, ISafetyPlane plane)
    {
        var normal = plane.GetDirection();
        if (Math.Abs(normal.Item3) > 0.9)
            return (envelope.SizeX / 2.0, envelope.SizeY / 2.0);

        if (Math.Abs(normal.Item1) > 0.9)
            return (envelope.SizeY / 2.0, envelope.SizeZ / 2.0);

        return (envelope.SizeX / 2.0, envelope.SizeZ / 2.0);
    }

    private static SceneColor ResolveFaceColor(string planeId)
    {
        if (planeId.Contains("bottom", StringComparison.OrdinalIgnoreCase))
            return new SceneColor(180, 140, 255);

        if (planeId.Contains("top", StringComparison.OrdinalIgnoreCase))
            return new SceneColor(120, 180, 255);

        return new SceneColor(100, 200, 180);
    }

    private static MeasurementSceneObject CreateSafetyPlaneObject(
        string taskId,
        ISafetyPlane plane,
        double halfU,
        double halfV,
        SceneColor color)
    {
        var position = plane.GetPosition();
        var (cx, cy, cz) = position;
        var direction = plane.GetDirection();
        var (nx, ny, nz) = direction;
        var (ux, uy, uz) = OrthogonalVector(nx, ny, nz);
        var (vx, vy, vz) = Cross(nx, ny, nz, ux, uy, uz);

        return new MeasurementSceneObject
        {
            Id = $"safety_plane_{taskId}_{plane.Id}",
            Kind = SceneObjectKind.SafetyPlane,
            Label = plane.Name,
            Color = color,
            Opacity = 0.15,
            Points =
            [
                Offset(cx, cy, cz, ux, uy, uz, vx, vy, vz, -halfU, -halfV),
                Offset(cx, cy, cz, ux, uy, uz, vx, vy, vz, halfU, -halfV),
                Offset(cx, cy, cz, ux, uy, uz, vx, vy, vz, halfU, halfV),
                Offset(cx, cy, cz, ux, uy, uz, vx, vy, vz, -halfU, halfV),
                Offset(cx, cy, cz, ux, uy, uz, vx, vy, vz, -halfU, -halfV)
            ]
        };
    }

    private static SceneColor ClassifyGotoColor(string reason)
    {
        var normalized = reason.ToLowerInvariant();
        if (normalized.Contains("manual") || normalized.Contains("user goto"))
            return new SceneColor(255, 90, 90);

        if (normalized.Contains("safe") || normalized.Contains("clearance"))
            return new SceneColor(255, 170, 60);

        return new SceneColor(90, 160, 255);
    }

    private static SceneBounds ComputeBounds(IReadOnlyList<MeasurementSceneObject> objects)
    {
        var minX = double.PositiveInfinity;
        var minY = double.PositiveInfinity;
        var minZ = double.PositiveInfinity;
        var maxX = double.NegativeInfinity;
        var maxY = double.NegativeInfinity;
        var maxZ = double.NegativeInfinity;

        foreach (var obj in objects)
        {
            foreach (var point in obj.Points)
                UpdateBounds(point, ref minX, ref minY, ref minZ, ref maxX, ref maxY, ref maxZ);

            for (var i = 0; i + 2 < obj.Vertices.Count; i += 3)
            {
                UpdateBounds(
                    new SceneVector3(obj.Vertices[i], obj.Vertices[i + 1], obj.Vertices[i + 2]),
                    ref minX, ref minY, ref minZ, ref maxX, ref maxY, ref maxZ);
            }
        }

        if (double.IsInfinity(minX))
            return new SceneBounds { Min = new(), Max = new() };

        return new SceneBounds
        {
            Min = new(minX, minY, minZ),
            Max = new(maxX, maxY, maxZ)
        };
    }

    private static void UpdateBounds(
        SceneVector3 point,
        ref double minX,
        ref double minY,
        ref double minZ,
        ref double maxX,
        ref double maxY,
        ref double maxZ)
    {
        minX = Math.Min(minX, point.X);
        minY = Math.Min(minY, point.Y);
        minZ = Math.Min(minZ, point.Z);
        maxX = Math.Max(maxX, point.X);
        maxY = Math.Max(maxY, point.Y);
        maxZ = Math.Max(maxZ, point.Z);
    }

    private static SceneVector3 Offset(
        double cx, double cy, double cz,
        double ux, double uy, double uz,
        double vx, double vy, double vz,
        double u, double v)
    {
        return new(cx + ux * u + vx * v, cy + uy * u + vy * v, cz + uz * u + vz * v);
    }

    private static (double X, double Y, double Z) OrthogonalVector(double x, double y, double z)
    {
        if (Math.Abs(z) < 0.9)
            return Normalize(0, 0, 1, x, y, z);

        return Normalize(1, 0, 0, x, y, z);
    }

    private static (double X, double Y, double Z) Cross(
        double ax, double ay, double az,
        double bx, double by, double bz)
    {
        return (
            ay * bz - az * by,
            az * bx - ax * bz,
            ax * by - ay * bx);
    }

    private static (double X, double Y, double Z) Normalize(
        double x, double y, double z,
        double ax, double ay, double az)
    {
        var dot = x * ax + y * ay + z * az;
        var ox = x - dot * ax;
        var oy = y - dot * ay;
        var oz = z - dot * az;
        var length = Math.Sqrt(ox * ox + oy * oy + oz * oz);
        if (length < 1e-12)
            return (1, 0, 0);

        return (ox / length, oy / length, oz / length);
    }
}
