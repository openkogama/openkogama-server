using System.Text;

namespace OpenKogama.Game;

public static class SessionToken
{
    const int MaxName = 20;

    public static string Create(int profile, string? name) =>
        Clean(name) is string clean ? $"{profile}-{Convert.ToHexString(Encoding.UTF8.GetBytes(clean))}" : profile.ToString();

    public static (int Profile, string? Name) Parse(string? token)
    {
        if (string.IsNullOrEmpty(token)) return (0, null);
        int dash = token.IndexOf('-');
        if (!int.TryParse(dash < 0 ? token : token[..dash], out int profile)) return (0, null);
        if (dash < 0) return (profile, null);
        try
        {
            return (profile, Clean(Encoding.UTF8.GetString(Convert.FromHexString(token[(dash + 1)..]))));
        }
        catch (FormatException)
        {
            return (profile, null);
        }
    }

    public static string? Clean(string? name)
    {
        if (name is null) return null;
        string clean = string.Concat(name.Where(c => !char.IsControl(c))).Trim();
        clean = string.Concat(clean.EnumerateRunes().Take(MaxName)).Trim();
        return clean.Length > 0 ? clean : null;
    }
}
