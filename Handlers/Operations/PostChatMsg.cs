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

        if (AntiCheat.Chat(sender, PhotonValues.Table(request[(byte)ParameterKey.GameMsgData])) is not { } data
            || !Plugins.PluginHost.Chat(session, sender, data)) return;

        int type = request[(byte)ParameterKey.GameMsgType] is { } value ? Convert.ToInt32(value) : 0;
        ServerChat.Relay(session, sender, ServerChat.KindOf(sender, type), data);
        Console.WriteLine($"{sender.Username}: {data.GetValueOrDefault(Legacy.SendChatMsg.Text)}");
    }
}
