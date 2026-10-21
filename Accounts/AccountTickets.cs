using System.Security.Cryptography;

namespace OpenKogama.Accounts;

public static class AccountTickets
{
    static readonly Dictionary<string, long> Used = [];

    public static Ticket? Accept(string? ticket, Func<string, bool> audience)
    {
        if (string.IsNullOrEmpty(ticket) || CentralKey.Current is not ECDsa key || Tickets.Verify(key, ticket, audience) is not Ticket valid) return null;
        long now = Environment.TickCount64;
        lock (Used)
        {
            foreach (string old in Used.Where(entry => entry.Value < now).Select(entry => entry.Key).ToList()) Used.Remove(old);
            if (!Used.TryAdd(valid.Id, now + 2 * (long)Tickets.Lifetime.TotalMilliseconds)) return null;
        }
        return valid;
    }

    public static string Audience(string address)
    {
        string trimmed = address.Trim().TrimEnd('/');
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? url) || url.Scheme is not ("http" or "https")) return trimmed.ToLowerInvariant();
        return url.IsDefaultPort ? $"{url.Scheme}://{url.Host}" : $"{url.Scheme}://{url.Host}:{url.Port}";
    }
}
