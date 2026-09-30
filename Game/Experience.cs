using System.Text.Json;
using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.Storage;

namespace OpenKogama.Game;

public static class Experience
{
    public const byte NoReason = 0;
    const int NoMembers = 0;
    const byte PlayModeRepeatPlayTime = 1;
    const byte EditModeRepeatPlayTime = 2;
    const byte AvatarModeRepeatPlayTime = 3;
    const byte HugeReward = 6;
    const string PlayMinute = "1MinXpRewardPlayMode";
    static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    public static int Award(Player player, int amount, byte reason)
    {
        int total = Leveling.Grant(player.ProfileId, amount);
        ShowLevelGold(player, total);
        if (!player.ServerExperience) return total;

        player.Peer.Send(new EventData((byte)EventCode.XPReward)
        {
            Parameters =
            {
                [(byte)ParameterKey.CurrentPlayerXP] = total,
                [(byte)ParameterKey.XPRewardType] = Shown(player, reason),
                [(byte)ParameterKey.AmountXP] = amount,
                [(byte)ParameterKey.Count] = NoMembers,
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

    static byte Shown(Player player, byte reason) => reason switch
    {
        NoReason => player.Mode switch
        {
            GameMode.Edit => EditModeRepeatPlayTime,
            GameMode.CharacterEditor => AvatarModeRepeatPlayTime,
            _ => PlayModeRepeatPlayTime,
        },
        > HugeReward => HugeReward,
        _ => reason,
    };

    public static void ShowLevelGold(Player player, int xp)
    {
        if (player.Peer.Translator is not Kogama.Protocols.OperationRemap remap || !remap.Knows(EventCode.GoldRewardedForLevel)) return;

        Dictionary<int, int> rewards = Leveling.TakeUnseenGold(player.ProfileId);
        if (rewards.Count > 0)
            player.Peer.Send(new EventData((byte)EventCode.GoldRewardedForLevel)
            {
                Parameters = { [(byte)ParameterKey.Data] = JsonSerializer.Serialize(new { levelGoldRewards = rewards }) },
            });
        NextGoldReward(player, xp);
    }

    static void NextGoldReward(Player player, int xp)
    {
        if (player.Peer.Translator is not Kogama.Protocols.OperationRemap remap || !remap.Knows(EventCode.NextLevelGoldReward)
            || Leveling.NextReward(xp) is not var (level, gold))
            return;

        player.Peer.Send(new EventData((byte)EventCode.NextLevelGoldReward)
        {
            Parameters = { [(byte)ParameterKey.Data] = JsonSerializer.Serialize(new { level, goldReward = gold }) },
        });
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
