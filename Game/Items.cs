using System.Text.Json;

namespace OpenKogama.Game;

public sealed class Item
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public int Category { get; set; }
    public string Data { get; set; } = "";

    public byte[] Bytes => Convert.FromBase64String(Data);
}

public sealed class ItemCatalog
{
    public Dictionary<int, string> Categories { get; set; } = [];
    public List<Item> Items { get; set; } = [];

    public Item? Find(int id) => Items.Find(item => item.Id == id);
}

public static class Items
{
    static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };
    static readonly Dictionary<string, ItemCatalog> Cache = [];

    public static ItemCatalog For(string version)
    {
        if (Cache.TryGetValue(version, out ItemCatalog? cached)) return cached;

        string path = Path.Combine(AppContext.BaseDirectory, "data", "items", version + ".json");
        ItemCatalog catalog = JsonSerializer.Deserialize<ItemCatalog>(File.ReadAllText(path), Options) ?? new();
        Cache[version] = catalog;
        return catalog;
    }
}
