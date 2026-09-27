using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;

namespace OpenKogama.Handlers.Operations;

public sealed class PostChatMsg(Session session) : IOperationHandler
{
    const int Chat = 7;

    public byte Code => (byte)OperationCode.PostChatMsg;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        var evt = new EventData((byte)EventCode.PostGameMsg)
        {
            Parameters =
            {
                [(byte)ParameterKey.GameMsgType] = Chat,
                [(byte)ParameterKey.GameMsgData] = request[(byte)ParameterKey.GameMsgData],
            },
        };

        foreach (Player player in session.Players)
            player.Peer.Send(evt);
    }
}
