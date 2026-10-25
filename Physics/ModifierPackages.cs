namespace OpenKogama.Physics;

enum ModifierPackageType
{
    None, Fire, Mutant, Sticky, Poison, WallJump, InstantDeath, NoFriction, FlamerBurn, Underwater, Frozen, NinjaRun, Shrunken, WindFriction,
    DisableVehiclePickup, Enlarged, Shielded, SpawnProtection, RayHeal, TimeAttackFlagDebriefSlow, Lethal, HealingMat, SlowMat, SpeedMat, CrumbleMat,
    RayHealEnemy,
}

enum ModifierEffect
{
    Density, Weight, JumpPower, Speed, VelocityDamping, Scale, DamageMultiplier, DisablePickups, DisableWeapons, DisableVehicles, Invulnerable,
    PoisonImmune, Friction, Bounciness, EnvironmentDamagePrSec, EnvironmentHealingPrSec, Softness, StaticFriction, WallJump, FlamerDamagePrSec,
    ThrustFactor, OverHeal, PoisonDamagePrSec, LethalDamagePrSec, HealingMat, SlowMat, SpeedMat, CrumbleMat, Size, RayHealEnemyDamagePrSec,
}

enum ModifierType { Multiply, Addition, Override }

enum ModifierAction { Add, Renew, Replace, CancelOut }

readonly record struct Modifier(ModifierType Type, ModifierEffect Effect, Func<float> Value)
{
    public Modifier(ModifierType type, ModifierEffect effect, float value) : this(type, effect, () => value) { }
}

sealed record PackageDefinition(bool Renews, float Duration, Modifier[] Modifiers, Dictionary<ModifierPackageType, ModifierAction>? Actions = null, bool Persistent = false);

sealed class ModifierPackages
{
    const ModifierType Multiply = ModifierType.Multiply;
    const ModifierType Addition = ModifierType.Addition;
    const ModifierType Override = ModifierType.Override;

    static readonly Dictionary<ModifierPackageType, PackageDefinition> Definitions = new()
    {
        [ModifierPackageType.Fire] = new(true, 1f, [new(Addition, ModifierEffect.EnvironmentDamagePrSec, 10f)]),
        [ModifierPackageType.Mutant] = new(true, 20f,
        [
            new(Multiply, ModifierEffect.JumpPower, 1.5f), new(Multiply, ModifierEffect.Speed, 1.25f), new(Override, ModifierEffect.DisablePickups, 1f),
            new(Override, ModifierEffect.DisableVehicles, 1f), new(Override, ModifierEffect.PoisonImmune, 1f), new(Override, ModifierEffect.Invulnerable, 1f),
        ]),
        [ModifierPackageType.Sticky] = new(true, 0.4f, [new(Multiply, ModifierEffect.JumpPower, 0.01f), new(Multiply, ModifierEffect.Speed, 0.03f), new(Override, ModifierEffect.Friction, 1f)]),
        [ModifierPackageType.SlowMat] = new(true, 0.4f, [new(Multiply, ModifierEffect.JumpPower, 0.1f), new(Multiply, ModifierEffect.Speed, 0.5f)]),
        [ModifierPackageType.SpeedMat] = new(true, 0.4f, [new(Multiply, ModifierEffect.Speed, 3f), new(Multiply, ModifierEffect.ThrustFactor, 20f)]),
        [ModifierPackageType.CrumbleMat] = new(true, 0.4f, [new(Override, ModifierEffect.CrumbleMat, 20f)]),
        [ModifierPackageType.Poison] = new(true, float.PositiveInfinity, [new(Addition, ModifierEffect.PoisonDamagePrSec, 25f)]),
        [ModifierPackageType.HealingMat] = new(true, 0.4f, [new(Addition, ModifierEffect.EnvironmentHealingPrSec, 2f), new(Addition, ModifierEffect.OverHeal, 2f)],
            new() { [ModifierPackageType.Poison] = ModifierAction.Replace }),
        [ModifierPackageType.Lethal] = new(true, float.PositiveInfinity, [new(Addition, ModifierEffect.LethalDamagePrSec, 500f)]),
        [ModifierPackageType.InstantDeath] = new(true, float.PositiveInfinity, [new(Addition, ModifierEffect.EnvironmentDamagePrSec, 150f)]),
        [ModifierPackageType.WallJump] = new(true, 0.2f, [new(Override, ModifierEffect.WallJump, 1f)]),
        [ModifierPackageType.NoFriction] = new(true, 0.2f, [new(Override, ModifierEffect.Friction, 0f)]),
        [ModifierPackageType.FlamerBurn] = new(true, 0.5f, [new(Addition, ModifierEffect.FlamerDamagePrSec, 25f)]),
        [ModifierPackageType.Underwater] = new(true, 10f, [new(Multiply, ModifierEffect.Density, 0.5f), new(Multiply, ModifierEffect.VelocityDamping, 0.95f)]),
        [ModifierPackageType.Frozen] = new(true, 4.2f, [new(Override, ModifierEffect.Friction, 0f), new(Multiply, ModifierEffect.JumpPower, 0.1f), new(Multiply, ModifierEffect.Speed, 0.4f)]),
        [ModifierPackageType.NinjaRun] = new(true, 7f, [new(Multiply, ModifierEffect.Speed, 3f), new(Multiply, ModifierEffect.ThrustFactor, 20f)]),
        [ModifierPackageType.TimeAttackFlagDebriefSlow] = new(true, 7f, [new(Multiply, ModifierEffect.Speed, 0.3f)]),
        [ModifierPackageType.Shrunken] = new(true, 35f,
        [
            new(Multiply, ModifierEffect.Speed, 0.4f), new(Override, ModifierEffect.Scale, 0.25f), new(Multiply, ModifierEffect.JumpPower, 0.6f),
            new(Multiply, ModifierEffect.DamageMultiplier, 4f), new(Override, ModifierEffect.DisableWeapons, 1f), new(Override, ModifierEffect.DisableVehicles, 1f),
            new(Override, ModifierEffect.Weight, 0.25f),
        ], new() { [ModifierPackageType.Shrunken] = ModifierAction.Renew, [ModifierPackageType.Enlarged] = ModifierAction.CancelOut }),
        [ModifierPackageType.WindFriction] = new(true, float.PositiveInfinity, [new(Override, ModifierEffect.VelocityDamping, 0.96f)]),
        [ModifierPackageType.Shielded] = new(true, float.PositiveInfinity, [new(Override, ModifierEffect.DamageMultiplier, 0.5f)], Persistent: true),
        [ModifierPackageType.DisableVehiclePickup] = new(true, float.PositiveInfinity, [new(Override, ModifierEffect.DisablePickups, 1f)]),
        [ModifierPackageType.Enlarged] = new(true, 35f,
        [
            new(Multiply, ModifierEffect.Speed, 1.5f), new(Override, ModifierEffect.Scale, 2f), new(Multiply, ModifierEffect.JumpPower, 2f),
            new(Multiply, ModifierEffect.DamageMultiplier, 0.5f), new(Override, ModifierEffect.DisableWeapons, 1f), new(Override, ModifierEffect.DisableVehicles, 1f),
            new(Override, ModifierEffect.Weight, 2f),
        ], new() { [ModifierPackageType.Shrunken] = ModifierAction.CancelOut, [ModifierPackageType.Enlarged] = ModifierAction.Renew }),
        [ModifierPackageType.SpawnProtection] = new(true, 4f, [new(Override, ModifierEffect.Invulnerable, 1f)],
            new() { [ModifierPackageType.SpawnProtection] = ModifierAction.Renew }, Persistent: true),
        [ModifierPackageType.RayHeal] = new(false, 0.2f, [new(Addition, ModifierEffect.EnvironmentHealingPrSec, 20f), new(Addition, ModifierEffect.OverHeal, 20f)],
            new() { [ModifierPackageType.Poison] = ModifierAction.Replace, [ModifierPackageType.Fire] = ModifierAction.Replace }),
        [ModifierPackageType.RayHealEnemy] = new(true, 1f, [new(Addition, ModifierEffect.RayHealEnemyDamagePrSec, 7f)]),
    };

    static readonly string[] Names = [.. Enum.GetNames<ModifierPackageType>().Select(name => "_" + name)];

    sealed class Package(ModifierPackageType type, int id, PackageDefinition definition, Modifier[] modifiers, double stamp)
    {
        public ModifierPackageType Type => type;
        public int Id => id;
        public PackageDefinition Definition => definition;
        public Modifier[] Modifiers => modifiers;
        public double Stamp { get; set; } = stamp;
        public bool Expired { get; set; }
    }

    readonly List<Package> _packages = [];
    readonly Dictionary<ModifierPackageType, byte> _keys = [];
    double _time;

    public IEnumerable<(string Key, byte Renewals)> RuntimeKeys => _keys.Select(pair => (Names[(int)pair.Key], pair.Value));
    public IReadOnlyList<ModifierPackageType> Active { get; private set; } = [];
    public bool Changed { get; set; }

    public void Add(ModifierPackageType type, int id = -1, Modifier[]? extra = null)
    {
        if (!Definitions.ContainsKey(type)) return;
        if (type is ModifierPackageType.Poison or ModifierPackageType.InstantDeath && Has(ModifierEffect.PoisonImmune)) return;

        switch (ActionFor(type))
        {
            case ModifierAction.Add when !_keys.ContainsKey(type):
                AddPackage(type, id, extra);
                SetKey(type, 0);
                break;
            case ModifierAction.Renew when _keys.TryGetValue(type, out byte renewals):
                AddPackage(type, id, extra);
                SetKey(type, (byte)(renewals + 1));
                break;
            case ModifierAction.Replace when !_keys.ContainsKey(type):
                AddPackage(type, id, extra);
                Remove(PackageToActWith(type, ModifierAction.Replace));
                SetKey(type, 0);
                break;
            case ModifierAction.CancelOut:
                Remove(PackageToActWith(type, ModifierAction.CancelOut), id);
                break;
        }
    }

    public void Remove(ModifierPackageType type, int id = -1)
    {
        if (!_keys.Remove(type)) return;
        KeysChanged();
        RemovePackage(type, id);
    }

    public bool Has(ModifierPackageType type) => _packages.Exists(package => package.Type == type);

    public bool Has(ModifierEffect effect) => _packages.Exists(package => package.Modifiers.Any(modifier => modifier.Effect == effect));

    public float Apply(ModifierEffect effect, float value) => _packages.Count == 0 ? value : Fold(_packages, effect, value);

    public Dictionary<int, float> ApplyById(ModifierEffect effect, float value)
    {
        var values = new Dictionary<int, float>();
        foreach (IGrouping<int, Package> group in _packages.Where(package => package.Modifiers.Any(modifier => modifier.Effect == effect)).GroupBy(package => package.Id))
            values[group.Key] = Fold(group, effect, value);
        return values;
    }

    public void Update(float deltaTime)
    {
        _time += deltaTime;
        for (int i = _packages.Count - 1; i >= 0; i--)
        {
            Package package = _packages[i];
            if (!package.Expired && package.Definition.Duration > _time - package.Stamp) continue;
            Remove(package.Type, package.Id);
            _packages.RemoveAt(i);
        }
    }

    public void Clear()
    {
        for (int i = _packages.Count - 1; i >= 0; i--)
        {
            Package package = _packages[i];
            if (package.Definition.Persistent) continue;
            Remove(package.Type, package.Id);
            _packages.RemoveAt(i);
        }
    }

    void AddPackage(ModifierPackageType type, int id, Modifier[]? extra)
    {
        PackageDefinition definition = Definitions[type];
        Modifier[] modifiers = extra is null ? definition.Modifiers : [.. definition.Modifiers.Union(extra)];
        if (!ApplyActions(definition)) return;

        if (definition.Renews && _packages.Find(package => package.Type == type && package.Id == id) is { } same)
        {
            same.Stamp = _time;
            same.Expired = false;
            return;
        }
        _packages.Add(new Package(type, id, definition, modifiers, _time));
    }

    bool ApplyActions(PackageDefinition added)
    {
        if (added.Actions is null) return true;
        bool keep = true;
        foreach (Package package in _packages)
        {
            if (!added.Actions.TryGetValue(package.Type, out ModifierAction action)) continue;
            if (action is ModifierAction.Replace or ModifierAction.CancelOut) RemovePackage(package.Type, package.Id);
            if (action == ModifierAction.CancelOut) keep = false;
        }
        return keep;
    }

    void RemovePackage(ModifierPackageType type, int id)
    {
        if (type == ModifierPackageType.None) return;
        List<Package> matches = _packages.FindAll(package => package.Type == type && (id == -1 || package.Id == id));
        if (id == -1 && matches.Count != 1 || matches.Count == 0) return;
        matches[0].Expired = true;
    }

    ModifierAction ActionFor(ModifierPackageType type)
    {
        foreach (Package package in _packages)
            if (package.Definition.Actions?.TryGetValue(type, out ModifierAction action) == true) return action;
        return ModifierAction.Add;
    }

    static ModifierPackageType PackageToActWith(ModifierPackageType type, ModifierAction action)
    {
        if (Definitions.TryGetValue(type, out PackageDefinition? definition) && definition.Actions is { } actions)
            foreach ((ModifierPackageType other, ModifierAction found) in actions)
                if (found == action) return other;
        return ModifierPackageType.None;
    }

    void SetKey(ModifierPackageType type, byte renewals)
    {
        _keys[type] = renewals;
        KeysChanged();
    }

    void KeysChanged()
    {
        Changed = true;
        Active = [.. _keys.Keys];
    }

    static float Fold(IEnumerable<Package> packages, ModifierEffect effect, float value)
    {
        List<Modifier> modifiers = [.. packages.SelectMany(package => package.Modifiers).Where(modifier => modifier.Effect == effect)];
        modifiers.Sort((x, y) => x.Type.CompareTo(y.Type));
        foreach (Modifier modifier in modifiers)
            value = modifier.Type switch
            {
                ModifierType.Multiply => value * modifier.Value(),
                ModifierType.Addition => value + modifier.Value(),
                _ => modifier.Value(),
            };
        return value;
    }
}
