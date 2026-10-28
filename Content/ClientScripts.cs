using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace OpenKogama.Content;

public static partial class ClientScripts
{
    const int FileLimit = 1 << 20;
    const int TotalLimit = 16 << 20;
    const int FileCount = 64;
    static readonly string[] Kinds = [".lua", ".png", ".jpg", ".jpeg", ".json", ".txt"];
    static readonly object Sync = new();
    static readonly Dictionary<string, Dictionary<string, byte[]>> Added = new(StringComparer.Ordinal);
    static string _folder = "scripts";
    static DateTime _built = DateTime.MinValue;
    static Snapshot _current = new("", new Dictionary<string, byte[]>());
    static string _announced = "";

    sealed record Snapshot(string Manifest, IReadOnlyDictionary<string, byte[]> Files);

    [GeneratedRegex(@"^[A-Za-z0-9_-]{1,48}$")]
    private static partial Regex NamePattern();

    [GeneratedRegex(@"^[A-Za-z0-9_.-]{1,64}(/[A-Za-z0-9_.-]{1,64}){0,3}$")]
    private static partial Regex PathPattern();

    public static void Use(string folder)
    {
        lock (Sync)
        {
            _folder = folder;
            _built = DateTime.MinValue;
        }
    }

    public static IReadOnlyList<string> Names
    {
        get
        {
            lock (Sync) return [.. Collect().Keys];
        }
    }

    public static bool Add(string name, IReadOnlyDictionary<string, byte[]> files)
    {
        if (!NamePattern().IsMatch(name))
        {
            Console.WriteLine($"scripts: {name} is not a script name, use letters, digits, _ and - only");
            return false;
        }
        if (Accept(files) is not { } accepted)
        {
            Console.WriteLine($"scripts: {name} has no main.lua");
            return false;
        }
        lock (Sync)
        {
            Added[name] = accepted;
            _built = DateTime.MinValue;
        }
        return true;
    }

    public static bool Remove(string name)
    {
        lock (Sync)
        {
            _built = DateTime.MinValue;
            return Added.Remove(name);
        }
    }

    public static void ClearAdded()
    {
        lock (Sync)
        {
            Added.Clear();
            _built = DateTime.MinValue;
        }
    }

    public static string Manifest() => Current().Manifest;

    public static bool Any() => Current().Manifest.Length > 0;

    public static byte[]? Find(string hash) => Current().Files.GetValueOrDefault(hash);

    static Snapshot Current()
    {
        lock (Sync)
        {
            if (DateTime.UtcNow - _built < TimeSpan.FromSeconds(2)) return _current;
            _current = Build(Collect());
            _built = DateTime.UtcNow;
            return _current;
        }
    }

    static SortedDictionary<string, Dictionary<string, byte[]>> Collect()
    {
        SortedDictionary<string, Dictionary<string, byte[]>> scripts = new(StringComparer.Ordinal);
        if (Directory.Exists(_folder))
            foreach (string folder in Directory.GetDirectories(_folder))
            {
                string name = Path.GetFileName(folder);
                if (NamePattern().IsMatch(name) && Read(folder) is { } files) scripts[name] = files;
            }
        foreach ((string name, Dictionary<string, byte[]> files) in Added) scripts[name] = files;
        return scripts;
    }

    static Dictionary<string, byte[]>? Read(string folder)
    {
        Dictionary<string, byte[]> files = new(StringComparer.Ordinal);
        foreach (string file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            string path = Path.GetRelativePath(folder, file).Replace('\\', '/');
            if (files.Count >= FileCount || !Allowed(path)) continue;
            try
            {
                if (new FileInfo(file).Length <= FileLimit) files[path] = File.ReadAllBytes(file);
            }
            catch (IOException)
            {
            }
        }
        return files.ContainsKey("main.lua") ? files : null;
    }

    static Dictionary<string, byte[]>? Accept(IReadOnlyDictionary<string, byte[]> files)
    {
        Dictionary<string, byte[]> accepted = new(StringComparer.Ordinal);
        foreach ((string path, byte[] data) in files)
        {
            string clean = path.Replace('\\', '/');
            if (Allowed(clean) && data.Length <= FileLimit && accepted.Count < FileCount) accepted[clean] = data.ToArray();
        }
        return accepted.ContainsKey("main.lua") ? accepted : null;
    }

    static bool Allowed(string path) =>
        PathPattern().IsMatch(path) && !path.Split('/').Any(part => part is "." or "..") && Kinds.Contains(Path.GetExtension(path).ToLowerInvariant());

    static Snapshot Build(SortedDictionary<string, Dictionary<string, byte[]>> scripts)
    {
        StringBuilder manifest = new();
        Dictionary<string, byte[]> files = [];
        List<string> sent = [];
        long total = 0;
        foreach ((string name, Dictionary<string, byte[]> content) in scripts)
        {
            long size = content.Values.Sum(data => (long)data.Length);
            if (total + size > TotalLimit) continue;
            total += size;
            sent.Add(name);
            foreach ((string path, byte[] data) in content.OrderBy(file => file.Key, StringComparer.Ordinal))
            {
                string hash = Convert.ToHexStringLower(SHA256.HashData(data));
                files[hash] = data;
                manifest.Append(name).Append(' ').Append(path).Append(' ').Append(hash).Append(' ').Append(data.Length).Append('\n');
            }
        }
        string names = string.Join(", ", sent);
        if (names != _announced)
        {
            _announced = names;
            Console.WriteLine(sent.Count == 0 ? "scripts: none for clients" : $"scripts: {names} for clients");
            if (sent.Count < scripts.Count) Console.WriteLine($"scripts: {string.Join(", ", scripts.Keys.Except(sent))} left out, clients take up to {TotalLimit >> 20} MB of scripts");
        }
        return new Snapshot(manifest.ToString(), files);
    }
}
