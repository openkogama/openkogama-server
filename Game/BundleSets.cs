using System.Text.Json;

namespace OpenKogama.Game;

public static class BundleSets
{
    sealed record Range(string From, string Set);
    sealed record Config(Dictionary<string, string> Sets, List<Range> Ranges, Dictionary<string, string> Builds, Dictionary<string, string>? Engines, Dictionary<string, string>? Renamed);

    static readonly Config Loaded = JsonSerializer.Deserialize<Config>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "streaming", "bundles.json")),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    public static IReadOnlyDictionary<string, string> Roots => Loaded.Sets;

    public static string EngineOf(string set) => Loaded.Engines?.GetValueOrDefault(set) ?? "5";

    public static string FileOf(string file) => Loaded.Renamed?.GetValueOrDefault(file) ?? file;

    public static string? For(string build, string? unity = null)
    {
        if (Loaded.Builds.TryGetValue(build, out string? exact)) return exact;
        if (!Version.TryParse(build, out Version? version)) return null;
        string? set = Loaded.Ranges.LastOrDefault(range => version >= Version.Parse(range.From))?.Set;
        if (ClientEngines.Engine(unity) is not string engine || set is null || EngineOf(set) == engine) return set;
        List<string> matching = [.. Loaded.Sets.Keys.Where(name => EngineOf(name) == engine).OrderBy(Number)];
        if (matching.Count == 0) return set;
        return Loaded.Ranges.LastOrDefault(range => version >= Version.Parse(range.From) && matching.Contains(range.Set))?.Set ?? matching[^1];
    }

    static int Number(string set) => int.TryParse(set.TrimStart('v'), out int number) ? number : 0;

    public const string WebGL = "-webgl";

    public static List<string> Installed(string client)
    {
        string build = client.Split('@')[0];
        string? unity = client.Contains('@') ? client[(client.IndexOf('@') + 1)..] : null;
        if (Kogama.Protocols.ProtocolTable.Resolve(build) is string table && Kogama.Protocols.ProtocolTable.IsLegacy(table)) return ["2015"];
        string? engine = ClientEngines.Engine(unity);
        return [.. new[] { For(build, unity) ?? "2015" }
            .Concat(Loaded.Ranges.Where(range => range.From.StartsWith(build + ".") && (engine is null || EngineOf(range.Set) == engine)).Select(range => range.Set))
            .Distinct()];
    }

    public static string WebGLRoot(string root) => root.Replace("/kogama_assets_u5/", "/kogama_assets_u5_webgl/");

    public const string Legacy = "2015";

    public static string Url(string build, bool webgl = false, string? unity = null) =>
        For(build, unity) is string set ? $"http://127.0.0.1:8080/bundles-{set}{(webgl ? WebGL : "")}/"
            : webgl ? $"http://127.0.0.1:8080/bundles-{Legacy}{WebGL}/" : "http://127.0.0.1:8080/bundles/";
}
