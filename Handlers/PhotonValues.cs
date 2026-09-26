using OpenKogama.Photon;

namespace OpenKogama.Handlers;

public static class PhotonValues
{
    public static object? Normalize(object? value) => value switch
    {
        PhotonDictionary dictionary => dictionary.Entries.ToDictionary(entry => entry.Key, entry => Normalize(entry.Value)),
        Dictionary<object, object?> table => table.ToDictionary(entry => entry.Key, entry => Normalize(entry.Value)),
        _ => value,
    };

    public static Dictionary<object, object?> Table(object? value) =>
        Normalize(value) as Dictionary<object, object?> ?? [];
}
