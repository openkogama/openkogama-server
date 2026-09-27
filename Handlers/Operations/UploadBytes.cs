using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;

namespace OpenKogama.Handlers.Operations;

public sealed class UploadBytes(Session session) : IOperationHandler
{
    public byte Code => (byte)OperationCode.UploadBytes;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        if (session.For(peer) is not { } player || request[(byte)ParameterKey.Data] is not byte[] chunk)
        {
            peer.Send(new OperationResponse(request) { ReturnCode = -1 });
            return;
        }

        player.Receive(Convert.ToInt32(request[(byte)ParameterKey.Id]), chunk);
        peer.Send(new OperationResponse(request));
    }
}
