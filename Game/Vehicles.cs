using OpenKogama.World;

namespace OpenKogama.Game;

public static class Vehicles
{
    public static readonly HashSet<WorldObjectType> Types = [WorldObjectType.HoverCraft, WorldObjectType.MonoPlane, WorldObjectType.JetPack, WorldObjectType.HamsterWheel];
    public static readonly HashSet<WorldObjectType> Spawners = [WorldObjectType.WorldObjectSpawnerVehicle, WorldObjectType.WorldObjectSpawnerVehicleEnergy];
}
