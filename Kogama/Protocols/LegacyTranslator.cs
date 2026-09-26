using System.Text.Json;
using OpenKogama.Photon;
using OpenKogama.World;

namespace OpenKogama.Kogama.Protocols;

public sealed class LegacyTranslator(ProtocolTable client, ProtocolTable server) : IMessageTranslator
{
    readonly CodeMap _operationsIn = new(client.OperationCodes, server.OperationCodes);
    readonly CodeMap _operationsOut = new(server.OperationCodes, client.OperationCodes);
    readonly CodeMap _eventsOut = new(server.EventCodes, client.EventCodes);
    readonly CodeMap _keysIn = new(client.ParameterKeys, server.ParameterKeys);
    readonly CodeMap _keysOut = new(server.ParameterKeys, client.ParameterKeys);
    readonly HashSet<string> _reported = [];
    readonly HashSet<object> _raw = new(ReferenceEqualityComparer.Instance);
    readonly HashSet<byte> _nestedWorlds = client.NestedWorlds
        .Where(server.DBQueryKeys.ContainsKey)
        .Select(name => (byte)server.DBQueryKeys[name])
        .ToHashSet();
    readonly Dictionary<int, List<Nesting>> _nestedIn = client.Nested
        .Where(rule => client.ParameterKeys.ContainsKey(rule.Parameter))
        .GroupBy(rule => client.ParameterKeys[rule.Parameter])
        .ToDictionary(group => group.Key, group => group
            .Select(rule => new Nesting(rule.Path.Split('/', StringSplitOptions.RemoveEmptyEntries), new CodeMap(client.Enum(rule.Keys), server.Enum(rule.Keys)), []))
            .ToList());
    readonly Dictionary<int, List<Nesting>> _nested = client.Nested
        .Where(rule => server.ParameterKeys.ContainsKey(rule.Parameter))
        .GroupBy(rule => server.ParameterKeys[rule.Parameter])
        .ToDictionary(group => group.Key, group => group
            .Select(rule => new Nesting(rule.Path.Split('/', StringSplitOptions.RemoveEmptyEntries), new CodeMap(server.Enum(rule.Keys), client.Enum(rule.Keys)),
                (rule.Defaults ?? []).Where(entry => client.Enum(rule.Keys).ContainsKey(entry.Key))
                    .ToDictionary(entry => (byte)client.Enum(rule.Keys)[entry.Key], entry => Convert(entry.Value))))
            .ToList());

    sealed record Nesting(string[] Path, CodeMap Keys, Dictionary<byte, object?> Defaults);

    readonly Dictionary<int, CodeMap> _valuesIn = client.Values
        .Where(rule => rule.Enum is not null && client.ParameterKeys.ContainsKey(rule.Parameter))
        .ToDictionary(rule => client.ParameterKeys[rule.Parameter], rule => new CodeMap(client.Enum(rule.Enum!), server.Enum(rule.Enum!)));
    readonly Dictionary<int, CodeMap> _valuesOut = client.Values
        .Where(rule => server.ParameterKeys.ContainsKey(rule.Parameter))
        .ToDictionary(rule => server.ParameterKeys[rule.Parameter], rule => rule.Map is { } map ? new CodeMap(map) : new CodeMap(server.Enum(rule.Enum!), client.Enum(rule.Enum!)));

    readonly Dictionary<int, HashSet<byte>> _worlds = client.Worlds
        .Where(rule => server.EventCodes.ContainsKey(rule.Event) && server.ParameterKeys.ContainsKey(rule.Parameter))
        .GroupBy(rule => server.EventCodes[rule.Event])
        .ToDictionary(group => group.Key, group => group.Select(rule => (byte)server.ParameterKeys[rule.Parameter]).ToHashSet());

    readonly bool _floatRotation = !client.ParameterKeys.ContainsKey("ByteRotation") && client.ParameterKeys.ContainsKey("RotW");

    public string Version => client.Version;

    public string ClientOperation(byte code) => _operationsIn.Name(code);

    public byte Key(string name) => (byte)client.ParameterKeys[name];

    public bool HasEvent(string name) => client.EventCodes.ContainsKey(name);

    public byte Event(string name) => (byte)client.EventCodes[name];

    public void SendRaw(PhotonPeer peer, OperationResponse response)
    {
        lock (_raw) _raw.Add(response);
        peer.Send(response);
    }

    public void SendRaw(PhotonPeer peer, EventData data)
    {
        lock (_raw) _raw.Add(data);
        peer.Send(data);
    }

    public OperationRequest? Incoming(OperationRequest request)
    {
        if (_operationsIn.Map(request.OperationCode) is not int code)
        {
            Report($"op {_operationsIn.Name(request.OperationCode)} has no {server.Version} equivalent");
            return null;
        }

        return new OperationRequest { OperationCode = (byte)code, Parameters = RotationIn(Keys(Values(Nested(request.Parameters, _nestedIn, false), _valuesIn), _keysIn, "in")) };
    }

    public OperationResponse? Outgoing(OperationResponse response)
    {
        lock (_raw)
            if (_raw.Remove(response)) return response;

        if (_operationsOut.Map(response.OperationCode) is not int code)
        {
            Report($"response {_operationsOut.Name(response.OperationCode)} is not known by {client.Version}");
            return null;
        }

        return new OperationResponse
        {
            OperationCode = (byte)code,
            ReturnCode = response.ReturnCode,
            DebugMessage = response.DebugMessage,
            Parameters = Overrides(Defaults(Keys(Values(Nested(response.Parameters, _nested, true), _valuesOut), _keysOut, "out"), client.Responses, _operationsOut.Name(response.OperationCode)), _operationsOut.Name(response.OperationCode)),
        };
    }

    public EventData? Outgoing(EventData data)
    {
        lock (_raw)
            if (_raw.Remove(data)) return data;

        if (Handlers.Legacy.LegacyEvents.Rewrite(data, this) is { } rewritten)
            return rewritten;

        if (_eventsOut.Map(data.Code) is not int code)
        {
            Report($"event {_eventsOut.Name(data.Code)} is not known by {client.Version}");
            return null;
        }

        Dictionary<byte, object?> parameters = RotationOut(Worlds(data.Code, data.Parameters));
        return new EventData((byte)code) { Parameters = Defaults(Keys(Values(Nested(parameters, _nested, true), _valuesOut), _keysOut, "out"), client.Events, _eventsOut.Name(data.Code)) };
    }

    Dictionary<byte, object?> RotationIn(Dictionary<byte, object?> parameters)
    {
        if (!_floatRotation) return parameters;

        byte[] rotation = [.. new[] { "RotX", "RotY", "RotZ", "RotW" }.Select(name => (byte)server.ParameterKeys[name])];
        byte packed = (byte)server.ParameterKeys["ByteRotation"];
        if (parameters.ContainsKey(packed) || rotation.Any(key => parameters.GetValueOrDefault(key) is not float)) return parameters;

        parameters[packed] = QuaternionCompression.ToBytes(
            (float)parameters[rotation[0]]!, (float)parameters[rotation[1]]!, (float)parameters[rotation[2]]!, (float)parameters[rotation[3]]!);
        return parameters;
    }

    Dictionary<byte, object?> RotationOut(Dictionary<byte, object?> parameters)
    {
        if (!_floatRotation) return parameters;

        byte packed = (byte)server.ParameterKeys["ByteRotation"];
        byte rotX = (byte)server.ParameterKeys["RotX"];
        if (parameters.GetValueOrDefault(packed) is not byte[] { Length: 3 } bytes || parameters.ContainsKey(rotX)) return parameters;

        var (x, y, z, w) = QuaternionCompression.ToQuaternion(bytes);
        var result = new Dictionary<byte, object?>(parameters)
        {
            [rotX] = x,
            [(byte)server.ParameterKeys["RotY"]] = y,
            [(byte)server.ParameterKeys["RotZ"]] = z,
            [(byte)server.ParameterKeys["RotW"]] = w,
        };
        return result;
    }

    Dictionary<byte, object?> Worlds(byte code, Dictionary<byte, object?> parameters)
    {
        if (!_worlds.TryGetValue(code, out var keys)) return parameters;

        var result = new Dictionary<byte, object?>(parameters);
        foreach (byte key in keys)
        {
            if (result.GetValueOrDefault(key) is not byte[] world) continue;
            try
            {
                result[key] = LegacyWorld.Convert(world, client);
            }
            catch (Exception e)
            {
                Report($"world conversion failed: {e.Message}");
            }
        }
        return result;
    }

    Dictionary<byte, object?> Values(Dictionary<byte, object?> parameters, Dictionary<int, CodeMap> values)
    {
        if (values.Count == 0) return parameters;

        var result = new Dictionary<byte, object?>(parameters);
        foreach (var (key, map) in values)
        {
            if (!result.TryGetValue((byte)key, out object? value) || value is null) continue;

            int code = System.Convert.ToInt32(value);
            if (map.Map(code) is not int mapped)
            {
                Report($"value {map.Name(code)} of key {key} has no equivalent");
                continue;
            }
            result[(byte)key] = value switch
            {
                byte => (byte)mapped,
                short => (short)mapped,
                _ => (object)mapped,
            };
        }
        return result;
    }

    Dictionary<byte, object?> Nested(Dictionary<byte, object?> parameters, Dictionary<int, List<Nesting>> nested, bool outgoing)
    {
        var result = new Dictionary<byte, object?>();
        foreach (var (key, value) in parameters)
        {
            object? converted = StateTypes.Convert(outgoing ? Hashtables(value) : Hashtables(value), client.StateTypes, outgoing);
            if (nested.TryGetValue(key, out var rules))
                foreach (Nesting rule in rules)
                    converted = Rekey(outgoing ? converted : Hashtables(converted), rule, 0, outgoing);
            result[key] = converted;
        }
        return result;
    }

    static object? Hashtables(object? value) => value switch
    {
        PhotonDictionary dictionary => dictionary.Entries.ToDictionary(entry => entry.Key, entry => Hashtables(entry.Value)),
        Dictionary<object, object?> table => table.ToDictionary(entry => entry.Key, entry => Hashtables(entry.Value)),
        object?[] items when items.GetType() == typeof(object[]) => items.Select(Hashtables).ToArray(),
        _ => value,
    };

    object? Rekey(object? value, Nesting rule, int depth, bool outgoing)
    {
        if (value is not Dictionary<object, object?> table) return value;
        string[] path = rule.Path;
        CodeMap keys = rule.Keys;

        if (depth == path.Length)
        {
            var result = new Dictionary<object, object?>();
            foreach (var (key, raw) in table)
            {
                object? item = outgoing && key is byte world && _nestedWorlds.Contains(world) && raw is byte[] bytes ? ItemWorld(bytes) : raw;
                if (key is not byte code)
                    result[key] = item;
                else if (keys.Map(code) is int mapped)
                    result[(byte)mapped] = item;
                else
                    Report($"nested key {keys.Name(code)} dropped");
            }
            foreach (var (key, fallback) in rule.Defaults)
                result.TryAdd(key, fallback);
            return result;
        }

        string segment = path[depth];
        return table.ToDictionary(entry => entry.Key,
            entry => segment == "*" || segment == entry.Key.ToString() ? Rekey(entry.Value, rule, depth + 1, outgoing) : entry.Value);
    }

    byte[] ItemWorld(byte[] data)
    {
        try
        {
            return LegacyWorld.Convert(data, client, runtime: false);
        }
        catch (Exception e)
        {
            Report($"item conversion failed: {e.Message}");
            return data;
        }
    }

    Dictionary<byte, object?> Keys(Dictionary<byte, object?> parameters, CodeMap keys, string direction)
    {
        var result = new Dictionary<byte, object?>();
        foreach (var (key, value) in parameters)
        {
            if (keys.Map(key) is int mapped)
                result[(byte)mapped] = value;
            else
                Report($"{direction} key {keys.Name(key)} dropped");
        }
        return result;
    }

    Dictionary<byte, object?> Defaults(Dictionary<byte, object?> parameters, Dictionary<string, Dictionary<string, JsonElement>> defaults, string name)
    {
        if (!defaults.TryGetValue(name, out var values)) return parameters;

        foreach (var (key, value) in values)
        {
            if (!client.ParameterKeys.TryGetValue(key, out int code))
            {
                Report($"default key {key} is not a {client.Version} parameter");
                continue;
            }
            parameters.TryAdd((byte)code, Convert(value));
        }
        return parameters;
    }

    Dictionary<byte, object?> Overrides(Dictionary<byte, object?> parameters, string name)
    {
        if (!client.Overrides.TryGetValue(name, out var values)) return parameters;

        foreach (var (key, value) in values)
            if (client.ParameterKeys.TryGetValue(key, out int code))
                parameters[(byte)code] = Convert(value);
        return parameters;
    }

    static object? Convert(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object when value.TryGetProperty("byte", out JsonElement number) => number.GetByte(),
        JsonValueKind.Object when value.TryGetProperty("short", out JsonElement number) => number.GetInt16(),
        JsonValueKind.Object when value.TryGetProperty("long", out JsonElement number) => number.GetInt64(),
        JsonValueKind.Object when value.TryGetProperty("float", out JsonElement number) => number.GetSingle(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number when value.TryGetInt32(out int number) => number,
        JsonValueKind.Number => value.GetSingle(),
        JsonValueKind.Object => value.EnumerateObject().ToDictionary(property => (object)property.Name, property => Convert(property.Value)),
        _ => null,
    };

    void Report(string message)
    {
        lock (_reported)
            if (!_reported.Add(message)) return;
        Console.WriteLine($"{client.Version}: {message}");
    }
}
