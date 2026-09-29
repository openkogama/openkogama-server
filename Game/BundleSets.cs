using System.Text.Json;

namespace OpenKogama.Game;

public static class BundleSets
{
    sealed record Range(string From, string Set);
    sealed record Config(Dictionary<string, string> Sets, List<Range> Ranges, Dictionary<string, string> Builds, Dictionary<string, string>? Engines);

    static readonly Config Loaded = JsonSerializer.Deserialize<Config>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "streaming", "bundles.json")),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    public static IReadOnlyDictionary<string, string> Roots => Loaded.Sets;

    public static string EngineOf(string set) => Loaded.Engines?.GetValueOrDefault(set) ?? "5";

    public static string? For(string build)
    {
        if (Loaded.Builds.TryGetValue(build, out string? exact)) return exact;
        if (!Version.TryParse(build, out Version? version)) return null;
        return Loaded.Ranges.LastOrDefault(range => version >= Version.Parse(range.From))?.Set;
    }

    public static string Url(string build) =>
        For(build) is string set ? $"http://127.0.0.1:8080/bundles-{set}/" : "http://127.0.0.1:8080/bundles/";
}
