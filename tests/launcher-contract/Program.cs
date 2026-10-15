using System.Collections.Specialized;
using System.Text.Json;
using OpenKogama.Game;
using OpenKogama.Storage;
using OpenKogama.Web;

int checks = 0;
void Check(bool condition, string description)
{
    if (!condition) throw new Exception(description);
    checks++;
    Console.WriteLine("PASS " + description);
}

ApiRequest Get(string path, params (string Key, string Value)[] parameters)
{
    var query = new NameValueCollection();
    foreach (var (key, value) in parameters) query[key] = value;
    return new ApiRequest("GET", path, query, null, [], false);
}

var worlds = new TestWorlds();
Stores.Worlds = worlds;
string image = Path.Combine(Path.GetTempPath(), "openkogama-contract-" + Guid.NewGuid() + ".png");
string accounts = Path.Combine(Path.GetTempPath(), "openkogama-contract-" + Guid.NewGuid() + ".db");
try
{
    File.WriteAllBytes(image, [137, 80, 78, 71]);
    var info = new ServerInfo { Title = new string('X', 60), Thumbnail = image, JoinWorld = 7 };
    int selectedWorld = -1;
    GameMode selectedMode = GameMode.Edit;
    var api = new WebApi {
        ServerInfo = info,
        SessionJson = (_, mode, world, _) => {
            selectedWorld = world;
            selectedMode = mode;
            return "{}";
        },
    };
    var metadata = api.Route(Get("/api/server"));
    using (var json = JsonDocument.Parse(metadata.Body))
    {
        Check(metadata.Status == 200 && json.RootElement.EnumerateObject().Count() == 3,
            "launcher metadata exposes only its three tile fields");
        Check(json.RootElement.GetProperty("clients").GetArrayLength() == 0, "every version is accepted when clients is not set");
        Check(json.RootElement.GetProperty("title").GetString()!.Length == 40, "title has a 40-character limit");
        Check(json.RootElement.GetProperty("thumbnail").GetString() == "/api/server/thumbnail", "thumbnail uses the fixed endpoint");
    }
    Check(worlds.Reads == 0, "metadata does not read world lists");
    Check(api.Route(Get("/api/server/thumbnail")).Status == 200, "configured thumbnail is served");
    File.WriteAllBytes(image, new byte[256 * 1024 + 1]);
    Check(api.Route(Get("/api/server/thumbnail")).Status == 404, "oversized thumbnail is rejected");
    api.Route(Get("/session", ("mode", "play")));
    Check(selectedWorld == 7 && selectedMode == GameMode.Play, "Join selects the configured entry world in play mode");
    api.Route(Get("/session", ("mode", "play"), ("world", "3")));
    Check(selectedWorld == 3, "explicit local world selection remains supported");
    api.Route(Get("/session", ("mode", "edit")));
    Check(selectedWorld == 0 && selectedMode == GameMode.Edit, "entry world does not override editor requests");
    Check(new ServerInfo().EntryWorld() == 9, "default entry uses the first saved world");
    var builder = new WebApi
    {
        ServerInfo = new ServerInfo { JoinWorld = 7, JoinMode = "build" },
        SessionJson = (_, mode, world, _) =>
        {
            selectedWorld = world;
            selectedMode = mode;
            return "{}";
        },
    };
    builder.Route(Get("/session", ("mode", "play")));
    Check(selectedWorld == 7 && selectedMode == GameMode.Edit, "joinMode build opens the entry world in the editor");
    SessionLocator.EntryWorld = info.EntryWorld;
    SessionLocator.GameServer = "example.com";
    string? legacy = SessionLocator.Answer("/PHP/GameSessionLocator.php", Get("/", ("PlanetName", "0")).Query);
    Check(legacy is not null && legacy.Contains("GAMENAME:'7'") && legacy.Contains("GAMESERVERIP:'example.com'"),
        "2012 Join uses the entry world and public game hostname");
    var unicode = new ServerInfo { Title = string.Concat(Enumerable.Repeat("😀", 45)) };
    Check(unicode.DisplayTitle.EnumerateRunes().Count() == 40, "title limit preserves Unicode characters");
    var filtered = new ServerInfo { Clients = ["3.5.20", "1.25.12.280-1.32.10.547"] };
    Check(filtered.Allows("3.5.20") && filtered.Allows("1.30.0.391") && !filtered.Allows("3.5.25") && !filtered.Allows(null),
        "clients accepts exact versions and inclusive ranges only");
    var gated = new WebApi { ServerInfo = filtered, SessionJson = (_, _, _, _) => "{}" };
    Check(gated.Route(Get("/session", ("mode", "play"), ("client", "3.5.25"))).Status == 403, "session refuses versions outside clients");
    Check(SessionToken.Parse(SessionToken.Create(4, " nightus ")) == (4, "nightus") && SessionToken.Parse("4") == (4, null),
        "session token carries the nickname");
    var profiles = new SqliteProfileStore(new Database(accounts, writeAhead: false));
    int first = profiles.ForName("Nightus");
    Check(profiles.ForName("nightus") == first && profiles.ForName("Other") != first && profiles.Name(first) == "Nightus",
        "every nickname gets one profile, letter case aside");
    Check(new ServerInfo { PublicUrl = "https://example.com" }.Accounts && !new ServerInfo().Accounts, "accounts only on a public server");
    Console.WriteLine($"{checks} contract checks passed.");
}
finally
{
    File.Delete(image);
    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    File.Delete(accounts);
}

sealed class TestWorlds : IWorldStore
{
    public int Reads;
    public List<WorldInfo> List() { Reads++; return [new WorldInfo(9, "Entry", 1, "", null, null)]; }
    public StoredWorld? World(int id) => id is 7 or 9 ? new StoredWorld(id, "Entry", 1, []) : null;
    public byte[]? Published(int id) => null;
    public List<byte[]> PlayStates(int id) => [];
    public int Create(string name, int owner, byte[] data) => throw new NotSupportedException();
    public void SaveWorld(StoredWorld world) => throw new NotSupportedException();
    public void Publish(int id, byte[] data) => throw new NotSupportedException();
    public void SavePlayState(int id, byte[] data) => throw new NotSupportedException();
    public void MarkPlayed(int id) => throw new NotSupportedException();
    public bool Rename(int id, string name) => false;
    public bool Delete(int id) => false;
}
