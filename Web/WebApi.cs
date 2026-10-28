using System.Collections.Specialized;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Web;
using OpenKogama.Accounts;
using OpenKogama.Content;
using OpenKogama.Game;
using OpenKogama.Storage;

namespace OpenKogama.Web;

public sealed record ApiRequest(string Method, string Path, NameValueCollection Query, string? ContentType, byte[] Body, bool Local, string? Via = null, string? Encoding = null, System.Net.IPAddress? From = null);

public sealed record ApiResponse(int Status, string? ContentType, byte[] Body)
{
    public static readonly ApiResponse NotFound = new(404, null, []);

    public static ApiResponse Json(string body, int status = 200) => new(status, "application/json", Encoding.UTF8.GetBytes(body));
}

public sealed class WebApi
{
    static readonly JsonSerializerOptions WorldJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    const int PlanetImage = 0;
    const int AvatarImage = 1;
    const int MaxThumbnail = 2 * 1024 * 1024;

    public Func<int, GameMode, int, string?, string?, string> SessionJson { get; set; } = (_, _, _, _, _) => "{}";
    public AssetCache? Assets { get; set; }
    public AssetCache? LegacyAssets { get; set; }
    public Dictionary<string, AssetCache> AssetSets { get; init; } = [];
    public Func<int, bool> DeleteWorld { get; set; } = _ => false;
    public Func<int, byte[]?> ExportWorld { get; set; } = _ => null;
    public Action? Shutdown { get; set; }
    public Func<int> Players { get; set; } = () => 0;
    public Func<IReadOnlyList<Session>> Sessions { get; set; } = () => [];
    public ServerInfo ServerInfo { get; init; } = new();

    public Task<ApiResponse>? Waiting(ApiRequest request) =>
        Regex.Replace(request.Path, "/{2,}", "/").TrimEnd('/') == "/api/scripts/poll"
            ? ScriptMessages.Poll(Reauths.TokenOf(request.Query["key"])).ContinueWith(task =>
                task.Result is byte[] body ? new ApiResponse(200, "text/plain; charset=utf-8", body) : new ApiResponse(403, null, []))
            : null;

    public ApiResponse Route(ApiRequest request)
    {
        string path = Regex.Replace(request.Path, "/{2,}", "/");
        NameValueCollection query = request.Query;

        if (request.Via is not null && Managing(path))
            return new ApiResponse(403, null, []);

        if (path.StartsWith("/api/admin/"))
            return request.Local && request.Via is null && !ServerInfo.Shared
                ? AdminApi.Route(path.TrimEnd('/'), request, Sessions())
                : new ApiResponse(403, null, []);

        if (path.TrimEnd('/') == "/api/server")
        {
            int world = WorldOf(query);
            return ApiResponse.Json(JsonSerializer.Serialize(new {
                title = ServerInfo.DisplayTitle,
                thumbnail = Thumbnail(world) is not null ? "/api/server/thumbnail" + (world > 0 ? $"?world={world}" : "") : null,
                clients = ServerInfo.Clients ?? [],
                players = Players(),
                udp = ServerInfo.PhotonPort,
                @public = ServerInfo.Public,
                features = ServerInfo.FeaturesNow(),
            }));
        }

        if (path.TrimEnd('/') == "/api/server/thumbnail")
            return Thumbnail(WorldOf(query)) is byte[] thumbnail
                ? new ApiResponse(200, "application/octet-stream", thumbnail)
                : ApiResponse.NotFound;

        if (path.TrimEnd('/') == "/api/name")
            return ApiResponse.Json(JsonSerializer.Serialize(new { claimed = NameClaimed(SessionToken.Clean(query["name"])) }));

        if (SessionLocator.Answer(path, query, request.Via) is string located)
            return new ApiResponse(200, "text/html", Encoding.UTF8.GetBytes(located));

        if (path == "/crossdomain.xml")
            return new ApiResponse(200, "text/xml", Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><cross-domain-policy><allow-access-from domain=\"*\"/></cross-domain-policy>"));

        if (path is "/" or "/session")
        {
            if (!ServerInfo.Allows(query["client"]))
                return ApiResponse.Json(JsonSerializer.Serialize(new { error = $"This server accepts {string.Join(", ", ServerInfo.Clients)}" }), 403);
            if (Reauths.Resume(query["key"]) is { } resumed)
                return ApiResponse.Json(Reauthed(resumed));
            bool remote = ServerInfo.Shared || request.Via is not null;
            int profile = !remote && int.TryParse(query["profile"], out int requested) && requested > 0 ? requested : 1;
            string? nickname = SessionToken.Clean(query["name"]);
            string? account = null;
            if (remote && ServerInfo.Accounts != AccountMode.Offline && AccountTickets.Accept(query["ticket"], ServerInfo.Accepts) is Ticket ticket)
            {
                profile = Stores.Profiles.ForAccount(ticket.Account, ticket.Name);
                nickname = ticket.Name;
                account = SessionToken.Clean(ticket.Name);
            }
            else if (remote && ServerInfo.Accounts == AccountMode.Online)
                return Refused("This server needs an OpenKogama account, sign in with the launcher");
            else if (remote && nickname is not null)
            {
                profile = Stores.Profiles.ForName(nickname);
                if (ServerInfo.Accounts == AccountMode.Optional && Stores.Profiles.Claimed(profile))
                    return Refused("That name belongs to an OpenKogama account, sign in with the launcher to use it");
            }
            if (remote && !request.Local && profile == 1)
                return Refused("Pick a nickname in the launcher settings");
            if (remote && !request.Local && !Access.Admits(request.From, account))
                return Refused("This server only lets in invited players");
            SessionLocator.Remember(profile, request.Via, int.TryParse(query["local"], out int local) && local is > 0 and < 65536 ? local : 0);
            bool join = query["mode"] == "play";
            int world = int.TryParse(query["world"], out int chosen) ? chosen : 0;
            if (world == 0 && join) world = ServerInfo.EntryWorld();
            bool build = ServerInfo.BuildJoin && remote;
            GameMode mode = query["mode"] switch
            {
                "play" => build ? GameMode.Edit : GameMode.Play,
                "avatar" => GameMode.CharacterEditor,
                _ => GameMode.Edit,
            };
            ClientEngines.Remember(profile, query["unity"]);
            PlayerPackage? package = PlayerPackages.Find(query["key"]);
            ProfileSettings.Remember(profile, ServerInfo.PlayerSettings ? query["key"] : null, ServerInfo.PlayerSettings ? package?.Settings?.GetRawText() : null);
            if (package is not null && remote)
                PlayerPackages.Apply(profile, package, ServerInfo.PlayerAvatars, ServerInfo.PlayerInventory);
            Console.WriteLine($"profile {profile}: joins world {world} in {mode.ToString().ToLowerInvariant()} mode");
            string[] patches = query["patches"]?.Split(',') ?? [];
            return ApiResponse.Json(Issue(new Reauths.Origin(profile, nickname, query["client"], request.Via, query["transport"] == "ws", world, mode, Key: query["key"],
                Reloads: patches.Contains("world-switch"), Language: ClientLanguages.Clean(query["language"]), Tcp: patches.Contains("tcp"),
                Themes: patches.Contains("themes") ? query["themes"] : null, Teleports: patches.Contains("teleport"))));
        }

        if (path.TrimEnd('/') == "/session/reauth")
            return Reauths.Take(query["token"]) is { } taken ? ApiResponse.Json(Reauthed(taken)) : ApiResponse.NotFound;

        if (path.TrimEnd('/') == "/api/tunnel/guest" && request.Method == "POST")
        {
            if (!request.Local || ServerInfo.Shared || !int.TryParse(query["port"], out int port) || port is <= 0 or > 65535
                || !System.Net.IPAddress.TryParse(query["ip"], out System.Net.IPAddress? address))
                return new ApiResponse(403, null, []);
            TunnelGuests.Report(port, address);
            GuestSeen(port);
            return ApiResponse.Json("{}");
        }

        if (path.TrimEnd('/') == "/api/player" && request.Method == "POST")
            return ApiResponse.Json("{}", PlayerPackages.Offer(query["key"], request.Body, request.Encoding) ? 200 : 400);

        if (path.TrimEnd('/') == "/api/profile/export")
            return request.Local && !ServerInfo.Shared ? ApiResponse.Json(PlayerPackages.Export(ProfileOf(query))) : new ApiResponse(403, null, []);

        if (path.TrimEnd('/') == "/api/settings")
            return ProfileSettings.ForKey(query["key"]) is string settings ? ApiResponse.Json(settings) : ApiResponse.NotFound;

        if (path.TrimEnd('/') == "/api/templates")
            return ApiResponse.Json(JsonSerializer.Serialize(Templates.All.Select(template => new { template.Id, template.Name }), WorldJson));

        if (path.TrimEnd('/') == "/api/worlds/revision")
            return ApiResponse.Json(JsonSerializer.Serialize(new { revision = Revision.Value }));

        if (path.TrimEnd('/') == "/api/worlds/delete" && request.Method == "POST")
        {
            bool deleted = int.TryParse(query["id"], out int id) && DeleteWorld(id);
            return ApiResponse.Json("{}", deleted ? 200 : 409);
        }

        if (path.TrimEnd('/') == "/api/worlds/rename" && request.Method == "POST")
        {
            bool renamed = int.TryParse(query["id"], out int id)
                && query["name"] is { Length: > 0 } name
                && Stores.Worlds.Rename(id, name.Trim());
            return ApiResponse.Json("{}", renamed ? 200 : 400);
        }

        if (path.TrimEnd('/') == "/api/worlds/platformer" && request.Method == "POST")
        {
            bool changed = int.TryParse(query["id"], out int id) && Stores.Worlds.SetPlatformer(id, query["on"] is "1" or "true");
            return ApiResponse.Json("{}", changed ? 200 : 400);
        }

        if (path.TrimEnd('/') == "/api/worlds/export")
        {
            return int.TryParse(query["id"], out int id) && ExportWorld(id) is byte[] file
                ? new ApiResponse(200, "application/octet-stream", file)
                : ApiResponse.NotFound;
        }

        if (path.TrimEnd('/') == "/api/worlds/import" && request.Method == "POST")
        {
            string name = query["name"] is { Length: > 0 } given ? given : "Imported World";
            return ApiResponse.Json(JsonSerializer.Serialize(new { id = Session.ImportWorld(name, request.Body, query["client"]) }));
        }

        if (path.TrimEnd('/') == "/api/worlds/thumbnail" && request.Method == "POST")
        {
            bool saved = int.TryParse(query["id"], out int id) && Stores.Worlds.World(id) is not null && IsPng(request.Body);
            if (saved) Stores.Images.SaveImage(PlanetImage, id, request.Body);
            return ApiResponse.Json("{}", saved ? 200 : 400);
        }

        if (path.TrimEnd('/') == "/api/inventory" || path.StartsWith("/api/inventory/"))
            return request.Local && !ServerInfo.Shared
                ? InventoryApi.Route(path.TrimEnd('/'), request, ProfileOf(query))
                : new ApiResponse(403, null, []);

        if (path.TrimEnd('/') == "/api/avatars")
        {
            int owner = ProfileOf(query);
            int active = Stores.Profiles.ActiveAvatar(owner);
            return ApiResponse.Json(JsonSerializer.Serialize(Stores.Profiles.Avatars(owner).Select(avatar => new { id = avatar.Id, name = avatar.Name, active = avatar.Id == active })));
        }

        if (path.TrimEnd('/') == "/api/avatars/active" && request.Method == "POST")
        {
            if (!int.TryParse(query["id"], out int id)) return ApiResponse.Json("{}", 400);
            Stores.Profiles.SetActiveAvatar(ProfileOf(query), id);
            return ApiResponse.Json("{}");
        }

        if (path.TrimEnd('/') == "/api/avatars/rename" && request.Method == "POST")
        {
            bool renamed = int.TryParse(query["id"], out int id)
                && query["name"] is { Length: > 0 } name
                && Stores.Profiles.RenameAvatar(ProfileOf(query), id, name.Trim());
            return ApiResponse.Json("{}", renamed ? 200 : 400);
        }

        if (path.TrimEnd('/') == "/api/avatars/delete" && request.Method == "POST")
        {
            if (!request.Local || ServerInfo.Shared) return new ApiResponse(403, null, []);
            bool deleted = int.TryParse(query["id"], out int id) && Stores.Profiles.DeleteAvatar(ProfileOf(query), id);
            if (deleted) Stores.Images.DeleteImage(AvatarImage, id);
            return ApiResponse.Json("{}", deleted ? 200 : 409);
        }

        if (path.TrimEnd('/') == "/api/avatars/export")
        {
            return int.TryParse(query["id"], out int id) && AvatarFiles.Export(id) is { } skin
                ? new ApiResponse(200, "application/json", Encoding.UTF8.GetBytes(skin.ToJson()))
                : ApiResponse.NotFound;
        }

        if (path.TrimEnd('/') == "/api/avatars/import" && request.Method == "POST")
        {
            return AvatarFiles.Import(ProfileOf(query), request.Body) is int id
                ? ApiResponse.Json(JsonSerializer.Serialize(new { id }))
                : ApiResponse.Json("{}", 400);
        }

        if (path.TrimEnd('/') == "/api/worlds")
        {
            if (request.Method != "POST")
                return ApiResponse.Json(JsonSerializer.Serialize(Stores.Worlds.List(), WorldJson));
            string name = query["name"] is { Length: > 0 } given ? given : "New World";
            int created = Session.CreateWorld(name, query["template"]);
            if (query["platformer"] is "1" or "true")
                Stores.Worlds.SetPlatformer(created, true);
            return ApiResponse.Json(JsonSerializer.Serialize(new { id = created }));
        }

        if (Bundle(path) is byte[] asset)
            return new ApiResponse(200, "application/octet-stream", asset);

        if (path is "/ping" or "/disconnect")
            return ApiResponse.Json("{}");

        if (path.TrimEnd('/') == "/api/content")
            return ApiResponse.Json(CustomContent.Json(query["client"]));

        if (path.TrimEnd('/') == "/api/scripts")
            return new ApiResponse(200, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(ClientScripts.Manifest()));

        if (path.TrimEnd('/') == "/api/scripts/send" && request.Method == "POST")
            return ScriptMessages.Receive(Reauths.TokenOf(query["key"]), request.Body) ? new ApiResponse(204, null, []) : new ApiResponse(403, null, []);

        if (path.StartsWith("/api/scripts/") && ClientScripts.Find(path["/api/scripts/".Length..]) is byte[] script)
            return new ApiResponse(200, "application/octet-stream", script);

        if (path is "/reward")
            return ApiResponse.Json("""{"rewardEnabled":false,"timeInSeconds":0,"xp":0}""");

        if (path is "/shutdown" && request.Local && Shutdown is not null)
        {
            Shutdown();
            return ApiResponse.Json("{}");
        }

        if (path.StartsWith("/api/xp_level/"))
        {
            int profile = int.TryParse(query["profile_id"], out int id) ? id : 0;
            switch (path["/api/xp_level/".Length..].TrimEnd('/'))
            {
                case "init_data":
                    string root = request.Via is null ? ServerInfo.PublicUrl.TrimEnd('/') : "http://" + request.Via;
                    return ApiResponse.Json(Leveling.InitData(profile, root + "/badges/").ToJsonString());
                case "level":
                    return ApiResponse.Json(Leveling.LevelOf(profile).ToString());
                case "xp_limits_data":
                    return ApiResponse.Json(Leveling.Limits(int.TryParse(query["level"], out int level) ? level : 1).ToJsonString());
                case "xp":
                    NameValueCollection form = Form(request);
                    int formProfile = int.TryParse(form["profile_id"], out int p) ? p : 0;
                    int type = int.TryParse(form["xp_type_id"], out int t) ? t : 0;
                    return ApiResponse.Json($$"""{"XP":{{Leveling.Add(formProfile, type)}},"XPTypeID":{{type}}}""");
            }
        }

        if (path.StartsWith("/images/"))
        {
            string[] parts = path["/images/".Length..].Replace(".png", "").Split('/');
            if (parts.Length == 2 && int.TryParse(parts[0], out int type) && int.TryParse(parts[1], out int id)
                && Stores.Images.Image(type, id) is byte[] image)
                return new ApiResponse(200, "image/png", image);
        }

        if (path.StartsWith("/badges/custom/") && CustomBadges.Image(Uri.UnescapeDataString(Path.GetFileNameWithoutExtension(path))) is byte[] icon)
            return new ApiResponse(200, "image/png", icon);

        if (path.StartsWith("/badges/"))
        {
            string file = Path.Combine(AppContext.BaseDirectory, "data", "badges", Path.GetFileName(path));
            if (File.Exists(file))
                return new ApiResponse(200, "image/png", File.ReadAllBytes(file));
        }

        if (path.StartsWith("/api/"))
            return ApiResponse.Json("{}");

        return ApiResponse.NotFound;
    }

    string Reauthed((Reauths.Origin Origin, Reauths.Plan? Plan) taken)
    {
        (Reauths.Origin origin, Reauths.Plan? plan) = taken;
        Reauths.Origin next = origin with { World = plan?.World ?? origin.World, Mode = plan?.Mode ?? origin.Mode, Used = origin.Used + 1 };
        Console.WriteLine($"profile {next.Profile}: reauths into world {next.World} in {next.Mode.ToString().ToLowerInvariant()} mode");
        return Issue(next, plan?.Session);
    }

    string Issue(Reauths.Origin origin, IReadOnlyDictionary<string, object?>? overrides = null)
    {
        string session = SessionJson(origin.Profile, origin.Mode, origin.World, origin.Client, origin.Via);
        if (JsonNode.Parse(session) is not JsonObject issued) return session;
        string token = SessionTokens.Issue(origin.Profile, origin.Nickname);
        string root = origin.Via is null ? SessionLocator.PublicUrl : "http://" + origin.Via;
        issued["token"] = token;
        issued["reauthURL"] = $"{root}/session/reauth?token={token}";
        if (origin.Language is not null) issued["language"] = ClientLanguages.For(origin.Language, origin.Client);
        if (origin.WebSocket) issued["serverIP"] = "ws://" + issued["serverIP"]?.GetValue<string>();
        else if (origin.Tcp && ServerInfo.TcpPort is int tcp && tcp != ServerInfo.PhotonPort && issued["serverIP"]?.GetValue<string>() is string address)
            issued["serverIP"] = address[..(address.LastIndexOf(':') is int colon and > 0 ? colon : address.Length)] + ":" + tcp;
        foreach ((string key, object? value) in overrides ?? new Dictionary<string, object?>())
        {
            if (value is null) issued.Remove(key);
            else issued[key] = JsonSerializer.SerializeToNode(value, value.GetType());
        }
        Reauths.Remember(token, origin);
        return issued.ToJsonString();
    }

    byte[]? Thumbnail(int world) => ServerInfo.ThumbnailBytes() ?? Stores.Images.Image(PlanetImage, world > 0 ? world : ServerInfo.EntryWorld());

    static int WorldOf(NameValueCollection query) => int.TryParse(query["world"], out int world) && world > 0 ? world : 0;

    static bool IsPng(byte[] data) => data.Length <= MaxThumbnail && data.AsSpan().StartsWith(PngSignature);

    void GuestSeen(int port)
    {
        foreach (Player player in Sessions().SelectMany(session => session.Players.ToList()))
            if (player.Peer.EndPoint.Port == port && P2p.Tunnel.Carries(player.Peer.EndPoint.Address) && Bans.Holds(player))
            {
                Console.WriteLine($"peer {player.Peer.Id}: {player.Username} is banned");
                player.Peer.Disconnect();
            }
    }

    bool NameClaimed(string? name) =>
        name is not null && ServerInfo.Accounts == AccountMode.Optional && Stores.Profiles.Named(name) is int profile && Stores.Profiles.Claimed(profile);

    static ApiResponse Refused(string message) => ApiResponse.Json(JsonSerializer.Serialize(new { error = message }), 403);

    static bool Managing(string path) => path.TrimEnd('/') == "/shutdown"
        || new[] { "/api/worlds", "/api/avatars", "/api/inventory", "/api/templates", "/api/profile", "/api/admin" }.Any(area => path == area || path.StartsWith(area + "/"));

    static int ProfileOf(NameValueCollection query) =>
        int.TryParse(query["profile"], out int profile) && profile > 0 ? profile : 1;

    static NameValueCollection Form(ApiRequest request)
    {
        string body = Encoding.Latin1.GetString(request.Body);
        if (request.ContentType is not string type || !type.StartsWith("multipart/form-data")
            || Regex.Match(type, "boundary=\"?([^\";]+)") is not { Success: true } boundary)
            return HttpUtility.ParseQueryString(body);

        var form = new NameValueCollection();
        foreach (string part in body.Split("--" + boundary.Groups[1].Value))
        {
            int split = part.IndexOf("\r\n\r\n");
            if (split < 0 || Regex.Match(part[..split], "name=\"([^\"]*)\"") is not { Success: true } name) continue;
            string value = part[(split + 4)..];
            form[name.Groups[1].Value] = value.EndsWith("\r\n") ? value[..^2] : value;
        }
        return form;
    }

    byte[]? Bundle(string path)
    {
        if (path.StartsWith("/bundles/"))
        {
            string legacy = Uri.UnescapeDataString(path["/bundles/".Length..]);
            return CustomContent.Bundle(BundleSets.Legacy, legacy) ?? Assets?.Get(legacy);
        }
        if (!path.StartsWith("/bundles-")) return null;

        int slash = path.IndexOf('/', "/bundles-".Length);
        if (slash < 0) return null;
        string set = path["/bundles-".Length..slash];
        string file = BundleSets.FileOf(Uri.UnescapeDataString(path[(slash + 1)..]));
        if (CustomContent.Bundle(set, file, name => Original(set, name)) is { } custom) return custom;
        return Original(set, file) is { } original ? CustomContent.Replace(set, file, original) : null;
    }

    byte[]? Original(string set, string file)
    {
        if (set == "3x") return LegacyAssets?.Get(file);
        if (!AssetSets.ContainsKey(set)) return null;

        foreach (string candidate in NearestSets(set))
            if (AssetSets[candidate].Get(file) is { } data)
                return data;
        return null;
    }

    IEnumerable<string> NearestSets(string set)
    {
        const int Neighbours = 6;
        bool webgl = set.EndsWith(BundleSets.WebGL);
        string engine = BundleSets.EngineOf(Base(set));
        List<string> ordered = [.. AssetSets.Keys
            .Where(name => name.EndsWith(BundleSets.WebGL) == webgl && BundleSets.EngineOf(Base(name)) == engine)
            .OrderBy(name => int.TryParse(Base(name).TrimStart('v'), out int number) ? number : 0)];
        int index = ordered.IndexOf(set);
        return ordered.Select((name, position) => (name, distance: Math.Abs(position - index)))
            .OrderBy(entry => entry.distance)
            .Take(Neighbours + 1)
            .Select(entry => entry.name);
    }

    static string Base(string set) => set.EndsWith(BundleSets.WebGL) ? set[..^BundleSets.WebGL.Length] : set;
}
