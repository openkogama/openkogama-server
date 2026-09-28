using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;

namespace OpenKogama.Handlers.Operations;

public sealed class SetFirstTimeEvent(Session session) : IOperationHandler
{
    public byte Code => (byte)OperationCode.SetFirstTimeEvent;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        int id = Convert.ToInt32(request[(byte)ParameterKey.Id]);
        if (session.For(peer) is Player player) ProfileMeta.Set(player.ProfileId, id, true);
        peer.Send(new OperationResponse(request)
        {
            Parameters =
            {
                [(byte)ParameterKey.Id] = id,
                [(byte)ParameterKey.XPRewardType] = Experience.NoReason,
            },
        });
    }
}

public sealed class OverrideFirstTimeEvent(Session session) : IOperationHandler
{
    public byte Code => (byte)OperationCode.OverrideFirstTimeEvent;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        if (session.For(peer) is Player player)
            ProfileMeta.Set(player.ProfileId, Convert.ToInt32(request[(byte)ParameterKey.Id]), request[(byte)ParameterKey.Bool] is true);
        peer.Send(new OperationResponse(request));
    }
}

public sealed class ResetFirstTimeEvents(Session session) : IOperationHandler
{
    public byte Code => (byte)OperationCode.ResetFirstTimeEvents;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        if (session.For(peer) is Player player)
            ProfileMeta.Reset(player.ProfileId, request[(byte)ParameterKey.Bool] is true);
        peer.Send(new OperationResponse(request));
    }
}
