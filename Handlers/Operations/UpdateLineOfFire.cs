using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;

namespace OpenKogama.Handlers.Operations;

public sealed class UpdateLineOfFire(Session session) : IOperationHandler
{
    public byte Code => (byte)OperationCode.UpdateLineOfFire;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        var evt = new EventData((byte)EventCode.UpdateLineOfFire)
        {
            Parameters = new Dictionary<byte, object?>(request.Parameters),
        };
        evt.Parameters[(byte)ParameterKey.ActorNr] = (int)peer.Id;

        foreach (Player other in session.Players)
            if (other.Peer != peer)
                other.Peer.Send(evt, reliable: false);
    }
}
