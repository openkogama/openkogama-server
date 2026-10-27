using OpenKogama.Mods.Patches;
using OpenKogama.World;

namespace OpenKogama.Game;

public static class ThemeValues
{
    public static Dictionary<string, object> Read(object? table)
    {
        var values = new Dictionary<string, object>();
        foreach ((string name, object? raw) in Entries(table))
            if (ThemeKeys.Find(name) is { } key && Parse(key, raw) is { } value)
                values[name] = value;
        return values;
    }

    public static object? Parse(ThemeKey key, object? raw) =>
        ThemeKeys.Parse(key, raw is List<(string Key, PackedType Type, object Value)> packed ? packed.ToDictionary(pair => (object)pair.Key, pair => (object?)pair.Value) : raw);

    public static Dictionary<object, object?> ToTable(IReadOnlyDictionary<string, object> values)
    {
        var table = new Dictionary<object, object?>();
        foreach (ThemeKey key in ThemeKeys.All)
            if (values.TryGetValue(key.Name, out object? value))
                table[key.Name] = value is float[] color
                    ? ThemeKeys.Channels.Select((channel, i) => (channel, color[i])).ToDictionary(pair => (object)pair.channel, pair => (object?)pair.Item2)
                    : value;
        return table;
    }

    public static Dictionary<string, object> ToJson(IReadOnlyDictionary<string, object> values) =>
        ThemeKeys.All.Where(key => values.ContainsKey(key.Name)).ToDictionary(key => key.Name, key => values[key.Name] is float[] color
            ? ThemeKeys.Channels.Select((channel, i) => (channel, color[i])).ToDictionary(pair => pair.channel, pair => pair.Item2)
            : values[key.Name]);

    public static bool Same(object? a, object? b) =>
        a is float[] left && b is float[] right ? left.SequenceEqual(right) : Equals(a, b);

    static IEnumerable<(string Key, object? Value)> Entries(object? table) => table switch
    {
        Dictionary<object, object?> photon => photon.Select(pair => (pair.Key.ToString() ?? "", pair.Value)),
        List<(string Key, PackedType Type, object Value)> packed => packed.Select(pair => (pair.Key, (object?)pair.Value)),
        _ => [],
    };
}
