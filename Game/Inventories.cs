using System.Text.Json;

namespace OpenKogama.Game;

public sealed class Inventory
{
    public List<Item> Items { get; set; } = [];
    public Dictionary<int, int> Slots { get; set; } = [];
}

public static class Inventories
{
    const string Folder = "profiles";
    const int FirstOwnItemId = 900_000_000;

    static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    static readonly Dictionary<int, Inventory> Cache = [];
    static readonly object Sync = new();

    public static Inventory For(int profile)
    {
        lock (Sync)
        {
            if (Cache.TryGetValue(profile, out Inventory? cached)) return cached;

            string path = PathOf(profile);
            Inventory inventory = File.Exists(path)
                ? JsonSerializer.Deserialize<Inventory>(File.ReadAllText(path), Options) ?? new()
                : new();
            Cache[profile] = inventory;
            return inventory;
        }
    }

    public static Item? Find(int profile, int itemId) =>
        Items.For("2015").Find(itemId) ?? For(profile).Items.Find(item => item.Id == itemId);

    public static Item Add(int profile, string name, int category, byte[] data)
    {
        lock (Sync)
        {
            var item = new Item
            {
                Id = NextItemId(),
                Name = name,
                Category = category,
                Author = profile,
                Data = Convert.ToBase64String(data),
            };

            For(profile).Items.Add(item);
            Save(profile);
            return item;
        }
    }

    public static bool Remove(int profile, int itemId)
    {
        lock (Sync)
        {
            Inventory inventory = For(profile);
            if (inventory.Items.RemoveAll(item => item.Id == itemId) == 0) return false;
            inventory.Slots.Remove(itemId);
            Save(profile);
            return true;
        }
    }

    public static void SetSlots(int profile, Dictionary<int, int> slots)
    {
        lock (Sync)
        {
            For(profile).Slots = slots;
            Save(profile);
        }
    }

    public static List<(Item Item, int Slot)> WithSlots(int profile)
    {
        lock (Sync)
        {
            Inventory inventory = For(profile);
            List<Item> all = [.. Items.For("2015").Items, .. inventory.Items];
            var taken = all.GroupBy(item => item.Category).ToDictionary(
                group => group.Key,
                group => group.Where(item => inventory.Slots.ContainsKey(item.Id)).Select(item => inventory.Slots[item.Id]).ToHashSet());

            var result = new List<(Item, int)>();
            foreach (Item item in all)
            {
                if (!inventory.Slots.TryGetValue(item.Id, out int slot))
                {
                    HashSet<int> used = taken[item.Category];
                    slot = 0;
                    while (!used.Add(slot)) slot++;
                }
                result.Add((item, slot));
            }
            return result;
        }
    }

    static void Save(int profile) =>
        File.WriteAllText(PathOf(profile), JsonSerializer.Serialize(For(profile), Options));

    static int NextItemId()
    {
        Directory.CreateDirectory(Folder);
        string path = Path.Combine(Folder, "next-item-id");
        int next = File.Exists(path) && int.TryParse(File.ReadAllText(path), out int saved) ? saved : FirstOwnItemId;
        File.WriteAllText(path, (next + 1).ToString());
        return next;
    }

    static string PathOf(int profile)
    {
        Directory.CreateDirectory(Folder);
        return Path.Combine(Folder, $"{profile}.json");
    }
}
