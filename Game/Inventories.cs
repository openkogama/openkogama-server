using OpenKogama.Storage;

namespace OpenKogama.Game;

public static class Inventories
{
    public static Item? Find(int profile, int itemId, string version = Kogama.Protocols.ClientProtocols.ServerVersion) =>
        Items.ForClient(version).Find(itemId) ?? Stores.Profiles.Items(profile).Find(item => item.Id == itemId);

    public static Item Add(int profile, string name, int category, byte[] data, int author) =>
        Stores.Profiles.AddItem(profile, name, category, data, author);

    public static bool Remove(int profile, int itemId) => Stores.Profiles.RemoveItem(profile, itemId);

    public static void SetSlots(int profile, Dictionary<int, int> slots) => Stores.Profiles.SetSlots(profile, slots);

    public static List<(Item Item, int Slot, bool BuiltIn)> WithSlots(int profile, string version = Kogama.Protocols.ClientProtocols.ServerVersion)
    {
        Dictionary<int, int> slots = Stores.Profiles.Slots(profile);
        List<Item> builtIn = Items.ForClient(version).Items;
        List<Item> all = [.. builtIn, .. Stores.Profiles.Items(profile)];
        var taken = all.GroupBy(item => item.Category).ToDictionary(
            group => group.Key,
            group => group.Where(item => slots.ContainsKey(item.Id)).Select(item => slots[item.Id]).ToHashSet());

        var result = new List<(Item, int, bool)>();
        for (int index = 0; index < all.Count; index++)
        {
            Item item = all[index];
            if (!slots.TryGetValue(item.Id, out int slot))
            {
                HashSet<int> used = taken[item.Category];
                slot = 0;
                while (!used.Add(slot)) slot++;
            }
            result.Add((item, slot, index < builtIn.Count));
        }
        return result;
    }
}
