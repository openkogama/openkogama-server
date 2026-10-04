using System.Collections.Specialized;
using System.Net;
using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using System.Web;
using OpenKogama;
using OpenKogama.Game;
using OpenKogama.Hosting;
using OpenKogama.Photon;
using OpenKogama.Storage;
using OpenKogama.Web;

namespace OpenKogama.Browser;

public static partial class BrowserHost
{
    const string DatabaseFile = "server.db";
    const int SaveIntervalMs = 5000;
    const int TickIntervalMs = 50;

    static readonly Dictionary<int, PhotonPeer> Peers = [];
    static PhotonServer _server = null!;
    static SessionHost _host = null!;
    static WebApi _api = null!;
    static Database _database = null!;
    static int _nextConnection;

    public static async Task Main()
    {
        ExtractData();
        if (await LoadDatabase() is { Length: > 0 } saved)
            File.WriteAllBytes(DatabaseFile, Convert.FromBase64String(saved));

        _database = new Database(DatabaseFile, writeAhead: false);
        Stores.Profiles = new SqliteProfileStore(_database);
        Stores.Worlds = new SqliteWorldStore(_database);
        Stores.Friends = new SqliteFriendStore(_database);
        Stores.Images = new SqliteImageStore(_database);
        Stores.Market = new SqliteMarketStore(_database);
        Session.EnsureDefaultWorld();

        _server = new PhotonServer { Log = Console.WriteLine };
        _host = new SessionHost(_server, false);
        _server.Disconnected += (peer, _) => _host.Disconnect(peer);
        _server.Operation = _host.Handle;

        var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        _api = new WebApi
        {
            SessionJson = (profile, mode, world, client) => JsonSerializer.Serialize(
                new SessionData("127.0.0.1:5055", profile, world, mode, "en_US", false, profile.ToString(),
                    "http://127.0.0.1:8080/ping", "http://127.0.0.1:8080/disconnect", "http://127.0.0.1:8080/reward", client),
                json),
            DeleteWorld = _host.DeleteWorld,
            ExportWorld = _host.ExportWorld,
        };

        _ = _server.TickAsync();
        _ = Loop(TickIntervalMs, _host.Tick);
        _ = Loop(SaveIntervalMs, Save);
        Console.WriteLine("openkogama browser server ready");
    }

    static void ExtractData()
    {
        var assembly = typeof(BrowserHost).Assembly;
        foreach (string name in assembly.GetManifestResourceNames().Where(name => name.StartsWith("data/")))
        {
            string path = Path.Combine(AppContext.BaseDirectory, name.Replace('\\', '/'));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using Stream resource = assembly.GetManifestResourceStream(name)!;
            using FileStream file = File.Create(path);
            resource.CopyTo(file);
        }
    }

    static async Task Loop(int interval, Action action)
    {
        while (true)
        {
            await Task.Delay(interval);
            try
            {
                action();
            }
            catch (Exception error)
            {
                Console.WriteLine(error);
            }
        }
    }

    [JSExport]
    public static void Save()
    {
        _host.SaveAll();
        SaveDatabase(Convert.ToBase64String(File.ReadAllBytes(DatabaseFile)));
    }

    [JSExport]
    public static int Connect()
    {
        int id = ++_nextConnection;
        Peers[id] = _server.Accept(new IPEndPoint(IPAddress.Loopback, id), data => Send(id, data), () => Close(id));
        return id;
    }

    [JSExport]
    public static void Receive(int id, byte[] data)
    {
        if (Peers.TryGetValue(id, out PhotonPeer? peer)) _server.Receive(peer, data);
    }

    [JSExport]
    public static void Disconnect(int id)
    {
        if (Peers.Remove(id, out PhotonPeer? peer)) _server.Close(peer);
    }

    [JSExport]
    public static string Http(string method, string url, string? contentType, byte[] body)
    {
        var uri = new Uri(url);
        NameValueCollection query = HttpUtility.ParseQueryString(uri.Query);
        ApiResponse response;
        try
        {
            response = _api.Route(new ApiRequest(method, uri.AbsolutePath, query, contentType, body, true));
        }
        catch (Exception error)
        {
            Console.WriteLine($"http: {uri.AbsolutePath}: {error.Message}");
            response = new ApiResponse(500, null, []);
        }
        return JsonSerializer.Serialize(new { status = response.Status, contentType = response.ContentType, body = Convert.ToBase64String(response.Body) });
    }

    [JSImport("send", "openkogama")]
    private static partial void Send(int id, byte[] data);

    [JSImport("close", "openkogama")]
    private static partial void Close(int id);

    [JSImport("loadDatabase", "openkogama")]
    private static partial Task<string?> LoadDatabase();

    [JSImport("saveDatabase", "openkogama")]
    private static partial void SaveDatabase(string data);
}
