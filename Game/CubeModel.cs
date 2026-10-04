using System.Buffers.Binary;
using OpenKogama.Kogama;

namespace OpenKogama.Game;

public sealed class CubeModel
{
    const byte DefaultCorners = 1;
    const byte OneMaterial = 2;

    readonly Dictionary<(short X, short Y, short Z), byte[]> _cubes = [];
    readonly object _sync = new();

    public int Count => _cubes.Count;

    public List<(short X, short Y, short Z)> Positions()
    {
        lock (_sync) return [.. _cubes.Keys];
    }

    public byte[]? Get(int x, int y, int z)
    {
        if (x is < short.MinValue or > short.MaxValue || y is < short.MinValue or > short.MaxValue || z is < short.MinValue or > short.MaxValue) return null;
        lock (_sync) return _cubes.GetValueOrDefault(((short)x, (short)y, (short)z));
    }

    public ((int X, int Y, int Z) Min, (int X, int Y, int Z) Max)? Bounds()
    {
        lock (_sync)
        {
            if (_cubes.Count == 0) return null;
            int minX = int.MaxValue, minY = int.MaxValue, minZ = int.MaxValue;
            int maxX = int.MinValue, maxY = int.MinValue, maxZ = int.MinValue;
            foreach ((short x, short y, short z) in _cubes.Keys)
            {
                minX = Math.Min(minX, x); minY = Math.Min(minY, y); minZ = Math.Min(minZ, z);
                maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y); maxZ = Math.Max(maxZ, z);
            }
            return ((minX, minY, minZ), (maxX, maxY, maxZ));
        }
    }

    public static void AppendAdded(List<byte> changes, short x, short y, short z, byte material)
    {
        changes.Add((byte)CubeAction.Added);
        AppendPosition(changes, x, y, z);
        changes.Add(DefaultCorners | OneMaterial);
        changes.Add(material);
    }

    public static void AppendDeleted(List<byte> changes, short x, short y, short z)
    {
        changes.Add((byte)CubeAction.Deleted);
        AppendPosition(changes, x, y, z);
    }

    static void AppendPosition(List<byte> changes, short x, short y, short z)
    {
        Span<byte> buffer = stackalloc byte[2];
        foreach (short value in (ReadOnlySpan<short>)[x, y, z])
        {
            BinaryPrimitives.WriteInt16BigEndian(buffer, value);
            changes.Add(buffer[0]);
            changes.Add(buffer[1]);
        }
    }

    public static CubeModel SingleCube(byte material)
    {
        var model = new CubeModel();
        model._cubes[(0, 0, 0)] = [DefaultCorners | OneMaterial, material];
        return model;
    }

    public void Clear()
    {
        lock (_sync) _cubes.Clear();
    }

    public CubeModel Clone()
    {
        var copy = new CubeModel();
        lock (_sync)
            foreach (var (position, cube) in _cubes)
                copy._cubes[position] = cube;
        return copy;
    }

    public static CubeModel FromBytes(byte[] data)
    {
        var model = new CubeModel();
        int offset = 0;
        int entries = BinaryPrimitives.ReadInt32BigEndian(data);
        offset += 4;

        for (int i = 0; i < entries; i++)
        {
            short x = ReadInt16(data, ref offset);
            short y = ReadInt16(data, ref offset);
            short z = ReadInt16(data, ref offset);
            byte[] cube = ReadCube(data, ref offset);

            int row = Math.Max(cube[0] >> 2, 1);
            for (int j = 0; j < row; j++)
                model._cubes[((short)(x + j), y, z)] = cube;
        }

        return model;
    }

    public void Apply(byte[] changes)
    {
        lock (_sync) ApplyUnsafe(changes);
    }

    void ApplyUnsafe(byte[] changes)
    {
        int offset = 0;

        while (offset < changes.Length)
        {
            var action = (CubeAction)changes[offset++];
            short x = ReadInt16(changes, ref offset);
            short y = ReadInt16(changes, ref offset);
            short z = ReadInt16(changes, ref offset);

            switch (action)
            {
                case CubeAction.Deleted:
                    _cubes.Remove((x, y, z));
                    break;

                case CubeAction.Added:
                case CubeAction.FaceChanged:
                case CubeAction.CornersChangedDone:
                    byte[] cube = ReadCube(changes, ref offset);
                    int row = Math.Max(cube[0] >> 2, 1);
                    for (int j = 0; j < row; j++)
                        _cubes[((short)(x + j), y, z)] = cube;
                    break;
            }
        }
    }

    public byte[] ToBytes()
    {
        lock (_sync) return ToBytesUnsafe();
    }

    byte[] ToBytesUnsafe()
    {
        using var stream = new MemoryStream();
        Span<byte> buffer = stackalloc byte[4];

        BinaryPrimitives.WriteInt32BigEndian(buffer, _cubes.Count);
        stream.Write(buffer);

        foreach (var ((x, y, z), cube) in _cubes)
        {
            BinaryPrimitives.WriteInt16BigEndian(buffer, x); stream.Write(buffer[..2]);
            BinaryPrimitives.WriteInt16BigEndian(buffer, y); stream.Write(buffer[..2]);
            BinaryPrimitives.WriteInt16BigEndian(buffer, z); stream.Write(buffer[..2]);

            stream.WriteByte((byte)((1 << 2) | (cube[0] & 3)));
            stream.Write(cube, 1, cube.Length - 1);
        }

        return stream.ToArray();
    }

    static byte[] ReadCube(byte[] data, ref int offset)
    {
        byte flags = data[offset];
        int length = 1
            + ((flags & DefaultCorners) != 0 ? 0 : 8)
            + ((flags & OneMaterial) != 0 ? 1 : 6);

        byte[] cube = data[offset..(offset + length)];
        offset += length;
        return cube;
    }

    static short ReadInt16(byte[] data, ref int offset)
    {
        short value = BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(offset));
        offset += 2;
        return value;
    }
}
