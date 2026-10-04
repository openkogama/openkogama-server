using OpenKogama.Kogama;
using OpenKogama.Photon;

namespace OpenKogama.Game;

public sealed partial class Session
{
    const int PlayerJoinedNotification = 3;
    const int AnnouncementMs = 12_000;
    const int PrestigeLevel = 25;
    const string AnnouncerRegion = "announce";
    const string HideJoined = "\n\n\n";

    readonly List<(int Actor, Player[] Audience, long Until)> _announcers = [];

    public void Announce(string text, string? icon, IEnumerable<Player> audience)
    {
        int level = icon is not null && CustomBadges.LevelOf(icon) is int custom ? custom : PrestigeLevel;
        int actor = Interlocked.Increment(ref _nextNpcActor);
        var join = new EventData((byte)EventCode.Join)
        {
            Parameters =
            {
                [(byte)ParameterKey.ActorNr] = actor,
                [(byte)ParameterKey.ProfileID] = 0,
                [(byte)ParameterKey.Username] = text + HideJoined,
                [(byte)ParameterKey.RegionCode] = AnnouncerRegion,
                [(byte)ParameterKey.TeamID] = Npc.NoTeam,
                [(byte)ParameterKey.Level] = level,
                [(byte)ParameterKey.ClientBuildTarget] = (byte)0,
                [(byte)ParameterKey.IsActorReady] = true,
                [(byte)ParameterKey.UserProfileData] = System.Text.Json.JsonSerializer.Serialize(new
                {
                    IsAdmin = false,
                    UserName = text,
                    Gold = 0,
                    SubscriptionData = new { SubscriptionType = 0 },
                }),
                [(byte)ParameterKey.PlayerPlanetData] = """{"highScoreGamePoints":0,"gamePassTier":0}""",
            },
        };
        var notification = new EventData((byte)EventCode.NotificationEvent)
        {
            Parameters =
            {
                [(byte)ParameterKey.NotificationType] = PlayerJoinedNotification,
                [(byte)ParameterKey.NotificationData] = new Dictionary<object, object?> { [(byte)9] = actor, [(byte)12] = AnnouncerRegion },
            },
        };

        var shown = new List<Player>();
        foreach (Player player in audience)
        {
            if (!player.InWorld) continue;
            if (!player.Notifications)
            {
                ServerChat.Message(player, text);
                continue;
            }
            player.Peer.Send(join);
            player.Peer.Send(notification);
            shown.Add(player);
        }
        lock (_announcers) _announcers.Add((actor, [.. shown], Environment.TickCount64 + AnnouncementMs));
    }

    public void TickAnnouncements()
    {
        (int Actor, Player[] Audience, long Until)[] done;
        lock (_announcers)
        {
            if (_announcers.Count == 0) return;
            long now = Environment.TickCount64;
            done = [.. _announcers.Where(entry => entry.Until <= now)];
            _announcers.RemoveAll(entry => entry.Until <= now);
        }
        foreach ((int actor, Player[] audience, _) in done)
        {
            var leave = new EventData((byte)EventCode.Leave) { Parameters = { [(byte)ParameterKey.ActorNr] = actor } };
            foreach (Player player in audience.Where(player => _players.Contains(player)))
                player.Peer.Send(leave);
        }
    }
}
