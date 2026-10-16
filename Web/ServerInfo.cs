using System.Text.Json;
using OpenKogama.Storage;

namespace OpenKogama.Web;

public sealed class ServerInfo
{
    public string Title { get; init; } = "OpenKogama";
    public string? Thumbnail { get; init; }
    public int JoinWorld { get; init; }
    public string PublicUrl { get; init; } = "http://127.0.0.1:8080";
    public string ListenHost { get; init; } = "127.0.0.1";
    public int HttpPort { get; init; } = 8080;
    public int PhotonPort { get; init; } = 5055;
    public string JoinMode { get; init; } = "play";
    public string[] Admins { get; init; } = [];

    public bool BuildJoin => JoinMode.Equals("build", StringComparison.OrdinalIgnoreCase);
    public string[] Clients { get; init; } = [];
    public bool MixedClients { get; init; }
    public bool KeepPlayChanges { get; init; }
    public bool Public { get; init; }
    public string List { get; init; } = PublicList.Announcer.DefaultList;

    public bool Accounts => Uri.TryCreate(PublicUrl, UriKind.Absolute, out Uri? url) && !url.IsLoopback;

    public static ServerInfo Load(string path) => File.Exists(path)
        ? JsonSerializer.Deserialize<ServerInfo>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? new ServerInfo()
        : new ServerInfo();

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
