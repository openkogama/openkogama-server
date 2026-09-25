using System.Text.Json;
using OpenKogama.Game;
using OpenKogama.Hosting;
using OpenKogama.Photon;
using OpenKogama.Storage;
using OpenKogama.Web;

var database = new Database("server.db");
Stores.Profiles = new SqliteProfileStore(database);
Stores.Worlds = new SqliteWorldStore(database);
Stores.Friends = new SqliteFriendStore(database);
Session.EnsureDefaultWorld();

var server = new PhotonServer(5055) { Log = Console.WriteLine };
var host = new SessionHost(server);

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
string SessionJson(int profile, GameMode mode, int world) => JsonSerializer.Serialize(
    new SessionData("127.0.0.1:5055", profile, world, mode, "en_US", false, "0",
        "http://127.0.0.1:8080/ping", "http://127.0.0.1:8080/disconnect"),
    jsonOptions);

StreamingAssetCatalog streaming = StreamingAssets.For("2015");
var assets = new AssetCache(streaming.Root);
_ = Task.Run(() => assets.PrefetchAsync(streaming.Assets.Select(asset => asset.Path)));

_ = new NullProxy(8081).RunAsync();
Console.WriteLine("proxy 8081");

var web = new HttpServer("http://127.0.0.1:8080/") { SessionJson = SessionJson, Assets = assets };
Console.WriteLine("http 8080");
web.Run();
