namespace OpenKogama.Api;

public interface IPluginStorage
{
    IReadOnlyCollection<string> Keys { get; }
    string? Get(string key);
    void Set(string key, string? value);
}
