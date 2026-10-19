using System.Buffers.Binary;
using System.Text;

namespace OpenKogama.Content;

public sealed record ArchiveNode(string Name, byte[] Data, uint Flags);

public static class UnityArchive
{
    public const uint SerializedFileNode = 4;
    const string Signature = "UnityFS";
    const uint Version = 8;
    const uint CompressionMask = 0x3F;
    const uint CombinedInfo = 0x40;
    const uint InfoAtEnd = 0x80;
    const uint PaddedInfo = 0x200;
    const ushort StreamedBlock = 0x40;

    public static string EngineOf(byte[] archive)
    {
        var reader = new ByteReader(archive);
        if (reader.CString() != Signature) throw new InvalidDataException("not a Unity bundle");
        reader.BigUInt();
        reader.CString();
        return reader.CString();
    }

    public static (string Engine, List<ArchiveNode> Nodes) Read(byte[] archive)
    {
        var reader = new ByteReader(archive);
        if (reader.CString() != Signature) throw new InvalidDataException("not a Unity bundle");
        uint version = reader.BigUInt();
        reader.CString();
        string engine = reader.CString();
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
        return (engine, result);
    }

    public static byte[] Write(string engine, IReadOnlyList<ArchiveNode> nodes)
    {
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
        WriteUInt(archive, Version);
        archive.Write(Encoding.ASCII.GetBytes("5.x.x\0" + engine + "\0"));
        long headerEnd = archive.Position + 8 + 12;
        long infoStart = Align16(headerEnd);
        long dataStart = Align16(infoStart + info.Length);
        WriteLong(archive, dataStart + size);
        WriteUInt(archive, (uint)info.Length);
        WriteUInt(archive, (uint)info.Length);
        WriteUInt(archive, CombinedInfo | PaddedInfo);
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
