using System.Net;
using System.Text.Json;

namespace OpenKogama.Game;

public static class Bans
{
    const string FileName = "bans.json";

    sealed record Ban(string Key, DateTime Until, string Reason);

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

    static string KeyOf(Player player)
    {
        IPAddress address = player.Peer.EndPoint.Address;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        return P2p.Tunnel.Carries(address) ? $"profile:{player.ProfileId}" : address.ToString();
    }

    public static void Add(Player player, int hours, string reason)
    {
        lock (Sync)
        {
            List<Ban> bans = All();
            string key = KeyOf(player);
            bans.RemoveAll(ban => ban.Until <= DateTime.UtcNow || ban.Key == key);
            bans.Add(new Ban(key, DateTime.UtcNow.AddHours(hours), reason));
            File.WriteAllText(FileName, JsonSerializer.Serialize(bans));
        }
    }

    public static bool Holds(Player player)
    {
        lock (Sync)
        {
            string key = KeyOf(player);
            return All().Any(ban => ban.Key == key && ban.Until > DateTime.UtcNow);
        }
    }
}
