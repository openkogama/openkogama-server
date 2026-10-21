using System.Text.Json;

namespace OpenKogama.Content;

public static class GameBundles
{
    const string Root = "AssetBundles/";
    const string Extension = ".unity3d";
    static readonly Lazy<List<string>> Known = new(Load);
    static readonly Lazy<Dictionary<string, string>> ByPath = new(() =>
        Known.Value.ToDictionary(path => path, path => path, StringComparer.OrdinalIgnoreCase));

    public static IReadOnlyList<string> All => Known.Value;

    public static string Normalize(string path)
    {
        string normal = path.Replace('\\', '/').Trim('/');
        if (normal.StartsWith(Root, StringComparison.OrdinalIgnoreCase)) normal = normal[Root.Length..];
        return normal.EndsWith(Extension, StringComparison.OrdinalIgnoreCase) ? normal[..^Extension.Length] : normal;
    }

    public static string? Find(string path)
    {
        string normal = Normalize(path);
        return Known.Value.Count == 0 ? normal : ByPath.Value.GetValueOrDefault(normal);
    }

    public static string CacheName(string path) => Path.GetFileName(Normalize(path));

    public static IEnumerable<string> SharingCache(string path) =>
        Known.Value.Where(other => !other.Equals(path, StringComparison.OrdinalIgnoreCase)
            && CacheName(other).Equals(CacheName(path), StringComparison.OrdinalIgnoreCase));

    static List<string> Load()
    {
        string folder = Path.Combine(AppContext.BaseDirectory, "data", "streaming", "manifests");
        if (!Directory.Exists(folder)) return [];
        var paths = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string manifest in Directory.EnumerateFiles(folder, "*.json"))
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(manifest));
            if (!document.RootElement.TryGetProperty("files", out JsonElement files)) continue;
            foreach (JsonProperty file in files.EnumerateObject())
                if (file.Name.EndsWith(Extension, StringComparison.OrdinalIgnoreCase)) paths.Add(Normalize(file.Name));
        }
        return [.. paths];
    }
}
