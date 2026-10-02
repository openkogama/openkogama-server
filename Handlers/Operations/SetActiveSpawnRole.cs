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
        if (role != player.AvatarId && !player.ExtraRole(role) || role == player.ActiveSpawnRole) return;
        session.ActivateRole(player, role);
    }
}
