using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;

namespace OpenKogama.Game;

public static class TunnelGuests
{
    const string KeyFile = "guests.key";
    const int KeyLength = 32;
    const int IdLength = 16;

    static readonly ConcurrentDictionary<int, string> Ids = new();
    static readonly Lazy<byte[]> Salt = new(LoadSalt);

    public static void Report(int port, IPAddress address) => Ids[port] = IdOf(address);

    public static string? Of(IPEndPoint endPoint) =>
        P2p.Tunnel.Carries(endPoint.Address) && Ids.TryGetValue(endPoint.Port, out string? id) ? id : null;

    static string IdOf(IPAddress address) =>
        Convert.ToHexString(HMACSHA256.HashData(Salt.Value, address.MapToIPv4().GetAddressBytes()))[..IdLength].ToLowerInvariant();

    static byte[] LoadSalt()
    {
        try
        {
            if (File.Exists(KeyFile) && File.ReadAllBytes(KeyFile) is { Length: KeyLength } saved) return saved;
        }
        catch (IOException)
        {
        }
        byte[] fresh = RandomNumberGenerator.GetBytes(KeyLength);
        try
        {
            File.WriteAllBytes(KeyFile, fresh);
        }
        catch (IOException error)
        {
            Console.WriteLine($"tunnel guests: {error.Message}");
        }
        return fresh;
    }
}
