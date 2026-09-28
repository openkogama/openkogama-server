using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.World;

namespace OpenKogama.Handlers.Operations;

public sealed class AddWorldObjectToInventoryDev(Session session) : IOperationHandler
{
    public byte Code => (byte)OperationCode.AddWorldObjectToInventoryDev;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        int objectId = Convert.ToInt32(request[(byte)ParameterKey.WorldObjectID]);
        if (session.For(peer) is null || session.World.Find(objectId) is null)
        {
            Respond(peer, request, -1, objectId, 0);
            return;
        }

        Snapshot copy = WorldSerializer.Read(WorldSerializer.Write(session.World.SubtreeSnapshot(objectId), runtime: false), runtime: false);
        copy.Objects.First(obj => obj.Id == objectId).ParentId = -1;

        string name = request[(byte)ParameterKey.ItemTypeName] as string is { Length: > 0 } given ? given : "Item";
        int category = Convert.ToInt32(request[(byte)ParameterKey.ItemCategoryID]);
        bool overwrite = request[(byte)ParameterKey.ItemOverwriteExisting] is true;

        byte[] data = WorldSerializer.Write(copy, runtime: false);
        Item item = Items.AddBuiltIn(name, category, data, overwrite);
        peer.Send(new OperationResponse(request) { Parameters = ModelInventory.Fields(item, category, data, -1, objectId, resellable: false) });
        Console.WriteLine($"peer {peer.Id}: added object {objectId} to built-in items as {item.Id} ({name})");
    }

    static void Respond(PhotonPeer peer, OperationRequest request, short code, int objectId, int itemId) =>
        peer.Send(new OperationResponse(request)
        {
            ReturnCode = code,
            Parameters =
            {
                [(byte)ParameterKey.WorldObjectID] = objectId,
                [(byte)ParameterKey.ItemID] = itemId,
            },
        });
}
