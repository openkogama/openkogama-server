using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.Storage;

namespace OpenKogama.Game;

public static class Experience
{
    public const byte NoReason = 0;
    const byte PlayModeRepeatPlayTime = 1;
    const string PlayMinute = "1MinXpRewardPlayMode";
    static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    public static int Award(Player player, int amount, byte reason)
    {
        int total = Stores.Profiles.AddXp(player.ProfileId, amount);
        if (!player.ServerExperience) return total;

        player.Peer.Send(new EventData((byte)EventCode.XPReward)
        {
            Parameters =
            {
                [(byte)ParameterKey.CurrentPlayerXP] = total,
                [(byte)ParameterKey.XPRewardType] = reason,
                [(byte)ParameterKey.AmountXP] = amount,
            },
        });

        int level = Leveling.LevelFor(total);
        if (level != Leveling.LevelFor(total - amount))
        {
            player.Peer.Send(new EventData((byte)EventCode.LevelChanged)
            {
                Parameters =
                {
                    [(byte)ParameterKey.ActorNr] = player.Actor,
                    [(byte)ParameterKey.Level] = level,
                },
            });
        }
        return total;
    }

    public static void Tick(Session session)
    {
        if (!session.Play || session.Players.Count < Leveling.Data.MinPlayersActivateXP) return;
        int amount = Leveling.Data.Xp.Find(xp => xp.Type == PlayMinute)?.Amount ?? 0;
        if (amount <= 0) return;

        DateTime now = DateTime.UtcNow;
        foreach (Player player in session.Players)
        {
            if (!player.ServerExperience || player.PlayingSince is not DateTime since || now - since < Minute) continue;
            player.PlayingSince = since + Minute;
            Award(player, amount, PlayModeRepeatPlayTime);
        }
    }
}
