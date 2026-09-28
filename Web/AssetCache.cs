namespace OpenKogama.Web;

public sealed class AssetCache(string remoteRoot, string folder = "cache/streaming")
{
    static readonly HttpClient Http = new();

    public async Task PrefetchAsync(IEnumerable<string> paths)
    {
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
