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
        Dictionary<object, object?> data = PhotonValues.Table(request[(byte)ParameterKey.GameMsgData]);
        if (session.For(peer) is Player sender && !Plugins.PluginHost.Chat(sender, data)) return;

        var evt = new EventData((byte)EventCode.PostGameMsg)
        {
            Parameters =
            {
                [(byte)ParameterKey.GameMsgType] = Chat,
                [(byte)ParameterKey.GameMsgData] = data,
            },
        };

        foreach (Player player in session.Players)
            player.Peer.Send(evt);
    }
}
