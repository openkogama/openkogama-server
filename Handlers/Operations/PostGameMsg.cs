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
        if (session.For(peer) is not Player sender) return;
        int type = Convert.ToInt32(request[(byte)ParameterKey.GameMsgType]);
        Dictionary<object, object?> data = PhotonValues.Table(request[(byte)ParameterKey.GameMsgData]);

        if (type == Chat)
        {
            if (AntiCheat.Chat(sender, data) is { } chat && Plugins.PluginHost.Chat(session, sender, chat))
                ServerChat.Relay(session, sender, ChatKind.Everyone, chat);
            return;
        }

        if (AntiCheat.GameMessage(sender, type, data) is not { } message) return;
        if (type == AvatarKilled && Actor(message, Victim) == sender.Actor && Actor(message, Killer) is int killerActor)
        {
            if (session.Players.FirstOrDefault(player => player.Actor == killerActor) is Player killer)
            {
                session.Round.ReportKill(killer, sender);
                Plugins.PluginHost.Killed(session, sender, killer);
            }
            else if (session.Npcs.FirstOrDefault(npc => npc.Actor == killerActor) is Npc npc)
            {
                Plugins.PluginHost.KilledByNpc(session, sender, npc);
            }
        }

        var evt = new EventData((byte)EventCode.PostGameMsg)
        {
            Parameters =
            {
                [(byte)ParameterKey.GameMsgType] = type,
                [(byte)ParameterKey.GameMsgData] = message,
            },
        };

        foreach (Player player in session.Players)
            player.Peer.Send(evt);
    }

    static int? Actor(Dictionary<object, object?> data, int key) =>
        data.FirstOrDefault(entry => Convert.ToInt32(entry.Key) == key).Value is object value ? Convert.ToInt32(value) : null;
}
