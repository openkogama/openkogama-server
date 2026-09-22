using System.Text.Json;

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
}
