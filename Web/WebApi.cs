using System.Collections.Specialized;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using OpenKogama.Game;
using OpenKogama.Storage;

namespace OpenKogama.Web;

public sealed record ApiRequest(string Method, string Path, NameValueCollection Query, string? ContentType, byte[] Body, bool Local);

public sealed record ApiResponse(int Status, string? ContentType, byte[] Body)
{
    public static readonly ApiResponse NotFound = new(404, null, []);

    public static ApiResponse Json(string body, int status = 200) => new(status, "application/json", Encoding.UTF8.GetBytes(body));
}

public sealed class WebApi
{
    static readonly JsonSerializerOptions WorldJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public Func<int, GameMode, int, string?, string> SessionJson { get; set; } = (_, _, _, _) => "{}";
    public AssetCache? Assets { get; set; }
    public AssetCache? LegacyAssets { get; set; }
    public Dictionary<string, AssetCache> AssetSets { get; init; } = [];
    public Func<int, bool> DeleteWorld { get; set; } = _ => false;
    public Func<int, byte[]?> ExportWorld { get; set; } = _ => null;
    public Action? Shutdown { get; set; }

    public ApiResponse Route(ApiRequest request)
    {
        string path = request.Path;
        NameValueCollection query = request.Query;

        if (SessionLocator.Answer(path, query) is string located)
            return new ApiResponse(200, "text/html", Encoding.UTF8.GetBytes(located));

        if (path == "/crossdomain.xml")
            return new ApiResponse(200, "text/xml", Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><cross-domain-policy><allow-access-from domain=\"*\"/></cross-domain-policy>"));

        if (path is "/" or "/session")
        {
            int profile = int.TryParse(query["profile"], out int requested) && requested > 0 ? requested : 1;
            GameMode mode = query["mode"] switch
            {
                "play" => GameMode.Play,
                "avatar" => GameMode.CharacterEditor,
                _ => GameMode.Edit,
            };
            int world = int.TryParse(query["world"], out int chosen) ? chosen : 0;
            ClientEngines.Remember(profile, query["unity"]);
            string session = SessionJson(profile, mode, world, query["client"]);
            if (query["transport"] == "ws" && System.Text.Json.Nodes.JsonNode.Parse(session) is System.Text.Json.Nodes.JsonObject web)
            {
                web["serverIP"] = "ws://" + web["serverIP"]?.GetValue<string>();
                session = web.ToJsonString();
            }
            return ApiResponse.Json(session);
        }

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

        if (path.TrimEnd('/') == "/api/avatars")
        {
            int owner = ProfileOf(query);
            int active = Stores.Profiles.ActiveAvatar(owner);
            return ApiResponse.Json(JsonSerializer.Serialize(Stores.Profiles.Avatars(owner).Select(avatar => new { id = avatar.Id, active = avatar.Id == active })));
        }

        if (path.TrimEnd('/') == "/api/avatars/active" && request.Method == "POST")
        {
            if (!int.TryParse(query["id"], out int id)) return ApiResponse.Json("{}", 400);
            Stores.Profiles.SetActiveAvatar(ProfileOf(query), id);
            return ApiResponse.Json("{}");
        }

        if (path.TrimEnd('/') == "/api/avatars/export")
        {
            return int.TryParse(query["id"], out int id) && AvatarFiles.Export(id) is { } skin
                ? new ApiResponse(200, "application/json", Encoding.UTF8.GetBytes(skin.ToJson()))
                : ApiResponse.NotFound;
        }

        if (path.TrimEnd('/') == "/api/avatars/import" && request.Method == "POST")
        {
            return AvatarFiles.Import(ProfileOf(query), Encoding.UTF8.GetString(request.Body)) is int id
                ? ApiResponse.Json(JsonSerializer.Serialize(new { id }))
                : ApiResponse.Json("{}", 400);
        }

        if (path.TrimEnd('/') == "/api/worlds")
        {
            if (request.Method != "POST")
                return ApiResponse.Json(JsonSerializer.Serialize(Stores.Worlds.List(), WorldJson));
            string name = query["name"] is { Length: > 0 } given ? given : "New World";
            return ApiResponse.Json(JsonSerializer.Serialize(new { id = Session.CreateWorld(name, query["template"]) }));
        }

        if (Bundle(path) is byte[] asset)
            return new ApiResponse(200, "application/octet-stream", asset);

        if (path is "/ping" or "/disconnect")
            return ApiResponse.Json("{}");

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
                    return ApiResponse.Json(Leveling.InitData(profile, "http://127.0.0.1:8080/badges/").ToJsonString());
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
        if (path.StartsWith("/bundles/")) return Assets?.Get(Uri.UnescapeDataString(path["/bundles/".Length..]));
        if (!path.StartsWith("/bundles-")) return null;

        int slash = path.IndexOf('/', "/bundles-".Length);
        if (slash < 0) return null;
        string set = path["/bundles-".Length..slash];
        string file = BundleSets.FileOf(Uri.UnescapeDataString(path[(slash + 1)..]));
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
