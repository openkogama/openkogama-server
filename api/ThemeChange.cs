namespace OpenKogama.Api;

public sealed class ThemeChange(IPlayer player, IWorld world, ITheme? theme, ThemeSettings? settings)
{
    public IPlayer Player { get; } = player;
    public IWorld World { get; } = world;
    public ITheme? Theme { get; } = theme;
    public ThemeSettings? Settings { get; } = settings;
    public bool Cancel { get; set; }
}
