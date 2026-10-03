using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenKogama.Kogama.Protocols;

public sealed class ProtocolTable
{
    static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };
    static readonly Dictionary<string, ProtocolTable> Cache = [];

    public string Version { get; init; } = "";
    public Dictionary<string, int> OperationCodes { get; init; } = [];
    public Dictionary<string, int> EventCodes { get; init; } = [];
    public Dictionary<string, int> ParameterKeys { get; init; } = [];
    public Dictionary<string, int> DBQuery { get; init; } = [];
    public Dictionary<string, int> DBQueryKeys { get; init; } = [];
    public Dictionary<string, int> WorldObjectType { get; init; } = [];
    public Dictionary<string, int> AvatarItemType { get; init; } = [];
    public List<string>? CreatableObjects { get; init; }
    public Dictionary<string, Dictionary<string, JsonElement>> Responses { get; private set; } = [];
    public Dictionary<string, Dictionary<string, JsonElement>> Events { get; private set; } = [];
    public Dictionary<string, Dictionary<string, JsonElement>> Overrides { get; private set; } = [];
    public List<NestedRule> Nested { get; private set; } = [];

    public List<ValueRule> Values { get; private set; } = [];
    public List<WorldRule> Worlds { get; private set; } = [];
    public Dictionary<string, ObjectDefaults> Objects { get; private set; } = [];
    public List<string> NestedWorlds { get; private set; } = [];
    public Dictionary<string, StateType> StateTypes { get; private set; } = [];
    public ContentRule Content { get; private set; } = new(null, null, null, null);

    public sealed record StateType(string Client, string Server);
    public sealed record ContentRule(string? Streaming, int? Materials, Dictionary<int, string>? MaterialPaths, bool? AsciiStrings);

    public sealed record NestedRule(string Parameter, string Path, string Keys, Dictionary<string, JsonElement>? Defaults);
    public sealed record ValueRule(string Parameter, string? Enum, Dictionary<string, int>? Map);
    public sealed record WorldRule(string Event, string Parameter);
    public sealed record ObjectDefaults(Dictionary<string, JsonElement>? Data, Dictionary<string, JsonElement>? Runtime);

    sealed class Legacy
    {
        public Dictionary<string, Dictionary<string, JsonElement>> Responses { get; init; } = [];
        public Dictionary<string, Dictionary<string, JsonElement>> Events { get; init; } = [];
        public Dictionary<string, Dictionary<string, JsonElement>> Overrides { get; init; } = [];
        public List<NestedRule> Nested { get; init; } = [];
        public List<ValueRule> Values { get; init; } = [];
        public List<WorldRule> Worlds { get; init; } = [];
        public Dictionary<string, ObjectDefaults> Objects { get; init; } = [];
        public List<string> NestedWorlds { get; init; } = [];
        public Dictionary<string, StateType> StateTypes { get; init; } = [];
        public ContentRule? Content { get; init; }
    }

    public Dictionary<string, int> Enum(string name) => name switch
    {
        "OperationCodes" => OperationCodes,
        "EventCodes" => EventCodes,
        "ParameterKeys" => ParameterKeys,
        "DBQuery" => DBQuery,
        "DBQueryKeys" => DBQueryKeys,
        "WorldObjectType" => WorldObjectType,
        "AvatarItemType" => AvatarItemType,
        _ => throw new ArgumentException($"unknown enum {name}"),
    };

    public static ProtocolTable For(string version)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(version, out ProtocolTable? table)) return table;

            string path = Path.Combine(AppContext.BaseDirectory, "data", "protocols", version + ".json");
            table = JsonSerializer.Deserialize<ProtocolTable>(File.ReadAllText(path), Options) ?? new();

            if (LegacyRules(version)?.Deserialize<Legacy>(Options) is { } legacy)
            {
                table.Responses = legacy.Responses;
                table.Events = legacy.Events;
                table.Overrides = legacy.Overrides;
                table.Nested = legacy.Nested;
                table.Values = legacy.Values;
                table.Worlds = legacy.Worlds;
                table.Objects = legacy.Objects;
                table.NestedWorlds = legacy.NestedWorlds;
                table.StateTypes = legacy.StateTypes;
                table.Content = legacy.Content ?? table.Content;
            }
            return Cache[version] = table;
        }
    }

    public static bool IsLegacy(string version) => LegacyRules(version) is not null;

    public static IEnumerable<ProtocolTable> Native() =>
        Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "data", "protocols"), "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .OfType<string>()
            .Where(version => System.Version.TryParse(version, out _) && !IsLegacy(version))
            .Select(For)
            .Where(table => table.CreatableObjects is not null);

    static JsonObject? LegacyRules(string version)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "data", "protocols", version + ".legacy.json");
        if (!File.Exists(path) || JsonNode.Parse(File.ReadAllText(path)) is not JsonObject rules) return null;
        if (rules["extends"]?.GetValue<string>() is not string parent) return rules;

        JsonObject merged = LegacyRules(parent) ?? [];
        rules.Remove("extends");
        Merge(merged, rules);
        return merged;
    }

    static void Merge(JsonObject target, JsonObject source)
    {
        foreach ((string name, JsonNode? value) in source)
        {
            if (target[name] is JsonObject existing && value is JsonObject nested)
                Merge(existing, nested);
            else
                target[name] = value?.DeepClone();
        }
    }

    public static string? Resolve(string version) => Candidates(version).FirstOrDefault();

    public static IEnumerable<string> Candidates(string version)
    {
        if (version.Length == 0 || !version.All(c => char.IsDigit(c) || c == '.')) yield break;
        if (File.Exists(Path.Combine(AppContext.BaseDirectory, "data", "protocols", version + ".json"))) yield return version;

        Dictionary<string, string> known = Known();
        if (known.TryGetValue(version, out string? table))
        {
            if (table != version) yield return table;
        }
        else if (Nearest(version, known) is string nearest)
        {
            yield return nearest;
        }
    }

    static Dictionary<string, string> Known()
    {
        string folder = Path.Combine(AppContext.BaseDirectory, "data", "protocols");
        Dictionary<string, string> known = Directory.EnumerateFiles(folder, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .OfType<string>()
            .Where(name => System.Version.TryParse(name, out _))
            .ToDictionary(name => name, name => name);
        string aliases = Path.Combine(folder, "aliases.json");
        if (File.Exists(aliases) && JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(aliases)) is { } map)
            foreach ((string alias, string target) in map)
                known.TryAdd(alias, target);
        return known;
    }

    static string? Nearest(string version, Dictionary<string, string> known)
    {
        if (!System.Version.TryParse(version, out System.Version? wanted)) return null;
        List<(System.Version Version, string Table)> line = [.. known
            .Select(entry => (Parsed: System.Version.TryParse(entry.Key, out System.Version? parsed) ? parsed : null, entry.Value))
            .Where(entry => entry.Parsed is not null && entry.Parsed.Major == wanted.Major && entry.Parsed.Minor == wanted.Minor)
            .Select(entry => (entry.Parsed!, entry.Value))
            .OrderBy(entry => entry.Item1)];
        if (line.Count == 0 || wanted > line[^1].Version) return null;

        List<(System.Version Version, string Table)> build = [.. line.Where(entry => entry.Version.Build == wanted.Build)];
        List<(System.Version Version, string Table)> pool = build.Count > 0 ? build : line;
        return pool.MinBy(entry => Distance(entry.Version, wanted)).Table;
    }

    static long Distance(System.Version a, System.Version b) =>
        Math.Abs(((long)Math.Max(a.Build, 0) * 100000 + Math.Max(a.Revision, 0)) - ((long)Math.Max(b.Build, 0) * 100000 + Math.Max(b.Revision, 0)));
}
