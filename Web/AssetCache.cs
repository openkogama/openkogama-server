using System.Text.Json;

namespace OpenKogama.Web;

public sealed class AssetCache(string remoteRoot, string folder)
{
    static readonly HttpClient Http = new();

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
        string manifest = Path.Combine(AppContext.BaseDirectory, "data", "streaming", "manifests", set + ".json");
        if (!File.Exists(manifest) && Version.TryParse(set, out _))
        {
            set = Game.BundleSets.Installed(set);
            manifest = Path.Combine(AppContext.BaseDirectory, "data", "streaming", "manifests", set + ".json");
            Console.WriteLine($"install: assets {set}");
        }
        if (!File.Exists(manifest))
        {
            Console.WriteLine($"install: no manifest for {set}");
            return false;
        }

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(manifest));
        string root = document.RootElement.GetProperty("root").GetString()!;
        List<(string Path, long Size)> files = [.. document.RootElement.GetProperty("files").EnumerateObject()
            .Select(file => (file.Name, file.Value.GetInt64()))];
        var cache = new AssetCache(root, Folder(set));
        long total = files.Sum(file => file.Size), done = 0;
        int failed = 0;

        foreach ((string path, long size) in files)
        {
            string local = cache.LocalPath(path);
            if (!File.Exists(local) || new FileInfo(local).Length != size)
            {
                try
                {
                    await cache.DownloadAsync(path);
                }
                catch (Exception error)
                {
                    failed++;
                    Console.WriteLine($"install: {path}: {error.Message}");
                }
            }
            done += size;
            Console.WriteLine($"progress {done} {total}");
        }

        Console.WriteLine(failed == 0 ? $"install: {set} ready" : $"install: {set} missing {failed} files");
        return failed == 0;
    }

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
