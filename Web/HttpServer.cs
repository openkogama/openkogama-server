using System.Net;

namespace OpenKogama.Web;

public sealed class HttpServer(string prefix, WebApi api)
{
    readonly HttpListener _listener = new() { Prefixes = { prefix } };

    public void Run()
    {
        _listener.Start();

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

        using var body = new MemoryStream();
        if (request.HasEntityBody) request.InputStream.CopyTo(body);
        ApiResponse result = api.Route(new ApiRequest(request.HttpMethod, path, request.QueryString, request.ContentType, body.ToArray(),
            IPAddress.IsLoopback(request.RemoteEndPoint.Address)));

        response.StatusCode = result.Status;
        if (result.ContentType is not null) response.ContentType = result.ContentType;
        response.ContentLength64 = result.Body.Length;
        response.OutputStream.Write(result.Body);
        response.Close();
    }
}
