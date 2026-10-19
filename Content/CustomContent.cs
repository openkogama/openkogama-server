using System.Text.Json;
using System.Text.RegularExpressions;
using OpenKogama.Game;

namespace OpenKogama.Content;

public static partial class CustomContent
{
    public static readonly AssetLibrary<CustomSound> Sounds = new("sound");
    public static readonly AssetLibrary<CustomBundle> Bundles = new("bundle");
    static readonly Dictionary<string, byte[]?> Templates = [];

    public static void Clear()
    {
        Sounds.Clear();
        Bundles.Clear();
    }

    public static bool IsBundleUrl(object? value) => value is string url && url.EndsWith(".unity3d", StringComparison.OrdinalIgnoreCase);

    public static byte[]? Bundle(string set, string file)
    {
        Match match = BundlePath().Match(file);
        if (!match.Success) return null;

        int id = int.Parse(match.Groups[2].Value);
        CustomAsset? asset = match.Groups[1].Value.ToLowerInvariant() switch
        {
            "sound" => Sounds.Find(id),
            "bundle" => Bundles.Find(id),
            _ => null,
        };
        if (asset is null) return null;
        try
        {
            return asset.BundleFor(BundleSets.EngineOf(set));
        }
        catch (Exception error)
        {
            Console.WriteLine($"{asset.Kind} {asset.Name}: {error.Message}");
            return null;
        }
    }

    public static string Json(string? client)
    {
        string? engine = client is not null && BundleSets.For(client) is string set ? BundleSets.EngineOf(set) : null;
        return JsonSerializer.Serialize(new
        {
            sounds = Sounds.All.Where(sound => engine is null || sound.Fits(engine)).Select(sound => new { url = sound.Url, name = sound.Name, group = sound.Group }),
        });
    }

    public static byte[]? Template(string kind, string engine)
    {
        lock (Templates)
        {
            string key = kind + "/" + engine;
            if (!Templates.TryGetValue(key, out byte[]? template))
            {
                string path = Path.Combine(AppContext.BaseDirectory, "data", "streaming", "content-templates", kind, engine + ".cab");
                Templates[key] = template = File.Exists(path) ? File.ReadAllBytes(path) : null;
            }
            return template;
        }
    }

    public static bool SameEngine(string bundle, string set) =>
        bundle == set || bundle.StartsWith(set + ".", StringComparison.Ordinal);

    [GeneratedRegex(@"(?:^|/)Custom/(sound|bundle)/(\d+)-[0-9a-f]+\.unity3d$", RegexOptions.IgnoreCase)]
    private static partial Regex BundlePath();
}
