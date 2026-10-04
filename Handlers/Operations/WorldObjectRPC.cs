using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.World;

namespace OpenKogama.Handlers.Operations;

public sealed class WorldObjectRPC(Session session) : IOperationHandler
{
    public byte Code => (byte)OperationCode.WorldObjectRPCOperation;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        object? rpcData = request[(byte)ParameterKey.WorldObjectRPCData];
        int targetId = Convert.ToInt32(request[(byte)ParameterKey.WorldObjectID]);
        List<Interaction> interactions = Interaction.Parse(rpcData);

        if (session.For(peer) is Player shooter
            && session.World.Find(targetId) is { Type: WorldObjectType.AdvancedGhost } ghost
            && interactions.Find(interaction => interaction.Damage > 0f) is { Damage: > 0f } hit)
        {
            session.Round.GhostHit(shooter, ghost, hit.Damage);
        }

        session.HitNpc(targetId, peer.Id, interactions);

        var evt = new EventData((byte)EventCode.WorldObjectRPCEvent)
        {
            Parameters =
            {
                [(byte)ParameterKey.ActorNr] = (int)peer.Id,
                [(byte)ParameterKey.WorldObjectID] = targetId,
                [(byte)ParameterKey.WorldObjectRPCData] = rpcData,
            },
        };
        foreach (Player player in session.Players)
            player.Peer.Send(evt);
    }
}
