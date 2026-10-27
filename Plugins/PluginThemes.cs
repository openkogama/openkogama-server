using System.Reflection;
using System.Runtime.CompilerServices;
using OpenKogama.Api;
using OpenKogama.Content;
using OpenKogama.Game;
using OpenKogama.Mods.Patches;

namespace OpenKogama.Plugins;

sealed class PluginTheme(ThemeEntry entry) : ITheme
{
    public ThemeEntry Entry => entry;
    public int Id => entry.Id;
    public string Name => entry.Name;
    public string Title => entry.Title;
    public string Description => entry.Description;
    public GameTheme Base => Enum.Parse<GameTheme>(entry.Base);
    public bool Custom => entry.Custom;
    public ThemeSettings Defaults => ThemeSettingsMap.ToSettings(entry.Defaults);
    public IReadOnlyList<ThemeTexture> Replaced => [.. Themes.ReplacedOf(entry).Select(PluginThemes.TextureOf)];

    public ThemeCycle? Cycle
    {
        get => entry.Cycle is { } cycle ? ThemeCycleMap.ToApi(cycle) : null;
        set => Themes.SetCycle(entry, value is null ? null : ThemeCycleMap.FromApi(value));
    }

    public int Price
    {
        get => entry.Price;
        set => entry.Price = Math.Max(0, value);
    }

    public int Level
    {
        get => entry.Level;
        set => entry.Level = Math.Max(0, value);
    }

    public bool InMenu
    {
        get => entry.InMenu;
        set => entry.InMenu = value;
    }
}

sealed class PluginThemes : IThemes
{
    static readonly ConditionalWeakTable<ThemeEntry, PluginTheme> Known = [];

    public static PluginTheme Of(ThemeEntry entry) => Known.GetValue(entry, theme => new PluginTheme(theme));

    public static ThemeEntry? EntryOf(ITheme theme) => theme is PluginTheme known ? known.Entry : Themes.Find(theme.Name);

    public IReadOnlyList<ITheme> All => [.. Themes.All.Select(Of)];

    public ITheme this[GameTheme theme] =>
        Themes.Find(theme.ToString()) is { } entry ? Of(entry) : throw new ArgumentOutOfRangeException(nameof(theme));

    public ITheme? Find(string name) => Themes.Find(name) is { } entry ? Of(entry) : null;

    public bool MenuEnabled
    {
        get => Themes.MenuEnabled;
        set => Themes.MenuEnabled = value;
    }

    public ITheme Add(CustomTheme theme)
    {
        string name = new([.. theme.Name.Trim().Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_').Take(CustomAsset.MaxName)]);
        if (name.Length == 0) throw new ArgumentException("a theme needs a name made of letters, digits, spaces, - or _", nameof(theme));
        if (Themes.BuiltInName(name)) throw new ArgumentException($"{name} is one of the game's themes, pick another name", nameof(theme));
        ThemeEntry basis = Themes.Find(theme.Base.ToString()) ?? throw new ArgumentOutOfRangeException(nameof(theme));

        var defaults = new Dictionary<string, object>(basis.Defaults);
        foreach ((string key, object value) in ThemeSettingsMap.FromSettings(theme.Defaults)) defaults[key] = value;
        var entry = new ThemeEntry
        {
            Id = Themes.NewId(name),
            Name = name,
            Title = CustomAsset.Clean(theme.Title ?? name, name),
            Description = theme.Description?.Trim() ?? "",
            Folder = basis.Folder,
            Base = basis.Name,
            Custom = true,
            Defaults = defaults,
            Textures = basis.Textures,
            Cycle = theme.Cycle is null ? null : ThemeCycleMap.FromApi(theme.Cycle),
        };
        foreach ((ThemeTexture texture, byte[] image) in theme.Textures)
            entry.Replaced[SlotOf(texture)] = Texture(entry, texture, image).Url;
        return Of(Themes.Add(entry));
    }

    public bool Remove(ITheme theme) => theme.Custom && Themes.Remove(theme.Name);

    public void Replace(GameTheme theme, ThemeTexture texture, byte[] image)
    {
        ThemeEntry entry = Themes.Find(theme.ToString()) ?? throw new ArgumentOutOfRangeException(nameof(theme));
        Themes.SetTexture(entry, SlotOf(texture), Texture(entry, texture, image).Url);
    }

    public bool Restore(GameTheme theme, ThemeTexture texture) =>
        Themes.Find(theme.ToString()) is { } entry && Themes.SetTexture(entry, SlotOf(texture), null);

    public static string SlotOf(ThemeTexture texture)
    {
        if (!Enum.IsDefined(texture)) throw new ArgumentOutOfRangeException(nameof(texture));
        string name = texture.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    public static ThemeTexture TextureOf(string slot) => Enum.Parse<ThemeTexture>(slot, ignoreCase: true);

    static CustomTexture Texture(ThemeEntry theme, ThemeTexture texture, byte[] image)
    {
        string slot = SlotOf(texture);
        Rgba decoded = Rgba.Decode(image);
        if (texture is ThemeTexture.Clouds or ThemeTexture.Stars) SkyImages.FaceSize(decoded, SkyImages.MaxSide);
        return CustomContent.Textures.Add(new CustomTexture
        {
            Name = CustomAsset.Clean($"{slot} {theme.Name}", "Texture"),
            Group = "Themes",
            Data = image,
            Version = CustomAsset.VersionOf([.. image, (byte)texture, TextureBundles.Revision]),
            Template = $"AssetBundles/Themes/{theme.Folder}/{theme.Textures[slot]}",
            Look = texture switch
            {
                ThemeTexture.Preview => TextureLook.Picture,
                ThemeTexture.SunFlare or ThemeTexture.MoonFlare => TextureLook.Light,
                ThemeTexture.Stars => TextureLook.Glow,
                _ => TextureLook.Mask,
            },
        });
    }
}

sealed class PluginWorldTheme(Session session) : IWorldTheme
{
    public ITheme? Current => session.Theme.Current is { } entry ? PluginThemes.Of(entry) : null;

    public ThemeSettings? Settings => session.Theme.Settings is { } settings ? ThemeSettingsMap.ToSettings(settings) : null;

    public int? ObjectId => session.Theme.Object?.Id;

    public float Time
    {
        get => session.Theme.Time;
        set => session.Theme.SetTime(value);
    }

    public bool Set(GameTheme theme, ThemeSettings settings) =>
        Themes.Find(theme.ToString()) is { } entry && Set(entry, settings);

    public bool Set(ITheme theme, ThemeSettings settings) =>
        PluginThemes.EntryOf(theme) is { } entry && Set(entry, settings);

    public bool Change(ThemeSettings settings) => session.Theme.Change(ThemeSettingsMap.FromSettings(settings));

    public bool Remove() => session.Theme.Remove();

    bool Set(ThemeEntry entry, ThemeSettings settings)
    {
        session.Theme.Set(entry, ThemeSettingsMap.FromSettings(settings));
        return true;
    }
}

static class ThemeCycleMap
{
    const float MaxIntensity = 8f;

    public static ThemeCycleData FromApi(ThemeCycle cycle)
    {
        (float? sunrise, float? sunset) = Orbit(cycle.Sunrise, cycle.Sunset, "sun");
        (float? moonrise, float? moonset) = Orbit(cycle.Moonrise, cycle.Moonset, "moon");
        return new ThemeCycleData
        {
            Sunrise = sunrise,
            Sunset = sunset,
            Moonrise = moonrise,
            Moonset = moonset,
            Sky = Keys(cycle.Sky, key => key.Time, "sky", (time, key) => new SkyKey(time, Color(key.Top), Color(key.Bottom))),
            Sun = Keys(cycle.Sun, key => key.Time, "sun", Light),
            Moon = Keys(cycle.Moon, key => key.Time, "moon", Light),
            Stars = Keys(cycle.Stars, key => key.Time, "stars", (time, key) => new StarsKey(time, Color(key.Tint))),
        };
    }

    public static ThemeCycle ToApi(ThemeCycleData cycle)
    {
        (float? sunrise, float? sunset) = Horizon(cycle.Sunrise, cycle.Sunset);
        (float? moonrise, float? moonset) = Horizon(cycle.Moonrise, cycle.Moonset);
        return new ThemeCycle
        {
            Sunrise = sunrise,
            Sunset = sunset,
            Moonrise = moonrise,
            Moonset = moonset,
            Sky = [.. cycle.Sky.Select(key => new ThemeSkyKey(key.Time, Color(key.Top), Color(key.Bottom)))],
            Sun = [.. cycle.Sun.Select(key => new ThemeLightKey(key.Time, Color(key.Tint), Color(key.Light), key.Intensity))],
            Moon = [.. cycle.Moon.Select(key => new ThemeLightKey(key.Time, Color(key.Tint), Color(key.Light), key.Intensity))],
            Stars = [.. cycle.Stars.Select(key => new ThemeStarsKey(key.Time, Color(key.Tint)))],
        };
    }

    static (float?, float?) Orbit(float? rise, float? set, string body)
    {
        if (rise is null && set is null) return (null, null);
        if (rise is not { } up || set is not { } down || !float.IsFinite(up) || !float.IsFinite(down))
            throw new ArgumentException($"give the {body} both a rise and a set time, or neither");
        (float start, float end) = ThemeCycleData.Orbit(up, down);
        return (start, end);
    }

    static (float?, float?) Horizon(float? start, float? end)
    {
        if (start is not { } from || end is not { } to) return (null, null);
        (float rise, float set) = ThemeCycleData.Horizon(from, to);
        return (rise, set);
    }

    static LightKey Light(float time, ThemeLightKey key) =>
        new(time, Color(key.Tint), Color(key.Light), float.IsFinite(key.Intensity) ? Math.Clamp(key.Intensity, 0f, MaxIntensity) : 0f);

    static List<T> Keys<TKey, T>(IReadOnlyList<TKey> keys, Func<TKey, float> timeOf, string part, Func<float, TKey, T> make)
    {
        var made = new SortedList<float, T>();
        foreach (TKey key in keys)
        {
            float time = timeOf(key);
            if (!float.IsFinite(time)) throw new ArgumentException($"a {part} key has no real time");
            time = ThemeCycleData.Wrap(time);
            if (!made.TryAdd(time, make(time, key))) throw new ArgumentException($"the {part} has two keys at time {time:0.###}");
        }
        return [.. made.Values];
    }

    static float[] Color(ThemeColor color) => [color.R, color.G, color.B, color.A];

    static ThemeColor Color(float[] rgba) => new(rgba[0], rgba[1], rgba[2], rgba[3]);
}

static class ThemeSettingsMap
{
    static readonly Dictionary<string, string> Renamed = new()
    {
        [nameof(ThemeSettings.DayNightCycle)] = "useDayNightCycle",
        [nameof(ThemeSettings.ServerTime)] = "useServerTime",
        [nameof(ThemeSettings.Palette)] = "colorPresetIndex",
        [nameof(ThemeSettings.StartTime)] = "gameStartTime",
        [nameof(ThemeSettings.Fog)] = "useFog",
    };

    static readonly (PropertyInfo Property, ThemeKey Key)[] Map = Build();

    public static ThemeSettings ToSettings(IReadOnlyDictionary<string, object> values)
    {
        var settings = new ThemeSettings();
        foreach ((PropertyInfo property, ThemeKey key) in Map)
            if (values.TryGetValue(key.Name, out object? value))
                property.SetValue(settings, ToApi(key, value));
        return settings;
    }

    public static Dictionary<string, object> FromSettings(ThemeSettings settings)
    {
        var values = new Dictionary<string, object>();
        foreach ((PropertyInfo property, ThemeKey key) in Map)
            if (property.GetValue(settings) is { } value && ThemeValues.Parse(key, FromApi(value)) is { } parsed)
                values[key.Name] = parsed;
        return values;
    }

    static object ToApi(ThemeKey key, object value) => value switch
    {
        float[] rgba => new ThemeColor(rgba[0], rgba[1], rgba[2], rgba[3]),
        int index when key.Name == Renamed[nameof(ThemeSettings.Palette)] => (ThemePalette)index,
        _ => value,
    };

    static object FromApi(object value) => value switch
    {
        ThemeColor color => new[] { color.R, color.G, color.B, color.A },
        ThemePalette palette => (int)palette,
        _ => value,
    };

    static (PropertyInfo, ThemeKey)[] Build()
    {
        var map = typeof(ThemeSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanWrite && property.Name != "EqualityContract")
            .Select(property =>
            {
                string name = Renamed.GetValueOrDefault(property.Name) ?? char.ToLowerInvariant(property.Name[0]) + property.Name[1..];
                return (property, ThemeKeys.Find(name) ?? throw new InvalidOperationException($"ThemeSettings.{property.Name} has no client setting {name}"));
            })
            .ToArray();
        if (map.Length != ThemeKeys.All.Count) throw new InvalidOperationException("ThemeSettings does not cover every client setting");
        return map;
    }
}
