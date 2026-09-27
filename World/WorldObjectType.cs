namespace OpenKogama.World;

// From MV.WorldObject.WorldObjectType.
public enum WorldObjectType
{
    Avatar = 0,
    CubeModel = 1,
    PointLight = 2,
    TriggerBox = 3,
    Mover = 4,
    Path = 5,
    PathNode = 6,
    SpawnPoint = 7,
    SoundEmitter = 13,
    Battery = 19,
    ToggleBox = 20,
    Negate = 21,
    And = 22,
    TextMsg = 24,
    TimeTrigger = 27,
    CubeModelPrototypeTerrain = 8,
    Group = 9,
    Flag = 17,
    PickupItemHealthPack = 30,
    PickupItemCenterGun = 31,
    CubeModelTerrainFineGrained = 32,
    PressurePlate = 33,
    PickupItemSpawner = 37,
    SpawnPointRed = 39,
    SpawnPointGreen = 40,
    SpawnPointYellow = 41,
    SpawnPointBlue = 42,
    Blueprint = 45,
    PulseBox = 46,
    RandomBox = 47,
    SentryGun = 48,
    CollectibleItem = 49,
    PickupCubeGun = 54,
    HoverCraft = 56,
    WorldObjectSpawnerVehicle = 57,
    MonoPlane = 58,
    JetPack = 59,
    RoundCube = 60,
    AdvancedGhost = 61,
    HamsterWheel = 62,
    KillLimit = 63,
    OculusKillLimit = 64,
    CountingCube = 65,
    ShootableButton = 165,
    UseLever = 166,
}

// From MV.Common.BlueprintType, picked by Data.BlueprintData.ClientSideType.
public enum BlueprintType : byte
{
    Movable = 7,
    Body = 8,
    Teleporter = 9,
}
