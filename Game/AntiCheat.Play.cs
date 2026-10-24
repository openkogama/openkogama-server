using System.Runtime.CompilerServices;
using OpenKogama.Handlers;
using OpenKogama.Handlers.Legacy2012;
using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.World;

namespace OpenKogama.Game;

public static partial class AntiCheat
{
    const int Planet = 0;
    const int Item = 2;
    const byte DefaultGunMaterial = 24;
    const int MaxCubeChanges = 64;
    const int MaxRuntimeEvents = 100_000;
    const int MaxEvents = 40;
    const long EventWindowMs = 2000;
    const int Depth = 64;
    const byte CubeAdd = TerrainEvents.CubeAdd;
    const byte CubeRemove = TerrainEvents.CubeRemove;
    const byte AddedCubeRemove = TerrainEvents.AddedCubeRemove;
    const byte Driver = 0;
    const byte Spawner = 1;
    const byte Vehicle = 4;
    const int MaxVehicles = 4;
    const long VehicleWindowMs = 30_000;

    static readonly HashSet<byte> Explosions = [3, 4, 6, 7, 8, 9, 10, 15, 16];
    static readonly HashSet<OperationCode> EditorOnly =
    [
        OperationCode.RegisterWorldObject, OperationCode.UpdatePrototypeScale, OperationCode.AddLink, OperationCode.RemoveLink,
        OperationCode.AddObjectLink, OperationCode.RemoveObjectLink, OperationCode.Ungroup, OperationCode.LockHierarchy,
        OperationCode.RequestWoUniquePrototype, OperationCode.ResetLogicChunk, OperationCode.TransferWorldObjectsToGroup,
        OperationCode.CloneWorldObjectTree, OperationCode.AddPlanetToPlanet, OperationCode.RequestBuiltInItem, OperationCode.AddItemToWorld,
        OperationCode.AddWorldObjectToInventory, OperationCode.AddWorldObjectToInventoryDev, OperationCode.ResetTerrain, OperationCode.PublishPlanet,
    ];
    static readonly HashSet<OperationCode> OwnObjects =
    [
        OperationCode.UnregisterWorldObject, OperationCode.UpdateWorldObject, OperationCode.TransferOwnership,
        OperationCode.UpdateWorldObjectData, OperationCode.UpdateWorldObjectDataPartial, OperationCode.RemoveWorldObjectDataPartial,
    ];
    static readonly HashSet<Op2012> EditorOnly2012 =
    [
        Op2012.RegisterPrototype, Op2012.RegisterLocalPrototype, Op2012.UpdatePrototype, Op2012.UpdatePrototypeScale,
        Op2012.UpdateTerrain, Op2012.AddLink, Op2012.RemoveLink, Op2012.PublishPlanet, Op2012.AddPrototypeToInventory, Op2012.Ungroup,
        Op2012.LockHierarchy, Op2012.UploadPlanetScreenshot, Op2012.RequestWoUniquePrototype, Op2012.ResetLogicChunk,
    ];
    static readonly HashSet<Op2012> OwnObjects2012 =
    [
        Op2012.UnregisterWorldObject, Op2012.UpdateWorldObject, Op2012.UpdateWorldObjectData, Op2012.TransferOwnership,
    ];
    static readonly ConditionalWeakTable<GameWorld, Terrain> Terrains = new();
    static readonly ConditionalWeakTable<Player, Queue<long>> Events = new();
    static readonly ConditionalWeakTable<Player, Queue<long>> Spawned = new();
    static readonly ConditionalWeakTable<Player, HashSet<byte>> Refused = new();
    static readonly Lazy<Dictionary<int, float>> Toughness = new(() => Materials.For("2015").Concat(Materials.For("2025"))
        .GroupBy(material => material.Id).ToDictionary(group => group.Key, group => group.Last().Physical is { Length: > 4 } physical ? physical[4] : 0f));

    public static bool Build { get; set; } = true;

    sealed class Terrain
    {
        public int Events;
        public readonly Dictionary<(short, short, short), byte> Added = [];
        public readonly HashSet<(int, int, int)> Broken = [];
    }

    public static bool Allows(Session session, Player? player, OperationRequest request)
    {
        var code = (OperationCode)request.OperationCode;
        if (code == OperationCode.RuntimeEvent) return RuntimeEvent(session, player, request[(byte)ParameterKey.Data] as byte[]);
        if (!Build || !session.Play) return true;
        if (EditorOnly.Contains(code)) return false;
        if (OwnObjects.Contains(code))
        {
            int id = Convert.ToInt32(request[(byte)ParameterKey.WorldObjectID]);
            return player is not null && (Mine(session, player, id) || code == OperationCode.UpdateWorldObject && Holds(session, player, id));
        }
        return code switch
        {
            OperationCode.UploadScreenshot => Convert.ToInt32(request[(byte)ParameterKey.ImageType]) is not (Planet or Item),
            OperationCode.UpdatePrototype => CubeGun(session, player, Convert.ToInt32(request[(byte)ParameterKey.WorldInventoryID]),
                request[(byte)ParameterKey.WorldInventoryData] as byte[]),
            OperationCode.AttachWorldObjectToSeat => Seats(session, player, PhotonValues.Table(request[(byte)ParameterKey.WorldObjectIDs])),
            OperationCode.SpawnVehicleWithDriver => Spawns(session, player, PhotonValues.Table(request[(byte)ParameterKey.WorldObjectIDs])),
            OperationCode.DetachWorldObjectFromVehicle => player is not null && player.Owns(Convert.ToInt32(request[(byte)ParameterKey.WorldObjectID])),
            _ => true,
        };
    }

    static bool Seats(Session session, Player? player, Dictionary<object, object?> ids) =>
        player is not null && Id(ids, Driver) is int driver && player.Owns(driver)
        && Id(ids, Vehicle) is int id && session.World.Find(id) is { } vehicle && Vehicles.Types.Contains(vehicle.Type)
        && !(session.World.Find(vehicle.ParentId) is { } parent && Vehicles.Spawners.Contains(parent.Type));

    static bool Spawns(Session session, Player? player, Dictionary<object, object?> ids) =>
        player is not null && Id(ids, Driver) is int driver && player.Owns(driver)
        && Id(ids, Spawner) is int id && session.World.Find(id) is { } spawner && Vehicles.Spawners.Contains(spawner.Type)
        && (!Spam || !Floods(Spawned.GetOrCreateValue(player), MaxVehicles, VehicleWindowMs));

    static int? Id(Dictionary<object, object?> data, byte key) =>
        Value(data, key) is byte or short or int ? Convert.ToInt32(Value(data, key)) : null;

    public static bool Allows(Session session, Player? player, Op2012 code, OperationRequest request)
    {
        if (!Build || !session.Play) return true;
        if (EditorOnly2012.Contains(code)) return false;
        return !OwnObjects2012.Contains(code) || player is not null && Mine(session, player, Convert.ToInt32(request[(byte)Key2012.WorldObjectID]));
    }

    public static bool Registers(Session session, WorldObject obj) => !Build || !session.Play || obj.Type == WorldObjectType.Avatar;

    public static bool FirstRefusal(Player? player, byte code)
    {
        if (player is null) return true;
        HashSet<byte> refused = Refused.GetOrCreateValue(player);
        lock (refused) return refused.Add(code);
    }

    public static void Disown(GameWorld world)
    {
        if (!Build) return;
        foreach (WorldObject obj in world.ToSnapshot().Objects.Where(obj => obj.Owner > 0))
            world.Modify(obj.Id, owned => owned.Owner = null);
    }

    static bool Mine(Session session, Player player, int objectId) =>
        player.Owns(objectId) || player.ExtraRole(objectId)
        || Up(session, objectId).Any(obj => obj.Owner == player.Actor || player.Owns(obj.Id) || player.ExtraRole(obj.Id));

    static bool Holds(Session session, Player player, int objectId) =>
        (session.World.Find(player.ActiveSpawnRole)?.ParentId == objectId || session.World.Find(player.AvatarId)?.ParentId == objectId)
        && session.World.Find(objectId) is { ParentId: not -1 };

    static IEnumerable<WorldObject> Up(Session session, int id)
    {
        for (int depth = 0; depth < Depth && session.World.Find(id) is { } obj; depth++, id = obj.ParentId)
            yield return obj;
    }

    static bool CubeGun(Session session, Player? player, int prototypeId, byte[]? changes)
    {
        if (changes is null || session.World.FindFirst(WorldObjectType.CubeModelTerrainFineGrained)?.PrototypeId != prototypeId
            || session.World.FindPrototype(prototypeId) is not { } terrain) return false;
        var cubes = CubeModel.Changes(changes);
        if (cubes.Count > MaxCubeChanges) return false;
        HashSet<byte> guns = GunMaterials(session, player);
        return cubes.All(change => change.Action switch
        {
            CubeAction.Added => change.Cube is { } cube && CubeModel.Row(cube) == 1 && CubeModel.Materials(cube) is var materials
                && materials.All(material => material == materials[0]) && Tough(materials[0]) == 0 && guns.Contains(materials[0]),
            CubeAction.Deleted => terrain.Cubes.Get(change.X, change.Y, change.Z) is { } existing && Tough(CubeModel.Materials(existing)[0]) == 0,
            _ => false,
        });
    }

    static HashSet<byte> GunMaterials(Session session, Player? player)
    {
        HashSet<byte> materials = [.. session.World.FindAll(WorldObjectType.PickupCubeGun).Select(gun => ItemMaterial(gun.Data) ?? DefaultGunMaterial)];
        if (player is not null && session.World.Find(player.PlayAvatar)?.Runtime.Find(pair => pair.Key == "currentItem").Value is List<(string Key, PackedType Type, object Value)> held
            && held.Find(pair => pair.Key == "type").Value is int type && type == (int)AvatarItemType.CubeGun && ItemMaterial(held) is null)
            materials.Add(DefaultGunMaterial);
        return materials;
    }

    static byte? ItemMaterial(List<(string Key, PackedType Type, object Value)> data) =>
        data.Find(pair => pair.Key == "itemData").Value is List<(string Key, PackedType Type, object Value)> item
        && item.Find(pair => pair.Key == "material").Value is { } material
            ? Convert.ToByte(material)
            : null;

    static float Tough(byte material) => Toughness.Value.GetValueOrDefault(material);

    static bool RuntimeEvent(Session session, Player? player, byte[]? data)
    {
        byte type = data is { Length: > 0 } ? data[0] : (byte)0;
        bool known = data is not null && TerrainEvents.Valid(data) && (type is CubeAdd or CubeRemove or AddedCubeRemove || Explosions.Contains(type));
        if (Crash && !known) return false;
        if (Spam && player is not null && Floods(Events.GetOrCreateValue(player), MaxEvents, EventWindowMs)) return false;
        if (!Build || !known || data is null || Explosions.Contains(type)) return true;

        (short, short, short) cube = TerrainEvents.Cube(data);
        Terrain terrain = Terrains.GetOrCreateValue(session.World);
        lock (terrain)
        {
            int count = session.World.RuntimeEventCount;
            if (count < terrain.Events)
            {
                terrain.Added.Clear();
                terrain.Broken.Clear();
                terrain.Events = 0;
            }
            if (terrain.Events < count) Replay(session, terrain, session.World.RuntimeEventsFrom(terrain.Events));
            bool allowed = type == CubeAdd
                ? Tough(data[1]) > 0 && GunMaterials(session, player).Contains(data[1])
                : terrain.Added.Remove(cube) || Destructible(session, terrain, cube);
            if (!allowed) return false;
            if (type == CubeAdd) terrain.Added[cube] = data[1];
            terrain.Events = Records(session.World) ? count + 1 : count;
            return true;
        }
    }

    static void Replay(Session session, Terrain terrain, List<byte[]> runtimeEvents)
    {
        foreach (byte[] past in runtimeEvents)
        {
            if (!TerrainEvents.Valid(past) || past[0] is not (CubeAdd or CubeRemove or AddedCubeRemove)) continue;
            (short, short, short) cube = TerrainEvents.Cube(past);
            if (past[0] == CubeAdd) terrain.Added[cube] = past[1];
            else if (!terrain.Added.Remove(cube)) Destructible(session, terrain, cube);
        }
    }

    public static bool Records(GameWorld world) => !Spam || world.RuntimeEventCount < MaxRuntimeEvents;

    static bool Destructible(Session session, Terrain terrain, (short X, short Y, short Z) cube)
    {
        Prototype? fine = session.World.FindFirst(WorldObjectType.CubeModelTerrainFineGrained)?.PrototypeId is int fineId ? session.World.FindPrototype(fineId) : null;
        if (fine?.Cubes.Get(cube.X, cube.Y, cube.Z) is { } piece) return Tough(CubeModel.Materials(piece)[0]) > 0;
        if (fine is null || session.World.FindFirst(WorldObjectType.CubeModelPrototypeTerrain)?.PrototypeId is not int coarseId
            || session.World.FindPrototype(coarseId) is not { Scale: > 0 } coarse) return false;

        float ratio = fine.Scale / coarse.Scale;
        (int, int, int) block = ((int)MathF.Floor(cube.X * ratio), (int)MathF.Floor(cube.Y * ratio), (int)MathF.Floor(cube.Z * ratio));
        if (terrain.Broken.Contains(block)) return true;
        if (coarse.Cubes.Get(block.Item1, block.Item2, block.Item3) is not { } whole || Tough(CubeModel.Materials(whole)[0]) <= 0) return false;
        terrain.Broken.Add(block);
        return true;
    }
}
