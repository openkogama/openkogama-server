using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.World;

namespace OpenKogama.Game;

public static class ModelInventory
{
    public const int ModelCategory = 1;
    public const int PremiumModelCategory = 5;

    public static int ShopCategory(int category) => category == ModelCategory ? PremiumModelCategory : category;
    public const string ModelName = "CubeModel";

    public static (Item Item, Dictionary<byte, object?> Fields) Add(Player player, WorldObject obj, Prototype prototype, int objectId) =>
        Give(player, ModelName, ModelCategory, Pack(prototype, obj.Scale), prototype.AuthorId, objectId);

    public static short Failure(Player? player, short code) =>
        player?.Peer.Translator is Kogama.Protocols.OperationRemap remap && !remap.Knows(EventCode.AddItemToInventory) ? (short)-1 : code;

    public static Dictionary<byte, object?> Fields(Item item, int category, byte[] data, int slot, int objectId, bool resellable) => new()
    {
        [(byte)ParameterKey.ItemID] = item.Id,
        [(byte)ParameterKey.ItemCategoryID] = category,
        [(byte)ParameterKey.ItemTypeID] = item.Id,
        [(byte)ParameterKey.ItemName] = item.Name,
        [(byte)ParameterKey.ItemData] = data,
        [(byte)ParameterKey.SlotIndex] = slot,
        [(byte)ParameterKey.WorldObjectID] = objectId,
        [(byte)ParameterKey.IsResellable] = resellable,
        [(byte)ParameterKey.AuthorProfileID] = item.Author,
        [(byte)ParameterKey.OriginalItemID] = item.Id,
        [(byte)ParameterKey.ItemPrice] = 0,
    };

    public static byte[] Pack(Prototype prototype, float[] scale)
    {
        var itemPrototype = new Prototype(1, prototype.Scale, prototype.AuthorId, prototype.Cubes.Clone());
        var itemObject = new WorldObject
        {
            Id = 1,
            ParentId = -1,
            Type = WorldObjectType.CubeModel,
            Scale = [.. scale],
            Data = [("protoTypeID", PackedType.Int32, itemPrototype.Id)],
        };
        return WorldSerializer.Write(new Snapshot([itemPrototype], [itemObject], [], []), runtime: false);
    }

    public static Prototype? SinglePrototype(Item item)
    {
        if (item.Category != ModelCategory) return null;
        try
        {
            Snapshot snapshot = WorldSerializer.Read(item.Bytes, runtime: false);
            return snapshot.Prototypes.Count == 1 ? snapshot.Prototypes[0] : null;
        }
        catch (Exception error) when (error is FormatException or EndOfStreamException or ArgumentException or InvalidDataException)
        {
            return null;
        }
    }

    public static (Item Item, Dictionary<byte, object?> Fields) Give(Player player, string name, int category, byte[] data, int author, int objectId)
    {
        Item item = Inventories.Add(player.ProfileId, name, category, data, author);
        int slot = Inventories.WithSlots(player.ProfileId, player.ClientVersion).First(entry => entry.Item.Id == item.Id).Slot;
        Dictionary<byte, object?> fields = Fields(item, category, data, slot, objectId, item.Author == player.ProfileId);

        player.Peer.Send(new EventData((byte)EventCode.AddItemToInventory)
        {
            Parameters = new Dictionary<byte, object?>(fields) { [(byte)ParameterKey.ActorNr] = player.Actor },
        });

        return (item, fields);
    }
}
