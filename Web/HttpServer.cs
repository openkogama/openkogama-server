using System.Net;
using System.Text;

namespace OpenKogama.Web;

public sealed class HttpServer
{
    readonly HttpListener _listener = new();

    public string SessionJson { get; set; } = "";

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
                Route(path, context.Response);
            }
            catch (Exception error)
            {
                Console.WriteLine($"http: {path}: {error.Message}");
            }
        }
    }

    void Route(string path, HttpListenerResponse response)
    {
        if (path is "/" or "/session")
        {
            Json(response, SessionJson);
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
