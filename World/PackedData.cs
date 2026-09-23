namespace OpenKogama.World;

public static class PackedData
{
    public static List<(string Key, PackedType Type, object Value)> FromPhoton(Dictionary<object, object?> table) =>
        [.. table.Where(entry => entry.Value is not null).Select(entry => Pair(entry.Key, entry.Value!, null))];

    public static void Merge(List<(string Key, PackedType Type, object Value)> target, Dictionary<object, object?> changes)
    {
        foreach ((object rawKey, object? value) in changes)
        {
            if (value is null) continue;

            string key = rawKey.ToString()!;
            int index = target.FindIndex(pair => pair.Key == key);

            if (index >= 0 && value is Dictionary<object, object?> nested && target[index].Value is List<(string, PackedType, object)> existing)
            {
                Merge(existing, nested);
                continue;
            }

            var pair = Pair(key, value, index >= 0 ? target[index].Type : null);
            if (index >= 0) target[index] = pair;
            else target.Add(pair);
        }
    }

    public static void Remove(List<(string Key, PackedType Type, object Value)> target, Dictionary<object, object?> removals)
    {
        foreach ((object rawKey, object? value) in removals)
        {
            string key = rawKey.ToString()!;
            int index = target.FindIndex(pair => pair.Key == key);
            if (index < 0) continue;

            if (value is Dictionary<object, object?> nested && target[index].Value is List<(string, PackedType, object)> existing)
                Remove(existing, nested);
            else
                target.RemoveAt(index);
        }
    }

    public static object ToPhoton(object value) => value switch
    {
        List<(string Key, PackedType Type, object Value)> pairs =>
            pairs.ToDictionary(pair => (object)pair.Key, pair => (object?)ToPhoton(pair.Value)),
        _ => value,
    };

    static (string, PackedType, object) Pair(object key, object value, PackedType? previous) => value switch
    {
        int number => (key.ToString()!, PackedType.Int32, number),
        float number => (key.ToString()!, PackedType.Single, number),
        bool flag => (key.ToString()!, PackedType.Bool, flag),
        byte number => (key.ToString()!, PackedType.Byte, number),
        long number => (key.ToString()!, PackedType.Int64, number),
        string text => (key.ToString()!, PackedType.String, text),
        int[] numbers => (key.ToString()!, previous == PackedType.Int32HashtableKeysOnly ? PackedType.Int32HashtableKeysOnly : PackedType.Int32Array, numbers),
        float[] numbers => (key.ToString()!, PackedType.SingleArray, numbers),
        bool[] flags => (key.ToString()!, PackedType.BoolArray, flags),
        long[] numbers => (key.ToString()!, PackedType.Int64Array, numbers),
        Dictionary<object, object?> table => (key.ToString()!, PackedType.Hashtable, FromPhoton(table)),
        _ => throw new NotSupportedException($"world object data: cannot store {value.GetType()}"),
    };
}
