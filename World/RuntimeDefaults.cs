namespace OpenKogama.World;

public static class RuntimeDefaults
{
    const int LongAgo = -60_000;

    public static void Apply(WorldObject obj)
    {
        foreach ((string key, PackedType type, object value) in For(obj.Type))
            if (!obj.Runtime.Exists(pair => pair.Key == key))
                obj.Runtime.Add((key, type, value));
    }

    static List<(string, PackedType, object)> For(WorldObjectType type) => type switch
    {
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
