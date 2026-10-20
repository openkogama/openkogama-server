using System.Globalization;
using System.Numerics;
using OpenKogama.Api;

namespace Killstreak;

//   Commands
//
//   !streak                  your kill count and whether the server hooks are on
//   !streak <n>              admins only: set your kill count
//   !streak give <name> [n]  give n of your kills to another player
//   !streak debug            what the plugin sees: rig, skyboxes, damage hook counters etcetc

public sealed class KillstreakPlugin : IPlugin
{
    static readonly int[] Steps = [10, 25, 50, 100, 250];
    static readonly float[] Red = [1f, 0.05f, 0.05f];
    static readonly float[] Cyan = [0f, 0.9f, 1f];
    static readonly float[] Maroon = [0.55f, 0f, 0.12f];
    static readonly float[] Yellow = [1f, 0.85f, 0f];
    static readonly float[] White = [1f, 1f, 1f];
    static readonly float[] Black = [0.03f, 0.03f, 0.03f];

    readonly record struct Look(float[] Ring, float[] Glow, string Text);

    static readonly Look[] Looks =
    [
        new(White, White, "#ffffff"),
        new(Red, Red, "#ff3030"),
        new(Cyan, Cyan, "#30e0ff"),
        new(Maroon, Maroon, "#d02838"),
        new(Yellow, White, "#ffd24a"),
        new(Black, Black, "#9a9a9a"),
    ];

    const int SpeedFromLevel = 2; 
    const int TintLevel = 3;      
    const int FireLevel = 3;      
    const int GodLevel = 4;       
    const int MutantLevel = 5;    
    const string FireType = "25";
    const string SmokeType = "26";
    const string TextType = "TextMsg";
    const float HeadHeight = 2.6f;
    const float TextSize = 0.6f;
    const float BodyHeight = 1.0f;
    const float FollowEvery = 0.1f;
    const float MoveEpsilon = 0.05f;
    const float RetryAfter = 5f;
    const float SpinDegreesPerSecond = 120f;
    const float GlowRange = 8f;
    const float GlowIntensity = 5f;
    const float TopGlowIntensity = 6f;
    const int RingLights = 4;
    const float RingRadius = 1.3f;
    const float RingRange = 6f;
    const float RingIntensity = 4f;
    const int PuffCount = 3;
    const float PuffSize = 0.6f;
    const float PuffLife = 1.2f;
    const float PuffAlpha = 0.5f;
    const float FireRange = 5f;
    const float FireSeconds = 3f;
    const float FireCooldown = 6f;
    const float GlitchEvery = 0.2f;
    const string Glyphs = "_-|/\\=";
    const float SpeedEvery = 6f;
    static readonly bool WorldTint = true;
    const float TintFog = 0.02f;
    const float TintEvery = 2f;
    static readonly float[] TintColor = [0.7f, 0.05f, 0.1f];
    static readonly float[] DefaultSky = [95f / 255f, 180f / 255f, 254f / 255f];
    const float DefaultFog = 0.007f;

#if SERVER_HOOKS
    const bool HooksOn = true;
    const float BonusPerLevel = 0.2f;
    const int BonusLevels = 3;
    const float GodMinDamage = 90f;
    const float GodHealth = 450f;
    const float ClientHealth = 100f;
#else
    const bool HooksOn = false;
#endif

    const string Mark = "killstreak";

    sealed class State
    {
        public int Kills { get; set; }
        public int Level { get; set; }
        public float SpeedLeft { get; set; }
        public bool MutantPending { get; set; }
        public float RetryAt { get; set; }
        public Rig? Rig { get; set; }
        public int Fire { get; set; }
        public IWorld? FireWorld { get; set; }
        public float FireUntil { get; set; }
        public float FireReadyAt { get; set; }
    }

    sealed class Rig(IWorld world, Vector3 at)
    {
        public IWorld World => world;
        public Vector3 At { get; set; } = at;
        public int Root { get; set; }
        public int Spin { get; set; }
        public int Text { get; set; }
        public List<int> Aura { get; } = [];
        public List<int> Puffs { get; } = [];
        public List<int> Glitch { get; } = [];
        public int AuraLevel { get; set; } = -1;
        public float Angle { get; set; }
        public float PuffsAt { get; set; }
        public float GlitchAt { get; set; }
    }

    sealed record Sky(int Id, float[] Color, float Fog);

    readonly object _sync = new();
    readonly Dictionary<IPlayer, State> _states = [];
    readonly Dictionary<IWorld, List<Sky>> _skies = [];
    readonly HashSet<IWorld> _swept = [];
    readonly Random _random = new();
    IServer _server = null!;
    float _clock;
    float _now;
    float _tintAt;
    float _hitLogAt;
    int _hits;
    int _changed;
    string _lastChange = "none";

    public void Load(IServer server)
    {
        _server = server;
        server.PlayerJoined += player => { lock (_sync) StateOf(player); };
        server.PlayerLeft += Left;
        server.PlayerKilled += (victim, killer) =>
        {
            Died(victim);
            if (killer != victim) Credit(killer);
        };
        server.PlayerKilledByNpc += (victim, _) => Died(victim);
        server.NpcKilled += (_, killer) =>
        {
            if (killer is not null) Credit(killer);
        };
        server.PlayerAnimated += (player, animation) =>
        {
            if (animation == AvatarAnimation.Dead) Died(player);
        };
        server.PlayerPickedUp += PickedUp;
        server.Update += Update;
        server.AddCommand("streak", Command);
#if SERVER_HOOKS
        server.DamageModifier = Damage;
        server.GhostKilled += (player, _) => Credit(player);
#endif
        server.Log(HooksOn
            ? "killstreak: loaded with server hooks (damage buffs and Oculus kills on)"
            : "killstreak: loaded WITHOUT server hooks (no damage buffs, Oculus kills are not counted)");
    }

    public void Unload()
    {
        lock (_sync)
        {
            foreach (State state in _states.Values)
            {
                Extinguish(state);
                if (state.Rig is { } rig) rig.World.Remove(rig.Root);
            }
            foreach ((IWorld world, List<Sky> skies) in _skies.ToList())
                foreach (Sky sky in skies) Restore(world, sky);
            _states.Clear();
            _skies.Clear();
        }
    }

    State StateOf(IPlayer player)
    {
        if (!_states.TryGetValue(player, out State? state)) _states[player] = state = new State();
        return state;
    }

    void Left(IPlayer player)
    {
        lock (_sync)
        {
            if (!_states.Remove(player, out State? state)) return;
            Extinguish(state);
            if (state.Rig is { } rig) rig.World.Remove(rig.Root);
            Tint(player.World);
        }
    }

    void Command(IPlayer player, string[] arguments)
    {
        lock (_sync)
        {
            State state = StateOf(player);
            string first = arguments.Length > 0 ? arguments[0] : "";
            if (first.Equals("debug", StringComparison.OrdinalIgnoreCase))
            {
                Report(player, state);
                return;
            }
            if (first.Equals("give", StringComparison.OrdinalIgnoreCase))
            {
                Give(player, state, arguments[1..]);
                return;
            }
            if (arguments.Length > 0 && player.IsAdmin && int.TryParse(first, NumberStyles.None, CultureInfo.InvariantCulture, out int kills))
            {
                state.Kills = kills;
                Refresh(player, state);
            }
            string hooks = HooksOn ? "on" : "OFF (no damage buffs, no Oculus kills)";
            player.Message($"Killstreak: {state.Kills}, server hooks {hooks}");
        }
    }

    void Give(IPlayer giver, State state, string[] arguments)
    {
        if (arguments.Length == 0)
        {
            giver.Message("usage: !streak give <name> [amount]");
            return;
        }
        string name = string.Join(' ', arguments);
        int amount = state.Kills;
        if (arguments.Length > 1 && int.TryParse(arguments[^1], NumberStyles.None, CultureInfo.InvariantCulture, out int typed))
        {
            amount = typed;
            name = string.Join(' ', arguments[..^1]);
        }

        IPlayer[] matches = [.. _server.Players.Where(other => other.Name.Equals(name, StringComparison.OrdinalIgnoreCase))];
        if (matches.Length != 1)
        {
            giver.Message(matches.Length == 0 ? $"no player called {name} on this server" : $"{name} matches more than one player");
            return;
        }
        IPlayer receiver = matches[0];
        if (receiver.Actor == giver.Actor)
        {
            giver.Message("you can't give kills to yourself");
            return;
        }
        if (amount <= 0 || amount > state.Kills)
        {
            giver.Message($"you have {state.Kills} kills, give between 1 and that");
            return;
        }

        State target = StateOf(receiver);
        state.Kills -= amount;
        target.Kills += amount;
        Refresh(giver, state);
        Refresh(receiver, target);
        receiver.Notify($"{giver.Name} gave you {amount} killstreak", brief: true);
        giver.World.Broadcast($"{giver.Name} gave {amount} kills to {receiver.Name}", "#ffd24a");
    }

    void Report(IPlayer player, State state)
    {
        IWorld world = player.World;
        string where = player.Position is Vector3 at ? $"{at.X:0.0}, {at.Y:0.0}, {at.Z:0.0}" : "unknown";
        string rig = state.Rig is { } r
            ? $"root {r.Root}, level {r.AuraLevel}, puffs {r.Puffs.Count}, glitch lines {r.Glitch.Count}"
            : "none";
        int marked = world.Objects.Count(obj => obj.Data.ContainsKey(Mark));
        int skies = world.Objects.Count(obj => obj.Type == "Skybox");
        int tinted = _skies.TryGetValue(world, out List<Sky>? saved) ? saved.Count : 0;
        player.Message($"playing {world.Play}, position {where}, rig {rig}");
        player.Message($"killstreak objects in the world {marked}, skyboxes {skies} (tinted {tinted}), server hooks {(HooksOn ? "on" : "off")}");
        player.Message($"damage hook: {_hits} hits seen, {_changed} changed, last change {_lastChange}");
        if (state.RetryAt > _now) player.Message($"building the rig failed, retrying in {state.RetryAt - _now:0} s, see the server console");
    }

    static int Level(int kills) => Steps.Count(step => kills >= step);

    void Credit(IPlayer player)
    {
        lock (_sync)
        {
            State state = StateOf(player);
            state.Kills++;
            if (!Refresh(player, state)) player.Notify($"Killstreak: {state.Kills}", brief: true);
        }
    }

    void Died(IPlayer player)
    {
        lock (_sync)
        {
            State state = StateOf(player);
            state.SpeedLeft = 0;
            state.MutantPending = false;
            if (state.Kills == 0) return;
            int lost = state.Kills;
            state.Kills = 0;
            Refresh(player, state);
            if (lost >= Steps[0]) player.World.Broadcast($"{player.Name} lost a {lost} killstreak", "#ff8080");
        }
    }

    bool Refresh(IPlayer player, State state)
    {
        int level = Level(state.Kills);
        int before = state.Level;
        state.Level = level;
        if (state.Rig is { } rig)
        {
            rig.World.SetData(rig.Text, "text", Label(state.Kills));
            if (rig.AuraLevel != level) Aura(rig, level);
        }
        if (level >= SpeedFromLevel && before < SpeedFromLevel) state.SpeedLeft = 0;
        if (level == MutantLevel && before < MutantLevel) state.MutantPending = true;

        bool reached = level > before;
        if (reached)
        {
            int step = Steps[level - 1];
            player.World.Broadcast($"{player.Name} reached a {step} killstreak!", Looks[level].Text);
            player.Notify($"{step} KILLSTREAK!");
        }
        Tint(player.World);
        return reached;
    }

#if SERVER_HOOKS
    float Damage(IPlayer attacker, IPlayer? victim, float damage)
    {
        lock (_sync)
        {
            _hits++;
            int level = _states.TryGetValue(attacker, out State? mine) ? Level(mine.Kills) : 0;
            float before = damage;
            if (level > 0) damage *= 1f + BonusPerLevel * Math.Min(level, BonusLevels);
            if (level >= GodLevel) damage = Math.Max(damage, GodMinDamage);
            if (victim is not null && _states.TryGetValue(victim, out State? theirs) && Level(theirs.Kills) >= GodLevel)
                damage *= ClientHealth / GodHealth;

            if (damage != before)
            {
                _changed++;
                _lastChange = $"{attacker.Name} to {victim?.Name ?? "no player resolved"}: {before:0.0} -> {damage:0.0}";
                if (_now >= _hitLogAt)
                {
                    _hitLogAt = _now + 2f;
                    _server.Log($"killstreak: {_lastChange}");
                }
            }
            return damage;
        }
    }
#endif

    void Update(float seconds)
    {
        lock (_sync)
        {
            _clock += seconds;
            if (_clock < FollowEvery) return;
            float elapsed = _clock;
            _clock = 0;
            _now += elapsed;

            IPlayer[] players = [.. _server.Players];
            foreach (IPlayer player in players)
            {
                State state = StateOf(player);
                try
                {
                    Follow(player, state, elapsed);
                    Speed(player, state, elapsed);
                    Mutant(player, state);
                    Burn(player, state, players);
                }
                catch (Exception error)
                {
                    state.RetryAt = _now + RetryAfter;
                    _server.Log($"killstreak: {error.GetType().Name}: {error.Message}");
                }
            }

            if (_now >= _tintAt)
            {
                _tintAt = _now + TintEvery;
                foreach (IWorld world in players.Select(player => player.World).Distinct()) Tint(world);
            }
        }
    }

    void Follow(IPlayer player, State state, float elapsed)
    {
        IWorld world = player.World;
        if (_now < state.RetryAt || !world.Play || player.Position is not Vector3 position) return;

        if (state.Rig is { } stale && stale.World != world)
        {
            stale.World.Remove(stale.Root);
            state.Rig = null;
        }
        if (state.Rig is null)
        {
            state.Rig = Build(world, state.Kills, position);
            Tint(world);
        }
        Rig rig = state.Rig;

        if (Vector3.DistanceSquared(position, rig.At) > MoveEpsilon * MoveEpsilon)
        {
            world.Move(rig.Root, position, seconds: FollowEvery);
            rig.At = position;
        }
        if (rig.AuraLevel > 0)
        {
            rig.Angle = (rig.Angle + SpinDegreesPerSecond * elapsed) % 360f;
            world.Move(rig.Spin, Vector3.Zero, Quaternion.CreateFromAxisAngle(Vector3.UnitY, rig.Angle * MathF.PI / 180f), FollowEvery);
            if (_now >= rig.PuffsAt) Puff(rig);
        }
        if (rig.Glitch.Count > 0 && _now >= rig.GlitchAt) Glitch(rig);
    }

    void Speed(IPlayer player, State state, float elapsed)
    {
        if (state.Level < SpeedFromLevel) return;
        state.SpeedLeft -= elapsed;
        if (state.SpeedLeft > 0 || !player.Alive) return;
        if (player.GiveLightningSpeed()) state.SpeedLeft = SpeedEvery;
    }

    void Mutant(IPlayer player, State state)
    {
        if (!state.MutantPending) return;
        if (state.Level < MutantLevel)
        {
            state.MutantPending = false;
            return;
        }
        if (player.Alive && player.Equip(AvatarItem.Mutant)) state.MutantPending = false;
    }

    void PickedUp(IPlayer player, AvatarItem item)
    {
        lock (_sync)
        {
            State state = StateOf(player);
            if (state.Level >= MutantLevel && item is not (AvatarItem.Unknown or AvatarItem.Health or AvatarItem.Mutant or AvatarItem.NinjaRun))
                state.MutantPending = true;
        }
    }

    void Burn(IPlayer victim, State state, IPlayer[] players)
    {
        if (state.Fire != 0)
        {
            Vector3? at = victim.Position;
            bool over = _now >= state.FireUntil || !victim.Alive || state.FireWorld != victim.World || at is null;
            if (over) Extinguish(state);
            else state.FireWorld!.Move(state.Fire, at.GetValueOrDefault(), seconds: FollowEvery);
            if (state.Fire != 0) return;
        }
        if (_now < state.FireReadyAt || !victim.Alive || victim.Position is not Vector3 position) return;

        bool close = players.Any(other => other.Actor != victim.Actor && other.World == victim.World
            && other.Position is Vector3 spot && Level(StateOf(other).Kills) >= FireLevel
            && Vector3.DistanceSquared(spot, position) <= FireRange * FireRange);
        if (!close) return;

        IWorld world = victim.World;
        state.Fire = world.Add(FireType, position, Marked()).Id;
        state.FireWorld = world;
        state.FireUntil = _now + FireSeconds;
        state.FireReadyAt = _now + FireCooldown;
    }

    void Extinguish(State state)
    {
        if (state.Fire != 0) state.FireWorld?.Remove(state.Fire);
        state.Fire = 0;
        state.FireWorld = null;
    }

    Rig Build(IWorld world, int kills, Vector3 position)
    {
        Sweep(world);
        var rig = new Rig(world, position);
        try
        {
            rig.Root = world.Add("Group", position, Marked()).Id;
            rig.Spin = world.Add("Group", Vector3.Zero, Marked(), parentId: rig.Root).Id;
            rig.Text = world.Add(TextType, new Vector3(0, HeadHeight, 0), TextData(kills), parentId: rig.Root).Id;
            Aura(rig, Level(kills));
            return rig;
        }
        catch
        {
            if (rig.Root != 0) world.Remove(rig.Root);
            throw;
        }
    }

    void Aura(Rig rig, int level)
    {
        foreach (int id in rig.Aura.Concat(rig.Puffs).Concat(rig.Glitch)) rig.World.Remove(id);
        rig.Aura.Clear();
        rig.Puffs.Clear();
        rig.Glitch.Clear();
        rig.AuraLevel = level;
        if (level == 0) return;

        Look look = Looks[level];
        float glow = level >= GodLevel ? TopGlowIntensity : GlowIntensity;
        rig.Aura.Add(rig.World.Add("PointLight", new Vector3(0, BodyHeight, 0), LightData(look.Glow, GlowRange, glow), parentId: rig.Root).Id);
        for (int i = 0; i < RingLights; i++)
        {
            float angle = MathF.Tau * i / RingLights;
            var spot = new Vector3(MathF.Cos(angle) * RingRadius, BodyHeight, MathF.Sin(angle) * RingRadius);
            rig.Aura.Add(rig.World.Add("PointLight", spot, LightData(look.Ring, RingRange, RingIntensity), parentId: rig.Spin).Id);
        }
        Puff(rig);

        if (level < MutantLevel) return;
        (Vector3 at, Quaternion? turn)[] lines =
        [
            (new Vector3(-0.9f, HeadHeight, 0f), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2)),
            (new Vector3(0.9f, HeadHeight + 0.2f, 0f), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -MathF.PI / 2)),
            (new Vector3(0f, HeadHeight - 0.5f, 0f), null),
        ];
        foreach (var line in lines)
            rig.Glitch.Add(rig.World.Add(TextType, line.at, GlitchData(), rotation: line.turn, parentId: rig.Root).Id);
        rig.GlitchAt = _now;
    }

    void Puff(Rig rig)
    {
        foreach (int id in rig.Puffs) rig.World.Remove(id);
        rig.Puffs.Clear();
        float[] color = Looks[rig.AuraLevel].Ring;
        float phase = (float)_random.NextDouble() * MathF.Tau;
        for (int i = 0; i < PuffCount; i++)
        {
            float angle = phase + MathF.Tau * i / PuffCount;
            var spot = new Vector3(MathF.Cos(angle) * 0.9f, 0.6f + 0.6f * i, MathF.Sin(angle) * 0.9f);
            rig.Puffs.Add(rig.World.Add(SmokeType, spot, PuffData(color), scale: Vector3.One * PuffSize, parentId: rig.Spin).Id);
        }
        rig.PuffsAt = _now + PuffLife;
    }

    void Glitch(Rig rig)
    {
        foreach (int id in rig.Glitch) rig.World.SetData(id, "text", GlitchText());
        rig.GlitchAt = _now + GlitchEvery;
    }

    void Tint(IWorld world)
    {
        bool wanted = WorldTint && _states.Any(pair => pair.Key.World == world && Level(pair.Value.Kills) == TintLevel);
        if (wanted)
        {
            List<Sky> saved = _skies.GetValueOrDefault(world) ?? [];
            foreach (IWorldObject sky in world.Objects.Where(obj => obj.Type == "Skybox"))
            {
                if (saved.Exists(entry => entry.Id == sky.Id)) continue;
                float fog = Number(sky, "fogDensity", DefaultFog);
                saved.Add(new Sky(sky.Id, Rgb(sky), fog));
                world.SetData(sky.Id, new Dictionary<string, object> { ["color"] = TintColor, ["fogDensity"] = Math.Max(fog, TintFog) });
            }
            if (saved.Count > 0) _skies[world] = saved;
        }
        else if (_skies.Remove(world, out List<Sky>? restore))
        {
            foreach (Sky sky in restore) Restore(world, sky);
        }
    }

    static void Restore(IWorld world, Sky sky) =>
        world.SetData(sky.Id, new Dictionary<string, object> { ["color"] = sky.Color, ["fogDensity"] = sky.Fog });

    void Sweep(IWorld world)
    {
        if (!_swept.Add(world)) return;
        foreach (IWorldObject obj in world.Objects.Where(obj => obj.Data.ContainsKey(Mark)).ToList())
            world.Remove(obj.Id);
    }

    static string Label(int kills) => kills.ToString(CultureInfo.InvariantCulture);

    string GlitchText()
    {
        int length = _random.Next(3, 9);
        var chars = new char[length];
        for (int i = 0; i < length; i++)
            chars[i] = _random.Next(5) == 0 ? ' ' : Glyphs[_random.Next(Glyphs.Length)];
        return new string(chars);
    }

    static Dictionary<string, object> Marked() => new() { [Mark] = true };

    static Dictionary<string, object> TextData(int kills) =>
        new() { ["text"] = Label(kills), ["textSize"] = TextSize, [Mark] = true };

    Dictionary<string, object> GlitchData() => new() { ["text"] = GlitchText(), [Mark] = true };

    static Dictionary<string, object> PuffData(float[] color) =>
        new() { ["color"] = color, ["alpha"] = PuffAlpha, [Mark] = true };

    static Dictionary<string, object> LightData(float[] color, float range, float intensity) =>
        new() { ["color"] = color, ["range"] = range, ["intensity"] = intensity, [Mark] = true };

    static float[] Rgb(IWorldObject sky)
    {
        if (!sky.Data.TryGetValue("color", out object? value) || value is null) return DefaultSky;
        if (value is float[] rgb && rgb.Length >= 3) return rgb;
        if (value is IEnumerable<object> items)
        {
            float[] parts = [.. items.Select(item => Convert.ToSingle(item, CultureInfo.InvariantCulture))];
            if (parts.Length >= 3) return parts;
        }
        return DefaultSky;
    }

    static float Number(IWorldObject obj, string key, float fallback) =>
        obj.Data.TryGetValue(key, out object? value) && value is not null
            ? Convert.ToSingle(value, CultureInfo.InvariantCulture)
            : fallback;
}
