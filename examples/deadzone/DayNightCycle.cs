using System.Runtime.CompilerServices;
using OpenKogama.Api;

namespace Deadzone;

sealed class DayNightCycle
{
    public readonly record struct Phase(int Day, bool Night);

    const long DayMs = 30_000;
    const double UpdateSeconds = 3;

    static readonly Dictionary<string, object> DaySky = new()
    {
        ["sunAngle"] = 50f,
        ["color"] = new[] { 95f / 255f, 180f / 255f, 254f / 255f },
        ["fogDensity"] = 0.010f,
    };

    static readonly Dictionary<string, object> NightSky = new()
    {
        ["sunAngle"] = 60f,
        ["color"] = new[] { 0.47f, 0.50f, 0.53f },
        ["fogDensity"] = 0.028f,
    };

    sealed class Clock(long now)
    {
        public Phase Phase { get; set; } = new(1, false);
        public long PhaseStarted { get; set; } = now;
        public long? Sent { get; set; }
        public Phase? Announced { get; set; }
        public bool Won { get; set; }
        public Queue<string> Cheers { get; } = [];
    }

    readonly ConditionalWeakTable<IWorld, Clock> _clocks = [];

    public Action<IWorld, Phase>? PhaseStarted { get; set; }
    public Func<int, string?>? NightName { get; set; }
    public int? LastNight { get; set; }

    public static Dictionary<string, object> Start => new(DaySky);

    public void Update(IWorld world, int skybox)
    {
        long now = Environment.TickCount64;
        Clock clock = _clocks.GetValue(world, _ => new Clock(now));
        if (!clock.Won && !clock.Phase.Night && now - clock.PhaseStarted >= DayMs) Begin(clock, clock.Phase with { Night = true }, now);
        if (clock.Cheers.TryDequeue(out string? cheer)) world.Notify(cheer, brief: true);
        if (now - clock.Sent < UpdateSeconds * 1000) return;
        clock.Sent = now;

        world.SetData(skybox, new Dictionary<string, object>(clock.Phase.Night ? NightSky : DaySky));
        if (clock.Announced is { } last && last == clock.Phase) return;
        if (!clock.Won) Announce(world, clock.Announced, clock.Phase);
        clock.Announced = clock.Phase;
        PhaseStarted?.Invoke(world, clock.Phase);
    }

    public void Restart(IWorld world) => _clocks.AddOrUpdate(world, new Clock(Environment.TickCount64));

    public Phase Current(IWorld world) => _clocks.TryGetValue(world, out Clock? clock) ? clock.Phase : new Phase(1, false);

    public void Jump(IWorld world, bool night)
    {
        long now = Environment.TickCount64;
        Clock clock = _clocks.GetValue(world, _ => new Clock(now));
        if (night && clock.Won) return;
        if (night) Begin(clock, clock.Phase.Night ? new Phase(clock.Phase.Day + 1, true) : clock.Phase with { Night = true }, now);
        else Begin(clock, new Phase(clock.Phase.Day + 1, false), now);
    }

    public void Set(IWorld world, Phase phase)
    {
        long now = Environment.TickCount64;
        Clock clock = _clocks.GetValue(world, _ => new Clock(now));
        clock.Won = false;
        clock.Cheers.Clear();
        clock.Announced = null;
        Begin(clock, phase, now);
    }

    public void EndNight(IWorld world)
    {
        if (!_clocks.TryGetValue(world, out Clock? clock) || !clock.Phase.Night) return;
        if (LastNight is int last && clock.Phase.Day >= last) Win(clock, clock.Phase.Day);
        Begin(clock, new Phase(clock.Phase.Day + 1, false), Environment.TickCount64);
    }

    static void Win(Clock clock, int nights)
    {
        clock.Won = true;
        foreach (string cheer in (string[])
        [
            "Victory!",
            $"You survived all {nights} nights!",
            "The zombies are gone",
            "The Deadzone is safe again",
            "Well done!",
            "Thanks for playing!",
        ])
            clock.Cheers.Enqueue(cheer);
    }

    static void Begin(Clock clock, Phase phase, long now)
    {
        clock.Phase = phase;
        clock.PhaseStarted = now;
        clock.Sent = null;
    }

    void Announce(IWorld world, Phase? previous, Phase phase)
    {
        if (phase.Night)
        {
            world.Notify(NightName?.Invoke(phase.Day) is string name ? $"Night {phase.Day}: {name}" : $"Night {phase.Day} - survive until dawn!");
            return;
        }
        if (previous is { Night: true } night)
            world.Notify($"You survived night {night.Day}!");
        world.Notify($"Day {phase.Day} - get ready");
    }
}
