using System.Numerics;

namespace Worldmap;

readonly record struct Quad(Vector3 A, Vector3 B, Vector3 C, Vector3 D, Vector3 Normal, int Material)
{
    public static readonly Vector3[] Outward =
    [
        Vector3.UnitY, -Vector3.UnitY, -Vector3.UnitZ, Vector3.UnitZ, -Vector3.UnitX, Vector3.UnitX,
    ];

    public static Quad? Make(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward, int material)
    {
        Vector3 normal = Vector3.Cross(c - a, d - b);
        if (normal.LengthSquared() < 1e-12f)
            return null;
        normal = Vector3.Normalize(normal);
        if (Vector3.Dot(normal, outward) < 0)
            normal = -normal;
        return new Quad(a, b, c, d, normal, material);
    }
}

sealed class Picture(int width, int height, byte[] rgba, float[] depth, byte[]? halo)
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    public byte[] Rgba { get; } = rgba;
    public float[] Depth { get; } = depth;
    public byte[]? Halo { get; } = halo;
}

sealed class Renderer(Textures? textures, Vector3 light, float ambient, float diffuse)
{
    const float Seam = 1e-3f;
    static readonly Vector2[] Corners = [new(0, 0), new(1, 0), new(1, 1), new(0, 1)];

    public Picture Render(IEnumerable<Quad> quads, int width, int height, int supersample, Vector3 towardViewer, Func<Vector3, Vector3> project, int outline = 0)
    {
        int bigWidth = width * supersample, bigHeight = height * supersample;
        float[] depth = new float[bigWidth * bigHeight];
        Array.Fill(depth, float.NegativeInfinity);
        uint[] color = new uint[bigWidth * bigHeight];
        foreach (Quad quad in quads)
        {
            if (Vector3.Dot(quad.Normal, towardViewer) <= 0)
                continue;
            float light1 = ambient + diffuse * Math.Max(0, Vector3.Dot(quad.Normal, light));
            Vector3[] points = [project(quad.A), project(quad.B), project(quad.C), project(quad.D)];
            for (int i = 0; i < 4; i++)
                points[i] = new Vector3(points[i].X * supersample, points[i].Y * supersample, points[i].Z);
            Triangle(points[0], points[1], points[2], Corners[0], Corners[1], Corners[2], quad.Material, light1, bigWidth, bigHeight, depth, color);
            Triangle(points[0], points[2], points[3], Corners[0], Corners[2], Corners[3], quad.Material, light1, bigWidth, bigHeight, depth, color);
        }

        bool[] ring = new bool[bigWidth * bigHeight];
        if (outline > 0)
        {
            int radius = outline * supersample;
            bool Covered(int x, int y) => x >= 0 && y >= 0 && x < bigWidth && y < bigHeight && !float.IsNegativeInfinity(depth[y * bigWidth + x]);
            for (int y = 0; y < bigHeight; y++)
                for (int x = 0; x < bigWidth; x++)
                {
                    if (!Covered(x, y) || Covered(x - 1, y) && Covered(x + 1, y) && Covered(x, y - 1) && Covered(x, y + 1))
                        continue;
                    for (int dy = -radius; dy <= radius; dy++)
                        for (int dx = -radius; dx <= radius; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (dx * dx + dy * dy <= radius * radius && nx >= 0 && ny >= 0 && nx < bigWidth && ny < bigHeight && !Covered(nx, ny))
                                ring[ny * bigWidth + nx] = true;
                        }
                }
        }

        byte[] rgba = new byte[width * height * 4];
        byte[]? halo = outline > 0 ? new byte[width * height] : null;
        float[] heights = new float[width * height];
        int samples = supersample * supersample;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int r = 0, g = 0, b = 0, hits = 0, ringed = 0;
                float sum = 0;
                for (int sy = 0; sy < supersample; sy++)
                    for (int sx = 0; sx < supersample; sx++)
                    {
                        int i = (y * supersample + sy) * bigWidth + x * supersample + sx;
                        if (float.IsNegativeInfinity(depth[i]))
                        {
                            if (ring[i])
                                ringed++;
                            continue;
                        }
                        uint value = color[i];
                        r += (int)(value >> 16 & 0xFF);
                        g += (int)(value >> 8 & 0xFF);
                        b += (int)(value & 0xFF);
                        sum += depth[i];
                        hits++;
                    }
                int p = y * width + x;
                heights[p] = hits > 0 ? sum / hits : float.NegativeInfinity;
                if (halo is not null)
                    halo[p] = (byte)(255 * (hits + ringed) / samples);
                if (hits == 0)
                    continue;
                rgba[p * 4] = (byte)(r / hits);
                rgba[p * 4 + 1] = (byte)(g / hits);
                rgba[p * 4 + 2] = (byte)(b / hits);
                rgba[p * 4 + 3] = (byte)(255 * hits / samples);
            }
        return new Picture(width, height, rgba, heights, halo);
    }

    void Triangle(Vector3 p0, Vector3 p1, Vector3 p2, Vector2 t0, Vector2 t1, Vector2 t2, int material, float light1,
        int width, int height, float[] depth, uint[] color)
    {
        float area = (p1.X - p0.X) * (p2.Y - p0.Y) - (p2.X - p0.X) * (p1.Y - p0.Y);
        if (MathF.Abs(area) < 1e-9f)
            return;
        int x0 = Math.Max(0, (int)MathF.Floor(MathF.Min(p0.X, MathF.Min(p1.X, p2.X))));
        int x1 = Math.Min(width - 1, (int)MathF.Ceiling(MathF.Max(p0.X, MathF.Max(p1.X, p2.X))));
        int y0 = Math.Max(0, (int)MathF.Floor(MathF.Min(p0.Y, MathF.Min(p1.Y, p2.Y))));
        int y1 = Math.Min(height - 1, (int)MathF.Ceiling(MathF.Max(p0.Y, MathF.Max(p1.Y, p2.Y))));
        uint flat = MapPainter.Base(material);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float px = x + 0.5f, py = y + 0.5f;
                float w0 = ((p1.X - px) * (p2.Y - py) - (p2.X - px) * (p1.Y - py)) / area;
                float w1 = ((p2.X - px) * (p0.Y - py) - (p0.X - px) * (p2.Y - py)) / area;
                float w2 = 1 - w0 - w1;
                if (w0 < -Seam || w1 < -Seam || w2 < -Seam)
                    continue;
                int i = y * width + x;
                float z = w0 * p0.Z + w1 * p1.Z + w2 * p2.Z;
                if (z <= depth[i])
                    continue;
                depth[i] = z;
                Vector2 uv = t0 * w0 + t1 * w1 + t2 * w2;
                color[i] = Shade(textures?.Texel(material, uv.X, uv.Y) ?? flat, light1);
            }
    }

    public static uint Shade(uint color, float light) =>
        (uint)Math.Clamp((int)((color >> 16 & 0xFF) * light), 0, 255) << 16
        | (uint)Math.Clamp((int)((color >> 8 & 0xFF) * light), 0, 255) << 8
        | (uint)Math.Clamp((int)((color & 0xFF) * light), 0, 255);
}
