using System.Buffers.Binary;
using System.Text;

namespace OpenKogama.World;

public sealed class BytePackerReader(byte[] data)
{
    int _offset;

    public bool AtEnd => _offset >= data.Length;

    public byte ReadByte() => data[_offset++];

    public bool ReadBool() => data[_offset++] != 0;

    public short ReadInt16()
    {
        short value = BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(_offset));
        _offset += 2;
        return value;
    }

    public int ReadInt32()
    {
        int value = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(_offset));
        _offset += 4;
        return value;
    }

    public long ReadInt64()
    {
        long value = BinaryPrimitives.ReadInt64BigEndian(data.AsSpan(_offset));
        _offset += 8;
        return value;
    }

    public float ReadSingle()
    {
        float value = BinaryPrimitives.ReadSingleBigEndian(data.AsSpan(_offset));
        _offset += 4;
        return value;
    }

    public byte[] ReadBytes(int count)
    {
        byte[] value = data[_offset..(_offset + count)];
        _offset += count;
        return value;
    }

    public string ReadString()
    {
        int length = Read7BitEncodedInt();
        string value = Encoding.UTF8.GetString(data, _offset, length);
        _offset += length;
        return value;
    }

    public float[] ReadSingles(int count)
    {
        var values = new float[count];
        for (int i = 0; i < count; i++) values[i] = ReadSingle();
        return values;
    }

    int Read7BitEncodedInt()
    {
        int value = 0;
        int shift = 0;
        byte current;
        do
        {
            current = ReadByte();
            value |= (current & 0x7F) << shift;
            shift += 7;
        } while ((current & 0x80) != 0);
        return value;
    }

    public List<(string Key, PackedType Type, object Value)> ReadPairs()
    {
        int count = ReadInt32();
        var pairs = new List<(string, PackedType, object)>(count);

        for (int i = 0; i < count; i++)
        {
            string key = ReadString();
            var type = (PackedType)ReadByte();
            pairs.Add((key, type, ReadValue(type)));
        }

        return pairs;
    }

    object ReadValue(PackedType type) => type switch
    {
        PackedType.Int32 => ReadInt32(),
        PackedType.Single => ReadSingle(),
        PackedType.Bool => ReadBool(),
        PackedType.Byte => ReadByte(),
        PackedType.Int64 => ReadInt64(),
        PackedType.String => ReadString(),
        PackedType.Int32Array or PackedType.Int32HashtableKeysOnly => ReadArray(ReadInt32),
        PackedType.SingleArray => ReadArray(ReadSingle),
        PackedType.BoolArray => ReadArray(ReadBool),
        PackedType.Int64Array => ReadArray(ReadInt64),
        PackedType.Hashtable => ReadPairs(),
        _ => throw new NotSupportedException($"BytePacker: cannot read {type}"),
    };

    T[] ReadArray<T>(Func<T> read)
    {
        var items = new T[ReadInt32()];
        for (int i = 0; i < items.Length; i++) items[i] = read();
        return items;
    }
}
