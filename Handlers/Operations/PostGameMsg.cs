using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;

namespace OpenKogama.Handlers.Operations;

public sealed class PostGameMsg(Session session) : IOperationHandler
{
    public byte Code => (byte)OperationCode.PostGameMsg;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        var evt = new EventData((byte)EventCode.PostGameMsg)
        {
            Parameters = new Dictionary<byte, object?>(request.Parameters),
        };

        foreach (Player player in session.Players)
            player.Peer.Send(evt);
    }
}
