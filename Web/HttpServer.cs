using System.Net;
using OpenKogama.P2p;

namespace OpenKogama.Web;

public sealed class HttpServer(string prefix, WebApi api)
{
    const long MaxRemoteBody = 16 * 1024 * 1024;

    readonly HttpListener _listener = new() { Prefixes = { prefix } };

    public void Start() => _listener.Start();

    public void Run()
    {
        if (!_listener.IsListening) _listener.Start();

        while (true)
        {
            HttpListenerContext context = _listener.GetContext();
            string path = context.Request.Url?.AbsolutePath ?? "/";

            try
            {
                Serve(path, context.Request, context.Response);
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

    void Serve(string path, HttpListenerRequest request, HttpListenerResponse response)
    {
        response.AddHeader("Access-Control-Allow-Origin", "*");
        if (request.HttpMethod == "OPTIONS")
        {
            response.AddHeader("Access-Control-Allow-Headers", request.Headers["Access-Control-Request-Headers"] ?? "*");
            response.AddHeader("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS");
            response.StatusCode = 204;
            response.Close();
            return;
        }

        IPAddress remote = Game.Access.Plain(request.RemoteEndPoint.Address);
        bool tunneled = Tunnel.Carries(remote);
        IPAddress? forwarded = Forwarded(request, remote, tunneled);
        string? via = tunneled ? Tunnel.Via(request.Headers["Host"]) : null;
        if (tunneled && via is null)
        {
            response.StatusCode = 400;
            response.Close();
            return;
        }

        bool local = IPAddress.IsLoopback(remote) && !tunneled && forwarded is null;
        using var body = new MemoryStream();
        if (request.HasEntityBody && !Read(request.InputStream, body, local ? long.MaxValue : MaxRemoteBody))
        {
            response.StatusCode = 413;
            response.Close();
            return;
        }
        ApiRequest call = new(request.HttpMethod, path, request.QueryString, request.ContentType, body.ToArray(),
            local, via, request.Headers["Content-Encoding"], forwarded ?? (tunneled ? null : remote),
            request.Headers["Origin"] ?? (request.Headers["Sec-Fetch-Site"] is "cross-site" or "same-site" ? "null" : null));
        if (api.Waiting(call) is Task<ApiResponse> waiting)
        {
            waiting.ContinueWith(task =>
            {
                try
                {
                    Write(request, response, task.IsCompletedSuccessfully ? task.Result : new ApiResponse(500, null, []));
                }
                catch (Exception)
                {
                }
            });
            return;
        }
        Write(request, response, api.Route(call));
    }

    static bool Read(Stream input, MemoryStream body, long limit)
    {
        byte[] buffer = new byte[81920];
        int read;
        while ((read = input.Read(buffer)) > 0)
        {
            if (body.Length + read > limit) return false;
            body.Write(buffer, 0, read);
        }
        return true;
    }

    static void Write(HttpListenerRequest request, HttpListenerResponse response, ApiResponse result)
    {
        if (Handlers.OperationRouter.Trace) Console.WriteLine($"http {request.HttpMethod} {request.Url?.PathAndQuery} {result.Status} {result.Body.Length}");
        response.StatusCode = result.Status;
        if (result.Location is not null) response.RedirectLocation = result.Location;
        if (result.ContentType is not null) response.ContentType = result.ContentType;
        response.ContentLength64 = result.Body.Length;
        response.OutputStream.Write(result.Body);
        response.Close();
    }

    IPAddress? Forwarded(HttpListenerRequest request, IPAddress remote, bool tunneled)
    {
        bool proxy = !tunneled && (IPAddress.IsLoopback(remote) || remote.ToString() == api.ServerInfo.ListenHost);
        string? client = request.Headers["X-Forwarded-For"]?.Split(',')[^1].Trim();
        return proxy && IPAddress.TryParse(client, out IPAddress? address) ? Game.Access.Plain(address) : null;
    }
}
