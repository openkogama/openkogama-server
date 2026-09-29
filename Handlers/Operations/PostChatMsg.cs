using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;

namespace OpenKogama.Handlers.Operations;

public sealed class PostChatMsg(Session session) : IOperationHandler
{
    public byte Code => (byte)OperationCode.PostChatMsg;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        if (session.For(peer) is not Player sender) return;

        Dictionary<object, object?> data = PhotonValues.Table(request[(byte)ParameterKey.GameMsgData]);
        if (!Plugins.PluginHost.Chat(session, sender, data)) return;

        int type = request[(byte)ParameterKey.GameMsgType] is { } value ? Convert.ToInt32(value) : 0;
        ServerChat.Relay(session, sender, ServerChat.KindOf(sender, type), data);
    }
}
