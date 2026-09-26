using OpenKogama.Kogama;
using OpenKogama.Photon;

namespace OpenKogama.Handlers.Operations;

public sealed class Leave : IOperationHandler
{
    public byte Code => (byte)OperationCode.Leave;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        peer.Send(new OperationResponse(request));
        Console.WriteLine($"peer {peer.Id}: left");
        peer.Disconnect();
    }
}
