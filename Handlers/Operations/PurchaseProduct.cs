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
    const int GameCoinBoosterProduct = 6;
    const int MysteryBoxSpinsProduct = 7;
    const int MarketPlaceAvatarProduct = 8;
    const int ThemeProduct = 9;
    const int AccessoryBundleProduct = 10;
    const int GamePassTierProduct = 11;

    public byte Code => (byte)OperationCode.PurchaseProduct;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        int type = request.Parameters.TryGetValue((byte)ParameterKey.ProductTypeID, out object? value) ? Convert.ToInt32(value) : -1;
        var product = PhotonValues.Table(request.Parameters.GetValueOrDefault((byte)ParameterKey.PurchaseProductData));
        if (session.For(peer)?.ModernAccessories == true)
            type = type switch
            {
                7 => MarketPlaceAvatarProduct,
                8 => ThemeProduct,
                9 => AccessoryBundleProduct,
                10 => GamePassTierProduct,
                _ => type,
            };

        switch (type)
        {
            case StreamingAssetProduct when Value(product, 105) is { } asset:
                BuyStreamingAsset(peer, request, asset, session.For(peer));
                break;
            case AvatarProduct when Value(product, 126) is { } avatar:
                BuyAvatar(peer, request, Convert.ToInt32(avatar));
                break;
            case ItemProduct when Value(product, 9) is { } item:
                BuyItem(peer, request, Convert.ToInt32(item));
                break;
            case GameCoinBoosterProduct when session.For(peer) is Player buyer:
                BuyCoinBoost(peer, request, buyer);
                break;
            case MysteryBoxSpinsProduct when session.For(peer) is Player buyer && product.Values.OfType<int>().FirstOrDefault() is > 0 and int count:
                peer.Send(Success(request, buyer));
                Console.WriteLine($"profile {buyer.ProfileId}: bought {count} spins, {Spins.Buy(buyer, count)} left");
                break;
            case GamePassTierProduct when session.For(peer) is Player tierBuyer && Value(product, (byte)ParameterKey.Data) is { } tier:
                short result = session.GamePasses.Purchase(tierBuyer, Convert.ToInt32(tier));
                peer.Send(result == 0 ? Success(request, tierBuyer) : new OperationResponse(request) { ReturnCode = result });
                break;
            case ThemeProduct when !session.Play && Value(product, (byte)ParameterKey.Id) is { } theme:
                bool applied = Themes.Apply(session, Convert.ToInt32(theme), PhotonValues.Table(Value(product, (byte)ParameterKey.MetaData)));
                peer.Send(applied ? Success(request, session.For(peer)) : new OperationResponse(request) { ReturnCode = -1 });
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

        Item item = ModelInventory.Give(player, listing.Name, ModelInventory.ShopCategory(listing.Category), listing.Data, listing.Owner, 0).Item;
        peer.Send(Success(request, player));
        Console.WriteLine($"profile {player.ProfileId}: bought listing {listingId} as item {item.Id}");
    }

    static void BuyCoinBoost(PhotonPeer peer, OperationRequest request, Player player)
    {
        int left = CoinBoost.Buy(player);
        int key = Kogama.Protocols.ProtocolTable.For(player.ClientVersion).ParameterKeys
            .GetValueOrDefault(nameof(ParameterKey.GameCoinBoosterLeft), (int)ParameterKey.GameCoinBoosterLeft);
        PhotonDictionary result = PhotonDictionary.Untyped();
        result.Add((byte)key, left);

        OperationResponse response = Success(request, player);
        response.Parameters[(byte)ParameterKey.PurchaseProductData] = result;
        peer.Send(response);
        Console.WriteLine($"profile {player.ProfileId}: bought coin boost, {left / 60000} minutes left");
    }

    static OperationResponse Success(OperationRequest request, Player? player) =>
        new(request) { Parameters = { [(byte)ParameterKey.GoldAmount] = player is null ? 0 : Stores.Profiles.Gold(player.ProfileId) } };

    static object? Value(Dictionary<object, object?> product, int key) =>
        product.FirstOrDefault(entry => entry.Key is byte or short or int && Convert.ToInt32(entry.Key) == key).Value;

    static void BuyStreamingAsset(PhotonPeer peer, OperationRequest request, object asset, Player? player)
    {
        Dictionary<string, int> keys = Kogama.Protocols.ProtocolTable.For(player?.ClientVersion ?? Kogama.Protocols.ClientProtocols.ServerVersion).DBQueryKeys;
        PhotonDictionary result = PhotonDictionary.Untyped();
        result.Add((byte)keys.GetValueOrDefault(nameof(DBQueryKey.StreamingAssetInventoryID), (int)DBQueryKey.StreamingAssetInventoryID), asset);
        result.Add((byte)keys.GetValueOrDefault(nameof(DBQueryKey.PurchaseTimeTicks), (int)DBQueryKey.PurchaseTimeTicks), DateTime.Now.Ticks);

        OperationResponse response = Success(request, player);
        response.Parameters[(byte)ParameterKey.PurchaseProductData] = result;
        peer.Send(response);
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

        peer.Send(Success(request, player));
        peer.Send(new EventData((byte)EventCode.UpdateAvatarMetaData)
        {
            Parameters =
            {
                [(byte)ParameterKey.WorldObjectID] = bodyId,
                [(byte)ParameterKey.AvatarMetaData] = AvatarMetaData.Of(bodyId, player.Silver),
            },
        });
        peer.Send(new EventData((byte)EventCode.GetGameBatch)
        {
            Parameters =
            {
                [(byte)ParameterKey.ActorNr] = player.Actor,
                [(byte)ParameterKey.Data] = player.WorldData(session.World.SubtreeSnapshot(bodyId)),
                [(byte)ParameterKey.QueryType] = (byte)QueryType.AddToGameWorld,
                [(byte)ParameterKey.QueryId] = GetNextGameBatch.NextQueryId(),
                [(byte)ParameterKey.QueryDataLeft] = false,
            },
        });
        Console.WriteLine($"profile {player.ProfileId}: bought avatar {shopId} as body {bodyId}");
    }
}
