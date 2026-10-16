using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;

namespace OpenKogama.Handlers.Operations;

public sealed class LevelChanged(Session session) : IOperationHandler
{
    public byte Code => (byte)OperationCode.LevelChanged;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        if (session.For(peer) is Player player) Experience.ShowLevel(session, player);
    }
}
