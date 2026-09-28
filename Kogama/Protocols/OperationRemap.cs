using OpenKogama.Photon;

namespace OpenKogama.Kogama.Protocols;

public sealed class OperationRemap(ProtocolTable client) : IMessageTranslator
{
    static readonly ProtocolTable ServerTable = ProtocolTable.For(ClientProtocols.ServerVersion);
    static readonly Dictionary<string, int> Server = ServerCodes<OperationCode>(ServerTable.OperationCodes);
    static readonly Dictionary<string, int> ServerEvents = ServerCodes<EventCode>(ServerTable.EventCodes);
    static readonly Dictionary<string, int> ServerKeys = ServerCodes<ParameterKey>(ServerTable.ParameterKeys);
    static readonly Dictionary<string, int> ServerQueries = ServerCodes<DBQueryType>(ServerTable.DBQuery);
    static readonly Dictionary<string, string> Synonyms = new() { ["UploadPlanetTextureData"] = "PlanetTextureData", ["Message"] = "GameMsgData" };
    static readonly byte QueryKey = (byte)ParameterKey.DBQuery;
    static readonly HashSet<byte> NestedIn = [(byte)ParameterKey.PurchaseProductData];
    static readonly HashSet<byte> NestedOut = [(byte)ParameterKey.UserList];
    static readonly byte GameState = (byte)ParameterKey.GameStateType;
    static readonly System.Version ShortStatesVersion = new(1, 42, 6);

    readonly CodeMap _in = new(client.OperationCodes, Server);
    readonly CodeMap _out = new(Server, client.OperationCodes);
    readonly CodeMap _events = new(ServerEvents, client.EventCodes);
    readonly CodeMap? _keysIn = Renumbered(Named(client.ParameterKeys), ServerKeys) ? new(Named(client.ParameterKeys), ServerKeys) : null;
    readonly CodeMap? _keysOut = Renumbered(Named(client.ParameterKeys), ServerKeys) ? new(ServerKeys, Named(client.ParameterKeys)) : null;
    readonly CodeMap? _queries = Renumbered(client.DBQuery, ServerQueries) ? new(client.DBQuery, ServerQueries) : null;
    readonly HashSet<string> _keyNames = [.. Named(client.ParameterKeys).Keys];
    readonly HashSet<EventData> _pushed = [];
    readonly bool _shortStates = System.Version.TryParse(client.Version, out System.Version? version) && version >= ShortStatesVersion;

    public string Version => client.Version;

    public bool ShortStates => _shortStates;

    public PhotonPeer? Peer { get; set; }

    public bool Pushing { get; set; }

    public static OperationRemap? For(string version)
    {
        ProtocolTable table = ProtocolTable.For(version);
        bool renumbered = Renumbered(table.OperationCodes, Server) || Renumbered(table.EventCodes, ServerEvents)
            || Renumbered(Named(table.ParameterKeys), ServerKeys) || Renumbered(table.DBQuery, ServerQueries);
        return renumbered ? new OperationRemap(table) : null;
    }

    public string ClientOperation(byte code) => _in.Name(code);

    public bool Knows(EventCode code) => _events.Map((int)code) is not null;

    public bool Knows(ParameterKey key) => _keyNames.Contains(key.ToString());

    public OperationRequest? Incoming(OperationRequest request) =>
        _in.Map(request.OperationCode) is int code
            ? new OperationRequest { OperationCode = (byte)code, Parameters = Queries(Keys(request.Parameters, _keysIn, outgoing: false)) }
            : null;

    Dictionary<byte, object?> Queries(Dictionary<byte, object?> parameters)
    {
        if (_queries is null || parameters.GetValueOrDefault(QueryKey) is not { } value) return parameters;
        int query = _queries.Map(Convert.ToInt32(value)) ?? byte.MaxValue;
        return new Dictionary<byte, object?>(parameters) { [QueryKey] = value is byte ? (byte)query : query };
    }

    public OperationResponse? Outgoing(OperationResponse response)
    {
        if (Pushing && Peer is not null && client.EventCodes.TryGetValue(_out.Name(response.OperationCode), out int pushed))
        {
            if (response.ReturnCode == 0) Push(new EventData((byte)pushed) { Parameters = Out(response.Parameters) });
            else Console.WriteLine($"peer {Peer.Id}: {_out.Name(response.OperationCode)} failed during synchronize, not pushed");
            return null;
        }

        return _out.Map(response.OperationCode) is int code
            ? new OperationResponse((byte)code) { ReturnCode = response.ReturnCode, DebugMessage = response.DebugMessage, Parameters = Out(response.Parameters) }
            : null;
    }

    public EventData? Outgoing(EventData data)
    {
        lock (_pushed)
            if (_pushed.Remove(data)) return data;

        if (_events.Map(data.Code) is not int code) return null;
        Dictionary<byte, object?> parameters = Out(data.Parameters);
        return code == data.Code && ReferenceEquals(parameters, data.Parameters) ? data : new EventData((byte)code) { Parameters = parameters };
    }

    Dictionary<byte, object?> Out(Dictionary<byte, object?> parameters) => Keys(States(parameters), _keysOut, outgoing: true);

    Dictionary<byte, object?> States(Dictionary<byte, object?> parameters)
    {
        if (!_shortStates || parameters.GetValueOrDefault(GameState) is not int state) return parameters;
        return new Dictionary<byte, object?>(parameters) { [GameState] = state switch { 0 => 0, 2 => 1, _ => 2 } };
    }

    void Push(EventData data)
    {
        lock (_pushed) _pushed.Add(data);
        Peer!.Send(data);
    }

    static Dictionary<byte, object?> Keys(Dictionary<byte, object?> parameters, CodeMap? keys, bool outgoing)
    {
        if (keys is null) return parameters;

        var mapped = new Dictionary<byte, object?>(parameters.Count);
        foreach ((byte key, object? value) in parameters)
        {
            if (keys.Map(key) is not int code) continue;
            mapped[(byte)code] = (outgoing ? NestedOut.Contains(key) : NestedIn.Contains((byte)code)) ? Inner(value, keys) : value;
        }
        return mapped;
    }

    static object? Inner(object? value, CodeMap keys) => value switch
    {
        PhotonDictionary dictionary when dictionary.KeyType == GpType.Byte || dictionary.Entries.Keys.All(key => key is byte) =>
            new PhotonDictionary { KeyType = dictionary.KeyType, ValueType = dictionary.ValueType, Entries = Entries(dictionary.Entries, keys) },
        PhotonDictionary dictionary =>
            new PhotonDictionary { KeyType = dictionary.KeyType, ValueType = dictionary.ValueType, Entries = dictionary.Entries.ToDictionary(entry => entry.Key, entry => Inner(entry.Value, keys)) },
        Dictionary<object, object?> table when table.Keys.All(key => key is byte) => Entries(table, keys),
        _ => value,
    };

    static Dictionary<object, object?> Entries(Dictionary<object, object?> entries, CodeMap keys)
    {
        var mapped = new Dictionary<object, object?>(entries.Count);
        foreach ((object key, object? value) in entries)
        {
            if (key is not byte code) mapped[key] = value;
            else if (keys.Map(code) is int target) mapped[(byte)target] = value;
        }
        return mapped;
    }

    static Dictionary<string, int> Named(Dictionary<string, int> codes) =>
        codes.ToDictionary(pair => Synonyms.GetValueOrDefault(pair.Key, pair.Key), pair => pair.Value);

    static bool Renumbered(Dictionary<string, int> client, Dictionary<string, int> server) =>
        client.Any(pair => server.TryGetValue(pair.Key, out int code) && code != pair.Value);

    static Dictionary<string, int> ServerCodes<T>(Dictionary<string, int> table) where T : struct, Enum
    {
        var codes = new Dictionary<string, int>(table);
        foreach (T value in Enum.GetValues<T>())
        {
            int code = Convert.ToInt32(value);
            if (!codes.ContainsValue(code))
                codes.TryAdd(value.ToString(), code);
        }
        return codes;
    }
}
