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

    public static ServerInfo Load(string path) => File.Exists(path)
        ? JsonSerializer.Deserialize<ServerInfo>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? new ServerInfo()
        : new ServerInfo();

    public int EntryWorld() => JoinWorld > 0 ? JoinWorld : Stores.Worlds.List().FirstOrDefault()?.Id ?? 0;

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
