using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenKogama.P2p;

namespace OpenKogama.PublicList;

public sealed class ServerList(int port, string bans)
{
    const int PollMs = 20_000;
    const long StaleMs = 90_000;
    const long PlayingMs = 180_000;
    const long ForgetMs = 600_000;
    const int PerSource = 5;
    const int MaxBody = 4096;
    const int MaxImage = 64 * 1024;
    static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF];
    static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47];
    const int MaxTitle = 40;
    const int MaxClients = 10;
    const int MaxPlaying = 100_000;

    sealed class Entry(string address, bool code)
    {
        public string Address => address;
        public bool Code => code;
        public string Title = "";
        public string[] Clients = [];
        public bool Thumbnail;
        public byte[]? Image;
        public string Source = "";
        public long Announced;
        public long Seen;
    }

    readonly object _sync = new();
    readonly Dictionary<string, Entry> _entries = [];
    readonly Dictionary<string, (string Address, long Seen)> _playing = [];
    readonly HttpClient _http = new(new SocketsHttpHandler { ConnectCallback = ConnectPublicAsync, AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(5),
    };

    public static int Run(string[] options)
    {
        int port = int.TryParse(Tunnel.Option(options, "port"), out int given) ? given : 8099;
        new ServerList(port, Tunnel.Option(options, "bans") ?? "bans.txt").Serve();
        return 0;
    }

    void Serve()
    {
        var listener = new HttpListener { Prefixes = { $"http://127.0.0.1:{port}/" } };
        listener.Start();
        Console.WriteLine($"server list {port}");
        _ = Task.Run(PollAsync);
        while (true)
        {
            HttpListenerContext context = listener.GetContext();
            _ = Task.Run(() => HandleAsync(context));
        }
    }

    async Task HandleAsync(HttpListenerContext context)
    {
        HttpListenerRequest request = context.Request;
        HttpListenerResponse response = context.Response;
        try
        {
            string path = request.Url?.AbsolutePath.TrimEnd('/') ?? "";
            if (request.HttpMethod == "GET" && path == "/servers")
                await ReplyAsync(response, 200, Listed());
            else if (request.HttpMethod == "POST" && path == "/announce")
                await ReplyAsync(response, await AnnounceAsync(request), "{}");
            else if (request.HttpMethod == "POST" && path == "/playing")
                await ReplyAsync(response, await PlayingAsync(request), "{}");
            else if (request.HttpMethod == "POST" && path == "/thumbnail")
                await ReplyAsync(response, await UploadAsync(request), "{}");
            else if (request.HttpMethod == "GET" && path == "/thumbnail" && Image(request.QueryString["address"]) is byte[] image)
                await ImageAsync(response, image);
            else
                await ReplyAsync(response, 404, "{}");
        }
        catch (Exception error)
        {
            Console.WriteLine($"server list: {error.Message}");
            try
            {
                response.StatusCode = 500;
                response.Close();
            }
            catch (Exception)
            {
            }
        }
    }

    static async Task ReplyAsync(HttpListenerResponse response, int status, string json)
    {
        byte[] body = Encoding.UTF8.GetBytes(json);
        response.StatusCode = status;
        response.ContentType = "application/json";
        response.AddHeader("Access-Control-Allow-Origin", "*");
        response.ContentLength64 = body.Length;
        await response.OutputStream.WriteAsync(body);
        response.Close();
    }

    string Listed()
    {
        HashSet<string> banned = Banned();
        long now = Environment.TickCount64;
        List<Entry> visible;
        var players = new Dictionary<string, int>();
        lock (_sync)
        {
            visible = [.. _entries.Values.Where(entry => now - entry.Seen < StaleMs && !banned.Contains(entry.Address) && !banned.Contains(entry.Source))];
            foreach ((string source, (string address, long seen)) in _playing)
                if (now - seen < PlayingMs && !banned.Contains(source))
                    players[address] = players.GetValueOrDefault(address) + 1;
        }
        var list = new JsonArray();
        foreach (Entry entry in visible.OrderByDescending(entry => players.GetValueOrDefault(entry.Address)).ThenBy(entry => entry.Title, StringComparer.OrdinalIgnoreCase))
            list.Add(new JsonObject
            {
                ["address"] = entry.Address,
                ["title"] = entry.Title,
                ["clients"] = new JsonArray([.. entry.Clients.Select(client => (JsonNode)client)]),
                ["players"] = players.GetValueOrDefault(entry.Address),
                ["thumbnail"] = entry.Thumbnail || entry.Image is not null,
            });
        return list.ToJsonString();
    }

    static async Task ImageAsync(HttpListenerResponse response, byte[] image)
    {
        response.StatusCode = 200;
        response.ContentType = image[0] == 0xFF ? "image/jpeg" : "image/png";
        response.AddHeader("Access-Control-Allow-Origin", "*");
        response.ContentLength64 = image.Length;
        await response.OutputStream.WriteAsync(image);
        response.Close();
    }

    static async Task<JsonObject?> BodyAsync(HttpListenerRequest request, int limit = MaxBody)
    {
        var buffer = new byte[limit + 1];
        int read = 0;
        while (read < buffer.Length && await request.InputStream.ReadAsync(buffer.AsMemory(read)) is int chunk and > 0) read += chunk;
        return read <= limit ? JsonNode.Parse(buffer.AsSpan(0, read)) as JsonObject : null;
    }

    async Task<int> UploadAsync(HttpListenerRequest request)
    {
        if (await BodyAsync(request, MaxImage * 2) is not JsonObject body || body["secret"]?.GetValue<string>() is not { Length: >= 16 } secret
            || body["image"]?.GetValue<string>() is not string encoded)
            return 400;
        byte[] image;
        try
        {
            image = Convert.FromBase64String(encoded);
        }
        catch (FormatException)
        {
            return 400;
        }
        if (image.Length is 0 or > MaxImage || !(image.AsSpan().StartsWith(Jpeg) || image.AsSpan().StartsWith(Png))) return 400;
        lock (_sync)
        {
            if (!_entries.TryGetValue("p2p:" + Tunnel.CodeOf(secret), out Entry? entry)) return 404;
            entry.Image = image;
        }
        return 200;
    }

    byte[]? Image(string? address)
    {
        if (address is null || Listing(address) is not string listed) return null;
        lock (_sync) return _entries.TryGetValue(listed, out Entry? entry) ? entry.Image : null;
    }

    async Task<int> PlayingAsync(HttpListenerRequest request)
    {
        if (await BodyAsync(request) is not JsonObject body || body["address"]?.GetValue<string>() is not string text
            || Listing(text) is not string address)
            return 400;
        string source = SourceOf(request);
        lock (_sync)
        {
            if (Flag(body["left"]))
            {
                if (_playing.TryGetValue(source, out var playing) && playing.Address == address) _playing.Remove(source);
                return 200;
            }
            if (!_entries.ContainsKey(address)) return 404;
            if (_playing.Count >= MaxPlaying && !_playing.ContainsKey(source)) return 429;
            _playing[source] = (address, Environment.TickCount64);
        }
        return 200;
    }

    async Task<int> AnnounceAsync(HttpListenerRequest request)
    {
        if (await BodyAsync(request) is not JsonObject body) return 400;
        string source = SourceOf(request);
        long now = Environment.TickCount64;
        bool left = Flag(body["left"]);

        if (body["url"]?.GetValue<string>() is string url)
        {
            if (Address(url) is not string address) return 400;
            if (left) return Forget(address, source);
            if (Claim(address, code: false, source, now) is not Entry entry) return 429;
            if (entry.Seen == 0) _ = Task.Run(() => CheckAsync(entry));
            return 200;
        }

        if (left && body["secret"]?.GetValue<string>() is { Length: >= 16 } leaving)
            return Forget("p2p:" + Tunnel.CodeOf(leaving), source: null);

        if (body["secret"]?.GetValue<string>() is { Length: >= 16 } secret && body["info"] is JsonObject info)
        {
            if (Claim("p2p:" + Tunnel.CodeOf(secret), code: true, source, now) is not Entry entry) return 429;
            lock (_sync)
            {
                entry.Title = Clean(info["title"]?.GetValue<string>());
                entry.Clients = Clients(info);
                entry.Seen = now;
            }
            return 200;
        }
        return 400;
    }

    Entry? Claim(string address, bool code, string source, long now)
    {
        lock (_sync)
        {
            if (!_entries.TryGetValue(address, out Entry? entry))
            {
                if (_entries.Values.Count(other => other.Source == source) >= PerSource) return null;
                entry = new Entry(address, code) { Source = source };
                _entries[address] = entry;
            }
            entry.Announced = now;
            return entry;
        }
    }

    int Forget(string address, string? source)
    {
        lock (_sync)
        {
            if (!_entries.TryGetValue(address, out Entry? entry) || (source is not null && entry.Source != source)) return 200;
            _entries.Remove(address);
            foreach (string player in _playing.Where(play => play.Value.Address == address).Select(play => play.Key).ToList())
                _playing.Remove(player);
        }
        return 200;
    }

    static bool Flag(JsonNode? node) => node is JsonValue value && value.TryGetValue(out bool flag) && flag;

    async Task PollAsync()
    {
        while (true)
        {
            await Task.Delay(PollMs);
            long now = Environment.TickCount64;
            List<Entry> checking;
            lock (_sync)
            {
                foreach (string gone in _entries.Values.Where(entry => now - entry.Announced > ForgetMs).Select(entry => entry.Address).ToList())
                    _entries.Remove(gone);
                foreach (string left in _playing.Where(play => now - play.Value.Seen > PlayingMs).Select(play => play.Key).ToList())
                    _playing.Remove(left);
                checking = [.. _entries.Values.Where(entry => !entry.Code)];
            }
            await Task.WhenAll(checking.Select(CheckAsync));
        }
    }

    async Task CheckAsync(Entry entry)
    {
        try
        {
            using HttpResponseMessage response = await _http.GetAsync(entry.Address + "/api/server", HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxBody) return;
            if (JsonNode.Parse(await response.Content.ReadAsStringAsync()) is not JsonObject server
                || server["public"]?.GetValue<bool>() is not true) return;
            lock (_sync)
            {
                entry.Title = Clean(server["title"]?.GetValue<string>());
                entry.Clients = Clients(server);
                entry.Thumbnail = server["thumbnail"]?.GetValue<string>() == "/api/server/thumbnail";
                entry.Seen = Environment.TickCount64;
            }
        }
        catch (Exception)
        {
        }
    }

    HashSet<string> Banned()
    {
        try
        {
            return File.Exists(bans)
                ? [.. File.ReadAllLines(bans).Select(line => line.Trim()).Where(line => line.Length > 0 && !line.StartsWith('#'))]
                : [];
        }
        catch (IOException)
        {
            return [];
        }
    }

    static string SourceOf(HttpListenerRequest request)
    {
        IPAddress remote = request.RemoteEndPoint.Address;
        string? forwarded = request.Headers["X-Forwarded-For"]?.Split(',')[0].Trim();
        return IPAddress.IsLoopback(remote) && !string.IsNullOrEmpty(forwarded) ? forwarded : remote.ToString();
    }

    static string? Address(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("http" or "https")
            || uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.UserInfo))
            return null;
        return uri.GetLeftPart(UriPartial.Authority).ToLowerInvariant();
    }

    static string? Listing(string text) =>
        text.StartsWith("p2p:", StringComparison.OrdinalIgnoreCase)
            ? Tunnel.Normalize(text[4..]) is string code ? "p2p:" + code : null
            : Address(text);

    static string Clean(string? text) =>
        string.Concat((text ?? "").Where(letter => !char.IsControl(letter)).Take(MaxTitle)).Trim();

    static string[] Clients(JsonObject source)
    {
        if (source["clients"] is JsonArray listed)
            return [.. listed.Select(value => value?.GetValue<string>()?.Trim() ?? "").Where(client => client.Length is > 0 and <= 64).Take(MaxClients)];
        return source["version"]?.GetValue<string>() is { Length: > 0 and <= 64 } version ? [version] : [];
    }

    static async ValueTask<Stream> ConnectPublicAsync(SocketsHttpConnectionContext context, CancellationToken cancel)
    {
        IPAddress[] addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancel);
        IPAddress target = addresses.FirstOrDefault(Public) ?? throw new HttpRequestException("not a public address");
        var socket = new Socket(target.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(new IPEndPoint(target, context.DnsEndPoint.Port), cancel);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch (Exception)
        {
            socket.Dispose();
            throw;
        }
    }

    static bool Public(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address) || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6UniqueLocal) return false;
        if (address.AddressFamily != AddressFamily.InterNetwork) return true;
        byte[] bytes = address.GetAddressBytes();
        return bytes[0] switch
        {
            0 or 10 or 127 => false,
            100 => bytes[1] is < 64 or > 127,
            169 => bytes[1] != 254,
            172 => bytes[1] is < 16 or > 31,
            192 => bytes[1] != 168,
            _ => bytes[0] < 224,
        };
    }
}
