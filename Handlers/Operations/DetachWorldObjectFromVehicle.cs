using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.World;

namespace OpenKogama.Handlers.Operations;

public sealed class DetachWorldObjectFromVehicle(Session session) : IOperationHandler
{
    public byte Code => (byte)OperationCode.DetachWorldObjectFromVehicle;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        int objectId = Convert.ToInt32(request[(byte)ParameterKey.WorldObjectID]);
        int rootId = session.World.RootId;

        bool found = session.World.Modify(objectId, obj =>
        {
            obj.ParentId = rootId;
            obj.SetRuntime("seat", PackedType.Int32, -1);
        });
        if (!found) return;

        var evt = new EventData((byte)EventCode.DetachWorldObjectFromVehicle)
        {
            Parameters = { [(byte)ParameterKey.WorldObjectID] = objectId },
        };
        foreach (Player player in session.Players)
            player.Peer.Send(evt);

        peer.Send(new OperationResponse(request));
    }
}
