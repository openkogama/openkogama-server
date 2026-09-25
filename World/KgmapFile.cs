using System.IO.Compression;
using System.Text;
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
        using Stream input = file[0] == 0x1f && file[1] == 0x8b
            ? new GZipStream(new MemoryStream(file), CompressionMode.Decompress)
            : new MemoryStream(file);
        using var reader = new BinaryReader(input, Encoding.UTF8);

        if (reader.ReadUInt32() != Magic) throw new InvalidDataException("not a .kgmap");
        ushort version = reader.ReadUInt16();

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

    public static void Write(string path, JsonObject meta, byte[] batch)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

        string temp = path + ".tmp";
        using (var file = File.Create(temp))
        using (var gzip = new GZipStream(file, CompressionLevel.Optimal))
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

        File.Move(temp, path, overwrite: true);
    }
}
