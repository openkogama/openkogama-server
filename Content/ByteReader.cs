using System.Buffers.Binary;
using System.Text;

namespace OpenKogama.Content;

sealed class ByteReader(byte[] data, int position = 0)
{
    public int Position { get; set; } = position;
    public int Length => data.Length;

    public void Skip(int count) => Position += count;
    public void Align(int size) => Position = (Position + size - 1) / size * size;
    public byte Byte() => data[Position++];
    public short Short() => BinaryPrimitives.ReadInt16LittleEndian(Take(2));
    public ushort UShort() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
    public int Int() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));
    public long Long() => BinaryPrimitives.ReadInt64LittleEndian(Take(8));
    public ushort BigUShort() => BinaryPrimitives.ReadUInt16BigEndian(Take(2));
    public int BigInt() => BinaryPrimitives.ReadInt32BigEndian(Take(4));
    public uint BigUInt() => BinaryPrimitives.ReadUInt32BigEndian(Take(4));
    public long BigLong() => BinaryPrimitives.ReadInt64BigEndian(Take(8));

    public ReadOnlySpan<byte> Take(int count)
    {
        ReadOnlySpan<byte> span = data.AsSpan(Position, count);
        Position += count;
        return span;
    }

    public string CString()
    {
        int end = Array.IndexOf(data, (byte)0, Position);
        if (end < 0) throw new InvalidDataException("unterminated string");
        string text = Encoding.UTF8.GetString(data, Position, end - Position);
        Position = end + 1;
        return text;
    }

    public string Text()
    {
        int length = Int();
        if (length < 0 || Position + length > data.Length) throw new InvalidDataException("bad string length");
        string text = Encoding.UTF8.GetString(data, Position, length);
        Position += length;
        Align(4);
        return text;
    }
}
