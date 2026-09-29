using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.World;

namespace OpenKogama.Handlers.Operations;

public sealed class SetActiveSpawnRole(Session session) : IOperationHandler
{
    public byte Code => (byte)OperationCode.SetActiveSpawnRole;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        if (session.For(peer) is not Player player || request[(byte)ParameterKey.Id] is not { } value) return;

        int role = Convert.ToInt32(value);
        if (role != player.AvatarId && role != player.BuildAvatarId || role == player.ActiveSpawnRole) return;

        WorldObject? previous = session.World.Find(player.ActiveSpawnRole);
        float[] position = player.LastPosition ?? previous?.Position ?? [.. session.World.Spawn];
        float[] rotation = player.LastRotation ?? previous?.Rotation ?? [0f, 0f, 0f, 1f];
        player.ActiveSpawnRole = role;

        var activate = new EventData((byte)EventCode.SetActiveSpawnRole)
        {
            Parameters =
            {
                [(byte)ParameterKey.ActorNr] = player.Actor,
                [(byte)ParameterKey.Id] = role,
                [(byte)ParameterKey.PosX] = position[0],
                [(byte)ParameterKey.PosY] = position[1],
                [(byte)ParameterKey.PosZ] = position[2],
                [(byte)ParameterKey.RotX] = rotation[0],
                [(byte)ParameterKey.RotY] = rotation[1],
                [(byte)ParameterKey.RotZ] = rotation[2],
                [(byte)ParameterKey.RotW] = rotation[3],
            },
        };
        foreach (Player other in session.Players)
            if (other.SpawnRoles && (other == player || other.Saw(player.Actor)))
                other.Peer.Send(activate);
    }
}
