using OpenKogama.Handlers.Legacy;
using OpenKogama.Handlers.Legacy2012;
using OpenKogama.Kogama;
using OpenKogama.Photon;

namespace OpenKogama.Game;

public enum ChatKind
{
    Everyone,
    Team,
    Nearby,
}

public static class ServerChat
{
    const int AdminMessage = 3;
    const int ModalNotification = 6;
    const int ShortNotification = 2;
    const int LongNotification = 8;
    const int TeamMessage = 8;
    const int NearbyMessage = 9;

    public static ChatKind KindOf(Player sender, int type) =>
        !sender.ChatKinds ? ChatKind.Everyone : type switch
        {
            TeamMessage => ChatKind.Team,
            NearbyMessage => ChatKind.Nearby,
            _ => ChatKind.Everyone,
        };

    public static void Relay(Session session, Player sender, ChatKind kind, Dictionary<object, object?> data)
    {
        foreach (Player player in session.Players)
        {
            if (kind == ChatKind.Team && player.Team != sender.Team) continue;
            int type = !player.ChatKinds ? SendChatMsg.ChatMessage : kind switch
            {
                ChatKind.Team => TeamMessage,
                ChatKind.Nearby => NearbyMessage,
                _ => SendChatMsg.ChatMessage,
            };
            player.Peer.Send(GameMessage(type, data));
        }
    }

    public static void Message(Player player, string text, string? color = null)
    {
        if (color is not null && player.RichText) text = $"<color={color}>{text}</color>";
        if (player.Peer.Protocol == PhotonProtocol.Protocol15)
            player.Peer.Send(Chat2012(player.Actor, text));
        else if (player.AdminMessages)
            player.Peer.Send(GameMessage(AdminMessage, new() { [SendChatMsg.Text] = text }));
        else
            player.Peer.Send(GameMessage(SendChatMsg.ChatMessage, Line(player, text)));
    }

    public static void Notify(Player player, string text, bool brief = false)
    {
        if (!player.Notifications)
        {
            Message(player, text);
            return;
        }
        player.Peer.Send(new EventData((byte)EventCode.NotificationEvent)
        {
            Parameters =
            {
                [(byte)ParameterKey.NotificationType] = ModalNotification,
                [(byte)ParameterKey.NotificationData] = new Dictionary<object, object?>
                {
                    [(byte)1] = text,
                    [(byte)2] = brief ? ShortNotification : LongNotification,
                },
            },
        });
    }

    public static void Say(Session session, Player speaker, string text)
    {
        if (speaker.Peer.Protocol != PhotonProtocol.Protocol15)
        {
            Relay(session, speaker, ChatKind.Nearby, Line(speaker, text));
            return;
        }
        foreach (Player player in session.Players)
            player.Peer.Send(Chat2012(speaker.Actor, text));
    }

    static Dictionary<object, object?> Line(Player speaker, string text) => new()
    {
        [SendChatMsg.Sender] = speaker.Actor,
        [SendChatMsg.Text] = text,
    };

    static EventData GameMessage(int type, Dictionary<object, object?> data) => new((byte)EventCode.PostGameMsg)
    {
        Parameters =
        {
            [(byte)ParameterKey.GameMsgType] = type,
            [(byte)ParameterKey.GameMsgData] = data,
        },
    };

    static EventData Chat2012(int actor, string text) => new((byte)Event2012.ChatMsg)
    {
        Parameters =
        {
            [(byte)Key2012.ActorNr] = actor,
            [(byte)Key2012.ChatMsg] = text,
        },
    };
}
