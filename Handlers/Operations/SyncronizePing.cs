using OpenKogama.Kogama;
using OpenKogama.Photon;

namespace OpenKogama.Handlers.Operations;

public sealed class SyncronizePing : IOperationHandler
{
    public byte Code => (byte)OperationCode.SyncronizePing;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
    }
}
