using static Worldgen.KogamaTerrain;

namespace Worldgen;

readonly record struct Layer(int J, Point[] Corners, int[]? Faces);

readonly record struct GeneratedCube(int I, int J, int K, Point[] Corners, int[]? Faces);

sealed record Shape(List<Layer> Layers, int Fallbacks);

sealed class ShapeFallback : Exception
{
}

enum Tie
{
    High,
    Low,
    Both,
}

sealed class TerrainGenerator
{
    readonly Dictionary<(int, int, int, int), Shape> _shapes = [];

    public CapBuilder Builder { get; } = new(floorRoof: true);

    public int Fallbacks { get; private set; }

    public ((int, int, int, int) Local, int Shift, List<Layer> Layers) ShapeLocal(int[] heights)
    {
        int shift = FloorDiv(heights.Min(), 4) - 1;
        var local = (heights[0] - shift * 4, heights[1] - shift * 4, heights[2] - shift * 4, heights[3] - shift * 4);
        if (!_shapes.TryGetValue(local, out Shape? entry))
        {
            entry = GenerateShape([local.Item1, local.Item2, local.Item3, local.Item4]);
            _shapes[local] = entry;
        }
        Fallbacks += entry.Fallbacks;
        return (local, shift, entry.Layers);
    }

    public List<GeneratedCube> Cell(int[,] H, int i, int k)
    {
        (_, int shift, List<Layer> layers) = ShapeLocal([H[k, i], H[k, i + 1], H[k + 1, i + 1], H[k + 1, i]]);
        return [.. layers.Select(layer => new GeneratedCube(i, layer.J + shift, k, layer.Corners, layer.Faces))];
    }

    public List<GeneratedCube> Generate(int[,] H)
    {
        var cubes = new List<GeneratedCube>();
        for (int k = 0; k < H.GetLength(0) - 1; k++)
            for (int i = 0; i < H.GetLength(1) - 1; i++)
                cubes.AddRange(Cell(H, i, k));
        return cubes;
    }

    Shape GenerateShape(int[] h)
    {
        double low = h.Min() / 4.0, high = h.Max() / 4.0;
        int first = (int)Math.Floor(low), last = (int)Math.Ceiling(high);

        Shape Block() =>
            new([.. Enumerable.Range(first, Math.Max(first + 1, last) - first).Select(j => new Layer(j, Full, null))], 1);

        for (int j = (int)Math.Ceiling(low); j < (int)Math.Floor(high) + 1; j++)
        {
            int q = j * 4;
            if ((h[0] < q && h[2] < q && h[1] > q && h[3] > q) || (h[0] > q && h[2] > q && h[1] < q && h[3] < q))
                return Block();
        }
        if (low == high && low == (int)low)
            return new([new Layer((int)low - 1, Full, null)], 0);
        Point[] roof = [new(0, h[0], 0), new(4, h[1], 0), new(4, h[2], 4), new(0, h[3], 4)];

        Point Intersect(Point a, Point b, double level)
        {
            Point p, q;
            if (a.X == b.X && a.X is 0 or 4)
                (p, q) = a.X == 4 ? (roof[1], roof[2]) : (roof[0], roof[3]);
            else if (a.Z == b.Z && a.Z is 0 or 4)
                (p, q) = a.Z == 4 ? (roof[3], roof[2]) : (roof[0], roof[1]);
            else
                throw new ShapeFallback();
            if (q.Y == p.Y)
                throw new ShapeFallback();
            double t = (level - p.Y) / (q.Y - p.Y);
            double vx = JsRound(p.X + t * (q.X - p.X));
            double vz = JsRound(p.Z + t * (q.Z - p.Z));
            if (Eps < t && t < 1 - Eps)
            {
                if (p.X != q.X)
                    vx = Math.Max(p.X + 1, Math.Min(q.X - 1, vx));
                if (p.Z != q.Z)
                    vz = Math.Max(p.Z + 1, Math.Min(q.Z - 1, vz));
            }
            return new Point(vx, level, vz);
        }

        List<Point> Clip(IReadOnlyList<Point> poly, double level, bool lower)
        {
            int above = roof.Count(p => p.Y > level);
            int below = roof.Count(p => p.Y < level);
            Tie tie = above > 0 && below > 0 ? above >= below ? Tie.High : Tie.Low : Tie.Both;
            Func<Point, bool> inside;
            if (lower)
                inside = tie == Tie.Low ? p => p.Y > level : p => p.Y >= level;
            else
                inside = tie == Tie.High ? p => p.Y < level : p => p.Y <= level;
            var output = new List<Point>();
            int n = poly.Count;
            for (int m = 0; m < n; m++)
            {
                Point a = poly[m];
                Point b = poly[(m + 1) % n];
                bool ia = inside(a), ib = inside(b);
                if (ia)
                    output.Add(a);
                if (ia != ib)
                    output.Add(Intersect(a, b, level));
            }
            return [.. output.Where((p, m) => p != output[(m - 1 + output.Count) % output.Count])];
        }

        try
        {
            var slabs = new List<List<Point>>();
            double projected = 0.0;
            bool cliff = false;
            for (int j = first; j < last; j++)
            {
                List<Point> polygon = Clip(Clip(roof, j * 4, true), (j + 1) * 4, false);
                double area = polygon.Count >= 3 ? PolygonArea(polygon) : 0.0;
                if (area < Eps)
                {
                    polygon = [];
                    cliff = cliff || j + 1 < last;
                }
                projected += area;
                slabs.Add(polygon);
            }
            if (Math.Abs(projected - 16) > Eps)
                return Block();
            bool floorRoof = Builder.FloorRoof && !cliff;
            var layers = new List<Layer>();
            int fallbacks = 0;
            for (int j = first; j < last; j++)
            {
                List<Point> polygon = slabs[j - first];
                List<Point> solid = [.. Clip(roof, j * 4, true).Select(p => new Point(p.X, 0, p.Z))];
                List<Point> local;
                if (polygon.Count == 0)
                {
                    if (j + 1 == last)
                        continue;
                    polygon = InscribedQuad(Clip(roof, (j + 1) * 4, true));
                    if (polygon.Count < 3)
                        return Block();
                    local = [.. polygon.Select(p => new Point(p.X, 4, p.Z))];
                }
                else
                    local = [.. polygon.Select(p => new Point(p.X, p.Y - j * 4, p.Z))];
                Cap? cap = Builder.Build(local, floorRoof, solid);
                if (cap is null)
                {
                    fallbacks++;
                    layers.Add(new Layer(j, Full, null));
                }
                else
                    layers.Add(new Layer(j, cap.Corners, cap.Faces));
            }
            return new Shape(layers, fallbacks);
        }
        catch (ShapeFallback)
        {
            return Block();
        }
    }
}
