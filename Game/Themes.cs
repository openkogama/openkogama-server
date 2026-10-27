using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OpenKogama.Game;

public sealed class ThemeEntry
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required string Folder { get; init; }
    public required string Base { get; init; }
    public bool Custom { get; init; }
    public required IReadOnlyDictionary<string, object> Defaults { get; init; }
    public required IReadOnlyDictionary<string, string> Textures { get; init; }
    public Dictionary<string, string> Replaced { get; } = [];
    public ThemeCycleData? Cycle { get; set; }
    public int Price { get; set; }
    public int Level { get; set; }
    public bool InMenu { get; set; } = true;
}

public static class Themes
{
    const int FirstCustomId = 100_000;
    const int CustomIdRange = 100_000;
    static readonly List<ThemeEntry> BuiltIn = Load();
    static readonly List<ThemeEntry> Custom = [];
    static readonly object Sync = new();
    static string? _version;

    public static bool MenuEnabled { get; set; } = true;

    public static IReadOnlyList<ThemeEntry> All
    {
        get { lock (Sync) return [.. BuiltIn, .. Custom]; }
    }

    public static ThemeEntry? Find(string? name)
    {
        lock (Sync) return BuiltIn.Find(theme => theme.Name == name) ?? Custom.Find(theme => theme.Name == name);
    }

    public static ThemeEntry? Find(int id)
    {
        lock (Sync) return BuiltIn.Find(theme => theme.Id == id) ?? Custom.Find(theme => theme.Id == id);
    }

    public static bool BuiltInName(string name) => BuiltIn.Exists(theme => theme.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public static int NewId(string name)
    {
        uint hash = 2166136261;
        foreach (char c in name.ToLowerInvariant()) hash = (hash ^ c) * 16777619;
        lock (Sync)
        {
            int id = FirstCustomId + (int)(hash % CustomIdRange);
            while (Custom.Exists(theme => theme.Id == id && theme.Name != name)) id = FirstCustomId + (id - FirstCustomId + 1) % CustomIdRange;
            return id;
        }
    }

    public static ThemeEntry Add(ThemeEntry theme)
    {
        lock (Sync)
        {
            Custom.RemoveAll(existing => existing.Name == theme.Name);
            Custom.Add(theme);
            _version = null;
        }
        return theme;
    }

    public static bool Remove(string name)
    {
        lock (Sync)
        {
            _version = null;
            return Custom.RemoveAll(theme => theme.Name == name) > 0;
        }
    }

    public static IReadOnlyList<string> ReplacedOf(ThemeEntry theme)
    {
        lock (Sync) return [.. theme.Replaced.Keys];
    }

    public static bool SetTexture(ThemeEntry theme, string slot, string? url)
    {
        lock (Sync)
        {
            _version = null;
            if (url is not null)
            {
                theme.Replaced[slot] = url;
                return true;
            }
            return theme.Replaced.Remove(slot);
        }
    }

    public static void SetCycle(ThemeEntry theme, ThemeCycleData? cycle)
    {
        lock (Sync)
        {
            _version = null;
            theme.Cycle = cycle;
        }
    }

    public static string Version
    {
        get
        {
            lock (Sync)
            {
                if (_version is not null) return _version;
                List<object> specs = Specs();
                _version = specs.Count == 0 ? "" : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(specs))))[..12];
                return _version;
            }
        }
    }

    public static List<object> Specs()
    {
        lock (Sync)
        {
            List<object> specs = [.. BuiltIn.Where(theme => theme.Replaced.Count > 0 || theme.Cycle is not null).Select(theme => (object)new
            {
                name = theme.Name,
                @base = theme.Base,
                textures = theme.Replaced,
                cycle = theme.Cycle?.ToJson(),
            })];
            specs.AddRange(Custom.Select(theme => (object)new
            {
                name = theme.Name,
                @base = theme.Base,
                title = theme.Title,
                description = theme.Description,
                settings = ThemeValues.ToJson(theme.Defaults),
                textures = theme.Replaced,
                cycle = theme.Cycle?.ToJson(),
            }));
            return specs;
        }
    }

    public static string Catalog(bool custom) => JsonSerializer.Serialize(All.Where(theme => theme.InMenu && (custom || !theme.Custom)).Select(theme => new
    {
        id = theme.Id,
        themeIdentifier = theme.Name,
        priceGold = theme.Price,
        levelRequirement = theme.Level,
    }));

    public static void Reset()
    {
        lock (Sync)
        {
            MenuEnabled = true;
            Custom.Clear();
            foreach (ThemeEntry theme in BuiltIn)
            {
                theme.Price = 0;
                theme.Level = 0;
                theme.InMenu = true;
                theme.Replaced.Clear();
                theme.Cycle = null;
            }
            _version = null;
        }
    }

    static List<ThemeEntry> Load()
    {
        using JsonDocument json = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "themes.json")));
        return [.. json.RootElement.EnumerateArray().Select((theme, index) => new ThemeEntry
        {
            Id = index + 1,
            Name = theme.GetProperty("name").GetString()!,
            Title = theme.GetProperty("title").GetString()!,
            Description = theme.GetProperty("description").GetString()!,
            Folder = theme.GetProperty("folder").GetString()!,
            Base = theme.GetProperty("name").GetString()!,
            Defaults = ThemeValues.FromJson(theme.GetProperty("settings")),
            Textures = theme.GetProperty("textures").EnumerateObject().ToDictionary(texture => texture.Name, texture => texture.Value.GetString()!),
        })];
    }
}
