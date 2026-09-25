using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.World;

namespace OpenKogama.Handlers.Operations;

public sealed class LockHierarchy(Session session) : IOperationHandler
{
    public byte Code => (byte)OperationCode.LockHierarchy;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        int id = Convert.ToInt32(request[(byte)ParameterKey.WorldObjectID]);
        bool locked = (bool)request[(byte)ParameterKey.Lock]!;
        int actor = session.For(peer)?.Actor ?? 0;
        int? owner = locked ? actor : null;

        List<WorldObject> targets = [.. session.World.Subtree(id).Where(obj => obj.Id == id || obj.ParentId == id)];
        foreach (WorldObject target in targets)
            session.World.Modify(target.Id, obj => obj.Owner = owner);
        session.World.MarkChanged();

        peer.Send(new OperationResponse(request)
        {
            ReturnCode = (short)(targets.Count > 0 ? 0 : -1),
            Parameters =
            {
                [(byte)ParameterKey.WorldObjectID] = id,
                [(byte)ParameterKey.Lock] = locked,
            },
        });

        var evt = new EventData((byte)EventCode.LockHierarchy)
        {
            Parameters =
            {
                [(byte)ParameterKey.WorldObjectID] = id,
                [(byte)ParameterKey.OwnerActorNr] = owner ?? 0,
            },
        };
        foreach (Player player in session.Players)
            if (player.Peer != peer) player.Peer.Send(evt);
    }
}
