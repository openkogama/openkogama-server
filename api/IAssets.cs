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
    IReadOnlyList<GameSound> Replaced { get; }
    ISound Add(string name, byte[] file, SoundGroup group);
    ISound Add(string name, byte[] file, string format, string group = "Custom");
    bool Remove(ISound sound);
    void Replace(GameSound sound, byte[] file);
    bool Restore(GameSound sound);
}

public interface IBundles
{
    IReadOnlyList<IBundle> All { get; }
    IReadOnlyList<string> Game { get; }
    IReadOnlyList<string> Replaced { get; }
    IBundle Add(string name, byte[] bundle, string group = "Custom");
    bool Remove(IBundle bundle);
    void Replace(string path, byte[] bundle);
    bool Restore(string path);
}

public interface IAssets
{
    ISounds Sounds { get; }
    IBundles Bundles { get; }
}
