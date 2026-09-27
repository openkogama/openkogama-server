namespace OpenKogama.World;

public static class RuntimeDefaults
{
    const int LongAgo = -60_000;

    public static void Apply(WorldObject obj)
    {
        foreach ((string key, PackedType type, object value) in For(obj))
            if (!obj.Runtime.Exists(pair => pair.Key == key))
                obj.Runtime.Add((key, type, value));
    }

    public static void Reset(WorldObject obj)
    {
        var keys = For(obj).Select(pair => pair.Item1).ToHashSet();
        obj.Runtime.RemoveAll(pair => keys.Contains(pair.Key));
        Apply(obj);
    }

    static List<(string, PackedType, object)> For(WorldObject obj) => [("iH", PackedType.Bool, false), .. ForType(obj)];

    static List<(string, PackedType, object)> ForType(WorldObject obj) => obj.Type switch
    {
        WorldObjectType.ToggleBox => [("toggled", PackedType.Bool, false)],
        WorldObjectType.TimeTrigger or WorldObjectType.ShootableButton => [("cT", PackedType.Int32, -1)],
        WorldObjectType.UseLever => [("a", PackedType.Bool, obj.Data.Find(pair => pair.Key == "beginActivated").Value as bool? ?? false)],
        WorldObjectType.CountingCube => [("currentValue", PackedType.Int32, obj.Data.Find(pair => pair.Key == "startingValue").Value as int? ?? 0)],
        WorldObjectType.PulseBox => [("currentStartTime", PackedType.Int32, 0)],
        WorldObjectType.PressurePlate => [("triggerBoxState", PackedType.Bool, false)],
        WorldObjectType.GodzillaTrigger => [("occupantWOID", PackedType.Int32, -1)],
        WorldObjectType.CollectTheItemDropOff => [("isActive", PackedType.Bool, true)],
        WorldObjectType.SentryGun =>
        [
            ("health", PackedType.Single, 300f),
            ("deathTime", PackedType.Int32, LongAgo),
        ],
        WorldObjectType.AdvancedGhost =>
        [
            ("health", PackedType.Single, 80f),
            ("deathTime", PackedType.Int32, LongAgo),
            ("modifiers", PackedType.Hashtable, Empty()),
        ],
        WorldObjectType.WorldObjectSpawnerVehicle =>
        [
            ("UseTime", PackedType.Int32, LongAgo),
        ],
        WorldObjectType.RandomBox =>
        [
            ("currentValue", PackedType.Int32, 0),
            ("currentRandomValues", PackedType.Int32Array, new[] { Random.Shared.Next(1, int.MaxValue), 1, 1 }),
        ],
        WorldObjectType.HoverCraft or WorldObjectType.MonoPlane => Vehicle(),
        WorldObjectType.JetPack => [.. Vehicle(), ("jetMode", PackedType.Byte, (byte)0)],
        WorldObjectType.HamsterWheel =>
        [
            .. Vehicle(),
            ("isMovingForward", PackedType.Bool, false),
            ("isMovingBackwards", PackedType.Bool, false),
            ("isGrounded", PackedType.Bool, false),
        ],
        _ => [],
    };

    static List<(string, PackedType, object)> Vehicle() =>
    [
        ("health", PackedType.Single, 150f),
        ("isFiring", PackedType.Bool, false),
        ("modifiers", PackedType.Hashtable, Empty()),
        ("currentItem", PackedType.Hashtable, Empty()),
        ("isDead", PackedType.Bool, false),
    ];

    static List<(string, PackedType, object)> Empty() => [];
}
