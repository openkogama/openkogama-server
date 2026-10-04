using System.Numerics;
using OpenKogama.Handlers.Operations;
using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.Physics;
using OpenKogama.World;

namespace OpenKogama.Game;

public sealed partial class Session
{
    const int NpcSendIntervalMs = 200;
    const int CollisionRefreshMs = 1000;
    const int FirstNpcActor = 10_000;
    const float MaxCatchUpSeconds = 0.25f;
    const int DeadToHiddenMs = 2500;
    const int NoFrictionMs = 200;
    const int BurnMs = 500;
    const float BurnDamagePerSecond = 25f;
    const float FallBelowWorld = 200f;
    const int KillNotification = 1;
    const int AvatarKilledMessage = 0;
    const int ResizeDelayMs = 1000;
    const int SizeRenewMs = 20_000;
    const float AwayHeight = 5000f;
    const float FireHeight = 1f;
    const int FirePulseMs = 100;

    static readonly HashSet<KilledBy> SilentKills = [KilledBy.Environmental, KilledBy.Crushed, KilledBy.FallOffWorld, KilledBy.Impact];

    enum TransformPackage : byte { Teleport, Interpolate, Stop }

    readonly List<Npc> _npcs = [];
    int _nextNpcActor = FirstNpcActor;
    CollisionWorld? _collision;
    long _collisionBuilt;

    public IReadOnlyList<Npc> Npcs
    {
        get { lock (_npcs) return [.. _npcs]; }
    }

    public Npc AddNpc(string name, float[] position, float yaw, List<AvatarPart>? parts, List<WornAccessory>? accessories, float size = 1f, int level = 1)
    {
        int actor = Interlocked.Increment(ref _nextNpcActor);
        List<WorldObject> avatar = Avatar.Build(World, actor, World.RootId, _avatarPrototypes);
        var npc = new Npc(actor, avatar[0].Id, name, [.. position], yaw) { Size = Math.Clamp(size, Npc.MinSize, Npc.MaxSize), Level = Math.Max(1, level) };
        float avatarScale = npc.AvatarScale;
        avatar[0].Position = [.. position];
        avatar[0].Rotation = npc.Rotation;
        avatar[0].Scale = [avatarScale, avatarScale, avatarScale];
        avatar[0].SetRuntime("size", PackedType.Single, avatarScale);
        avatar[0].SetRuntime("maxHealth", PackedType.Int32, (int)Npc.DefaultMaxHealth);
        foreach (WorldObject obj in avatar) World.Add(obj);
        if (BodyOf(npc.AvatarId) is int body and >= 0)
        {
            if (parts is { Count: > 0 }) Avatar.UseParts(World, body, parts);
            if (accessories is { Count: > 0 }) Avatar.WearAccessories(World, body, accessories);
            World.Modify(body, obj => obj.Scale = [1f / avatarScale, 1f / avatarScale, 1f / avatarScale]);
        }
        lock (_npcs) _npcs.Add(npc);

        bool Audience(Player player) => player.InWorld && player.SeesNpcs;
        foreach (Player player in _players.Where(Audience))
            Introduce(player, npc);
        GetNextGameBatch.SendAdded(this, actor, NpcsAway(NpcSnapshot(npc)), Audience);

        Console.WriteLine($"npc {actor}: {name} spawned");
        return npc;
    }

    public bool RemoveNpc(Npc npc)
    {
        lock (_npcs)
            if (!_npcs.Remove(npc)) return false;
        World.RemoveTree(npc.AvatarId);

        var unregister = new EventData((byte)EventCode.UnregisterWorldObject)
        {
            Parameters = { [(byte)ParameterKey.WorldObjectID] = npc.AvatarId },
        };
        var leave = new EventData((byte)EventCode.Leave)
        {
            Parameters = { [(byte)ParameterKey.ActorNr] = npc.Actor },
        };
        foreach (Player player in _players.Where(player => player.Saw(npc.Actor)))
        {
            player.Peer.Send(unregister);
            player.Peer.Send(leave);
        }
        Console.WriteLine($"npc {npc.Actor}: {npc.Name} removed");
        return true;
    }

    public void Introduce(Player player, Npc npc)
    {
        var join = new EventData((byte)EventCode.Join)
        {
            Parameters = { [(byte)ParameterKey.ActorNr] = npc.Actor },
        };
        foreach ((ParameterKey key, object value) in npc.Info()) join.Parameters[(byte)key] = value;
        player.Peer.Send(join);
        player.Sees(npc.Actor);
        lock (npc.SeenAt)
        {
            npc.SeenAt[player] = Environment.TickCount64;
            npc.Resized.Remove(player);
        }
        if (player.SpawnRoles)
            player.Peer.Send(new EventData((byte)EventCode.ReplicateSpawnRoleData)
            {
                Parameters = { [(byte)ParameterKey.ActorNr] = npc.Actor, [(byte)ParameterKey.Data] = npc.SpawnRoleData() },
            });
    }

    public Snapshot NpcsAway(Snapshot snapshot)
    {
        HashSet<int> away;
        lock (_npcs) away = [.. _npcs.Where(npc => npc.SizeModifier is not null).Select(npc => npc.AvatarId)];
        if (away.Count == 0) return snapshot;
        return snapshot with { Objects = [.. snapshot.Objects.Select(obj => away.Contains(obj.Id) ? Away(obj) : obj)] };
    }

    static WorldObject Away(WorldObject obj) => new()
    {
        Id = obj.Id,
        ParentId = obj.ParentId,
        ItemId = obj.ItemId,
        Type = obj.Type,
        Position = [obj.Position[0], obj.Position[1] + AwayHeight, obj.Position[2]],
        Rotation = obj.Rotation,
        Scale = obj.Scale,
        Data = obj.Data,
        Owner = obj.Owner,
        PreviewOwner = obj.PreviewOwner,
        Runtime = obj.Runtime,
        Transient = obj.Transient,
    };

    public Snapshot WithoutNpcs(Snapshot snapshot)
    {
        HashSet<int> actors;
        lock (_npcs)
        {
            if (_npcs.Count == 0) return snapshot;
            actors = [.. _npcs.Select(npc => npc.Actor)];
        }
        return snapshot with { Objects = [.. snapshot.Objects.Where(obj => obj.Owner is not int owner || !actors.Contains(owner))] };
    }

    public void TickNpcs()
    {
        Npc[] npcs;
        lock (_npcs)
        {
            if (_npcs.Count == 0) return;
            npcs = [.. _npcs];
        }

        long now = Environment.TickCount64;
        if (_collision is null || now - _collisionBuilt >= CollisionRefreshMs)
        {
            _collision = CollisionWorld.Build(World);
            _collisionBuilt = now;
        }

        foreach (Npc npc in npcs)
        {
            Combat(npc, _collision, now);
            if (npc.Mode == NpcMode.Hidden) npc.LastStep = now;
            else Simulate(npc, _collision, now);
            bool urgent = npc.Teleported || npc.StateChanged;
            if (urgent || now - npc.LastSend >= NpcSendIntervalMs) SendNpc(npc, now);
            SendSize(npc, now);
            SendWeapon(npc, now);
        }
    }

    bool Placed(Npc npc, Player player)
    {
        lock (npc.SeenAt) return npc.SizeModifier is null || npc.Resized.Contains(player);
    }

    void SendWeapon(Npc npc, long now)
    {
        if (!npc.HeldItemChanged && !npc.Firing && npc.Shots.IsEmpty) return;
        Player[] audience = [.. _players.Where(player => player.InWorld && player.Saw(npc.Actor) && Placed(npc, player))];

        if (npc.HeldItemChanged)
        {
            npc.HeldItemChanged = false;
            var held = new Dictionary<object, object?>
            {
                ["currentItem"] = new Dictionary<object, object?> { ["type"] = npc.HeldItem ?? (int)AvatarItemType.Hand },
            };
            World.Modify(npc.AvatarId, obj => PackedData.Merge(obj.Runtime, held));
            foreach (Player player in audience) player.Peer.Send(RuntimeEvent(npc, held));
        }

        if (npc.Firing)
        {
            if (now < npc.FiringUntil) return;
            npc.Firing = false;
            foreach (Player player in audience) player.Peer.Send(RuntimeEvent(npc, new Dictionary<object, object?> { ["isFiring"] = false }));
        }

        if (!npc.Shots.TryDequeue(out Vector3 direction)) return;
        var line = new EventData((byte)EventCode.UpdateLineOfFire)
        {
            Parameters =
            {
                [(byte)ParameterKey.WorldObjectID] = npc.AvatarId,
                [(byte)ParameterKey.CamOriginX] = npc.Position[0],
                [(byte)ParameterKey.CamOriginY] = npc.Position[1] + FireHeight * npc.Size,
                [(byte)ParameterKey.CamOriginZ] = npc.Position[2],
                [(byte)ParameterKey.CamDirX] = direction.X,
                [(byte)ParameterKey.CamDirY] = direction.Y,
                [(byte)ParameterKey.CamDirZ] = direction.Z,
                [(byte)ParameterKey.ActorNr] = npc.Actor,
            },
        };
        foreach (Player player in audience)
        {
            player.Peer.Send(line);
            player.Peer.Send(RuntimeEvent(npc, new Dictionary<object, object?> { ["isFiring"] = true }));
        }
        npc.Firing = true;
        npc.FiringUntil = now + FirePulseMs;
    }

    void SendSize(Npc npc, long now)
    {
        if (npc.SizeModifier is null) return;
        bool renew = now - npc.SizeRenewedAt >= SizeRenewMs;
        if (renew)
        {
            npc.SizeRenewal++;
            npc.SizeRenewedAt = now;
        }
        lock (npc.SeenAt)
        {
            foreach (Player player in _players.Where(player => player.InWorld && player.Saw(npc.Actor)))
            {
                if (npc.Resized.Contains(player) ? !renew : !npc.SeenAt.TryGetValue(player, out long seen) || now - seen < ResizeDelayMs) continue;
                npc.Resized.Add(player);
                player.Peer.Send(RuntimeEvent(npc, new Dictionary<object, object?> { ["modifiers"] = ModifiersOf(npc, resized: true) }));
                player.Peer.Send(TransformEvent(npc, [.. npc.Position], TransformPackage.Teleport, player.Peer.ServerTime));
            }
        }
    }

    static Dictionary<object, object?> ModifiersOf(Npc npc, bool resized)
    {
        Dictionary<object, object?> modifiers = npc.Modifiers.ToDictionary(pair => (object)pair.Key, pair => (object?)pair.Value);
        if (resized && npc.SizeModifier is string modifier) modifiers[modifier] = npc.SizeRenewal;
        return modifiers;
    }

    static EventData TransformEvent(Npc npc, float[] position, TransformPackage type, int time) => new((byte)EventCode.UpdateWorldObject)
    {
        Parameters =
        {
            [(byte)ParameterKey.WorldObjectID] = npc.AvatarId,
            [(byte)ParameterKey.Timestamp] = time,
            [(byte)ParameterKey.PosX] = position[0],
            [(byte)ParameterKey.PosY] = position[1],
            [(byte)ParameterKey.PosZ] = position[2],
            [(byte)ParameterKey.ByteRotation] = npc.ByteRotation,
            [(byte)ParameterKey.TransformPackageType] = (byte)type,
            [(byte)ParameterKey.ActorNr] = npc.Actor,
        },
    };

    static EventData RuntimeEvent(Npc npc, Dictionary<object, object?> changes) => new((byte)EventCode.UpdateWorldObjectRunTimeData)
    {
        Parameters =
        {
            [(byte)ParameterKey.WorldObjectID] = npc.AvatarId,
            [(byte)ParameterKey.WorldObjectRunTimeData] = changes,
            [(byte)ParameterKey.ActorNr] = npc.Actor,
        },
    };

    public Npc? NpcByAvatar(int avatarId)
    {
        lock (_npcs) return _npcs.FirstOrDefault(npc => npc.AvatarId == avatarId);
    }

    public bool HitNpc(int avatarId, int shooterActor, List<Interaction> interactions)
    {
        if (NpcByAvatar(avatarId) is not { } npc) return false;
        foreach (Interaction interaction in interactions) npc.Hits.Enqueue((shooterActor, interaction));
        return true;
    }

    void Combat(Npc npc, CollisionWorld collision, long now)
    {
        while (npc.Respawns.TryDequeue(out Vector3 position)) Respawn(npc, position);
        while (npc.Damages.TryDequeue(out (float Amount, int Actor) damage))
            Damage(npc, damage.Amount, damage.Actor, damage.Actor == npc.Actor ? KilledBy.Environmental : KilledBy.None, now);
        while (npc.Hits.TryDequeue(out (int Actor, Interaction Interaction) hit)) Hit(npc, hit.Actor, hit.Interaction, now);

        float seconds = MathF.Min((now - npc.LastStep) / 1000f, MaxCatchUpSeconds);
        if (now < npc.BurnUntil) Damage(npc, BurnDamagePerSecond * seconds, npc.BurnActor, KilledBy.FlameThrower, now);
        if (now >= npc.NoFrictionUntil) RemoveModifier(npc, "_NoFriction");
        if (npc.OnFire && !npc.Modifiers.ContainsKey("_Fire")) AddModifier(npc, "_Fire");
        if (!npc.OnFire) RemoveModifier(npc, "_Fire");
        if (now >= npc.BurnUntil) RemoveModifier(npc, "_FlamerBurn");

        if (npc.Mode == NpcMode.Dead && now - npc.DiedAt >= DeadToHiddenMs)
        {
            npc.Mode = NpcMode.Hidden;
            npc.ModeChanged = true;
        }

        if (npc.Alive && collision.TerrainBottom is float bottom && npc.Position[1] < bottom - FallBelowWorld)
        {
            npc.Health = 0f;
            npc.HealthChanged = true;
            Die(npc, npc.Actor, KilledBy.FallOffWorld, now);
        }
    }

    void Hit(Npc npc, int actor, Interaction interaction, long now)
    {
        if (!npc.Alive) return;
        switch (interaction.Type)
        {
            case InteractionType.ImpulseGunHit:
                Push(npc, interaction.Impulse, now, noFriction: true);
                break;
            case InteractionType.SwordHit:
                Damage(npc, interaction.Damage, actor, KilledBy.Sword, now);
                Push(npc, interaction.Impulse, now, noFriction: true);
                break;
            case InteractionType.RailGunHit:
                Damage(npc, interaction.Damage, actor, KilledBy.RailGun, now);
                break;
            case InteractionType.MutantHit:
                Damage(npc, interaction.Damage, actor, KilledBy.Mutant, now);
                break;
            case InteractionType.FlamethrowerHit:
                AddModifier(npc, "_FlamerBurn");
                npc.BurnUntil = now + BurnMs;
                npc.BurnActor = actor;
                break;
            case InteractionType.SentryTowerFire:
            case InteractionType.SentryTowerIce:
                Push(npc, interaction.Impulse, now, noFriction: false);
                break;
            case InteractionType.ShotgunHit:
            case InteractionType.CenterGun:
            case InteractionType.SixShooterHit:
            case InteractionType.DoubleSixShooterHit:
            case InteractionType.ThrowingStarHit:
            case InteractionType.MultiThrowingStarHit:
            case InteractionType.SlapGunHit:
            case InteractionType.ProximityDamageAndImpulse:
                Damage(npc, interaction.Damage, actor, KilledByOf(interaction), now);
                Push(npc, interaction.Impulse, now, noFriction: false);
                break;
        }
    }

    static KilledBy KilledByOf(Interaction interaction) => interaction.Type switch
    {
        InteractionType.ShotgunHit => KilledBy.Shotgun,
        InteractionType.CenterGun => KilledBy.CenterGun,
        InteractionType.SixShooterHit => KilledBy.SixShooter,
        InteractionType.DoubleSixShooterHit => KilledBy.DoubleSixShooter,
        InteractionType.ThrowingStarHit => KilledBy.ThrowingStar,
        InteractionType.MultiThrowingStarHit => KilledBy.MultiThrowingStar,
        InteractionType.SlapGunHit => KilledBy.SlapGun,
        _ => interaction.KilledBy,
    };

    static void Push(Npc npc, Vector3 impulse, long now, bool noFriction)
    {
        npc.Motor?.AddImpulse(impulse);
        if (!noFriction) return;
        AddModifier(npc, "_NoFriction");
        npc.NoFrictionUntil = now + NoFrictionMs;
    }

    static void AddModifier(Npc npc, string key)
    {
        npc.Modifiers[key] = npc.Modifiers.TryGetValue(key, out byte renewed) ? (byte)(renewed + 1) : (byte)0;
        npc.ModifiersChanged = true;
    }

    static void RemoveModifier(Npc npc, string key)
    {
        if (npc.Modifiers.Remove(key)) npc.ModifiersChanged = true;
    }

    void Damage(Npc npc, float amount, int actor, KilledBy killedBy, long now)
    {
        if (!npc.Alive || amount <= 0f) return;
        float before = npc.Health;
        npc.Health = Math.Clamp(before - amount, 0f, npc.MaxHealth);
        npc.HealthChanged = true;
        Plugins.PluginHost.NpcDamaged(this, npc, PlayerOf(actor), amount);
        if (npc.Health <= 0f && before > 0f) Die(npc, actor, killedBy, now);
    }

    void Die(Npc npc, int killerActor, KilledBy killedBy, long now)
    {
        npc.Mode = NpcMode.Dead;
        npc.ModeChanged = true;
        npc.DiedAt = now;
        npc.Target = null;
        npc.JumpUntil = 0;

        var message = new EventData((byte)EventCode.PostGameMsg)
        {
            Parameters =
            {
                [(byte)ParameterKey.GameMsgType] = AvatarKilledMessage,
                [(byte)ParameterKey.GameMsgData] = new Dictionary<object, object?> { [(byte)0] = npc.Actor, [(byte)1] = killerActor, [(byte)2] = (byte)killedBy },
            },
        };
        var notification = new EventData((byte)EventCode.NotificationEvent)
        {
            Parameters =
            {
                [(byte)ParameterKey.NotificationType] = KillNotification,
                [(byte)ParameterKey.NotificationData] = new Dictionary<object, object?> { [(byte)7] = npc.Actor, [(byte)6] = killerActor, [(byte)8] = (byte)killedBy },
            },
        };
        foreach (Player player in _players.Where(player => player.Saw(npc.Actor)))
        {
            player.Peer.Send(message);
            if (!SilentKills.Contains(killedBy) && player.Peer.Translator is Kogama.Protocols.OperationRemap)
                player.Peer.Send(notification);
        }

        Player? killer = killerActor == npc.Actor ? null : PlayerOf(killerActor);
        Console.WriteLine($"npc {npc.Actor}: {npc.Name} killed by {killer?.Username ?? "nothing"} with {killedBy}");
        Plugins.PluginHost.NpcKilled(this, npc, killer);
    }

    static void Respawn(Npc npc, Vector3 position)
    {
        npc.Health = npc.MaxHealth;
        npc.HealthChanged = true;
        npc.Mode = NpcMode.Playing;
        npc.ModeChanged = true;
        npc.Modifiers.Clear();
        npc.ModifiersChanged = true;
        npc.NoFrictionUntil = 0;
        npc.BurnUntil = 0;
        npc.Target = null;
        npc.Position = [position.X, position.Y, position.Z];
        npc.Teleported = true;
    }

    Player? PlayerOf(int actor) => _players.FirstOrDefault(player => player.Actor == actor);

    public void DamagePlayer(Player victim, int attackerActor, float damage, Vector3 impulse, KilledBy killedBy)
    {
        var hit = new Interaction(InteractionType.ProximityDamageAndImpulse, damage, impulse, killedBy);
        victim.Peer.Send(new EventData((byte)EventCode.WorldObjectRPCEvent)
        {
            Parameters =
            {
                [(byte)ParameterKey.ActorNr] = attackerActor,
                [(byte)ParameterKey.WorldObjectID] = victim.PlayAvatar,
                [(byte)ParameterKey.WorldObjectRPCData] = new Dictionary<object, object?> { [(byte)0] = hit.ToBytes() },
            },
        });
    }

    Snapshot NpcSnapshot(Npc npc)
    {
        Snapshot avatar = World.SubtreeSnapshot(npc.AvatarId);
        return avatar with { Prototypes = [.. avatar.Prototypes.Where(prototype => !_avatarPrototypes.Contains(prototype.Id))] };
    }

    static void Simulate(Npc npc, CollisionWorld collision, long now)
    {
        npc.Motor ??= new AvatarMotor(collision, ToVector(npc.Position));
        AvatarMotor motor = npc.Motor;
        motor.World = collision;
        motor.WalkSpeed = npc.Speed;
        motor.Scale = npc.Size;
        motor.NoFriction = now < npc.NoFrictionUntil;
        if (npc.Teleported) motor.Teleport(ToVector(npc.Position));
        bool alive = npc.Alive;

        npc.Accumulator += MathF.Min((now - npc.LastStep) / 1000f, MaxCatchUpSeconds);
        npc.LastStep = now;

        bool walking = false;
        while (npc.Accumulator >= AvatarMotor.FixedDeltaTime)
        {
            npc.Accumulator -= AvatarMotor.FixedDeltaTime;
            Vector3 direction = Vector3.Zero;
            if (alive && npc.Target is { } target)
            {
                var away = new Vector3(target[0] - motor.Position.X, 0f, target[2] - motor.Position.Z);
                if (away.Length() <= Npc.ArriveDistance) npc.Target = null;
                else direction = Vector3.Normalize(away);
            }
            walking = direction != Vector3.Zero;
            if (walking) npc.Yaw = MathF.Atan2(direction.X, direction.Z) * 180f / MathF.PI;
            motor.Step(direction, alive && now < npc.JumpUntil);
        }

        Vector3 position = motor.Position;
        npc.Position = [position.X, position.Y, position.Z];
        npc.Animation = !alive ? "Dead" : npc.AnimationOverride ?? (motor.Jumping ? "Jump" : walking || npc.Target is not null ? "Walk" : "Idle");
    }

    static Vector3 ToVector(float[] value) => new(value[0], value[1], value[2]);

    void SendNpc(Npc npc, long now)
    {
        npc.LastSend = now;
        bool moved = npc.SentPosition is not { } last || !last.SequenceEqual(npc.Position) || npc.SentYaw != npc.Yaw;
        string animation = npc.Animation;
        bool animate = animation != npc.SentAnimation;
        bool state = npc.StateChanged;
        if (!moved && npc.StopSent && !animate && !state) return;

        float[] position = [.. npc.Position];
        float[] rotation = npc.Rotation;
        World.Modify(npc.AvatarId, obj =>
        {
            obj.Position = position;
            obj.Rotation = rotation;
        });

        Player[] audience = [.. _players.Where(player => player.InWorld && player.Saw(npc.Actor))];
        int time = audience.Length > 0 ? audience[0].Peer.ServerTime : 0;

        if (moved || !npc.StopSent)
        {
            TransformPackage type = npc.Teleported ? TransformPackage.Teleport : moved ? TransformPackage.Interpolate : TransformPackage.Stop;
            EventData transform = TransformEvent(npc, position, type, time);
            foreach (Player player in audience.Where(player => Placed(npc, player)))
                player.Peer.Send(transform, reliable: type != TransformPackage.Interpolate);
            npc.StopSent = !moved;
            npc.SentPosition = position;
            npc.SentYaw = npc.Yaw;
            npc.Teleported = false;
        }

        if (!animate && !state) return;
        bool modifiers = npc.ModifiersChanged;
        var changes = new Dictionary<object, object?>();
        if (animate) changes["animation"] = new Dictionary<object, object?> { ["state"] = animation, ["timeStamp"] = time };
        if (npc.HealthChanged) changes["health"] = npc.Health;
        if (npc.ModeChanged) changes["avatarModeTypes"] = (int)npc.Mode;
        if (modifiers) changes["modifiers"] = ModifiersOf(npc, resized: false);
        if (npc.MaxHealthChanged) changes["maxHealth"] = (int)MathF.Round(npc.MaxHealth);
        npc.HealthChanged = npc.ModeChanged = npc.ModifiersChanged = npc.MaxHealthChanged = false;
        World.Modify(npc.AvatarId, obj => PackedData.Merge(obj.Runtime, changes));

        EventData runtime = RuntimeEvent(npc, changes);
        EventData? resizedRuntime = modifiers && npc.SizeModifier is not null
            ? RuntimeEvent(npc, new Dictionary<object, object?>(changes) { ["modifiers"] = ModifiersOf(npc, resized: true) })
            : null;
        lock (npc.SeenAt)
            foreach (Player player in audience)
                player.Peer.Send(resizedRuntime is not null && npc.Resized.Contains(player) ? resizedRuntime : runtime);
        npc.SentAnimation = animation;
    }
}
