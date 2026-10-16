using OpenKogama.Handlers.Operations;
using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.World;

namespace OpenKogama.Game;

public static class Boosts
{
    const float PickupScale = 12f;
    const int PickupLifetimeMs = 1500;

    public static bool LightningSpeed(Session session, Player player)
    {
        if (player.LastPosition is not float[] position) return false;
        var pickup = new WorldObject
        {
            Id = session.World.NewObjectId(),
            ParentId = session.World.RootId,
            Type = WorldObjectType.PickupItemSpawner,
            Position = [position[0], position[1], position[2]],
            Scale = [PickupScale, PickupScale, PickupScale],
            Data = [("itemType", PackedType.Int32, (int)AvatarItemType.NinjaRun)],
        };
        RuntimeDefaults.Apply(pickup);
        var added = new Snapshot([], [pickup], [], []);
        if (!player.Knows(added)) return false;

        GetNextGameBatch.SendAdded(session, 0, added, other => other == player);
        _ = Task.Delay(PickupLifetimeMs).ContinueWith(_ => player.Peer.Send(new EventData((byte)EventCode.UnregisterWorldObject)
        {
            Parameters = { [(byte)ParameterKey.WorldObjectID] = pickup.Id },
        }));
        return true;
    }
}
