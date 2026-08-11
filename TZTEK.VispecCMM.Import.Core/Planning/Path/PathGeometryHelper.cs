namespace TZTEK.VispecCMM.Import.Core.Planning.Path;

internal static class PathGeometryHelper
{
    internal const double DefaultMachineSpeedMmPerSecond = 20.0;

    internal static PlanningVectors.Vec3 ToVec3((double X, double Y, double Z) point) =>
        new(point.X, point.Y, point.Z);

    internal static (double X, double Y, double Z) ToTuple(PlanningVectors.Vec3 point) =>
        (point.X, point.Y, point.Z);

    internal static PlanningVectors.Vec3 GetApproachPoint(MeasurementPoint point)
    {
        // 测点法向指向探针可进入的自由空间；迫近点应沿法向再向外偏移，
        // 探针再从迫近点沿 -法向进入接触点。
        var normal = PlanningVectors.Normalize(new PlanningVectors.Vec3(point.NormalX, point.NormalY, point.NormalZ));
        return new PlanningVectors.Vec3(
            point.X + normal.X * point.ApproachDistance,
            point.Y + normal.Y * point.ApproachDistance,
            point.Z + normal.Z * point.ApproachDistance);
    }

    internal static PlanningVectors.Vec3 GetSafePosition(MeasurementPoint point) =>
        point.SafetyPlaneSafeX is not null && point.SafetyPlaneSafeY is not null && point.SafetyPlaneSafeZ is not null
            ? new PlanningVectors.Vec3(point.SafetyPlaneSafeX.Value, point.SafetyPlaneSafeY.Value, point.SafetyPlaneSafeZ.Value)
            : new PlanningVectors.Vec3(point.X, point.Y, point.Z);

    internal static MeasurementPoint ClonePoint(MeasurementPoint point, int index) => new()
    {
        Index = index,
        X = point.X,
        Y = point.Y,
        Z = point.Z,
        NormalX = point.NormalX,
        NormalY = point.NormalY,
        NormalZ = point.NormalZ,
        ApproachDistance = point.ApproachDistance,
        RetractDistance = point.RetractDistance,
        SearchDistance = point.SearchDistance,
        AssignedSafetyPlaneFace = point.AssignedSafetyPlaneFace,
        SafetyPlaneAssignmentScore = point.SafetyPlaneAssignmentScore,
        SafetyPlaneSafeX = point.SafetyPlaneSafeX,
        SafetyPlaneSafeY = point.SafetyPlaneSafeY,
        SafetyPlaneSafeZ = point.SafetyPlaneSafeZ
    };

    internal static double EstimateTimeSeconds(double distanceMm) =>
        distanceMm / DefaultMachineSpeedMmPerSecond;

    internal static IReadOnlyList<MeasurementPoint> CloneAndReindex(IReadOnlyList<MeasurementPoint> points) =>
        points.Select((point, index) => ClonePoint(point, index + 1)).ToList();
}
