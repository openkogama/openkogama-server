namespace OpenKogama.Game;

public sealed class Triggers
{
    readonly Dictionary<int, HashSet<int>> _inside = [];
    readonly Dictionary<int, bool> _switches = [];

    public bool Switch(int objectId, bool on, bool initial)
    {
        lock (_inside)
        {
            if (_switches.GetValueOrDefault(objectId, initial) == on) return false;
            _switches[objectId] = on;
            return true;
        }
    }

    public bool IsOn(int objectId, bool initial)
    {
        lock (_inside) return _switches.GetValueOrDefault(objectId, initial);
    }

    public bool Enter(int objectId, int actor)
    {
        lock (_inside)
        {
            if (!_inside.TryGetValue(objectId, out HashSet<int>? actors))
                _inside[objectId] = actors = [];
            return actors.Add(actor) && actors.Count == 1;
        }
    }

    public bool Exit(int objectId, int actor)
    {
        lock (_inside)
        {
            return _inside.TryGetValue(objectId, out HashSet<int>? actors)
                && actors.Remove(actor)
                && actors.Count == 0;
        }
    }

    public bool IsPressed(int objectId)
    {
        lock (_inside) return _inside.TryGetValue(objectId, out HashSet<int>? actors) && actors.Count > 0;
    }

    public List<int> ExitAll(int actor)
    {
        lock (_inside)
        {
            return [.. _inside.Where(entry => entry.Value.Remove(actor) && entry.Value.Count == 0).Select(entry => entry.Key)];
        }
    }
}
