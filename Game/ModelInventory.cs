using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.World;

namespace OpenKogama.Game;

public static class ModelInventory
{
    const int ModelCategory = 1;
    const string ModelName = "CubeModel";

    public static Item Add(Player player, WorldObject obj, Prototype prototype, int objectId)
    {
        var itemPrototype = new Prototype(1, prototype.Scale, prototype.AuthorId, prototype.Cubes.Clone());
        var itemObject = new WorldObject
        {
            Id = 1,
            ParentId = -1,
            Type = WorldObjectType.CubeModel,
            Scale = [.. obj.Scale],
            Data = [("protoTypeID", PackedType.Int32, itemPrototype.Id)],
        };
        byte[] data = WorldSerializer.Write(new Snapshot([itemPrototype], [itemObject], [], []), runtime: false);
        return Give(player, ModelName, ModelCategory, data, prototype.AuthorId, objectId);
    }

    public static Item Give(Player player, string name, int category, byte[] data, int author, int objectId)
    {
        Item item = Inventories.Add(player.ProfileId, name, category, data, author);
        int slot = Inventories.WithSlots(player.ProfileId).First(entry => entry.Item.Id == item.Id).Slot;

        player.Peer.Send(new EventData((byte)EventCode.AddItemToInventory)
        {
            Parameters =
            {
                [(byte)ParameterKey.ActorNr] = player.Actor,
                [(byte)ParameterKey.ItemID] = item.Id,
                [(byte)ParameterKey.ItemCategoryID] = category,
                [(byte)ParameterKey.ItemTypeID] = item.Id,
                [(byte)ParameterKey.ItemName] = item.Name,
                [(byte)ParameterKey.ItemData] = data,
                [(byte)ParameterKey.SlotIndex] = slot,
                [(byte)ParameterKey.WorldObjectID] = objectId,
                [(byte)ParameterKey.IsResellable] = item.Author == player.ProfileId,
                [(byte)ParameterKey.AuthorProfileID] = item.Author,
                [(byte)ParameterKey.OriginalItemID] = item.Id,
                [(byte)ParameterKey.ItemPrice] = 0,
            },
        });

        return item;
    }
}
