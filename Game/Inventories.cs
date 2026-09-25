using OpenKogama.Storage;

namespace OpenKogama.Game;

public static class Inventories
{
    public static Item? Find(int profile, int itemId) =>
        Items.For("2015").Find(itemId) ?? Stores.Profiles.Items(profile).Find(item => item.Id == itemId);

    public static Item Add(int profile, string name, int category, byte[] data) =>
        Stores.Profiles.AddItem(profile, name, category, data);

    public static bool Remove(int profile, int itemId) => Stores.Profiles.RemoveItem(profile, itemId);

    public static void SetSlots(int profile, Dictionary<int, int> slots) => Stores.Profiles.SetSlots(profile, slots);

    public static List<(Item Item, int Slot)> WithSlots(int profile)
    {
        Dictionary<int, int> slots = Stores.Profiles.Slots(profile);
        List<Item> all = [.. Items.For("2015").Items, .. Stores.Profiles.Items(profile)];
        var taken = all.GroupBy(item => item.Category).ToDictionary(
            group => group.Key,
            group => group.Where(item => slots.ContainsKey(item.Id)).Select(item => slots[item.Id]).ToHashSet());

        var result = new List<(Item, int)>();
        foreach (Item item in all)
        {
            if (!slots.TryGetValue(item.Id, out int slot))
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
