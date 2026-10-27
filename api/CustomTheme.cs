namespace OpenKogama.Api;

public sealed record CustomTheme
{
    public required string Name { get; init; }
    public GameTheme Base { get; init; } = GameTheme.Normal;
    public string? Title { get; init; }
    public string? Description { get; init; }
    public ThemeSettings Defaults { get; init; } = new();
    public IReadOnlyDictionary<ThemeTexture, byte[]> Textures { get; init; } = new Dictionary<ThemeTexture, byte[]>();
    public ThemeCycle? Cycle { get; init; }
}
