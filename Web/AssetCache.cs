using System.Net;
using System.Text.Json;

namespace OpenKogama.Web;

public sealed class AssetCache(string remoteRoot, string folder)
{
    static readonly HttpClient Http = new() { DefaultRequestVersion = HttpVersion.Version20, DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower };
    const int ParallelDownloads = 32;

    public static bool Offline { get; set; }

    public static string Folder(string set) => Path.Combine("bundles", set);

    public static AssetCache ForSet(string set, string remoteRoot)
    {
        string folder = Folder(set);
        string old = Path.Combine("cache", set switch { "2015" => "streaming", "3.x" => "streaming-3x", _ => $"streaming-{set}" });
        if (Directory.Exists(old) && !Directory.Exists(folder))
        {
            Directory.CreateDirectory("bundles");
            Directory.Move(old, folder);
        }
        return new AssetCache(remoteRoot, folder);
    }

    public async Task PrefetchAsync(IEnumerable<string> paths)
    {
        if (Offline) return;
        int fetched = 0;
        foreach (string path in paths)
        {
            try
            {
                if (!File.Exists(LocalPath(path)))
                {
                    await DownloadAsync(path);
                    fetched++;
                }
            }
            catch (Exception error)
            {
                Console.WriteLine($"assets: {path}: {error.Message}");
            }
        }
        if (fetched > 0) Console.WriteLine($"assets: {fetched} downloaded to {folder}");
    }

    public byte[]? Get(string path)
    {
        if (path.Contains("..")) return null;

        string local = LocalPath(path);
        if (!File.Exists(local))
        {
            if (Offline) return null;
            lock (_missing)
                if (_missing.Contains(path)) return null;
            try
            {
                DownloadAsync(path).GetAwaiter().GetResult();
            }
            catch (HttpRequestException error) when (error.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                lock (_missing) _missing.Add(path);
                return null;
            }
        }
        return File.Exists(local) ? File.ReadAllBytes(local) : null;
    }

    readonly HashSet<string> _missing = [];

    public static async Task<bool> InstallAsync(string set)
    {
        List<string> sets = !File.Exists(Manifest(set)) && Version.TryParse(set.Split('@')[0], out _) ? Game.BundleSets.Installed(set) : [set];
        string label = string.Join(' ', sets);
        if (label != set) Console.WriteLine($"install: assets {label}");

        List<(AssetCache Cache, string Path, long Size)> files = [];
        foreach (string name in sets)
        {
            if (!File.Exists(Manifest(name)))
            {
                Console.WriteLine($"install: no manifest for {name}");
                return false;
            }
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(Manifest(name)));
            var cache = new AssetCache(document.RootElement.GetProperty("root").GetString()!, Folder(name));
            files.AddRange(document.RootElement.GetProperty("files").EnumerateObject().Select(file => (cache, file.Name, file.Value.GetInt64())));
        }
        long total = files.Sum(file => file.Size), done = 0;
        int failed = 0;
        object sync = new();

        await Parallel.ForEachAsync(files, new ParallelOptions { MaxDegreeOfParallelism = ParallelDownloads }, async (file, _) =>
        {
            bool ok = true;
            string local = file.Cache.LocalPath(file.Path);
            if (!File.Exists(local) || new FileInfo(local).Length != file.Size)
            {
                try
                {
                    await file.Cache.DownloadAsync(file.Path);
                }
                catch (Exception error)
                {
                    ok = false;
                    Console.WriteLine($"install: {file.Path}: {error.Message}");
                }
            }
            lock (sync)
            {
                if (!ok) failed++;
                done += file.Size;
                Console.WriteLine($"progress {done} {total}");
            }
        });

        Console.WriteLine(failed == 0 ? $"install: {label} ready" : $"install: {label} missing {failed} files");
        return failed == 0;
    }

    static string Manifest(string set) => Path.Combine(AppContext.BaseDirectory, "data", "streaming", "manifests", set + ".json");

    async Task DownloadAsync(string path)
    {
        byte[] data = await Http.GetByteArrayAsync(remoteRoot + path);
        string local = LocalPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(local)!);
        await File.WriteAllBytesAsync(local + ".tmp", data);
        File.Move(local + ".tmp", local, overwrite: true);
    }

    string LocalPath(string path) => Path.Combine(folder, path.Replace('/', Path.DirectorySeparatorChar));
}
