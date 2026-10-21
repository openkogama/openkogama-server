using System.Text;
using System.Text.Json;
using OpenKogama.Api;
using OpenKogama.Storage;
using OpenKogama.World;

namespace OpenKogama.Game;

public static class AvatarFiles
{
    public static AvatarSkin? Export(int avatar)
    {
        if (Stores.Profiles.AvatarParts(avatar) is not { } parts) return null;
        return new AvatarSkin(
            [.. parts.Select(part => new AvatarSkinPart(part.Bone, part.Scale, part.Cubes))],
            [.. Stores.Profiles.Accessories(avatar).Select(worn => new AvatarSkinAccessory(worn.Item, worn.Slot, worn.Offset, worn.Scale))]);
    }

    public static int? Import(int profile, byte[] file)
    {
        (string? name, AvatarSkin? skin) = RegamaFile.Read(file, RegamaFile.Avatar) is (var title, Snapshot snapshot)
            ? (title, FromSnapshot(snapshot))
            : (null, FromJson(file));
        if (skin is null || !Valid(skin)) return null;

        int avatar = Stores.Profiles.AddAvatar(profile, null);
        Write(avatar, skin, item => item);
        if (name is not null) Stores.Profiles.RenameAvatar(profile, avatar, name);
        return avatar;
    }

    static AvatarSkin? FromJson(byte[] file)
    {
        try
        {
            return AvatarSkin.FromJson(Encoding.UTF8.GetString(file));
        }
        catch (Exception e) when (e is JsonException or InvalidDataException or NotSupportedException)
        {
            return null;
        }
    }

    static AvatarSkin? FromSnapshot(Snapshot snapshot)
    {
        foreach (WorldObject body in snapshot.Objects)
        {
            if (Table(body.Data, "BlueprintData") is not { } blueprint || Table(blueprint, "ChildrenMap") is not { } children
                || blueprint.Find(pair => pair.Key == "ClientSideType").Value is not byte type || type != (byte)BlueprintType.Body) continue;

            var parts = new List<AvatarSkinPart>();
            foreach ((string bone, _, object id) in children)
                if (snapshot.Objects.FirstOrDefault(obj => id is int partId && obj.Id == partId)?.PrototypeId is int prototypeId
                    && snapshot.Prototypes.FirstOrDefault(prototype => prototype.Id == prototypeId) is Prototype prototype)
                    parts.Add(new AvatarSkinPart(bone, prototype.Scale, Convert.ToBase64String(prototype.Cubes.ToBytes())));

            var accessories = new List<AvatarSkinAccessory>();
            foreach ((_, _, object value) in Table(blueprint, "3") ?? [])
                if (value is List<(string Key, PackedType Type, object Value)> worn
                    && worn.Find(pair => pair.Key == "4").Value is string path && StreamingAssets.FindAccessory(path) is { } asset
                    && worn.Find(pair => pair.Key == "2").Value is int slot)
                    accessories.Add(new AvatarSkinAccessory(asset.Id, slot,
                        worn.Find(pair => pair.Key == "3").Value as float? ?? 0f,
                        worn.Find(pair => pair.Key == "7").Value as float? ?? 1f));

            return new AvatarSkin(parts, accessories);
        }
        return null;
    }

    static List<(string Key, PackedType Type, object Value)>? Table(List<(string Key, PackedType Type, object Value)> pairs, string key) =>
        pairs.Find(pair => pair.Key == key).Value as List<(string Key, PackedType Type, object Value)>;

    public static void Wear(int profile, AvatarSkin skin, Func<int, int?> item)
    {
        if (!Valid(skin)) return;
        int avatar = Stores.Profiles.ResetPlayerAvatar(profile);
        Write(avatar, skin, item);
        Stores.Profiles.SetActiveAvatar(profile, avatar);
    }

    static bool Valid(AvatarSkin skin)
    {
        HashSet<string> bones = [.. Avatar.Parts.Select(part => part.Bone)];
        return skin.Parts is { Count: > 0 } && skin.Parts.All(part => bones.Contains(part.Bone) && Readable(part.Cubes));
    }

    static void Write(int avatar, AvatarSkin skin, Func<int, int?> item)
    {
        Stores.Profiles.SaveAvatar(avatar, [.. skin.Parts.Select(part => new AvatarPart { Bone = part.Bone, Scale = part.Scale, Cubes = part.Cubes })]);
        foreach (AvatarSkinAccessory worn in skin.Accessories ?? [])
            if (item(worn.Item) is int mapped)
                Stores.Profiles.SetAccessory(avatar, mapped, worn.Slot, worn.Offset, worn.Scale);
    }

    static bool Readable(string cubes)
    {
        try
        {
            return CubeModel.FromBytes(Convert.FromBase64String(cubes)).Count > 0;
        }
        catch (Exception e) when (e is FormatException or ArgumentException or IndexOutOfRangeException)
        {
            return false;
        }
    }
}
