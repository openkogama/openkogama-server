using System.Text.Json;

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
    public Dictionary<string, Dictionary<string, JsonElement>> Responses { get; private set; } = [];
    public Dictionary<string, Dictionary<string, JsonElement>> Events { get; private set; } = [];
    public Dictionary<string, Dictionary<string, JsonElement>> Overrides { get; private set; } = [];
    public List<NestedRule> Nested { get; private set; } = [];

    public List<ValueRule> Values { get; private set; } = [];
    public List<WorldRule> Worlds { get; private set; } = [];
    public Dictionary<string, ObjectDefaults> Objects { get; private set; } = [];
    public List<string> NestedWorlds { get; private set; } = [];
    public Dictionary<string, StateType> StateTypes { get; private set; } = [];

    public sealed record StateType(string Client, string Server);

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
    }

    public Dictionary<string, int> Enum(string name) => name switch
    {
        "OperationCodes" => OperationCodes,
        "EventCodes" => EventCodes,
        "ParameterKeys" => ParameterKeys,
        "DBQuery" => DBQuery,
        "DBQueryKeys" => DBQueryKeys,
        "WorldObjectType" => WorldObjectType,
        _ => throw new ArgumentException($"unknown enum {name}"),
    };

    public static ProtocolTable For(string version)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(version, out ProtocolTable? table)) return table;

            string path = Path.Combine(AppContext.BaseDirectory, "data", "protocols", version + ".json");
            table = JsonSerializer.Deserialize<ProtocolTable>(File.ReadAllText(path), Options) ?? new();

            string legacyPath = Path.Combine(AppContext.BaseDirectory, "data", "protocols", version + ".legacy.json");
            if (File.Exists(legacyPath) && JsonSerializer.Deserialize<Legacy>(File.ReadAllText(legacyPath), Options) is { } legacy)
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
            }
            return Cache[version] = table;
        }
    }
}
