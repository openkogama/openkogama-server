using System.Text.Json;
using OpenKogama.Api;
using OpenKogama.Storage;

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

    public static int? Import(int profile, string json)
    {
        AvatarSkin skin;
        try
        {
            skin = AvatarSkin.FromJson(json);
        }
        catch (Exception e) when (e is JsonException or InvalidDataException or NotSupportedException)
        {
            return null;
        }

        HashSet<string> bones = [.. Avatar.Parts.Select(part => part.Bone)];
        if (skin.Parts is not { Count: > 0 } || skin.Parts.Any(part => !bones.Contains(part.Bone) || !Readable(part.Cubes))) return null;

        int avatar = Stores.Profiles.AddAvatar(profile, null);
        Stores.Profiles.SaveAvatar(avatar, [.. skin.Parts.Select(part => new AvatarPart { Bone = part.Bone, Scale = part.Scale, Cubes = part.Cubes })]);
        foreach (AvatarSkinAccessory worn in skin.Accessories ?? [])
            Stores.Profiles.SetAccessory(avatar, worn.Item, worn.Slot, worn.Offset, worn.Scale);
        return avatar;
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
