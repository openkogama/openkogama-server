using System.Buffers.Binary;
using System.Text;

namespace OpenKogama.Content;

public sealed record ArchiveNode(string Name, byte[] Data, uint Flags);

public sealed record ArchiveFormat(string Signature, uint Version, string Player, string Engine)
{
    public static ArchiveFormat Current(string engine) => new(UnityArchive.Signature, UnityArchive.Version, "5.x.x", engine);
}

public static class UnityArchive
{
    public const uint SerializedFileNode = 4;
    public const string Signature = "UnityFS";
    public const uint Version = 8;
    const string RawSignature = "UnityRaw";
    const string WebSignature = "UnityWeb";
    const uint RawVersion = 3;
    const uint CompressionMask = 0x3F;
    const uint CombinedInfo = 0x40;
    const uint InfoAtEnd = 0x80;
    const uint PaddedInfo = 0x200;
    const ushort StreamedBlock = 0x40;

    public static string EngineOf(byte[] archive)
    {
        var reader = new ByteReader(archive);
        if (reader.CString() is not (Signature or RawSignature or WebSignature)) throw new InvalidDataException("not a Unity bundle");
        reader.BigUInt();
        reader.CString();
        return reader.CString();
    }

    public static (string Engine, List<ArchiveNode> Nodes) Read(byte[] archive)
    {
        (ArchiveFormat format, List<ArchiveNode> nodes) = ReadWithFormat(archive);
        return (format.Engine, nodes);
    }

    public static (ArchiveFormat Format, List<ArchiveNode> Nodes) ReadWithFormat(byte[] archive)
    {
        var reader = new ByteReader(archive);
        string signature = reader.CString();
        if (signature == WebSignature) throw new NotSupportedException("compressed UnityWeb bundles are not supported, store them as UnityRaw");
        if (signature is not (Signature or RawSignature)) throw new InvalidDataException("not a Unity bundle");
        uint version = reader.BigUInt();
        string player = reader.CString();
        string engine = reader.CString();
        var format = new ArchiveFormat(signature, version, player, engine);
        return (format, signature == RawSignature ? ReadRaw(archive, reader, version) : ReadFs(archive, reader, version));
    }

    static List<ArchiveNode> ReadRaw(byte[] archive, ByteReader reader, uint version)
    {
        if (version >= 4) reader.Skip(20);
        reader.BigUInt();
        int headerSize = (int)reader.BigUInt();
        reader.BigUInt();
        int levels = reader.BigInt();
        int blockSize = 0;
        for (int i = 0; i < levels; i++)
        {
            blockSize = (int)reader.BigUInt();
            reader.BigUInt();
        }

        var block = new ByteReader(archive.AsSpan(headerSize, blockSize).ToArray());
        int count = block.BigInt();
        var nodes = new List<ArchiveNode>(count);
        for (int i = 0; i < count; i++)
        {
            string name = block.CString();
            int offset = (int)block.BigUInt();
            int size = (int)block.BigUInt();
            nodes.Add(new ArchiveNode(name, archive.AsSpan(headerSize + offset, size).ToArray(), name.Contains('.') ? 0 : SerializedFileNode));
        }
        return nodes;
    }

    static List<ArchiveNode> ReadFs(byte[] archive, ByteReader reader, uint version)
    {
        long size = reader.BigLong();
        int packedInfo = (int)reader.BigUInt();
        int infoSize = (int)reader.BigUInt();
        uint flags = reader.BigUInt();
        if (version >= 7) reader.Align(16);

        ReadOnlySpan<byte> packed = (flags & InfoAtEnd) != 0 ? archive.AsSpan((int)(size - packedInfo), packedInfo) : reader.Take(packedInfo);
        var info = new ByteReader(Unpack(packed, infoSize, flags & CompressionMask));
        if ((flags & PaddedInfo) != 0) reader.Align(16);

        info.Skip(16);
        int blocks = info.BigInt();
        using var joined = new MemoryStream();
        for (int i = 0; i < blocks; i++)
        {
            int unpacked = (int)info.BigUInt();
            int packedSize = (int)info.BigUInt();
            ushort blockFlags = info.BigUShort();
            joined.Write(Unpack(reader.Take(packedSize), unpacked, blockFlags & CompressionMask));
        }
        byte[] data = joined.ToArray();

        int nodes = info.BigInt();
        var result = new List<ArchiveNode>(nodes);
        for (int i = 0; i < nodes; i++)
        {
            long offset = info.BigLong();
            long length = info.BigLong();
            uint nodeFlags = info.BigUInt();
            result.Add(new ArchiveNode(info.CString(), data.AsSpan((int)offset, (int)length).ToArray(), nodeFlags));
        }
        return result;
    }

    public static byte[] Write(string engine, IReadOnlyList<ArchiveNode> nodes) => Write(ArchiveFormat.Current(engine), nodes);

    public static byte[] Write(ArchiveFormat format, IReadOnlyList<ArchiveNode> nodes) =>
        format.Signature == RawSignature ? WriteRaw(format, nodes) : WriteFs(format, nodes);

    // UnityRaw is the uncompressed form of the UnityWeb bundles of Unity 3 to 5.2: one level, a directory, then the files.
    static byte[] WriteRaw(ArchiveFormat format, IReadOnlyList<ArchiveNode> nodes)
    {
        using var directory = new MemoryStream();
        WriteUInt(directory, (uint)nodes.Count);
        int directorySize = 4 + nodes.Sum(node => Encoding.UTF8.GetByteCount(node.Name) + 1 + 8);
        directorySize = (directorySize + 3) / 4 * 4;
        long offset = directorySize;
        foreach (ArchiveNode node in nodes)
        {
            directory.Write(Encoding.UTF8.GetBytes(node.Name + "\0"));
            WriteUInt(directory, (uint)offset);
            WriteUInt(directory, (uint)node.Data.Length);
            offset += node.Data.Length;
        }
        directory.Write(new byte[directorySize - directory.Length]);
        long blockSize = offset;

        byte[] strings = Encoding.ASCII.GetBytes(RawSignature + "\0");
        int headerSize = strings.Length + 4 + Encoding.ASCII.GetByteCount(format.Player + "\0" + format.Engine + "\0") + 4 * 4 + 8 + 4 + 4;
        headerSize = (headerSize + 3) / 4 * 4;
        long total = headerSize + blockSize;

        using var archive = new MemoryStream();
        archive.Write(strings);
        WriteUInt(archive, RawVersion);
        archive.Write(Encoding.ASCII.GetBytes(format.Player + "\0" + format.Engine + "\0"));
        WriteUInt(archive, (uint)total);
        WriteUInt(archive, (uint)headerSize);
        WriteUInt(archive, 1);
        WriteUInt(archive, 1);
        WriteUInt(archive, (uint)blockSize);
        WriteUInt(archive, (uint)blockSize);
        WriteUInt(archive, (uint)total);
        WriteUInt(archive, (uint)directorySize);
        archive.Write(new byte[headerSize - archive.Length]);
        directory.WriteTo(archive);
        foreach (ArchiveNode node in nodes) archive.Write(node.Data);
        return archive.ToArray();
    }

    static byte[] WriteFs(ArchiveFormat format, IReadOnlyList<ArchiveNode> nodes)
    {
        bool aligned = format.Version >= 7;
        long size = nodes.Sum(node => (long)node.Data.Length);
        using var info = new MemoryStream();
        info.Write(new byte[16]);
        WriteUInt(info, 1);
        WriteUInt(info, (uint)size);
        WriteUInt(info, (uint)size);
        WriteUShort(info, StreamedBlock);
        WriteUInt(info, (uint)nodes.Count);
        long offset = 0;
        foreach (ArchiveNode node in nodes)
        {
            WriteLong(info, offset);
            WriteLong(info, node.Data.Length);
            WriteUInt(info, node.Flags);
            info.Write(Encoding.UTF8.GetBytes(node.Name + "\0"));
            offset += node.Data.Length;
        }

        using var archive = new MemoryStream();
        archive.Write(Encoding.ASCII.GetBytes(Signature + "\0"));
        WriteUInt(archive, format.Version);
        archive.Write(Encoding.ASCII.GetBytes(format.Player + "\0" + format.Engine + "\0"));
        long headerEnd = archive.Position + 8 + 12;
        long infoStart = aligned ? Align16(headerEnd) : headerEnd;
        long dataStart = aligned ? Align16(infoStart + info.Length) : infoStart + info.Length;
        WriteLong(archive, dataStart + size);
        WriteUInt(archive, (uint)info.Length);
        WriteUInt(archive, (uint)info.Length);
        WriteUInt(archive, aligned ? CombinedInfo | PaddedInfo : CombinedInfo);
        archive.Write(new byte[infoStart - headerEnd]);
        info.WriteTo(archive);
        archive.Write(new byte[dataStart - infoStart - info.Length]);
        foreach (ArchiveNode node in nodes) archive.Write(node.Data);
        return archive.ToArray();
    }

    static long Align16(long value) => (value + 15) / 16 * 16;

    static void WriteUInt(Stream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        stream.Write(bytes);
    }

    static void WriteLong(Stream stream, long value)
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(bytes, value);
        stream.Write(bytes);
    }

    static void WriteUShort(Stream stream, ushort value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
        stream.Write(bytes);
    }

    static byte[] Unpack(ReadOnlySpan<byte> packed, int size, uint compression) => compression switch
    {
        0 => packed.ToArray(),
        2 or 3 => Lz4(packed, size),
        _ => throw new NotSupportedException($"bundle compression {compression} is not supported"),
    };

    static byte[] Lz4(ReadOnlySpan<byte> source, int size)
    {
        byte[] target = new byte[size];
        int read = 0, written = 0;
        while (read < source.Length)
        {
            int token = source[read++];
            int literal = token >> 4;
            if (literal == 15)
                for (byte next = 255; next == 255; literal += next) next = source[read++];
            source.Slice(read, literal).CopyTo(target.AsSpan(written));
            read += literal;
            written += literal;
            if (read >= source.Length) break;

            int distance = source[read] | (source[read + 1] << 8);
            read += 2;
            int match = token & 15;
            if (match == 15)
                for (byte next = 255; next == 255; match += next) next = source[read++];
            match += 4;
            for (int i = 0; i < match; i++) target[written + i] = target[written - distance + i];
            written += match;
        }
        if (written != size) throw new InvalidDataException("bundle block is damaged");
        return target;
    }
}
