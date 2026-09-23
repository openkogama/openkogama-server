using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.World;

namespace OpenKogama.Handlers.Operations;

public sealed class RequestWoUniquePrototype(Session session) : IOperationHandler
{
    public byte Code => (byte)OperationCode.RequestWoUniquePrototype;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        int objectId = Convert.ToInt32(request[(byte)ParameterKey.WorldObjectID]);

        Prototype? unique = session.World.MakeUniquePrototype(objectId);
        if (unique is null)
        {
            Console.WriteLine($"peer {peer.Id}: no prototype to make unique for {objectId}");
            return;
        }

        session.World.MarkChanged();

        var evt = new EventData((byte)EventCode.WoUniquePrototype)
        {
            Parameters =
            {
                [(byte)ParameterKey.WorldObjectID] = objectId,
                [(byte)ParameterKey.WorldInventoryID] = unique.Id,
            },
        };
        foreach (Player player in session.Players)
            player.Peer.Send(evt);

        peer.Send(new OperationResponse(request));
    }
}
