using System.Net;
using System.Text;
using System.Text.Json;
using System.Web;
using OpenKogama.Game;
using OpenKogama.Storage;

namespace OpenKogama.Web;

public sealed class HttpServer
{
    static readonly JsonSerializerOptions WorldJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    readonly HttpListener _listener = new();

    public Func<int, GameMode, int, string> SessionJson { get; set; } = (_, _, _) => "{}";
    public AssetCache? Assets { get; set; }
    public Func<int, bool> DeleteWorld { get; set; } = _ => false;

    public HttpServer(string prefix) => _listener.Prefixes.Add(prefix);

    public void Run()
    {
        _listener.Start();

        while (true)
        {
            HttpListenerContext context = _listener.GetContext();
            string path = context.Request.Url?.AbsolutePath ?? "/";

            try
            {
                Route(path, context.Request, context.Response);
            }
            catch (Exception error)
            {
                Console.WriteLine($"http: {path}: {error.Message}");
                try
                {
                    context.Response.StatusCode = 500;
                    context.Response.Close();
                }
                catch (Exception)
                {
                }
            }
        }
    }

    void Route(string path, HttpListenerRequest request, HttpListenerResponse response)
    {
        if (path is "/" or "/session")
        {
            int profile = int.TryParse(request.QueryString["profile"], out int requested) && requested > 0 ? requested : 1;
            GameMode mode = request.QueryString["mode"] switch
            {
                "play" => GameMode.Play,
                "avatar" => GameMode.CharacterEditor,
                _ => GameMode.Edit,
            };
            int world = int.TryParse(request.QueryString["world"], out int chosen) ? chosen : 0;
            Json(response, SessionJson(profile, mode, world));
            return;
        }

        if (path.TrimEnd('/') == "/api/templates")
        {
            Json(response, JsonSerializer.Serialize(Templates.All.Select(template => new { template.Id, template.Name }), WorldJson));
            return;
        }

        if (path.TrimEnd('/') == "/api/worlds/revision")
        {
            Json(response, JsonSerializer.Serialize(new { revision = Revision.Value }));
            return;
        }

        if (path.TrimEnd('/') == "/api/worlds/delete" && request.HttpMethod == "POST")
        {
            bool deleted = int.TryParse(request.QueryString["id"], out int id) && DeleteWorld(id);
            response.StatusCode = deleted ? 200 : 409;
            Json(response, "{}");
            return;
        }

        if (path.TrimEnd('/') == "/api/worlds/rename" && request.HttpMethod == "POST")
        {
            bool renamed = int.TryParse(request.QueryString["id"], out int id)
                && request.QueryString["name"] is { Length: > 0 } name
                && Stores.Worlds.Rename(id, name.Trim());
            response.StatusCode = renamed ? 200 : 400;
            Json(response, "{}");
            return;
        }

        if (path.TrimEnd('/') == "/api/worlds/import" && request.HttpMethod == "POST")
        {
            using var body = new MemoryStream();
            request.InputStream.CopyTo(body);
            string name = request.QueryString["name"] is { Length: > 0 } given ? given : "Imported World";
            Json(response, JsonSerializer.Serialize(new { id = Session.ImportWorld(name, body.ToArray()) }));
            return;
        }

        if (path.TrimEnd('/') == "/api/worlds")
        {
            if (request.HttpMethod == "POST")
            {
                string name = request.QueryString["name"] is { Length: > 0 } given ? given : "New World";
                Json(response, JsonSerializer.Serialize(new { id = Session.CreateWorld(name, request.QueryString["template"]) }));
            }
            else
            {
                Json(response, JsonSerializer.Serialize(Stores.Worlds.List(), WorldJson));
            }
            return;
        }

        if (path.StartsWith("/bundles/") && Assets?.Get(Uri.UnescapeDataString(path["/bundles/".Length..])) is byte[] asset)
        {
            response.ContentType = "application/octet-stream";
            response.ContentLength64 = asset.Length;
            response.OutputStream.Write(asset);
            response.Close();
            return;
        }

        if (path is "/ping" or "/disconnect")
        {
            Json(response, "{}");
            return;
        }

        if (path is "/shutdown" && IPAddress.IsLoopback(request.RemoteEndPoint.Address))
        {
            Json(response, "{}");
            Environment.Exit(0);
        }

        if (path.StartsWith("/api/xp_level/"))
        {
            int profile = int.TryParse(request.QueryString["profile_id"], out int id) ? id : 0;
            switch (path["/api/xp_level/".Length..].TrimEnd('/'))
            {
                case "init_data":
                    Json(response, Leveling.InitData(profile, "http://127.0.0.1:8080/badges/").ToJsonString());
                    return;
                case "level":
                    Json(response, Leveling.LevelOf(profile).ToString());
                    return;
                case "xp_limits_data":
                    Json(response, Leveling.Limits(int.TryParse(request.QueryString["level"], out int level) ? level : 1).ToJsonString());
                    return;
                case "xp":
                    var form = HttpUtility.ParseQueryString(new StreamReader(request.InputStream).ReadToEnd());
                    int formProfile = int.TryParse(form["profile_id"], out int p) ? p : 0;
                    int type = int.TryParse(form["xp_type_id"], out int t) ? t : 0;
                    Json(response, $$"""{"XP":{{Leveling.Add(formProfile, type)}},"XPTypeID":{{type}}}""");
                    return;
            }
        }

        if (path.StartsWith("/images/"))
        {
            string[] parts = path["/images/".Length..].Replace(".png", "").Split('/');
            if (parts.Length == 2 && int.TryParse(parts[0], out int type) && int.TryParse(parts[1], out int id)
                && Stores.Images.Image(type, id) is byte[] image)
            {
                response.ContentType = "image/png";
                response.ContentLength64 = image.Length;
                response.OutputStream.Write(image);
                response.Close();
                return;
            }
        }

        if (path.StartsWith("/badges/"))
        {
            string file = Path.Combine(AppContext.BaseDirectory, "data", "badges", Path.GetFileName(path));
            if (File.Exists(file))
            {
                byte[] image = File.ReadAllBytes(file);
                response.ContentType = "image/png";
                response.ContentLength64 = image.Length;
                response.OutputStream.Write(image);
                response.Close();
                return;
            }
        }

        if (path.StartsWith("/api/"))
        {
            Json(response, "{}");
            return;
        }

        response.StatusCode = 404;
        response.Close();
    }

    static void Json(HttpListenerResponse response, string body)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(body);
        response.ContentType = "application/json";
        response.ContentLength64 = bytes.Length;
        response.OutputStream.Write(bytes);
        response.Close();
    }
}
