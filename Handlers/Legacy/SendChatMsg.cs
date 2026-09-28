using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Kogama.Protocols;
using OpenKogama.Photon;

namespace OpenKogama.Handlers.Legacy;

public sealed class SendChatMsg : ILegacyHandler
{
    public const int ChatMessage = 7;
    public const byte Sender = 0;
    public const byte Text = 5;

    public string Operation => "SendChatMsg";

    public void Handle(PhotonPeer peer, OperationRequest request, LegacyTranslator protocol, Session session)
    {
        if (session.For(peer) is not Player sender || request[protocol.Key("ChatMsg")] is not string message) return;
        if (Plugins.PluginHost.Chat(sender, message) is not { } text) return;

        var evt = new EventData((byte)EventCode.PostGameMsg)
        {
            Parameters =
            {
                [(byte)ParameterKey.GameMsgType] = ChatMessage,
                [(byte)ParameterKey.GameMsgData] = new Dictionary<object, object?> { [Sender] = sender.Actor, [Text] = text },
            },
        };

        foreach (Player player in session.Players)
            player.Peer.Send(evt);
    }
}
