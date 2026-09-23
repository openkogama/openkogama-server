using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.World;

namespace OpenKogama.Game;

public sealed class Logic(Session session)
{
    const int MaxPasses = 64;
    const int NoOutput = -1;

    enum TimerState { Listening, Counting, Firing, Resetting, Done }

    static readonly HashSet<WorldObjectType> Stateful =
        [WorldObjectType.ToggleBox, WorldObjectType.RandomBox, WorldObjectType.TimeTrigger];

    readonly Dictionary<int, bool> _inputs = [];
    readonly Dictionary<int, bool> _timerOutputs = [];
    readonly Dictionary<int, int> _timerVersions = [];
    readonly object _sync = new();

    public Func<int> Clock { get; set; } = () => Environment.TickCount;

    public void Evaluate(bool react = true)
    {
        lock (_sync)
        {
            for (int round = 0; round < MaxPasses; round++)
            {
                Snapshot world = session.World.ToSnapshot();
                Dictionary<int, bool> inputs = Propagate(world);
                bool changed = false;

                foreach (WorldObject obj in world.Objects.Where(obj => Stateful.Contains(obj.Type)))
                {
                    bool input = inputs.GetValueOrDefault(obj.Id);
                    bool before = _inputs.GetValueOrDefault(obj.Id);
                    _inputs[obj.Id] = input;

                    if (react && input != before)
                        changed |= OnEdge(obj, input, OutputCount(world, obj.Id));
                }

                if (!changed) return;
            }
        }
    }

    public void Tick()
    {
        if (session.World.FindFirst(WorldObjectType.PulseBox) is not null)
            Evaluate();
    }

    public void Reset(IEnumerable<int> ids)
    {
        lock (_sync)
        {
            foreach (int id in ids)
            {
                WorldObject? obj = session.World.Find(id);
                switch (obj?.Type)
                {
                    case WorldObjectType.ToggleBox when State(obj):
                        SetData(id, "state", false);
                        break;
                    case WorldObjectType.RandomBox when CurrentOutput(obj) != NoOutput:
                        SetData(id, "currentOutput", NoOutput);
                        break;
                    case WorldObjectType.TimeTrigger:
                        _timerVersions[id] = _timerVersions.GetValueOrDefault(id) + 1;
                        _timerOutputs[id] = false;
                        if (Timer(obj) != TimerState.Listening) SetTimer(id, TimerState.Listening);
                        break;
                }
            }
        }
        Evaluate(react: false);
    }

    bool OnEdge(WorldObject obj, bool rising, int outputCount)
    {
        switch (obj.Type)
        {
            case WorldObjectType.ToggleBox when rising:
                if (Once(obj) && State(obj)) return false;
                SetData(obj.Id, "state", !State(obj));
                return true;

            case WorldObjectType.RandomBox:
                int pick = rising && outputCount > 0 ? Random.Shared.Next(outputCount) : NoOutput;
                if (pick == CurrentOutput(obj)) return false;
                SetData(obj.Id, "currentOutput", pick);
                return true;

            case WorldObjectType.TimeTrigger when rising && Timer(obj) == TimerState.Listening:
                SetTimer(obj.Id, TimerState.Counting);
                After(obj.Id, Seconds(obj, "time"), OnCounted);
                return true;

            case WorldObjectType.TimeTrigger when !rising && Timer(obj) is TimerState.Resetting:
                SetTimer(obj.Id, TimerState.Listening);
                return true;

            case WorldObjectType.TimeTrigger when !rising && Timer(obj) is TimerState.Firing && Seconds(obj, "duration") <= 0:
                SetTimer(obj.Id, Once(obj) ? TimerState.Done : TimerState.Listening);
                return true;

            default:
                return false;
        }
    }

    void OnCounted(int id)
    {
        if (session.World.Find(id) is not { } timer || Timer(timer) != TimerState.Counting) return;

        SetTimer(id, TimerState.Firing);
        float duration = Seconds(timer, "duration");
        if (duration > 0) After(id, duration, OnFired);
    }

    void OnFired(int id)
    {
        if (session.World.Find(id) is not { } timer || Timer(timer) != TimerState.Firing) return;

        if (Once(timer))
        {
            SetTimer(id, TimerState.Resetting);
            SetTimer(id, TimerState.Done);
            return;
        }

        bool inputHot = _inputs.GetValueOrDefault(id);
        SetTimer(id, inputHot ? TimerState.Resetting : TimerState.Listening);
    }

    void After(int id, float seconds, Action<int> action)
    {
        int version = _timerVersions.GetValueOrDefault(id);
        _ = Task.Delay(TimeSpan.FromSeconds(Math.Max(seconds, 0))).ContinueWith(_ =>
        {
            lock (_sync)
            {
                if (_timerVersions.GetValueOrDefault(id) != version) return;
                action(id);
            }
            Evaluate();
        });
    }

    Dictionary<int, bool> Propagate(Snapshot world)
    {
        var byId = world.Objects.ToDictionary(obj => obj.Id);
        var incoming = world.Links.GroupBy(link => link.To).ToDictionary(group => group.Key, group => group.ToList());
        var outputIndex = world.Links.GroupBy(link => link.From)
            .SelectMany(group => group.Select((link, index) => (link.Id, index)))
            .ToDictionary(entry => entry.Id, entry => entry.index);

        var outputs = new Dictionary<int, bool>();
        var inputs = new Dictionary<int, bool>();

        bool LinkValue(Link link) =>
            byId.TryGetValue(link.From, out WorldObject? from) && from.Type == WorldObjectType.RandomBox
                ? outputIndex[link.Id] == CurrentOutput(from)
                : outputs.GetValueOrDefault(link.From);

        for (int pass = 0; pass < MaxPasses; pass++)
        {
            bool changed = false;
            foreach (WorldObject obj in world.Objects)
            {
                bool hasInputs = incoming.TryGetValue(obj.Id, out List<Link>? links);
                List<bool> values = hasInputs ? [.. links!.Select(LinkValue)] : [];
                bool input = obj.Type == WorldObjectType.And
                    ? values.Count > 0 && values.All(value => value)
                    : values.Any(value => value);
                inputs[obj.Id] = input;

                bool output = obj.Type switch
                {
                    WorldObjectType.TriggerBox or WorldObjectType.PressurePlate => session.Triggers.IsPressed(obj.Id),
                    WorldObjectType.Battery => true,
                    WorldObjectType.ToggleBox => State(obj),
                    WorldObjectType.Negate => !input,
                    WorldObjectType.PulseBox => (!hasInputs || input) && PulseOn(obj),
                    WorldObjectType.TimeTrigger => TimerOutput(obj),
                    _ => input,
                };

                if (!outputs.TryGetValue(obj.Id, out bool old) || old != output)
                {
                    outputs[obj.Id] = output;
                    changed = true;
                }
            }
            if (!changed) break;
        }

        return inputs;
    }

    bool PulseOn(WorldObject pulse)
    {
        int on = (int)(Seconds(pulse, "intervalOn") * 1000);
        int off = (int)(Seconds(pulse, "intervalOff") * 1000);
        if (on + off <= 0) return false;
        return Math.Abs(Clock()) % (on + off) <= on;
    }

    bool TimerOutput(WorldObject timer)
    {
        bool latched = _timerOutputs.GetValueOrDefault(timer.Id);
        float duration = Seconds(timer, "duration");

        bool output = Timer(timer) switch
        {
            TimerState.Firing => true,
            TimerState.Resetting => false,
            TimerState.Counting => duration > 0 && latched,
            TimerState.Listening => duration <= 0 && latched,
            _ => latched,
        };

        _timerOutputs[timer.Id] = output;
        return output;
    }

    static int OutputCount(Snapshot world, int id) => world.Links.Count(link => link.From == id);

    static bool Once(WorldObject obj) => obj.Data.Find(pair => pair.Key == "once").Value as bool? ?? false;

    static bool State(WorldObject obj) => obj.Data.Find(pair => pair.Key == "state").Value as bool? ?? false;

    static TimerState Timer(WorldObject obj) => (TimerState)(obj.Data.Find(pair => pair.Key == "state").Value as int? ?? 0);

    static int CurrentOutput(WorldObject obj) => obj.Data.Find(pair => pair.Key == "currentOutput").Value as int? ?? NoOutput;

    static float Seconds(WorldObject obj, string key) => obj.Data.Find(pair => pair.Key == key).Value switch
    {
        float value => value,
        int value => value,
        _ => 0f,
    };

    void SetTimer(int id, TimerState state) => SetData(id, "state", (int)state);

    void SetData(int id, string key, object value)
    {
        var change = new Dictionary<object, object?> { [key] = value };
        session.World.Modify(id, obj => PackedData.Merge(obj.Data, change));
        session.World.MarkChanged();

        var evt = new EventData((byte)EventCode.UpdateWorldObjectDataPartial)
        {
            Parameters =
            {
                [(byte)ParameterKey.WorldObjectID] = id,
                [(byte)ParameterKey.WorldObjectData] = change,
            },
        };
        foreach (Player player in session.Players)
            player.Peer.Send(evt);
    }
}
