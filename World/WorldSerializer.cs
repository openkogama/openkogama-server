using OpenKogama.Game;

namespace OpenKogama.World;

public sealed record Link(int Id, int From, int To);

public sealed record Snapshot(
    IReadOnlyList<Prototype> Prototypes,
    IReadOnlyList<WorldObject> Objects,
    IReadOnlyList<Link> Links,
    IReadOnlyList<Link> ObjectLinks);

// The packed world (BytePacker v11), the same bytes the client gets in GetGameBatch:
//   int32 prototypeCount    + records
//   int32 worldObjectCount  + records
//   int32 linkCount         + records
//   int32 objectLinkCount   + records
//   int32 runtimeEventCount + records
public static class WorldSerializer
{
    const byte HasOwner = 1;
    const byte HasPreviewOwner = 2;

    public static byte[] Write(Snapshot snapshot)
    {
        var writer = new BytePackerWriter();

        writer.WriteInt32(snapshot.Prototypes.Count);
        foreach (Prototype prototype in snapshot.Prototypes)
            WritePrototype(writer, prototype);

        writer.WriteInt32(snapshot.Objects.Count);
        foreach (WorldObject obj in snapshot.Objects)
            WriteObject(writer, obj);

        WriteLinks(writer, snapshot.Links);
        WriteLinks(writer, snapshot.ObjectLinks);
        writer.WriteInt32(0);   // runtime events

        return writer.ToArray();
    }

    public static byte[] Write(IReadOnlyList<Prototype> prototypes, IReadOnlyList<WorldObject> objects) =>
        Write(new Snapshot(prototypes, objects, [], []));

    public static Snapshot Read(byte[] data, bool runtime = true)
    {
        var reader = new BytePackerReader(data);

        var prototypes = new List<Prototype>();
        for (int i = reader.ReadInt32(); i > 0; i--)
            prototypes.Add(ReadPrototype(reader));

        var objects = new List<WorldObject>();
        for (int i = reader.ReadInt32(); i > 0; i--)
            objects.Add(ReadObject(reader, runtime));

        List<Link> links = ReadLinks(reader);
        List<Link> objectLinks = ReadLinks(reader);

        return new Snapshot(prototypes, objects, links, objectLinks);
    }

    static void WritePrototype(BytePackerWriter writer, Prototype prototype)
    {
        byte[] cubes = prototype.Cubes.ToBytes();

        writer.WriteInt32(prototype.Id);
        writer.WriteSingle(prototype.Scale);
        writer.WriteInt32(prototype.AuthorId);
        writer.WriteInt32(cubes.Length);
        writer.WriteBytes(cubes);
    }

    static Prototype ReadPrototype(BytePackerReader reader)
    {
        int id = reader.ReadInt32();
        float scale = reader.ReadSingle();
        int authorId = reader.ReadInt32();
        byte[] cubes = reader.ReadBytes(reader.ReadInt32());

        return new Prototype(id, scale, authorId, CubeModel.FromBytes(cubes));
    }

    static void WriteObject(BytePackerWriter writer, WorldObject obj)
    {
        writer.WriteInt32(obj.Id);
        writer.WriteInt32(obj.ParentId);
        writer.WriteInt32(obj.ItemId);
        writer.WriteInt32((int)obj.Type);

        writer.WriteVector3(obj.Position[0], obj.Position[1], obj.Position[2]);
        writer.WriteQuaternion(obj.Rotation[0], obj.Rotation[1], obj.Rotation[2], obj.Rotation[3]);
        writer.WriteVector3(obj.Scale[0], obj.Scale[1], obj.Scale[2]);

        writer.WritePairs(obj.Data);

        byte flags = 0;
        if (obj.Owner is not null) flags |= HasOwner;
        if (obj.PreviewOwner is not null) flags |= HasPreviewOwner;

        writer.WriteByte(flags);
        if (obj.Owner is int owner) writer.WriteInt32(owner);
        if (obj.PreviewOwner is int previewOwner) writer.WriteInt32(previewOwner);

        writer.WritePairs(obj.Runtime);
    }

    static WorldObject ReadObject(BytePackerReader reader, bool runtime)
    {
        var obj = new WorldObject
        {
            Id = reader.ReadInt32(),
            ParentId = reader.ReadInt32(),
            ItemId = reader.ReadInt32(),
            Type = (WorldObjectType)reader.ReadInt32(),
            Position = reader.ReadSingles(3),
            Rotation = reader.ReadSingles(4),
            Scale = reader.ReadSingles(3),
            Data = reader.ReadPairs(),
        };
        if (!runtime) return obj;

        byte flags = reader.ReadByte();
        if ((flags & HasOwner) != 0) obj.Owner = reader.ReadInt32();
        if ((flags & HasPreviewOwner) != 0) obj.PreviewOwner = reader.ReadInt32();

        obj.Runtime = reader.ReadPairs();
        return obj;
    }

    static void WriteLinks(BytePackerWriter writer, IReadOnlyList<Link> links)
    {
        writer.WriteInt32(links.Count);
        foreach (Link link in links)
        {
            writer.WriteInt32(link.Id);
            writer.WriteInt32(link.From);
            writer.WriteInt32(link.To);
        }
    }

    static List<Link> ReadLinks(BytePackerReader reader)
    {
        var links = new List<Link>();
        for (int i = reader.ReadInt32(); i > 0; i--)
            links.Add(new Link(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32()));
        return links;
    }
}
