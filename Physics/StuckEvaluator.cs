namespace OpenKogama.Physics;

sealed class StuckEvaluator(Func<List<int>> overlapping)
{
    const float UpdateInterval = 0.2f;
    const float TimeBeforeStuck = 2f;

    readonly Dictionary<int, float> _stuckSince = [];
    float _updated;

    public bool Update(float time)
    {
        if (time - _updated < UpdateInterval && _stuckSince.Count == 0) return false;
        _updated = time;

        List<int> objects = overlapping();
        if (objects.Count == 0)
        {
            _stuckSince.Clear();
            return false;
        }
        foreach (int left in _stuckSince.Keys.Except(objects).ToList()) _stuckSince.Remove(left);
        foreach (int id in objects) _stuckSince.TryAdd(id, time);
        return _stuckSince.Values.Any(since => time - since >= TimeBeforeStuck);
    }
}
