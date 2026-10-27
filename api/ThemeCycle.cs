namespace OpenKogama.Api;

public sealed record ThemeCycle
{
    public float? Sunrise { get; init; }
    public float? Sunset { get; init; }
    public float? Moonrise { get; init; }
    public float? Moonset { get; init; }
    public IReadOnlyList<ThemeSkyKey> Sky { get; init; } = [];
    public IReadOnlyList<ThemeLightKey> Sun { get; init; } = [];
    public IReadOnlyList<ThemeLightKey> Moon { get; init; } = [];
    public IReadOnlyList<ThemeStarsKey> Stars { get; init; } = [];
}

public readonly record struct ThemeSkyKey(float Time, ThemeColor Top, ThemeColor Bottom);

public readonly record struct ThemeLightKey(float Time, ThemeColor Tint, ThemeColor Light, float Intensity);

public readonly record struct ThemeStarsKey(float Time, ThemeColor Tint);
