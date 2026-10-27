using System.Collections.Concurrent;

namespace Worldgen;

readonly record struct Point(double X, double Y, double Z)
{
    public double this[int axis] => axis switch { 0 => X, 1 => Y, _ => Z };

    public Point With(int axis, double value) => axis switch
    {
        0 => this with { X = value },
        1 => this with { Y = value },
        _ => this with { Z = value },
    };

    public static Point operator -(Point a, Point b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
}

readonly record struct Tri(int Face, Point[] P, Point N);

readonly record struct ExportedCube(int X, int Y, int Z, byte[] Materials, byte[] Corners);

static class KogamaTerrain
{
    public const double Eps = 1e-8;
    public static readonly int[][] KFaces = [[0, 1, 3, 2], [5, 4, 6, 7], [4, 0, 2, 6], [1, 5, 7, 3], [4, 5, 1, 0], [2, 3, 7, 6]];
    public static readonly int[][] KN = [[0, 0, -1], [0, 0, 1], [-1, 0, 0], [1, 0, 0], [0, -1, 0], [0, 1, 0]];
    public static readonly int[] Axis = [2, 2, 0, 0, 1, 1];
    public static readonly int[] KogamaOrder = [2, 3, 7, 6, 4, 5, 1, 0];
    public static readonly int[] All = [0, 1, 2, 3, 4, 5, 6, 7];
    public static readonly int[] Top = [2, 3, 7, 6];
    public static readonly int[] Under = [0, 1, 5, 4];
    public static readonly byte[] IdentityBytes = [20, 120, 124, 24, 4, 104, 100, 0];

    static readonly int[][] Halves = [[0, 1, 2], [0, 2, 3]];
    const int TrianglesCacheSize = 65536;
    static readonly ConcurrentDictionary<ulong, List<Tri>> Triangles = new();

    public static double JsRound(double x) => Math.Floor(x + 0.5);

    public static int FloorDiv(int a, int b) => a / b - (a % b < 0 ? 1 : 0);

    public static double Cross(Point a, Point b, Point c) =>
        (b.X - a.X) * (c.Z - a.Z) - (b.Z - a.Z) * (c.X - a.X);

    public static Point Cross3(Point a, Point b) =>
        new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

    public static double PolygonArea(IReadOnlyList<Point> poly)
    {
        double total = 0.0;
        int n = poly.Count;
        for (int i = 0; i < n; i++)
        {
            Point a = poly[i];
            Point b = poly[(i + 1) % n];
            total += a.X * b.Z - b.X * a.Z;
        }
        return Math.Abs(total) / 2;
    }

    public static bool InsidePolygon(IReadOnlyList<Point> poly, Point p)
    {
        int n = poly.Count;
        for (int i = 0; i < n; i++)
            if (Cross(poly[i], poly[(i + 1) % n], p) < -Eps)
                return false;
        return true;
    }

    public static List<Point> InscribedQuad(List<Point> poly)
    {
        while (poly.Count > 4)
        {
            int n = poly.Count;
            (double Lost, int Index)? best = null;
            foreach (bool cornersOnly in (bool[])[true, false])
            {
                for (int i = 0; i < n; i++)
                {
                    Point p = poly[i];
                    if (cornersOnly && (p.X is not (0 or 4) || p.Z is not (0 or 4)))
                        continue;
                    double lost = Math.Abs(Cross(poly[(i - 1 + n) % n], p, poly[(i + 1) % n])) / 2;
                    if (best is null || lost < best.Value.Lost)
                        best = (lost, i);
                }
                if (best is not null)
                    break;
            }
            int removed = best!.Value.Index;
            poly = [.. poly.Where((_, i) => i != removed)];
        }
        return poly;
    }

    public static Point Nominal(int n) => new((n & 1) * 4, ((n >> 1) & 1) * 4, ((n >> 2) & 1) * 4);

    public static bool CubeLegal(Point[] cor)
    {
        for (int f = 0; f < 6; f++)
        {
            double ax = 0, ay = 0, az = 0;
            int[] face = KFaces[f];
            for (int k = 0; k < 4; k++)
            {
                Point a = cor[face[k]];
                Point b = cor[face[(k + 1) % 4]];
                ax += a.Y * b.Z - a.Z * b.Y;
                ay += a.Z * b.X - a.X * b.Z;
                az += a.X * b.Y - a.Y * b.X;
            }
            int[] n = KN[f];
            if (ax * n[0] + ay * n[1] + az * n[2] > 1e-4)
                return false;
        }
        return true;
    }

    public static List<Tri> KogamaTriangles(Point[] cor)
    {
        var result = new List<Tri>();
        for (int f = 0; f < 6; f++)
        {
            Point[] v = [.. KFaces[f].Select(n => cor[n])];
            foreach (int[] ids in Halves)
            {
                Point[] p = [.. ids.Select(i => v[i])];
                Point n = Cross3(p[2] - p[0], p[1] - p[0]);
                if (n.X != 0 || n.Y != 0 || n.Z != 0)
                    result.Add(new Tri(f, p, n));
            }
        }
        return result;
    }

    public static double Height(Point p, Point[] tri)
    {
        double area = Cross(tri[0], tri[1], tri[2]);
        return (Cross(p, tri[1], tri[2]) * tri[0].Y + Cross(tri[0], p, tri[2]) * tri[1].Y
            + Cross(tri[0], tri[1], p) * tri[2].Y) / area;
    }

    public static List<Point> Overlap(IReadOnlyList<Point> a, Point[] b)
    {
        List<Point> poly = [.. a];
        double direction = Cross(b[0], b[1], b[2]);
        direction = (direction > 0 ? 1 : 0) - (direction < 0 ? 1 : 0);
        for (int e = 0; e < 3; e++)
        {
            if (poly.Count == 0)
                break;
            Point c = b[e];
            Point d = b[(e + 1) % 3];
            var output = new List<Point>();
            int n = poly.Count;
            for (int i = 0; i < n; i++)
            {
                Point p = poly[i];
                Point q = poly[(i + 1) % n];
                double cp = direction * Cross(c, d, p);
                double cq = direction * Cross(c, d, q);
                if (cp >= -Eps)
                    output.Add(p);
                if ((cp >= -Eps) != (cq >= -Eps))
                {
                    double t = cp / (cp - cq);
                    output.Add(new Point(p.X + t * (q.X - p.X), 0, p.Z + t * (q.Z - p.Z)));
                }
            }
            poly = output;
        }
        return poly;
    }

    public static double RoofHeight(List<Tri> roof, Point p)
    {
        double best = double.NegativeInfinity;
        foreach (Tri r in roof)
        {
            bool inside = true;
            for (int i = 0; i < 3; i++)
            {
                if (Cross(r.P[i], r.P[(i + 1) % 3], p) < -Eps)
                {
                    inside = false;
                    break;
                }
            }
            if (inside)
            {
                double h = Height(p, r.P);
                if (h > best)
                    best = h;
            }
        }
        return best;
    }

    public static bool SegmentUnderRoof(Point a, Point b, List<Tri> roof, IReadOnlyList<Point>? solid = null)
    {
        var stops = new List<double> { 0.0, 1.0 };
        foreach (Tri r in roof)
        {
            for (int e = 0; e < 3; e++)
            {
                Point c = r.P[e];
                Point d = r.P[(e + 1) % 3];
                double ca = Cross(c, d, a);
                double cb = Cross(c, d, b);
                if (ca != cb)
                {
                    double t = ca / (ca - cb);
                    if (0 < t && t < 1)
                        stops.Add(t);
                }
            }
        }
        foreach (double t in stops)
        {
            var p = new Point(a.X + t * (b.X - a.X), a.Y + t * (b.Y - a.Y), a.Z + t * (b.Z - a.Z));
            double h = RoofHeight(roof, p);
            if (p.Y > h + Eps && !(solid is not null && h == double.NegativeInfinity && InsidePolygon(solid, p)))
                return false;
        }
        return true;
    }

    public static List<Tri> CachedTriangles(Point[] cor)
    {
        ulong key = 0;
        foreach (Point p in cor)
            key = key << 7 | (ulong)(p.X * 25 + p.Y * 5 + p.Z);
        if (Triangles.TryGetValue(key, out List<Tri>? cached))
            return cached;
        if (Triangles.Count >= TrianglesCacheSize)
            Triangles.Clear();
        return Triangles[key] = KogamaTriangles(cor);
    }

    public static bool KogamaValid(Point[] cor, int[] roofFaces, double area, bool floorRoof, IReadOnlyList<Point>? solid = null)
    {
        var roof = new List<Tri>();
        var rest = new List<Tri>();
        foreach (Tri t in CachedTriangles(cor))
        {
            if (roofFaces.Contains(t.Face))
            {
                if (t.N.Y < 0)
                    return false;
                if (t.N.Y > 0)
                    roof.Add(t);
            }
            else
                rest.Add(t);
        }
        double covered = 0.0;
        for (int i = 0; i < roof.Count; i++)
        {
            Tri r = roof[i];
            Point n = r.N;
            if (!floorRoof && r.P.All(p => p.Y == 0))
                return false;
            covered += n.Y / 2;
            for (int j = 0; j < i; j++)
                if (PolygonArea(Overlap(r.P, roof[j].P)) > Eps)
                    return false;
        }
        if (Math.Abs(covered - area) > Eps)
            return false;
        foreach (Tri t in rest)
        {
            if (solid is not null && !t.P.All(p => InsidePolygon(solid, p)))
                return false;
            if (t.N.Y == 0)
            {
                for (int e = 0; e < 3; e++)
                    if (!SegmentUnderRoof(t.P[e], t.P[(e + 1) % 3], roof, solid))
                        return false;
                continue;
            }
            foreach (Tri r in roof)
            {
                List<Point> poly = Overlap(t.P, r.P);
                if (poly.Count == 0)
                    continue;
                bool coplanar = true;
                foreach (Point p in poly)
                {
                    double d = Height(p, t.P) - Height(p, r.P);
                    if (d > Eps)
                        return false;
                    if (Math.Abs(d) > Eps)
                        coplanar = false;
                }
                if (coplanar && t.N.Y > 0 && PolygonArea(poly) > Eps)
                    return false;
            }
        }
        return true;
    }

    public static double RoofCost(Point[] cor, int[] faces)
    {
        double cost = 0.0;
        foreach (int f in faces)
        {
            Point[] v = [.. KFaces[f].Select(n => cor[n])];
            foreach (int[] ids in Halves)
            {
                Point[] p = [.. ids.Select(i => v[i])];
                Point n = Cross3(p[2] - p[0], p[1] - p[0]);
                if (n.Y <= 0)
                    continue;
                double length = Math.Sqrt(n.X * n.X + n.Y * n.Y + n.Z * n.Z);
                double projected = Math.Abs(n[Axis[f]]);
                if (projected == 0)
                    return double.PositiveInfinity;
                cost += length / 2 * (length / projected - 1);
            }
        }
        return cost;
    }

    public static readonly Point[] Nominals = [.. All.Select(Nominal)];

    public static double Misfit(Point[] cor, int[] ids)
    {
        double total = 0;
        foreach (int n in ids)
        {
            Point p = cor[n];
            Point q = Nominals[n];
            total += (p.X - q.X) * (p.X - q.X) + (p.Z - q.Z) * (p.Z - q.Z) + (q.Y == 0 ? 4 * p.Y * p.Y : 0);
        }
        return total;
    }

    public static int[] RingOf(int[] faces)
    {
        var all = new List<(int A, int B)>();
        foreach (int f in faces)
        {
            int[] face = KFaces[f];
            for (int i = 0; i < 4; i++)
                all.Add((face[i], face[(i + 1) % 4]));
        }
        List<(int A, int B)> edges = [.. all.Where(e => !all.Any(other => other.A == e.B && other.B == e.A))];
        var ring = new List<int> { edges[0].A };
        while (ring.Count < edges.Count)
            ring.Add(edges.First(e => e.A == ring[^1]).B);
        return [.. ring];
    }

    public static List<Point[]> Variants(IReadOnlyList<Point> poly, int slots)
    {
        var output = new List<Point[]>();

        void Distribute(int i, int left, List<Point> q)
        {
            if (i == poly.Count - 1)
            {
                if (left >= 1)
                    output.Add([.. q, .. Enumerable.Repeat(poly[i], left)]);
                return;
            }
            for (int n = 1; n < left - (poly.Count - i - 1) + 1; n++)
                Distribute(i + 1, left - n, [.. q, .. Enumerable.Repeat(poly[i], n)]);
        }

        Distribute(0, slots, []);
        return output;
    }

    public static int RepairSaddles(int[,] H, bool pinEdges = false)
    {
        int rows = H.GetLength(0);
        int columns = H.GetLength(1);
        int moved = 0;
        for (int pass = 0; pass < 64; pass++)
        {
            var flagged = new List<(int K, int I)>();
            for (int k = 0; k < rows - 1; k++)
            {
                for (int i = 0; i < columns - 1; i++)
                {
                    int a = H[k, i], b = H[k, i + 1], c = H[k + 1, i + 1], d = H[k + 1, i];
                    int q1 = (FloorDiv(Math.Max(b, d), 4) + 1) * 4;
                    int q2 = (FloorDiv(Math.Max(a, c), 4) + 1) * 4;
                    if (q1 < Math.Min(a, c) || q2 < Math.Min(b, d))
                        flagged.Add((k, i));
                }
            }
            bool changed = false;
            foreach ((int k, int i) in flagged)
            {
                int[] h = [H[k, i], H[k, i + 1], H[k + 1, i + 1], H[k + 1, i]];
                int q = (FloorDiv(h.Min(), 4) + 1) * 4;
                while (q < h.Max())
                {
                    if ((h[0] > q && h[2] > q && h[1] < q && h[3] < q) || (h[0] < q && h[2] < q && h[1] > q && h[3] > q))
                    {
                        int n = -1;
                        for (int m = 0; m < 4; m++)
                        {
                            int row = k + (m >= 2 ? 1 : 0);
                            int column = i + (m is 1 or 2 ? 1 : 0);
                            if (pinEdges && (row == 0 || row == rows - 1 || column == 0 || column == columns - 1))
                                continue;
                            if (n < 0 || Math.Abs(h[m] - q) < Math.Abs(h[n] - q))
                                n = m;
                        }
                        if (n < 0)
                            break;
                        H[k + (n >= 2 ? 1 : 0), i + (n is 1 or 2 ? 1 : 0)] = q;
                        moved++;
                        changed = true;
                        break;
                    }
                    q += 4;
                }
            }
            if (!changed)
                break;
        }
        return moved;
    }

    public static readonly Point[] Full = [.. All.Select(Nominal)];

    public static byte[] CornerBytes(Point[] cor) =>
        [.. KogamaOrder.Select(n => (byte)(cor[n].X * 25 + cor[n].Y * 5 + cor[n].Z))];

    public static int[,] HeightsFromZ(double[] zValues, int gridX, int gridZ, int baseHeight = 4)
    {
        var H = new int[gridZ + 1, gridX + 1];
        for (int k = 0; k <= gridZ; k++)
            for (int i = 0; i <= gridX; i++)
                H[k, i] = Math.Max(4, (int)Math.Floor(zValues[k * (gridX + 1) + i] * 4 + 0.5) + baseHeight);
        return H;
    }

    public static (List<ExportedCube> Cubes, int Moved, int Fallbacks) ExportCubes(int[,] H, byte material, TerrainGenerator? generator = null, bool pinEdges = false)
    {
        generator ??= new TerrainGenerator();
        H = (int[,])H.Clone();
        int moved = RepairSaddles(H, pinEdges);
        int before = generator.Fallbacks;
        var cubes = new List<ExportedCube>();
        for (int k = 0; k < H.GetLength(0) - 1; k++)
            for (int i = 0; i < H.GetLength(1) - 1; i++)
                foreach (GeneratedCube cube in generator.Cell(H, i, k))
                    cubes.Add(new ExportedCube(cube.I, cube.J, cube.K, [material, material, material, material, material, material], CornerBytes(cube.Corners)));
        return (cubes, moved, generator.Fallbacks - before);
    }
}
