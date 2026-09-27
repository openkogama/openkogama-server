using System.Text.Json;
using System.Text.Json.Serialization;
using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.Storage;

namespace OpenKogama.Game;

public enum RewardRarity { Common, UnCommon, Rare, Epic, Legendary }

public sealed record SpinReward(int Xp, RewardRarity Rarity, double Chance);

public sealed record SpinConfig(int FreeSpinSeconds, int SpinPrice, List<SpinReward> Rewards);

public static class Spins
{
    const int XPReward = 1;

    static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } };
    static readonly object Sync = new();
    static SpinConfig? _config;

    public static SpinConfig Config => _config ??= JsonSerializer.Deserialize<SpinConfig>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "rewards.json")), Options)!;

    public static int[] Types => [.. Config.Rewards.Select(_ => XPReward)];

    public static string[] Datas =>
        [.. Config.Rewards.Select(reward => JsonSerializer.Serialize(new { xpAmount = reward.Xp, RewardType = XPReward, RewardRarity = (int)reward.Rarity }))];

    static long Interval => Config.FreeSpinSeconds * 1000L;

    static long Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public static bool Supported(Player player) =>
        player.Peer.Translator is Kogama.Protocols.OperationRemap remap && remap.Knows(EventCode.RewardIsReady);

    public static void Join(Session session, Player player)
    {
        if (!Supported(player)) return;

        lock (Sync)
        {
            (int spins, long next) = Stores.Profiles.Spins(player.ProfileId);
            long now = Now;
            if (now >= next)
            {
                spins++;
                next = now + Interval;
                Stores.Profiles.SetSpins(player.ProfileId, spins, next);
            }
            player.NextFreeSpin = next;
            Announce(session, player, spins, next - now);
        }
    }

    public static void Tick(Session session)
    {
        long now = Now;
        foreach (Player player in session.Players)
        {
            if (player.NextFreeSpin is not long next || now < next) continue;

            lock (Sync)
            {
                int spins = Stores.Profiles.Spins(player.ProfileId).Spins + 1;
                Stores.Profiles.SetSpins(player.ProfileId, spins, now + Interval);
                player.NextFreeSpin = now + Interval;
                Announce(session, player, spins, Interval);
            }
        }
    }

    public static int Pick(Player player)
    {
        double roll = Random.Shared.NextDouble() * Config.Rewards.Sum(reward => reward.Chance);
        int index = Config.Rewards.Count - 1;
        for (int i = 0; i < Config.Rewards.Count; i++)
        {
            roll -= Config.Rewards[i].Chance;
            if (roll >= 0) continue;
            index = i;
            break;
        }
        player.PendingReward = index;
        return index;
    }

    public static SpinReward? Claim(Player player)
    {
        lock (Sync)
        {
            (int spins, long next) = Stores.Profiles.Spins(player.ProfileId);
            if (player.PendingReward is not int index || spins <= 0) return null;

            SpinReward reward = Config.Rewards[index];
            player.PendingReward = null;
            Stores.Profiles.SetSpins(player.ProfileId, spins - 1, next);
            Stores.Profiles.AddXp(player.ProfileId, reward.Xp);
            Pending(player, spins - 1);
            return reward;
        }
    }

    public static int Buy(Player player, int count)
    {
        lock (Sync)
        {
            (int spins, long next) = Stores.Profiles.Spins(player.ProfileId);
            spins += Math.Max(count, 0);
            Stores.Profiles.SetSpins(player.ProfileId, spins, next);
            Pending(player, spins);
            return spins;
        }
    }

    static void Announce(Session session, Player player, int spins, long remaining)
    {
        player.Peer.Send(new EventData((byte)EventCode.StartRewardCountDown)
        {
            Parameters =
            {
                [(byte)ParameterKey.TimeInterval] = (int)remaining,
                [(byte)ParameterKey.Timestamp] = session.Clock(),
            },
        });
        Pending(player, spins);
    }

    static void Pending(Player player, int spins) =>
        player.Peer.Send(new EventData((byte)EventCode.RewardIsReady)
        {
            Parameters = { [(byte)ParameterKey.NumberOfPendingRewards] = spins },
        });
}
