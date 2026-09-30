using System.Globalization;
using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.World;

namespace OpenKogama.Game;

public static class Accessories
{
    const string Blueprint = "BlueprintData";
    const string Worn = "3";
    const string ModernWorn = "4";
    const int ParticlesCategory = 2;
    const int BackCategory = 3;
    const int ModernHead = 1;
    const int ModernTorso = 2;
    const int ModernBack = 3;
    const int LegacyHead = 2;
    const int LegacyTorso = 3;
    const int LegacyBack = 8;

    public static int ToLegacySlot(int modern) => modern switch
    {
        ModernHead => LegacyHead,
        ModernBack => LegacyBack,
        _ => LegacyTorso,
    };

    static int ToModernSlot(int legacy) => legacy switch
    {
        LegacyHead => ModernHead,
        LegacyBack => ModernBack,
        _ => ModernTorso,
    };

    public static int ModernSlotOf(StreamingAsset asset) => asset.Category switch
    {
        ParticlesCategory => ModernTorso,
        BackCategory => ModernBack,
        _ => ModernHead,
    };

    public static int SlotOf(List<(string Key, PackedType Type, object Value)> entry) => Int(entry, "2");

    public static (string Key, PackedType Type, object Value) Entry(StreamingAsset asset, int slot, float offset, float scale) =>
        (asset.Id.ToString(), PackedType.Hashtable, new List<(string Key, PackedType Type, object Value)>
        {
            ("1", PackedType.Int32, asset.Id),
            ("2", PackedType.Int32, slot),
            ("3", PackedType.Single, offset),
            ("4", PackedType.String, asset.Path),
            ("5", PackedType.Int64, DateTime.Now.Ticks),
            ("6", PackedType.Int32, 0),
            ("7", PackedType.Single, scale),
        });

    public static void Set(List<(string Key, PackedType Type, object Value)> entry, string key, PackedType type, object value)
    {
        entry.RemoveAll(pair => pair.Key == key);
        entry.Add((key, type, value));
    }

    public static void Change(Session session, int bodyId, Action<List<(string Key, PackedType Type, object Value)>> edit)
    {
        Dictionary<string, object> before = [];
        Dictionary<string, object> after = [];
        session.World.Modify(bodyId, body =>
        {
            if (WornList(body.Data) is not { } worn) return;
            before = worn.ToDictionary(pair => pair.Key, pair => PackedData.ToPhoton(pair.Value));
            edit(worn);
            after = worn.ToDictionary(pair => pair.Key, pair => PackedData.ToPhoton(pair.Value));
        });

        var removed = before.Keys.Where(key => !after.TryGetValue(key, out object? now) || Fingerprint(now) != Fingerprint(before[key])).ToList();
        var added = after.Where(pair => !before.TryGetValue(pair.Key, out object? old) || Fingerprint(old) != Fingerprint(pair.Value)).ToList();
        if (removed.Count == 0 && added.Count == 0) return;

        EventData remove = Removal(bodyId, removed);
        EventData add = Addition(bodyId, added);
        WorldObject? changed = session.World.Find(bodyId);

        foreach (Player player in session.Players)
        {
            if (player.ModernAccessories)
            {
                if (changed is not null)
                    player.Peer.Send(new EventData((byte)EventCode.UpdateWorldObjectData)
                    {
                        Parameters =
                        {
                            [(byte)ParameterKey.WorldObjectID] = bodyId,
                            [(byte)ParameterKey.WorldObjectData] = PackedData.ToPhoton(For(player, changed).Data),
                        },
                    });
                continue;
            }

            StreamingAssetCatalog catalog = player.Streaming;
            if (removed.Any(key => Known(catalog, key)))
                player.Peer.Send(removed.All(key => Known(catalog, key)) ? remove : Removal(bodyId, removed.Where(key => Known(catalog, key))));
            if (added.Any(pair => Known(catalog, pair.Key)))
                player.Peer.Send(added.All(pair => Known(catalog, pair.Key)) ? add : Addition(bodyId, added.Where(pair => Known(catalog, pair.Key))));
        }
    }

    public static WorldObject For(Player viewer, WorldObject obj)
    {
        if (obj.Data.Find(pair => pair.Key == Blueprint).Value is not List<(string Key, PackedType Type, object Value)> blueprint
            || blueprint.Find(pair => pair.Key == "ClientSideType").Value is not { } kind
            || Convert.ToInt32(kind) != (int)BlueprintType.Body)
            return obj;

        StreamingAssetCatalog catalog = viewer.Streaming;
        var worn = blueprint.Find(pair => pair.Key == Worn).Value as List<(string Key, PackedType Type, object Value)> ?? [];
        var known = worn.Where(pair => pair.Value is List<(string Key, PackedType Type, object Value)> entry && catalog.Has(Int(entry, "1"))).ToList();
        if (viewer.ModernAccessories)
            return WithBlueprint(obj, [.. blueprint.Where(pair => pair.Key != Worn && pair.Key != ModernWorn), (ModernWorn, PackedType.Hashtable, Modern(known))]);
        return known.Count == worn.Count ? obj : WithBlueprint(obj, [.. blueprint.Where(pair => pair.Key != Worn), (Worn, PackedType.Hashtable, known)]);
    }

    static bool Known(StreamingAssetCatalog catalog, string key) => int.TryParse(key, out int id) && catalog.Has(id);

    static EventData Removal(int bodyId, IEnumerable<string> keys) => new((byte)EventCode.RemoveWorldObjectDataPartial)
    {
        Parameters =
        {
            [(byte)ParameterKey.WorldObjectID] = bodyId,
            [(byte)ParameterKey.WorldObjectDataToRemove] = Wrap(keys.ToDictionary(key => (object)key, _ => (object?)"")),
        },
    };

    static EventData Addition(int bodyId, IEnumerable<KeyValuePair<string, object>> entries) => new((byte)EventCode.UpdateWorldObjectDataPartial)
    {
        Parameters =
        {
            [(byte)ParameterKey.WorldObjectID] = bodyId,
            [(byte)ParameterKey.WorldObjectData] = Wrap(entries.ToDictionary(pair => (object)pair.Key, pair => (object?)pair.Value)),
        },
    };

    static List<(string Key, PackedType Type, object Value)> Modern(List<(string Key, PackedType Type, object Value)> worn)
    {
        var converted = new List<(string Key, PackedType Type, object Value)>();
        foreach ((_, _, object value) in worn)
        {
            if (value is not List<(string Key, PackedType Type, object Value)> entry) continue;
            int slot = ToModernSlot(SlotOf(entry));
            converted.RemoveAll(pair => pair.Key == slot.ToString());
            converted.Add((slot.ToString(), PackedType.Hashtable, new List<(string Key, PackedType Type, object Value)>
            {
                ("1", PackedType.Int32, Int(entry, "1")),
                ("2", PackedType.Int32, slot),
                ("3", PackedType.Single, Float(entry, "3", 0f)),
                ("4", PackedType.String, entry.Find(pair => pair.Key == "4").Value as string ?? ""),
                ("5", PackedType.Single, Float(entry, "7", 1f)),
            }));
        }

        return converted;
    }

    static WorldObject WithBlueprint(WorldObject obj, List<(string Key, PackedType Type, object Value)> blueprint) =>
        new()
        {
            Id = obj.Id,
            ParentId = obj.ParentId,
            ItemId = obj.ItemId,
            Type = obj.Type,
            Position = obj.Position,
            Rotation = obj.Rotation,
            Scale = obj.Scale,
            Data = [.. obj.Data.Select(pair => pair.Key == Blueprint ? (Blueprint, PackedType.Hashtable, (object)blueprint) : pair)],
            Owner = obj.Owner,
            PreviewOwner = obj.PreviewOwner,
            Runtime = obj.Runtime,
            Transient = obj.Transient,
        };

    static List<(string Key, PackedType Type, object Value)>? WornList(List<(string Key, PackedType Type, object Value)> data)
    {
        if (data.Find(pair => pair.Key == Blueprint).Value is not List<(string Key, PackedType Type, object Value)> blueprint) return null;
        if (blueprint.Find(pair => pair.Key == Worn).Value is List<(string Key, PackedType Type, object Value)> worn) return worn;
        worn = [];
        blueprint.Add((Worn, PackedType.Hashtable, worn));
        return worn;
    }

    static Dictionary<object, object?> Wrap(Dictionary<object, object?> entries) =>
        new() { [Blueprint] = new Dictionary<object, object?> { [Worn] = entries } };

    static int Int(List<(string Key, PackedType Type, object Value)> entry, string key) =>
        entry.Find(pair => pair.Key == key).Value is { } value ? Convert.ToInt32(value) : 0;

    static float Float(List<(string Key, PackedType Type, object Value)> entry, string key, float fallback) =>
        entry.Find(pair => pair.Key == key).Value is { } value ? Convert.ToSingle(value) : fallback;

    static string Fingerprint(object? value) => value switch
    {
        Dictionary<object, object?> table => string.Join(";", table.OrderBy(pair => pair.Key.ToString()).Select(pair => $"{pair.Key}={Fingerprint(pair.Value)}")),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "",
    };
}
