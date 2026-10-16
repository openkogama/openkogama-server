using System.Collections.Concurrent;
using System.Collections.Specialized;
using OpenKogama.Storage;

namespace OpenKogama.Web;

public static class SessionLocator
{
    static readonly ConcurrentDictionary<int, string> Tunneled = new();

    public static string GameServer { get; set; } = "127.0.0.1";
    public static string PublicUrl { get; set; } = "http://127.0.0.1:8080";
    public static Func<int> EntryWorld { get; set; } = () => 0;

    public static void Remember(int profile, string? via)
    {
        if (via is null) Tunneled.TryRemove(profile, out _);
        else Tunneled[profile] = via;
    }

    public static string UrlFor(int profile) => Tunneled.TryGetValue(profile, out string? via) ? "http://" + via : PublicUrl;

    public static string? Answer(string path, NameValueCollection query, string? via = null)
    {
        bool edit = path.EndsWith("/PHP/EditSessionLocator.php", StringComparison.OrdinalIgnoreCase);
        if (!edit && !path.EndsWith("/PHP/GameSessionLocator.php", StringComparison.OrdinalIgnoreCase)) return null;

        string planet = query["PlanetName"] ?? "";
        bool parsed = int.TryParse(planet, out int id);
        if (parsed && id == 0 && !edit) id = EntryWorld();
        bool found = parsed && Stores.Worlds.World(id) is not null;
        Console.WriteLine($"session locator: {(edit ? "edit" : "play")} planet '{planet}' {(found ? "found" : "missing")}");

        if (!found) return "STATUS:'1' GAMESERVERIP:'' GAMENAME:''";
        string server = via is null ? GameServer : P2p.Tunnel.Address2012.ToString();
        return edit ? $"STATUS:'3' GAMESERVERIP:'{server}'" : $"STATUS:'2' GAMESERVERIP:'{server}' GAMENAME:'{id}'";
    }
}
