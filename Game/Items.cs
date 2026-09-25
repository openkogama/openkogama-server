using System.Text.Json;

namespace OpenKogama.Game;

public sealed class Item
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public int Category { get; set; }
    public int Author { get; set; }
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
    static readonly JsonSerializerOptions WriteOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    static readonly Dictionary<string, ItemCatalog> Cache = [];
    static readonly object Sync = new();

    public static Item AddBuiltIn(string version, string name, int category, byte[] data, bool overwrite)
    {
        lock (Sync)
        {
            ItemCatalog catalog = For(version);
            Item? item = overwrite ? catalog.Items.Find(existing => existing.Name == name && existing.Category == category) : null;

            if (item is null)
            {
                int first = category * 100 + 1;
                int id = first;
                while (catalog.Find(id) is not null) id++;
                item = new Item { Id = id, Name = name, Category = category };
                catalog.Items.Add(item);
            }

            item.Data = Convert.ToBase64String(data);
            string path = Path.Combine(AppContext.BaseDirectory, "data", "items", version + ".json");
            File.WriteAllText(path, JsonSerializer.Serialize(new { catalog.Categories, Items = catalog.Items.Select(i => new { i.Id, i.Name, i.Description, i.Category, i.Data }) }, WriteOptions));
            return item;
        }
    }

    public static ItemCatalog For(string version)
    {
        if (Cache.TryGetValue(version, out ItemCatalog? cached)) return cached;

        string path = Path.Combine(AppContext.BaseDirectory, "data", "items", version + ".json");
        ItemCatalog catalog = JsonSerializer.Deserialize<ItemCatalog>(File.ReadAllText(path), Options) ?? new();
        Cache[version] = catalog;
        return catalog;
    }
}
