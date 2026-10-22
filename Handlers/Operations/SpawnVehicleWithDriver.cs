using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.World;

namespace OpenKogama.Handlers.Operations;

public sealed class SpawnVehicleWithDriver(PhotonServer server, Session session) : IOperationHandler
{
    const byte Driver = 0;
    const byte Spawner = 1;
    const byte Vehicle = 3;

    public byte Code => (byte)OperationCode.SpawnVehicleWithDriver;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        var ids = PhotonValues.Table(request[(byte)ParameterKey.WorldObjectIDs]);
        int driverId = Convert.ToInt32(ids[Driver]);
        int spawnerId = Convert.ToInt32(ids[Spawner]);
        byte seat = Convert.ToByte(request[(byte)ParameterKey.SeatID]);
        Player? player = session.For(peer);

        WorldObject? spawner = session.World.Find(spawnerId);
        if (player is null || spawner is null || Spawn(session, player, driverId, spawner, seat, server.Now) is not int vehicleId)
        {
            Console.WriteLine($"peer {peer.Id}: cannot spawn vehicle from {spawnerId}");
            peer.Send(new OperationResponse(request) { ReturnCode = -1 });
            return;
        }

        peer.Send(new OperationResponse(request));
        Console.WriteLine($"peer {peer.Id}: spawned vehicle {vehicleId} from {spawnerId}");
    }

    public static int? Spawn(Session session, Player player, int driverId, WorldObject spawner, byte seat, int now)
    {
        if (TemplateOf(spawner) is not int templateId || session.World.CloneTree(templateId) is not { } vehicle) return null;
        int spawnerId = spawner.Id;

        List<WorldObject> parts = session.World.Subtree(vehicle.Id);
        if (!session.World.SetParent(driverId, vehicle.Id))
        {
            session.World.Remove(vehicle.Id);
            return null;
        }
        foreach (WorldObject part in parts)
            session.World.Modify(part.Id, obj => obj.Owner = player.Actor);
        session.World.Modify(vehicle.Id, obj => obj.Transient = true);
        session.World.Modify(driverId, obj => obj.SetRuntime("seat", PackedType.Int32, (int)seat));
        session.World.Modify(spawnerId, obj => obj.SetRuntime("UseTime", PackedType.Int32, now));

        PhotonDictionary spawned = PhotonDictionary.Untyped();
        spawned.Add(Driver, driverId);
        spawned.Add(Spawner, spawnerId);
        spawned.Add(Vehicle, vehicle.Id);

        var evt = new EventData((byte)EventCode.SpawnVehicleWithDriver)
        {
            Parameters =
            {
                [(byte)ParameterKey.WorldObjectIDs] = spawned,
                [(byte)ParameterKey.ActorNr] = player.Actor,
                [(byte)ParameterKey.Timestamp] = now,
                [(byte)ParameterKey.LinkID] = -1,
                [(byte)ParameterKey.ObjectLinkID] = -1,
                [(byte)ParameterKey.SeatID] = seat,
            },
        };
        foreach (Player other in session.Players)
            other.Peer.Send(evt);
        return vehicle.Id;
    }

    static int? TemplateOf(WorldObject spawner) =>
        Find(spawner.Data, "BlueprintData") is List<(string, PackedType, object)> blueprint
        && Find(blueprint, "ChildrenMap") is List<(string, PackedType, object)> children
            ? Find(children, "spawnWorldObjectID") as int?
            : null;

    static object? Find(List<(string Key, PackedType Type, object Value)> pairs, string key) =>
        pairs.Find(pair => pair.Key == key).Value;
}
