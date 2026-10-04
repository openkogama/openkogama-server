namespace OpenKogama.Api;

public interface ITemplates
{
    IReadOnlyList<string> Ids { get; }
    void Add(string id, string name, byte[] kgmap);
    void Add(string id, string name, byte[] kgmap, Action<IWorldBuilder> build);
    void Add(string id, string name, Action<IWorldBuilder> build, string? baseTemplate = null);
}
