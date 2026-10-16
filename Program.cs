using System.Runtime.InteropServices;
using System.Text.Json;
using OpenKogama.Game;
using OpenKogama.Hosting;
using OpenKogama.P2p;
using OpenKogama.Photon;
using OpenKogama.Plugins;
using OpenKogama.Storage;
using OpenKogama.Web;

if (args is ["install", .. var sets])
{
    bool installed = true;
    foreach (string set in sets)
        installed &= await AssetCache.InstallAsync(set);
    return installed ? 0 : 1;
}
if (args is ["tunnel", .. var tunnel])
    return Tunnel.Run(tunnel);
if (args is ["rendezvous", .. var rendezvous])
    return Rendezvous.Run(rendezvous);

LogFile.Start("logs");
AssetCache.Offline = args.Contains("--offline");
var serverInfo = ServerInfo.Load("server.json");
var publicUrl = new Uri(serverInfo.PublicUrl.TrimEnd('/'));
if (publicUrl.Scheme is not ("http" or "https") || publicUrl.AbsolutePath != "/"
    || !string.IsNullOrEmpty(publicUrl.Query) || !string.IsNullOrEmpty(publicUrl.Fragment)
    || !string.IsNullOrEmpty(publicUrl.UserInfo))
    throw new ArgumentException("server.json publicUrl must be an HTTP/HTTPS server address.");
string publicBase = publicUrl.GetLeftPart(UriPartial.Authority);
string photonHost = publicUrl.Host;
string photonAddress = (photonHost.Contains(':') ? $"[{photonHost.Trim('[', ']')}]" : photonHost) + $":{serverInfo.PhotonPort}";
SessionLocator.GameServer = photonHost;
SessionLocator.PublicUrl = publicBase;
SessionLocator.EntryWorld = serverInfo.EntryWorld;

var database = new Database("server.db");
Stores.Profiles = new SqliteProfileStore(database);
Stores.Worlds = new SqliteWorldStore(database);
Stores.Friends = new SqliteFriendStore(database);
Stores.Images = new SqliteImageStore(database);
Stores.Market = new SqliteMarketStore(database);
Session.EnsureDefaultWorld();
Session.KeepPlayChanges = serverInfo.KeepPlayChanges;

var server = new PhotonServer(serverInfo.PhotonPort) { Log = Console.WriteLine };
bool mixedClients = args.Contains("--mixed-clients") || serverInfo.MixedClients;
var host = new SessionHost(server, mixedClients) { EntryWorld = serverInfo.EntryWorld, Allows = serverInfo.Allows };
PluginHost.Admins = serverInfo.Admins ?? [];
PluginHost.LoadAll("plugins", host.Sessions);
if (mixedClients) Console.WriteLine("mixed client versions enabled");

server.Connected += peer => Console.WriteLine($"peer {peer.Id}: photon init done");
server.Disconnected += (peer, reason) =>
{
    Console.WriteLine($"peer {peer.Id}: gone ({reason})");
    host.Disconnect(peer);
};
server.Operation = host.Handle;

_ = server.RunAsync();
Console.WriteLine($"udp {serverInfo.PhotonPort}");
_ = server.RunWebSocketsAsync($"http://{serverInfo.ListenHost}:{serverInfo.PhotonPort}/");
Console.WriteLine($"websocket {serverInfo.PhotonPort}");

_ = Task.Run(async () =>
{
    while (true)
    {
        await Task.Delay(5000);
        host.SaveAll();
    }
});
int stopping = 0;
void SaveBeforeExit()
{
    if (Interlocked.Exchange(ref stopping, 1) == 1) return;
    Console.WriteLine("stopping, saving worlds");
    PluginHost.Shutdown();
    host.SaveAll(force: true);
}
AppDomain.CurrentDomain.ProcessExit += (_, _) => SaveBeforeExit();
using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, _ => SaveBeforeExit());
using var sigint = PosixSignalRegistration.Create(PosixSignal.SIGINT, _ => SaveBeforeExit());
using var sighup = PosixSignalRegistration.Create(PosixSignal.SIGHUP, _ => SaveBeforeExit());

_ = Task.Run(async () =>
{
    while (true)
    {
        await Task.Delay(50);
        host.Tick();
    }
});

var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
string SessionJson(int profile, GameMode mode, int world, string? client, string? via)
{
    string root = via is null ? publicBase : "http://" + via;
    return JsonSerializer.Serialize(
        new SessionData(via ?? photonAddress, profile, world, mode, "en_US", false, profile.ToString(),
            root + "/ping", root + "/disconnect", root + "/reward", client),
        jsonOptions);
}

StreamingAssetCatalog streaming = StreamingAssets.For("2015");
var assets = AssetCache.ForSet("2015", streaming.Root);
var legacyAssets = AssetCache.ForSet("3.x", StreamingAssets.For("3.x").Root);
var assetSets = BundleSets.Roots.ToDictionary(set => set.Key, set => AssetCache.ForSet(set.Key, set.Value));
foreach ((string set, string root) in BundleSets.Roots.Where(set => set.Value.Contains("/kogama_assets_u5/")))
    assetSets[set + BundleSets.WebGL] = AssetCache.ForSet(set + BundleSets.WebGL, BundleSets.WebGLRoot(root));
if (streaming.WebGLRoot is string legacyWebGL)
    assetSets[BundleSets.Legacy + BundleSets.WebGL] = AssetCache.ForSet(BundleSets.Legacy + BundleSets.WebGL, legacyWebGL);
_ = Task.Run(() => assets.PrefetchAsync(streaming.Assets.Select(asset => asset.Path)));

foreach (int port in new[] { 843, 844, 845 })
    _ = new SocketPolicyServer(port).RunAsync();
Console.WriteLine("policy 843-845");

if (publicUrl.IsLoopback)
{
    _ = new NullProxy(8081).RunAsync();
    Console.WriteLine("proxy 8081");
}

var api = new WebApi
{
    SessionJson = SessionJson,
    ServerInfo = serverInfo,
    Assets = assets,
    LegacyAssets = legacyAssets,
    AssetSets = assetSets,
    DeleteWorld = host.DeleteWorld,
    ExportWorld = host.ExportWorld,
    Shutdown = () => _ = Task.Delay(100).ContinueWith(_ => Environment.Exit(0)),
};
var web = new HttpServer($"http://{serverInfo.ListenHost}:{serverInfo.HttpPort}/", api);
Console.WriteLine($"http {serverInfo.HttpPort}");
web.Run();
return 0;
