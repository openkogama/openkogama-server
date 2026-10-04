using System.Numerics;

namespace OpenKogama.Physics;

static class TriangleCheck
{
    public static bool Sweep(Vector3 p1, Vector3 p2, Vector3 p3, Vector3 origin, Vector3 direction, float distance, out Vector3 point, out float hitDistance)
    {
        point = Vector3.Zero;
        hitDistance = float.PositiveInfinity;

        Vector3 planeNormal = UnityMath.PlaneNormal(p1, p2, p3);
        Vector3 b = direction * distance;
        if (Vector3.Dot(planeNormal, direction) > 0f) return false;

        bool embedded = false;
        double signed = UnityMath.SignedDistance(planeNormal, p1, origin);
        float normalDotVelocity = Vector3.Dot(planeNormal, b);
        double t0;
        if (normalDotVelocity == 0f)
        {
            if (Math.Abs(signed) >= 1.0) return false;
            embedded = true;
            t0 = 0.0;
        }
        else
        {
            t0 = (-1.0 - signed) / normalDotVelocity;
            double t1 = (1.0 - signed) / normalDotVelocity;
            if (t0 > t1) (t0, t1) = (t1, t0);
            if (t0 > 1.0 || t1 < 0.0) return false;
            if (t0 < 0.0) t0 = 0.0;
            if (t0 > 1.0) t0 = 1.0;
        }

        bool found = false;
        double best = 1.0;
        if (!embedded)
        {
            float time = (float)t0;
            Vector3 contact = origin - planeNormal + time * b;
            if (PointInTriangle(contact, p1, p2, p3))
            {
                found = true;
                best = t0;
                point = contact;
            }
        }

        if (!found)
        {
            float velocitySquared = b.LengthSquared();
            float root = 0f;

            Vector3 toP1 = p1 - origin;
            float distanceP1 = toP1.LengthSquared();
            if (LowestRoot(velocitySquared, 2f * Vector3.Dot(b, origin - p1), distanceP1 - 1f, (float)best, ref root))
            {
                best = root;
                found = true;
                point = p1;
            }

            Vector3 toP2 = p2 - origin;
            float distanceP2 = toP2.LengthSquared();
            if (LowestRoot(velocitySquared, 2f * Vector3.Dot(b, origin - p2), distanceP2 - 1f, (float)best, ref root))
            {
                best = root;
                found = true;
                point = p2;
            }

            Vector3 toP3 = p3 - origin;
            float distanceP3 = toP3.LengthSquared();
            if (LowestRoot(velocitySquared, 2f * Vector3.Dot(b, origin - p3), distanceP3 - 1f, (float)best, ref root))
            {
                best = root;
                found = true;
                point = p3;
            }

            Edge(p1, p2, toP1, distanceP1, b, velocitySquared, ref best, ref found, ref point, ref root);
            Edge(p2, p3, toP2, distanceP2, b, velocitySquared, ref best, ref found, ref point, ref root);
            Edge(p3, p1, toP3, distanceP3, b, velocitySquared, ref best, ref found, ref point, ref root);
        }

        if (!found) return false;
        hitDistance = (float)best * b.Length();
        return true;
    }

    static void Edge(Vector3 from, Vector3 to, Vector3 baseToVertex, float baseToVertexSquared, Vector3 b, float velocitySquared,
        ref double best, ref bool found, ref Vector3 point, ref float root)
    {
        Vector3 edge = to - from;
        float edgeSquared = edge.LengthSquared();
        float edgeDotVelocity = Vector3.Dot(edge, b);
        float edgeDotBase = Vector3.Dot(edge, baseToVertex);
        float a = edgeSquared * -velocitySquared + edgeDotVelocity * edgeDotVelocity;
        float bb = edgeSquared * (2f * Vector3.Dot(b, baseToVertex)) - 2f * edgeDotVelocity * edgeDotBase;
        float c = edgeSquared * (1f - baseToVertexSquared) + edgeDotBase * edgeDotBase;
        if (!LowestRoot(a, bb, c, (float)best, ref root)) return;
        float f = (edgeDotVelocity * root - edgeDotBase) / edgeSquared;
        if (f < 0.0 || f > 1.0) return;
        best = root;
        found = true;
        point = from + f * edge;
    }

    static bool LowestRoot(float a, float b, float c, float maxR, ref float root)
    {
        if (a == 0f) return false;
        float determinant = b * b - 4f * a * c;
        if (determinant < 0f) return false;
        float sqrtD = MathF.Sqrt(determinant);
        float r1 = (-b - sqrtD) / (2f * a);
        float r2 = (-b + sqrtD) / (2f * a);
        if (r1 > r2) (r1, r2) = (r2, r1);
        if (r1 > 0f && r1 < maxR)
        {
            root = r1;
            return true;
        }
        if (r2 > 0f && r2 < maxR)
        {
            root = r2;
            return true;
        }
        return false;
    }

    static bool SameSide(Vector3 p1, Vector3 p2, Vector3 a, Vector3 b)
    {
        Vector3 edge = b - a;
        return Vector3.Dot(Vector3.Cross(edge, p1 - a), Vector3.Cross(edge, p2 - a)) >= 0f;
    }

    static bool PointInTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c) =>
        SameSide(p, a, b, c) && SameSide(p, b, a, c) && SameSide(p, c, a, b);

    public static bool Separated(Vector3 a, Vector3 b, Vector3 c, float radius)
    {
        if ((a - b).LengthSquared() < 0.01f || (a - c).LengthSquared() < 0.01f || (b - c).LengthSquared() < 0.01f) return true;
        float rr = radius * radius;
        Vector3 normal = UnityMath.Normalized(Vector3.Cross(b - a, c - a));
        float d = Vector3.Dot(a, normal);
        float e = Vector3.Dot(normal, normal);
        bool separatedPlane = d * d > rr * e;
        float aa = Vector3.Dot(a, a);
        float ab = Vector3.Dot(a, b);
        float ac = Vector3.Dot(a, c);
        float bb = Vector3.Dot(b, b);
        float bc = Vector3.Dot(b, c);
        float cc = Vector3.Dot(c, c);
        bool separatedA = aa > rr & ab > aa & ac > aa;
        bool separatedB = bb > rr & ab > bb & bc > bb;
        bool separatedC = cc > rr & ac > cc & bc > cc;
        Vector3 ab3 = b - a;
        Vector3 bc3 = c - b;
        Vector3 ca3 = a - c;
        float d1 = ab - aa;
        float d2 = bc - bb;
        float d3 = ac - cc;
        float e1 = Vector3.Dot(ab3, ab3);
        float e2 = Vector3.Dot(bc3, bc3);
        float e3 = Vector3.Dot(ca3, ca3);
        Vector3 q1 = a * e1 - d1 * ab3;
        Vector3 q2 = b * e2 - d2 * bc3;
        Vector3 q3 = c * e3 - d3 * ca3;
        Vector3 qc = c * e1 - q1;
        Vector3 qa = a * e2 - q2;
        Vector3 qb = b * e3 - q3;
        bool separatedAB = Vector3.Dot(q1, q1) > rr * e1 * e1 & Vector3.Dot(q1, qc) > 0f;
        bool separatedBC = Vector3.Dot(q2, q2) > rr * e2 * e2 & Vector3.Dot(q2, qa) > 0f;
        bool separatedCA = Vector3.Dot(q3, q3) > rr * e3 * e3 & Vector3.Dot(q3, qb) > 0f;
        return separatedPlane || separatedA || separatedB || separatedC || separatedAB || separatedBC || separatedCA;
    }
}
