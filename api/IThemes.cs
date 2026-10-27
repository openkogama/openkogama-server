namespace OpenKogama.Api;

public interface IThemes
{
    IReadOnlyList<ITheme> All { get; }
    ITheme this[GameTheme theme] { get; }
    ITheme? Find(string name);
    bool MenuEnabled { get; set; }
    ITheme Add(CustomTheme theme);
    bool Remove(ITheme theme);
    void Replace(GameTheme theme, ThemeTexture texture, byte[] image);
    bool Restore(GameTheme theme, ThemeTexture texture);
}
