namespace TZTEK.VispecCMM.Import.Core.Planning.Path;

internal readonly record struct SurfacePoint(
    SafetyPlaneFace Face,
    double X,
    double Y,
    double Z)
{
    public PlanningVectors.Vec3 ToVec3() => new(X, Y, Z);
}

internal static class SafetyPlaneSurfaceRouter
{
    private static readonly IReadOnlyDictionary<SafetyPlaneFace, SafetyPlaneFace[]> AdjacentFaces =
        new Dictionary<SafetyPlaneFace, SafetyPlaneFace[]>
        {
            [SafetyPlaneFace.Top] = [SafetyPlaneFace.PosX, SafetyPlaneFace.NegX, SafetyPlaneFace.PosY, SafetyPlaneFace.NegY],
            [SafetyPlaneFace.Bottom] = [SafetyPlaneFace.PosX, SafetyPlaneFace.NegX, SafetyPlaneFace.PosY, SafetyPlaneFace.NegY],
            [SafetyPlaneFace.PosX] = [SafetyPlaneFace.Top, SafetyPlaneFace.Bottom, SafetyPlaneFace.PosY, SafetyPlaneFace.NegY],
            [SafetyPlaneFace.NegX] = [SafetyPlaneFace.Top, SafetyPlaneFace.Bottom, SafetyPlaneFace.PosY, SafetyPlaneFace.NegY],
            [SafetyPlaneFace.PosY] = [SafetyPlaneFace.Top, SafetyPlaneFace.Bottom, SafetyPlaneFace.PosX, SafetyPlaneFace.NegX],
            [SafetyPlaneFace.NegY] = [SafetyPlaneFace.Top, SafetyPlaneFace.Bottom, SafetyPlaneFace.PosX, SafetyPlaneFace.NegX]
        };

    public static double SurfaceDistance(SafetyPlaneBox envelope, SurfacePoint from, SurfacePoint to)
    {
        if (from.Face == to.Face)
            return PlanningVectors.Distance(from.ToVec3(), to.ToVec3());

        var path = FindFacePath(from.Face, to.Face);
        if (path.Count == 0)
            return PlanningVectors.Distance(from.ToVec3(), to.ToVec3());

        var total = 0.0;
        var current = from;
        foreach (var nextFace in path.Skip(1))
        {
            var edgePoint = FindBestEdgePoint(envelope, current, nextFace);
            total += PlanningVectors.Distance(current.ToVec3(), edgePoint.ToVec3());
            current = edgePoint with { Face = nextFace };
        }

        total += PlanningVectors.Distance(current.ToVec3(), to.ToVec3());
        return total;
    }

    public static IReadOnlyList<SurfacePoint> BuildSurfaceWaypoints(
        SafetyPlaneBox envelope,
        SurfacePoint from,
        SurfacePoint to)
    {
        if (from.Face == to.Face)
            return BuildSameFaceWaypoints(from, to);

        var path = FindFacePath(from.Face, to.Face);
        if (path.Count == 0)
            return [to];

        var waypoints = new List<SurfacePoint>();
        var current = from;
        foreach (var nextFace in path.Skip(1))
        {
            var edgePoint = FindBestEdgePoint(envelope, current, nextFace);
            waypoints.Add(edgePoint);
            current = edgePoint with { Face = nextFace };
        }

        waypoints.AddRange(BuildSameFaceWaypoints(current, to));
        return waypoints;
    }

    /// <summary>
    /// 在同一安全面内用曼哈顿路径移动，避免 3D 弦线穿过工件包围盒内部。
    /// </summary>
    public static IReadOnlyList<SurfacePoint> BuildSameFaceWaypoints(SurfacePoint from, SurfacePoint to)
    {
        if (AreSamePoint(from, to))
            return [];

        var waypoints = new List<SurfacePoint>();
        var face = from.Face;

        switch (face)
        {
            case SafetyPlaneFace.Top:
            case SafetyPlaneFace.Bottom:
                if (Math.Abs(from.X - to.X) > 1e-6)
                    waypoints.Add(new SurfacePoint(face, to.X, from.Y, from.Z));
                if (Math.Abs(from.Y - to.Y) > 1e-6)
                    waypoints.Add(to);
                break;

            case SafetyPlaneFace.PosX:
            case SafetyPlaneFace.NegX:
                if (Math.Abs(from.Y - to.Y) > 1e-6)
                    waypoints.Add(new SurfacePoint(face, from.X, to.Y, from.Z));
                if (Math.Abs(from.Z - to.Z) > 1e-6)
                    waypoints.Add(to);
                break;

            case SafetyPlaneFace.PosY:
            case SafetyPlaneFace.NegY:
                if (Math.Abs(from.X - to.X) > 1e-6)
                    waypoints.Add(new SurfacePoint(face, to.X, from.Y, from.Z));
                if (Math.Abs(from.Z - to.Z) > 1e-6)
                    waypoints.Add(to);
                break;

            default:
                waypoints.Add(to);
                break;
        }

        if (waypoints.Count == 0)
            waypoints.Add(to);

        return waypoints;
    }

    public static SurfacePoint ToSurfacePoint(SafetyPlaneBox envelope, FeatureSafetyProjection projection) =>
        new(projection.AssignedFace, projection.ProjectionOnPlane.X, projection.ProjectionOnPlane.Y, projection.ProjectionOnPlane.Z);

    public static SurfacePoint ToSurfacePoint(MeasurementPoint point)
    {
        var face = point.AssignedSafetyPlaneFace ?? SafetyPlaneFace.Top;
        var x = point.SafetyPlaneSafeX ?? point.X;
        var y = point.SafetyPlaneSafeY ?? point.Y;
        var z = point.SafetyPlaneSafeZ ?? point.Z;
        return new SurfacePoint(face, x, y, z);
    }

    private static IReadOnlyList<SafetyPlaneFace> FindFacePath(SafetyPlaneFace from, SafetyPlaneFace to)
    {
        if (from == to)
            return [from];

        var queue = new Queue<SafetyPlaneFace>();
        var previous = new Dictionary<SafetyPlaneFace, SafetyPlaneFace?>();
        queue.Enqueue(from);
        previous[from] = null;

        while (queue.Count > 0)
        {
            var face = queue.Dequeue();
            if (face == to)
                break;

            foreach (var neighbor in AdjacentFaces[face])
            {
                if (previous.ContainsKey(neighbor))
                    continue;

                previous[neighbor] = face;
                queue.Enqueue(neighbor);
            }
        }

        if (!previous.ContainsKey(to))
            return [];

        var path = new List<SafetyPlaneFace>();
        SafetyPlaneFace? cursor = to;
        while (cursor is not null)
        {
            path.Add(cursor.Value);
            cursor = previous[cursor.Value];
        }

        path.Reverse();
        return path;
    }

    private static SurfacePoint FindBestEdgePoint(
        SafetyPlaneBox envelope,
        SurfacePoint from,
        SafetyPlaneFace toFace)
    {
        var best = from;
        var bestScore = double.PositiveInfinity;
        foreach (var candidate in SampleSharedEdgePoints(envelope, from.Face, toFace))
        {
            var score = PlanningVectors.Distance(from.ToVec3(), candidate.ToVec3());
            if (score >= bestScore)
                continue;

            bestScore = score;
            best = candidate;
        }

        return best;
    }

    private static IEnumerable<SurfacePoint> SampleSharedEdgePoints(
        SafetyPlaneBox envelope,
        SafetyPlaneFace faceA,
        SafetyPlaneFace faceB)
    {
        const int samples = 16;
        for (var i = 0; i <= samples; i++)
        {
            var t = (double)i / samples;
            yield return InterpolateEdgePoint(envelope, faceA, faceB, t);
        }
    }

    private static SurfacePoint InterpolateEdgePoint(
        SafetyPlaneBox envelope,
        SafetyPlaneFace faceA,
        SafetyPlaneFace faceB,
        double t)
    {
        var pair = NormalizePair(faceA, faceB);
        return pair switch
        {
            (SafetyPlaneFace.Top, SafetyPlaneFace.PosX) => new SurfacePoint(faceA, envelope.MaxX, Lerp(envelope.MinY, envelope.MaxY, t), envelope.MaxZ),
            (SafetyPlaneFace.Top, SafetyPlaneFace.NegX) => new SurfacePoint(faceA, envelope.MinX, Lerp(envelope.MinY, envelope.MaxY, t), envelope.MaxZ),
            (SafetyPlaneFace.Top, SafetyPlaneFace.PosY) => new SurfacePoint(faceA, Lerp(envelope.MinX, envelope.MaxX, t), envelope.MaxY, envelope.MaxZ),
            (SafetyPlaneFace.Top, SafetyPlaneFace.NegY) => new SurfacePoint(faceA, Lerp(envelope.MinX, envelope.MaxX, t), envelope.MinY, envelope.MaxZ),
            (SafetyPlaneFace.Bottom, SafetyPlaneFace.PosX) => new SurfacePoint(faceA, envelope.MaxX, Lerp(envelope.MinY, envelope.MaxY, t), envelope.MinZ),
            (SafetyPlaneFace.Bottom, SafetyPlaneFace.NegX) => new SurfacePoint(faceA, envelope.MinX, Lerp(envelope.MinY, envelope.MaxY, t), envelope.MinZ),
            (SafetyPlaneFace.Bottom, SafetyPlaneFace.PosY) => new SurfacePoint(faceA, Lerp(envelope.MinX, envelope.MaxX, t), envelope.MaxY, envelope.MinZ),
            (SafetyPlaneFace.Bottom, SafetyPlaneFace.NegY) => new SurfacePoint(faceA, Lerp(envelope.MinX, envelope.MaxX, t), envelope.MinY, envelope.MinZ),
            (SafetyPlaneFace.PosX, SafetyPlaneFace.PosY) => new SurfacePoint(faceA, envelope.MaxX, envelope.MaxY, Lerp(envelope.MinZ, envelope.MaxZ, t)),
            (SafetyPlaneFace.PosX, SafetyPlaneFace.NegY) => new SurfacePoint(faceA, envelope.MaxX, envelope.MinY, Lerp(envelope.MinZ, envelope.MaxZ, t)),
            (SafetyPlaneFace.NegX, SafetyPlaneFace.PosY) => new SurfacePoint(faceA, envelope.MinX, envelope.MaxY, Lerp(envelope.MinZ, envelope.MaxZ, t)),
            (SafetyPlaneFace.NegX, SafetyPlaneFace.NegY) => new SurfacePoint(faceA, envelope.MinX, envelope.MinY, Lerp(envelope.MinZ, envelope.MaxZ, t)),
            _ => new SurfacePoint(faceA, envelope.CenterX, envelope.CenterY, envelope.CenterZ)
        };
    }

    private static (SafetyPlaneFace, SafetyPlaneFace) NormalizePair(SafetyPlaneFace a, SafetyPlaneFace b)
    {
        var order = new[] { SafetyPlaneFace.Top, SafetyPlaneFace.Bottom, SafetyPlaneFace.PosX, SafetyPlaneFace.NegX, SafetyPlaneFace.PosY, SafetyPlaneFace.NegY };
        return Array.IndexOf(order, a) <= Array.IndexOf(order, b) ? (a, b) : (b, a);
    }

    private static double Lerp(double min, double max, double t) => min + (max - min) * t;

    private static bool AreSamePoint(SurfacePoint left, SurfacePoint right) =>
        left.Face == right.Face
        && Math.Abs(left.X - right.X) < 1e-6
        && Math.Abs(left.Y - right.Y) < 1e-6
        && Math.Abs(left.Z - right.Z) < 1e-6;
}
