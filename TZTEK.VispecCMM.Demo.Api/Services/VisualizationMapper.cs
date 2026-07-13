using TZTEK.VispecCMM.Demo.Api.Models;
using TZTEK.VispecCMM.Import.Interfaces.Interfaces;
using TZTEK.VispecCMM.Import.Interfaces.Models;

namespace TZTEK.VispecCMM.Demo.Api.Services;

internal static class VisualizationMapper
{
    public static VisualizationResultDto Map(
        string sessionId,
        ImportResult import,
        MeasurementTask baseline,
        MeasurementTask optimized,
        bool hasModel)
    {
        var optimizedMeasurements = optimized.Steps
            .Where(step => step.StepType == MeasurementStepType.Measurement && step.TargetItem is not null)
            .GroupBy(step => step.TargetItem!.Primitive.Id)
            .ToDictionary(group => group.Key, group => group.ToList());

        var features = import.Items.Select(item =>
        {
            optimizedMeasurements.TryGetValue(item.Primitive.Id, out var steps);
            var points = steps?
                .SelectMany(step => step.MeasurementPoints ?? [])
                .Select(MapPoint)
                .ToList() ?? [];
            var fitting = steps?.Select(step => step.FittingMethod?.ToString()).FirstOrDefault(value => value is not null);
            return MapFeature(item, points, fitting);
        }).ToList();

        var warnings = BuildWarnings(optimized, hasModel).ToList();
        var allPositions = features.Select(feature => feature.Position)
            .Concat(features.SelectMany(feature => feature.MeasurementPoints.Select(point => point.Position)))
            .Concat(MapSegments(optimized).SelectMany(segment => new[] { segment.Start, segment.End }))
            .ToList();

        return new VisualizationResultDto
        {
            SessionId = sessionId,
            FileName = Path.GetFileName(import.FilePath),
            SourceType = import.SourceType.ToString(),
            ModelUrl = hasModel ? $"/api/demo/sessions/{sessionId}/model" : null,
            Features = features,
            BaselinePlan = MapPlan("基础路径", baseline, import.Items.Count),
            OptimizedPlan = MapPlan("优化路径", optimized, import.Items.Count),
            Probe = MapProbe(optimized.ProbeConfigurations.FirstOrDefault()),
            Warnings = warnings,
            Bounds = BuildBounds(allPositions)
        };
    }

    private static VisualizationFeatureDto MapFeature(
        PrimitiveToleranceItem item,
        IReadOnlyList<VisualizationPointDto> points,
        string? fittingMethod)
    {
        var primitive = item.Primitive;
        var position = primitive.GetRepresentativePoint();
        var direction = primitive.GetDirection();

        return new VisualizationFeatureDto
        {
            Id = primitive.Id,
            Name = primitive.Name,
            Type = primitive.PrimitiveType.ToString(),
            Position = [position.X, position.Y, position.Z],
            Direction = [direction.I, direction.J, direction.K],
            Radius = primitive switch
            {
                CirclePrimitive value => value.Radius,
                ArcPrimitive value => value.Radius,
                CylinderPrimitive value => value.Radius,
                SpherePrimitive value => value.Radius,
                _ => null
            },
            Area = primitive.SourceAreaMm2,
            AngleRad = primitive is ConePrimitive cone ? cone.HalfAngleRad : null,
            SurfaceType = primitive is Surface3DPrimitive surface ? surface.SurfaceType : null,
            FittingMethod = fittingMethod,
            Tolerances = item.Tolerances.Select(tolerance => tolerance.Name).ToList(),
            MeasurementPoints = points
        };
    }

    private static VisualizationPointDto MapPoint(MeasurementPoint point) => new()
    {
        Index = point.Index,
        Position = [point.X, point.Y, point.Z],
        Normal = [point.NormalX, point.NormalY, point.NormalZ],
        ApproachDistance = point.ApproachDistance,
        RetractDistance = point.RetractDistance,
        SearchDistance = point.SearchDistance
    };

    private static VisualizationPathPlanDto MapPlan(string name, MeasurementTask task, int primitiveCount)
    {
        var segments = MapSegments(task);
        var measurementSteps = task.Steps.Where(step => step.StepType == MeasurementStepType.Measurement).ToList();
        return new VisualizationPathPlanDto
        {
            Name = name,
            Segments = segments,
            Statistics = new VisualizationStatisticsDto
            {
                PrimitiveCount = primitiveCount,
                FeatureCount = measurementSteps.Select(step => step.TargetItem?.Primitive.Id).Where(id => id is not null).Distinct().Count(),
                MeasurementPointCount = measurementSteps.Sum(step => step.MeasurementPoints?.Count ?? 0),
                MovementCount = task.Steps.Count(step => step.StepType == MeasurementStepType.Movement),
                MeasurementCount = measurementSteps.Count,
                GotoCount = task.Steps.Count(step => step.GotoTarget is not null),
                AutoGotoCount = task.Steps.Count(step => step.Name.Contains("Auto global safe GOTO", StringComparison.OrdinalIgnoreCase)),
                ManualGotoCount = task.Steps.Count(step => step.GotoTarget?.Reason.Contains("manual GOTO", StringComparison.OrdinalIgnoreCase) == true),
                TotalPathLengthMm = task.TotalPathLengthMm,
                EstimatedTimeSeconds = task.EstimatedTotalTimeSeconds
            }
        };
    }

    private static List<VisualizationPathSegmentDto> MapSegments(MeasurementTask task)
    {
        var result = new List<VisualizationPathSegmentDto>();
        (double X, double Y, double Z)? previous = null;

        foreach (var step in task.Steps)
        {
            if (step.StepType == MeasurementStepType.Movement && step.GotoTarget is not null)
            {
                var end = (step.GotoTarget.X, step.GotoTarget.Y, step.GotoTarget.Z);
                if (previous is not null)
                    result.Add(MapSegment(step, previous.Value, end, "Movement"));
                previous = end;
                continue;
            }

            foreach (var point in step.MeasurementPoints ?? [])
            {
                var end = (point.X, point.Y, point.Z);
                if (previous is not null)
                    result.Add(MapSegment(step, previous.Value, end, "Measurement"));
                previous = end;
            }
        }

        for (var i = 0; i < result.Count; i++)
            result[i] = result[i] with { Sequence = i + 1 };
        return result;
    }

    private static VisualizationPathSegmentDto MapSegment(
        MeasurementStep step,
        (double X, double Y, double Z) start,
        (double X, double Y, double Z) end,
        string kind)
    {
        var reason = step.GotoTarget?.Reason;
        var isAuto = step.Name.Contains("Auto global safe GOTO", StringComparison.OrdinalIgnoreCase);
        var isGoto = step.GotoTarget is not null && (isAuto || step.Name.Contains("GOTO", StringComparison.OrdinalIgnoreCase));
        var risk = reason?.Contains("manual GOTO", StringComparison.OrdinalIgnoreCase) == true
            || reason?.Contains("risk", StringComparison.OrdinalIgnoreCase) == true;
        return new VisualizationPathSegmentDto
        {
            Kind = kind,
            Name = step.Name,
            FeatureId = step.TargetItem?.Primitive.Id,
            Start = [start.X, start.Y, start.Z],
            End = [end.X, end.Y, end.Z],
            IsGoto = isGoto,
            IsAutoGoto = isAuto,
            HasRisk = risk,
            Reason = reason,
            DistanceMm = Distance(start, end)
        };
    }

    private static VisualizationProbeDto? MapProbe(IProbe? probe)
    {
        if (probe is null)
            return null;
        var concrete = probe as Probe;
        return new VisualizationProbeDto
        {
            Name = probe.Name,
            Type = probe.ProbeType.ToString(),
            TipDiameterMm = probe.TipDiameter,
            TipLengthMm = probe.TipLength,
            AngleADeg = concrete?.AngleADeg ?? 0,
            AngleBDeg = concrete?.AngleBDeg ?? 0
        };
    }

    private static IEnumerable<VisualizationWarningDto> BuildWarnings(MeasurementTask task, bool hasModel)
    {
        yield return new VisualizationWarningDto
        {
            Level = "Info",
            Message = "当前碰撞检测为 AABB 粗筛加部分基元窄相检查，仅用于原型级离线验证。"
        };
        if (!hasModel)
        {
            yield return new VisualizationWarningDto
            {
                Level = "Warning",
                Message = "未生成工件 STL 外壳，视图将使用基元近似显示。"
            };
        }
        if (task.Steps.Any(step => step.GotoTarget?.Reason.Contains("manual GOTO", StringComparison.OrdinalIgnoreCase) == true))
        {
            yield return new VisualizationWarningDto
            {
                Level = "Warning",
                Message = "部分路径需要人工设置安全 GOTO 点。"
            };
        }
    }

    private static VisualizationBoundsDto BuildBounds(IReadOnlyList<double[]> positions)
    {
        if (positions.Count == 0)
            return new VisualizationBoundsDto();
        return new VisualizationBoundsDto
        {
            Min = [positions.Min(value => value[0]), positions.Min(value => value[1]), positions.Min(value => value[2])],
            Max = [positions.Max(value => value[0]), positions.Max(value => value[1]), positions.Max(value => value[2])]
        };
    }

    private static double Distance((double X, double Y, double Z) left, (double X, double Y, double Z) right)
    {
        var dx = left.X - right.X;
        var dy = left.Y - right.Y;
        var dz = left.Z - right.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}
