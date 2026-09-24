using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;

namespace OpenKogama.Handlers.Operations;

public sealed class ReportCaptureFlag(Session session) : IOperationHandler
{
    public byte Code => (byte)OperationCode.ReportCaptureFlag;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        if (session.For(peer) is Player player)
            session.Round.CaptureFlag(player);
    }
}
