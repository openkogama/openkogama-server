using System.Buffers.Binary;
using System.Text;

namespace OpenKogama.World;

// Type tags for the name/value pairs inside a packed world object (HashtableDataType).
public enum PackedType : byte
{
    Int32 = 0,
    Int32Array = 1,
    Single = 2,
    SingleArray = 3,
    Int32HashtableKeysOnly = 4,
    Bool = 5,
    BoolArray = 6,
    String = 7,
    Hashtable = 8,
    Byte = 9,
    Int64 = 10,
    Int64Array = 11,
}

// Writes the binary format the client reads with MV.WorldObject.BytePacker.
// Numbers are big endian, strings carry a 7 bit encoded length like BinaryWriter.
public sealed class BytePackerWriter
{
    readonly List<byte> _bytes = [];

    public byte[] ToArray() => [.. _bytes];

    public void WriteByte(byte value) => _bytes.Add(value);

    public void WriteBytes(byte[] value) => _bytes.AddRange(value);

    public void WriteBool(bool value) => _bytes.Add(value ? (byte)1 : (byte)0);

    public void WriteInt16(short value)
    {
        Span<byte> buffer = stackalloc byte[2];
        BinaryPrimitives.WriteInt16BigEndian(buffer, value);
        _bytes.AddRange(buffer);
    }

    public void WriteInt32(int value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buffer, value);
        _bytes.AddRange(buffer);
    }

    public void WriteInt64(long value)
    {
        Span<byte> buffer = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(buffer, value);
        _bytes.AddRange(buffer);
    }

    public void WriteSingle(float value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteSingleBigEndian(buffer, value);
        _bytes.AddRange(buffer);
    }

    public void WriteString(string value)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(value);
        Write7BitEncodedInt(utf8.Length);
        _bytes.AddRange(utf8);
    }

    public void WriteVector3(float x, float y, float z)
    {
        WriteSingle(x);
        WriteSingle(y);
        WriteSingle(z);
    }

    public void WriteQuaternion(float x, float y, float z, float w)
    {
        WriteSingle(x);
        WriteSingle(y);
        WriteSingle(z);
        WriteSingle(w);
    }

    void Write7BitEncodedInt(int value)
    {
        uint remaining = (uint)value;
        while (remaining >= 0x80)
        {
            _bytes.Add((byte)(remaining | 0x80));
            remaining >>= 7;
        }
        _bytes.Add((byte)remaining);
    }

    // A pair table is a count followed by that many key, type, value triples.
    public void WritePairs(IReadOnlyList<(string Key, PackedType Type, object Value)> pairs)
    {
        WriteInt32(pairs.Count);

        foreach ((string key, PackedType type, object value) in pairs)
        {
            WriteString(key);
            WriteByte((byte)type);

            switch (type)
            {
                case PackedType.Int32: WriteInt32((int)value); break;
                case PackedType.Single: WriteSingle((float)value); break;
                case PackedType.Bool: WriteBool((bool)value); break;
                case PackedType.Byte: WriteByte((byte)value); break;
                case PackedType.Int64: WriteInt64((long)value); break;
                case PackedType.String: WriteString((string)value); break;

                case PackedType.Int32Array:
                {
                    int[] items = (int[])value;
                    WriteInt32(items.Length);
                    foreach (int item in items) WriteInt32(item);
                    break;
                }
                case PackedType.SingleArray:
                {
                    float[] items = (float[])value;
                    WriteInt32(items.Length);
                    foreach (float item in items) WriteSingle(item);
                    break;
                }
                case PackedType.BoolArray:
                {
                    bool[] items = (bool[])value;
                    WriteInt32(items.Length);
                    foreach (bool item in items) WriteBool(item);
                    break;
                }
                case PackedType.Int32HashtableKeysOnly:
                {
                    int[] items = (int[])value;
                    WriteInt32(items.Length);
                    foreach (int item in items) WriteInt32(item);
                    break;
                }
                case PackedType.Int64Array:
                {
                    long[] items = (long[])value;
                    WriteInt32(items.Length);
                    foreach (long item in items) WriteInt64(item);
                    break;
                }
                case PackedType.Hashtable:
                {
                    var nested = (IReadOnlyList<(string, PackedType, object)>)value;
                    WritePairs(nested);
                    break;
                }

                default:
                    throw new NotSupportedException($"BytePacker: cannot write {type}");
            }
        }
    }
}
