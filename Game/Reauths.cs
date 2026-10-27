using System.Collections.Concurrent;
using OpenKogama.Kogama;
using OpenKogama.Photon;

namespace OpenKogama.Game;

public static class Reauths
{
    const int ClientTries = 3;
    const short ReauthCode = -12;
    const long PlanLifeMs = 30_000;
    const string SwitchesUnpatched = "1.25.";
    static readonly TimeSpan Life = TimeSpan.FromHours(12);
    static readonly ConcurrentDictionary<string, Entry> Entries = new();
    static readonly ConcurrentDictionary<string, string> Launches = new();

    public sealed record Origin(int Profile, string? Nickname, string? Client, string? Via, bool WebSocket, int World, GameMode Mode, int Used = 0, string? Key = null, bool Reloads = false,
        string? Language = null, bool Tcp = false, string? Themes = null, bool Teleports = false);

    public sealed record Plan(int? World, GameMode? Mode, IReadOnlyDictionary<string, object?> Session);

    sealed record Entry(Origin Origin, long Expires, Plan? Plan = null, long PlannedAt = 0);

    public static void Remember(string token, Origin origin)
    {
        long now = Environment.TickCount64;
        foreach (string stale in Entries.Where(pair => pair.Value.Expires < now).Select(pair => pair.Key).ToList())
            Entries.TryRemove(stale, out _);
        foreach (string gone in Launches.Where(pair => !Entries.ContainsKey(pair.Value)).Select(pair => pair.Key).ToList())
            Launches.TryRemove(gone, out _);
        Entries[token] = new Entry(origin, now + (long)Life.TotalMilliseconds);
        if (origin.Key is string key) Launches[key] = token;
    }

    public static (Origin Origin, Plan? Plan)? Resume(string? key) =>
        key is not null && Launches.TryRemove(key, out string? token) ? Take(token) : null;

    public static int Left(Player player) =>
        player.Peer.Protocol == PhotonProtocol.Protocol15 || Find(player.Token) is not { } entry ? 0
            : entry.Origin.Reloads ? int.MaxValue
            : entry.Origin.Client?.StartsWith(SwitchesUnpatched) == true ? Math.Max(0, player.ReauthTries ?? ClientTries - entry.Origin.Used)
            : 0;

    public static string? LanguageOf(string? token) => Find(token)?.Origin.Language;

    public static string? ThemesOf(string? token) => Find(token)?.Origin.Themes;
    public static bool Teleports(Player player) => Find(player.Token)?.Origin.Teleports == true;

    public static bool Start(Player player, Plan plan)
    {
        if (player.Token is not string token || Left(player) == 0 || !Schedule(token, plan)) return false;
        player.Peer.Send(new OperationResponse((byte)OperationCode.Join) { ReturnCode = ReauthCode });
        Console.WriteLine($"peer {player.Peer.Id}: {player.Username} reauths into world {plan.World?.ToString() ?? "unchanged"}");
        return true;
    }

    public static bool Schedule(string token, Plan plan)
    {
        long now = Environment.TickCount64;
        return Find(token) is { } entry && (entry.Plan is null || now - entry.PlannedAt >= PlanLifeMs)
            && Entries.TryUpdate(token, entry with { Plan = plan, PlannedAt = now }, entry);
    }

    public static (Origin Origin, Plan? Plan)? Take(string? token) =>
        token is not null && Entries.TryRemove(token, out Entry? entry) && entry.Expires >= Environment.TickCount64
            ? (entry.Origin, entry.Plan)
            : null;

    static Entry? Find(string? token) =>
        token is not null && Entries.TryGetValue(token, out Entry? entry) && entry.Expires >= Environment.TickCount64 ? entry : null;
}
