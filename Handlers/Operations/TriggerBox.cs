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
            case WorldObjectType.TriggerBox or WorldObjectType.PressurePlate or WorldObjectType.TriggerCube:
                bool changed = entering ? session.Triggers.Enter(objectId, actor) : session.Triggers.Exit(objectId, actor);
                if (!changed) break;
                Send(session, objectId, actor, entering);
                session.Logic.Evaluate();
                break;

            case WorldObjectType.ShootableButton or WorldObjectType.UseLever:
                if (!session.Triggers.Switch(objectId, entering, Logic.StartsOn(obj))) break;
                Send(session, objectId, actor, entering);
                if (entering && obj.Type == WorldObjectType.ShootableButton) session.Logic.ReleaseLater(obj);
                session.Logic.Evaluate();
                break;

            case WorldObjectType.GodzillaTrigger when ids.Length > 1:
                if (entering) Colossus.Enter(session, obj, ids[1]);
                else Colossus.Exit(session, obj, ids[1]);
                break;

            case WorldObjectType.CollectTheItemCollectableInstance when entering && ids.Length > 1:
                CollectTheItem.Pickup(session, obj, ids[1]);
                break;

            case WorldObjectType.CollectTheItemDropOff when entering && ids.Length > 1:
                CollectTheItem.DropOff(session, obj, ids[1]);
                break;

            case WorldObjectType.CollectTheItemDropOff:
                CollectTheItem.LeaveDropOff(session, obj);
                break;

            case WorldObjectType.CollectTheItemCollectableInstance:
                break;

            case WorldObjectType.CollectibleItem:
                if (entering) session.Round.Collect(player, obj);
                break;

            case WorldObjectType.GamePoint or WorldObjectType.GamePointChest:
                if (entering) session.GamePasses.Collect(player, obj);
                break;

            case WorldObjectType.PickupItemSpawner or WorldObjectType.PickupCubeGun:
                if (entering) Pickup.Take(session, player, obj);
                break;

            default:
                Console.WriteLine($"peer {peer.Id}: trigger on {obj.Type} {objectId} not handled");
                break;
        }
    }

    public static void Send(Session session, int objectId, int actor, bool pressed) => session.Logic.Signal(objectId, actor, pressed);
}
