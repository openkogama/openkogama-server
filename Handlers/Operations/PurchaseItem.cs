using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.World;

namespace OpenKogama.Handlers.Operations;

public sealed class PurchaseItem(Session session) : IOperationHandler
{
    const short NotFound = -4;
    const short YouAreOwner = -3;
    const short Failed = -1;

    public byte Code => (byte)OperationCode.PurchaseItem;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        int objectId = Convert.ToInt32(request[(byte)ParameterKey.WorldObjectID]);
        Player? player = session.For(peer);
        WorldObject? obj = session.World.Find(objectId);
        Prototype? prototype = obj?.PrototypeId is int id ? session.World.FindPrototype(id) : null;

        if (player is null || obj?.Type != WorldObjectType.CubeModel || prototype is null)
        {
            peer.Send(new OperationResponse(request) { ReturnCode = ModelInventory.Failure(player, player is null ? Failed : NotFound) });
            return;
        }

        if (prototype.AuthorId == player.ProfileId)
        {
            peer.Send(new OperationResponse(request) { ReturnCode = ModelInventory.Failure(player, YouAreOwner) });
            return;
        }

        (Item item, Dictionary<byte, object?> fields) = ModelInventory.Add(player, obj, prototype, objectId);
        peer.Send(new OperationResponse(request) { Parameters = fields });
        Console.WriteLine($"peer {peer.Id}: bought model {objectId} by {prototype.AuthorId} as item {item.Id}");
    }
}
