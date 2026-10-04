using System.Text.Json;
using System.Text.RegularExpressions;
using OpenKogama.Api;

namespace OpenKogama.Plugins;

sealed partial class PluginStorage : IPluginStorage
{
    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    readonly string _path;
    readonly Dictionary<string, string> _values;
    readonly object _sync = new();

    public PluginStorage(string folder, string name)
    {
        Directory.CreateDirectory(folder);
        _path = Path.Combine(folder, Unsafe().Replace(name, "_") + ".json");
        _values = File.Exists(_path) ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_path)) ?? [] : [];
    }

    public IReadOnlyCollection<string> Keys
    {
        get { lock (_sync) return [.. _values.Keys]; }
    }

    public string? Get(string key)
    {
        lock (_sync) return _values.GetValueOrDefault(key);
    }

    public void Set(string key, string? value)
    {
        lock (_sync)
        {
            if (value is null) _values.Remove(key);
            else _values[key] = value;
            File.WriteAllText(_path + ".tmp", JsonSerializer.Serialize(_values, Options));
            File.Move(_path + ".tmp", _path, overwrite: true);
        }
    }

    [GeneratedRegex(@"[^A-Za-z0-9_.-]")]
    private static partial Regex Unsafe();
}
