using System.Text.Json;
using OpenKogama.Kogama.Protocols;
using OpenKogama.World;

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
    const string CatalogFile = "all";
    const string ReferenceVersion = "2.30.6";
    const int NewerItems = 400_000;

    static readonly System.Text.RegularExpressions.Regex Placeholder = new(@"^(Caspar|Christian|Jakob|Thomas|Mathias|Carl)\d+$");

    static readonly Dictionary<string, ItemCatalog> Cache = [];
    static readonly Dictionary<string, ItemCatalog> ClientCache = [];
    static readonly object Sync = new();

    public static ItemCatalog Catalog => For(CatalogFile);

    public static ItemCatalog ForClient(string clientVersion)
    {
        lock (Sync)
        {
            if (ClientCache.TryGetValue(clientVersion, out ItemCatalog? cached)) return cached;

            ProtocolTable client = ProtocolTable.For(clientVersion);
            var supported = Catalog.Items.Where(item => Supports(item, client)).OrderBy(item => item.Id).ToList();
            var names = new HashSet<(string, int)>();
            var items = new List<Item>();
            foreach (Item item in supported)
                if (names.Add((item.Name, item.Category)) || item.Id < NewerItems)
                    items.Add(item);
            return ClientCache[clientVersion] = new ItemCatalog { Categories = Catalog.Categories, Items = items };
        }
    }

    static bool Supports(Item item, ProtocolTable client)
    {
        Snapshot snapshot;
        try
        {
            snapshot = WorldSerializer.Read(item.Bytes, runtime: false);
        }
        catch (Exception error) when (error is FormatException or EndOfStreamException or ArgumentException or InvalidDataException)
        {
            return false;
        }

        ProtocolTable server = ProtocolTable.For(ClientProtocols.ServerVersion);
        ProtocolTable reference = ProtocolTable.For(ReferenceVersion);
        return snapshot.Objects.All(obj =>
            Known((int)obj.Type, client.WorldObjectType, server.WorldObjectType, reference.WorldObjectType)
            && (obj.Data.Find(pair => pair.Key == "itemType").Value is not int itemType
                || Known(itemType, client.AvatarItemType, server.AvatarItemType, reference.AvatarItemType)));
    }

    static bool Known(int code, Dictionary<string, int> client, Dictionary<string, int> server, Dictionary<string, int> reference)
    {
        string? name = client.FirstOrDefault(pair => pair.Value == code).Key;
        if (name is null || Placeholder.IsMatch(name)) return false;
        return server.ContainsValue(code) || reference.GetValueOrDefault(name, -1) == code;
    }

    public static Item AddBuiltIn(string name, int category, byte[] data, bool overwrite)
    {
        lock (Sync)
        {
            string version = CatalogFile;
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
            ClientCache.Clear();
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
