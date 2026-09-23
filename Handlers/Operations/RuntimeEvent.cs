using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;

namespace OpenKogama.Handlers.Operations;

public sealed class RuntimeEvent(Session session) : IOperationHandler
{
    public byte Code => (byte)OperationCode.RuntimeEvent;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        var data = (byte[])request[(byte)ParameterKey.Data]!;
        session.World.AddRuntimeEvent(data);

        var evt = new EventData((byte)EventCode.RuntimeEvent)
        {
            Parameters = { [(byte)ParameterKey.Data] = data },
        };
        foreach (Player player in session.Players)
            player.Peer.Send(evt);
    }
}
