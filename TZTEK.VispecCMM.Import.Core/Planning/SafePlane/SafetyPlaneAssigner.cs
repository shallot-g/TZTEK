using TZTEK.VispecCMM.Import.Core.Planning.Collision;
using TZTEK.VispecCMM.Import.Core.Planning.Path;

namespace TZTEK.VispecCMM.Import.Core.Planning.SafePlane;

internal readonly record struct SafetyPlaneFaceScore(
    SafetyPlaneFace Face,
    double Alignment,
    double ProjectionDistance,
    double PlaneChangeBonus,
    double TotalScore,
    bool IsEligible,
    bool ReturnPathCollides);

internal readonly record struct SafetyPlaneAssignmentWeights(
    double AlignmentWeight,
    double DistanceWeight,
    double PlaneChangeBonus)
{
    public static SafetyPlaneAssignmentWeights Default { get; } = new(10.0, 0.25, 2.0);
}

/// <summary>
/// 为测点选择安全平面：迫近点垂直到安全面的返回段无碰撞，且测点法向与安全平面越垂直分越高。
/// </summary>
internal static class SafetyPlaneAssigner
{
    public static SafetyPlaneFaceScore ScoreFace(
        SafetyPlaneBox envelope,
        MeasurementPoint point,
        SafetyPlaneFace face,
        SafetyPlaneFace? previousFace,
        SafetyPlaneAssignmentContext? context = null,
        SafetyPlaneAssignmentWeights weights = default)
    {
        weights = ResolveWeights(weights);
        context ??= new SafetyPlaneAssignmentContext { Envelope = envelope };

        var plane = envelope.GetPlane(face);
        if (plane is null)
        {
            return new SafetyPlaneFaceScore(face, 0, 0, 0, 0, false, true);
        }

        var surfaceNormal = NormalizeVector((point.NormalX, point.NormalY, point.NormalZ));
        var planeNormal = NormalizeVector((plane.NormalX, plane.NormalY, plane.NormalZ));
        var projections = envelope.GetFaceProjectionDistances((point.X, point.Y, point.Z));
        var projectionDistance = projections[face];

        var approach = PathGeometryHelper.GetApproachPoint(point);
        var safeOnFace = envelope.ProjectPoint(face, PathGeometryHelper.ToTuple(approach));
        var returnPathCollides = ReturnPathCollides(
            context,
            approach,
            safeOnFace,
            projectionDistance < -1e-6);

        if (returnPathCollides)
        {
            return new SafetyPlaneFaceScore(
                face,
                0,
                projectionDistance,
                0,
                0,
                false,
                true);
        }

        // 测点法向与安全平面法向越平行 → 测点法向与平面本身越接近 90° → 分越高。
        var normalPlaneAngleScore = Math.Abs(Dot(surfaceNormal, planeNormal));
        var planeChangeBonus = previousFace == face ? weights.PlaneChangeBonus : 0;
        var totalScore = weights.AlignmentWeight * normalPlaneAngleScore
            - weights.DistanceWeight * Math.Max(0, projectionDistance)
            + planeChangeBonus;
        var eligible = normalPlaneAngleScore > 1e-6 && projectionDistance >= -1e-6;

        return new SafetyPlaneFaceScore(
            face,
            normalPlaneAngleScore,
            projectionDistance,
            planeChangeBonus,
            totalScore,
            eligible,
            false);
    }

    public static IReadOnlyList<SafetyPlaneFaceScore> ScoreAllFaces(
        SafetyPlaneBox envelope,
        MeasurementPoint point,
        SafetyPlaneFace? previousFace = null,
        SafetyPlaneAssignmentContext? context = null,
        SafetyPlaneAssignmentWeights weights = default)
    {
        weights = ResolveWeights(weights);

        return Enum.GetValues<SafetyPlaneFace>()
            .Select(face => ScoreFace(envelope, point, face, previousFace, context, weights))
            .OrderByDescending(score => score.TotalScore)
            .ToList();
    }

    public static SafetyPlaneFace SelectBestFace(
        SafetyPlaneBox envelope,
        MeasurementPoint point,
        SafetyPlaneFace? previousFace = null,
        SafetyPlaneAssignmentContext? context = null,
        SafetyPlaneAssignmentWeights weights = default)
    {
        weights = ResolveWeights(weights);
        var scores = ScoreAllFaces(envelope, point, previousFace, context, weights);

        var bestEligible = scores.FirstOrDefault(score => score.IsEligible);
        if (bestEligible.IsEligible)
            return bestEligible.Face;

        var bestClear = scores
            .Where(score => !score.ReturnPathCollides && score.Alignment > 1e-6)
            .OrderByDescending(score => score.Alignment)
            .ThenByDescending(score => score.TotalScore)
            .FirstOrDefault();
        if (bestClear.Alignment > 1e-6)
            return bestClear.Face;

        return scores
            .Where(score => !score.ReturnPathCollides)
            .OrderByDescending(score => score.Alignment)
            .Select(score => score.Face)
            .FirstOrDefault(SafetyPlaneFace.Top);
    }

    public static SafetyPlaneFaceScore SelectBestScore(
        SafetyPlaneBox envelope,
        MeasurementPoint point,
        SafetyPlaneFace? previousFace = null,
        SafetyPlaneAssignmentContext? context = null,
        SafetyPlaneAssignmentWeights weights = default)
    {
        weights = ResolveWeights(weights);
        var face = SelectBestFace(envelope, point, previousFace, context, weights);
        return ScoreFace(envelope, point, face, previousFace, context, weights);
    }

    public static void AssignPoint(
        SafetyPlaneBox envelope,
        MeasurementPoint point,
        SafetyPlaneFace? previousFace = null,
        SafetyPlaneAssignmentContext? context = null,
        SafetyPlaneAssignmentWeights weights = default)
    {
        var best = SelectBestScore(envelope, point, previousFace, context, weights);
        var safePosition = envelope.ProjectPoint(best.Face, (point.X, point.Y, point.Z));

        point.AssignedSafetyPlaneFace = best.Face;
        point.SafetyPlaneAssignmentScore = best.TotalScore;
        point.SafetyPlaneSafeX = safePosition.X;
        point.SafetyPlaneSafeY = safePosition.Y;
        point.SafetyPlaneSafeZ = safePosition.Z;
    }

    public static IReadOnlyDictionary<int, SafetyPlaneFace> AssignTaskPoints(
        MeasurementTask task,
        MeasurementPlanOptions? options = null,
        SafetyPlaneAssignmentWeights weights = default)
    {
        if (task.SafetyEnvelope is not { } envelope)
            return new Dictionary<int, SafetyPlaneFace>();

        weights = ResolveWeights(weights);
        var resolvedOptions = options ?? new MeasurementPlanOptions();
        if (resolvedOptions.CollisionPrimitives.Count == 0 && task.CollisionPrimitives.Count > 0)
            resolvedOptions.CollisionPrimitives = task.CollisionPrimitives;

        var assignments = new Dictionary<int, SafetyPlaneFace>();
        SafetyPlaneFace? previousFace = null;

        foreach (var step in task.Steps.Where(step => step.StepType == MeasurementStepType.Measurement))
        {
            if (step.MeasurementPoints is not { Count: > 0 } points)
                continue;

            var context = new SafetyPlaneAssignmentContext
            {
                Envelope = envelope,
                CollisionPrimitives = task.CollisionPrimitives,
                Options = resolvedOptions,
                TargetItem = step.TargetItem
            };

            foreach (var point in points)
            {
                AssignPoint(envelope, point, previousFace, context, weights);
                if (point.AssignedSafetyPlaneFace is { } face)
                {
                    assignments[point.Index] = face;
                    previousFace = face;
                }
            }
        }

        return assignments;
    }

    private static bool ReturnPathCollides(
        SafetyPlaneAssignmentContext context,
        PlanningVectors.Vec3 approach,
        (double X, double Y, double Z) safeOnFace,
        bool pointOutsideEnvelope)
    {
        if (pointOutsideEnvelope)
            return true;

        if (PlanningVectors.Distance(approach, safeOnFace) < 1e-6)
            return false;

        if (context.CollisionPrimitives.Count == 0)
            return false;

        var options = context.Options;
        if (options.CollisionPrimitives.Count == 0)
            options.CollisionPrimitives = context.CollisionPrimitives;

        var task = new MeasurementTask
        {
            CollisionPrimitives = context.CollisionPrimitives,
            SafetyEnvelope = context.Envelope
        };
        var step = new MeasurementStep
        {
            StepType = MeasurementStepType.Movement,
            MovementKind = "Return to safety plane",
            TargetItem = context.TargetItem
        };

        foreach (var box in CollisionBoxBuilder.Build(task, step, options))
        {
            if (!SegmentCollisionChecker.IntersectsBox(box, approach, safeOnFace))
                continue;

            if (options.EnablePrimitiveNarrowPhaseCollisionCheck
                && box.Primitive is not null
                && !SegmentCollisionChecker.IntersectsPrimitiveNarrowPhase(
                    box.Primitive,
                    approach,
                    safeOnFace,
                    box.Margin))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static SafetyPlaneAssignmentWeights ResolveWeights(SafetyPlaneAssignmentWeights weights) =>
        weights.AlignmentWeight <= 0 ? SafetyPlaneAssignmentWeights.Default : weights;

    private static (double X, double Y, double Z) NormalizeVector((double X, double Y, double Z) value)
    {
        var length = Math.Sqrt(value.X * value.X + value.Y * value.Y + value.Z * value.Z);
        return length < 1e-12 ? (0, 0, 1) : (value.X / length, value.Y / length, value.Z / length);
    }

    private static double Dot((double X, double Y, double Z) left, (double X, double Y, double Z) right) =>
        left.X * right.X + left.Y * right.Y + left.Z * right.Z;
}
