using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;

namespace OpenKogama.Handlers.Operations;

public sealed class PostGameMsg(Session session) : IOperationHandler
{
    const int AvatarKilled = 0;
    const int Chat = 7;
    const int Victim = 0;
    const int Killer = 1;

    public byte Code => (byte)OperationCode.PostGameMsg;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        if (Convert.ToInt32(request[(byte)ParameterKey.GameMsgType]) == AvatarKilled
            && PhotonValues.Normalize(request[(byte)ParameterKey.GameMsgData]) is Dictionary<object, object?> data
            && session.For(peer) is Player victim
            && Actor(data, Killer) is int killerActor
            && session.Players.FirstOrDefault(player => player.Actor == killerActor) is Player killer
            && Actor(data, Victim) == victim.Actor)
        {
            session.Round.ReportKill(killer, victim);
        }

        if (Convert.ToInt32(request[(byte)ParameterKey.GameMsgType]) == Chat && session.For(peer) is Player sender)
        {
            Dictionary<object, object?> chat = PhotonValues.Table(request[(byte)ParameterKey.GameMsgData]);
            if (Plugins.PluginHost.Chat(session, sender, chat))
                ServerChat.Relay(session, sender, ChatKind.Everyone, chat);
            return;
        }

        var evt = new EventData((byte)EventCode.PostGameMsg)
        {
            Parameters = new Dictionary<byte, object?>(request.Parameters),
        };

        foreach (Player player in session.Players)
            player.Peer.Send(evt);
    }

    static int? Actor(Dictionary<object, object?> data, int key) =>
        data.FirstOrDefault(entry => Convert.ToInt32(entry.Key) == key).Value is object value ? Convert.ToInt32(value) : null;
}
