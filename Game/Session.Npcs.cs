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
    const float SightStep = 8f;
    static readonly Vector3 SightRadius = new(0.1f);
    const int FirstNpcActor = 10_000;
    const float MaxCatchUpSeconds = 0.25f;
    const int DeadToHiddenMs = 2500;
    const int LastAttackerMs = 4000;
    const float FallBelowWorld = 200f;
    const int KillNotification = 1;
    const int AvatarKilledMessage = 0;
    const int ResizeDelayMs = 1000;
    const int SizeRenewMs = 20_000;
    const float AwayHeight = 5000f;
    const int FirePulseMs = 100;

    static readonly HashSet<KilledBy> SilentKills = [KilledBy.Environmental, KilledBy.Crushed, KilledBy.FallOffWorld, KilledBy.Impact];

    readonly List<Npc> _npcs = [];
    int _nextNpcActor = FirstNpcActor;
    CollisionWorld? _collision;
    long _collisionBuilt;
    float _worldBottom;

    public IReadOnlyList<Npc> Npcs
    {
        get { lock (_npcs) return [.. _npcs]; }
    }

    public Npc AddNpc(string name, float[] position, float yaw, List<AvatarPart>? parts, List<WornAccessory>? accessories, float size = 1f, int level = 1)
    {
        int actor = Interlocked.Increment(ref _nextNpcActor);
        List<WorldObject> avatar = Avatar.Build(World, actor, World.RootId, _avatarPrototypes);
        var npc = new Npc(actor, avatar[0].Id, name, [.. position], yaw) { Size = Math.Clamp(size, Npc.MinSize, Npc.MaxSize), Level = Math.Max(1, level), Parts = parts, Accessories = accessories };
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
        foreach (Player player in Players.Where(Audience))
            Introduce(player, npc);
        GetNextGameBatch.SendAdded(this, actor, NpcsAway(NpcSnapshot(npc)), Audience);

        Console.WriteLine($"npc {actor}: {name} spawned");
        return npc;
    }

    public bool RemoveNpc(Npc npc)
    {
        lock (_npcs)
            if (!_npcs.Remove(npc)) return false;
        Colossus.Release(this, npc.AvatarId);
        World.RemoveTree(npc.AvatarId);

        var unregister = new EventData((byte)EventCode.UnregisterWorldObject)
        {
            Parameters = { [(byte)ParameterKey.WorldObjectID] = npc.AvatarId },
        };
        var leave = new EventData((byte)EventCode.Leave)
        {
            Parameters = { [(byte)ParameterKey.ActorNr] = npc.Actor },
        };
        foreach (Player player in Players.Where(player => player.Saw(npc.Actor)))
        {
            player.Peer.Send(unregister);
            player.Peer.Send(leave);
        }
        Console.WriteLine($"npc {npc.Actor}: {npc.Name} removed");
        return true;
    }

    public bool MakeColossus(Npc npc, WorldObject trigger)
    {
        if (npc.ColossusScale is not null || Colossus.Occupant(trigger) != Colossus.Empty) return false;
        npc.Position = [.. trigger.Position];
        npc.Teleported = true;
        MakeColossus(npc, Colossus.PackageOf(trigger));
        return Colossus.Enter(this, trigger, npc.AvatarId);
    }

    public bool MakeColossus(Npc npc, byte package)
    {
        if (npc.ColossusScale is not null) return false;
        npc.ColossusScale = Colossus.ScaleOf(package);
        npc.Speed = 0f;
        npc.Target = null;
        AddModifier(npc, Colossus.ModifierOf(package));
        npc.HeldItem = (int)AvatarItemType.GodzillaLaser;
        npc.HeldItemData = new Dictionary<object, object?> { ["avatarModifierPackageType"] = package };
        npc.HeldItemChanged = true;
        return true;
    }

    public bool LineOfSight(Vector3 from, Vector3 to)
    {
        Vector3 path = to - from;
        float distance = path.Length();
        if (distance < SightRadius.X) return true;

        CollisionWorld collision = Collision(Environment.TickCount64);
        Vector3 direction = path / distance;
        for (float start = 0f; start < distance; start += SightStep)
            if (collision.Cast(from + direction * start, direction, SightRadius, MathF.Min(SightStep, distance - start)) is not null)
                return false;
        return true;
    }

    CollisionWorld Collision(long now)
    {
        if (_collision is null || now - _collisionBuilt >= CollisionRefreshMs)
        {
            _collision = CollisionWorld.Build(World);
            _collisionBuilt = now;
            _worldBottom = MathF.Min(_worldBottom, _collision.Bottom);
        }
        return _collision;
    }

    public void Introduce(Player player, Npc npc)
    {
        var join = new EventData((byte)EventCode.Join)
        {
            Parameters = { [(byte)ParameterKey.ActorNr] = npc.Actor },
        };
        foreach ((ParameterKey key, object value) in npc.Info()) join.Parameters[(byte)key] = value;
        join.Parameters[(byte)ParameterKey.Bool] = false;
        player.Peer.Send(join);
        if (player.ReadyEvents)
            player.Peer.Send(new EventData((byte)EventCode.SetActorReady)
            {
                Parameters = { [(byte)ParameterKey.ActorNr] = npc.Actor, [(byte)ParameterKey.Bool] = false },
            });
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

    public Snapshot WithoutClassicAvatars(Snapshot snapshot)
    {
        var classic = Players.Where(player => player.ClassicAvatarId >= 0).Select(player => player.ClassicAvatarId).ToHashSet();
        if (classic.Count == 0) return snapshot;
        return snapshot with { Objects = [.. snapshot.Objects.Where(obj => !classic.Contains(obj.Id) && !classic.Contains(obj.ParentId))] };
    }

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
        CollisionWorld collision = Collision(now);

        foreach (Npc npc in npcs)
        {
            try
            {
                Combat(npc, now);
                if (npc.Mode == NpcMode.Hidden) npc.LastStep = now;
                else Simulate(npc, collision, now);
                bool urgent = npc.Teleported || npc.StateChanged;
                if (urgent || now - npc.LastSend >= NpcSendIntervalMs) SendNpc(npc, now);
                SendSize(npc, now);
                SendWeapon(npc, now);
            }
            catch (Exception error)
            {
                TickErrors.Log($"npc {npc.Actor} {npc.Name}", error);
            }
        }
    }

    bool Placed(Npc npc, Player player)
    {
        lock (npc.SeenAt) return npc.SizeModifier is null || npc.Resized.Contains(player);
    }

    void SendWeapon(Npc npc, long now)
    {
        if (!npc.HeldItemChanged && !npc.Firing && npc.Shots.IsEmpty && npc.Aims.IsEmpty) return;
        Player[] audience = [.. Players.Where(player => player.InWorld && player.Saw(npc.Actor) && Placed(npc, player))];

        if (npc.HeldItemChanged)
        {
            npc.HeldItemChanged = false;
            var item = new Dictionary<object, object?> { ["type"] = npc.HeldItem ?? (int)AvatarItemType.Hand };
            if (npc.HeldItemData is { } data) item["itemData"] = data;
            var held = new Dictionary<object, object?> { ["currentItem"] = item };
            World.Modify(npc.AvatarId, obj => PackedData.Merge(obj.Runtime, held));
            foreach (Player player in audience) player.Peer.Send(RuntimeEvent(npc, held));
        }

        while (npc.Aims.TryDequeue(out Vector3 aim))
        {
            EventData aimed = LineOfFire(npc, aim);
            foreach (Player player in audience) player.Peer.Send(aimed);
        }

        if (npc.Firing)
        {
            if (now < npc.FiringUntil) return;
            npc.Firing = false;
            foreach (Player player in audience) player.Peer.Send(RuntimeEvent(npc, new Dictionary<object, object?> { ["isFiring"] = false }));
        }

        if (!npc.Shots.TryDequeue(out (Vector3 Direction, int HoldMs) shot)) return;
        EventData line = LineOfFire(npc, shot.Direction);
        foreach (Player player in audience)
        {
            player.Peer.Send(line);
            player.Peer.Send(RuntimeEvent(npc, new Dictionary<object, object?> { ["isFiring"] = true }));
        }
        npc.Firing = true;
        npc.FiringUntil = now + Math.Max(FirePulseMs, shot.HoldMs);
    }

    static EventData LineOfFire(Npc npc, Vector3 direction) => new((byte)EventCode.UpdateLineOfFire)
    {
        Parameters =
        {
            [(byte)ParameterKey.WorldObjectID] = npc.AvatarId,
            [(byte)ParameterKey.CamOriginX] = npc.Position[0],
            [(byte)ParameterKey.CamOriginY] = npc.Position[1] + npc.EyeHeight,
            [(byte)ParameterKey.CamOriginZ] = npc.Position[2],
            [(byte)ParameterKey.CamDirX] = direction.X,
            [(byte)ParameterKey.CamDirY] = direction.Y,
            [(byte)ParameterKey.CamDirZ] = direction.Z,
            [(byte)ParameterKey.ActorNr] = npc.Actor,
        },
    };

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
            foreach (Player player in Players.Where(player => player.InWorld && player.Saw(npc.Actor)))
            {
                if (npc.Resized.Contains(player) ? !renew : !npc.SeenAt.TryGetValue(player, out long seen) || now - seen < ResizeDelayMs) continue;
                npc.Resized.Add(player);
                var resized = new Dictionary<object, object?> { ["modifiers"] = ModifiersOf(npc, resized: true) };
                player.Peer.Send(RuntimeEvent(npc, AvatarModifiers.Without(player, resized) ?? resized));
                player.Peer.Send(TransformEvent(npc, [.. npc.Position], TransformPackage.Teleport, player.Peer.ServerTime));
            }
        }
    }

    static Dictionary<object, object?> ModifiersOf(Npc npc, bool resized)
    {
        Dictionary<object, object?> modifiers = npc.Modifiers.ToDictionary(pair => (object)pair.Key, pair => (object?)pair.Value);
        foreach ((string key, byte renewals) in npc.Packages.RuntimeKeys) modifiers[key] = renewals;
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

    void Combat(Npc npc, long now)
    {
        while (npc.Respawns.TryDequeue(out Vector3 position)) Respawn(npc, position);
        while (npc.Damages.TryDequeue(out (float Amount, int Actor, Vector3 Impulse, KilledBy? Reason) damage))
        {
            if (!npc.Alive || Allied(npc, damage.Actor)) continue;
            Damage(npc, damage.Amount, damage.Actor, damage.Reason ?? CauseOf(npc, damage.Actor), now, damage.Impulse);
            npc.Motor?.SuspendImpactDamage();
            Push(npc, damage.Impulse, noFriction: false);
        }
        while (npc.Hits.TryDequeue(out (int Actor, Interaction Interaction) hit)) Hit(npc, hit.Actor, hit.Interaction, now);
        while (npc.Heals.TryDequeue(out float amount)) Heal(npc, amount);
        while (npc.ModifierChanges.TryDequeue(out (ModifierPackageType Type, int Actor, bool Add) change))
        {
            if (change.Add) GiveModifier(npc, change.Type, change.Actor, now);
            else npc.Packages.Remove(change.Type);
        }

        if (npc.OnFire && !npc.Modifiers.ContainsKey("_Fire")) AddModifier(npc, "_Fire");
        if (!npc.OnFire) RemoveModifier(npc, "_Fire");

        if (npc.Mode == NpcMode.Dead && now - npc.DiedAt >= DeadToHiddenMs)
        {
            npc.Mode = NpcMode.Hidden;
            npc.ModeChanged = true;
        }

        if (npc.Alive && npc.Position[1] < _worldBottom - FallBelowWorld) Kill(npc, KilledBy.FallOffWorld, now);
    }

    void Effects(Npc npc, long now)
    {
        const float deltaTime = AvatarMotor.FixedDeltaTime;
        ModifierPackages packages = npc.Packages;
        packages.Update(deltaTime);
        foreach ((int actor, float perSecond) in packages.ApplyById(ModifierEffect.FlamerDamagePrSec, 0f))
            Damage(npc, perSecond * deltaTime, actor, KilledBy.FlameThrower, now);
        foreach ((int actor, float perSecond) in packages.ApplyById(ModifierEffect.RayHealEnemyDamagePrSec, 0f))
            Damage(npc, perSecond * deltaTime, actor, KilledBy.RayHealGun, now);
        Damage(npc, packages.Apply(ModifierEffect.EnvironmentDamagePrSec, 0f) * deltaTime, npc.Actor, KilledBy.Environmental, now);
        Damage(npc, packages.Apply(ModifierEffect.PoisonDamagePrSec, 0f) * deltaTime, npc.Actor, KilledBy.Environmental, now);
        Damage(npc, packages.Apply(ModifierEffect.LethalDamagePrSec, 0f) * deltaTime, npc.Actor, KilledBy.Environmental, now);
        Heal(npc, packages.Apply(ModifierEffect.EnvironmentHealingPrSec, 0f) * deltaTime);
    }

    void Hit(Npc npc, int actor, Interaction interaction, long now)
    {
        if (!npc.Alive || Allied(npc, actor)) return;
        npc.Motor?.SuspendImpactDamage();
        switch (interaction.Type)
        {
            case InteractionType.ImpulseGunHit:
            case InteractionType.VehicleHit:
                Push(npc, interaction.Impulse, noFriction: true);
                break;
            case InteractionType.SwordHit:
                Damage(npc, interaction.Damage, actor, KilledBy.Sword, now, interaction.Impulse);
                Push(npc, interaction.Impulse, noFriction: true);
                break;
            case InteractionType.RailGunHit:
                Damage(npc, interaction.Damage, actor, KilledBy.RailGun, now);
                break;
            case InteractionType.MutantHit:
                Damage(npc, interaction.Damage, actor, KilledBy.Mutant, now);
                break;
            case InteractionType.HealRayHit:
                GiveModifier(npc, ModifierPackageType.RayHealEnemy, actor, now);
                break;
            case InteractionType.FlamethrowerHit:
                GiveModifier(npc, ModifierPackageType.FlamerBurn, actor, now);
                break;
            case InteractionType.SentryTowerFire:
            case InteractionType.SentryTowerIce:
                Push(npc, interaction.Impulse, noFriction: false);
                break;
            case InteractionType.ShotgunHit:
            case InteractionType.CenterGun:
            case InteractionType.SixShooterHit:
            case InteractionType.DoubleSixShooterHit:
            case InteractionType.ThrowingStarHit:
            case InteractionType.MultiThrowingStarHit:
            case InteractionType.SlapGunHit:
            case InteractionType.CustomGunHit:
            case InteractionType.ProximityDamageAndImpulse:
                Damage(npc, interaction.Damage, actor, KilledByOf(interaction), now, interaction.Impulse);
                Push(npc, interaction.Impulse, noFriction: false);
                break;
        }
    }

    static void GiveModifier(Npc npc, ModifierPackageType type, int actor, long now)
    {
        KilledBy killedBy = type switch
        {
            ModifierPackageType.FlamerBurn => KilledBy.FlameThrower,
            ModifierPackageType.RayHealEnemy => KilledBy.RayHealGun,
            _ => KilledBy.Environmental,
        };
        npc.Packages.Add(type, killedBy == KilledBy.Environmental ? -1 : actor);
        if (actor != npc.Actor && type is not (ModifierPackageType.RayHeal or ModifierPackageType.HealingMat)) npc.LastAttacker = (actor, killedBy, now);
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

    static void Push(Npc npc, Vector3 impulse, bool noFriction)
    {
        npc.Motor?.AddImpulse(impulse);
        if (noFriction) npc.Packages.Add(ModifierPackageType.NoFriction);
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

    void Damage(Npc npc, float amount, int actor, KilledBy killedBy, long now, Vector3 impulse = default)
    {
        if (!npc.Alive || !(amount > 0f) || npc.Packages.Has(ModifierEffect.Invulnerable)) return;
        amount *= npc.Packages.Apply(ModifierEffect.DamageMultiplier, 1f);
        float before = npc.Health;
        npc.Health = Math.Clamp(before - amount, 0f, npc.MaxHealth);
        npc.HealthChanged = true;
        npc.LastDamage = new DamageTaken(amount, killedBy, actor, impulse);
        if (actor != npc.Actor) npc.LastAttacker = (actor, killedBy, now);
        Plugins.PluginHost.NpcDamaged(this, npc, PlayerOf(actor), amount);
        if (npc.Health > 0f || before <= 0f) return;

        if (actor == npc.Actor && npc.LastAttacker is var (attacker, attackedWith, at) && now - at <= LastAttackerMs) Die(npc, attacker, attackedWith, now);
        else Die(npc, actor, killedBy, now);
    }

    void Kill(Npc npc, KilledBy killedBy, long now)
    {
        npc.LastDamage = new DamageTaken(npc.Health, killedBy, npc.Actor, Vector3.Zero);
        npc.Health = 0f;
        npc.HealthChanged = true;
        Die(npc, npc.Actor, killedBy, now);
    }

    static void Heal(Npc npc, float amount)
    {
        if (!npc.Alive || !(amount > 0f) || npc.Packages.Has(ModifierEffect.Invulnerable)) return;
        float healed = Math.Clamp(npc.Health + amount, 0f, npc.MaxHealth);
        if (healed == npc.Health) return;
        npc.Health = healed;
        npc.HealthChanged = true;
    }

    void Die(Npc npc, int killerActor, KilledBy killedBy, long now)
    {
        npc.Mode = NpcMode.Dead;
        npc.ModeChanged = true;
        npc.DiedAt = now;
        npc.Target = null;
        npc.JumpUntil = 0;

        foreach (Player player in Players.Where(player => player.Saw(npc.Actor)))
        {
            byte reason = KillReasons.ToClient(killedBy, player.Build);
            player.Peer.Send(new EventData((byte)EventCode.PostGameMsg)
            {
                Parameters =
                {
                    [(byte)ParameterKey.GameMsgType] = AvatarKilledMessage,
                    [(byte)ParameterKey.GameMsgData] = new Dictionary<object, object?> { [(byte)0] = npc.Actor, [(byte)1] = killerActor, [(byte)2] = reason },
                },
            });
            if (!SilentKills.Contains(killedBy) && player.Peer.Translator is Kogama.Protocols.OperationRemap)
                player.Peer.Send(new EventData((byte)EventCode.NotificationEvent)
                {
                    Parameters =
                    {
                        [(byte)ParameterKey.NotificationType] = KillNotification,
                        [(byte)ParameterKey.NotificationData] = new Dictionary<object, object?> { [(byte)7] = npc.Actor, [(byte)6] = killerActor, [(byte)8] = reason },
                    },
                });
        }

        Player? killer = killerActor == npc.Actor ? null : PlayerOf(killerActor);
        string by = killerActor == npc.Actor ? "nothing" : killer?.Username ?? NpcByActor(killerActor)?.Name ?? "nothing";
        Console.WriteLine($"npc {npc.Actor}: {npc.Name} killed by {by} with {killedBy}");
        Plugins.PluginHost.NpcKilled(this, npc, killer);
        npc.Water.Refill();
    }

    void Respawn(Npc npc, Vector3 position)
    {
        npc.Health = npc.MaxHealth;
        npc.HealthChanged = true;
        npc.Mode = NpcMode.Playing;
        npc.ModeChanged = true;
        npc.Modifiers.Clear();
        npc.ModifiersChanged = true;
        npc.Packages.Clear();
        npc.Water.Refill();
        if (npc.ColossusScale is not null)
        {
            Colossus.Release(this, npc.AvatarId);
            npc.ColossusScale = null;
            npc.HeldItem = null;
            npc.HeldItemData = null;
            npc.HeldItemChanged = true;
        }
        npc.Target = null;
        npc.Position = [position.X, position.Y, position.Z];
        npc.Teleported = true;
    }

    Player? PlayerOf(int actor) => Players.FirstOrDefault(player => player.Actor == actor);

    public Npc? NpcByActor(int actor)
    {
        lock (_npcs) return _npcs.FirstOrDefault(npc => npc.Actor == actor);
    }

    bool Allied(Npc npc, int actor) =>
        actor != npc.Actor && npc.Team is Team team && (PlayerOf(actor)?.Team ?? NpcByActor(actor)?.Team) == team;

    KilledBy CauseOf(Npc npc, int actor) =>
        actor == npc.Actor ? KilledBy.Environmental
        : NpcByActor(actor) is { } attacker ? attacker.ColossusScale is not null ? KilledBy.GodzillaLaser : KilledBy.Mutant
        : KilledBy.None;

    public void DamagePlayer(Player victim, int attackerActor, float damage, Vector3 impulse, KilledBy killedBy)
    {
        var hit = new Interaction(InteractionType.ProximityDamageAndImpulse, damage, impulse, killedBy);
        victim.Peer.Send(new EventData((byte)EventCode.WorldObjectRPCEvent)
        {
            Parameters =
            {
                [(byte)ParameterKey.ActorNr] = attackerActor,
                [(byte)ParameterKey.WorldObjectID] = victim.PlayAvatar,
                [(byte)ParameterKey.WorldObjectRPCData] = new Dictionary<object, object?> { [(byte)0] = hit.ToBytes(victim.Build) },
            },
        });
    }

    Snapshot NpcSnapshot(Npc npc)
    {
        Snapshot avatar = World.SubtreeSnapshot(npc.AvatarId);
        return avatar with { Prototypes = [.. avatar.Prototypes.Where(prototype => !_avatarPrototypes.Contains(prototype.Id))] };
    }

    void Simulate(Npc npc, CollisionWorld collision, long now)
    {
        npc.Motor ??= new AvatarMotor(collision, ToVector(npc.Position), npc.Packages, npc.Water);
        AvatarMotor motor = npc.Motor;
        motor.World = collision;
        motor.WalkSpeed = npc.Speed;
        motor.Scale = npc.Size;
        if (npc.Teleported) motor.Teleport(ToVector(npc.Position));

        npc.Accumulator += MathF.Min((now - npc.LastStep) / 1000f, MaxCatchUpSeconds);
        npc.LastStep = now;

        bool walking = false;
        while (npc.Accumulator >= AvatarMotor.FixedDeltaTime)
        {
            npc.Accumulator -= AvatarMotor.FixedDeltaTime;
            bool alive = npc.Alive;
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
            Damage(npc, motor.ImpactDamage, npc.Actor, KilledBy.Impact, now);
            Damage(npc, motor.SuffocationDamage, npc.Actor, KilledBy.Environmental, now);
            Effects(npc, now);
        }

        if (npc.Alive && motor.IsStuck()) Kill(npc, KilledBy.Crushed, now);

        Vector3 position = motor.Position;
        npc.Position = [position.X, position.Y, position.Z];
        npc.Animation = !npc.Alive ? "Dead"
            : npc.AnimationOverride ?? (motor.Water.Swimming ? "Swim" : motor.Jumping ? "Jump" : walking || npc.Target is not null ? "Walk" : "Idle");
    }

    static Vector3 ToVector(float[] value) => new(value[0], value[1], value[2]);

    void SendNpc(Npc npc, long now)
    {
        npc.LastSend = now;
        bool moved = npc.SentPosition is not { } last || !last.SequenceEqual(npc.Position) || npc.SentYaw != npc.Yaw;
        string animation = npc.Animation;
        bool animate = animation != npc.SentAnimation;
        bool state = npc.StateChanged || npc.Packages.Changed;
        if (!moved && npc.StopSent && !animate && !state) return;

        float[] position = [.. npc.Position];
        float[] rotation = npc.Rotation;
        World.Modify(npc.AvatarId, obj =>
        {
            obj.Position = position;
            obj.Rotation = rotation;
        });

        Player[] audience = [.. Players.Where(player => player.InWorld && player.Saw(npc.Actor))];
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
        bool modifiers = npc.ModifiersChanged || npc.Packages.Changed;
        var changes = new Dictionary<object, object?>();
        if (animate) changes["animation"] = new Dictionary<object, object?> { ["state"] = animation, ["timeStamp"] = time };
        if (npc.HealthChanged) changes["health"] = npc.Health;
        if (npc.ModeChanged) changes["avatarModeTypes"] = (int)npc.Mode;
        if (modifiers) changes["modifiers"] = ModifiersOf(npc, resized: false);
        if (npc.MaxHealthChanged) changes["maxHealth"] = (int)MathF.Round(npc.MaxHealth);
        npc.HealthChanged = npc.ModeChanged = npc.ModifiersChanged = npc.MaxHealthChanged = npc.Packages.Changed = false;
        World.Modify(npc.AvatarId, obj =>
        {
            if (modifiers) PackedData.Remove(obj.Runtime, new Dictionary<object, object?> { ["modifiers"] = null });
            PackedData.Merge(obj.Runtime, changes);
        });

        Dictionary<object, object?>? resized = modifiers && npc.SizeModifier is not null
            ? new Dictionary<object, object?>(changes) { ["modifiers"] = ModifiersOf(npc, resized: true) }
            : null;
        EventData runtime = RuntimeEvent(npc, changes);
        EventData? resizedRuntime = resized is not null ? RuntimeEvent(npc, resized) : null;
        lock (npc.SeenAt)
            foreach (Player player in audience)
            {
                bool big = resized is not null && npc.Resized.Contains(player);
                player.Peer.Send(AvatarModifiers.Without(player, big ? resized! : changes) is { } known ? RuntimeEvent(npc, known) : big ? resizedRuntime! : runtime);
            }
        npc.SentAnimation = animation;
    }
}
