using System.Text.Json;
using OpenKogama.World;

namespace OpenKogama.Game;

public static class Avatar
{
    static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };
    static List<AvatarPart>? _parts;

    public static List<AvatarPart> Parts
    {
        get
        {
            if (_parts is not null) return _parts;
            string path = Path.Combine(AppContext.BaseDirectory, "data", "avatar.json");
            _parts = JsonSerializer.Deserialize<List<AvatarPart>>(File.ReadAllText(path), Options) ?? [];
            return _parts;
        }
    }

    public static int[] AddPrototypes(GameWorld world) =>
    [
        .. Parts.Select(part =>
        {
            var prototype = new Prototype(world.NewPrototypeId(), part.Scale, 1, CubeModel.FromBytes(part.CubeData));
            world.Add(prototype);
            return prototype.Id;
        }),
    ];

    public static List<WorldObject> Build(GameWorld world, int actor, int parentId, int[] partPrototypes)
    {
        var avatar = new WorldObject
        {
            Id = world.NewObjectId(),
            ParentId = parentId,
            Type = WorldObjectType.Avatar,
            Position = [.. world.Spawn],
            Owner = actor,
            Runtime =
            [
                ("health", PackedType.Single, 100f),
                ("isFiring", PackedType.Bool, false),
                ("modifiers", PackedType.Hashtable, new List<(string, PackedType, object)>()),
                ("currentItem", PackedType.Hashtable, new List<(string, PackedType, object)>()),
                ("invulnerable", PackedType.Bool, false),
                ("avatarRuntimeState", PackedType.Byte, (byte)1),
                ("animation", PackedType.Hashtable, new List<(string, PackedType, object)>()),
                ("seat", PackedType.Int32, -1),
            ],
        };

        int bodyId = world.NewObjectId();
        var parts = Parts.Select((part, i) => new WorldObject
        {
            Id = world.NewObjectId(),
            ParentId = bodyId,
            Type = WorldObjectType.CubeModel,
            Data = [("protoTypeID", PackedType.Int32, partPrototypes[i])],
            Owner = actor,
        }).ToList();

        var body = new WorldObject
        {
            Id = bodyId,
            ParentId = avatar.Id,
            Type = WorldObjectType.Blueprint,
            Data =
            [
                ("BlueprintData", PackedType.Hashtable, new List<(string, PackedType, object)>
                {
                    ("ClientSideType", PackedType.Byte, (byte)BlueprintType.Body),
                    ("ChildrenMap", PackedType.Hashtable, Parts.Select((part, i) =>
                        (part.Bone, PackedType.Int32, (object)parts[i].Id)).ToList()),
                }),
            ],
            Owner = actor,
        };

        return [avatar, body, .. parts];
    }
}
