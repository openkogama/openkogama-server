using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.World;

namespace OpenKogama.Game;

public sealed class Logic(Session session)
{
    const int MaxPasses = 64;
    const int MaxCyclePasses = 256;
    const int NoOutput = -1;

    sealed record Graph(
        int Version,
        WorldObject[] Nodes,
        Dictionary<int, int> Index,
        int[][] Sources,
        int[][] Slots,
        int[] OutputCounts,
        int[] Stateful,
        int[] Pulses,
        bool Cyclic);

    enum TimerState { Listening, Counting, Firing, Resetting, Done }

    static readonly HashSet<WorldObjectType> Stateful =
        [WorldObjectType.ToggleBox, WorldObjectType.RandomBox, WorldObjectType.TimeTrigger, WorldObjectType.CountingCube];

    readonly Dictionary<int, bool> _inputs = [];
    readonly Dictionary<int, bool> _timerOutputs = [];
    readonly Dictionary<int, int> _timerVersions = [];
    readonly Dictionary<int, (int Start, int Value)> _counts = [];
    readonly Dictionary<int, bool> _pulses = [];
    readonly object _sync = new();
    Graph? _graph;
    bool _dirty;

    public Action<int>? DataChanged { get; set; }

    public void Evaluate(bool react = true)
    {
        lock (_sync)
        {
            for (int round = 0; round < MaxPasses; round++)
            {
                Graph graph = GraphFor();
                bool[] inputs = Propagate(graph);
                bool changed = false;

                foreach (int index in graph.Stateful)
                {
                    WorldObject obj = graph.Nodes[index];
                    bool before = _inputs.GetValueOrDefault(obj.Id);
                    _inputs[obj.Id] = inputs[index];
                    if (react && inputs[index] != before)
                        changed |= OnEdge(obj, inputs[index], graph.OutputCounts[index]);
                }

                foreach (int id in _inputs.Where(entry => entry.Value && !graph.Index.ContainsKey(entry.Key)).Select(entry => entry.Key).ToList())
                {
                    _inputs[id] = false;
                    if (react && session.World.Find(id) is { } obj)
                        changed |= OnEdge(obj, false, 0);
                }

                if (!changed) return;
            }
        }
    }

    public void Resync(Player player)
    {
        if (DataChanged is not null) return;

        foreach (WorldObject obj in session.World.LogicGraph().Objects)
        {
            if (obj.Type == WorldObjectType.CountingCube)
            {
                player.Peer.Send(CountEvent(obj.Id, Count(obj)));
                continue;
            }

            if (obj.Type is WorldObjectType.ShootableButton or WorldObjectType.UseLever)
            {
                bool on = session.Triggers.IsOn(obj.Id, StartsOn(obj));
                if (on == StartsOn(obj)) continue;
                player.Peer.Send(StayEvent(obj.Id, player.Actor, on));
                continue;
            }

            if (!Stateful.Contains(obj.Type)) continue;
            string key = obj.Type == WorldObjectType.RandomBox ? "currentOutput" : "state";
            if (obj.Data.Find(pair => pair.Key == key).Value is not { } value) continue;

            player.Peer.Send(new EventData((byte)EventCode.UpdateWorldObjectDataPartial)
            {
                Parameters =
                {
                    [(byte)ParameterKey.WorldObjectID] = obj.Id,
                    [(byte)ParameterKey.WorldObjectData] = new Dictionary<object, object?> { [key] = value },
                },
            });
        }
    }

    public void Tick()
    {
        bool changed;
        lock (_sync)
        {
            changed = _dirty;
            _dirty = false;
            Graph graph = GraphFor();
            foreach (int index in graph.Pulses)
            {
                bool on = PulseOn(graph.Nodes[index]);
                if (_pulses.TryGetValue(graph.Nodes[index].Id, out bool before) && before == on) continue;
                _pulses[graph.Nodes[index].Id] = on;
                changed = true;
            }
        }
        if (changed) Evaluate();
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
                    case WorldObjectType.TimeTrigger when Classic(obj):
                        _timerVersions[id] = _timerVersions.GetValueOrDefault(id) + 1;
                        if (Seconds(obj, "currentTime") != Seconds(obj, "time")) SetData(id, "currentTime", Seconds(obj, "time"));
                        break;
                    case WorldObjectType.CountingCube:
                        _counts[id] = (StartingValue(obj), StartingValue(obj));
                        break;
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

            case WorldObjectType.CountingCube when rising:
                int count = Count(obj);
                int next = count == 0 && Flag(obj, "reset") ? StartingValue(obj) : Math.Max(count - 1, 0);
                if (next == count) return false;
                _counts[obj.Id] = (StartingValue(obj), next);
                Broadcast(CountEvent(obj.Id, next));
                return true;

            case WorldObjectType.RandomBox:
                int pick = rising && outputCount > 0 ? Random.Shared.Next(outputCount) : NoOutput;
                if (pick == CurrentOutput(obj)) return false;
                SetData(obj.Id, "currentOutput", pick);
                return true;

            case WorldObjectType.TimeTrigger when Classic(obj) && rising:
                if (Seconds(obj, "currentTime") > 0) After(obj.Id, Seconds(obj, "currentTime"), OnClassicCounted);
                return false;

            case WorldObjectType.TimeTrigger when Classic(obj):
                _timerVersions[obj.Id] = _timerVersions.GetValueOrDefault(obj.Id) + 1;
                if (Once(obj) || Seconds(obj, "currentTime") == Seconds(obj, "time")) return false;
                SetData(obj.Id, "currentTime", Seconds(obj, "time"));
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

    public void ReleaseLater(WorldObject button)
    {
        lock (_sync) _timerVersions[button.Id] = _timerVersions.GetValueOrDefault(button.Id) + 1;
        After(button.Id, Seconds(button, "duration"), id =>
        {
            if (session.Triggers.Switch(id, false, false))
                Broadcast(StayEvent(id, 0, false));
        });
    }

    void OnClassicCounted(int id)
    {
        if (session.World.Find(id) is not { } timer || Seconds(timer, "currentTime") <= 0) return;
        SetData(id, "currentTime", 0f);
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
                _dirty = true;
            }
        });
    }

    Graph GraphFor()
    {
        int version = session.World.LogicVersion;
        if (_graph?.Version == version) return _graph;

        Snapshot world = session.World.LogicGraph();
        var byId = world.Objects.ToDictionary(obj => obj.Id);
        var successors = world.Links.GroupBy(link => link.From)
            .ToDictionary(group => group.Key, group => group.Select(link => link.To).Distinct().ToList());
        var pending = world.Objects.ToDictionary(obj => obj.Id, obj => 0);
        foreach (Link link in world.Links.DistinctBy(link => (link.From, link.To))) pending[link.To]++;

        var order = new List<WorldObject>();
        var ready = new Queue<int>(world.Objects.Where(obj => pending[obj.Id] == 0).Select(obj => obj.Id));
        while (ready.TryDequeue(out int id))
        {
            order.Add(byId[id]);
            foreach (int next in successors.GetValueOrDefault(id, []))
                if (--pending[next] == 0) ready.Enqueue(next);
        }
        bool cyclic = order.Count < world.Objects.Count;
        if (cyclic)
        {
            var placed = order.Select(obj => obj.Id).ToHashSet();
            order.AddRange(world.Objects.Where(obj => !placed.Contains(obj.Id)));
        }

        var position = new Dictionary<int, int>();
        foreach (WorldObject obj in order) position[obj.Id] = position.Count;
        var slot = world.Links.GroupBy(link => link.From)
            .SelectMany(group => group.Select((link, number) => (link.Id, number)))
            .ToDictionary(entry => entry.Id, entry => entry.number);
        var incoming = world.Links.GroupBy(link => link.To).ToDictionary(group => group.Key, group => group.ToList());
        var outgoing = world.Links.GroupBy(link => link.From).ToDictionary(group => group.Key, group => group.Count());

        WorldObject[] nodes = [.. order];
        int[] Sources(WorldObject obj) => incoming.TryGetValue(obj.Id, out List<Link>? links) ? [.. links.Select(link => position[link.From])] : [];
        int[] Slots(WorldObject obj) => incoming.TryGetValue(obj.Id, out List<Link>? links) ? [.. links.Select(link => slot[link.Id])] : [];
        return _graph = new Graph(
            version,
            nodes,
            position,
            [.. nodes.Select(Sources)],
            [.. nodes.Select(Slots)],
            [.. nodes.Select(obj => outgoing.GetValueOrDefault(obj.Id))],
            [.. Enumerable.Range(0, nodes.Length).Where(i => Stateful.Contains(nodes[i].Type))],
            [.. Enumerable.Range(0, nodes.Length).Where(i => nodes[i].Type == WorldObjectType.PulseBox)],
            cyclic);
    }

    bool[] Propagate(Graph graph)
    {
        WorldObject[] nodes = graph.Nodes;
        var outputs = new bool[nodes.Length];
        var inputs = new bool[nodes.Length];
        int passes = graph.Cyclic ? MaxCyclePasses : 1;

        for (int pass = 0; pass < passes; pass++)
        {
            bool changed = false;
            for (int i = 0; i < nodes.Length; i++)
            {
                WorldObject obj = nodes[i];
                int[] sources = graph.Sources[i];
                bool hasInputs = sources.Length > 0;
                bool all = hasInputs, any = false;
                for (int k = 0; k < sources.Length; k++)
                {
                    WorldObject from = nodes[sources[k]];
                    bool value = from.Type == WorldObjectType.RandomBox
                        ? graph.Slots[i][k] == CurrentOutput(from)
                        : outputs[sources[k]];
                    all &= value;
                    any |= value;
                }
                bool input = obj.Type == WorldObjectType.And ? all : any;
                inputs[i] = input;

                bool output = obj.Type switch
                {
                    WorldObjectType.TriggerBox or WorldObjectType.PressurePlate => session.Triggers.IsPressed(obj.Id),
                    WorldObjectType.Battery => true,
                    WorldObjectType.ToggleBox => State(obj),
                    WorldObjectType.Negate => !input,
                    WorldObjectType.PulseBox => (!hasInputs || input) && PulseOn(obj),
                    WorldObjectType.TimeTrigger => TimerOutput(obj),
                    WorldObjectType.CountingCube => Count(obj) == 0,
                    WorldObjectType.ShootableButton or WorldObjectType.UseLever => session.Triggers.IsOn(obj.Id, StartsOn(obj)),
                    _ => input,
                };

                if (outputs[i] != output)
                {
                    outputs[i] = output;
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
        return Math.Abs(session.Clock()) % (on + off) <= on;
    }

    bool TimerOutput(WorldObject timer)
    {
        if (Classic(timer)) return Seconds(timer, "currentTime") <= 0;

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

    static bool Classic(WorldObject obj) => obj.Data.Exists(pair => pair.Key == "currentTime");

    static bool Once(WorldObject obj) => Flag(obj, "once");

    static bool Flag(WorldObject obj, string key) => obj.Data.Find(pair => pair.Key == key).Value as bool? ?? false;

    public static bool StartsOn(WorldObject obj) => obj.Type == WorldObjectType.UseLever && Flag(obj, "beginActivated");

    static int StartingValue(WorldObject obj) => obj.Data.Find(pair => pair.Key == "startingValue").Value as int? ?? 0;

    int Count(WorldObject obj)
    {
        lock (_sync)
        {
            int start = StartingValue(obj);
            if (_counts.TryGetValue(obj.Id, out var count) && count.Start == start) return count.Value;
            _counts[obj.Id] = (start, start);
            return start;
        }
    }

    static EventData StayEvent(int id, int actor, bool on) => new((byte)(on ? EventCode.TriggerBoxStayBegin : EventCode.TriggerBoxStayEnd))
    {
        Parameters = { [(byte)ParameterKey.WorldObjectID] = id, [(byte)ParameterKey.ActorNr] = actor },
    };

    static EventData CountEvent(int id, int value) => new((byte)EventCode.CountingCubeUpdate)
    {
        Parameters =
        {
            [(byte)ParameterKey.WorldObjectID] = id,
            [(byte)ParameterKey.CountingCubeCurrentValue] = value,
        },
    };

    void Broadcast(EventData evt)
    {
        foreach (Player player in session.Players)
            if (player.InWorld)
                player.Peer.Send(evt);
    }

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

        if (DataChanged is not null)
        {
            DataChanged(id);
            return;
        }

        Broadcast(new EventData((byte)EventCode.UpdateWorldObjectDataPartial)
        {
            Parameters =
            {
                [(byte)ParameterKey.WorldObjectID] = id,
                [(byte)ParameterKey.WorldObjectData] = change,
            },
        });
    }
}
