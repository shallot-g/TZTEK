namespace TZTEK.VispecCMM.Import.Core.Planning.Collision;

internal static class CollisionVectors
{
    internal readonly record struct Vec3(double X, double Y, double Z)
    {
        public static implicit operator (double X, double Y, double Z)(Vec3 value) =>
            (value.X, value.Y, value.Z);

        public static implicit operator Vec3((double X, double Y, double Z) value) =>
            new(value.X, value.Y, value.Z);
    }

    internal static Vec3 Normalize(Vec3 value)
    {
        var length = Length(value);
        return length < 1e-12 ? new Vec3(0, 0, 1) : new Vec3(value.X / length, value.Y / length, value.Z / length);
    }

    internal static double Length(Vec3 value) =>
        Math.Sqrt(value.X * value.X + value.Y * value.Y + value.Z * value.Z);

    internal static double Distance(Vec3 left, Vec3 right)
    {
        var dx = left.X - right.X;
        var dy = left.Y - right.Y;
        var dz = left.Z - right.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    internal static Vec3 Add(Vec3 left, Vec3 right) =>
        new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);

    internal static Vec3 Subtract(Vec3 left, Vec3 right) =>
        new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);

    internal static Vec3 Scale(Vec3 value, double scale) =>
        new(value.X * scale, value.Y * scale, value.Z * scale);

    internal static double Dot(Vec3 left, Vec3 right) =>
        left.X * right.X + left.Y * right.Y + left.Z * right.Z;

    internal static Vec3 Cross(Vec3 left, Vec3 right) =>
        new(
            left.Y * right.Z - left.Z * right.Y,
            left.Z * right.X - left.X * right.Z,
            left.X * right.Y - left.Y * right.X);
}
