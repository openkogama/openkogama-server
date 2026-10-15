using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;

namespace OpenKogama.Handlers.Operations;

public sealed class AvatarGesture(Session session, OperationCode code, EventCode relay, bool reliable, Api.Gesture? gesture = null) : IOperationHandler
{
    public byte Code => (byte)code;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        if (session.For(peer) is not Player player) return;
        if (gesture is Api.Gesture made) Plugins.PluginHost.Gestured(session, player, made);

        var evt = new EventData((byte)relay)
        {
            Parameters = new Dictionary<byte, object?>(request.Parameters) { [(byte)ParameterKey.WorldObjectID] = player.PlayAvatar, [(byte)ParameterKey.ActorNr] = player.Actor },
        };
        foreach (Player other in session.Players)
            if (other != player)
                other.Peer.Send(evt, reliable: reliable);
    }
}
