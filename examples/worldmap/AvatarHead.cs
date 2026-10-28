using System.Buffers.Binary;
using System.Numerics;
using OpenKogama.Api;

namespace Worldmap;

sealed record HeadImages(byte[] Head, byte[] Ring);

static class AvatarHead
{
    const int Size = 96;
    const int Supersample = 3;
    const int Outline = 4;
    const float Margin = 0.08f;
    static readonly Vector3 Light = Vector3.Normalize(new(-0.35f, 0.45f, 0.82f));

    static readonly int[][] Faces =
    [
        [0, 1, 2, 3],
        [4, 5, 6, 7],
        [7, 6, 1, 0],
        [5, 4, 3, 2],
        [4, 7, 0, 3],
        [6, 5, 2, 1],
    ];

    readonly record struct Block(int X, int Y, int Z, byte[] Corners, byte[] Materials);

    public static string? Key(AvatarSkin skin) => skin.Parts.FirstOrDefault(part => part.Bone == "Head")?.Cubes;

    public static HeadImages? Render(AvatarSkin skin, Textures? textures)
    {
        if (Key(skin) is not string cubes)
            return null;
        List<Quad> quads = [];
        foreach (Block block in Read(Convert.FromBase64String(cubes)))
        {
            Vector3[] corners = new Vector3[8];
            for (int i = 0; i < 8; i++)
            {
                byte corner = block.Corners[i];
                corners[i] = new Vector3(-(block.Z + corner % 5 / 4f), block.Y + corner / 5 % 5 / 4f, -(block.X + corner / 25 / 4f));
            }
            for (int face = 0; face < 6; face++)
            {
                Vector3 outward = Quad.Outward[face];
                if (Quad.Make(corners[Faces[face][0]], corners[Faces[face][1]], corners[Faces[face][2]], corners[Faces[face][3]],
                        new Vector3(-outward.Z, outward.Y, -outward.X), block.Materials[face]) is Quad quad)
                    quads.Add(quad);
            }
        }
        if (quads.Count == 0)
            return null;

        float minX = quads.Min(quad => MathF.Min(MathF.Min(quad.A.X, quad.B.X), MathF.Min(quad.C.X, quad.D.X)));
        float maxX = quads.Max(quad => MathF.Max(MathF.Max(quad.A.X, quad.B.X), MathF.Max(quad.C.X, quad.D.X)));
        float minY = quads.Min(quad => MathF.Min(MathF.Min(quad.A.Y, quad.B.Y), MathF.Min(quad.C.Y, quad.D.Y)));
        float maxY = quads.Max(quad => MathF.Max(MathF.Max(quad.A.Y, quad.B.Y), MathF.Max(quad.C.Y, quad.D.Y)));
        float scale = Size * (1 - 2 * Margin) / Math.Max(maxX - minX, maxY - minY);
        Vector2 offset = new(Size / 2f - (minX + maxX) / 2 * scale, Size / 2f + (minY + maxY) / 2 * scale);

        var renderer = new Renderer(textures, Light, 0.65f, 0.5f);
        Picture picture = renderer.Render(quads, Size, Size, Supersample, Vector3.UnitZ,
            point => new Vector3(offset.X + point.X * scale, offset.Y - point.Y * scale, point.Z), Outline);
        byte[] ring = new byte[Size * Size * 4];
        for (int i = 0; i < Size * Size; i++)
        {
            ring[i * 4] = ring[i * 4 + 1] = ring[i * 4 + 2] = 255;
            ring[i * 4 + 3] = picture.Halo![i];
        }
        return new HeadImages(MapPainter.Png(Size, Size, Bleed(picture.Rgba)), MapPainter.Png(Size, Size, ring));
    }

    static byte[] Bleed(byte[] rgba)
    {
        byte[] result = (byte[])rgba.Clone();
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                int i = (y * Size + x) * 4;
                if (rgba[i + 3] != 0)
                    continue;
                int r = 0, g = 0, b = 0, count = 0;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= Size || ny >= Size || rgba[(ny * Size + nx) * 4 + 3] == 0)
                            continue;
                        int n = (ny * Size + nx) * 4;
                        r += rgba[n];
                        g += rgba[n + 1];
                        b += rgba[n + 2];
                        count++;
                    }
                if (count == 0)
                    continue;
                result[i] = (byte)(r / count);
                result[i + 1] = (byte)(g / count);
                result[i + 2] = (byte)(b / count);
            }
        return result;
    }

    static List<Block> Read(byte[] data)
    {
        List<Block> blocks = [];
        int count = BinaryPrimitives.ReadInt32BigEndian(data), offset = 4;
        for (int entry = 0; entry < count; entry++)
        {
            int x = BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(offset));
            int y = BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(offset + 2));
            int z = BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(offset + 4));
            byte flags = data[offset + 6];
            offset += 7;
            byte[] corners = (flags & 1) != 0 ? Cube.IdentityByteCorners.ToArray() : data.AsSpan(offset, 8).ToArray();
            if ((flags & 1) == 0)
                offset += 8;
            byte[] materials = (flags & 2) != 0 ? [.. Enumerable.Repeat(data[offset], 6)] : data.AsSpan(offset, 6).ToArray();
            offset += (flags & 2) != 0 ? 1 : 6;
            for (int run = 0; run < Math.Max(flags >> 2, 1); run++)
                blocks.Add(new Block(x + run, y, z, corners, materials));
        }
        return blocks;
    }
}
