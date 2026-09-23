namespace OpenKogama.Game;

public sealed class Triggers
{
    readonly Dictionary<int, HashSet<int>> _inside = [];

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

    public List<int> ExitAll(int actor)
    {
        lock (_inside)
        {
            return [.. _inside.Where(entry => entry.Value.Remove(actor) && entry.Value.Count == 0).Select(entry => entry.Key)];
        }
    }
}
