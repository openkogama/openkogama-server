using System.Text.Json;
using OpenKogama.Kogama;
using OpenKogama.Photon;

namespace OpenKogama.Game;

public sealed class StreamingAsset
{
    public int Id { get; set; }
    public int Type { get; set; }
    public int Category { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Path { get; set; } = "";
    public int RentSeconds { get; set; }
    public string Since { get; set; } = "";

    public PhotonDictionary Describe()
    {
        PhotonDictionary entry = PhotonDictionary.Untyped();
        entry.Add((byte)DBQueryKey.StreamingAssetTypeID, Type);
        entry.Add((byte)DBQueryKey.StreamingAssetCategoryID, Category);
        entry.Add((byte)DBQueryKey.StreamingAssetName, Name);
        entry.Add((byte)DBQueryKey.StreamingAssetDescription, Description);
        entry.Add((byte)DBQueryKey.StreamingAssetURL, Path);
        entry.Add((byte)DBQueryKey.PriceGold, 0);
        entry.Add((byte)DBQueryKey.PriceSilver, 0);
        entry.Add((byte)DBQueryKey.RentPriceSilver, 0);
        entry.Add((byte)DBQueryKey.RentPriceGold, 0);
        entry.Add((byte)DBQueryKey.RentExpireSeconds, RentSeconds);
        return entry;
    }
}

public sealed class StreamingAssetCatalog
{
    public string Root { get; set; } = "";
    public string? WebGLRoot { get; set; }
    public List<StreamingAsset> Assets { get; set; } = [];

    HashSet<int>? _ids;

    public bool Has(int id) => (_ids ??= [.. Assets.Select(asset => asset.Id)]).Contains(id);

    public IEnumerable<StreamingAsset> OfTypes(object? requested) =>
        requested is int[] types && types.Length > 0 ? Assets.Where(asset => types.Contains(asset.Type)) : Assets;
}

public static class StreamingAssets
{
    const int AccessoryType = 2;
    const string Accessories = "accessories";

    public static StreamingAsset? Find(int id) =>
        For(Accessories).Assets.Find(asset => asset.Id == id) ?? For("2015").Assets.Find(asset => asset.Id == id);

    public static StreamingAssetCatalog ForSet(string set)
    {
        StreamingAssetCatalog common = For("2015");
        StreamingAssetCatalog accessories = For(Accessories);
        lock (Cache)
        {
            string key = "set " + set;
            if (Cache.TryGetValue(key, out StreamingAssetCatalog? cached)) return cached;

            int number = SetNumber(set);
            var catalog = new StreamingAssetCatalog
            {
                Root = common.Root,
                Assets = [.. common.Assets.Where(asset => asset.Type != AccessoryType), .. accessories.Assets.Where(asset => SetNumber(asset.Since) <= number)],
            };
            Cache[key] = catalog;
            return catalog;
        }
    }

    static int SetNumber(string set) => int.TryParse(set.TrimStart('v'), out int number) ? number : 0;

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
