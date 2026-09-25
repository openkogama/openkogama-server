using System.Text.Json;
using OpenKogama.Storage;
using OpenKogama.World;

namespace OpenKogama.Game;

public sealed class ShopAvatar
{
    public int Id { get; set; }
    public int Slot { get; set; }
    public string Data { get; set; } = "";
    public int Price { get; set; }

    public byte[] Bytes => Convert.FromBase64String(Data);

    public Snapshot Snapshot(int owner)
    {
        Snapshot snapshot = WorldSerializer.Read(Bytes, runtime: false);
        foreach (WorldObject obj in snapshot.Objects) obj.Owner = owner;
        return snapshot;
    }
}

public static class AvatarShop
{
    static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };
    const int ListingSlotStart = 1000;

    static List<ShopAvatar>? _avatars;

    public static List<ShopAvatar> Official => _avatars ??= JsonSerializer.Deserialize<List<ShopAvatar>>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "avatars", "shop.json")), Options) ?? [];

    public static List<ShopAvatar> Avatars =>
    [
        .. Official,
        .. Stores.Market.List(ListingKind.Avatar).Select((listing, index) => new ShopAvatar
        {
            Id = listing.Id,
            Slot = ListingSlotStart + index,
            Data = Convert.ToBase64String(listing.Data),
            Price = listing.Price,
        }),
    ];

    public static ShopAvatar? Find(int id) =>
        Official.Find(avatar => avatar.Id == id)
        ?? (Stores.Market.Find(id) is { Kind: ListingKind.Avatar } listing
            ? new ShopAvatar { Id = listing.Id, Data = Convert.ToBase64String(listing.Data), Price = listing.Price }
            : null);
}
