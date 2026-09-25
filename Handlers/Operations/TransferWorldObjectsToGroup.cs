using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;

namespace OpenKogama.Handlers.Operations;

public sealed class TransferWorldObjectsToGroup(Session session) : IOperationHandler
{
    public byte Code => (byte)OperationCode.TransferWorldObjectsToGroup;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        int groupId = Convert.ToInt32(request[(byte)ParameterKey.WorldObjectID]);
        int[] ids = (int[])request[(byte)ParameterKey.WorldObjectIDs]!;

        bool moved = ids.All(id => session.World.Reparent(id, groupId));
        session.World.MarkChanged();

        peer.Send(new OperationResponse(request)
        {
            ReturnCode = (short)(moved ? 0 : -1),
            Parameters = { [(byte)ParameterKey.WorldObjectID] = groupId },
        });

        var evt = new EventData((byte)EventCode.TransferWorldObjectsToGroup)
        {
            Parameters =
            {
                [(byte)ParameterKey.WorldObjectID] = groupId,
                [(byte)ParameterKey.WorldObjectIDs] = ids,
            },
        };
        foreach (Player player in session.Players)
            player.Peer.Send(evt);
    }
}
