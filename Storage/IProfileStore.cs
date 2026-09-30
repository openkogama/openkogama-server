using OpenKogama.Game;

namespace OpenKogama.Storage;

public sealed record StoredAvatar(int Id, bool Active, int? Source);

public interface IProfileStore
{
    int Xp(int profile);
    int AddXp(int profile, int amount);
    int Gold(int profile);
    int AddGold(int profile, int amount);
    (int Level, Dictionary<int, int> Unseen) LevelRewards(int profile);
    void SetLevelRewards(int profile, int level, Dictionary<int, int> unseen);
    (int Tier, int Seen) GameTier(int profile, int world);
    void SetGameTier(int profile, int world, int tier, int seen);
    int CoinBoost(int profile);
    int AddCoinBoost(int profile, int milliseconds);
    (int Spins, long Next) Spins(int profile);
    void SetSpins(int profile, int spins, long next);
    byte[]? FirstTime(int profile);
    void SetFirstTime(int profile, byte[] state);

    List<Item> Items(int profile);
    Item AddItem(int profile, string name, int category, byte[] data, int author);
    bool RemoveItem(int profile, int itemId);

    Dictionary<int, int> Slots(int profile);
    void SetSlots(int profile, Dictionary<int, int> slots);

    List<StoredAvatar> Avatars(int profile);
    int ActiveAvatar(int profile);
    int AddAvatar(int profile, int? source);
    int? AvatarSource(int avatar);
    List<AvatarPart>? AvatarParts(int avatar);
    void SaveAvatar(int avatar, List<AvatarPart> parts);
    void SetActiveAvatar(int profile, int avatar);

    List<WornAccessory> Accessories(int avatar);
    void SetAccessory(int avatar, int item, int slot, float offset, float scale = 1f);
    void SetAccessoryOffset(int avatar, int slot, float offset);
    void SetAccessoryScale(int avatar, int slot, float scale);
    void ClearAccessorySlot(int avatar, int slot);
}
