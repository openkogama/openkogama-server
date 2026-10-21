using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace OpenKogama.Game;

public static class SessionTokens
{
    static readonly TimeSpan Life = TimeSpan.FromHours(12);
    const int Size = 24;
    static readonly ConcurrentDictionary<string, (int Profile, string? Name, long Expires)> Issued = new();

    public static string Issue(int profile, string? name)
    {
        long now = Environment.TickCount64;
        foreach (string stale in Issued.Where(pair => pair.Value.Expires < now).Select(pair => pair.Key).ToList())
            Issued.TryRemove(stale, out _);
        string token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(Size));
        Issued[token] = (profile, SessionToken.Clean(name), now + (long)Life.TotalMilliseconds);
        return token;
    }

    public static (int Profile, string? Name) Find(string? token) =>
        token is not null && Issued.TryGetValue(token, out var issued) && issued.Expires >= Environment.TickCount64
            ? (issued.Profile, issued.Name)
            : (0, null);
}
