using System.Text.Json;

namespace OpenKogama.Game;

public sealed record SkyKey(float Time, float[] Top, float[] Bottom);

public sealed record LightKey(float Time, float[] Tint, float[] Light, float Intensity);

public sealed record StarsKey(float Time, float[] Tint);

public sealed record ThemeCycleData
{
    public const float OrbitStart = -20f;
    public const float OrbitEnd = 200f;
    public const float GameSunrise = 40f;
    public const float GameSunset = 85f;

    public float? Sunrise { get; init; }
    public float? Sunset { get; init; }
    public float? Moonrise { get; init; }
    public float? Moonset { get; init; }
    public IReadOnlyList<SkyKey> Sky { get; init; } = [];
    public IReadOnlyList<LightKey> Sun { get; init; } = [];
    public IReadOnlyList<LightKey> Moon { get; init; } = [];
    public IReadOnlyList<StarsKey> Stars { get; init; } = [];

    public static (float Start, float End) Orbit(float rise, float set)
    {
        float visible = Wrap(set - rise);
        float whole = visible * (OrbitEnd - OrbitStart) / 180f;
        if (visible <= 0f || whole >= 100f)
            throw new ArgumentException($"a sun or moon can be above the horizon for more than 0 and less than {100f * 180f / (OrbitEnd - OrbitStart):0.#} of the 100 of a cycle");
        float start = Wrap(rise + whole * OrbitStart / (OrbitEnd - OrbitStart));
        return (start, Wrap(start + whole));
    }

    public static (float Rise, float Set) Horizon(float start, float end)
    {
        float whole = Wrap(end - start);
        return (Wrap(start - whole * OrbitStart / (OrbitEnd - OrbitStart)), Wrap(start + whole * (180f - OrbitStart) / (OrbitEnd - OrbitStart)));
    }

    public static float Wrap(float time) => (time % 100f + 100f) % 100f;

    public object ToJson() => new
    {
        sunrise = Sunrise,
        sunset = Sunset,
        moonrise = Moonrise,
        moonset = Moonset,
        sky = Sky.Select(key => new { time = key.Time, top = key.Top, bottom = key.Bottom }),
        sun = Sun.Select(LightJson),
        moon = Moon.Select(LightJson),
        stars = Stars.Select(key => new { time = key.Time, tint = key.Tint }),
    };

    public static ThemeCycleData FromJson(JsonElement json) => new()
    {
        Sunrise = Number(json, "sunrise"),
        Sunset = Number(json, "sunset"),
        Moonrise = Number(json, "moonrise"),
        Moonset = Number(json, "moonset"),
        Sky = [.. Items(json, "sky").Select(key => new SkyKey(key.GetProperty("time").GetSingle(), Color(key, "top"), Color(key, "bottom")))],
        Sun = [.. Items(json, "sun").Select(LightOf)],
        Moon = [.. Items(json, "moon").Select(LightOf)],
        Stars = [.. Items(json, "stars").Select(key => new StarsKey(key.GetProperty("time").GetSingle(), Color(key, "tint")))],
    };

    static object LightJson(LightKey key) => new { time = key.Time, tint = key.Tint, light = key.Light, intensity = key.Intensity };

    static LightKey LightOf(JsonElement key) =>
        new(key.GetProperty("time").GetSingle(), Color(key, "tint"), Color(key, "light"), key.GetProperty("intensity").GetSingle());

    static float? Number(JsonElement json, string name) =>
        json.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number ? value.GetSingle() : null;

    static IEnumerable<JsonElement> Items(JsonElement json, string name) =>
        json.TryGetProperty(name, out JsonElement list) && list.ValueKind == JsonValueKind.Array ? list.EnumerateArray() : [];

    static float[] Color(JsonElement key, string name)
    {
        float[] channels = [.. key.GetProperty(name).EnumerateArray().Select(channel => channel.GetSingle())];
        return [.. channels.Take(4), .. Enumerable.Repeat(1f, Math.Max(0, 4 - channels.Length))];
    }
}
