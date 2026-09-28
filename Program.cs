using System.Text.Json;
using OpenKogama.Game;
using OpenKogama.Hosting;
using OpenKogama.Photon;
using OpenKogama.Plugins;
using OpenKogama.Storage;
using OpenKogama.Web;

LogFile.Start("logs");

var database = new Database("server.db");
Stores.Profiles = new SqliteProfileStore(database);
Stores.Worlds = new SqliteWorldStore(database);
Stores.Friends = new SqliteFriendStore(database);
Stores.Images = new SqliteImageStore(database);
Stores.Market = new SqliteMarketStore(database);
Session.EnsureDefaultWorld();
PluginHost.LoadAll("plugins");

var server = new PhotonServer(5055) { Log = Console.WriteLine };
bool mixedClients = args.Contains("--mixed-clients");
var host = new SessionHost(server, mixedClients);
if (mixedClients) Console.WriteLine("mixed client versions enabled, unsupported");

server.Connected += peer => Console.WriteLine($"peer {peer.Id}: photon init done");
server.Disconnected += (peer, reason) =>
{
    Console.WriteLine($"peer {peer.Id}: gone ({reason})");
    host.Disconnect(peer);
};
server.Operation = host.Handle;

_ = server.RunAsync();
Console.WriteLine("udp 5055");

_ = Task.Run(async () =>
{
    while (true)
    {
        await Task.Delay(5000);
        host.SaveAll();
    }
});
AppDomain.CurrentDomain.ProcessExit += (_, _) => host.SaveAll();

_ = Task.Run(async () =>
{
    while (true)
    {
        await Task.Delay(50);
        host.Tick();
    }
});

var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
string SessionJson(int profile, GameMode mode, int world, string? client) => JsonSerializer.Serialize(
    new SessionData("127.0.0.1:5055", profile, world, mode, "en_US", false, profile.ToString(),
        "http://127.0.0.1:8080/ping", "http://127.0.0.1:8080/disconnect", client),
    jsonOptions);

StreamingAssetCatalog streaming = StreamingAssets.For("2015");
var assets = new AssetCache(streaming.Root);
var legacyAssets = new AssetCache(StreamingAssets.For("3.x").Root, "cache/streaming-3x");
var assetSets = BundleSets.Roots.ToDictionary(set => set.Key, set => new AssetCache(set.Value, $"cache/streaming-{set.Key}"));
_ = Task.Run(() => assets.PrefetchAsync(streaming.Assets.Select(asset => asset.Path)));

foreach (int port in new[] { 843, 844, 845 })
    _ = new SocketPolicyServer(port).RunAsync();
Console.WriteLine("policy 843-845");

_ = new NullProxy(8081).RunAsync();
Console.WriteLine("proxy 8081");

var web = new HttpServer("http://127.0.0.1:8080/") { SessionJson = SessionJson, Assets = assets, LegacyAssets = legacyAssets, AssetSets = assetSets, DeleteWorld = host.DeleteWorld };
Console.WriteLine("http 8080");
web.Run();
