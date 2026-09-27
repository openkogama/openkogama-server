using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.World;

namespace OpenKogama.Game;

public static class CollectTheItem
{
    const string OriginalKey = "OriginalId";
    const string ActiveKey = "isActive";

    public static bool IsTemporary(WorldObject instance) => instance.Runtime.Exists(pair => pair.Key == OriginalKey);

    public static void Pickup(Session session, WorldObject instance, int instigator)
    {
        session.Logic.Signal(instance.Id, instigator, true);
        if (!IsTemporary(instance))
        {
            int actor = session.Players.FirstOrDefault(player => player.AvatarId == instigator)?.Actor ?? 0;
            Handlers.Operations.Pickup.Respawn(session, instance.Id, actor);
            return;
        }

        session.World.Remove(instance.Id);
        var unregister = new EventData((byte)EventCode.UnregisterWorldObject)
        {
            Parameters = { [(byte)ParameterKey.WorldObjectID] = instance.Id },
        };
        foreach (Player player in session.Players)
            player.Peer.Send(unregister);
    }

    public static void DropOff(Session session, WorldObject dropOff, int instigator)
    {
        if (Flag(dropOff, "doOnce"))
            session.World.Modify(dropOff.Id, obj => obj.SetRuntime(ActiveKey, PackedType.Bool, false));

        var delivered = new EventData((byte)EventCode.CollectTheItemDropOff)
        {
            Parameters =
            {
                [(byte)ParameterKey.WorldObjectIDs] = new[] { instigator, dropOff.Id },
                [(byte)ParameterKey.Timestamp] = session.Logic.Stamp(),
            },
        };
        var stay = new EventData((byte)EventCode.TriggerBoxStayBegin)
        {
            Parameters = { [(byte)ParameterKey.WorldObjectID] = dropOff.Id, [(byte)ParameterKey.ActorNr] = instigator },
        };
        foreach (Player player in session.Players)
            player.Peer.Send(Delivers(player) ? delivered : stay);

        session.Triggers.Enter(dropOff.Id, instigator);
        session.Logic.Evaluate();
        session.Triggers.Exit(dropOff.Id, instigator);
        session.Logic.Evaluate();
    }

    public static void LeaveDropOff(Session session, WorldObject dropOff)
    {
        var end = new EventData((byte)EventCode.TriggerBoxStayEnd)
        {
            Parameters = { [(byte)ParameterKey.WorldObjectID] = dropOff.Id },
        };
        foreach (Player player in session.Players)
            if (!Delivers(player))
                player.Peer.Send(end);
    }

    static bool Delivers(Player player) =>
        player.Peer.Translator is Kogama.Protocols.OperationRemap remap && remap.Knows(EventCode.CollectTheItemDropOff);

    public static WorldObject? DropTemporary(Session session, int originalId, float[] position, float[] rotation)
    {
        if (session.World.Find(originalId) is not { Type: WorldObjectType.CollectTheItemCollectableInstance } original || IsTemporary(original)) return null;
        if (session.World.CloneTree(originalId) is not { } clone) return null;

        foreach (WorldObject obj in session.World.SubtreeSnapshot(clone.Id).Objects)
            session.World.Modify(obj.Id, part => part.Transient = true);
        session.World.Modify(clone.Id, root =>
        {
            root.Position = position;
            root.Rotation = rotation;
            root.SetRuntime(OriginalKey, PackedType.Int32, originalId);
        });
        return session.World.Find(clone.Id);
    }

    static bool Flag(WorldObject obj, string key) =>
        obj.Data.Find(pair => pair.Key == "BlueprintData").Value is List<(string Key, PackedType Type, object Value)> blueprint
            && blueprint.Find(pair => pair.Key == key).Value is true;
}
