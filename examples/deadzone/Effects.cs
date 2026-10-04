using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using OpenKogama.Api;

namespace Deadzone;

sealed class Effects(IPluginStorage storage)
{
    public const string Marker = "deadzoneCycle";
    public const float CameraDistance = 4f;

    const string Added = "deadzoneAdded";

    static readonly float[] DefaultColor = [95f / 255f, 180f / 255f, 254f / 255f];

    static readonly Dictionary<string, object> CameraDefaults = new()
    {
        ["height"] = 3f,
        ["smoothness"] = 0.46f,
        ["tiltAdjust"] = 5f,
        ["distanceToAvatar"] = 10f,
        ["avatarWorldCollision"] = false,
        ["speedDistanceModifier"] = 12f,
    };

    sealed record Sky(float SunAngle, float[] Color, float FogDensity);

    sealed class State(bool enabled)
    {
        public bool Enabled { get; set; } = enabled;
        public int Skybox { get; set; }
    }

    readonly DayNightCycle _cycle = new();

    public Func<int, string?>? NightName
    {
        get => _cycle.NightName;
        set => _cycle.NightName = value;
    }

    public int? LastNight
    {
        get => _cycle.LastNight;
        set => _cycle.LastNight = value;
    }

    public Action<IWorld, DayNightCycle.Phase>? PhaseStarted
    {
        get => _cycle.PhaseStarted;
        set => _cycle.PhaseStarted = value;
    }
    readonly ConditionalWeakTable<IWorld, State> _states = [];
    readonly object _sync = new();

    public void Update(IWorld world)
    {
        if (world.Id == 0 || world.Players.Count == 0) return;
        lock (_sync)
        {
            State state = StateOf(world);
            if (!state.Enabled) return;
            if (state.Skybox == 0 || world.Find(state.Skybox) is null) state.Skybox = Apply(world);
            _cycle.Update(world, state.Skybox);
        }
    }

    public bool? Toggle(IWorld world)
    {
        if (world.Id == 0) return null;
        lock (_sync)
        {
            State state = StateOf(world);
            state.Enabled = !state.Enabled;
            storage.Set(world.Id.ToString(), state.Enabled ? "on" : "off");
            if (state.Enabled)
            {
                _cycle.Restart(world);
                state.Skybox = Apply(world);
            }
            else
            {
                Restore(world);
                state.Skybox = 0;
            }
            return state.Enabled;
        }
    }

    public DayNightCycle.Phase? Current(IWorld world)
    {
        lock (_sync) return world.Id != 0 && StateOf(world).Enabled ? _cycle.Current(world) : null;
    }

    public void EndNight(IWorld world)
    {
        lock (_sync) _cycle.EndNight(world);
    }

    public bool Set(IWorld world, DayNightCycle.Phase phase)
    {
        lock (_sync)
        {
            if (world.Id == 0 || !StateOf(world).Enabled) return false;
            _cycle.Set(world, phase);
            return true;
        }
    }

    public bool Jump(IWorld world, bool night)
    {
        lock (_sync)
        {
            if (world.Id == 0 || !StateOf(world).Enabled) return false;
            _cycle.Jump(world, night);
            return true;
        }
    }

    State StateOf(IWorld world) => _states.GetValue(world, created => new State(storage.Get(created.Id.ToString()) switch
    {
        "on" => true,
        "off" => false,
        _ => created.Objects.Any(obj => obj.Type == "Skybox" && obj.Data.ContainsKey(Marker)),
    }));

    int Apply(IWorld world)
    {
        IReadOnlyList<IWorldObject> objects = world.Objects;
        Vector3 anchor = (objects.FirstOrDefault(obj => obj.Type.StartsWith("SpawnPoint"))?.Position ?? Vector3.Zero) + new Vector3(0, 3, 0);

        if (objects.FirstOrDefault(obj => obj.Type == "CameraSettings") is { } camera)
        {
            if (!camera.Data.ContainsKey(Added) && storage.Get(Key(world, "camera")) is null)
                storage.Set(Key(world, "camera"), Number(camera, "distanceToAvatar", 10f).ToString(CultureInfo.InvariantCulture));
            world.SetData(camera.Id, "distanceToAvatar", CameraDistance);
        }
        else
        {
            world.Add("CameraSettings", anchor + Vector3.UnitX, new Dictionary<string, object>(CameraDefaults) { ["distanceToAvatar"] = CameraDistance, [Added] = true });
        }

        if (objects.FirstOrDefault(obj => obj.Type == "Skybox") is not { } skybox)
            return world.Add("Skybox", anchor, new Dictionary<string, object>(DayNightCycle.Start) { [Added] = true }).Id;

        if (!skybox.Data.ContainsKey(Added) && storage.Get(Key(world, "sky")) is null)
        {
            float[] color = skybox.Data.TryGetValue("color", out object? value) && value is float[] rgb ? rgb : DefaultColor;
            storage.Set(Key(world, "sky"), JsonSerializer.Serialize(new Sky(Number(skybox, "sunAngle", 80f), color, Number(skybox, "fogDensity", 0.007f))));
        }
        return skybox.Id;
    }

    void Restore(IWorld world)
    {
        foreach (IWorldObject obj in world.Objects.Where(obj => obj.Data.ContainsKey(Added)))
            world.Remove(obj.Id);

        IReadOnlyList<IWorldObject> objects = world.Objects;
        if (storage.Get(Key(world, "sky")) is { } saved && JsonSerializer.Deserialize<Sky>(saved) is { } sky
            && objects.FirstOrDefault(obj => obj.Type == "Skybox") is { } skybox)
        {
            world.SetData(skybox.Id, new Dictionary<string, object>
            {
                ["sunAngle"] = sky.SunAngle,
                ["color"] = sky.Color,
                ["fogDensity"] = sky.FogDensity,
            });
        }
        if (storage.Get(Key(world, "camera")) is { } distance && objects.FirstOrDefault(obj => obj.Type == "CameraSettings") is { } camera)
            world.SetData(camera.Id, "distanceToAvatar", float.Parse(distance, CultureInfo.InvariantCulture));

        storage.Set(Key(world, "sky"), null);
        storage.Set(Key(world, "camera"), null);
    }

    static string Key(IWorld world, string part) => $"{world.Id}:{part}";

    static float Number(IWorldObject obj, string key, float fallback) =>
        obj.Data.TryGetValue(key, out object? value) ? Convert.ToSingle(value, CultureInfo.InvariantCulture) : fallback;
}
