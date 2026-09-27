using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.Storage;
using OpenKogama.World;

namespace OpenKogama.Handlers.Operations;

public sealed class PurchaseProduct(Session session) : IOperationHandler
{
    const int StreamingAssetProduct = 2;
    const int ItemProduct = 3;
    const int AvatarProduct = 4;

    public byte Code => (byte)OperationCode.PurchaseProduct;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        int type = request.Parameters.TryGetValue((byte)ParameterKey.ProductTypeID, out object? value) ? Convert.ToInt32(value) : -1;
        var product = PhotonValues.Table(request.Parameters.GetValueOrDefault((byte)ParameterKey.PurchaseProductData));

        switch (type)
        {
            case StreamingAssetProduct when Value(product, 105) is { } asset:
                BuyStreamingAsset(peer, request, asset);
                break;
            case AvatarProduct when Value(product, 126) is { } avatar:
                BuyAvatar(peer, request, Convert.ToInt32(avatar));
                break;
            case ItemProduct when Value(product, 9) is { } item:
                BuyItem(peer, request, Convert.ToInt32(item));
                break;
            default:
                Console.WriteLine($"peer {peer.Id}: unknown purchase type {type}: {string.Join(", ", product.Select(entry => $"{entry.Key} ({entry.Key.GetType().Name})={entry.Value}"))}");
                peer.Send(new OperationResponse(request) { ReturnCode = -1 });
                break;
        }
    }

    void BuyItem(PhotonPeer peer, OperationRequest request, int listingId)
    {
        if (session.For(peer) is not Player player || Stores.Market.Find(listingId) is not { Kind: ListingKind.Item } listing)
        {
            peer.Send(new OperationResponse(request) { ReturnCode = -1 });
            return;
        }

        Item item = ModelInventory.Give(player, listing.Name, listing.Category, listing.Data, listing.Owner, 0);
        peer.Send(new OperationResponse(request));
        Console.WriteLine($"profile {player.ProfileId}: bought listing {listingId} as item {item.Id}");
    }

    static object? Value(Dictionary<object, object?> product, int key) =>
        product.FirstOrDefault(entry => entry.Key is byte or short or int && Convert.ToInt32(entry.Key) == key).Value;

    static void BuyStreamingAsset(PhotonPeer peer, OperationRequest request, object asset)
    {
        PhotonDictionary result = PhotonDictionary.Untyped();
        result.Add((byte)DBQueryKey.StreamingAssetInventoryID, asset);
        result.Add((byte)DBQueryKey.PurchaseTimeTicks, DateTime.Now.Ticks);

        peer.Send(new OperationResponse(request)
        {
            Parameters = { [(byte)ParameterKey.PurchaseProductData] = result },
        });
    }

    void BuyAvatar(PhotonPeer peer, OperationRequest request, int shopId)
    {
        if (session.For(peer) is not Player player || AvatarShop.Find(shopId) is not { } bought)
        {
            Console.WriteLine($"peer {peer.Id}: cannot buy avatar {shopId} (player {session.For(peer) is not null}, shop has {AvatarShop.Avatars.Count})");
            peer.Send(new OperationResponse(request) { ReturnCode = -1 });
            return;
        }

        int avatar = Stores.Profiles.AddAvatar(player.ProfileId, bought.Id);
        int bodyId = session.AddEditorBody(bought.Snapshot(player.Actor), avatar);
        Stores.Profiles.SaveAvatar(avatar, Avatar.ReadParts(session.World, bodyId));

        peer.Send(new OperationResponse(request));
        peer.Send(new EventData((byte)EventCode.UpdateAvatarMetaData)
        {
            Parameters =
            {
                [(byte)ParameterKey.WorldObjectID] = bodyId,
                [(byte)ParameterKey.AvatarMetaData] = AvatarMetaData.Of(bodyId),
            },
        });
        peer.Send(new EventData((byte)EventCode.GetGameBatch)
        {
            Parameters =
            {
                [(byte)ParameterKey.ActorNr] = player.Actor,
                [(byte)ParameterKey.Data] = player.WorldData(session.World.SubtreeSnapshot(bodyId)),
                [(byte)ParameterKey.QueryType] = (byte)QueryType.AddToGameWorld,
            },
        });
        Console.WriteLine($"profile {player.ProfileId}: bought avatar {shopId} as body {bodyId}");
    }
}
