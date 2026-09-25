using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.Storage;

namespace OpenKogama.Handlers.Operations;

public sealed class SetActiveAvatar(Session session) : IOperationHandler
{
    public byte Code => (byte)OperationCode.SetActiveAvatar;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        int bodyId = Convert.ToInt32(request[(byte)ParameterKey.AvatarID]);
        if (session.For(peer) is not Player player || !session.BodyAvatars.TryGetValue(bodyId, out int avatar))
        {
            peer.Send(new OperationResponse(request) { ReturnCode = -1 });
            return;
        }

        Stores.Profiles.SetActiveAvatar(player.ProfileId, avatar);
        peer.Send(new OperationResponse(request));
    }
}
