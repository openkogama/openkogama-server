using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.Storage;

namespace OpenKogama.Handlers.Operations;

public sealed class GetNextResultSet(Session session) : IOperationHandler
{
    public byte Code => (byte)OperationCode.GetNextResultSet;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        int queryId = Convert.ToInt32(request[(byte)ParameterKey.LargeDBQueryID]);

        PhotonDictionary outData = (DBQueryType)queryId switch
        {
            DBQueryType.RequestInventory => Inventory(session.For(peer)?.ProfileId ?? 0, session.For(peer)?.ClientVersion ?? Kogama.Protocols.ClientProtocols.ServerVersion),
            DBQueryType.RequestAvatarShopInventory => AvatarShopInventory(),
            DBQueryType.RequestClientShopInventoryForPlayer => ItemShopInventory(),
            _ => PhotonDictionary.Untyped(),
        };

        peer.Send(new OperationResponse(request)
        {
            Parameters =
            {
                [(byte)ParameterKey.DBQueryOutData] = outData,
                [(byte)ParameterKey.LargeDBQueryID] = queryId,
                [(byte)ParameterKey.HasMoreResultSets] = false,
            },
        });
    }

    public static PhotonDictionary ItemShopInventory()
    {
        PhotonDictionary shop = PhotonDictionary.Untyped();
        foreach ((Listing listing, int index) in Stores.Market.List(ListingKind.Item).Select((listing, index) => (listing, index)))
        {
            PhotonDictionary entry = PhotonDictionary.Untyped();
            entry.Add((byte)DBQueryKey.ItemCategoryID, listing.Category);
            entry.Add((byte)DBQueryKey.ItemTypeID, listing.Id);
            entry.Add((byte)DBQueryKey.ItemName, listing.Name);
            entry.Add((byte)DBQueryKey.ItemDescription, listing.Description);
            entry.Add((byte)DBQueryKey.ItemData, listing.Data);
            entry.Add((byte)DBQueryKey.Resellable, false);
            entry.Add((byte)DBQueryKey.PriceSilver, listing.Price);
            entry.Add((byte)DBQueryKey.PriceGold, 0);
            entry.Add((byte)DBQueryKey.PositionIndex, index);
            shop.Add(listing.Id, entry);
        }
        return shop;
    }

    public static PhotonDictionary AvatarShopInventory()
    {
        PhotonDictionary shop = PhotonDictionary.Untyped();
        foreach (ShopAvatar avatar in AvatarShop.Avatars)
        {
            PhotonDictionary entry = PhotonDictionary.Untyped();
            entry.Add((byte)DBQueryKey.KogamaData, avatar.Bytes);
            entry.Add((byte)DBQueryKey.PriceSilver, avatar.Price);
            entry.Add((byte)DBQueryKey.PriceGold, 0);
            entry.Add((byte)DBQueryKey.PositionIndex, avatar.Slot);
            shop.Add(avatar.Id, entry);
        }
        return shop;
    }

    public static PhotonDictionary Inventory(int profileId, string version)
    {
        PhotonDictionary inventory = PhotonDictionary.Untyped();
        bool defaultFlag = Kogama.Protocols.ProtocolTable.For(version).DBQueryKeys.ContainsKey(nameof(DBQueryKey.IsDefaultInvItem));

        foreach ((Item item, int slot, bool builtIn) in Inventories.WithSlots(profileId, version))
        {
            PhotonDictionary entry = PhotonDictionary.Untyped();
            entry.Add((byte)DBQueryKey.ItemID, item.Id);
            entry.Add((byte)DBQueryKey.ItemCategoryID, item.Category);
            entry.Add((byte)DBQueryKey.ItemTypeID, item.Id);
            entry.Add((byte)DBQueryKey.ItemName, item.Name);
            entry.Add((byte)DBQueryKey.ItemDescription, item.Description);
            entry.Add((byte)DBQueryKey.PriceGold, 0);
            entry.Add((byte)DBQueryKey.PriceSilver, 0);
            entry.Add((byte)DBQueryKey.Resellable, item.Author == profileId);
            entry.Add((byte)DBQueryKey.ShopInventoryID, Stores.Market.FindBySource(ListingKind.Item, item.Id)?.Id ?? 0);
            entry.Add((byte)DBQueryKey.AuthorProfileID, item.Author);
            entry.Add((byte)DBQueryKey.OriginalItemID, item.Id);
            entry.Add((byte)DBQueryKey.Deleted, false);
            entry.Add((byte)DBQueryKey.ItemData, item.Bytes);
            entry.Add((byte)DBQueryKey.SlotIndex, slot);
            if (defaultFlag) entry.Add((byte)DBQueryKey.IsDefaultInvItem, builtIn);
            inventory.Add(item.Id, entry);
        }

        return inventory;
    }
}
