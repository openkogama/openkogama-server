using System.Text.Json;
using OpenKogama.Kogama.Protocols;

namespace OpenKogama.World;

public static class LegacyWorld
{
    public static byte[] Convert(byte[] data, ProtocolTable client, bool runtime = true)
    {
        Snapshot snapshot = WorldSerializer.Read(data, runtime);
        var known = client.WorldObjectType.Values.ToHashSet();

        var removed = snapshot.Objects.Where(obj => !known.Contains((int)obj.Type)).Select(obj => obj.Id).ToHashSet();
        bool grew = removed.Count > 0;
        while (grew)
        {
            grew = false;
            foreach (WorldObject obj in snapshot.Objects)
                if (removed.Contains(obj.ParentId) && removed.Add(obj.Id))
                    grew = true;
        }

        var objects = snapshot.Objects.Where(obj => !removed.Contains(obj.Id)).ToList();
        foreach (WorldObject obj in objects)
        {
            if (client.Objects.TryGetValue(obj.Type.ToString(), out ProtocolTable.ObjectDefaults? defaults))
                Apply(obj, defaults);
            if (client.StateTypes.Count > 0)
            {
                obj.Data = Retype(obj.Data, client.StateTypes);
                obj.Runtime = Retype(obj.Runtime, client.StateTypes);
            }
        }

        return WorldSerializer.Write(runtime: runtime, snapshot: snapshot with
        {
            Objects = objects,
            Links = [.. snapshot.Links.Where(link => !removed.Contains(link.From) && !removed.Contains(link.To))],
            ObjectLinks = [.. snapshot.ObjectLinks.Where(link => !removed.Contains(link.From) && !removed.Contains(link.To))],
        });
    }

    static List<(string Key, PackedType Type, object Value)> Retype(List<(string Key, PackedType Type, object Value)> values, Dictionary<string, ProtocolTable.StateType> types) =>
        [.. values.Select(pair =>
            types.TryGetValue(pair.Key, out ProtocolTable.StateType? type) && Packed(type.Client) is PackedType packed
                ? (pair.Key, packed, StateTypes.Cast(pair.Value, type.Client))
                : pair.Value is List<(string Key, PackedType Type, object Value)> nested
                    ? (pair.Key, pair.Type, Retype(nested, types))
                    : pair)];

    static PackedType? Packed(string type) => type switch
    {
        "byte" => PackedType.Byte,
        "int" => PackedType.Int32,
        "long" => PackedType.Int64,
        "float" => PackedType.Single,
        _ => null,
    };

    static void Apply(WorldObject obj, ProtocolTable.ObjectDefaults defaults)
    {
        foreach (var (key, value) in defaults.Data ?? [])
            if (obj.Data.All(pair => pair.Key != key) && Pack(obj, value) is { } packed)
                obj.Data.Add((key, packed.Type, packed.Value));

        foreach (var (key, value) in defaults.Runtime ?? [])
            if (obj.Runtime.All(pair => pair.Key != key) && Pack(obj, value) is { } packed)
                obj.Runtime.Add((key, packed.Type, packed.Value));
    }

    static (PackedType Type, object Value)? Pack(WorldObject obj, JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String when value.GetString() == "$owner" => obj.Owner is int owner ? (PackedType.Int32, owner) : null,
        JsonValueKind.String => (PackedType.String, value.GetString()!),
        JsonValueKind.True => (PackedType.Bool, true),
        JsonValueKind.False => (PackedType.Bool, false),
        JsonValueKind.Number when value.TryGetInt32(out int number) => (PackedType.Int32, number),
        JsonValueKind.Number => (PackedType.Single, value.GetSingle()),
        JsonValueKind.Object when value.TryGetProperty("byte", out JsonElement number) => (PackedType.Byte, number.GetByte()),
        _ => null,
    };
}
