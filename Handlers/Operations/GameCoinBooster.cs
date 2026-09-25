using OpenKogama.Kogama;
using OpenKogama.Photon;

namespace OpenKogama.Handlers.Operations;

public sealed class GameCoinBooster : IOperationHandler
{
    public byte Code => (byte)OperationCode.GameCoinBooster;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        bool enabled = Convert.ToBoolean(request[(byte)ParameterKey.GameCoinBoosterEnabled]);

        peer.Send(new EventData((byte)EventCode.GameBoost)
        {
            Parameters =
            {
                [(byte)ParameterKey.GameCoinBoosterLeft] = 0,
                [(byte)ParameterKey.GameCoinBoosterEnabled] = enabled,
            },
        });
    }
}
