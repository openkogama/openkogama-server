using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.World;

namespace OpenKogama.Handlers.Operations;

public sealed class ResetTerrain(Session session) : IOperationHandler
{
    public byte Code => (byte)OperationCode.ResetTerrain;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        int? prototypeId = session.World.FindFirst(WorldObjectType.CubeModelTerrainFineGrained)?.PrototypeId;
        if (prototypeId is not int id || session.World.FindPrototype(id) is not Prototype terrain) return;

        terrain.Cubes.Clear();
        session.World.MarkChanged();

        var evt = new EventData((byte)EventCode.ResetTerrain);
        foreach (Player player in session.Players)
            player.Peer.Send(evt);
    }
}
