using System.Collections.Specialized;
using System.Net;
using System.Text.Json;
using OpenKogama.Game;
using OpenKogama.Plugins;
using OpenKogama.Storage;

namespace OpenKogama.Web;

public static class AdminApi
{
    const int DefaultHours = 24;

    static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static ApiResponse Route(string path, ApiRequest request, IReadOnlyList<Session> sessions)
    {
        bool post = request.Method == "POST";
        return path switch
        {
            "/api/admin/players" => ApiResponse.Json(JsonSerializer.Serialize(Players(sessions), Json)),
            "/api/admin/bans" => ApiResponse.Json(JsonSerializer.Serialize(Bans.Current(), Json)),
            "/api/admin/kick" when post => Remove(sessions, request.Query, ban: false),
            "/api/admin/ban" when post => Remove(sessions, request.Query, ban: true),
            "/api/admin/unban" when post => Unban(request.Query),
            "/api/admin/admins" when post => SetAdmins(request.Body),
            "/api/admin/admins" => ApiResponse.Json(JsonSerializer.Serialize(PluginHost.Admins)),
            _ => ApiResponse.NotFound,
        };
    }

    static List<object> Players(IReadOnlyList<Session> sessions)
    {
        Dictionary<int, string> worlds = Stores.Worlds.List().ToDictionary(world => world.Id, world => world.Name);
        return [.. sessions.SelectMany(session => session.Players.ToList().Select(player => (object)new
        {
            Peer = player.Peer.Id,
            Name = player.Username,
            Client = player.ClientVersion,
            Code = P2p.Tunnel.Carries(player.Peer.EndPoint.Address),
            Admin = PluginHost.Admin(player),
            World = session.WorldId is int id ? worlds.GetValueOrDefault(id) : null,
            Mode = session.WorldId is null ? "avatar" : session.Play ? "play" : "build",
        }))];
    }

    static ApiResponse Remove(IReadOnlyList<Session> sessions, NameValueCollection query, bool ban)
    {
        Player? player = short.TryParse(query["peer"], out short peer)
            ? sessions.SelectMany(session => session.Players.ToList()).FirstOrDefault(found => found.Peer.Id == peer)
            : null;
        if (player is null) return ApiResponse.Json("{}", 404);
        if (PluginHost.Admin(player)) return ApiResponse.Json("{}", 409);

        string reason = query["reason"]?.Trim() ?? "";
        if (ban)
        {
            int hours = Math.Clamp(int.TryParse(query["hours"], out int given) ? given : DefaultHours, 1, Bans.MaxHours);
            Bans.Add(player, hours, reason);
            Console.WriteLine($"admin: banned {player.Username} for {hours} h: {reason}");
        }
        else
        {
            Console.WriteLine($"admin: kicked {player.Username}: {reason}");
        }
        player.Peer.Disconnect();
        return ApiResponse.Json("{}");
    }

    static ApiResponse Unban(NameValueCollection query)
    {
        Bans.Lift(query.GetValues("key") ?? []);
        return ApiResponse.Json("{}");
    }

    static ApiResponse SetAdmins(byte[] body)
    {
        string[]? given;
        try
        {
            given = JsonSerializer.Deserialize<string[]>(body);
        }
        catch (JsonException)
        {
            given = null;
        }
        if (given is null || given.Any(entry => !IPAddress.TryParse(entry, out _))) return ApiResponse.Json("{}", 400);
        PluginHost.Admins = [.. given.Select(IPAddress.Parse).Select(address => (address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address).ToString()).Distinct()];
        Console.WriteLine($"admin: {PluginHost.Admins.Count} admin addresses");
        return ApiResponse.Json(JsonSerializer.Serialize(PluginHost.Admins));
    }
}
