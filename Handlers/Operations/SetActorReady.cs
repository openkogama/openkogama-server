using OpenKogama.Kogama;
using OpenKogama.Photon;

namespace OpenKogama.Handlers.Operations;

public sealed class SetActorReady : IOperationHandler
{
    public byte Code => (byte)OperationCode.SetActorReady;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        peer.Send(new OperationResponse(request));
        Console.WriteLine($"peer {peer.Id}: actor ready");
    }
}
