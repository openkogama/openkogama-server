using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.World;

namespace OpenKogama.Handlers.Operations;

public sealed class Ungroup(Session session) : IOperationHandler
{
    public byte Code => (byte)OperationCode.Ungroup;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        int groupId = Convert.ToInt32(request[(byte)ParameterKey.WorldObjectID]);
        if (session.World.Find(groupId) is not { ParentId: not -1 } group)
        {
            peer.Send(new OperationResponse(request) { ReturnCode = -1 });
            return;
        }

        foreach (WorldObject child in session.World.Subtree(groupId).Where(obj => obj.ParentId == groupId).ToList())
            session.World.Reparent(child.Id, group.ParentId);
        session.World.Remove(groupId);
        session.World.MarkChanged();

        peer.Send(new OperationResponse(request));

        var evt = new EventData((byte)EventCode.Ungroup)
        {
            Parameters = { [(byte)ParameterKey.WorldObjectID] = groupId },
        };
        foreach (Player player in session.Players)
            if (player.Peer != peer) player.Peer.Send(evt);
    }
}
