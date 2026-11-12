namespace OpenKogama.Web;

public static class Throttle
{
    const int MaxTracked = 10_000;
    static readonly Dictionary<string, Queue<long>> Seen = [];

    public static bool Allow(string key, int limit, TimeSpan window)
    {
        long now = Environment.TickCount64;
        lock (Seen)
        {
            if (Seen.Count >= MaxTracked)
                foreach (string stale in Seen.Where(pair => pair.Value.Count == 0 || now - pair.Value.Last() > window.TotalMilliseconds).Select(pair => pair.Key).ToList())
                    Seen.Remove(stale);
            if (Seen.Count >= MaxTracked) Seen.Clear();
            if (!Seen.TryGetValue(key, out Queue<long>? times)) Seen[key] = times = new Queue<long>();
            while (times.Count > 0 && now - times.Peek() > window.TotalMilliseconds) times.Dequeue();
            if (times.Count >= limit) return false;
            times.Enqueue(now);
            return true;
        }
    }
}
