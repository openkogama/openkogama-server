using System.Text.Json;
using System.Text.Json.Serialization;
using OpenKogama.Accounts;
using OpenKogama.Content;
using OpenKogama.Storage;

namespace OpenKogama.Web;

public enum AccountMode { Offline, Optional, Online }

public sealed class ServerInfo
{
    public string Title { get; init; } = "OpenKogama";
    public string? Thumbnail { get; init; }
    public int JoinWorld { get; init; }
    public string PublicUrl { get; set; } = "http://127.0.0.1:8080";
    public string ListenHost { get; init; } = "127.0.0.1";
    public int HttpPort { get; set; } = 8080;
    public int PhotonPort { get; set; } = 5055;
    public int? TcpPort { get; set; }
    public string JoinMode { get; init; } = "play";
    public string[] Admins { get; init; } = [];
    public string[] Allow { get; init; } = [];

    public bool BuildJoin => JoinMode.Equals("build", StringComparison.OrdinalIgnoreCase);
    public string[] Clients { get; init; } = [];
    public bool MixedClients { get; init; }
    public bool KeepPlayChanges { get; init; }
    public bool Public { get; init; }
    public bool PlayerSettings { get; init; } = true;
    public bool PlayerAvatars { get; init; } = true;
    public bool PlayerInventory { get; init; } = true;
    public bool AntiCheatCrash { get; init; } = true;
    public bool AntiCheatSpam { get; init; } = true;
    public bool AntiCheatSpoof { get; init; } = true;
    public bool AntiCheatBuild { get; init; } = true;
    public string List { get; init; } = PublicList.Announcer.DefaultList;

    public bool Shared => Uri.TryCreate(PublicUrl, UriKind.Absolute, out Uri? url) && !url.IsLoopback;
    public AccountMode Accounts { get; init; } = AccountMode.Optional;
    public string AccountsUrl { get; init; } = "https://accounts.openkogama.org";
    public string[] Addresses { get; init; } = [];

    public static readonly string[] Features = ["player", "player-deflate", "content", "tcp"];

    public string[] FeaturesNow()
    {
        string[] features = Accounts == AccountMode.Offline || CentralKey.Current is null ? Features
            : Accounts == AccountMode.Online ? [.. Features, "accounts", "accounts-required"] : [.. Features, "accounts"];
        return ClientScripts.Any() ? [.. features, "scripts"] : features;
    }

    public bool Accepts(string audience)
    {
        string wanted = AccountTickets.Audience(audience);
        return AccountTickets.Audience(PublicUrl) == wanted || Addresses.Any(address => AccountTickets.Audience(address) == wanted);
    }

    public static ServerInfo Load(string path) => File.Exists(path)
        ? JsonSerializer.Deserialize<ServerInfo>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } })
            ?? new ServerInfo()
        : new ServerInfo();

    public void UseHttpPort(int port)
    {
        if (PublicUrl == $"http://127.0.0.1:{HttpPort}") PublicUrl = $"http://127.0.0.1:{port}";
        HttpPort = port;
    }

    public int EntryWorld() => JoinWorld > 0 ? JoinWorld : Stores.Worlds.List().FirstOrDefault()?.Id ?? 0;

    public bool Allows(string? client) =>
        Clients is not { Length: > 0 } || client is not null && Clients.Any(entry => Matches(entry.Trim(), client));

    static bool Matches(string entry, string client)
    {
        string[] range = entry.Split('-', 2, StringSplitOptions.TrimEntries);
        if (range.Length == 2 && Version.TryParse(range[0], out Version? low) && Version.TryParse(range[1], out Version? high))
            return Version.TryParse(client, out Version? version) && version >= low && version <= high;
        return entry == client;
    }

    public byte[]? ThumbnailBytes()
    {
        if (string.IsNullOrWhiteSpace(Thumbnail) || !File.Exists(Thumbnail)) return null;
        using var file = File.OpenRead(Thumbnail);
        if (file.Length is <= 0 or > 256 * 1024) return null;
        var bytes = new byte[(int)file.Length];
        file.ReadExactly(bytes);
        return bytes;
    }

    public string DisplayTitle => string.Concat(Title.EnumerateRunes().Take(40)).Trim();
}
