using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.Storage;

namespace OpenKogama.Handlers.Operations;

public sealed class AddItemToMarketPlace(Session session) : IOperationHandler
{
    public byte Code => (byte)OperationCode.AddItemToMarketPlace;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        int itemId = Convert.ToInt32(request[(byte)ParameterKey.ItemID]);
        Player? player = session.For(peer);
        Item? item = player is null ? null : Stores.Profiles.Items(player.ProfileId).Find(item => item.Id == itemId);

        if (player is null || item is null)
        {
            peer.Send(new OperationResponse(request) { ReturnCode = -1 });
            return;
        }

        string name = request[(byte)ParameterKey.ItemName] as string is { Length: > 0 } given ? given : item.Name;
        string description = request[(byte)ParameterKey.ItemDescription] as string ?? "";
        int price = Math.Max(0, Convert.ToInt32(request[(byte)ParameterKey.ItemPrice]));
        int listing = Stores.Market.Put(ListingKind.Item, player.ProfileId, itemId, name, description, item.Category, price, item.Bytes);

        peer.Send(new OperationResponse(request)
        {
            Parameters =
            {
                [(byte)ParameterKey.ItemID] = itemId,
                [(byte)ParameterKey.ShopInventoryID] = listing,
            },
        });
        Console.WriteLine($"profile {player.ProfileId}: listed item {itemId} as {listing} for {price} silver");
    }
}
