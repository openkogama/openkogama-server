using System.Buffers.Binary;

namespace OpenKogama.Content;

public sealed class SerializedFile
{
    public const int MonoBehaviourClass = 114;
    public const int WebGLTarget = 20;
    const uint OldestVersion = 9;
    const uint WideVersion = 22;
    const int WideFileSize = 24;
    const int FileSize = 4;

    public sealed record Entry(long PathId, long Start, int Size, int ClassId, int Table);

    readonly byte[] _bytes;

    public uint Version { get; }
    public string Engine { get; }
    public int Target { get; }
    public long DataOffset { get; }
    public IReadOnlyList<Entry> Objects { get; }

    public SerializedFile(byte[] bytes)
    {
        _bytes = bytes;
        var reader = new ByteReader(bytes);
        reader.BigUInt();
        reader.BigUInt();
        Version = reader.BigUInt();
        DataOffset = reader.BigUInt();
        if (Version < OldestVersion) throw new NotSupportedException($"serialized file version {Version} is not supported");
        if (reader.Byte() != 0) throw new NotSupportedException("big endian serialized files are not supported");
        reader.Skip(3);
        if (Version >= WideVersion)
        {
            reader.BigUInt();
            reader.BigLong();
            DataOffset = reader.BigLong();
            reader.BigLong();
        }
        Engine = reader.CString();
        Target = reader.Int();
        bool tree = Version < 13 || reader.Byte() != 0;

        var classes = new List<int>();
        int types = reader.Int();
        for (int i = 0; i < types; i++)
        {
            int classId = reader.Int();
            short script = -1;
            if (Version >= 16) reader.Byte();
            if (Version >= 17) script = reader.Short();
            if (Version >= 13)
            {
                if (Version < 16 ? classId < 0 : classId == MonoBehaviourClass || script >= 0) reader.Skip(16);
                reader.Skip(16);
            }
            if (tree)
            {
                if (Version >= 12 || Version == 10)
                {
                    int nodes = reader.Int();
                    int strings = reader.Int();
                    reader.Skip(nodes * (Version >= 19 ? 32 : 24) + strings);
                    if (Version >= 21) reader.Skip(4 * reader.Int());
                }
                else
                {
                    SkipNode(reader);
                }
            }
            classes.Add(classId);
        }
        bool longIds = Version is >= 7 and < 14 && reader.Int() != 0;

        var objects = new List<Entry>();
        int count = reader.Int();
        for (int i = 0; i < count; i++)
        {
            long pathId;
            if (longIds) pathId = reader.Long();
            else if (Version < 14) pathId = reader.Int();
            else
            {
                reader.Align(4);
                pathId = reader.Long();
            }
            int table = reader.Position;
            long start = Version >= WideVersion ? reader.Long() : (uint)reader.Int();
            int size = reader.Int();
            int type = reader.Int();
            int classId = Version < 16 ? reader.UShort() : classes[type];
            if (Version < 17) reader.Skip(2);
            if (Version is 15 or 16) reader.Skip(1);
            objects.Add(new Entry(pathId, start, size, classId, table));
        }
        Objects = objects;
    }

    void SkipNode(ByteReader reader)
    {
        reader.CString();
        reader.CString();
        reader.Skip(Version == 3 ? 12 : 20);
        int children = reader.Int();
        for (int i = 0; i < children; i++) SkipNode(reader);
    }

    public byte[] ToArray() => (byte[])_bytes.Clone();

    public int Offset(Entry entry) => (int)(DataOffset + entry.Start);

    public ReadOnlySpan<byte> Data(Entry entry) => _bytes.AsSpan(Offset(entry), entry.Size);

    public byte[] With(Entry entry, ReadOnlySpan<byte> data)
    {
        if (data.Length == entry.Size)
        {
            byte[] same = ToArray();
            data.CopyTo(same.AsSpan(Offset(entry)));
            return same;
        }

        long end = (_bytes.Length + 15) / 16 * 16;
        byte[] grown = new byte[end + data.Length];
        _bytes.CopyTo(grown, 0);
        data.CopyTo(grown.AsSpan((int)end));
        Span<byte> table = grown.AsSpan(entry.Table);
        if (Version >= WideVersion)
        {
            BinaryPrimitives.WriteInt64LittleEndian(table, end - DataOffset);
            BinaryPrimitives.WriteInt32LittleEndian(table[8..], data.Length);
            BinaryPrimitives.WriteInt64BigEndian(grown.AsSpan(WideFileSize), grown.Length);
        }
        else
        {
            BinaryPrimitives.WriteUInt32LittleEndian(table, (uint)(end - DataOffset));
            BinaryPrimitives.WriteInt32LittleEndian(table[4..], data.Length);
            BinaryPrimitives.WriteUInt32BigEndian(grown.AsSpan(FileSize), (uint)grown.Length);
        }
        return grown;
    }
}
