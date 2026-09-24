using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;

namespace OpenKogama.Handlers.Operations;

public sealed class GetActiveAvatar(Session session) : IOperationHandler
{
    public byte Code => (byte)OperationCode.GetActiveAvatar;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        peer.Send(new OperationResponse(request)
        {
            Parameters = { [(byte)ParameterKey.WorldObjectID] = session.Bodies.FirstOrDefault(-1) },
        });
    }
}
