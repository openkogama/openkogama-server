using OpenKogama.Kogama;
using OpenKogama.Photon;

namespace OpenKogama.Handlers.Operations;

public sealed class JoinNotification : IOperationHandler
{
    public byte Code => (byte)OperationCode.JoinNotification;

    public void Handle(PhotonPeer peer, OperationRequest request) =>
        Console.WriteLine($"peer {peer.Id}: join notification already sent with the world");
}
