using System.Collections.Specialized;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using OpenKogama.PublicList;

namespace OpenKogama.Accounts;

public sealed partial class PluginHub
{
    const string Root = "/plugins";
    const int MaxJson = 32 * 1024;
    const long MaxFile = 32L * 1024 * 1024;
    const int MaxEntries = 2000;
    const long MaxUnpacked = 256L * 1024 * 1024;
    const long MaxImage = 1024 * 1024;
    const long MinFree = 1L * 1024 * 1024 * 1024;
    const int MinName = 2;
    const int MaxName = 40;
    const int MaxSummary = 120;
    const int MaxDescription = 20000;
    const int MaxChanges = 2000;
    const int MinReason = 3;
    const int MaxReason = 500;
    const int MaxSearch = 60;
    const int MaxImages = 6;
    const int MaxVideos = 2;
    const int MaxOwned = 20;
    const int MaxVersions = 100;
    const int PageSize = 24;
    const int MaxPage = 200;
    const string Bearer = "Bearer ";
    const string ApiAssembly = "OpenKogama.Api";
    static readonly TimeSpan Hour = TimeSpan.FromHours(1);
    static readonly TimeSpan Unused = TimeSpan.FromDays(1);
    static readonly HashSet<string> Reserved = ["files", "new", "edit", "mine", "view", "save", "upload", "version", "delete", "report", "state", "reports"];

    readonly AccountStore _accounts;
    readonly PluginStore _store;
    readonly string _folder;
    readonly Func<string, int, TimeSpan, bool> _allow;
    readonly HashSet<string> _admins;
    readonly Moderation _moderation;
    readonly VirusScan _scanner;
    readonly object _sync = new();
    DateTime _swept;

    public PluginHub(AccountStore accounts, PluginStore store, string folder, Func<string, int, TimeSpan, bool> allow)
    {
        _accounts = accounts;
        _store = store;
        _folder = folder;
        _allow = allow;
        Directory.CreateDirectory(folder);
        _admins = new HashSet<string>((Environment.GetEnvironmentVariable("OPENKOGAMA_PLUGIN_ADMINS") ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.OrdinalIgnoreCase);
        _moderation = new Moderation(Path.Combine(folder, "moderation.json"),
            Environment.GetEnvironmentVariable("OPENKOGAMA_MODERATION_KEY") is { Length: > 0 } key ? key : null);
        _scanner = new VirusScan(store, folder);
    }

    public static bool Handles(string path) => path == Root || path.StartsWith(Root + "/", StringComparison.Ordinal);

    public void DeleteOwner(long account) => _store.DeleteOwner(account);

    public async Task ServeAsync(HttpListenerContext context, string path, string source)
    {
        HttpListenerRequest request = context.Request;
        HttpListenerResponse response = context.Response;
        string[] parts = path[Root.Length..].Split('/', StringSplitOptions.RemoveEmptyEntries);
        Sweep();
        Reply reply;
        try
        {
            switch (request.HttpMethod, parts)
            {
                case ("GET", ["files", var name]):
                    if (await SendImageAsync(response, name)) return;
                    reply = NotFound();
                    break;
                case ("GET" or "POST", [var slug, "download", .. var rest]) when rest.Length <= 1:
                    if (await DownloadAsync(request, response, slug, rest.FirstOrDefault(), source)) return;
                    reply = NotFound();
                    break;
                case ("GET", []):
                    reply = List(request.QueryString);
                    break;
                case ("GET", [var slug]):
                    reply = Details(slug);
                    break;
                case ("POST", ["upload"]):
                    reply = await UploadAsync(request);
                    break;
                case ("POST", [_, ..]):
                    reply = Handle(string.Join('/', parts), await ReadAsync(request));
                    break;
                default:
                    reply = NotFound();
                    break;
            }
        }
        catch (JsonException)
        {
            reply = Error(400, "bad request");
        }
        await WriteAsync(response, reply);
    }

    Reply Handle(string action, JsonObject? body)
    {
        if (Text(body, "token") is not string token || _accounts.Session(token) is not Account account) return Error(401, "sign in again");
        return action switch
        {
            "mine" => Mine(account),
            "view" => View(account, body),
            "save" => Save(account, body),
            "version" => AddVersion(account, body),
            "version/delete" => DeleteVersion(account, body),
            "version/approve" => ApproveVersion(account, body),
            "delete" => Delete(account, body),
            "report" => Report(account, body),
            "state" => SetState(account, body),
            "reports/clear" => ClearReports(account, body),
            _ => NotFound(),
        };
    }

    Reply List(NameValueCollection query)
    {
        int page = int.TryParse(query["page"], out int given) ? Math.Clamp(given, 0, MaxPage) : 0;
        string search = Line(query["q"]);
        if (search.Length > MaxSearch) search = search[..MaxSearch];
        string sort = query["sort"] is "new" or "updated" ? query["sort"]! : "popular";
        IReadOnlyList<Plugin> found = _store.Public(search, sort, page * PageSize, PageSize + 1);
        return Ok(new JsonObject
        {
            ["plugins"] = Cards(found.Take(PageSize).Where(Allowed), withState: false),
            ["more"] = found.Count > PageSize,
        });
    }

    Reply Details(string slug) =>
        _store.BySlug(slug) is { State: PluginState.Public } plugin && Allowed(plugin) && _store.Version(plugin.Id, null) is not null
            ? Ok(Full(plugin, everything: false))
            : NotFound();

    Reply Mine(Account account)
    {
        bool admin = Admin(account);
        var reply = new JsonObject
        {
            ["admin"] = admin,
            ["plugins"] = Cards(_store.ByOwner(account.Id), withState: true),
        };
        if (!admin) return Ok(reply);
        reply["waiting"] = Cards(_store.Waiting(), withState: true);
        var reports = new JsonArray();
        foreach (PluginReport report in _store.Reports())
            reports.Add(new JsonObject
            {
                ["slug"] = report.Slug,
                ["name"] = report.Name,
                ["reporter"] = report.Reporter,
                ["reason"] = report.Reason,
                ["created"] = report.Created,
            });
        reply["reports"] = reports;
        return Ok(reply);
    }

    Reply View(Account account, JsonObject? body)
    {
        if (Managed(account, body) is not Plugin plugin) return NotFound();
        JsonObject full = Full(plugin, everything: true);
        full["state"] = StateName(plugin.State);
        full["owner"] = plugin.Owner == account.Id;
        full["admin"] = Admin(account);
        return Ok(full);
    }

    Reply Save(Account account, JsonObject? body)
    {
        if (!_allow("plugin save " + account.Id, 60, Hour)) return TooMany();
        string name = Line(Text(body, "name")), summary = Line(Text(body, "summary")), description = Block(Text(body, "description"));
        if (name.Length is < MinName or > MaxName) return Error(400, $"a name has {MinName} to {MaxName} characters");
        if (summary.Length is 0 or > MaxSummary) return Error(400, $"a short description has 1 to {MaxSummary} characters");
        if (description.Length > MaxDescription) return Error(400, $"a description can have at most {MaxDescription} characters");
        if (!Video(Text(body, "video"), out string? video)) return Error(400, "that does not look like a YouTube link");
        string? icon = Text(body, "icon") is { Length: > 0 } given ? given : null;
        if (icon is not null && !Stored(icon, ImageFile())) return Error(400, "upload the icon again");
        string[] images = body?["images"] is JsonArray list
            ? [.. list.Select(item => item is JsonValue value && value.TryGetValue(out string? text) ? text : "").Where(item => item.Length > 0).Distinct()]
            : [];
        if (images.Count(image => !image.StartsWith("yt:", StringComparison.Ordinal)) > MaxImages) return Error(400, $"a plugin can have at most {MaxImages} screenshots");
        if (images.Count(image => image.StartsWith("yt:", StringComparison.Ordinal)) > MaxVideos) return Error(400, $"a plugin can have at most {MaxVideos} videos");
        if (images.Any(image => image.StartsWith("yt:", StringComparison.Ordinal) ? !VideoId().IsMatch(image[3..]) : !Stored(image, ImageFile()))) return Error(400, "upload the screenshots again");
        if (video is not null && !images.Contains("yt:" + video)) images = [.. images, "yt:" + video];
        video = images.FirstOrDefault(image => image.StartsWith("yt:", StringComparison.Ordinal))?[3..];

        string? slug = Text(body, "slug");
        if (slug is not null)
        {
            if (_store.BySlug(slug) is not Plugin plugin || plugin.Owner != account.Id && !Admin(account)) return NotFound();
            _store.Update(plugin.Id, name, summary, description, video, icon, images);
        }
        else
        {
            if (_store.Owned(account.Id) >= MaxOwned) return Error(400, $"an account can have at most {MaxOwned} plugins");
            slug = NewSlug(name);
            _store.Add(account.Id, slug, name, summary, description, video, icon, images);
            Console.WriteLine($"plugins: {slug} added by {account.Name} and waits for approval");
        }
        _moderation.JudgeText(Words(name, summary, description), $"plugin {slug}");
        return Ok(new JsonObject { ["slug"] = slug });
    }

    Reply AddVersion(Account account, JsonObject? body)
    {
        if (Managed(account, body) is not Plugin plugin) return NotFound();
        if (!_allow("plugin version " + account.Id, 30, Hour)) return TooMany();
        if (Text(body, "file") is not string file || !Stored(file, PluginFile())) return Error(400, "upload the plugin again");
        string path = Path.Combine(_folder, file);
        if (Inspect(path) is not { } inspected) return Error(400, "that is not an OpenKogama plugin");
        string version = Line(Text(body, "version")) is { Length: > 0 } typed ? typed : inspected.Version;
        if (!VersionPattern().IsMatch(version)) return Error(400, "a version has up to 32 letters, digits, dots or dashes");
        string changes = Block(Text(body, "changes"));
        if (changes.Length > MaxChanges) return Error(400, $"the list of changes can have at most {MaxChanges} characters");
        if (_store.Versions(plugin.Id, all: true).Count >= MaxVersions) return Error(400, $"a plugin can have at most {MaxVersions} versions, delete an old one first");
        if (_store.Version(plugin.Id, version, all: true) is not null) return Error(409, "that version is already there");
        if (_store.AddVersion(plugin.Id, version, inspected.Api, file, inspected.Name, new FileInfo(path).Length, changes) is PluginVersion added) _scanner.Queue(added);
        if (changes.Length > 0) _moderation.JudgeText(changes, $"changes of plugin {plugin.Slug} {version}");
        Console.WriteLine($"plugins: {plugin.Slug} {version} added by {account.Name} and waits for approval");
        return Ok(new JsonObject { ["version"] = version, ["pending"] = true });
    }

    Reply ApproveVersion(Account account, JsonObject? body)
    {
        if (!Admin(account) || Text(body, "slug") is not string slug || _store.BySlug(slug) is not Plugin plugin) return NotFound();
        if (Text(body, "version") is not string version || _store.Version(plugin.Id, version, all: true) is not PluginVersion found) return NotFound();
        _store.ApproveVersion(found.Id);
        Console.WriteLine($"plugins: {plugin.Slug} {version} approved by {account.Name}");
        return Ok(new JsonObject());
    }

    Reply DeleteVersion(Account account, JsonObject? body)
    {
        if (Managed(account, body) is not Plugin plugin) return NotFound();
        if (Text(body, "version") is not string version || _store.Version(plugin.Id, version, all: true) is not PluginVersion found) return NotFound();
        _store.DeleteVersion(found.Id);
        Console.WriteLine($"plugins: {plugin.Slug} {version} deleted by {account.Name}");
        return Ok(new JsonObject());
    }

    Reply Delete(Account account, JsonObject? body)
    {
        if (Managed(account, body) is not Plugin plugin) return NotFound();
        _store.Delete(plugin.Id);
        Console.WriteLine($"plugins: {plugin.Slug} deleted by {account.Name}");
        return Ok(new JsonObject { ["deleted"] = true });
    }

    Reply Report(Account account, JsonObject? body)
    {
        if (!_allow("plugin report " + account.Id, 10, Hour)) return TooMany();
        if (Text(body, "slug") is not string slug || _store.BySlug(slug) is not { State: PluginState.Public } plugin) return NotFound();
        string reason = Block(Text(body, "reason"));
        if (reason.Length is < MinReason or > MaxReason) return Error(400, $"write why in {MinReason} to {MaxReason} characters");
        _store.Report(plugin.Id, account.Id, reason);
        Console.WriteLine($"plugins: {plugin.Slug} reported by {account.Name}");
        return Ok(new JsonObject { ["reported"] = true });
    }

    Reply SetState(Account account, JsonObject? body)
    {
        if (!Admin(account) || Text(body, "slug") is not string slug || _store.BySlug(slug) is not Plugin plugin) return NotFound();
        PluginState? state = Text(body, "state") switch
        {
            "public" => PluginState.Public,
            "hidden" => PluginState.Hidden,
            "pending" => PluginState.Pending,
            _ => null,
        };
        if (state is not PluginState chosen) return Error(400, "unknown state");
        _store.SetState(plugin.Id, chosen);
        if (chosen == PluginState.Public) _store.ApproveVersions(plugin.Id);
        Console.WriteLine($"plugins: {plugin.Slug} is {StateName(chosen)} now, set by {account.Name}");
        return Ok(new JsonObject { ["state"] = StateName(chosen) });
    }

    Reply ClearReports(Account account, JsonObject? body)
    {
        if (!Admin(account) || Text(body, "slug") is not string slug || _store.BySlug(slug) is not Plugin plugin) return NotFound();
        _store.ClearReports(plugin.Id);
        return Ok(new JsonObject());
    }

    async Task<Reply> UploadAsync(HttpListenerRequest request)
    {
        if (TokenOf(request) is not string token || _accounts.Session(token) is not Account account) return Error(401, "sign in again");
        if (!_allow("plugin upload " + account.Id, 60, Hour)) return TooMany();
        if (request.ContentLength64 > MaxFile) return Error(413, "a file can be at most 32 MB");
        if (new DriveInfo(Path.GetFullPath(_folder)).AvailableFreeSpace < MinFree + Math.Max(0, request.ContentLength64))
            return Error(507, "there is no room for new files right now, try again later");
        string temp = Path.Combine(_folder, Guid.NewGuid().ToString("N") + ".part");
        try
        {
            (string? sha, long size) = await SaveAsync(request.InputStream, temp);
            if (sha is null) return Error(413, "a file can be at most 32 MB");
            if (ImageExtension(temp) is string extension)
            {
                if (size > MaxImage) return Error(413, "an image can be at most 1 MB");
                string image = Keep(temp, sha + extension);
                if (_moderation.On) _moderation.JudgeImage(sha, await File.ReadAllBytesAsync(image), $"plugin image from {account.Name}");
                return Ok(new JsonObject { ["file"] = sha + extension, ["kind"] = "image" });
            }
            if (Inspect(temp) is { } plugin)
            {
                Keep(temp, sha + plugin.Extension);
                return Ok(new JsonObject
                {
                    ["file"] = sha + plugin.Extension,
                    ["format"] = plugin.Extension.TrimStart('.'),
                    ["kind"] = "plugin",
                    ["version"] = plugin.Version,
                    ["api"] = plugin.Api,
                    ["name"] = plugin.Name,
                    ["size"] = size,
                });
            }
            return Error(400, "upload an OpenKogama plugin as a .dll or a .zip, or a png, jpg or webp image");
        }
        finally
        {
            File.Delete(temp);
        }
    }

    async Task<bool> DownloadAsync(HttpListenerRequest request, HttpListenerResponse response, string slug, string? version, string source)
    {
        if (_store.BySlug(slug) is not Plugin plugin) return false;
        bool counted = request.HttpMethod == "GET";
        if (counted && (plugin.State != PluginState.Public || !Allowed(plugin))) return false;
        if (!counted && (TokenOf(request) is not string token || _accounts.Session(token) is not Account account || plugin.Owner != account.Id && !Admin(account)))
            return false;
        if (_store.Version(plugin.Id, version, all: !counted) is not PluginVersion found) return false;
        string path = Path.Combine(_folder, found.File);
        if (!File.Exists(path)) return false;
        if (counted && _allow($"plugin download {found.Id} {source}", 1, Hour)) _store.CountDownload(found);
        await using FileStream stream = File.OpenRead(path);
        response.StatusCode = 200;
        response.ContentType = "application/octet-stream";
        response.AddHeader("Content-Disposition", $"attachment; filename=\"{found.Name}\"");
        response.AddHeader("X-Content-Type-Options", "nosniff");
        response.AddHeader("Cache-Control", "no-store");
        response.ContentLength64 = stream.Length;
        await stream.CopyToAsync(response.OutputStream);
        response.Close();
        return true;
    }

    async Task<bool> SendImageAsync(HttpListenerResponse response, string name)
    {
        if (!ImageFile().IsMatch(name) || !Shown(name)) return false;
        string path = Path.Combine(_folder, name);
        if (!File.Exists(path)) return false;
        await using FileStream stream = File.OpenRead(path);
        response.StatusCode = 200;
        response.ContentType = Path.GetExtension(name) switch { ".png" => "image/png", ".jpg" => "image/jpeg", _ => "image/webp" };
        response.AddHeader("Cache-Control", "public, max-age=31536000, immutable");
        response.AddHeader("X-Content-Type-Options", "nosniff");
        response.ContentLength64 = stream.Length;
        await stream.CopyToAsync(response.OutputStream);
        response.Close();
        return true;
    }

    JsonArray Cards(IEnumerable<Plugin> plugins, bool withState)
    {
        var cards = new JsonArray();
        foreach (Plugin plugin in plugins)
        {
            JsonObject card = Card(plugin, withState);
            if (withState) card["state"] = StateName(plugin.State);
            cards.Add(card);
        }
        return cards;
    }

    JsonObject Card(Plugin plugin, bool everything)
    {
        PluginVersion? latest = _store.Version(plugin.Id, null, all: everything);
        return new JsonObject
        {
            ["slug"] = plugin.Slug,
            ["name"] = plugin.Name,
            ["summary"] = plugin.Summary,
            ["author"] = plugin.Author,
            ["icon"] = plugin.Icon is string icon && (everything || Shown(icon)) ? icon : null,
            ["cover"] = plugin.Images.FirstOrDefault(image => !image.StartsWith("yt:", StringComparison.Ordinal) && (everything || Shown(image))),
            ["downloads"] = plugin.Downloads,
            ["created"] = plugin.Created,
            ["updated"] = plugin.Updated,
            ["version"] = latest?.Version,
            ["api"] = latest?.Api,
        };
    }

    JsonObject Full(Plugin plugin, bool everything)
    {
        JsonObject full = Card(plugin, everything);
        full["description"] = plugin.Description;
        full["video"] = plugin.Video;
        var images = new JsonArray();
        foreach (string image in plugin.Images)
            if (everything || Shown(image)) images.Add(image);
        full["images"] = images;
        var versions = new JsonArray();
        foreach (PluginVersion version in _store.Versions(plugin.Id, all: everything))
        {
            var entry = new JsonObject
            {
                ["version"] = version.Version,
                ["api"] = version.Api,
                ["name"] = version.Name,
                ["size"] = version.Size,
                ["sha256"] = Path.GetFileNameWithoutExtension(version.File),
                ["changes"] = everything || _moderation.Verdict(Moderation.TitleHash(version.Changes)) != false ? version.Changes : "",
                ["created"] = version.Created,
                ["downloads"] = version.Downloads,
                ["scan"] = Scan(version),
            };
            if (everything) entry["pending"] = version.Pending;
            versions.Add(entry);
        }
        full["versions"] = versions;
        if (everything) full["flagged"] = !Allowed(plugin);
        return full;
    }

    JsonObject? Scan(PluginVersion version) => !_scanner.On ? null : new JsonObject
    {
        ["state"] = version.Scan is null ? "pending" : "done",
        ["malicious"] = version.Scan?.Malicious,
        ["suspicious"] = version.Scan?.Suspicious,
        ["total"] = version.Scan?.Total,
        ["url"] = VirusScan.Link(version),
    };

    Plugin? Managed(Account account, JsonObject? body) =>
        Text(body, "slug") is string slug && _store.BySlug(slug) is Plugin plugin && (plugin.Owner == account.Id || Admin(account)) ? plugin : null;

    bool Admin(Account account) => _admins.Contains(account.Name);

    bool Allowed(Plugin plugin) => plugin.State == PluginState.Public || _moderation.Verdict(Moderation.TitleHash(Words(plugin.Name, plugin.Summary, plugin.Description))) != false;

    bool Shown(string file) => _moderation.Verdict(Path.GetFileNameWithoutExtension(file)) != false;

    bool Stored(string file, Regex pattern) => pattern.IsMatch(file) && File.Exists(Path.Combine(_folder, file));

    string Keep(string temp, string name)
    {
        string path = Path.Combine(_folder, name);
        if (!File.Exists(path)) File.Move(temp, path, overwrite: true);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
        return path;
    }

    void Sweep()
    {
        lock (_sync)
        {
            if (DateTime.UtcNow - _swept < Hour) return;
            _swept = DateTime.UtcNow;
        }
        _ = Task.Run(() =>
        {
            try
            {
                HashSet<string> used = _store.Files();
                foreach (string path in Directory.EnumerateFiles(_folder))
                {
                    string name = Path.GetFileName(path);
                    if ((PluginFile().IsMatch(name) || ImageFile().IsMatch(name)) && !used.Contains(name) && DateTime.UtcNow - File.GetLastWriteTimeUtc(path) > Unused)
                        File.Delete(path);
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                Console.WriteLine($"plugins: cleanup failed: {error.Message}");
            }
        });
    }

    string NewSlug(string name)
    {
        string plain = new([.. name.Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)]);
        string basis = SlugBreak().Replace(plain.ToLowerInvariant(), "-").Trim('-');
        if (basis.Length > MaxName) basis = basis[..MaxName].Trim('-');
        if (basis.Length == 0) basis = "plugin";
        string slug = basis;
        for (int number = 2; Reserved.Contains(slug) || _store.Taken(slug); number++) slug = $"{basis}-{number}";
        return slug;
    }

    static (string Version, string Api, string Name, string Extension)? Inspect(string path)
    {
        try
        {
            using FileStream stream = File.OpenRead(path);
            var head = new byte[4];
            int got = stream.Read(head, 0, 4);
            stream.Position = 0;
            if (got == 4 && head[0] == 'P' && head[1] == 'K' && head[2] == 3 && head[3] == 4) return InspectZip(stream);
            return InspectDll(stream) is { } dll ? (dll.Version, dll.Api, dll.Name + ".dll", ".dll") : null;
        }
        catch (Exception error) when (error is BadImageFormatException or InvalidDataException or IOException)
        {
            return null;
        }
    }

    static (string Version, string Api, string Name, string Extension)? InspectZip(Stream stream)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        if (zip.Entries.Count > MaxEntries || zip.Entries.Sum(entry => entry.Length) > MaxUnpacked) return null;
        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            string name = entry.FullName.Replace('\\', '/');
            if (name.StartsWith('/') || name.Split('/').Contains("..")) return null;
        }
        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            if (!entry.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || entry.Length > MaxFile) continue;
            using var copy = new MemoryStream();
            using (Stream opened = entry.Open()) opened.CopyTo(copy);
            copy.Position = 0;
            if (InspectDll(copy) is { } dll) return (dll.Version, dll.Api, dll.Name + ".zip", ".zip");
        }
        return null;
    }

    static (string Version, string Api, string Name)? InspectDll(Stream stream)
    {
        try
        {
            using var reader = new PEReader(stream, PEStreamOptions.LeaveOpen);
            if (!reader.HasMetadata) return null;
            MetadataReader metadata = reader.GetMetadataReader();
            if (!metadata.IsAssembly) return null;
            foreach (AssemblyReferenceHandle handle in metadata.AssemblyReferences)
            {
                AssemblyReference reference = metadata.GetAssemblyReference(handle);
                if (metadata.GetString(reference.Name) != ApiAssembly) continue;
                AssemblyDefinition assembly = metadata.GetAssemblyDefinition();
                Version version = assembly.Version;
                string name = FileName().Replace(metadata.GetString(assembly.Name), "");
                return ($"{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}", $"{reference.Version.Major}.{reference.Version.Minor}", name.Length > 0 ? name : "plugin");
            }
            return null;
        }
        catch (BadImageFormatException)
        {
            return null;
        }
    }

    static string? ImageExtension(string path)
    {
        using FileStream file = File.OpenRead(path);
        var head = new byte[Math.Min(12, file.Length)];
        file.ReadExactly(head);
        return Moderation.Type(head) switch
        {
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            "image/webp" => ".webp",
            _ => null,
        };
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
            if (size > MaxFile) return (null, size);
            hash.AppendData(buffer, 0, read);
            await output.WriteAsync(buffer.AsMemory(0, read));
        }
        return (Convert.ToHexStringLower(hash.GetHashAndReset()), size);
    }

    static bool Video(string? text, out string? id)
    {
        id = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        string trimmed = text.Trim();
        if (YouTube().Match(trimmed) is { Success: true } match) id = match.Groups[1].Value;
        else if (VideoId().IsMatch(trimmed)) id = trimmed;
        return id is not null;
    }

    static string Words(string name, string summary, string description) => string.Join('\n', name, summary, description);

    static string StateName(PluginState state) => state switch
    {
        PluginState.Public => "public",
        PluginState.Hidden => "hidden",
        _ => "pending",
    };

    static string Line(string? text) => text is null ? "" : new string([.. text.Where(c => !char.IsControl(c))]).Trim();

    static string Block(string? text) => text is null ? "" : new string([.. text.Replace("\r\n", "\n").Where(c => c == '\n' || !char.IsControl(c))]).Trim();

    static string? Text(JsonObject? body, string name) =>
        body?[name] is JsonValue value && value.TryGetValue(out string? text) ? text : null;

    static string? TokenOf(HttpListenerRequest request) =>
        request.Headers["Authorization"] is string header && header.StartsWith(Bearer, StringComparison.Ordinal) ? header[Bearer.Length..] : null;

    static async Task<JsonObject?> ReadAsync(HttpListenerRequest request)
    {
        var buffer = new byte[MaxJson + 1];
        int read = 0;
        while (read < buffer.Length && await request.InputStream.ReadAsync(buffer.AsMemory(read)) is int chunk and > 0) read += chunk;
        return read is > 0 and <= MaxJson ? JsonNode.Parse(buffer.AsSpan(0, read)) as JsonObject : null;
    }

    static async Task WriteAsync(HttpListenerResponse response, Reply reply)
    {
        byte[] data = Encoding.UTF8.GetBytes(reply.Body.ToJsonString());
        response.StatusCode = reply.Status;
        response.ContentType = "application/json";
        response.ContentLength64 = data.Length;
        await response.OutputStream.WriteAsync(data);
        response.Close();
    }

    static Reply Ok(JsonObject body) => new(200, body);
    static Reply Error(int status, string message) => new(status, new JsonObject { ["error"] = message });
    static Reply NotFound() => Error(404, "not found");
    static Reply TooMany() => Error(429, "too many tries, wait a bit");

    [GeneratedRegex("^[0-9a-f]{64}\\.(dll|zip)$")]
    private static partial Regex PluginFile();

    [GeneratedRegex("^[0-9a-f]{64}\\.(png|jpg|webp)$")]
    private static partial Regex ImageFile();

    [GeneratedRegex("^[0-9A-Za-z][0-9A-Za-z.+-]{0,31}$")]
    private static partial Regex VersionPattern();

    [GeneratedRegex(@"(?:youtube\.com/(?:watch\?(?:.*&)?v=|shorts/|embed/|live/)|youtu\.be/)([A-Za-z0-9_-]{11})")]
    private static partial Regex YouTube();

    [GeneratedRegex("^[A-Za-z0-9_-]{11}$")]
    private static partial Regex VideoId();

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex SlugBreak();

    [GeneratedRegex("[^A-Za-z0-9._-]")]
    private static partial Regex FileName();
}
