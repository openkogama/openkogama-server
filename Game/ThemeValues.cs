using System.Text.Json;
using OpenKogama.World;

namespace OpenKogama.Game;

public enum ThemeValueKind { Bool, Int, Float, Color }

public sealed record ThemeKey(string Name, ThemeValueKind Kind, float Min = 0f, float Max = 0f);

public static class ThemeValues
{
    public const int Palettes = 13;
    public const string StartTime = "gameStartTime";
    public const string CycleLength = "cycleLength";
    public const string DayNightCycle = "useDayNightCycle";
    public const string ServerTime = "useServerTime";
    static readonly string[] Channels = ["Red", "Green", "Blue", "Alpha"];

    public static readonly IReadOnlyList<ThemeKey> Keys =
    [
        new(DayNightCycle, ThemeValueKind.Bool),
        new(ServerTime, ThemeValueKind.Bool),
        new("colorPresetIndex", ThemeValueKind.Int, 0, Palettes - 1),
        new(StartTime, ThemeValueKind.Float, 0f, 100f),
        new(CycleLength, ThemeValueKind.Float, 4f, 1200f),
        new("topColor", ThemeValueKind.Color),
        new("bottomColor", ThemeValueKind.Color),
        new("starsTint", ThemeValueKind.Color),
        new("starsExtinction", ThemeValueKind.Float, 0f, 10f),
        new("starsTwinklingSpeed", ThemeValueKind.Float, 0f, 24f),
        new("sunTint", ThemeValueKind.Color),
        new("sunSize", ThemeValueKind.Float, 0.1f, 3f),
        new("sunHeight", ThemeValueKind.Float, -1f, 1f),
        new("sunAxis", ThemeValueKind.Float, 0f, 360f),
        new("sunAltitude", ThemeValueKind.Float, 0f, 180f),
        new("sunLongitude", ThemeValueKind.Float, 0f, 360f),
        new("sunLightContrast", ThemeValueKind.Float, 0f, 1f),
        new("sunLightIntensity", ThemeValueKind.Float, 0f, 8f),
        new("sunFlareBrightness", ThemeValueKind.Float, 0.01f, 2f),
        new("moonTint", ThemeValueKind.Color),
        new("moonSize", ThemeValueKind.Float, 0.1f, 3f),
        new("moonHeight", ThemeValueKind.Float, -1f, 1f),
        new("moonAxis", ThemeValueKind.Float, 0f, 360f),
        new("moonAltitude", ThemeValueKind.Float, 0f, 180f),
        new("moonLongitude", ThemeValueKind.Float, 0f, 360f),
        new("moonLightContrast", ThemeValueKind.Float, 0f, 1f),
        new("moonLightIntensity", ThemeValueKind.Float, 0f, 8f),
        new("moonFlareBrightness", ThemeValueKind.Float, 0.01f, 2f),
        new("cloudsHeight", ThemeValueKind.Float, -0.75f, 0.75f),
        new("cloudsOffset", ThemeValueKind.Float, 0f, 1f),
        new("cloudsRotationSpeed", ThemeValueKind.Float, -50f, 50f),
        new("useFog", ThemeValueKind.Bool),
        new("fogDensity", ThemeValueKind.Float, 0.0005f, 0.25f),
        new("exposure", ThemeValueKind.Float, 0f, 3f),
    ];

    static readonly Dictionary<string, ThemeKey> ByName = Keys.ToDictionary(key => key.Name);

    public static ThemeKey? Find(string name) => ByName.GetValueOrDefault(name);

    public static Dictionary<string, object> Read(object? table)
    {
        var values = new Dictionary<string, object>();
        foreach ((string name, object? raw) in Entries(table))
            if (Find(name) is { } key && Parse(key, raw) is { } value)
                values[name] = value;
        return values;
    }

    public static Dictionary<string, object> FromJson(JsonElement json)
    {
        var values = new Dictionary<string, object>();
        foreach (JsonProperty property in json.EnumerateObject())
        {
            if (Find(property.Name) is not { } key) continue;
            object? raw = property.Value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Number => property.Value.GetDouble(),
                JsonValueKind.Object => property.Value.EnumerateObject().ToDictionary(channel => (object)channel.Name, channel => (object?)channel.Value.GetDouble()),
                _ => null,
            };
            if (Parse(key, raw) is { } value) values[key.Name] = value;
        }
        return values;
    }

    public static object? Parse(ThemeKey key, object? raw) => key.Kind switch
    {
        ThemeValueKind.Bool => raw is bool flag ? flag : null,
        ThemeValueKind.Int => Number(raw) is double number ? (int)Math.Clamp(Math.Round(number), key.Min, key.Max) : null,
        ThemeValueKind.Float => Number(raw) is double number && double.IsFinite(number) ? Math.Clamp((float)number, key.Min, key.Max) : null,
        _ => Color(raw),
    };

    public static Dictionary<object, object?> ToTable(IReadOnlyDictionary<string, object> values)
    {
        var table = new Dictionary<object, object?>();
        foreach (ThemeKey key in Keys)
            if (values.TryGetValue(key.Name, out object? value))
                table[key.Name] = value is float[] color
                    ? Channels.Select((channel, i) => (channel, color[i])).ToDictionary(pair => (object)pair.channel, pair => (object?)pair.Item2)
                    : value;
        return table;
    }

    public static Dictionary<string, object> ToJson(IReadOnlyDictionary<string, object> values) =>
        Keys.Where(key => values.ContainsKey(key.Name)).ToDictionary(key => key.Name, key => values[key.Name] is float[] color
            ? Channels.Select((channel, i) => (channel, color[i])).ToDictionary(pair => pair.channel, pair => pair.Item2)
            : values[key.Name]);

    public static bool Same(object? a, object? b) =>
        a is float[] left && b is float[] right ? left.SequenceEqual(right) : Equals(a, b);

    static float[]? Color(object? raw)
    {
        float[] rgba;
        if (raw is float[] { Length: 4 } given)
        {
            rgba = given;
        }
        else
        {
            var channels = Entries(raw).ToDictionary(pair => pair.Key, pair => Number(pair.Value));
            if (Channels.Any(channel => channels.GetValueOrDefault(channel) is null)) return null;
            rgba = [.. Channels.Select(channel => (float)channels[channel]!.Value)];
        }
        return rgba.All(float.IsFinite) ? [.. rgba.Select(channel => Math.Clamp(channel, 0f, 1f))] : null;
    }

    static double? Number(object? raw) => raw switch
    {
        float number => number,
        double number => number,
        int number => number,
        long number => number,
        short number => number,
        byte number => number,
        _ => null,
    };

    static IEnumerable<(string Key, object? Value)> Entries(object? table) => table switch
    {
        Dictionary<object, object?> photon => photon.Select(pair => (pair.Key.ToString() ?? "", pair.Value)),
        List<(string Key, PackedType Type, object Value)> packed => packed.Select(pair => (pair.Key, (object?)pair.Value)),
        _ => [],
    };
}
