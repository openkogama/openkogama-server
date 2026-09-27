using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;

namespace OpenKogama.Handlers.Operations;

public sealed class GetRewardList : IOperationHandler
{
    public byte Code => (byte)OperationCode.GetRewardList;

    public void Handle(PhotonPeer peer, OperationRequest request) =>
        peer.Send(new OperationResponse(request)
        {
            Parameters =
            {
                [(byte)ParameterKey.Price] = Spins.Config.SpinPrice,
                [(byte)ParameterKey.RewardTypes] = Spins.Types,
                [(byte)ParameterKey.RewardDatas] = Spins.Datas,
            },
        });
}

public sealed class GetRewardIndex(Session session) : IOperationHandler
{
    public byte Code => (byte)OperationCode.GetRewardIndex;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        if (session.For(peer) is not { } player) return;

        peer.Send(new OperationResponse(request)
        {
            Parameters = { [(byte)ParameterKey.RewardIndex] = Spins.Pick(player) },
        });
    }
}

public sealed class ClaimReward(Session session) : IOperationHandler
{
    public byte Code => (byte)OperationCode.ClaimReward;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        SpinReward? reward = session.For(peer) is { } player ? Spins.Claim(player) : null;
        peer.Send(new OperationResponse(request) { ReturnCode = (short)(reward is null ? -1 : 0) });
        if (reward is not null) Console.WriteLine($"peer {peer.Id}: spin won {reward.Xp} xp ({reward.Rarity})");
    }
}

public sealed class GetActorOffer : IOperationHandler
{
    const byte Unavailable = 0;

    public byte Code => (byte)OperationCode.GetActorOffer;

    public void Handle(PhotonPeer peer, OperationRequest request) =>
        peer.Send(new OperationResponse(request)
        {
            Parameters =
            {
                [(byte)ParameterKey.OfferType] = Unavailable,
                [(byte)ParameterKey.OfferData] = "{}",
            },
        });
}

public sealed class ClaimActorOffer : IOperationHandler
{
    public byte Code => (byte)OperationCode.ClaimActorOffer;

    public void Handle(PhotonPeer peer, OperationRequest request) =>
        peer.Send(new OperationResponse(request) { ReturnCode = -1 });
}
