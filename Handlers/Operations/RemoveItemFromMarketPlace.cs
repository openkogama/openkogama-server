using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.Storage;

namespace OpenKogama.Handlers.Operations;

public sealed class RemoveItemFromMarketPlace(Session session) : IOperationHandler
{
    public byte Code => (byte)OperationCode.RemoveItemFromMarketPlace;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        int itemId = Convert.ToInt32(request[(byte)ParameterKey.ItemID]);
        bool removed = session.For(peer) is Player player && Stores.Market.Remove(ListingKind.Item, player.ProfileId, itemId);
        peer.Send(new OperationResponse(request) { ReturnCode = (short)(removed ? 0 : -1) });
    }
}
