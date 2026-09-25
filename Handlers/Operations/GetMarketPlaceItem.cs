using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.Storage;

namespace OpenKogama.Handlers.Operations;

public sealed class GetMarketPlaceItem(Session session) : IOperationHandler
{
    const int QueryId = 9000;

    public byte Code => (byte)OperationCode.GetMarketPlaceItem;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        int itemId = Convert.ToInt32(request[(byte)ParameterKey.ItemID]);
        Player? player = session.For(peer);
        Item? item = player is null ? null : Inventories.Find(player.ProfileId, itemId);
        byte[]? data = Stores.Market.FindBySource(ListingKind.Item, itemId)?.Data ?? item?.Bytes;

        if (player is null || data is null)
        {
            peer.Send(new OperationResponse(request) { ReturnCode = -1 });
            return;
        }

        peer.Send(new OperationResponse(request));
        peer.Send(new EventData((byte)EventCode.GetGameBatch)
        {
            Parameters =
            {
                [(byte)ParameterKey.ActorNr] = player.Actor,
                [(byte)ParameterKey.Data] = data,
                [(byte)ParameterKey.QueryType] = (byte)QueryType.Item,
                [(byte)ParameterKey.QueryId] = QueryId,
                [(byte)ParameterKey.QueryDataLeft] = false,
            },
        });
    }
}
