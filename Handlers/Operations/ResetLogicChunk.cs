using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.World;

namespace OpenKogama.Handlers.Operations;

public sealed class ResetLogicChunk(Session session) : IOperationHandler
{
    public byte Code => (byte)OperationCode.ResetLogicChunk;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        int objectId = Convert.ToInt32(request[(byte)ParameterKey.WorldObjectID]);

        List<int> chunk = session.World.LogicChunk(objectId);
        foreach (int id in chunk)
            session.World.Modify(id, RuntimeDefaults.Reset);
        session.World.MarkChanged();
        session.Logic.Reset(chunk);

        var evt = new EventData((byte)EventCode.ResetLogicChunk)
        {
            Parameters = { [(byte)ParameterKey.WorldObjectID] = objectId },
        };
        foreach (Player player in session.Players)
            player.Peer.Send(evt);
    }
}
