using System.Buffers.Binary;
using System.IO.Compression;
using System.Numerics;
using System.Text;
using OpenKogama.Api;

namespace Worldmap;

sealed record Scene(List<Quad> Quads, uint Background, float? WaterLevel, uint Water);

sealed record MapTile(int Col, int Row, byte[] Png);

sealed record MapImage(byte[] Png, float MinX, float MinZ, float Unit, int Width, int Height, uint Background,
    int Cols, int Rows, float TileWorld, float DetailTop, IReadOnlyList<MapTile> Tiles);

static class MapPainter
{
    const int OverviewSide = 1024;
    const int MaxBytes = 1_000_000;
    const int TilePixels = 256;
    const int MaxTiles = 48;
    const int Supersample = 2;
    const float MaxPixelsPerUnit = 16;
    const float Sharper = 2;
    const uint Unknown = 0x808080;
    const uint DefaultWater = 0x1A3380;
    const uint DefaultSky = 0x5FB4FE;
    const float UnderWater = 0.7f;
    const float DeepestWater = 0.92f;
    const float DeepWater = 40f;
    const float Ambient = 0.6f;
    const float Diffuse = 0.5f;
    static readonly Vector3 Sun = Vector3.Normalize(new(-0.4f, 0.85f, 0.35f));

    static readonly int[][] Faces =
    [
        [0, 1, 2, 3],
        [4, 5, 6, 7],
        [7, 6, 1, 0],
        [5, 4, 3, 2],
        [4, 7, 0, 3],
        [6, 5, 2, 1],
    ];

    static readonly CubeFace[] FaceMaterials = [CubeFace.Top, CubeFace.Bottom, CubeFace.Front, CubeFace.Back, CubeFace.Left, CubeFace.Right];

    static readonly uint[] Colors =
    [
        0xB3342A, 0x86251E, 0x5A0F08, 0xD9A86F, 0xA289AF, 0x3F95E2, 0x295797, 0x002C64,
        0xA04F04, 0x735E89, 0x6D8315, 0x4D5B08, 0x283802, 0x652A05, 0x42345A, 0xDA7C0C,
        0xC05E00, 0x9F3D00, 0xFDCE65, 0xB69043, 0xA8A8A5, 0x838283, 0x585757, 0x222222,
        0x746442, 0x7CC5ED, 0x332019, 0x60211A, 0xABDF00, 0x838386, 0x784325, 0x705129,
        0x4A565B, 0x737171, 0x877A48, 0x31544A, 0x6C551A, 0x631817, 0xAA7D15, 0x6B6C6B,
        0x4D4743, 0x534D49, 0xE79898, 0x2C3953, 0xFC35A0, 0x400A06, 0x212B1D, 0x123A54,
        0x615757, 0x7AA769, 0x757576, 0x43731D, 0x830425, 0x311C02, 0x944304, 0xCDE7EC,
        0x4F5818, 0xAF8A41, 0x7F7D7E, 0xADDCF3, 0xE802CB,
    ];

    static readonly uint[] Crcs = [.. Enumerable.Range(0, 256).Select(Crc)];

    public static Scene Read(IWorld world, float? top, bool models, IReadOnlySet<int> hidden)
    {
        IWorldObject? water = world.Objects.FirstOrDefault(obj => obj.Type is "WaterPlane" or "WaterPlanePreset");
        IWorldObject? sky = world.Objects.FirstOrDefault(obj => obj.Type == "Skybox");
        uint waterColor = Rgb(water?.Data.GetValueOrDefault("waterColor")) ?? DefaultWater;
        uint skyColor = Rgb(sky?.Data.GetValueOrDefault("color")) ?? DefaultSky;
        return new Scene(Quads(world, top ?? float.PositiveInfinity, models, hidden), water is null ? skyColor : waterColor, water?.Position.Y, waterColor);
    }

    static uint? Rgb(object? value)
    {
        if (value is not System.Collections.IEnumerable list || value is string)
            return null;
        float[] parts = [.. list.Cast<object>().Take(3).Select(part => Convert.ToSingle(part, System.Globalization.CultureInfo.InvariantCulture))];
        if (parts.Length < 3)
            return null;
        return parts.Aggregate(0u, (rgb, part) => rgb << 8 | (uint)Math.Clamp((int)MathF.Round(part * 255), 0, 255));
    }

    static List<Quad> Quads(IWorld world, float top, bool models, IReadOnlySet<int> hidden)
    {
        List<Quad> quads = [];
        Dictionary<int, int> parents = [];
        if (hidden.Count > 0)
            foreach (IWorldObject obj in world.Objects)
                parents[obj.Id] = obj.ParentId;
        bool Hidden(int id)
        {
            for (int depth = 0; depth < 64; depth++)
            {
                if (hidden.Contains(id))
                    return true;
                if (!parents.TryGetValue(id, out id))
                    return false;
            }
            return false;
        }
        foreach (IWorldObject obj in world.Objects)
        {
            bool terrain = obj.Type is "CubeModelPrototypeTerrain" or "CubeModelTerrainFineGrained";
            if (!terrain && (!models || obj.Type != "CubeModel" || Hidden(obj.Id)) || world.Model(obj.Id) is not ICubeModel model)
                continue;
            Vector3 step = new Vector3(model.CubeSize) * (terrain ? Vector3.One : obj.Scale);
            Quaternion turn = terrain ? Quaternion.Identity : obj.Rotation;
            bool upright = Vector3.Transform(Vector3.UnitY, turn).Y > 0.999f;
            bool Below(int x, int y, int z) => float.IsPositiveInfinity(top) || (obj.Position + Vector3.Transform(new Vector3(x, y, z) * step, turn)).Y <= top;
            HashSet<(int, int, int)> filled = [.. model.Positions()];
            Vector3[] corners = new Vector3[8];
            foreach ((int x, int y, int z) in model.Positions())
            {
                if (model.Get(x, y, z) is not Cube cube || !Below(x, y, z))
                    continue;
                bool identity = cube.ByteCorners.SequenceEqual(Cube.IdentityByteCorners);
                if (identity && upright && filled.Contains((x, y + 1, z)) && Below(x, y + 1, z) && model.Get(x, y + 1, z) is Cube above
                    && above.ByteCorners.SequenceEqual(Cube.IdentityByteCorners))
                    continue;
                for (int i = 0; i < 8; i++)
                {
                    byte corner = cube.ByteCorners[i];
                    Vector3 local = new(x - 0.5f + corner / 25 / 4f, y - 0.5f + corner / 5 % 5 / 4f, z - 0.5f + corner % 5 / 4f);
                    corners[i] = obj.Position + Vector3.Transform(local * step, turn);
                }
                for (int face = 0; face < 6; face++)
                {
                    if (identity && upright && face != 0)
                        continue;
                    int[] at = Faces[face];
                    if (Quad.Make(corners[at[0]], corners[at[1]], corners[at[2]], corners[at[3]], Vector3.Transform(Quad.Outward[face], turn),
                            (int)cube.FaceMaterial(FaceMaterials[face])) is Quad quad
                        && quad.Normal.Y > 0.01f)
                        quads.Add(quad);
                }
            }
        }
        return quads;
    }

    public static MapImage? Paint(Scene scene, Textures? textures)
    {
        List<Quad> quads = scene.Quads;
        if (quads.Count == 0)
            return null;
        float minX = quads.Min(quad => MathF.Min(MathF.Min(quad.A.X, quad.B.X), MathF.Min(quad.C.X, quad.D.X)));
        float maxX = quads.Max(quad => MathF.Max(MathF.Max(quad.A.X, quad.B.X), MathF.Max(quad.C.X, quad.D.X)));
        float minZ = quads.Min(quad => MathF.Min(MathF.Min(quad.A.Z, quad.B.Z), MathF.Min(quad.C.Z, quad.D.Z)));
        float maxZ = quads.Max(quad => MathF.Max(MathF.Max(quad.A.Z, quad.B.Z), MathF.Max(quad.C.Z, quad.D.Z)));
        float spanX = Math.Max(maxX - minX, 0.01f), spanZ = Math.Max(maxZ - minZ, 0.01f);
        float low = quads.Min(quad => MathF.Min(MathF.Min(quad.A.Y, quad.B.Y), MathF.Min(quad.C.Y, quad.D.Y)));
        float high = quads.Max(quad => MathF.Max(MathF.Max(quad.A.Y, quad.B.Y), MathF.Max(quad.C.Y, quad.D.Y)));

        var flat = new Renderer(null, Sun, Ambient, Diffuse);
        float overviewScale = Math.Min(MaxPixelsPerUnit, OverviewSide / Math.Max(spanX, spanZ));
        int width, height;
        byte[] png;
        while (true)
        {
            width = Math.Max(1, (int)MathF.Ceiling(spanX * overviewScale));
            height = Math.Max(1, (int)MathF.Ceiling(spanZ * overviewScale));
            float top = minZ + height / overviewScale, scale = overviewScale;
            Picture picture = flat.Render(quads, width, height, Supersample, Vector3.UnitY,
                point => new Vector3((point.X - minX) * scale, (top - point.Z) * scale, point.Y));
            png = Png(width, height, Finish(picture, scene, low, high));
            if (png.Length <= MaxBytes)
                break;
            overviewScale /= 1.3f;
        }

        float detailScale = Math.Min(MaxPixelsPerUnit, TilePixels * MathF.Sqrt(MaxTiles / (spanX * spanZ)));
        while (detailScale > 0 && Tiles(spanX, detailScale) * Tiles(spanZ, detailScale) > MaxTiles)
            detailScale *= 0.95f;
        if (detailScale < overviewScale * Sharper)
            return new MapImage(png, minX, minZ, 1 / overviewScale, width, height, scene.Background, 0, 0, 0, 0, []);

        int cols = Tiles(spanX, detailScale), rows = Tiles(spanZ, detailScale);
        List<Quad>[] buckets = [.. Enumerable.Range(0, cols * rows).Select(_ => new List<Quad>())];
        foreach (Quad quad in quads)
        {
            float left = MathF.Min(MathF.Min(quad.A.X, quad.B.X), MathF.Min(quad.C.X, quad.D.X));
            float right = MathF.Max(MathF.Max(quad.A.X, quad.B.X), MathF.Max(quad.C.X, quad.D.X));
            float south = MathF.Min(MathF.Min(quad.A.Z, quad.B.Z), MathF.Min(quad.C.Z, quad.D.Z));
            float north = MathF.Max(MathF.Max(quad.A.Z, quad.B.Z), MathF.Max(quad.C.Z, quad.D.Z));
            int col0 = Math.Clamp((int)((left - minX) * detailScale / TilePixels), 0, cols - 1);
            int col1 = Math.Clamp((int)((right - minX) * detailScale / TilePixels), 0, cols - 1);
            int row0 = Math.Clamp((int)((maxZ - north) * detailScale / TilePixels), 0, rows - 1);
            int row1 = Math.Clamp((int)((maxZ - south) * detailScale / TilePixels), 0, rows - 1);
            for (int row = row0; row <= row1; row++)
                for (int col = col0; col <= col1; col++)
                    buckets[row * cols + col].Add(quad);
        }

        var textured = new Renderer(textures, Sun, Ambient, Diffuse);
        List<MapTile> tiles = [];
        for (int row = 0; row < rows; row++)
            for (int col = 0; col < cols; col++)
            {
                List<Quad> bucket = buckets[row * cols + col];
                if (bucket.Count == 0)
                    continue;
                float x0 = col * TilePixels, y0 = row * TilePixels, scale = detailScale;
                Picture picture = textured.Render(bucket, TilePixels, TilePixels, Supersample, Vector3.UnitY,
                    point => new Vector3((point.X - minX) * scale - x0, (maxZ - point.Z) * scale - y0, point.Y));
                tiles.Add(new MapTile(col, row, Png(TilePixels, TilePixels, Finish(picture, scene, low, high))));
            }
        return new MapImage(png, minX, minZ, 1 / overviewScale, width, height, scene.Background,
            cols, rows, TilePixels / detailScale, maxZ, tiles);
    }

    static int Tiles(float span, float scale) => Math.Max(1, (int)MathF.Ceiling(span * scale / TilePixels));

    public static float OverviewScale(Scene scene)
    {
        List<Quad> quads = scene.Quads;
        float spanX = quads.Max(quad => MathF.Max(MathF.Max(quad.A.X, quad.B.X), MathF.Max(quad.C.X, quad.D.X)))
            - quads.Min(quad => MathF.Min(MathF.Min(quad.A.X, quad.B.X), MathF.Min(quad.C.X, quad.D.X)));
        float spanZ = quads.Max(quad => MathF.Max(MathF.Max(quad.A.Z, quad.B.Z), MathF.Max(quad.C.Z, quad.D.Z)))
            - quads.Min(quad => MathF.Min(MathF.Min(quad.A.Z, quad.B.Z), MathF.Min(quad.C.Z, quad.D.Z)));
        return Math.Min(MaxPixelsPerUnit, OverviewSide / Math.Max(Math.Max(spanX, spanZ), 0.01f));
    }

    public static (byte[] Png, int Width, int Height) Poster(Scene scene, Textures? textures, float scale)
    {
        const int Piece = 512;
        List<Quad> quads = scene.Quads;
        float minX = quads.Min(quad => MathF.Min(MathF.Min(quad.A.X, quad.B.X), MathF.Min(quad.C.X, quad.D.X)));
        float maxX = quads.Max(quad => MathF.Max(MathF.Max(quad.A.X, quad.B.X), MathF.Max(quad.C.X, quad.D.X)));
        float minZ = quads.Min(quad => MathF.Min(MathF.Min(quad.A.Z, quad.B.Z), MathF.Min(quad.C.Z, quad.D.Z)));
        float maxZ = quads.Max(quad => MathF.Max(MathF.Max(quad.A.Z, quad.B.Z), MathF.Max(quad.C.Z, quad.D.Z)));
        float low = quads.Min(quad => MathF.Min(MathF.Min(quad.A.Y, quad.B.Y), MathF.Min(quad.C.Y, quad.D.Y)));
        float high = quads.Max(quad => MathF.Max(MathF.Max(quad.A.Y, quad.B.Y), MathF.Max(quad.C.Y, quad.D.Y)));
        int width = Math.Max(1, (int)MathF.Ceiling((maxX - minX) * scale)), height = Math.Max(1, (int)MathF.Ceiling((maxZ - minZ) * scale));
        int cols = (width + Piece - 1) / Piece, rows = (height + Piece - 1) / Piece;
        List<Quad>[] buckets = [.. Enumerable.Range(0, cols * rows).Select(_ => new List<Quad>())];
        foreach (Quad quad in quads)
        {
            int col0 = Math.Clamp((int)((MathF.Min(MathF.Min(quad.A.X, quad.B.X), MathF.Min(quad.C.X, quad.D.X)) - minX) * scale / Piece), 0, cols - 1);
            int col1 = Math.Clamp((int)((MathF.Max(MathF.Max(quad.A.X, quad.B.X), MathF.Max(quad.C.X, quad.D.X)) - minX) * scale / Piece), 0, cols - 1);
            int row0 = Math.Clamp((int)((maxZ - MathF.Max(MathF.Max(quad.A.Z, quad.B.Z), MathF.Max(quad.C.Z, quad.D.Z))) * scale / Piece), 0, rows - 1);
            int row1 = Math.Clamp((int)((maxZ - MathF.Min(MathF.Min(quad.A.Z, quad.B.Z), MathF.Min(quad.C.Z, quad.D.Z))) * scale / Piece), 0, rows - 1);
            for (int row = row0; row <= row1; row++)
                for (int col = col0; col <= col1; col++)
                    buckets[row * cols + col].Add(quad);
        }
        var renderer = new Renderer(textures, Sun, Ambient, Diffuse);
        byte[] rgba = new byte[width * height * 4];
        for (int row = 0; row < rows; row++)
            for (int col = 0; col < cols; col++)
            {
                float x0 = col * Piece, y0 = row * Piece;
                Picture picture = renderer.Render(buckets[row * cols + col], Piece, Piece, Supersample, Vector3.UnitY,
                    point => new Vector3((point.X - minX) * scale - x0, (maxZ - point.Z) * scale - y0, point.Y));
                byte[] piece = Finish(picture, scene, low, high);
                for (int y = 0; y < Piece && y0 + y < height; y++)
                    Array.Copy(piece, y * Piece * 4, rgba, ((int)y0 + y) * width * 4 + (int)x0 * 4, Math.Min(Piece, width - (int)x0) * 4);
            }
        return (Png(width, height, rgba), width, height);
    }

    static byte[] Finish(Picture picture, Scene scene, float low, float high)
    {
        byte[] rgba = picture.Rgba;
        for (int i = 0; i < picture.Width * picture.Height; i++)
        {
            float alpha = rgba[i * 4 + 3] / 255f;
            uint color = (uint)(rgba[i * 4] << 16 | rgba[i * 4 + 1] << 8 | rgba[i * 4 + 2]);
            float top = picture.Depth[i];
            if (!float.IsNegativeInfinity(top))
            {
                color = Renderer.Shade(color, 0.85f + 0.2f * (high > low ? (top - low) / (high - low) : 1));
                if (top < scene.WaterLevel)
                    color = Mix(color, scene.Water, Math.Clamp(UnderWater + (scene.WaterLevel.Value - top) / DeepWater, UnderWater, DeepestWater));
            }
            color = Mix(scene.Background, color, alpha);
            Put(rgba, i, color, 1);
            rgba[i * 4 + 3] = 255;
        }
        return rgba;
    }

    internal static uint Base(int material) => material >= 0 && material < Colors.Length ? Colors[material] : Unknown;

    internal static void Put(byte[] rgba, int i, uint color, float shade)
    {
        rgba[i * 4] = Channel(color >> 16, shade);
        rgba[i * 4 + 1] = Channel(color >> 8, shade);
        rgba[i * 4 + 2] = Channel(color, shade);
    }

    static uint Mix(uint from, uint to, float amount) =>
        Enumerable.Range(0, 3).Aggregate(0u, (rgb, shift) =>
        {
            int bits = 16 - shift * 8;
            float value = ((from >> bits) & 0xFF) * (1 - amount) + ((to >> bits) & 0xFF) * amount;
            return rgb << 8 | (uint)Math.Clamp((int)MathF.Round(value), 0, 255);
        });

    static byte Channel(uint value, float shade) => (byte)Math.Clamp((int)((value & 0xFF) * shade), 0, 255);

    internal static byte[] Png(int width, int height, byte[] rgba)
    {
        using var output = new MemoryStream();
        output.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        byte[] header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;
        header[9] = 6;
        Chunk(output, "IHDR", header);
        using (var pixels = new MemoryStream())
        {
            using (var zlib = new ZLibStream(pixels, CompressionLevel.Optimal, leaveOpen: true))
                for (int row = 0; row < height; row++)
                {
                    zlib.WriteByte(0);
                    zlib.Write(rgba, row * width * 4, width * 4);
                }
            Chunk(output, "IDAT", pixels.ToArray());
        }
        Chunk(output, "IEND", []);
        return output.ToArray();
    }

    static void Chunk(Stream output, string type, byte[] data)
    {
        byte[] name = Encoding.ASCII.GetBytes(type);
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
        output.Write(number);
        output.Write(name);
        output.Write(data);
        uint crc = 0xFFFFFFFF;
        foreach (byte value in name.Concat(data))
            crc = Crcs[(crc ^ value) & 0xFF] ^ (crc >> 8);
        BinaryPrimitives.WriteUInt32BigEndian(number, crc ^ 0xFFFFFFFF);
        output.Write(number);
    }

    static uint Crc(int value)
    {
        uint crc = (uint)value;
        for (int bit = 0; bit < 8; bit++)
            crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1;
        return crc;
    }
}

sealed class Textures
{
    readonly int _size;
    readonly uint[]?[] _pixels;

    Textures(int size, uint[]?[] pixels) => (_size, _pixels) = (size, pixels);

    public static Textures? Load(string file)
    {
        if (!File.Exists(file))
            return null;
        using var reader = new BinaryReader(File.OpenRead(file));
        int count = reader.ReadUInt16(), size = reader.ReadUInt16();
        var pixels = new uint[]?[count];
        for (int material = 0; material < count; material++)
        {
            byte[] rgba = reader.ReadBytes(size * size * 4);
            uint[] texels = new uint[size * size];
            bool any = false;
            for (int i = 0; i < texels.Length; i++)
            {
                texels[i] = (uint)(rgba[i * 4] << 16 | rgba[i * 4 + 1] << 8 | rgba[i * 4 + 2]);
                any |= rgba[i * 4 + 3] != 0;
            }
            pixels[material] = any ? texels : null;
        }
        return new Textures(size, pixels);
    }

    public uint? Texel(int material, float u, float v)
    {
        if (material < 0 || material >= _pixels.Length || _pixels[material] is not uint[] texels)
            return null;
        int x = Math.Clamp((int)(u * _size), 0, _size - 1), y = Math.Clamp((int)(v * _size), 0, _size - 1);
        return texels[y * _size + x];
    }
}
