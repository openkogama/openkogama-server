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
    const int HeaderSize = 20;
    const int WideHeaderSize = 48;
    const int WideMetadataSize = 20;
    const int WideDataOffset = 32;
    const int DataOffsetField = 12;
    const int AppendAlignment = 16;

    public sealed record Entry(long PathId, long Start, int Size, int ClassId, int Table, int TypeIndex = -1);

    readonly byte[] _bytes;
    readonly List<TypeNode?> _trees = [];
    readonly List<byte[]> _tails = [];
    readonly long _metadataEnd;
    int _tableStart;
    int _tableEnd;

    public uint Version { get; }
    public string Engine { get; }
    public int Target { get; }
    public long DataOffset { get; }
    public IReadOnlyList<Entry> Objects { get; }

    public SerializedFile(byte[] bytes)
    {
        _bytes = bytes;
        var reader = new ByteReader(bytes);
        long metadataSize = reader.BigUInt();
        reader.BigUInt();
        Version = reader.BigUInt();
        DataOffset = reader.BigUInt();
        if (Version < OldestVersion) throw new NotSupportedException($"serialized file version {Version} is not supported");
        if (reader.Byte() != 0) throw new NotSupportedException("big endian serialized files are not supported");
        reader.Skip(3);
        if (Version >= WideVersion)
        {
            metadataSize = reader.BigUInt();
            reader.BigLong();
            DataOffset = reader.BigLong();
            reader.BigLong();
        }
        _metadataEnd = (Version >= WideVersion ? WideHeaderSize : HeaderSize) + metadataSize;
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
                    _trees.Add(TypeNode.Read(reader, Version));
                    if (Version >= 21) reader.Skip(4 * reader.Int());
                }
                else
                {
                    SkipNode(reader);
                    _trees.Add(null);
                }
            }
            else
            {
                _trees.Add(null);
            }
            classes.Add(classId);
        }
        bool longIds = Version is >= 7 and < 14 && reader.Int() != 0;

        var objects = new List<Entry>();
        _tableStart = reader.Position;
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
            int tail = reader.Position;
            int type = reader.Int();
            int classId = Version < 16 ? reader.UShort() : classes[type];
            if (Version < 17) reader.Skip(2);
            if (Version is 15 or 16) reader.Skip(1);
            _tails.Add(bytes[tail..reader.Position]);
            objects.Add(new Entry(pathId, start, size, classId, table, Version >= 17 ? type : classes.IndexOf(type)));
        }
        _tableEnd = reader.Position;
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

    public TypeNode? Tree(Entry entry) => entry.TypeIndex >= 0 && entry.TypeIndex < _trees.Count ? _trees[entry.TypeIndex] : null;

    public byte[] ToArray() => (byte[])_bytes.Clone();

    public long NextPathId => Objects.Count == 0 ? 1 : Objects.Max(entry => entry.PathId) + 1;

    public Entry? Find(long pathId)
    {
        foreach (Entry entry in Objects)
            if (entry.PathId == pathId) return entry;
        return null;
    }

    public byte[] Rebuild(IReadOnlyDictionary<long, byte[]> changed, IReadOnlyList<(long PathId, long Like, byte[] Data)> added)
    {
        if (Version < 14 || _tableStart == 0) throw new NotSupportedException($"serialized file version {Version} cannot be rebuilt");
        Dictionary<long, int> indexes = Objects.Select((entry, index) => (entry.PathId, index)).ToDictionary(pair => pair.PathId, pair => pair.index);

        long end = _bytes.Length - DataOffset;
        var appended = new MemoryStream();
        long Append(byte[] data)
        {
            long at = (end + appended.Length + AppendAlignment - 1) / AppendAlignment * AppendAlignment;
            while (end + appended.Length < at) appended.WriteByte(0);
            appended.Write(data);
            return at;
        }

        var rows = new List<(long PathId, long Start, int Size, byte[] Tail)>();
        for (int i = 0; i < Objects.Count; i++)
        {
            Entry entry = Objects[i];
            rows.Add(changed.TryGetValue(entry.PathId, out byte[]? data)
                ? (entry.PathId, Append(data), data.Length, _tails[i])
                : (entry.PathId, entry.Start, entry.Size, _tails[i]));
        }
        foreach ((long pathId, long like, byte[] data) in added)
        {
            if (!indexes.TryGetValue(like, out int index)) throw new InvalidDataException($"object {like} is not in the file");
            rows.Add((pathId, Append(data), data.Length, _tails[index]));
        }
        rows = [.. rows.OrderBy(row => row.PathId)];

        using var metadata = new MemoryStream();
        metadata.Write(_bytes, 0, _tableStart);
        WriteLittle(metadata, rows.Count);
        Span<byte> number = stackalloc byte[8];
        foreach ((long pathId, long start, int size, byte[] tail) in rows)
        {
            while (metadata.Position % 4 != 0) metadata.WriteByte(0);
            BinaryPrimitives.WriteInt64LittleEndian(number, pathId);
            metadata.Write(number);
            if (Version >= WideVersion)
            {
                BinaryPrimitives.WriteInt64LittleEndian(number, start);
                metadata.Write(number);
            }
            else
            {
                WriteLittle(metadata, (int)start);
            }
            WriteLittle(metadata, size);
            metadata.Write(tail);
        }
        metadata.Write(_bytes, _tableEnd, (int)(_metadataEnd - _tableEnd));

        long dataOffset = DataOffset;
        if (metadata.Length > dataOffset)
        {
            long alignment = DataOffset % 4096 == 0 ? 4096 : 16;
            dataOffset = (metadata.Length + alignment - 1) / alignment * alignment;
        }
        byte[] file = new byte[dataOffset + end + appended.Length];
        metadata.ToArray().CopyTo(file, 0);
        _bytes.AsSpan((int)DataOffset).CopyTo(file.AsSpan((int)dataOffset));
        appended.ToArray().CopyTo(file, dataOffset + end);

        uint metadataSize = (uint)(metadata.Length - (Version >= WideVersion ? WideHeaderSize : HeaderSize));
        if (Version >= WideVersion)
        {
            BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(WideMetadataSize), metadataSize);
            BinaryPrimitives.WriteInt64BigEndian(file.AsSpan(WideFileSize), file.Length);
            BinaryPrimitives.WriteInt64BigEndian(file.AsSpan(WideDataOffset), dataOffset);
        }
        else
        {
            BinaryPrimitives.WriteUInt32BigEndian(file, metadataSize);
            BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(FileSize), (uint)file.Length);
            BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(DataOffsetField), (uint)dataOffset);
        }
        return file;
    }

    static void WriteLittle(Stream stream, int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        stream.Write(bytes);
    }

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
