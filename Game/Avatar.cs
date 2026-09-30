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

    public static void UseParts(GameWorld world, int bodyId, List<AvatarPart> parts)
    {
        if (Children(world, bodyId) is not { } children) return;

        foreach (AvatarPart part in parts)
        {
            if (children.Find(child => child.Key == part.Bone).Value is not int partId) continue;

            var prototype = new Prototype(world.NewPrototypeId(), part.Scale, 1, CubeModel.FromBytes(part.CubeData));
            world.Add(prototype);
            world.Modify(partId, obj =>
            {
                obj.Data.RemoveAll(pair => pair.Key == "protoTypeID");
                obj.Data.Add(("protoTypeID", PackedType.Int32, prototype.Id));
            });
        }
    }

    public static void WearAccessories(GameWorld world, int bodyId, List<WornAccessory> worn)
    {
        var accessories = new List<(string Key, PackedType Type, object Value)>();
        foreach (WornAccessory accessory in worn)
        {
            if (StreamingAssets.Find(accessory.Item) is not { } asset) continue;
            accessories.Add((accessory.Item.ToString(), PackedType.Hashtable, new List<(string Key, PackedType Type, object Value)>
            {
                ("1", PackedType.Int32, accessory.Item),
                ("2", PackedType.Int32, accessory.Slot),
                ("3", PackedType.Single, accessory.Offset),
                ("4", PackedType.String, asset.Path),
                ("5", PackedType.Int64, DateTime.Now.Ticks),
                ("6", PackedType.Int32, 0),
                ("7", PackedType.Single, accessory.Scale),
            }));
        }

        world.Modify(bodyId, body =>
        {
            if (body.Data.Find(pair => pair.Key == "BlueprintData").Value is not List<(string Key, PackedType Type, object Value)> blueprint) return;
            blueprint.RemoveAll(pair => pair.Key == "3");
            blueprint.Add(("3", PackedType.Hashtable, accessories));
        });
    }

    public static List<AvatarPart> ReadParts(GameWorld world, int bodyId)
    {
        var parts = new List<AvatarPart>();
        if (Children(world, bodyId) is not { } children) return parts;

        foreach ((string bone, _, object id) in children)
        {
            if (world.Find((int)id)?.PrototypeId is not int prototypeId || world.FindPrototype(prototypeId) is not { } prototype) continue;
            parts.Add(new AvatarPart { Bone = bone, Scale = prototype.Scale, Cubes = Convert.ToBase64String(prototype.Cubes.ToBytes()) });
        }
        return parts;
    }

    static List<(string Key, PackedType Type, object Value)>? Children(GameWorld world, int bodyId) =>
        world.Find(bodyId)?.Data.Find(pair => pair.Key == "BlueprintData").Value is List<(string Key, PackedType Type, object Value)> blueprint
            ? blueprint.Find(pair => pair.Key == "ChildrenMap").Value as List<(string Key, PackedType Type, object Value)>
            : null;

    public const int Playing = 1;
    public const int Hidden = 4;

    public static List<WorldObject> Build(GameWorld world, int actor, int parentId, int[] partPrototypes, WorldObjectType type = WorldObjectType.Avatar)
    {
        var avatar = new WorldObject
        {
            Id = world.NewObjectId(),
            ParentId = parentId,
            Type = type,
            Position = [.. world.Spawn],
            Owner = actor,
            Runtime =
            [
                ("health", PackedType.Single, 100f),
                ("shield", PackedType.Single, 0f),
                ("isFiring", PackedType.Bool, false),
                ("modifiers", PackedType.Hashtable, new List<(string, PackedType, object)>()),
                ("currentItem", PackedType.Hashtable, new List<(string, PackedType, object)> { ("type", PackedType.Int32, (int)Kogama.AvatarItemType.Hand) }),
                ("invulnerable", PackedType.Bool, false),
                ("avatarRuntimeState", PackedType.Byte, (byte)1),
                ("avatarModeTypes", PackedType.Int32, 1),
                ("animation", PackedType.Hashtable, new List<(string, PackedType, object)>()),
                ("seat", PackedType.Int32, -1),
                ("maxHealth", PackedType.Single, 100f),
                ("spawnRoleModeType", PackedType.Int32, Playing),
                ("headRotationYaw", PackedType.Single, 0f),
                ("headRotationPitch", PackedType.Single, 0f),
                ("pointRotationYaw", PackedType.Single, 0f),
                ("pointRotationPitch", PackedType.Single, 0f),
                ("emote", PackedType.Int32, 0),
            ],
        };

        return [avatar, .. BuildBody(world, actor, avatar.Id, partPrototypes)];
    }

    public static byte[] DefaultBody(int actor)
    {
        var world = new GameWorld();
        foreach (WorldObject obj in BuildBody(world, actor, -1, AddPrototypes(world))) world.Add(obj);
        return WorldSerializer.Write(world.ToSnapshot(), runtime: false);
    }

    public static List<WorldObject> BuildBody(GameWorld world, int actor, int parentId, int[] partPrototypes)
    {
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
            ParentId = parentId,
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

        return [body, .. parts];
    }
}
