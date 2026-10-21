using System.Collections.Specialized;
using System.Security.Cryptography;
using System.Text.Json;
using OpenKogama.Accounts;
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
Stores.Images = new TestImages();
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
        SessionJson = (_, mode, world, _, _) => {
            selectedWorld = world;
            selectedMode = mode;
            return "{}";
        },
    };
    var metadata = api.Route(Get("/api/server"));
    using (var json = JsonDocument.Parse(metadata.Body))
    {
        Check(metadata.Status == 200 && json.RootElement.EnumerateObject().Count() == 6,
            "launcher metadata exposes only its tile fields and features");
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
        SessionJson = (_, mode, world, _, _) =>
        {
            selectedWorld = world;
            selectedMode = mode;
            return "{}";
        },
    };
    builder.Route(Get("/session", ("mode", "play")));
    Check(selectedWorld == 7 && selectedMode == GameMode.Play, "joinMode build leaves the local player's own choice alone");
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
    var gated = new WebApi { ServerInfo = filtered, SessionJson = (_, _, _, _, _) => "{}" };
    Check(gated.Route(Get("/session", ("mode", "play"), ("client", "3.5.25"))).Status == 403, "session refuses versions outside clients");
    using (JsonDocument server = JsonDocument.Parse(api.Route(Get("/api/server")).Body))
        Check(server.RootElement.GetProperty("features").EnumerateArray().Select(feature => feature.GetString()).ToHashSet()
            .SetEquals(["player", "player-deflate", "content"]), "the server lists what newer launchers may use");
    Check(api.Route(Get("/session", ("mode", "play"), ("ticket", "from-a-newer-launcher"), ("future", "1"))).Status == 200,
        "session ignores parameters it does not know, so newer launchers still get in");
    string issued = SessionTokens.Issue(4, " nightus ");
    Check(SessionTokens.Find(issued) == (4, "nightus") && SessionTokens.Find("4") == (0, null) && SessionTokens.Find("4-6E69676874757573") == (0, null),
        "session tokens are random, carry the nickname and cannot be forged from a profile number");
    var profiles = new SqliteProfileStore(new Database(accounts, writeAhead: false));
    int first = profiles.ForName("Nightus");
    Check(profiles.ForName("nightus") == first && profiles.ForName("Other") != first && profiles.Name(first) == "Nightus",
        "every nickname gets one profile, letter case aside");
    Check(new ServerInfo { PublicUrl = "https://example.com" }.Shared && !new ServerInfo().Shared, "accounts only on a public server");
    Stores.Profiles = profiles;
    using var central = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    CentralKey.Current = central;
    WebApi Server(AccountMode mode) => new() { ServerInfo = new ServerInfo { PublicUrl = "https://Example.com/", Accounts = mode }, SessionJson = (_, _, _, _, _) => "{}" };
    (int Profile, string? Name) Joined(ApiResponse response) =>
        response.Status == 200 ? SessionTokens.Find(JsonDocument.Parse(response.Body).RootElement.GetProperty("token").GetString()) : (0, null);
    WebApi optional = Server(AccountMode.Optional);
    string ticket = Tickets.Issue(central, 77, "Nightus", "https://example.com");
    Check(Joined(optional.Route(Get("/session", ("mode", "play"), ("ticket", ticket)))) == (first, "Nightus"),
        "a ticket signs the account in and takes over the profile with its name");
    Check(optional.Route(Get("/session", ("mode", "play"), ("ticket", ticket))).Status == 403, "a ticket works only once");
    Check(optional.Route(Get("/session", ("mode", "play"), ("ticket", Tickets.Issue(central, 78, "Other", "https://evil.example")))).Status == 403,
        "a ticket made for another server does not work here");
    Check(optional.Route(Get("/session", ("mode", "play"), ("name", "nightus"))).Status == 403, "a name that belongs to an account needs that account");
    Check(Joined(optional.Route(Get("/session", ("mode", "play"), ("name", "Guest")))).Name == "Guest", "players without an account still get in with a free name");
    Check(optional.Route(Get("/session", ("mode", "play"))).Status == 403, "a remote player without a name never gets the owner's profile");
    Check(Server(AccountMode.Online).Route(Get("/session", ("mode", "play"), ("name", "Guest"))).Status == 403, "an online server needs an account");
    Check(Server(AccountMode.Offline).Route(Get("/session", ("mode", "play"), ("name", "nightus"))).Status == 200, "an offline server takes any name");
    bool Claimed(WebApi server, string name) => JsonDocument.Parse(server.Route(Get("/api/name", ("name", name))).Body).RootElement.GetProperty("claimed").GetBoolean();
    Check(Claimed(optional, "NIGHTUS") && !Claimed(optional, "Guest"), "launchers can ask whether a name belongs to an account");
    Check(!Claimed(optional, "Nobody123") && profiles.Named("Nobody123") is null, "asking about a name does not make a profile");
    Check(!Claimed(Server(AccountMode.Offline), "nightus"), "on an offline server no name is taken");
    using (JsonDocument online = JsonDocument.Parse(Server(AccountMode.Online).Route(Get("/api/server")).Body))
        Check(online.RootElement.GetProperty("features").EnumerateArray().Select(feature => feature.GetString()).Contains("accounts-required"),
            "an online server tells launchers that an account is required");
    CentralKey.Current = null;
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

sealed class TestImages : IImageStore
{
    public byte[]? Image(int type, int id) => null;
    public void SaveImage(int type, int id, byte[] data) => throw new NotSupportedException();
    public void DeleteImage(int type, int id) => throw new NotSupportedException();
}
