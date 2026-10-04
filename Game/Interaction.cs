using System.Numerics;
using OpenKogama.Handlers;
using OpenKogama.World;

namespace OpenKogama.Game;

public enum InteractionType : byte
{
    None = 0,
    ProjectileStandard = 1,
    ImpulseGunHit = 2,
    RailGunHit = 4,
    SwordHit = 5,
    MutantHit = 6,
    ShotgunHit = 7,
    FlamethrowerHit = 8,
    CenterGun = 9,
    SentryTowerFire = 10,
    SentryTowerIce = 11,
    AdvancedGhostBodyRotateWeapon = 12,
    ProximityDamageAndImpulse = 13,
    SixShooterHit = 14,
    ThrowingStarHit = 15,
    MouseGunHit = 16,
    GrowthGunHit = 17,
    IceGunHit = 18,
    GodzillaLaserHit = 19,
    MultiThrowingStarHit = 24,
    DoubleSixShooterHit = 25,
    SlapGunHit = 26,
}

public enum KilledBy : byte
{
    None, CenterGun, BazookaGun, RailGun, Suicide, Impact, Environmental, Sword, Explosive, Fire, FallOffWorld, Mutant, Shotgun,
    FlameThrower, Crushed, Ghost, AdvancedGhost, SixShooter, DoubleSixShooter, ThrowingStar, MultiThrowingStar, GodzillaLaser, KillZone, SlapGun,
}

public readonly record struct Interaction(InteractionType Type, float Damage, Vector3 Impulse, KilledBy KilledBy)
{
    const byte HasType = 1;
    const byte HasDamage = 2;
    const byte HasImpulse = 4;
    const byte HasKilledBy = 8;

    static readonly Dictionary<InteractionType, float> SharedDamage = new()
    {
        [InteractionType.CenterGun] = 11.5f,
        [InteractionType.MutantHit] = 110f,
        [InteractionType.RailGunHit] = 100f,
        [InteractionType.ShotgunHit] = 13f,
        [InteractionType.SixShooterHit] = 12.5f,
        [InteractionType.DoubleSixShooterHit] = 12.5f,
        [InteractionType.SwordHit] = 15f,
        [InteractionType.ThrowingStarHit] = 15f,
        [InteractionType.MultiThrowingStarHit] = 7.5f,
        [InteractionType.SlapGunHit] = 35f,
    };

    public byte[] ToBytes()
    {
        var writer = new BytePackerWriter();
        byte flags = HasType;
        writer.WriteByte(flags);
        writer.WriteByte((byte)Type);
        if (Damage != 0f)
        {
            flags |= HasDamage;
            writer.WriteSingle(Damage);
        }
        if (Impulse.LengthSquared() > 1E-05f)
        {
            flags |= HasImpulse;
            writer.WriteSingle(Impulse.X);
            writer.WriteSingle(Impulse.Y);
            writer.WriteSingle(Impulse.Z);
        }
        if (KilledBy != KilledBy.None)
        {
            flags |= HasKilledBy;
            writer.WriteByte((byte)KilledBy);
        }
        byte[] packet = writer.ToArray();
        packet[0] = flags;
        return packet;
    }

    public static List<Interaction> Parse(object? rpcData)
    {
        var interactions = new List<Interaction>();
        if (PhotonValues.Normalize(rpcData) is not Dictionary<object, object?> table) return interactions;
        foreach ((object key, object? value) in table)
            if (Convert.ToInt32(key) == 0 && value is byte[] { Length: > 0 } packet && Read(packet) is Interaction interaction)
                interactions.Add(interaction);
        return interactions;
    }

    static Interaction? Read(byte[] packet)
    {
        try
        {
            var reader = new BytePackerReader(packet);
            byte flags = reader.ReadByte();
            var type = (flags & HasType) != 0 ? (InteractionType)reader.ReadByte() : InteractionType.None;
            float damage = (flags & HasDamage) != 0 ? reader.ReadSingle() : SharedDamage.GetValueOrDefault(type);
            Vector3 impulse = (flags & HasImpulse) != 0 ? new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()) : Vector3.Zero;
            var killedBy = (flags & HasKilledBy) != 0 ? (KilledBy)reader.ReadByte() : KilledBy.None;
            return new Interaction(type, damage, impulse, killedBy);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
        catch (IndexOutOfRangeException)
        {
            return null;
        }
    }
}
