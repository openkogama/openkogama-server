using System.Buffers.Binary;
using OpenKogama.Kogama;

namespace OpenKogama.Game;

// Cubes of one prototype. Each cube is kept as its own wire bytes:
//   flags (bit0 default corners, bit1 one material, rest = row length)
//   [8 corner bytes]  [1 or 6 material bytes]
public sealed class CubeModel
{
    const byte DefaultCorners = 1;
    const byte OneMaterial = 2;

    readonly Dictionary<(short X, short Y, short Z), byte[]> _cubes = [];

    public int Count => _cubes.Count;

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
        using var stream = new MemoryStream();
        Span<byte> buffer = stackalloc byte[4];

        BinaryPrimitives.WriteInt32BigEndian(buffer, _cubes.Count);
        stream.Write(buffer);

        foreach (var ((x, y, z), cube) in _cubes)
        {
            BinaryPrimitives.WriteInt16BigEndian(buffer, x); stream.Write(buffer[..2]);
            BinaryPrimitives.WriteInt16BigEndian(buffer, y); stream.Write(buffer[..2]);
            BinaryPrimitives.WriteInt16BigEndian(buffer, z); stream.Write(buffer[..2]);

            stream.WriteByte((byte)((1 << 2) | (cube[0] & 3)));   // every cube is its own row of 1
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
