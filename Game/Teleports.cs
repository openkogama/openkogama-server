using System.Collections.Concurrent;
using System.Numerics;
using OpenKogama.Handlers.Operations;
using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.World;

namespace OpenKogama.Game;

public static class Teleports
{
    const byte Driver = 0;
    const byte Vehicle = 4;
    const byte DriverSeat = 0;
    const float SpawnerDepth = 1000f;
    const int SeatDelayMs = 300;
    const int EjectDelayMs = 200;
    const int VehicleCleanupMs = 1000;
    const int TeleporterLifetimeMs = 1500;
    const float TeleporterScale = 1_000_000f;

    static readonly ConcurrentDictionary<int, bool> Private = new();

    public static bool IsPrivate(int objectId) => Private.ContainsKey(objectId);

    public static bool HiddenVehicle(Session session, Player player, Vector3 target)
    {
        if (player.Peer.Translator is not Kogama.Protocols.OperationRemap remap || !remap.Knows(EventCode.ForceDetachWorldObjectFromVehicle)) return false;

        var hull = new Prototype(session.World.NewPrototypeId(), 1f, 0, new CubeModel());
        int spawnerId = session.World.NewObjectId();
        int hoverId = session.World.NewObjectId();
        int hullId = session.World.NewObjectId();
        WorldObject[] objects =
        [
            new()
            {
                Id = spawnerId,
                ParentId = session.World.RootId,
                Type = WorldObjectType.WorldObjectSpawnerVehicle,
                Position = [target.X, target.Y - SpawnerDepth, target.Z],
                Data = [Blueprint(null, ("spawnWorldObjectID", hoverId))],
            },
            new()
            {
                Id = hoverId,
                ParentId = spawnerId,
                Type = WorldObjectType.HoverCraft,
                Position = [0f, SpawnerDepth, 0f],
                Data = [Blueprint(null, ("HoverCraftHull", hullId))],
            },
            new()
            {
                Id = hullId,
                ParentId = hoverId,
                Type = WorldObjectType.CubeModel,
                Data = [("protoTypeID", PackedType.Int32, hull.Id)],
            },
        ];
        foreach (WorldObject obj in objects) RuntimeDefaults.Apply(obj);
        var added = new Snapshot([hull], objects, [], []);
        if (!player.Knows(added)) return false;

        foreach (WorldObject obj in objects) Private[obj.Id] = true;
        GetNextGameBatch.SendAdded(session, 0, added, other => other == player);
        _ = Ride(player, player.PlayAvatar, spawnerId, hoverId, hull.Id, [.. objects.Select(obj => obj.Id)]);
        return true;
    }

    public static bool Direct(Player player, Vector3 target)
    {
        if (!Reauths.Teleports(player)) return false;
        float[] turn = player.LastRotation is { Length: 4 } rotation ? rotation : [0f, 0f, 0f, 1f];
        player.Peer.Send(new EventData((byte)EventCode.UpdateWorldObject)
        {
            Parameters =
            {
                [(byte)ParameterKey.WorldObjectID] = player.ActiveSpawnRole,
                [(byte)ParameterKey.ActorNr] = 0,
                [(byte)ParameterKey.Timestamp] = player.Peer.ServerTime,
                [(byte)ParameterKey.PosX] = target.X,
                [(byte)ParameterKey.PosY] = target.Y,
                [(byte)ParameterKey.PosZ] = target.Z,
                [(byte)ParameterKey.ByteRotation] = QuaternionCompression.ToBytes(turn[0], turn[1], turn[2], turn[3]),
                [(byte)ParameterKey.TransformPackageType] = (byte)TransformPackage.Teleport,
            },
        });
        return true;
    }

    public static bool Teleporter(Session session, Player player, Vector3 target)
    {
        int groupId = session.World.NewObjectId();
        int entry = session.World.NewObjectId();
        int exit = session.World.NewObjectId();
        WorldObject[] objects =
        [
            new()
            {
                Id = groupId,
                ParentId = session.World.RootId,
                Type = WorldObjectType.Blueprint,
                Data = [Blueprint(BlueprintType.Teleporter, ("teleporter1", entry), ("teleporter2", exit))],
            },
            Pad(entry, groupId, target),
            Pad(exit, groupId, target),
        ];
        var pair = new Snapshot([], objects, [], []);
        if (!player.Knows(pair)) return false;

        GetNextGameBatch.SendAdded(session, 0, pair, other => other == player);
        _ = Task.Delay(TeleporterLifetimeMs).ContinueWith(_ => Remove(player, groupId));
        return true;
    }

    static async Task Ride(Player player, int avatar, int spawnerId, int hoverId, int hullPrototype, int[] ids)
    {
        await Task.Delay(SeatDelayMs);
        PhotonDictionary seated = PhotonDictionary.Untyped();
        seated.Add(Driver, avatar);
        seated.Add(Vehicle, hoverId);
        player.Peer.Send(new EventData((byte)EventCode.AttachWorldObjectToSeat)
        {
            Parameters =
            {
                [(byte)ParameterKey.WorldObjectIDs] = seated,
                [(byte)ParameterKey.ActorNr] = player.Actor,
                [(byte)ParameterKey.SeatID] = DriverSeat,
            },
        });

        await Task.Delay(EjectDelayMs);
        player.Peer.Send(new EventData((byte)EventCode.ForceDetachWorldObjectFromVehicle)
        {
            Parameters = { [(byte)ParameterKey.WorldObjectIDs] = new[] { hoverId, avatar } },
        });

        await Task.Delay(VehicleCleanupMs);
        Remove(player, spawnerId);
        player.Peer.Send(new EventData((byte)EventCode.UnregisterPrototype) { Parameters = { [(byte)ParameterKey.WorldInventoryID] = hullPrototype } });
        foreach (int id in ids) Private.TryRemove(id, out _);
    }

    static WorldObject Pad(int id, int groupId, Vector3 position) => new()
    {
        Id = id,
        ParentId = groupId,
        Type = WorldObjectType.Teleporter,
        Position = [position.X, position.Y, position.Z],
        Scale = [TeleporterScale, TeleporterScale, TeleporterScale],
    };

    static void Remove(Player player, int objectId) =>
        player.Peer.Send(new EventData((byte)EventCode.UnregisterWorldObject) { Parameters = { [(byte)ParameterKey.WorldObjectID] = objectId } });

    static (string, PackedType, object) Blueprint(BlueprintType? type, params (string Key, int Id)[] children)
    {
        var blueprint = new List<(string, PackedType, object)>();
        if (type is BlueprintType clientSide) blueprint.Add(("ClientSideType", PackedType.Byte, (byte)clientSide));
        blueprint.Add(("ChildrenMap", PackedType.Hashtable, children.Select(child => (child.Key, PackedType.Int32, (object)child.Id)).ToList()));
        return ("BlueprintData", PackedType.Hashtable, blueprint);
    }
}
