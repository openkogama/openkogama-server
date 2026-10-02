using OpenKogama.Kogama;
using OpenKogama.Photon;

namespace OpenKogama.Handlers.Operations;

public sealed class HighScoreList(Game.Session session, OperationCode code, EventCode reply, bool top) : IOperationHandler
{
    public byte Code => (byte)code;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        if (session.For(peer) is not { } player) return;
        peer.Send(new EventData((byte)reply) { Parameters = { [(byte)ParameterKey.Data] = session.GamePasses.HighScores(player, top) } });
    }
}

public sealed class GameTierOperation(Game.Session session, OperationCode code) : IOperationHandler
{
    public byte Code => (byte)code;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        if (session.For(peer) is not { } player) return;
        int tier = request[(byte)ParameterKey.Data] is { } value ? Convert.ToInt32(value) : 0;
        switch (code)
        {
            case OperationCode.SetGamePassTierOperation: session.GamePasses.Test(player, tier); break;
            case OperationCode.SetGamePassTierToSeenOperation: session.GamePasses.Seen(player, tier); break;
            case OperationCode.ResetPlayerPlanetData: session.GamePasses.Reset(player); break;
        }
    }
}

public sealed class UpdateGold(Game.Session session) : IOperationHandler
{
    public byte Code => (byte)OperationCode.UpdateGold;

    public void Handle(PhotonPeer peer, OperationRequest request) =>
        peer.Send(new OperationResponse(request) { Parameters = { [(byte)ParameterKey.GoldAmount] = Gold(session, peer) } });

    public static int Gold(Game.Session session, PhotonPeer peer) =>
        session.For(peer) is { } player ? Storage.Stores.Profiles.Gold(player.ProfileId) : 0;
}

public sealed class ClaimWelcomeReward(Game.Session session) : IOperationHandler
{
    public byte Code => (byte)OperationCode.ClaimGamePointWelcomeReward;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        if (session.For(peer) is not { } player) return;
        session.GamePasses.ClaimWelcome(player, request[(byte)ParameterKey.Bool] is true);
    }
}
