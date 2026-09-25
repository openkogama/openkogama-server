using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;

namespace OpenKogama.Handlers.Operations;

public sealed class UpdateNetworkInput(Session session) : IOperationHandler
{
    public byte Code => (byte)OperationCode.UpdateNetworkInput;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        var evt = new EventData((byte)EventCode.UpdateNetworkInput)
        {
            Parameters = new Dictionary<byte, object?>(request.Parameters),
        };
        foreach (Player player in session.Players)
            if (player.Peer != peer)
                player.Peer.Send(evt);
    }
}
