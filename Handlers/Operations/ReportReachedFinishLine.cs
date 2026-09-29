using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;

namespace OpenKogama.Handlers.Operations;

public sealed class ReportReachedFinishLine(Session session, OperationCode code = OperationCode.ReportReachedFinishLine) : IOperationHandler
{
    public byte Code => (byte)code;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        if (session.For(peer) is Player player)
            session.Round.CaptureFlag(player);
    }
}
