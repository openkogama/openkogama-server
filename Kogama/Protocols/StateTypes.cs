namespace OpenKogama.Kogama.Protocols;

public static class StateTypes
{
    public static object? Convert(object? value, Dictionary<string, ProtocolTable.StateType> types, bool toClient)
    {
        if (types.Count == 0 || value is not Dictionary<object, object?> table) return value;

        var result = new Dictionary<object, object?>(table.Count);
        foreach (var (key, item) in table)
        {
            result[key] = key is string name && types.TryGetValue(name, out ProtocolTable.StateType? type) && item is not null
                ? Cast(item, toClient ? type.Client : type.Server)
                : Convert(item, types, toClient);
        }
        return result;
    }

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
