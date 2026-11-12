using System.Net;

namespace OpenKogama.Game;

public static class Access
{
    public static IReadOnlyCollection<string> Allow { get; set; } = [];
    public static bool Shared { get; set; }
    public static bool RemoteEdit { get; set; } = true;
    public static bool Online { get; set; }

    public static bool Remote(IPAddress address)
    {
        IPAddress plain = Plain(address);
        return P2p.Tunnel.Carries(plain) || Shared && !IPAddress.IsLoopback(plain);
    }

    public static bool MayEdit(IPAddress address, string? account) =>
        !Remote(address) || RemoteEdit || Admin(P2p.Tunnel.Carries(Plain(address)) ? null : Plain(address), account);

    public static bool MayClaim(int profile, IPAddress address, string? token)
    {
        if (profile <= 0) return false;
        int own = SessionTokens.Find(token).Profile;
        if (own == profile || !Remote(address)) return true;
        if (own > 0) return false;
        if (SessionTokens.IssuedTo(profile, SessionTokens.SourceOf(address, P2p.Tunnel.Carries(Plain(address))))) return true;
        return profile != 1 && !Online && !Storage.Stores.Profiles.Claimed(profile);
    }

    public static string? AccountOf(string? token) =>
        SessionTokens.Find(token) is (int profile and > 0, string name) && Storage.Stores.Profiles.Claimed(profile) ? name : null;

    public static bool Admin(IPAddress? address, string? account) => Lists(Plugins.PluginHost.Admins, address, account);

    public static bool Admits(IPAddress? address, string? account) => Allow.Count == 0 || Lists(Allow, address, account) || Admin(address, account);

    public static bool Admits(Player player) =>
        Plugins.PluginHost.Admin(player) || Admits(P2p.Tunnel.Carries(player.Peer.EndPoint.Address) ? null : Plain(player.Peer.EndPoint.Address), player.Account);

    public static string? Entry(string? text) =>
        IPAddress.TryParse(text?.Trim(), out IPAddress? address) ? Plain(address).ToString() : SessionToken.Clean(text);

    public static IPAddress Plain(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

    static bool Lists(IEnumerable<string> entries, IPAddress? address, string? account) => entries.Any(entry =>
        IPAddress.TryParse(entry, out IPAddress? listed)
            ? address is not null && Plain(listed).Equals(Plain(address))
            : account is not null && entry.Equals(account, StringComparison.OrdinalIgnoreCase));
}
