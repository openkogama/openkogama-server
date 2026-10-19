namespace OpenKogama.Api;

public interface IAsset
{
    int Id { get; }
    string Name { get; }
    string Group { get; }
}

public interface ISound : IAsset;

public interface IBundle : IAsset
{
    string Url { get; }
    string Engine { get; }
}

public interface ISounds
{
    IReadOnlyList<ISound> All { get; }
    ISound Add(string name, byte[] file, string format, string group = "Custom");
    bool Remove(ISound sound);
}

public interface IBundles
{
    IReadOnlyList<IBundle> All { get; }
    IBundle Add(string name, byte[] bundle, string group = "Custom");
    bool Remove(IBundle bundle);
}

public interface IAssets
{
    ISounds Sounds { get; }
    IBundles Bundles { get; }
}
