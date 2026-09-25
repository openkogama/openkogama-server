using OpenKogama.Game;

namespace OpenKogama.Storage;

public interface IProfileStore
{
    int Xp(int profile);
    int AddXp(int profile, int amount);

    List<Item> Items(int profile);
    Item AddItem(int profile, string name, int category, byte[] data);
    bool RemoveItem(int profile, int itemId);

    Dictionary<int, int> Slots(int profile);
    void SetSlots(int profile, Dictionary<int, int> slots);

    List<AvatarPart>? Avatar(int profile);
    void SaveAvatar(int profile, List<AvatarPart> parts);

    List<WornAccessory> Accessories(int profile);
    void SetAccessory(int profile, int item, int slot, float offset);
    void SetAccessoryOffset(int profile, int slot, float offset);
}
