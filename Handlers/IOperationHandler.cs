using OpenKogama.Photon;

namespace OpenKogama.Handlers;

public interface IOperationHandler
{
    byte Code { get; }
    void Handle(PhotonPeer peer, OperationRequest request);
}
