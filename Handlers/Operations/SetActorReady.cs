using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;

namespace OpenKogama.Handlers.Operations;

public sealed class SetActorReady(Session session) : IOperationHandler
{
    public byte Code => (byte)OperationCode.SetActorReady;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        peer.Send(new OperationResponse(request));
        if (session.For(peer) is Player player)
        {
            session.Logic.Resync(player);
            Spins.Join(session, player);
        }
        Console.WriteLine($"peer {peer.Id}: actor ready");
    }
}
