using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;

namespace OpenKogama.Handlers.Operations;

public sealed class GetNextResultSet(Session session) : IOperationHandler
{
    public byte Code => (byte)OperationCode.GetNextResultSet;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        int queryId = Convert.ToInt32(request[(byte)ParameterKey.LargeDBQueryID]);

        PhotonDictionary outData = (DBQueryType)queryId switch
        {
            DBQueryType.RequestInventory => Inventory(session.For(peer)?.ProfileId ?? 0),
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

    static PhotonDictionary Inventory(int profileId)
    {
        PhotonDictionary inventory = PhotonDictionary.Untyped();
        var nextSlot = new Dictionary<int, int>();

        foreach (Item item in Items.For("2015").Items)
        {
            int slot = nextSlot.GetValueOrDefault(item.Category);
            nextSlot[item.Category] = slot + 1;

            PhotonDictionary entry = PhotonDictionary.Untyped();
            entry.Add((byte)DBQueryKey.ItemID, item.Id);
            entry.Add((byte)DBQueryKey.ItemCategoryID, item.Category);
            entry.Add((byte)DBQueryKey.ItemTypeID, item.Id);
            entry.Add((byte)DBQueryKey.ItemName, item.Name);
            entry.Add((byte)DBQueryKey.ItemDescription, item.Description);
            entry.Add((byte)DBQueryKey.PriceGold, 0);
            entry.Add((byte)DBQueryKey.PriceSilver, 0);
            entry.Add((byte)DBQueryKey.Resellable, false);
            entry.Add((byte)DBQueryKey.ShopInventoryID, 0);
            entry.Add((byte)DBQueryKey.AuthorProfileID, profileId);
            entry.Add((byte)DBQueryKey.OriginalItemID, item.Id);
            entry.Add((byte)DBQueryKey.Deleted, false);
            entry.Add((byte)DBQueryKey.ItemData, item.Bytes);
            entry.Add((byte)DBQueryKey.SlotIndex, slot);
            inventory.Add(item.Id, entry);
        }

        return inventory;
    }
}
