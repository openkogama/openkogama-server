namespace OpenKogama.Api;

public interface IWorldTheme
{
    ITheme? Current { get; }
    ThemeSettings? Settings { get; }
    int? ObjectId { get; }
    float Time { get; set; }

    bool Set(GameTheme theme) => Set(theme, new ThemeSettings());
    bool Set(GameTheme theme, ThemeSettings settings);
    bool Set(ITheme theme) => Set(theme, new ThemeSettings());
    bool Set(ITheme theme, ThemeSettings settings);
    bool Change(ThemeSettings settings);
    bool Remove();
}
