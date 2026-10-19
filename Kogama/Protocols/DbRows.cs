using OpenKogama.Photon;

namespace OpenKogama.Kogama.Protocols;

static class DbRows
{
    public static object? Remap(object? value, CodeMap rows) => value switch
    {
        PhotonDictionary list when list.Entries.Count > 0 && list.Entries.Values.All(entry => entry is PhotonDictionary) =>
            new PhotonDictionary { KeyType = list.KeyType, ValueType = list.ValueType, Entries = list.Entries.ToDictionary(entry => entry.Key, entry => Remap(entry.Value, rows)) },
        PhotonDictionary row when row.Entries.Keys.All(key => key is byte) =>
            new PhotonDictionary { KeyType = row.KeyType, ValueType = row.ValueType, Entries = Entries(row.Entries, rows) },
        _ => value,
    };

    public static Dictionary<object, object?> Entries(Dictionary<object, object?> entries, CodeMap keys)
    {
        var mapped = new Dictionary<object, object?>(entries.Count);
        foreach ((object key, object? value) in entries)
        {
            if (key is not byte code) mapped[key] = value;
            else if (keys.Map(code) is int target) mapped[(byte)target] = value;
        }
        return mapped;
    }
}
