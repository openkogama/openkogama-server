using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenKogama.World;

public static class KgmapFile
{
    const uint Magic = 0x504D474B;
    const ushort Version = 6;

    public static bool IsKgmap(byte[] file) =>
        file.Length >= 4 && (file[0] == 0x1f && file[1] == 0x8b || BitConverter.ToUInt32(file, 0) == Magic);

    public static (JsonObject Meta, List<byte[]> Batches) Read(byte[] file)
    {
        using Stream input = Open(file);
        using var reader = new BinaryReader(input, Encoding.UTF8);

        if (reader.ReadUInt32() != Magic) throw new InvalidDataException("not a .kgmap");
        ushort version = reader.ReadUInt16();
        if (LegacyKgmap.IsLegacy(version)) return LegacyKgmap.Read(reader, version);

        JsonObject meta = [];
        if (version >= 6)
        {
            byte[] json = reader.ReadBytes(reader.ReadInt32());
            meta = JsonNode.Parse(json) as JsonObject ?? [];
        }

        var batches = new List<byte[]>();
        for (int i = reader.ReadInt32(); i > 0; i--)
            batches.Add(reader.ReadBytes(reader.ReadInt32()));

        return (meta, batches);
    }

    static Stream Open(byte[] file) => file[0] == 0x1f && file[1] == 0x8b
        ? new GZipStream(new MemoryStream(file), CompressionMode.Decompress)
        : new MemoryStream(file);

    static ushort? FileVersion(byte[] file)
    {
        using Stream input = Open(file);
        using var reader = new BinaryReader(input);
        try
        {
            return reader.ReadUInt32() == Magic ? reader.ReadUInt16() : null;
        }
        catch (Exception error) when (error is EndOfStreamException or InvalidDataException)
        {
            return null;
        }
    }

    public static string? Title(byte[] file)
    {
        if (!IsKgmap(file) || FileVersion(file) is not ushort version || LegacyKgmap.IsLegacy(version)) return null;
        try
        {
            string? title = Read(file).Meta["GameTitle"]?.GetValue<string>();
            for (string? previous = null; title is not null && title != previous;)
            {
                previous = title;
                title = WebUtility.HtmlDecode(title);
            }
            return string.IsNullOrWhiteSpace(title) ? null : title.Trim();
        }
        catch (Exception error) when (error is InvalidDataException or EndOfStreamException or JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    public static void Write(string path, JsonObject meta, byte[] batch)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

        string temp = path + ".tmp";
        File.WriteAllBytes(temp, Pack(meta, batch));
        File.Move(temp, path, overwrite: true);
    }

    public static byte[] Pack(JsonObject meta, byte[] batch)
    {
        var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
        using (var writer = new BinaryWriter(gzip, Encoding.UTF8))
        {
            byte[] json = Encoding.UTF8.GetBytes(meta.ToJsonString());

            writer.Write(Magic);
            writer.Write(Version);
            writer.Write(json.Length);
            writer.Write(json);
            writer.Write(1);
            writer.Write(batch.Length);
            writer.Write(batch);
        }
        return output.ToArray();
    }
}
