using System.Text.Json;

namespace OpenKogama.Game;

public sealed class StreamingAsset
{
    public int Id { get; set; }
    public int Type { get; set; }
    public int Category { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Path { get; set; } = "";
}

public sealed class StreamingAssetCatalog
{
    public string Root { get; set; } = "";
    public List<StreamingAsset> Assets { get; set; } = [];

    public IEnumerable<StreamingAsset> OfTypes(object? requested) =>
        requested is int[] types && types.Length > 0 ? Assets.Where(asset => types.Contains(asset.Type)) : Assets;
}

public static class StreamingAssets
{
    static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };
    static readonly Dictionary<string, StreamingAssetCatalog> Cache = [];

    public static StreamingAssetCatalog For(string version)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(version, out StreamingAssetCatalog? cached)) return cached;

            string path = System.IO.Path.Combine(AppContext.BaseDirectory, "data", "streaming", version + ".json");
            StreamingAssetCatalog catalog = JsonSerializer.Deserialize<StreamingAssetCatalog>(File.ReadAllText(path), Options) ?? new();
            Cache[version] = catalog;
            return catalog;
        }
    }
}
