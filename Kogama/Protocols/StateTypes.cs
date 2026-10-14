namespace OpenKogama.Kogama.Protocols;

public static class StateTypes
{
    public static object? Convert(object? value, Dictionary<string, ProtocolTable.StateType> types, bool toClient)
    {
        if (types.Count == 0) return value;
        if (value is Photon.PhotonDictionary dictionary)
        {
            if (!Touches(dictionary.Entries, types)) return dictionary;
            var entries = (Dictionary<object, object?>)Convert(dictionary.Entries, types, toClient)!;
            return new Photon.PhotonDictionary { KeyType = dictionary.KeyType, ValueType = Photon.GpType.Unknown, Entries = entries };
        }
        if (value is not Dictionary<object, object?> table) return value;

        var result = new Dictionary<object, object?>(table.Count);
        foreach (var (key, item) in table)
        {
            result[key] = key is string name && types.TryGetValue(name, out ProtocolTable.StateType? type) && item is not null
                ? Cast(item, toClient ? type.Client : type.Server)
                : Convert(item, types, toClient);
        }
        return result;
    }

    static bool Touches(Dictionary<object, object?> table, Dictionary<string, ProtocolTable.StateType> types) =>
        table.Any(pair => pair.Key is string name && types.ContainsKey(name)
            || pair.Value is Photon.PhotonDictionary nested && Touches(nested.Entries, types)
            || pair.Value is Dictionary<object, object?> inner && Touches(inner, types));

    public static object Cast(object value, string type) => type switch
    {
        "byte" => System.Convert.ToByte(value),
        "short" => System.Convert.ToInt16(value),
        "int" => System.Convert.ToInt32(value),
        "long" => System.Convert.ToInt64(value),
        "float" => System.Convert.ToSingle(value),
        _ => value,
    };
}
