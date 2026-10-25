namespace OpenKogama.Game;

public static class TickErrors
{
    const long RepeatMs = 60_000;
    const int MaxRemembered = 256;

    static readonly object Sync = new();
    static readonly Dictionary<string, long> Logged = [];

    public static void Guard(string where, Action step)
    {
        try
        {
            step();
        }
        catch (Exception error)
        {
            Log(where, error);
        }
    }

    public static void Log(string where, Exception error)
    {
        string key = $"{where} {error.GetType().Name} {error.Message}";
        long now = Environment.TickCount64;
        lock (Sync)
        {
            if (Logged.TryGetValue(key, out long at) && now - at < RepeatMs) return;
            if (Logged.Count >= MaxRemembered) Logged.Clear();
            Logged[key] = now;
        }
        Console.WriteLine($"tick {where}: {error}");
    }
}
