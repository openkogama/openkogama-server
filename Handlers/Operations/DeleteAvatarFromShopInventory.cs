using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.Storage;

namespace OpenKogama.Handlers.Operations;

public sealed class DeleteAvatarFromShopInventory(Session session) : IOperationHandler
{
    public byte Code => (byte)OperationCode.DeleteAvatarFromShopInventory;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        int bodyId = Convert.ToInt32(request[(byte)ParameterKey.WorldObjectID]);
        bool removed = session.For(peer) is Player player
            && Stores.Market.Remove(ListingKind.Avatar, player.ProfileId, session.AvatarOfBody(bodyId, player.ProfileId));
        peer.Send(new OperationResponse(request) { ReturnCode = (short)(removed ? 0 : -1) });
    }
}
