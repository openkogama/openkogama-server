using System.Text.Json;

namespace OpenKogama.Api;

public sealed record AvatarSkinPart(string Bone, float Scale, string Cubes);

public sealed record AvatarSkinAccessory(int Item, int Slot, float Offset, float Scale);

public sealed record AvatarSkin(IReadOnlyList<AvatarSkinPart> Parts, IReadOnlyList<AvatarSkinAccessory> Accessories)
{
    static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    public static AvatarSkin FromJson(string json) =>
        JsonSerializer.Deserialize<AvatarSkin>(json, Options) ?? throw new InvalidDataException("not an avatar skin");
}
