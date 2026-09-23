using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;

namespace OpenKogama.World;

public static class KgmapFile
{
    const uint Magic = 0x504D474B;
    const ushort Version = 6;

    public static (JsonObject Meta, List<byte[]> Batches) Read(string path)
    {
        using var file = File.OpenRead(path);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new BinaryReader(gzip, Encoding.UTF8);

        if (reader.ReadUInt32() != Magic) throw new InvalidDataException($"{path}: not a .kgmap");
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
