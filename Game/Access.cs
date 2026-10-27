using System.Net;

namespace OpenKogama.Game;

public static class Access
{
    public static IReadOnlyCollection<string> Allow { get; set; } = [];

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
