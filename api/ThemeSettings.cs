namespace OpenKogama.Api;

public sealed record ThemeSettings
{
    public bool? DayNightCycle { get; init; }
    public bool? ServerTime { get; init; }
    public ThemePalette? Palette { get; init; }
    public float? StartTime { get; init; }
    public float? CycleLength { get; init; }

    public ThemeColor? TopColor { get; init; }
    public ThemeColor? BottomColor { get; init; }

    public ThemeColor? StarsTint { get; init; }
    public float? StarsExtinction { get; init; }
    public float? StarsTwinklingSpeed { get; init; }

    public ThemeColor? SunTint { get; init; }
    public float? SunSize { get; init; }
    public float? SunHeight { get; init; }
    public float? SunAxis { get; init; }
    public float? SunAltitude { get; init; }
    public float? SunLongitude { get; init; }
    public float? SunLightContrast { get; init; }
    public float? SunLightIntensity { get; init; }
    public float? SunFlareBrightness { get; init; }

    public ThemeColor? MoonTint { get; init; }
    public float? MoonSize { get; init; }
    public float? MoonHeight { get; init; }
    public float? MoonAxis { get; init; }
    public float? MoonAltitude { get; init; }
    public float? MoonLongitude { get; init; }
    public float? MoonLightContrast { get; init; }
    public float? MoonLightIntensity { get; init; }
    public float? MoonFlareBrightness { get; init; }

    public float? CloudsHeight { get; init; }
    public float? CloudsOffset { get; init; }
    public float? CloudsRotationSpeed { get; init; }

    public bool? Fog { get; init; }
    public float? FogDensity { get; init; }
    public float? Exposure { get; init; }
}
