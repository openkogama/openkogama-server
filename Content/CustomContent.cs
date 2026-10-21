using System.Text.Json;
using System.Text.RegularExpressions;
using OpenKogama.Api;
using OpenKogama.Game;
using OpenKogama.Photon;

namespace OpenKogama.Content;

public static partial class CustomContent
{
    public static readonly AssetLibrary<CustomSound> Sounds = new("sound");
    public static readonly AssetLibrary<CustomBundle> Bundles = new("bundle");
    public static readonly ReplacedSounds ReplacedSounds = new();
    public static readonly ReplacedBundles ReplacedBundles = new();
    static readonly Dictionary<string, byte[]?> Templates = [];
    static readonly object Building = new();
    const int AmbientAudio = 1;
    const string WebPlayerSet = "3x";
    static readonly string[] Categories = ["", "Nature", "Machinery", "Urban", "MusicLoop", "Alarms", "Horror", "Delight", "Suspense", "Robotic"];
    static readonly Dictionary<string, string> CategoryAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Music"] = "MusicLoop",
        ["Alarm"] = "Alarms",
        ["Suspence"] = "Suspense",
    };

    public static void Clear()
    {
        Sounds.Clear();
        Bundles.Clear();
        ReplacedSounds.Clear();
        ReplacedBundles.Clear();
    }

    public static byte[] Replace(string set, string file, byte[] original) =>
        ReplacedBundles.For(set, file) ?? ReplacedSounds.Apply(set, file, original);

    public static string GroupName(SoundGroup group) => group == SoundGroup.Suspense ? "Suspence" : group.ToString();

    public static byte[] Built(string file, Func<byte[]> build)
    {
        lock (Building)
        {
            if (File.Exists(file)) return File.ReadAllBytes(file);
            byte[] bundle = build();
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllBytes(file + ".tmp", bundle);
            File.Move(file + ".tmp", file, overwrite: true);
            return bundle;
        }
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
            return asset.BundleFor(asset is CustomSound ? SoundKey(set) : BundleSets.EngineOf(set));
        }
        catch (Exception error)
        {
            Console.WriteLine($"{asset.Kind} {asset.Name}: {error.Message}");
            return null;
        }
    }

    public static string Json(string? client)
    {
        string? key = client is not null ? SoundKey(BundleSets.For(client) ?? BundleSets.Legacy) : null;
        return JsonSerializer.Serialize(new
        {
            sounds = Sounds.All.Where(sound => key is null || sound.Fits(key)).Select(sound => new { url = sound.Url, name = sound.Name, group = sound.Group }),
            replaced = ReplacedSounds.All.Select(GameSoundFiles.CacheName)
                .Concat(ReplacedBundles.All.Select(GameBundles.CacheName))
                .Distinct(StringComparer.OrdinalIgnoreCase),
        });
    }

    // Which sound template a bundle set gets: the oldest engine of its era, since newer players load older bundles.
    public static string SoundKey(string set)
    {
        bool webgl = set.EndsWith(BundleSets.WebGL, StringComparison.Ordinal);
        string name = webgl ? set[..^BundleSets.WebGL.Length] : set;
        string key = name switch
        {
            WebPlayerSet => "3.5",
            "" or BundleSets.Legacy or "v2" => webgl ? "5.2" : "5.0",
            _ => BundleSets.EngineOf(name) switch
            {
                "5" => "5.3",
                string engine when engine.StartsWith("2018.", StringComparison.Ordinal) => "2018.2",
                string engine => engine,
            },
        };
        return webgl ? key + BundleSets.WebGL : key;
    }

    public static string SoundKey(PhotonPeer peer, Player player) =>
        peer.Translator is Kogama.Protocols.LegacyTranslator ? SoundKey(WebPlayerSet)
            : SoundKey((BundleSets.For(player.Build, player.Unity) ?? BundleSets.Legacy) + (peer.IsWebSocket ? BundleSets.WebGL : ""));

    // Older clients play only sounds their streaming list knows, so custom sounds go in there as free ambient audio.
    public static IEnumerable<StreamingAsset> Listed(PhotonPeer peer, Player? player, object? types)
    {
        if (player is null || types is int[] { Length: > 0 } wanted && !wanted.Contains(AmbientAudio)) return [];
        string key = SoundKey(peer, player);
        return Sounds.All.Where(sound => sound.Fits(key)).Select(Listing);
    }

    public static StreamingAsset? Find(int id) => Sounds.Find(id) is { } sound ? Listing(sound) : null;

    public static StreamingAsset? FindByUrl(string url) => Sounds.All.FirstOrDefault(sound => sound.Url == url) is { } sound ? Listing(sound) : null;

    static StreamingAsset Listing(CustomSound sound) => new()
    {
        Id = sound.Id,
        Type = AmbientAudio,
        Category = Category(sound.Group),
        Name = sound.Name,
        Description = sound.Name,
        Path = sound.Url,
    };

    static int Category(string group)
    {
        string wanted = group.Replace(" ", "");
        wanted = CategoryAliases.GetValueOrDefault(wanted, wanted);
        int index = Array.FindIndex(Categories, name => name.Length > 0 && name.Equals(wanted, StringComparison.OrdinalIgnoreCase));
        return index > 0 ? index : 1;
    }

    public static byte[]? Template(string kind, string key)
    {
        lock (Templates)
        {
            string name = kind + "/" + key;
            if (!Templates.TryGetValue(name, out byte[]? template))
            {
                string path = Path.Combine(AppContext.BaseDirectory, "data", "streaming", "content-templates", kind, key + ".unity3d");
                Templates[name] = template = File.Exists(path) ? File.ReadAllBytes(path) : null;
            }
            return template;
        }
    }

    public static bool SameEngine(string bundle, string set) =>
        bundle == set || bundle.StartsWith(set + ".", StringComparison.Ordinal);

    [GeneratedRegex(@"(?:^|/)Custom/(sound|bundle)/(\d+)-[0-9a-f]+\.unity3d$", RegexOptions.IgnoreCase)]
    private static partial Regex BundlePath();
}
