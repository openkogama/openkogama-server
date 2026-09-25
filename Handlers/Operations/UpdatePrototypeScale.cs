using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;

namespace OpenKogama.Handlers.Operations;

public sealed class UpdatePrototypeScale(Session session) : IOperationHandler
{
    public byte Code => (byte)OperationCode.UpdatePrototypeScale;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        int prototypeId = Convert.ToInt32(request[(byte)ParameterKey.WorldInventoryID]);
        float scale = Convert.ToSingle(request[(byte)ParameterKey.Scale]);

        if (!float.IsFinite(scale) || scale <= 0f || !session.World.SetPrototypeScale(prototypeId, scale))
        {
            peer.Send(new OperationResponse(request) { ReturnCode = -1 });
            return;
        }

        session.World.MarkChanged();
        peer.Send(new OperationResponse(request));

        var evt = new EventData((byte)EventCode.UpdatePrototypeScale)
        {
            Parameters =
            {
                [(byte)ParameterKey.WorldInventoryID] = prototypeId,
                [(byte)ParameterKey.Scale] = scale,
            },
        };
        foreach (Player player in session.Players)
            if (player.Peer != peer) player.Peer.Send(evt);
    }
}
