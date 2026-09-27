using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;

namespace OpenKogama.Handlers.Operations;

public sealed class GameCoinBooster(Session session) : IOperationHandler
{
    public byte Code => (byte)OperationCode.GameCoinBooster;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        if (session.For(peer) is not { } player) return;

        (int left, bool enabled) = CoinBoost.Switch(player, Convert.ToBoolean(request[(byte)ParameterKey.GameCoinBoosterEnabled]));

        peer.Send(new EventData((byte)EventCode.GameBoost)
        {
            Parameters =
            {
                [(byte)ParameterKey.GameCoinBoosterLeft] = left,
                [(byte)ParameterKey.GameCoinBoosterEnabled] = enabled,
            },
        });
    }
}
