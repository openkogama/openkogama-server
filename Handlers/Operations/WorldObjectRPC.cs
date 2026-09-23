using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;

namespace OpenKogama.Handlers.Operations;

public sealed class WorldObjectRPC(Session session) : IOperationHandler
{
    public byte Code => (byte)OperationCode.WorldObjectRPCOperation;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        var evt = new EventData((byte)EventCode.WorldObjectRPCEvent)
        {
            Parameters =
            {
                [(byte)ParameterKey.ActorNr] = (int)peer.Id,
                [(byte)ParameterKey.WorldObjectID] = request[(byte)ParameterKey.WorldObjectID],
                [(byte)ParameterKey.WorldObjectRPCData] = request[(byte)ParameterKey.WorldObjectRPCData],
            },
        };
        foreach (Player player in session.Players)
            player.Peer.Send(evt);
    }
}
