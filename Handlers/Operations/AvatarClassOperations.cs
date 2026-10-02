using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;

namespace OpenKogama.Handlers.Operations;

public sealed class CreateSpawnRole(Session session) : IOperationHandler
{
    public byte Code => (byte)OperationCode.CreateSpawnRole;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        if (session.For(peer) is not Player player || request[(byte)ParameterKey.WorldObjectID] is not { } creator) return;
        AvatarClasses.CreateRole(session, player, Convert.ToInt32(creator));
    }
}

public sealed class GetAvatarBodies(Session session) : IOperationHandler
{
    public byte Code => (byte)OperationCode.GetAvatarBodies;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        if (session.For(peer) is not Player player) return;
        peer.Send(new EventData((byte)EventCode.GetGameBatch)
        {
            Parameters =
            {
                [(byte)ParameterKey.ActorNr] = player.Actor,
                [(byte)ParameterKey.Data] = AvatarClasses.Bodies(player),
                [(byte)ParameterKey.QueryType] = (byte)QueryType.Bodies,
                [(byte)ParameterKey.QueryId] = GetNextGameBatch.NextQueryId(),
                [(byte)ParameterKey.QueryDataLeft] = false,
            },
        });
    }
}

public sealed class SetSpawnRoleBody(Session session) : IOperationHandler
{
    public byte Code => (byte)OperationCode.SetSpawnRoleBody;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        if (session.For(peer) is not Player player
            || request[(byte)ParameterKey.WorldObjectID] is not { } creator
            || request[(byte)ParameterKey.Id] is not { } avatar)
            return;
        AvatarClasses.SetBody(session, player, Convert.ToInt32(creator), Convert.ToInt32(avatar));
    }
}
