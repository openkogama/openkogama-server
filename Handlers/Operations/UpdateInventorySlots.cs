using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;

namespace OpenKogama.Handlers.Operations;

public sealed class UpdateInventorySlots(Session session) : IOperationHandler
{
    public byte Code => (byte)OperationCode.UpdateInventorySlots;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        Player? player = session.For(peer);
        if (player is null) return;

        if (PhotonValues.Normalize(request[(byte)ParameterKey.ItemIDToSlotIndexTable]) is Dictionary<object, object?> table)
        {
            var slots = table.ToDictionary(entry => Convert.ToInt32(entry.Key), entry => Convert.ToInt32(entry.Value));
            Inventories.SetSlots(player.ProfileId, slots);
        }

        peer.Send(new OperationResponse(request));
    }
}
