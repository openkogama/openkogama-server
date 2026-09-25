using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.World;

namespace OpenKogama.Handlers.Operations;

public sealed class AddWorldObjectToInventory(Session session) : IOperationHandler
{
    const short NotCreatorCanBuy = -6;
    const short Failed = -1;

    public byte Code => (byte)OperationCode.AddWorldObjectToInventory;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        int objectId = Convert.ToInt32(request[(byte)ParameterKey.WorldObjectID]);
        Player? player = session.For(peer);
        WorldObject? obj = session.World.Find(objectId);
        Prototype? prototype = obj?.PrototypeId is int id ? session.World.FindPrototype(id) : null;

        if (player is null || obj?.Type != WorldObjectType.CubeModel || prototype is null)
        {
            Console.WriteLine($"peer {peer.Id}: cannot add {objectId} to inventory");
            Respond(peer, request, Failed, objectId, 0);
            return;
        }

        if (prototype.AuthorId != player.ProfileId)
        {
            Respond(peer, request, NotCreatorCanBuy, objectId, 0);
            return;
        }

        Item item = ModelInventory.Add(player, obj, prototype, objectId);
        Respond(peer, request, 0, objectId, item.Id);
        Console.WriteLine($"peer {peer.Id}: added model {objectId} to inventory of profile {player.ProfileId} as item {item.Id}");
    }

    static void Respond(PhotonPeer peer, OperationRequest request, short code, int objectId, int itemId) =>
        peer.Send(new OperationResponse(request)
        {
            ReturnCode = code,
            Parameters =
            {
                [(byte)ParameterKey.WorldObjectID] = objectId,
                [(byte)ParameterKey.ItemID] = itemId,
                [(byte)ParameterKey.ItemPrice] = 0,
            },
        });
}
