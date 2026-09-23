using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.World;

namespace OpenKogama.Handlers.Operations;

public sealed class CloneWorldObjectTree(Session session) : IOperationHandler
{
    public byte Code => (byte)OperationCode.CloneWorldObjectTree;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        int objectId = Convert.ToInt32(request[(byte)ParameterKey.WorldObjectID]);
        int owner = Convert.ToInt32(request[(byte)ParameterKey.OwnerActorNr]);
        bool toRoot = Convert.ToBoolean(request[(byte)ParameterKey.CloneToRootGroup]);
        bool asPreview = Convert.ToBoolean(request[(byte)ParameterKey.SetAsPreviewItem]);

        WorldObject? original = session.World.Find(objectId);
        if (original is null || original.Type == WorldObjectType.Avatar)
        {
            Console.WriteLine($"peer {peer.Id}: cannot clone object {objectId}");
            return;
        }

        WorldObject clone = session.World.CloneTree(objectId)!;
        session.World.MarkChanged();

        var evt = new EventData((byte)EventCode.CloneWorldObjectTree)
        {
            Parameters =
            {
                [(byte)ParameterKey.WorldObjectIDs] = new[] { objectId, clone.Id },
                [(byte)ParameterKey.OwnerActorNr] = owner,
                [(byte)ParameterKey.LinkID] = -1,
                [(byte)ParameterKey.ObjectLinkID] = -1,
                [(byte)ParameterKey.CloneToRootGroup] = toRoot,
                [(byte)ParameterKey.PreviewProfileOwnerID] = asPreview ? session.For(peer)?.ProfileId ?? 0 : 0,
            },
        };
        foreach (Player player in session.Players)
            player.Peer.Send(evt);

        peer.Send(new OperationResponse(request)
        {
            Parameters = { [(byte)ParameterKey.WorldObjectID] = clone.Id },
        });

        Console.WriteLine($"peer {peer.Id}: cloned {objectId} as {clone.Id}");
    }
}
