using System.Net;
using System.Text;

namespace OpenKogama.Web;

public sealed class HttpServer
{
    readonly HttpListener _listener = new();

    public Func<int, string> SessionJson { get; set; } = _ => "{}";

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
            }
        }
    }

    void Route(string path, HttpListenerRequest request, HttpListenerResponse response)
    {
        if (path is "/" or "/session")
        {
            int profile = int.TryParse(request.QueryString["profile"], out int requested) && requested > 0 ? requested : 1;
            Json(response, SessionJson(profile));
            return;
        }

        if (path is "/ping" or "/disconnect")
        {
            Json(response, "{}");
            return;
        }

        if (path.StartsWith("/api/xp_level/init_data"))
        {
            Json(response, """
            {
              "XPManagerData": {},
              "BadgeUrlData": [],
              "Level": 1,
              "XP": 0,
              "XPLevelLimits": { "Levels": [] },
              "MinPlayersActivateXP": 2
            }
            """);
            return;
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
