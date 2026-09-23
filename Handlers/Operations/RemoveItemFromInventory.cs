using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;

namespace OpenKogama.Handlers.Operations;

public sealed class RemoveItemFromInventory(Session session) : IOperationHandler
{
    public byte Code => (byte)OperationCode.RemoveItemFromInventory;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        int itemId = Convert.ToInt32(request[(byte)ParameterKey.ItemID]);
        Player? player = session.For(peer);

        if (player is null || !Inventories.Remove(player.ProfileId, itemId))
        {
            Console.WriteLine($"peer {peer.Id}: item {itemId} is not removable");
            return;
        }

        peer.Send(new EventData((byte)EventCode.RemoveItemFromInventory)
        {
            Parameters = { [(byte)ParameterKey.ItemID] = itemId },
        });
        peer.Send(new OperationResponse(request));
    }
}
