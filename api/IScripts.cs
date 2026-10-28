namespace OpenKogama.Api;

public interface IScripts
{
    IReadOnlyList<string> All { get; }
    bool Add(string name, IReadOnlyDictionary<string, byte[]> files);
    bool Remove(string name);
}
