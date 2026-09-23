using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.World;

namespace OpenKogama.Handlers.Operations;

public sealed class AddWorldObjectToInventory(Session session) : IOperationHandler
{
    const int ModelCategory = 1;
    const string ModelName = "CubeModel";

    public byte Code => (byte)OperationCode.AddWorldObjectToInventory;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        int objectId = Convert.ToInt32(request[(byte)ParameterKey.WorldObjectID]);
        Player? player = session.For(peer);
        WorldObject? obj = session.World.Find(objectId);
        Prototype? prototype = obj?.PrototypeId is int id ? session.World.FindPrototype(id) : null;

        if (player is null || obj?.Type != WorldObjectType.CubeModel || prototype is null || prototype.AuthorId != player.ProfileId)
        {
            Console.WriteLine($"peer {peer.Id}: cannot add {objectId} to inventory");
            return;
        }

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

        Item item = Inventories.Add(player.ProfileId, ModelName, ModelCategory, data);
        int slot = Inventories.WithSlots(player.ProfileId).First(entry => entry.Item.Id == item.Id).Slot;

        peer.Send(new EventData((byte)EventCode.AddItemToInventory)
        {
            Parameters =
            {
                [(byte)ParameterKey.ActorNr] = player.Actor,
                [(byte)ParameterKey.ItemID] = item.Id,
                [(byte)ParameterKey.ItemCategoryID] = ModelCategory,
                [(byte)ParameterKey.ItemTypeID] = item.Id,
                [(byte)ParameterKey.ItemName] = item.Name,
                [(byte)ParameterKey.ItemData] = data,
                [(byte)ParameterKey.SlotIndex] = slot,
                [(byte)ParameterKey.WorldObjectID] = objectId,
                [(byte)ParameterKey.IsResellable] = true,
                [(byte)ParameterKey.AuthorProfileID] = player.ProfileId,
                [(byte)ParameterKey.OriginalItemID] = item.Id,
                [(byte)ParameterKey.ItemPrice] = 0,
            },
        });

        peer.Send(new OperationResponse(request)
        {
            Parameters =
            {
                [(byte)ParameterKey.WorldObjectID] = objectId,
                [(byte)ParameterKey.ItemID] = item.Id,
                [(byte)ParameterKey.ItemPrice] = 0,
            },
        });

        Console.WriteLine($"peer {peer.Id}: added model {objectId} to inventory of profile {player.ProfileId} as item {item.Id}");
    }
}
