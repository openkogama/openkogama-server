using System.Text;

namespace OpenKogama.Game;

public static class SessionToken
{
    const int MaxName = 20;

    public static string? Clean(string? name)
    {
        if (name is null) return null;
        string clean = AntiCheat.Name(string.Concat(name.Where(c => !char.IsControl(c)))).Trim();
        clean = string.Concat(clean.EnumerateRunes().Take(MaxName)).Trim();
        return clean.Length > 0 ? clean : null;
    }
}
