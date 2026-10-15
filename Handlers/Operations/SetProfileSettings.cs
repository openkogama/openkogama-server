using OpenKogama.Kogama;
using OpenKogama.Photon;

namespace OpenKogama.Handlers.Operations;

// 3.x clients save ui settings on the server, nothing reads them back here
public sealed class SetProfileSettings : IOperationHandler
{
    public byte Code => (byte)OperationCode.SetProfileSettings;

    public void Handle(PhotonPeer peer, OperationRequest request) => peer.Send(new OperationResponse(request));
}
