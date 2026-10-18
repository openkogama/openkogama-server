using System.Buffers.Binary;
using System.Text.Json;
using OpenKogama.Storage;
using OpenKogama.World;

namespace OpenKogama.Game;

public static class ModelFiles
{
    const int MaxBytes = 4 * 1024 * 1024;
    const int MaxNameLength = 100;
    const float MaxScale = 100;
    const int KsCubesHeader = 9;
    const int KsCubesEntry = 26;
    static readonly byte[] Magic = "OKM1"u8.ToArray();
    static readonly byte[] KsCubesMagic = "KSCB"u8.ToArray();
    static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];
    static readonly byte[] Identity = [20, 120, 124, 24, 4, 104, 100, 0];
    static readonly byte[] DefaultMaterials = [21, 21, 21, 21, 21, 21];
    static readonly int Header = Magic.Length + sizeof(float);

    sealed record LooseCube(int X, int Y, int Z, byte[] Materials, byte[] Corners);

    public static byte[]? Export(int profile, int itemId)
    {
        if (Stores.Profiles.Items(profile).Find(item => item.Id == itemId) is not Item item || ModelSnapshot(item) is not Snapshot snapshot)
            return null;

        Prototype prototype = snapshot.Prototypes[0];
        byte[] cubes = prototype.Cubes.ToBytes();
        byte[] file = new byte[Header + cubes.Length];
        Magic.CopyTo(file, 0);
        BinaryPrimitives.WriteSingleBigEndian(file.AsSpan(Magic.Length), prototype.Scale);
        cubes.CopyTo(file, Header);
        return file;
    }

    public static Item? Import(int profile, string? name, byte[] file, float? scale)
    {
        if (file.Length > MaxBytes || Read(file) is not (CubeModel cubes, float own)) return null;
        float chosen = scale ?? own;
        if (cubes.Count == 0 || !ValidScale(chosen)) return null;

        byte[] data = ModelInventory.Pack(new Prototype(1, chosen, profile, cubes), [1f, 1f, 1f]);
        return Inventories.Add(profile, CleanName(name) ?? ModelInventory.ModelName, ModelInventory.ModelCategory, data, profile);
    }

    public static bool Rescale(int profile, int itemId, float scale)
    {
        if (!ValidScale(scale) || Stores.Profiles.Items(profile).Find(item => item.Id == itemId) is not Item item || ModelSnapshot(item) is not Snapshot snapshot)
            return false;

        Prototype prototype = snapshot.Prototypes[0];
        Snapshot scaled = snapshot with { Prototypes = [new Prototype(prototype.Id, scale, prototype.AuthorId, prototype.Cubes)] };
        return Stores.Profiles.SetItemData(profile, itemId, WorldSerializer.Write(scaled, runtime: false));
    }

    public static string? CleanName(string? name)
    {
        string clean = name?.Trim() ?? "";
        return clean.Length == 0 ? null : clean[..Math.Min(clean.Length, MaxNameLength)];
    }

    static bool ValidScale(float scale) => float.IsFinite(scale) && scale > 0 && scale <= MaxScale;

    static Snapshot? ModelSnapshot(Item item)
    {
        if (item.Category is not (ModelInventory.ModelCategory or ModelInventory.PremiumModelCategory)) return null;
        try
        {
            Snapshot snapshot = WorldSerializer.Read(item.Bytes, runtime: false);
            return snapshot.Prototypes.Count == 1 ? snapshot : null;
        }
        catch (Exception e) when (e is FormatException or EndOfStreamException or ArgumentException or InvalidDataException)
        {
            return null;
        }
    }

    static (CubeModel Cubes, float Scale)? Read(byte[] file)
    {
        try
        {
            if (file.AsSpan().StartsWith(Magic))
                return (CubeModel.FromBytes(file[Header..]), BinaryPrimitives.ReadSingleBigEndian(file.AsSpan(Magic.Length)));
            if (file.AsSpan().StartsWith(KsCubesMagic))
                return KsCubesBinary(file) is byte[] binary ? (CubeModel.FromBytes(binary), 1f) : null;
            ReadOnlyMemory<byte> text = file.AsMemory(file.AsSpan().StartsWith(Utf8Bom) ? Utf8Bom.Length : 0);
            if (text.Span is [(byte)'{', ..])
                return KsCubesJson(text) is byte[] json ? (CubeModel.FromBytes(json), 1f) : null;
            return (CubeModel.FromBytes(file), 1f);
        }
        catch (Exception e) when (e is ArgumentException or IndexOutOfRangeException or JsonException
            or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            return null;
        }
    }

    static byte[]? KsCubesBinary(byte[] file)
    {
        if (file.Length < KsCubesHeader) return null;
        int count = BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(KsCubesMagic.Length + 1));
        if (count < 0 || KsCubesHeader + (long)count * KsCubesEntry > file.Length) return null;

        var cubes = new List<LooseCube>(count);
        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<byte> entry = file.AsSpan(KsCubesHeader + i * KsCubesEntry, KsCubesEntry);
            cubes.Add(new LooseCube(
                BinaryPrimitives.ReadInt32LittleEndian(entry),
                BinaryPrimitives.ReadInt32LittleEndian(entry[4..]),
                BinaryPrimitives.ReadInt32LittleEndian(entry[8..]),
                entry[12..18].ToArray(),
                entry[18..26].ToArray()));
        }
        return Native(cubes);
    }

    static byte[]? KsCubesJson(ReadOnlyMemory<byte> text)
    {
        using JsonDocument document = JsonDocument.Parse(text);
        if (!document.RootElement.TryGetProperty("cubes", out JsonElement list) || list.ValueKind != JsonValueKind.Array) return null;

        var cubes = new List<LooseCube>();
        foreach (JsonElement cube in list.EnumerateArray())
            cubes.Add(new LooseCube(
                cube.GetProperty("x").GetInt32(),
                cube.GetProperty("y").GetInt32(),
                cube.GetProperty("z").GetInt32(),
                Bytes(cube, "materials") ?? DefaultMaterials,
                Bytes(cube, "corners") ?? Identity));
        return Native(cubes);
    }

    static byte[]? Bytes(JsonElement cube, string name)
    {
        if (!cube.TryGetProperty(name, out JsonElement value) || value.ValueKind == JsonValueKind.Null) return null;
        return value.ValueKind == JsonValueKind.String ? value.GetBytesFromBase64() : [.. value.EnumerateArray().Select(number => number.GetByte())];
    }

    static byte[]? Native(List<LooseCube> cubes)
    {
        using var stream = new MemoryStream();
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, cubes.Count);
        stream.Write(number);

        foreach (LooseCube cube in cubes)
        {
            if (cube.Materials.Length != 6 || cube.Corners.Length != 8 || cube.Corners.Any(corner => corner >= 125)) return null;
            foreach (int value in (ReadOnlySpan<int>)[cube.X, cube.Y, cube.Z])
            {
                if (value is < short.MinValue or > short.MaxValue) return null;
                BinaryPrimitives.WriteInt16BigEndian(number, (short)value);
                stream.Write(number[..2]);
            }

            bool moved = !cube.Corners.AsSpan().SequenceEqual(Identity);
            bool uniform = cube.Materials.All(material => material == cube.Materials[0]);
            stream.WriteByte((byte)(1 << 2 | (moved ? 0 : 1) | (uniform ? 2 : 0)));
            if (moved) stream.Write(cube.Corners);
            if (uniform) stream.WriteByte(cube.Materials[0]);
            else stream.Write(cube.Materials);
        }
        return stream.ToArray();
    }
}
