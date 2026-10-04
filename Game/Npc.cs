using System.Collections.Concurrent;
using System.Numerics;
using OpenKogama.Kogama;
using OpenKogama.Physics;

namespace OpenKogama.Game;

[Flags]
public enum NpcMode { Playing = 1, Dead = 2, Hidden = 4 }

public sealed class Npc(int actor, int avatarId, string name, float[] position, float yaw)
{
    public const int NoTeam = 5;
    public const float ArriveDistance = 0.5f;
    public const float DefaultMaxHealth = 100f;
    public const float MinSize = 0.01f;
    public const float MaxSize = 10f;
    const float ShrunkenSize = 0.25f;
    const float EnlargedSize = 2f;

    public int Actor => actor;
    public int AvatarId => avatarId;
    public string Name => name;
    public float[] Position { get; set; } = position;
    public float Yaw { get; set; } = yaw;
    public string Animation { get; set; } = "Idle";
    public string? AnimationOverride { get; set; }
    public float[]? Target { get; set; }
    public float Speed { get; set; } = AvatarMotor.DefaultWalkSpeed;
    public bool Teleported { get; set; }
    public long JumpUntil { get; set; }
    public bool Grounded => Motor?.Grounded ?? false;
    public float Health { get; internal set; } = DefaultMaxHealth;
    public float MaxHealth { get; internal set; } = DefaultMaxHealth;
    public float Size { get; internal set; } = 1f;
    public int Level { get; internal set; } = 1;
    public bool OnFire { get; set; }
    public string? SizeModifier => Size < 1f ? "_Shrunken" : Size > 1f ? "_Enlarged" : null;
    public float AvatarScale => Size == 1f ? 1f : (Size < 1f ? ShrunkenSize : EnlargedSize) / Size;
    public NpcMode Mode { get; internal set; } = NpcMode.Playing;
    public bool Alive => Mode == NpcMode.Playing;

    internal ConcurrentQueue<(int Actor, Interaction Interaction)> Hits { get; } = new();
    internal ConcurrentQueue<Vector3> Respawns { get; } = new();
    internal ConcurrentQueue<(float Amount, int Actor)> Damages { get; } = new();
    internal long DiedAt { get; set; }
    internal long NoFrictionUntil { get; set; }
    internal long BurnUntil { get; set; }
    internal int BurnActor { get; set; }
    internal Dictionary<string, byte> Modifiers { get; } = [];
    internal bool HealthChanged { get; set; }
    internal bool ModeChanged { get; set; }
    internal bool ModifiersChanged { get; set; }
    internal bool MaxHealthChanged { get; set; }
    internal int? HeldItem { get; set; }
    internal bool HeldItemChanged { get; set; }
    internal ConcurrentQueue<Vector3> Shots { get; } = new();
    internal bool Firing { get; set; }
    internal long FiringUntil { get; set; }
    internal bool StateChanged => HealthChanged || ModeChanged || ModifiersChanged || MaxHealthChanged;
    internal Dictionary<Player, long> SeenAt { get; } = [];
    internal HashSet<Player> Resized { get; } = [];
    internal byte SizeRenewal { get; set; }
    internal long SizeRenewedAt { get; set; }

    internal AvatarMotor? Motor { get; set; }
    internal float Accumulator { get; set; }
    internal float[]? SentPosition { get; set; }
    internal float SentYaw { get; set; } = float.NaN;
    internal string? SentAnimation { get; set; }
    internal bool StopSent { get; set; } = true;
    internal long LastStep { get; set; } = Environment.TickCount64;
    internal long LastSend { get; set; }

    public float[] Rotation => [0f, MathF.Sin(Yaw * MathF.PI / 360f), 0f, MathF.Cos(Yaw * MathF.PI / 360f)];

    public byte[] ByteRotation => [(byte)((int)MathF.Round(Normalized(Yaw) * 32f / 45f) & 0xFF), 0, 0];

    public IEnumerable<(ParameterKey Key, object Value)> Info() =>
    [
        (ParameterKey.ProfileID, 0),
        (ParameterKey.Username, Name),
        (ParameterKey.RegionCode, "en_US"),
        (ParameterKey.TeamID, NoTeam),
        (ParameterKey.Level, Level),
        (ParameterKey.ClientBuildTarget, (byte)0),
        (ParameterKey.IsActorReady, true),
        (ParameterKey.UserProfileData, System.Text.Json.JsonSerializer.Serialize(new
        {
            IsAdmin = false,
            UserName = Name,
            Gold = 0,
            SubscriptionData = new { SubscriptionType = 0 },
        })),
        (ParameterKey.PlayerPlanetData, """{"highScoreGamePoints":0,"gamePassTier":0}"""),
    ];

    public string SpawnRoleData() => System.Text.Json.JsonSerializer.Serialize(new
    {
        activeSpawnRole = AvatarId,
        spawnRoleAvatarIds = new[] { AvatarId },
    });

    static float Normalized(float degrees) => (degrees % 360f + 360f) % 360f;
}
