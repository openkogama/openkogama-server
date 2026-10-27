namespace OpenKogama.Api;

public interface ITheme
{
    int Id { get; }
    string Name { get; }
    string Title { get; }
    string Description { get; }
    GameTheme Base { get; }
    bool Custom { get; }
    ThemeSettings Defaults { get; }
    IReadOnlyList<ThemeTexture> Replaced { get; }
    ThemeCycle? Cycle { get; set; }
    int Price { get; set; }
    int Level { get; set; }
    bool InMenu { get; set; }
}
