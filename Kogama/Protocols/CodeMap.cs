namespace OpenKogama.Kogama.Protocols;

public sealed class CodeMap
{
    readonly Dictionary<int, int> _map = [];
    readonly Dictionary<int, string> _missing = [];
    readonly Dictionary<int, string> _names = [];

    public CodeMap(Dictionary<string, int> from, Dictionary<string, int> to)
    {
        foreach (var (name, code) in from)
        {
            _names[code] = name;
            if (to.TryGetValue(name, out int target))
                _map[code] = target;
            else
                _missing[code] = name;
        }
    }

    public CodeMap(Dictionary<string, int> values)
    {
        foreach (var (from, to) in values)
            _map[int.Parse(from)] = to;
    }

    public int? Map(int code) =>
        _map.TryGetValue(code, out int target) ? target : _missing.ContainsKey(code) ? null : code;

    public string Name(int code) => _names.TryGetValue(code, out string? name) ? name : code.ToString();
}
