using System.Text.Json;

namespace OpenKogama.Game;

public static class Materials
{
    static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };
    static readonly Dictionary<string, List<Material>> Cache = [];

    public static List<Material> For(string version)
    {
        if (Cache.TryGetValue(version, out List<Material>? cached)) return cached;

        string path = Path.Combine(AppContext.BaseDirectory, "data", "materials", version + ".json");
        List<Material> list = JsonSerializer.Deserialize<List<Material>>(File.ReadAllText(path), Options) ?? [];
        Cache[version] = list;
        return list;
    }
}
