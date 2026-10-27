using System.Globalization;
using static Worldgen.KogamaTerrain;

namespace Worldgen;

sealed record Layout(int[] Faces, int[] Ring, int[] Bottom, int? Centre);

sealed record Cap(Point[] Corners, int[] Faces);

sealed class CapBuilder
{
    const int CacheSize = 8192;

    readonly Dictionary<string, Cap?>[] _cache = [[], []];
    readonly List<Layout> _twoFace = [];
    readonly List<Layout> _sideSide = [];
    readonly List<Layout> _threeFace = [];

    public CapBuilder(bool floorRoof = true)
    {
        FloorRoof = floorRoof;
        for (int side = 0; side < 4; side++)
        {
            int[] ring = RingOf([5, side]);
            _twoFace.Add(new Layout([5, side], ring, [.. All.Where(n => !ring.Contains(n))], null));
        }
        foreach (int s in (int[])[0, 1])
        {
            foreach (int t in (int[])[2, 3])
            {
                int[] pair = RingOf([s, t]);
                _sideSide.Add(new Layout([s, t], pair, [.. All.Where(n => !pair.Contains(n))], null));
                int[] ring = RingOf([5, s, t]);
                int centre = KFaces[5].First(n => KFaces[s].Contains(n) && KFaces[t].Contains(n));
                _threeFace.Add(new Layout([5, s, t], ring, [.. All.Where(n => !ring.Contains(n) && n != centre)], centre));
            }
        }
    }

    public bool FloorRoof { get; }

    static bool Valid(Point[] cor, int[] faces, double area, bool floorRoof, IReadOnlyList<Point>? solid) =>
        CubeLegal(cor) && KogamaValid(cor, faces, area, floorRoof, solid);

    public Cap? Build(IReadOnlyList<Point> poly, bool? floorRoof = null, IReadOnlyList<Point>? solid = null)
    {
        bool roof = floorRoof ?? FloorRoof;
        Dictionary<string, Cap?> cache = _cache[roof ? 1 : 0];
        string key = Key(poly) + "|" + (solid is null ? "none" : Key(solid));
        if (!cache.TryGetValue(key, out Cap? cap))
        {
            if (cache.Count >= CacheSize)
                cache.Clear();
            cap = cache[key] = Solve([.. poly], roof, solid is null ? null : [.. solid]);
        }
        return cap;
    }

    static string Key(IEnumerable<Point> points) =>
        string.Join(";", points.Select(p => string.Create(CultureInfo.InvariantCulture, $"{p.X},{p.Y},{p.Z}")));

    static bool Lattice(double v) => v >= 0 && v <= 4 && v == Math.Floor(v);

    static Point[] Place(int[] ring, Point[] q, int rotation)
    {
        var cor = new Point[8];
        for (int i = 0; i < ring.Length; i++)
            cor[ring[i]] = q[(i + rotation) % q.Length];
        return cor;
    }

    static (Point[] Cor, double Misfit) Settle(int[] ring, Point[] q, Action<Point[]> finish)
    {
        Point[] targets = [.. ring.Select(n => Nominals[n])];
        int count = q.Length;
        (int Rotation, double Misfit)? best = null;
        for (int rotation = 0; rotation < count; rotation++)
        {
            double m = 0;
            for (int i = 0; i < targets.Length; i++)
            {
                Point p = q[(i + rotation) % count];
                Point t = targets[i];
                m += (p.X - t.X) * (p.X - t.X) + (p.Z - t.Z) * (p.Z - t.Z) + (t.Y == 0 ? 4 * p.Y * p.Y : 0);
            }
            if (best is null || m < best.Value.Misfit)
                best = (rotation, m);
        }
        Point[] cor = Place(ring, q, best!.Value.Rotation);
        finish(cor);
        return (cor, best.Value.Misfit);
    }

    static void WallsFinish(Point[] cor)
    {
        for (int i = 0; i < 4; i++)
        {
            Point t = cor[Top[i]];
            cor[Under[i]] = new Point(t.X, 0, t.Z);
        }
    }

    static void WedgeFinish(Point[] cor, int s, int a, bool border)
    {
        double[] values = [.. KFaces[s].Select(n => cor[n][a])];
        double plane = border
            ? (s & 1) != 0 ? 0 : 4
            : (s & 1) != 0 ? values.Min() : values.Max();
        foreach (int n in KFaces[s])
            cor[n ^ (1 << a)] = cor[n].With(a, plane);
    }

    static void CapFinish(Point[] cor, int[] bottom)
    {
        foreach (int n in bottom)
        {
            Point top = cor[n | 2];
            cor[n] = new Point(top.X, 0, top.Z);
        }
    }

    static int Compare((double Cost, double Misfit, int Order) a, (double Cost, double Misfit, int Order) b) =>
        a.CompareTo(b);

    Cap? Solve(List<Point> poly, bool floorRoof, List<Point>? solid)
    {
        if (poly.Count < 3 || poly.Count > 6)
            return null;
        if (!poly.All(p => Lattice(p.X) && Lattice(p.Y) && Lattice(p.Z)))
            return null;
        double signed = 0;
        for (int i = 0; i < poly.Count; i++)
        {
            Point a = poly[i];
            Point b = poly[(i + 1) % poly.Count];
            signed += a.X * b.Z - b.X * a.Z;
        }
        if (Math.Abs(signed) < Eps)
            return null;
        if (signed < 0)
            poly.Reverse();
        if (solid is not null)
        {
            double turn = 0;
            for (int i = 0; i < solid.Count; i++)
            {
                Point a = solid[i];
                Point b = solid[(i + 1) % solid.Count];
                turn += a.X * b.Z - b.X * a.Z;
            }
            if (turn < 0)
                solid.Reverse();
        }

        bool Inside(Point p) => poly.Select((a, i) => Cross(a, poly[(i + 1) % poly.Count], p)).All(c => c >= -Eps);

        if (!poly.All(Inside))
            return null;
        double area = Math.Abs(signed) / 2;
        var floors = new List<Point>();
        var centroid = new Point(JsRound(poly.Sum(p => p.X) / poly.Count), 0, JsRound(poly.Sum(p => p.Z) / poly.Count));
        if (Inside(centroid))
            floors.Add(centroid);
        for (int x = 0; x < 5; x++)
        {
            for (int z = 0; z < 5; z++)
            {
                var p = new Point(x, 0, z);
                if (Inside(p) && !floors.Any(q => q.X == x && q.Z == z))
                    floors.Add(p);
            }
        }

        var options = new List<(double Cost, double Misfit, int Order, Point[] Cor, int[] Faces)>();
        if (poly.Count <= 4)
        {
            foreach (Point[] quad in Variants(poly, 4))
            {
                (Point[] cor, double m) = Settle(Top, quad, WallsFinish);
                options.Add((RoofCost(cor, [5]), m, options.Count, cor, [5]));
                for (int s = 0; s < 4; s++)
                {
                    int a = Axis[s];
                    foreach (bool border in (bool[])[true, false])
                    {
                        (cor, m) = Settle(KFaces[s], quad, c => WedgeFinish(c, s, a, border));
                        options.Add((RoofCost(cor, [s]), m, options.Count, cor, [s]));
                    }
                }
            }
        }
        List<Point[]> sixes = Variants(poly, 6);
        foreach (Point[] q in sixes)
        {
            foreach (Layout layout in _twoFace)
            {
                (Point[] cor, double m) = Settle(layout.Ring, q, c => CapFinish(c, layout.Bottom));
                options.Add((RoofCost(cor, layout.Faces), m, options.Count, cor, layout.Faces));
            }
        }
        options.Sort((a, b) => Compare((a.Cost, a.Misfit, a.Order), (b.Cost, b.Misfit, b.Order)));
        foreach (var option in options)
            if (Valid(option.Cor, option.Faces, area, floorRoof, solid))
                return new Cap(option.Cor, option.Faces);

        var wild = new List<(double Cost, double Misfit, int Order, Point[] Cor, int[] Faces, int[] Bottom)>();
        foreach (Point[] q in sixes)
        {
            foreach (Layout layout in _twoFace.Concat(_sideSide))
            {
                for (int rotation = 0; rotation < 6; rotation++)
                {
                    Point[] cor = Place(layout.Ring, q, rotation);
                    wild.Add((RoofCost(cor, layout.Faces), Misfit(cor, layout.Ring), wild.Count, cor, layout.Faces, layout.Bottom));
                }
            }
        }
        List<Point> anchors = [.. floors, .. floors.Select(f => new Point(f.X, 4, f.Z))];
        wild.Sort((a, b) => Compare((a.Cost, a.Misfit, a.Order), (b.Cost, b.Misfit, b.Order)));
        foreach (var option in wild)
        {
            foreach (Point anchor in anchors)
            {
                Point[] cor = [.. option.Cor];
                foreach (int n in option.Bottom)
                    cor[n] = anchor;
                if (Valid(cor, option.Faces, area, floorRoof, solid))
                    return new Cap(cor, option.Faces);
            }
        }

        foreach (Point[] q in sixes)
        {
            foreach (Layout layout in _twoFace)
            {
                for (int rotation = 0; rotation < 6; rotation++)
                {
                    for (int a = 0; a < floors.Count; a++)
                    {
                        for (int b = 0; b < floors.Count; b++)
                        {
                            if (a == b)
                                continue;
                            Point[] cor = Place(layout.Ring, q, rotation);
                            cor[layout.Bottom[0]] = floors[a];
                            cor[layout.Bottom[1]] = floors[b];
                            if (Valid(cor, layout.Faces, area, floorRoof, solid))
                                return new Cap(cor, layout.Faces);
                        }
                    }
                }
            }
        }

        double meanY = JsRound(poly.Sum(p => p.Y) / poly.Count);
        var centres = new List<Point>();
        foreach (int dy in (int[])[0, 1, -1, 2, -2, 3, -3, 4, -4])
        {
            double y = meanY + dy;
            if (0 <= y && y <= 4)
                foreach (Point f in floors)
                    centres.Add(new Point(f.X, y, f.Z));
        }
        foreach (Point[] q in sixes)
        {
            foreach (Point centrePoint in centres)
            {
                foreach (Layout layout in _threeFace)
                {
                    for (int rotation = 0; rotation < 6; rotation++)
                    {
                        foreach (Point f in floors)
                        {
                            Point[] cor = Place(layout.Ring, q, rotation);
                            cor[layout.Centre!.Value] = centrePoint;
                            cor[layout.Bottom[0]] = f;
                            if (Valid(cor, layout.Faces, area, floorRoof, solid))
                                return new Cap(cor, layout.Faces);
                        }
                    }
                }
            }
        }
        return null;
    }
}
