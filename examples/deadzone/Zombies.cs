using System.Numerics;
using System.Runtime.CompilerServices;
using OpenKogama.Api;

namespace Deadzone;

sealed class Zombies(AvatarSkin skin, IReadOnlyDictionary<string, AvatarSkin> skins, NightPlan plan)
{
    const float SpawnSpread = 1.5f;
    const float AttackRange = 1.0f;
    const float AvatarRadius = 0.45f;
    const float AttackHeight = 2f;
    const long AttackIntervalMs = 1000;
    const float KnockbackHorizontal = 150f;
    const float KnockbackUp = 60f;
    const long StuckCheckMs = 1000;
    const float StuckDistance = 0.3f;
    const float StuckJump = 0.4f;
    const long RemoveAfterDeathMs = 5000;
    const float FuseHop = 0.15f;
    const long SelfDestructDelayMs = 200;
    const string ExplosionItem = "Bazooka";
    const float Separation = 1.4f;
    const float SeparationWeight = 3f;
    const float Crowded = 0.5f;
    const float FlankDistance = 10f;
    const float FlankStrength = 0.8f;
    const float Lookahead = 3f;

    sealed class Wave(Night night)
    {
        public int MaxAlive => night.MaxAlive;
        public Queue<NightStep> Steps { get; } = new(night.Steps);
        public long NextSpawn { get; set; } = Environment.TickCount64;
        public long? ClearedAt { get; set; }
        public int NextPoint { get; set; }
    }

    sealed class Zombie(IWorld world, INpc npc, Monster monster)
    {
        public IWorld World => world;
        public INpc Npc => npc;
        public Monster Monster => monster;
        public long NextAttack { get; set; }
        public Vector3 LastCheck { get; set; } = npc.Position;
        public long LastCheckAt { get; set; } = Environment.TickCount64;
        public float Flank { get; } = Random.Shared.NextSingle() * 2f - 1f;
        public long? FuseLit { get; set; }
        public bool Exploded { get; set; }
        public long? DieAt { get; set; }
        public float Side => Flank < 0f ? -1f : 1f;
    }

    readonly ConditionalWeakTable<IWorld, Wave> _waves = [];
    readonly Dictionary<int, Zombie> _zombies = [];
    readonly List<(INpc Npc, long At)> _removals = [];
    readonly object _sync = new();

    public Action<IWorld>? NightCleared { get; set; }

    public void PhaseStarted(IWorld world, DayNightCycle.Phase phase)
    {
        lock (_sync)
        {
            foreach (Zombie zombie in _zombies.Values.Where(zombie => zombie.World == world && zombie.Npc.Alive))
            {
                zombie.Exploded = true;
                zombie.Npc.OnFire = false;
                zombie.Npc.Damage(zombie.Npc.Health);
            }
            if (phase.Night) _waves.AddOrUpdate(world, new Wave(plan.Night(phase.Day)));
            else _waves.Remove(world);
        }
    }

    public bool Killed(INpc npc)
    {
        lock (_sync)
        {
            if (!_zombies.Remove(npc.Id, out Zombie? zombie)) return false;
            _removals.Add((npc, Environment.TickCount64 + RemoveAfterDeathMs));
            npc.OnFire = false;
            if (zombie.Monster.Explosion is { } explosion && !zombie.Exploded) Explode(zombie, explosion);
            return true;
        }
    }

    void Explode(Zombie zombie, Explosion explosion)
    {
        zombie.Exploded = true;
        INpc npc = zombie.Npc;
        Vector3 center = npc.Position + Vector3.UnitY;

        foreach (IPlayer player in zombie.World.Players)
        {
            if (player.Position is not Vector3 position || Falloff(position + Vector3.UnitY - center, explosion) is not (float strength, Vector3 away)) continue;
            player.Damage(explosion.Damage * strength, npc, away * explosion.Push * strength);
        }
        foreach (Zombie other in _zombies.Values.Where(other => other != zombie && other.World == zombie.World && other.Npc.Alive))
        {
            if (Falloff(other.Npc.Position + Vector3.UnitY - center, explosion) is (float strength, _))
                other.Npc.Damage(explosion.Damage * strength);
        }
        npc.Holding = ExplosionItem;
        npc.Shoot(-Vector3.UnitY);
        if (npc.Alive) zombie.DieAt = Environment.TickCount64 + SelfDestructDelayMs;
    }

    static (float Strength, Vector3 Away)? Falloff(Vector3 offset, Explosion explosion)
    {
        float distance = offset.Length();
        if (distance >= explosion.Radius) return null;
        Vector3 away = distance > 0.001f ? offset / distance : Vector3.UnitY;
        return (1f - distance / explosion.Radius, away);
    }

    public void Update(IEnumerable<IWorld> worlds)
    {
        long now = Environment.TickCount64;
        var cleared = new List<IWorld>();
        lock (_sync)
        {
            foreach ((INpc npc, _) in _removals.Where(entry => entry.At <= now)) npc.Remove();
            _removals.RemoveAll(entry => entry.At <= now);

            foreach (IWorld world in worlds)
            {
                if (!_waves.TryGetValue(world, out Wave? wave)) continue;
                Spawn(world, wave, now);
                if (wave.Steps.Count > 0 || _zombies.Values.Any(zombie => zombie.World == world && zombie.Npc.Alive)) continue;
                _waves.Remove(world);
                cleared.Add(world);
            }

            foreach ((int id, Zombie zombie) in _zombies.ToList())
                if (!zombie.Npc.Exists) _zombies.Remove(id);
            Zombie[] alive = [.. _zombies.Values.Where(zombie => zombie.Npc.Alive)];
            foreach (Zombie zombie in alive) Think(zombie, alive, now);
            foreach (Zombie zombie in alive.Where(zombie => zombie.FuseLit is long lit && zombie.Monster.Explosion is { } explosion && now - lit >= explosion.Fuse * 1000f && !zombie.Exploded))
                Explode(zombie, zombie.Monster.Explosion!);
            foreach (Zombie zombie in alive.Where(zombie => zombie.DieAt is long at && now >= at && zombie.Npc.Alive))
                zombie.Npc.Damage(zombie.Npc.Health);
        }
        foreach (IWorld world in cleared) NightCleared?.Invoke(world);
    }

    void Spawn(IWorld world, Wave wave, long now)
    {
        if (now < wave.NextSpawn || !wave.Steps.TryPeek(out NightStep? step)) return;
        int alive = _zombies.Values.Count(zombie => zombie.World == world && zombie.Npc.Alive);
        int room = wave.MaxAlive - alive;
        switch (step)
        {
            case NightStep.Wait wait:
                if (alive > 0)
                {
                    wave.ClearedAt = null;
                    break;
                }
                wave.ClearedAt ??= now;
                if (now - wave.ClearedAt < (long)(wait.Seconds * 1000f)) break;
                wave.Steps.Dequeue();
                wave.ClearedAt = null;
                break;
            case NightStep.Spawn spawn when room > 0:
                wave.Steps.Dequeue();
                SpawnOne(world, wave, spawn.Monster);
                wave.NextSpawn = now + (long)(plan.SpawnEvery * 1000f);
                break;
            case NightStep.Rush rush when room >= Math.Min(rush.Count, wave.MaxAlive):
                wave.Steps.Dequeue();
                for (int i = 0; i < rush.Count; i++) SpawnOne(world, wave, rush.Monster);
                wave.NextSpawn = now;
                break;
        }
    }

    void SpawnOne(IWorld world, Wave wave, Monster monster)
    {
        Vector3 point = plan.SpawnPoint(wave.NextPoint++)
            + new Vector3((Random.Shared.NextSingle() * 2f - 1f) * SpawnSpread, 0f, (Random.Shared.NextSingle() * 2f - 1f) * SpawnSpread);
        INpc npc = world.SpawnNpc(monster.Name, point, 0f, monster.Skin is string name ? skins[name] : skin, monster.Size, monster.Level);
        npc.Speed = monster.Speed;
        npc.MaxHealth = monster.Health;
        _zombies[npc.Id] = new Zombie(world, npc, monster);
    }

    static float Range(Monster monster) => AttackRange + (monster.Size - 1f) * AvatarRadius;

    static void Think(Zombie zombie, Zombie[] pack, long now)
    {
        INpc npc = zombie.Npc;
        Vector3 position = npc.Position;
        IPlayer? target = zombie.World.Players
            .Where(player => player.Position is not null)
            .MinBy(player => Vector3.DistanceSquared(player.Position!.Value, position));
        if (target?.Position is not Vector3 goal)
        {
            if (npc.Walking) npc.Stop();
            return;
        }

        Vector3 offset = goal - position;
        float flat = new Vector2(offset.X, offset.Z).Length();
        Vector3 ahead = flat > 0.001f ? new Vector3(offset.X / flat, 0f, offset.Z / flat) : Vector3.UnitZ;
        var side = new Vector3(-ahead.Z, 0f, ahead.X);
        Vector3 crowd = Crowd(zombie, position, pack, ahead, side);
        float range = Range(zombie.Monster);

        if (zombie.Monster.Explosion is { } explosion)
        {
            if (zombie.FuseLit is null && flat <= explosion.Trigger && MathF.Abs(offset.Y) <= AttackHeight)
            {
                zombie.FuseLit = now;
                npc.OnFire = true;
                npc.Holding = ExplosionItem;
                npc.Jump(FuseHop);
            }
            if (zombie.FuseLit is not null)
            {
                if (npc.Walking) npc.Stop();
                if (flat > 0.001f) npc.Face(MathF.Atan2(offset.X, offset.Z) * 180f / MathF.PI);
                return;
            }
        }
        else if (flat <= range && MathF.Abs(offset.Y) <= AttackHeight)
        {
            if (crowd.Length() > Crowded) npc.WalkTo(position + Vector3.Normalize(crowd) * Lookahead);
            else if (npc.Walking) npc.Stop();
            if (flat > 0.001f) npc.Face(MathF.Atan2(offset.X, offset.Z) * 180f / MathF.PI);
            if (now < zombie.NextAttack) return;
            zombie.NextAttack = now + AttackIntervalMs;
            Vector3 push = flat > 0.001f ? new Vector3(offset.X / flat, 0f, offset.Z / flat) * KnockbackHorizontal : Vector3.Zero;
            target.Damage(zombie.Monster.Damage, npc, (push + Vector3.UnitY * KnockbackUp) * zombie.Monster.Knockback);
            return;
        }

        float flank = zombie.Flank * FlankStrength * Math.Clamp((flat - range) / FlankDistance, 0f, 1f);
        Vector3 steer = ahead + side * flank + crowd;
        steer.Y = 0f;
        if (steer.Length() < 0.1f)
        {
            if (npc.Walking) npc.Stop();
        }
        else npc.WalkTo(position + Vector3.Normalize(steer) * Lookahead);

        if (now - zombie.LastCheckAt < StuckCheckMs) return;
        Vector3 moved = position - zombie.LastCheck;
        moved.Y = 0f;
        if (moved.Length() < StuckDistance && npc.Grounded) npc.Jump(StuckJump);
        zombie.LastCheck = position;
        zombie.LastCheckAt = now;
    }

    static Vector3 Crowd(Zombie zombie, Vector3 position, Zombie[] pack, Vector3 ahead, Vector3 side)
    {
        Vector3 push = Vector3.Zero;
        foreach (Zombie other in pack)
        {
            if (other == zombie || other.World != zombie.World) continue;
            Vector3 apart = position - other.Npc.Position;
            if (MathF.Abs(apart.Y) > AttackHeight) continue;
            apart.Y = 0f;
            float gap = apart.Length();
            float spacing = Separation * (zombie.Monster.Size + other.Monster.Size) / 2f;
            if (gap >= spacing) continue;

            Vector3 away = gap > 0.001f ? apart / gap : side * zombie.Side;
            float strength = (spacing - gap) / spacing;
            push += away * strength;
            if (Vector3.Dot(away, ahead) >= -0.5f) continue;
            float lateral = Vector3.Dot(away, side);
            push += side * (MathF.Abs(lateral) > 0.1f ? MathF.Sign(lateral) : zombie.Side) * strength;
        }
        return push * SeparationWeight;
    }
}
