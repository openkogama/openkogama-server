using System.Collections.Specialized;
using OpenKogama.Storage;

namespace OpenKogama.Web;

public static class SessionLocator
{
    const string GameServer = "127.0.0.1";

    public static string? Answer(string path, NameValueCollection query)
    {
        bool edit = path.EndsWith("/PHP/EditSessionLocator.php", StringComparison.OrdinalIgnoreCase);
        if (!edit && !path.EndsWith("/PHP/GameSessionLocator.php", StringComparison.OrdinalIgnoreCase)) return null;

        string planet = query["PlanetName"] ?? "";
        bool found = int.TryParse(planet, out int id) && Stores.Worlds.World(id) is not null;
        Console.WriteLine($"session locator: {(edit ? "edit" : "play")} planet '{planet}' {(found ? "found" : "missing")}");

        if (!found) return "STATUS:'1' GAMESERVERIP:'' GAMENAME:''";
        return edit ? $"STATUS:'3' GAMESERVERIP:'{GameServer}'" : $"STATUS:'2' GAMESERVERIP:'{GameServer}' GAMENAME:'{id}'";
    }
}
