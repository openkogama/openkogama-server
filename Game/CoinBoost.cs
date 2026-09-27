using OpenKogama.Storage;

namespace OpenKogama.Game;

public static class CoinBoost
{
    public const int PurchaseMilliseconds = 30 * 60 * 1000;

    static readonly object Sync = new();

    public static int Left(Player player)
    {
        lock (Sync)
        {
            Settle(player);
            return Stores.Profiles.CoinBoost(player.ProfileId);
        }
    }

    public static int Buy(Player player)
    {
        lock (Sync)
        {
            Settle(player);
            return Stores.Profiles.AddCoinBoost(player.ProfileId, PurchaseMilliseconds);
        }
    }

    public static (int Left, bool Enabled) Switch(Player player, bool enable)
    {
        lock (Sync)
        {
            Settle(player);
            int left = Stores.Profiles.CoinBoost(player.ProfileId);
            player.BoostSince = enable && left > 0 ? DateTime.UtcNow : null;
            return (left, player.BoostSince is not null);
        }
    }

    public static void Stop(Player player)
    {
        lock (Sync)
        {
            Settle(player);
            player.BoostSince = null;
        }
    }

    static void Settle(Player player)
    {
        if (player.BoostSince is not DateTime since) return;

        DateTime now = DateTime.UtcNow;
        Stores.Profiles.AddCoinBoost(player.ProfileId, -(int)(now - since).TotalMilliseconds);
        player.BoostSince = now;
    }
}
