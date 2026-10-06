using System.Collections.Concurrent;

namespace OpenKogama.Game;

public static class ClientEngines
{
    static readonly ConcurrentDictionary<int, string> Launched = new();

    public static void Remember(int profile, string? unity)
    {
        if (string.IsNullOrEmpty(unity)) Launched.TryRemove(profile, out _);
        else Launched[profile] = unity;
    }

    public static string? Of(int profile) => Launched.GetValueOrDefault(profile);

    public static string? Engine(string? unity)
    {
        if (unity is null) return null;
        string[] parts = unity.Split('.');
        if (parts.Length < 2 || !int.TryParse(parts[0], out int major)) return null;
        return major < 2017 ? "5" : $"{major}.{parts[1]}";
    }
}
