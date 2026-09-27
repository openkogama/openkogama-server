using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.World;

namespace OpenKogama.Game;

public static class Colossus
{
    public const int Empty = -1;
    const string OccupantKey = "occupantWOID";

    public static int Occupant(WorldObject obj) => obj.Runtime.Find(pair => pair.Key == OccupantKey).Value as int? ?? Empty;

    public static void Enter(Session session, WorldObject obj, int avatar)
    {
        if (Occupant(obj) != Empty) return;
        Set(session, obj.Id, avatar);
        Send(session, new EventData((byte)EventCode.GodzillaEnter)
        {
            Parameters = { [(byte)ParameterKey.WorldObjectIDs] = new[] { obj.Id, avatar } },
        });
    }

    public static void Exit(Session session, WorldObject obj, int avatar)
    {
        if (Occupant(obj) != avatar) return;
        Set(session, obj.Id, Empty);
        Send(session, new EventData((byte)EventCode.GodzillaExit)
        {
            Parameters = { [(byte)ParameterKey.WorldObjectID] = obj.Id },
        });
    }

    public static void Release(Session session, int avatar)
    {
        foreach (WorldObject obj in session.World.ToSnapshot().Objects.Where(obj => obj.Type == WorldObjectType.GodzillaTrigger && Occupant(obj) == avatar))
            Exit(session, obj, avatar);
    }

    static void Set(Session session, int id, int occupant)
    {
        session.World.Modify(id, obj => obj.SetRuntime(OccupantKey, PackedType.Int32, occupant));
        session.Logic.Evaluate();
    }

    static void Send(Session session, EventData evt)
    {
        evt.Parameters[(byte)ParameterKey.Timestamp] = session.Logic.Stamp();
        foreach (Player player in session.Players)
            if (player.InWorld && player.Peer.Translator is Kogama.Protocols.OperationRemap remap && remap.Knows(EventCode.GodzillaEnter))
                player.Peer.Send(evt);
    }
}
