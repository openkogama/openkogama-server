using OpenKogama.World;

namespace OpenKogama.Game;

public static class AvatarModifiers
{
    const string Key = "modifiers";
    static readonly Version Current = new(3, 0);
    static readonly HashSet<string> Legacy =
    [
        "None", "Fire", "Mutant", "Sticky", "Poison", "WallJump", "InstantDeath", "NoFriction", "FlamerBurn", "Underwater",
        "Frozen", "NinjaRun", "Shrunken", "WindFriction", "DisableVehiclePickup", "Enlarged", "Shielded",
        "GodzillaS", "GodzillaM", "GodzillaL", "GodzillaXL",
        "GodzillaLaserBurnS", "GodzillaLaserBurnM", "GodzillaLaserBurnL", "GodzillaLaserBurnXL",
    ];

    public static bool Limited(Player player) =>
        !Version.TryParse(player.ClientVersion, out Version? version) || version < Current;

    public static WorldObject For(Player player, WorldObject obj)
    {
        if (obj.Type != WorldObjectType.Avatar || !Limited(player)
            || obj.Runtime.Find(pair => pair.Key == Key).Value is not List<(string Key, PackedType Type, object Value)> modifiers
            || modifiers.All(modifier => Legacy.Contains(modifier.Key)))
            return obj;

        return new WorldObject
        {
            Id = obj.Id,
            ParentId = obj.ParentId,
            ItemId = obj.ItemId,
            Type = obj.Type,
            Position = obj.Position,
            Rotation = obj.Rotation,
            Scale = obj.Scale,
            Data = obj.Data,
            Owner = obj.Owner,
            PreviewOwner = obj.PreviewOwner,
            Runtime =
            [
                .. obj.Runtime.Where(pair => pair.Key != Key),
                (Key, PackedType.Hashtable, modifiers.Where(modifier => Legacy.Contains(modifier.Key)).ToList()),
            ],
            Transient = obj.Transient,
        };
    }

    public static Dictionary<object, object?>? Without(Dictionary<object, object?> changes)
    {
        if (changes.GetValueOrDefault(Key) is not Dictionary<object, object?> modifiers
            || modifiers.Keys.All(name => name is string text && Legacy.Contains(text)))
            return null;

        return new Dictionary<object, object?>(changes)
        {
            [Key] = modifiers.Where(pair => pair.Key is string text && Legacy.Contains(text)).ToDictionary(pair => pair.Key, pair => pair.Value),
        };
    }
}
