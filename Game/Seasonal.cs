using OpenKogama.Plugins;

namespace OpenKogama.Game;

public static class Seasonal
{
    static readonly Lazy<PluginStorage> Storage = new(() => new PluginStorage(".", "seasonal"));
    static readonly object Sync = new();

    public static bool Enabled { get; set; } = true;

    public static bool Halloween(DateTime now) => (now.Month == 10 && now.Day >= 24) || (now.Month == 11 && now.Day <= 2);

    public static bool Seen(string name) => Storage.Value.Get(name) is not null;

    public static void MarkSeen(string name) => Storage.Value.Set(name, DateTime.UtcNow.ToString("O"));

    public static bool TryMarkSeen(string name)
    {
        lock (Sync)
        {
            if (Seen(name)) return false;
            MarkSeen(name);
            return true;
        }
    }
}
