using System.Net;
using System.Text.Json;

namespace OpenKogama.Game;

public static class Bans
{
    const string FileName = "bans.json";
    public const int MaxHours = 24 * 365 * 10;

    sealed record Ban(string Key, DateTime Until, string Reason, string? Name = null);

    public sealed record Entry(string Name, string Reason, DateTime Until, bool Code, List<string> Keys);

    static readonly object Sync = new();
    static List<Ban>? _bans;

    static List<Ban> All()
    {
        if (_bans is not null) return _bans;
        try
        {
            _bans = File.Exists(FileName) ? JsonSerializer.Deserialize<List<Ban>>(File.ReadAllText(FileName)) ?? [] : [];
        }
        catch (Exception error) when (error is IOException or JsonException)
        {
            Console.WriteLine($"bans: {error.Message}");
            _bans = [];
        }
        return _bans;
    }

    static List<string> KeysOf(Player player)
    {
        IPAddress address = player.Peer.EndPoint.Address;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (!P2p.Tunnel.Carries(address)) return [address.ToString()];
        List<string> keys = [$"profile:{player.ProfileId}"];
        if (TunnelGuests.Of(player.Peer.EndPoint) is string guest) keys.Add($"guest:{guest}");
        return keys;
    }

    public static void Add(Player player, int hours, string reason)
    {
        lock (Sync)
        {
            List<Ban> bans = All();
            List<string> keys = KeysOf(player);
            DateTime until = DateTime.UtcNow.AddHours(Math.Clamp(hours, 1, MaxHours));
            bans.RemoveAll(ban => ban.Until <= DateTime.UtcNow || keys.Contains(ban.Key));
            bans.AddRange(keys.Select(key => new Ban(key, until, reason, player.Username)));
            Save(bans);
        }
    }

    public static List<Entry> Current()
    {
        lock (Sync)
            return [.. All().Where(ban => ban.Until > DateTime.UtcNow)
                .GroupBy(ban => (ban.Name, ban.Reason, ban.Until))
                .Select(group => new Entry(group.Key.Name ?? group.First().Key, group.Key.Reason, group.Key.Until,
                    group.Any(ban => !IPAddress.TryParse(ban.Key, out _)), [.. group.Select(ban => ban.Key)]))
                .OrderByDescending(entry => entry.Until)];
    }

    public static void Lift(IEnumerable<string> keys)
    {
        lock (Sync)
        {
            HashSet<string> lifted = [.. keys];
            List<Ban> bans = All();
            if (bans.RemoveAll(ban => lifted.Contains(ban.Key) || ban.Until <= DateTime.UtcNow) > 0) Save(bans);
        }
    }

    static void Save(List<Ban> bans) => File.WriteAllText(FileName, JsonSerializer.Serialize(bans));

    public static bool Holds(Player player)
    {
        lock (Sync)
        {
            List<string> keys = KeysOf(player);
            return All().Any(ban => keys.Contains(ban.Key) && ban.Until > DateTime.UtcNow);
        }
    }
}
