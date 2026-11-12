using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;

namespace OpenKogama.Game;

public static class SessionTokens
{
    public const string Tunnel = "tunnel";
    static readonly TimeSpan Life = TimeSpan.FromHours(12);
    const int Size = 24;
    static readonly ConcurrentDictionary<string, (int Profile, string? Name, string? Source, long Expires)> Issued = new();

    public static string Issue(int profile, string? name, string? source = null)
    {
        long now = Environment.TickCount64;
        foreach (string stale in Issued.Where(pair => pair.Value.Expires < now).Select(pair => pair.Key).ToList())
            Issued.TryRemove(stale, out _);
        string token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(Size));
        Issued[token] = (profile, SessionToken.Clean(name), source, now + (long)Life.TotalMilliseconds);
        return token;
    }

    public static (int Profile, string? Name) Find(string? token) =>
        token is not null && Issued.TryGetValue(token, out var issued) && issued.Expires >= Environment.TickCount64
            ? (issued.Profile, issued.Name)
            : (0, null);

    public static bool IssuedTo(int profile, string? source)
    {
        long now = Environment.TickCount64;
        return source is not null && Issued.Values.Any(issued => issued.Profile == profile && issued.Source == source && issued.Expires >= now);
    }

    public static string? SourceOf(IPAddress? address, bool tunneled) =>
        tunneled ? Tunnel : address is null ? null : Access.Plain(address).ToString();
}
