using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;

namespace OpenKogama.Handlers.Operations;

public sealed class AvatarGesture(Session session, OperationCode code, EventCode relay, bool reliable) : IOperationHandler
{
    public byte Code => (byte)code;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        if (session.For(peer) is not Player player) return;

        var evt = new EventData((byte)relay)
        {
            Parameters = new Dictionary<byte, object?>(request.Parameters) { [(byte)ParameterKey.WorldObjectID] = player.AvatarId },
        };
        foreach (Player other in session.Players)
            if (other != player)
                other.Peer.Send(evt, reliable: reliable);
    }
}
