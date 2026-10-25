using System.Buffers.Text;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenKogama.World;

namespace OpenKogama.Accounts;

public sealed class MapSubmissions(string folder)
{
    const long MaxMap = 50L * 1024 * 1024;
    const long MinFree = 2L * 1024 * 1024 * 1024;
    const int HeadSize = 64 * 1024;
    const int MaxName = 200;
    const int MaxNick = 32;
    const string MapExtension = ".kgmap";
    const string PlanetExtension = ".kgm";
    const string Index = "index.jsonl";
    const int TicketSize = 24;
    const int MaxTickets = 10_000;
    static readonly TimeSpan TicketLife = TimeSpan.FromHours(12);

    readonly object _sync = new();
    readonly Dictionary<string, long> _tickets = [];

    public string Ticket()
    {
        string ticket = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TicketSize));
        long now = Environment.TickCount64;
        lock (_tickets)
        {
            foreach (string old in _tickets.Where(pair => pair.Value < now).Select(pair => pair.Key).ToList()) _tickets.Remove(old);
            if (_tickets.Count >= MaxTickets) _tickets.Remove(_tickets.MinBy(pair => pair.Value).Key);
            _tickets[ticket] = now + (long)TicketLife.TotalMilliseconds;
        }
        return ticket;
    }

    bool Valid(string? ticket)
    {
        if (ticket is null) return false;
        long now = Environment.TickCount64;
        lock (_tickets)
        {
            if (!_tickets.TryGetValue(ticket, out long expires) || expires < now) return false;
            _tickets[ticket] = now + (long)TicketLife.TotalMilliseconds;
            return true;
        }
    }

    public async Task<Reply> AcceptAsync(HttpListenerRequest request, string source)
    {
        if (!Valid(request.QueryString["ticket"]))
        {
            Reply again = Error(401, "confirm that you are not a bot again");
            again.Body["captcha"] = true;
            return again;
        }
        Directory.CreateDirectory(folder);
        if (request.ContentLength64 > MaxMap) return Error(413, "a map can be at most 50 MB");
        if (new DriveInfo(Path.GetFullPath(folder)).AvailableFreeSpace < MinFree + Math.Max(0, request.ContentLength64))
            return Error(507, "there is no room for new maps right now, try again later");

        string temp = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".part");
        try
        {
            (string? sha, long size) = await SaveAsync(request.InputStream, temp);
            if (sha is null) return Error(413, "a map can be at most 50 MB");
            if (size == 0 || ExtensionOf(temp) is not string extension) return Error(400, "that is not a KoGaMa map");

            string name = Clean(request.QueryString["name"], MaxName) ?? sha + extension;
            string? nick = Clean(request.QueryString["nick"], MaxNick);
            lock (_sync)
            {
                string path = Path.Combine(folder, sha + extension);
                if (File.Exists(Path.Combine(folder, sha + MapExtension)) || File.Exists(Path.Combine(folder, sha + PlanetExtension)))
                    return Error(409, "this map was already shared");
                File.Move(temp, path);
                File.AppendAllText(Path.Combine(folder, Index), JsonSerializer.Serialize(new { sha, name, nick, size, at = DateTime.UtcNow }) + "\n");
            }
            Console.WriteLine($"maps: {sha} {size} bytes {name} from {source}");
            return new Reply(200, new JsonObject { ["sha"] = sha });
        }
        finally
        {
            File.Delete(temp);
        }
    }

    static async Task<(string? Sha, long Size)> SaveAsync(Stream input, string path)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using FileStream output = File.Create(path);
        var buffer = new byte[81920];
        long size = 0;
        for (int read; (read = await input.ReadAsync(buffer)) > 0;)
        {
            size += read;
            if (size > MaxMap) return (null, size);
            hash.AppendData(buffer, 0, read);
            await output.WriteAsync(buffer.AsMemory(0, read));
        }
        return (Convert.ToHexStringLower(hash.GetHashAndReset()), size);
    }

    static string? ExtensionOf(string path)
    {
        using FileStream file = File.OpenRead(path);
        var head = new byte[Math.Min(HeadSize, file.Length)];
        file.ReadExactly(head);
        return KgmapFile.Recognized(head) ? MapExtension : WorldSerializer.LooksLikeSnapshot(head) ? PlanetExtension : null;
    }

    static string? Clean(string? text, int max)
    {
        string? cleaned = text is null ? null : new string([.. text.Where(c => !char.IsControl(c))]).Trim();
        return string.IsNullOrEmpty(cleaned) ? null : cleaned.Length > max ? cleaned[..max] : cleaned;
    }

    static Reply Error(int status, string message) => new(status, new JsonObject { ["error"] = message });
}
