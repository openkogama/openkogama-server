using System.Globalization;
using System.Text.Json;
using Values = System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, double>>;

namespace OpenKogama.Game;

public static class ProfileSettings
{
    enum Platform { Standalone, WebGL }

    enum Key { MouseSensitivity, TargetFrameRate, TextureQuality, TextureFilter, AnistropicFiltering, AntiAliasing, LightQuality, ResetToDefaultValues }

    const int MaxKeyLength = 64;

    static readonly Dictionary<Platform, double[]> Defaults = new()
    {
        [Platform.Standalone] = [50.5, 4, 1, 2, 5, 2, 2],
        [Platform.WebGL] = [50.5, 4, 1, 1, 4, 1, 1],
    };

    static readonly Dictionary<int, (string? Key, Values? Values)> Launched = [];

    public static void Remember(int profile, string? key, string? json)
    {
        Values? values = json is not null && Parse(json) is Values saved ? Complete(saved) : null;
        lock (Launched) Launched[profile] = (CleanKey(key), values);
    }

    public static Values? Of(int profile)
    {
        lock (Launched) return Launched.GetValueOrDefault(profile).Values is Values values ? Complete(values) : null;
    }

    public static string? ForKey(string? key)
    {
        if (CleanKey(key) is not string wanted) return null;
        lock (Launched)
            return Launched.Values.FirstOrDefault(entry => entry.Key == wanted).Values is Values values ? JsonSerializer.Serialize(values) : null;
    }

    public static void Set(int profile, int platform, int key, object? value)
    {
        if (!Enum.IsDefined((Platform)platform)) return;
        string name = ((Platform)platform).ToString();
        lock (Launched)
        {
            (string? launch, Values? values) = Launched.GetValueOrDefault(profile);
            values ??= Complete(null);
            if (key == (int)Key.ResetToDefaultValues)
                values[name] = Complete(null)[name];
            else if (Enum.IsDefined((Key)key) && Number(value) is double number)
                values[name][((Key)key).ToString()] = number;
            else
                return;
            Launched[profile] = (launch, values);
        }
    }

    static Values Complete(Values? saved) =>
        Defaults.ToDictionary(platform => platform.Key.ToString(), platform =>
        {
            Dictionary<string, double>? values = saved?.GetValueOrDefault(platform.Key.ToString());
            return platform.Value
                .Select((fallback, key) => (Name: ((Key)key).ToString(), Fallback: fallback))
                .ToDictionary(pair => pair.Name, pair => values is not null && values.TryGetValue(pair.Name, out double value) && double.IsFinite(value) ? value : pair.Fallback);
        });

    static Values? Parse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<Values>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string? CleanKey(string? key) => key is { Length: > 0 and <= MaxKeyLength } && key.All(char.IsAsciiLetterOrDigit) ? key : null;

    static double? Number(object? value)
    {
        try
        {
            double number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            return value is not null && double.IsFinite(number) ? number : null;
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
        {
            return null;
        }
    }
}
