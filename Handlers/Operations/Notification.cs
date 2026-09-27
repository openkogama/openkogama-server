using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;

namespace OpenKogama.Handlers.Operations;

public sealed class Notification(Session session) : IOperationHandler
{
    public byte Code => (byte)OperationCode.Notification;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        if (session.For(peer) is not { } player) return;

        var evt = new EventData((byte)EventCode.NotificationEvent)
        {
            Parameters =
            {
                [(byte)ParameterKey.NotificationType] = request[(byte)ParameterKey.NotificationType],
                [(byte)ParameterKey.NotificationData] = request[(byte)ParameterKey.NotificationData],
            },
        };

        foreach (Player other in session.Players)
            if (other != player && other.Saw(player.Actor) && other.Peer.Translator is Kogama.Protocols.OperationRemap)
                other.Peer.Send(evt);
    }
}
