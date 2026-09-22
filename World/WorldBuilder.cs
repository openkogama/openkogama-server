using OpenKogama.Game;

namespace OpenKogama.World;

// World object types, from MV.WorldObject.WorldObjectType.
public enum WorldObjectType
{
    Avatar = 0,
    CubeModel = 1,
    PointLight = 2,
    TriggerBox = 3,
    Mover = 4,
    Path = 5,
    PathNode = 6,
    SpawnPoint = 7,
    CubeModelPrototypeTerrain = 8,
    CubeModelTerrainFineGrained = 32,
    Group = 9,
    Blueprint = 45,
}

// Blueprint kinds, from MV.Common.BlueprintType, picked by Data.BlueprintData.ClientSideType.
public enum BlueprintType : byte
{
    Movable = 7,
    Body = 8,
    Teleporter = 9,
}

// Builds the packed world the client receives when it joins.
//   int32 prototypeCount    + records
//   int32 worldObjectCount  + records
//   int32 linkCount         + records
//   int32 objectLinkCount   + records
//   int32 runtimeEventCount + records
public static class WorldBuilder
{
    static List<AvatarPart> Parts => Avatar.Parts;

    public const int RootId = 1;
    const int TerrainId = 2;
    const int FineTerrainId = 3;

    const int BodyPrototypeId = 1;
    const int TerrainPrototypeId = 100;
    const int FineTerrainPrototypeId = 101;

    const int TerrainSize = 32;

    // The full world for a joining player: shared terrain plus every player's avatar.
    public static byte[] BuildWorld(IReadOnlyList<Player> players)
    {
        var writer = new BytePackerWriter();

        writer.WriteInt32(2 + Parts.Count);   // prototypes, shared by all avatars
        WriteTerrainPrototype(writer, TerrainPrototypeId, material: 0);
        WriteEmptyPrototype(writer, FineTerrainPrototypeId);
        for (int i = 0; i < Parts.Count; i++)
            WriteCubeModelPrototype(writer, BodyPrototypeId + i, Parts[i].Scale, Parts[i].CubeData);

        int perAvatar = 2 + Parts.Count;
        writer.WriteInt32(3 + players.Count * perAvatar);   // world objects
        WriteGroup(writer, RootId);
        WriteTerrain(writer, TerrainId, RootId);
        WriteFineTerrain(writer, FineTerrainId, RootId);
        foreach (Player player in players)
            WriteAvatarSubtree(writer, player.Actor, player.AvatarId, RootId);

        writer.WriteInt32(0);   // links
        writer.WriteInt32(0);   // object links
        writer.WriteInt32(0);   // runtime events

        return writer.ToArray();
    }

    // One avatar subtree, sent to existing players so they spawn the newcomer.
    // No prototypes: the receiver already has them from its own world load.
    public static byte[] BuildAvatarAddition(Player player)
    {
        var writer = new BytePackerWriter();

        writer.WriteInt32(0);                 // prototypes
        writer.WriteInt32(2 + Parts.Count);   // this avatar subtree only
        WriteAvatarSubtree(writer, player.Actor, player.AvatarId, RootId);

        writer.WriteInt32(0);
        writer.WriteInt32(0);
        writer.WriteInt32(0);

        return writer.ToArray();
    }

    static void WriteAvatarSubtree(BytePackerWriter writer, int actor, int avatarId, int parentId)
    {
        int bodyId = avatarId + 1;
        int firstPartId = avatarId + 2;

        WriteAvatar(writer, actor, avatarId, parentId);
        WriteBody(writer, actor, bodyId, avatarId, firstPartId);
        for (int i = 0; i < Parts.Count; i++)
            WriteBodyPart(writer, actor, firstPartId + i, bodyId, BodyPrototypeId + i);
    }

    static void WriteGroup(BytePackerWriter writer, int objectId)
    {
        writer.WriteInt32(objectId);
        writer.WriteInt32(-1);                               // no parent, this is the root
        writer.WriteInt32(0);
        writer.WriteInt32((int)WorldObjectType.Group);

        writer.WriteVector3(0f, 0f, 0f);
        writer.WriteQuaternion(0f, 0f, 0f, 1f);
        writer.WriteVector3(1f, 1f, 1f);

        writer.WritePairs([]);
        writer.WriteByte(1);
        writer.WriteInt32(0);                                // server owned
        writer.WritePairs([]);
    }

    static void WriteAvatar(BytePackerWriter writer, int actorNumber, int objectId, int parentId)
    {
        writer.WriteInt32(objectId);
        writer.WriteInt32(parentId);
        writer.WriteInt32(0);
        writer.WriteInt32((int)WorldObjectType.Avatar);

        writer.WriteVector3(0f, 2f, 0f);
        writer.WriteQuaternion(0f, 0f, 0f, 1f);
        writer.WriteVector3(1f, 1f, 1f);

        writer.WritePairs([]);

        writer.WriteByte(1);                                 // owner flags: actor number follows
        writer.WriteInt32(actorNumber);

        writer.WritePairs(
        [
            ("health", PackedType.Single, 100f),
            ("isFiring", PackedType.Bool, false),
            ("modifiers", PackedType.Hashtable, (IReadOnlyList<(string, PackedType, object)>)[]),
            ("currentItem", PackedType.Hashtable, (IReadOnlyList<(string, PackedType, object)>)[]),
            ("invulnerable", PackedType.Bool, false),
            ("avatarRuntimeState", PackedType.Byte, (byte)1),
            ("animation", PackedType.Hashtable, (IReadOnlyList<(string, PackedType, object)>)[]),
            ("seat", PackedType.Int32, -1),
        ]);
    }

    static void WriteCubeModelPrototype(BytePackerWriter writer, int prototypeId, float scale, byte[] cubes)
    {
        writer.WriteInt32(prototypeId);
        writer.WriteSingle(scale);
        writer.WriteInt32(1);          // author profile id

        writer.WriteInt32(cubes.Length);
        foreach (byte value in cubes) writer.WriteByte(value);
    }

    static void WriteTerrainPrototype(BytePackerWriter writer, int prototypeId, byte material)
    {
        writer.WriteInt32(prototypeId);
        writer.WriteSingle(1f);
        writer.WriteInt32(1);

        var cubes = new BytePackerWriter();
        cubes.WriteInt32(TerrainSize);          // one run per row

        for (int z = 0; z < TerrainSize; z++)
        {
            cubes.WriteInt16(0);                // run starts at x 0
            cubes.WriteInt16(-1);               // one cube below the avatar
            cubes.WriteInt16((short)z);
            cubes.WriteByte((TerrainSize << 2) | 1 | 2);
            cubes.WriteByte(material);
        }

        byte[] data = cubes.ToArray();
        writer.WriteInt32(data.Length);
        foreach (byte value in data) writer.WriteByte(value);
    }

    static void WriteFineTerrain(BytePackerWriter writer, int objectId, int parentId)
    {
        writer.WriteInt32(objectId);
        writer.WriteInt32(parentId);
        writer.WriteInt32(0);
        writer.WriteInt32((int)WorldObjectType.CubeModelTerrainFineGrained);

        writer.WriteVector3(0f, 0f, 0f);
        writer.WriteQuaternion(0f, 0f, 0f, 1f);
        writer.WriteVector3(1f, 1f, 1f);

        writer.WritePairs([("protoTypeID", PackedType.Int32, FineTerrainPrototypeId)]);

        writer.WriteByte(1);
        writer.WriteInt32(0);
        writer.WritePairs([]);
    }

    static void WriteEmptyPrototype(BytePackerWriter writer, int prototypeId)
    {
        writer.WriteInt32(prototypeId);
        writer.WriteSingle(1f);
        writer.WriteInt32(1);

        var cubes = new BytePackerWriter();
        cubes.WriteInt32(0);

        byte[] data = cubes.ToArray();
        writer.WriteInt32(data.Length);
        foreach (byte value in data) writer.WriteByte(value);
    }

    static void WriteTerrain(BytePackerWriter writer, int objectId, int parentId)
    {
        writer.WriteInt32(objectId);
        writer.WriteInt32(parentId);
        writer.WriteInt32(0);
        writer.WriteInt32((int)WorldObjectType.CubeModelPrototypeTerrain);

        writer.WriteVector3(0f, 0f, 0f);
        writer.WriteQuaternion(0f, 0f, 0f, 1f);
        writer.WriteVector3(1f, 1f, 1f);

        writer.WritePairs([("protoTypeID", PackedType.Int32, TerrainPrototypeId)]);

        writer.WriteByte(1);
        writer.WriteInt32(0);
        writer.WritePairs([]);
    }

    static IReadOnlyList<(string, PackedType, object)> BuildChildrenMap(int firstPartId) =>
        [.. Parts.Select((part, index) => (part.Bone, PackedType.Int32, (object)(firstPartId + index)))];

    static void WriteBodyPart(BytePackerWriter writer, int actorNumber, int objectId, int bodyObjectId, int prototypeId)
    {
        writer.WriteInt32(objectId);
        writer.WriteInt32(bodyObjectId);
        writer.WriteInt32(0);
        writer.WriteInt32((int)WorldObjectType.CubeModel);

        writer.WriteVector3(0f, 0f, 0f);
        writer.WriteQuaternion(0f, 0f, 0f, 1f);
        writer.WriteVector3(1f, 1f, 1f);

        writer.WritePairs([("protoTypeID", PackedType.Int32, prototypeId)]);

        writer.WriteByte(1);
        writer.WriteInt32(actorNumber);
        writer.WritePairs([]);
    }

    static void WriteBody(BytePackerWriter writer, int actorNumber, int objectId, int avatarObjectId, int firstPartId)
    {
        writer.WriteInt32(objectId);
        writer.WriteInt32(avatarObjectId);                   // parented to the avatar
        writer.WriteInt32(0);
        writer.WriteInt32((int)WorldObjectType.Blueprint);

        writer.WriteVector3(0f, 0f, 0f);
        writer.WriteQuaternion(0f, 0f, 0f, 1f);
        writer.WriteVector3(1f, 1f, 1f);

        writer.WritePairs(
        [
            ("BlueprintData", PackedType.Hashtable, (IReadOnlyList<(string, PackedType, object)>)
            [
                ("ClientSideType", PackedType.Byte, (byte)BlueprintType.Body),
                ("ChildrenMap", PackedType.Hashtable, BuildChildrenMap(firstPartId)),
            ]),
        ]);

        writer.WriteByte(1);
        writer.WriteInt32(actorNumber);
        writer.WritePairs([]);
    }
}
