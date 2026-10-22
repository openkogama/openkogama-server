using OpenKogama.Photon;
using OpenKogama.World;

namespace OpenKogama.Game;

public static class AvatarModifiers
{
    const string Key = "modifiers";
    static readonly Version LegacyClients = new(1, 8);
    static readonly (Version Since, Version? Until, string[] Names)[] Introduced =
    [
        (new(0, 0), null, ["None", "Fire", "Mutant", "Sticky", "Poison", "WallJump", "InstantDeath", "NoFriction", "FlamerBurn", "Underwater", "Frozen"]),
        (new(1, 25, 12, 280), null, ["NinjaRun"]),
        (new(1, 26, 19, 298), null, ["Shrunken", "WindFriction", "DisableVehiclePickup"]),
        (new(1, 27, 0, 335), null, ["Enlarged"]),
        (new(1, 36, 8, 707), null, ["Shielded"]),
        (new(1, 39, 47), new(2, 3, 10), ["GodzillaS", "GodzillaM", "GodzillaL", "GodzillaXL", "GodzillaLaserBurnS", "GodzillaLaserBurnM", "GodzillaLaserBurnL", "GodzillaLaserBurnXL"]),
        (new(1, 41, 6), new(2, 3, 10), ["GodzillaGrowthInvulnerability"]),
        (new(1, 41, 6), null, ["SpawnProtection"]),
        (new(1, 77, 2), null, ["RayHeal"]),
        (new(1, 98, 4), null, ["TimeAttackFlagDebriefSlow"]),
        (new(2, 30, 11, 1125), null, ["Lethal", "HealingMat", "SlowMat", "SpeedMat", "CrumbleMat"]),
        (new(2, 30, 46), null, ["RayHealEnemy"]),
    ];

    public static WorldObject For(Player player, WorldObject obj)
    {
        if (obj.Type != WorldObjectType.Avatar
            || obj.Runtime.Find(pair => pair.Key == Key).Value is not List<(string Key, PackedType Type, object Value)> modifiers)
            return obj;
        HashSet<string> known = KnownBy(player);
        if (modifiers.All(modifier => Knows(known, modifier.Key))) return obj;

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
                (Key, PackedType.Hashtable, modifiers.Where(modifier => Knows(known, modifier.Key)).ToList()),
            ],
            Transient = obj.Transient,
        };
    }

    public static Dictionary<object, object?>? Without(Player player, Dictionary<object, object?> changes)
    {
        if (changes.GetValueOrDefault(Key) is not Dictionary<object, object?> modifiers) return null;
        HashSet<string> known = KnownBy(player);
        if (modifiers.Keys.All(name => Knows(known, name))) return null;

        return new Dictionary<object, object?>(changes)
        {
            [Key] = modifiers.Where(pair => Knows(known, pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value),
        };
    }

    static HashSet<string> KnownBy(Player player)
    {
        if (player.Peer.Protocol == PhotonProtocol.Protocol15) return [];
        Version? client = player.Legacy ? LegacyClients : Version.TryParse(player.Build, out Version? build) ? build : null;
        return client is null ? [] : [.. Introduced.Where(entry => client >= entry.Since && (entry.Until is null || client <= entry.Until)).SelectMany(entry => entry.Names)];
    }

    static bool Knows(HashSet<string> known, object? name) => name is string text && known.Contains(text.TrimStart('_'));
}
