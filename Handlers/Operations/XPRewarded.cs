using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;

namespace OpenKogama.Handlers.Operations;

public sealed class XPRewarded(Session session) : IOperationHandler
{
    public byte Code => (byte)OperationCode.XPRewarded;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        if (session.For(peer) is not Player player) return;

        var evt = new EventData((byte)EventCode.XPRewarded)
        {
            Parameters =
            {
                [(byte)ParameterKey.ActorNr] = player.Actor,
                [(byte)ParameterKey.ReceivedXPReason] = Convert.ToByte(request[(byte)ParameterKey.ReceivedXPReason]),
            },
        };
        foreach (Player other in session.Players)
            if (other != player) other.Peer.Send(evt);
    }
}
