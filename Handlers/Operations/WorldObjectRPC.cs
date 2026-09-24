using System.Buffers.Binary;
using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.World;

namespace OpenKogama.Handlers.Operations;

public sealed class WorldObjectRPC(Session session) : IOperationHandler
{
    const byte HasType = 1;
    const byte HasDamage = 2;

    static readonly Dictionary<byte, float> DefaultDamage = new()
    {
        [4] = 100f,
        [6] = 110f,
        [9] = 13f,
    };

    public byte Code => (byte)OperationCode.WorldObjectRPCOperation;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        object? rpcData = request[(byte)ParameterKey.WorldObjectRPCData];
        int targetId = Convert.ToInt32(request[(byte)ParameterKey.WorldObjectID]);

        if (session.For(peer) is Player shooter
            && session.World.Find(targetId) is { Type: WorldObjectType.AdvancedGhost } ghost
            && Damage(rpcData) is float damage)
        {
            session.Round.GhostHit(shooter, ghost, damage);
        }

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

    static float? Damage(object? rpcData)
    {
        if (PhotonValues.Normalize(rpcData) is not Dictionary<object, object?> table
            || table.Values.OfType<byte[]>().FirstOrDefault() is not { Length: > 0 } packet)
            return null;

        byte flags = packet[0];
        int offset = 1;
        byte type = 0;
        if ((flags & HasType) != 0) type = packet[offset++];

        if ((flags & HasDamage) != 0 && packet.Length >= offset + 4)
            return BinaryPrimitives.ReadSingleBigEndian(packet.AsSpan(offset));

        return DefaultDamage.TryGetValue(type, out float damage) ? damage : null;
    }
}
