using OpenKogama.Game;
using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.World;

namespace OpenKogama.Handlers.Operations;

public sealed class VehicleEnergyUse(PhotonServer server, Session session) : IOperationHandler
{
    const byte Vehicle = 0;
    const byte Spawner = 1;
    const int Lag = 1000;

    readonly Dictionary<int, (int Time, int Count)> _taken = [];

    public byte Code => (byte)OperationCode.VehicleEnergyUse;

    public void Handle(PhotonPeer peer, OperationRequest request)
    {
        var ids = PhotonValues.Table(request[(byte)ParameterKey.WorldObjectIDs]);
        int vehicleId = Convert.ToInt32(ids[Vehicle]);
        int spawnerId = Convert.ToInt32(ids[Spawner]);
        int now = server.Now;

        WorldObject? spawner = session.World.Find(spawnerId);
        bool taken = spawner is { Type: WorldObjectType.WorldObjectSpawnerVehicleEnergy } && TryTake(spawner, now);

        if (taken)
        {
            session.World.Modify(spawnerId, obj => obj.SetRuntime("UseTime", PackedType.Int32, now));

            PhotonDictionary used = PhotonDictionary.Untyped();
            used.Add(Vehicle, vehicleId);
            used.Add(Spawner, spawnerId);

            var evt = new EventData((byte)EventCode.VehicleGotEnergy)
            {
                Parameters =
                {
                    [(byte)ParameterKey.WorldObjectIDs] = used,
                    [(byte)ParameterKey.Timestamp] = now,
                },
            };
            foreach (Player player in session.Players)
                player.Peer.Send(evt);
        }

        peer.Send(new OperationResponse(request)
        {
            ReturnCode = (short)(taken ? 0 : -1),
            Parameters =
            {
                [(byte)ParameterKey.WorldObjectID] = spawnerId,
                [(byte)ParameterKey.Id] = vehicleId,
            },
        });
        Console.WriteLine(taken
            ? $"peer {peer.Id}: vehicle {vehicleId} took energy from {spawnerId}"
            : $"peer {peer.Id}: energy {spawnerId} is not ready");
    }

    bool TryTake(WorldObject spawner, int now)
    {
        int interval = Setting(spawner, "RespawnInterval", 10_000);
        int limit = Setting(spawner, "RespawnCount", -1);

        lock (_taken)
        {
            _taken.TryGetValue(spawner.Id, out var last);
            bool waiting = last.Count > 0 && now - last.Time < interval - Lag;
            bool usedUp = limit >= 0 && last.Count >= limit;
            if (waiting || usedUp) return false;

            _taken[spawner.Id] = (now, last.Count + 1);
            return true;
        }
    }

    static int Setting(WorldObject spawner, string key, int fallback) =>
        spawner.Data.Find(pair => pair.Key == key).Value as int?
        ?? (spawner.Data.Find(pair => pair.Key == "BlueprintData").Value as List<(string Key, PackedType Type, object Value)>)
            ?.Find(pair => pair.Key == key).Value as int?
        ?? fallback;
}
