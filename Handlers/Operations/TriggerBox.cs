using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.World;

namespace OpenKogama.Handlers.Operations;

public sealed class TriggerBox(Session session, bool entering) : IOperationHandler
{
    public byte Code => (byte)(entering ? OperationCode.TriggerBoxEnter : OperationCode.TriggerBoxExit);

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        var ids = (int[])request[(byte)ParameterKey.WorldObjectID]!;
        int objectId = ids[0];
        int actor = peer.Id;

        WorldObject? obj = session.World.Find(objectId);
        Player? player = session.For(peer);
        if (obj is null || player is null) return;

        switch (obj.Type)
        {
            case WorldObjectType.TriggerBox or WorldObjectType.PressurePlate:
                bool changed = entering ? session.Triggers.Enter(objectId, actor) : session.Triggers.Exit(objectId, actor);
                if (changed) Send(session, objectId, actor, entering);
                break;

            case WorldObjectType.PickupItemSpawner or WorldObjectType.PickupCubeGun:
                if (entering) Pickup.Take(session, player, obj);
                break;

            default:
                Console.WriteLine($"peer {peer.Id}: trigger on {obj.Type} {objectId} not handled");
                break;
        }
    }

    public static void Send(Session session, int objectId, int actor, bool pressed)
    {
        var evt = new EventData((byte)(pressed ? EventCode.TriggerBoxStayBegin : EventCode.TriggerBoxStayEnd))
        {
            Parameters =
            {
                [(byte)ParameterKey.WorldObjectID] = objectId,
                [(byte)ParameterKey.ActorNr] = actor,
            },
        };
        foreach (Player player in session.Players)
            player.Peer.Send(evt);
    }
}
