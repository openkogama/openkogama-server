using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.World;

namespace OpenKogama.Handlers.Operations;

public sealed class UpdatePrototype(Session session) : IOperationHandler
{
    public byte Code => (byte)OperationCode.UpdatePrototype;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        int prototypeId = Convert.ToInt32(request[(byte)ParameterKey.WorldInventoryID]);
        var changes = (byte[])request[(byte)ParameterKey.WorldInventoryData]!;

        Prototype? prototype = session.World.FindPrototype(prototypeId);
        if (prototype is null)
        {
            Console.WriteLine($"peer {peer.Id}: unknown prototype {prototypeId}");
            return;
        }

        prototype.Cubes.Apply(changes);
        session.World.MarkChanged();

        var evt = new EventData((byte)EventCode.UpdatePrototype)
        {
            Parameters = new Dictionary<byte, object?>(request.Parameters),
        };

        foreach (Player player in session.Players)
            if (player.Peer != peer)
                player.Peer.Send(evt);
    }
}
