using System.Numerics;

namespace OpenKogama.Physics;

static class UnityMath
{
    public const float Rad2Deg = 57.29578f;
    public const float Deg2Rad = MathF.PI / 180f;

    public static Vector3 Normalized(Vector3 value)
    {
        float length = value.Length();
        return length > 1E-05f ? value / length : Vector3.Zero;
    }

    public static float Angle(Vector3 from, Vector3 to)
    {
        float denominator = MathF.Sqrt(from.LengthSquared() * to.LengthSquared());
        if (denominator < 1E-15f) return 0f;
        float dot = Math.Clamp(Vector3.Dot(from, to) / denominator, -1f, 1f);
        return MathF.Acos(dot) * Rad2Deg;
    }

    public static Vector3 Project(Vector3 vector, Vector3 onNormal)
    {
        float lengthSquared = Vector3.Dot(onNormal, onNormal);
        return lengthSquared < float.Epsilon ? Vector3.Zero : onNormal * Vector3.Dot(vector, onNormal) / lengthSquared;
    }

    public static Vector3 PlaneNormal(Vector3 a, Vector3 b, Vector3 c) => Normalized(Vector3.Cross(b - a, c - a));

    public static double SignedDistance(Vector3 planeNormal, Vector3 planeOrigin, Vector3 point) =>
        Vector3.Dot(planeNormal, point) + (0f - (planeNormal.X * planeOrigin.X + planeNormal.Y * planeOrigin.Y + planeNormal.Z * planeOrigin.Z));

    public static Vector3 TriangleNormal(Vector3 a, Vector3 b, Vector3 c) => Normalized(new Vector3(
        (b.Y - a.Y) * (c.Z - a.Z) - (b.Z - a.Z) * (c.Y - a.Y),
        (b.Z - a.Z) * (c.X - a.X) - (b.X - a.X) * (c.Z - a.Z),
        (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X)));

    public static bool LineFacet(Vector3 p1, Vector3 p2, Vector3 pa, Vector3 pb, Vector3 pc)
    {
        Vector3 normal = Normalized(new Vector3(
            (pb.Y - pa.Y) * (pc.Z - pa.Z) - (pb.Z - pa.Z) * (pc.Y - pa.Y),
            (pb.Z - pa.Z) * (pc.X - pa.X) - (pb.X - pa.X) * (pc.Z - pa.Z),
            (pb.X - pa.X) * (pc.Y - pa.Y) - (pb.Y - pa.Y) * (pc.X - pa.X)));
        float d = -normal.X * pa.X - normal.Y * pa.Y - normal.Z * pa.Z;
        float denominator = normal.X * (p2.X - p1.X) + normal.Y * (p2.Y - p1.Y) + normal.Z * (p2.Z - p1.Z);
        if (MathF.Abs(denominator) < float.Epsilon) return false;
        float mu = -(d + normal.X * p1.X + normal.Y * p1.Y + normal.Z * p1.Z) / denominator;
        Vector3 p = p1 + mu * (p2 - p1);
        if (mu < 0f || mu > 1f) return false;

        Vector3 a = Normalized(pa - p);
        Vector3 b = Normalized(pb - p);
        Vector3 c = Normalized(pc - p);
        float total = (MathF.Acos(Vector3.Dot(a, b)) + MathF.Acos(Vector3.Dot(b, c)) + MathF.Acos(Vector3.Dot(c, a))) * Rad2Deg;
        return MathF.Abs(total - 360f) <= 0.1f;
    }

    public static Vector3 Point(Matrix4x4 matrix, Vector3 point) => Vector3.Transform(point, matrix);

    public static Vector3 Direction(Matrix4x4 matrix, Vector3 direction) => Vector3.TransformNormal(direction, matrix);
}
