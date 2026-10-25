using System.Numerics;

namespace OpenKogama.Physics;

sealed class WaterState
{
    public const float DefaultMaxOxygen = 20f;

    const float SuffocationDamage = 5f;
    const float AvatarHeight = 2.1f;
    const float SwimProximity = 0.6f;
    const float DeepProximity = 0.7f;

    readonly Modifier[] _underwater;
    CollisionWorld? _world;
    Vector3 _position;
    float _oxygen = DefaultMaxOxygen;
    float _maxOxygen = DefaultMaxOxygen;

    public WaterState() => _underwater =
    [
        new(ModifierType.Multiply, ModifierEffect.Speed, () => (1f - Proximity(_world, _position)) * 0.3f + 0.7f),
        new(ModifierType.Multiply, ModifierEffect.JumpPower, () => Proximity(_world, _position) > DeepProximity ? 2f : 1f),
    ];

    public bool Swimming { get; private set; }

    public float Oxygen
    {
        get => _oxygen;
        set => _oxygen = Math.Clamp(value, 0f, _maxOxygen);
    }

    public float MaxOxygen
    {
        get => _maxOxygen;
        set
        {
            _maxOxygen = MathF.Max(0f, value);
            _oxygen = MathF.Min(_oxygen, _maxOxygen);
        }
    }

    public static float Proximity(CollisionWorld? world, Vector3 position) =>
        world?.WaterLevel is float level ? Math.Clamp((level - position.Y) / AvatarHeight, 0f, 1f) : 0f;

    public float Update(CollisionWorld world, Vector3 position, ModifierPackages modifiers)
    {
        _world = world;
        _position = position;
        if (world.WaterLevel is float level && position.Y < level)
        {
            modifiers.Add(ModifierPackageType.Underwater, extra: _underwater);
            if (world.LethalWater) modifiers.Add(ModifierPackageType.Lethal);
        }
        else if (modifiers.Has(ModifierPackageType.Underwater))
        {
            modifiers.Remove(ModifierPackageType.Underwater);
        }

        Swimming = Proximity(world, position) >= SwimProximity;
        if (!Swimming)
        {
            _oxygen = _maxOxygen;
            return 0f;
        }
        _oxygen = MathF.Max(0f, _oxygen - AvatarMotor.FixedDeltaTime);
        return _oxygen <= 0f ? AvatarMotor.FixedDeltaTime * SuffocationDamage : 0f;
    }

    public void Refill() => _oxygen = _maxOxygen;
}
