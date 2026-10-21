using OpenKogama.Api;
using OpenKogama.Content;

namespace OpenKogama.Plugins;

sealed class PluginAssets : IAssets
{
    public ISounds Sounds { get; } = new PluginSounds();
    public IBundles Bundles { get; } = new PluginBundles();
}

sealed class PluginSounds : ISounds
{
    public IReadOnlyList<ISound> All => CustomContent.Sounds.All;
    public IReadOnlyList<GameSound> Replaced => CustomContent.ReplacedSounds.All;

    public ISound Add(string name, byte[] file, SoundGroup group) =>
        Add(name, file, CustomSound.FormatOf(file), CustomContent.GroupName(group));

    public ISound Add(string name, byte[] file, string format, string group = "Custom")
    {
        string kind = format.TrimStart('.').ToLowerInvariant();
        if (!CustomSound.Formats.Contains(kind))
            throw new ArgumentException($"a sound has to be {string.Join(", ", CustomSound.Formats)}", nameof(format));
        if (kind == CustomSound.Prebuilt) UnityArchive.EngineOf(file);

        return CustomContent.Sounds.Add(new CustomSound
        {
            Name = CustomAsset.Clean(name, "Sound"),
            Group = CustomAsset.Clean(group, "Custom"),
            Data = file,
            Format = kind,
            Version = CustomAsset.VersionOf(file),
        });
    }

    public bool Remove(ISound sound) => CustomContent.Sounds.Remove(sound.Id);

    public void Replace(GameSound sound, byte[] file)
    {
        if (!Enum.IsDefined(sound)) throw new ArgumentOutOfRangeException(nameof(sound));
        string format = CustomSound.FormatOf(file);
        if (format == CustomSound.Prebuilt) UnityArchive.EngineOf(file);
        CustomContent.ReplacedSounds.Set(new ReplacedSound(sound, file, format));
    }

    public bool Restore(GameSound sound) => CustomContent.ReplacedSounds.Remove(sound);
}

sealed class PluginBundles : IBundles
{
    public IReadOnlyList<IBundle> All => CustomContent.Bundles.All;
    public IReadOnlyList<string> Game => GameBundles.All;
    public IReadOnlyList<string> Replaced => CustomContent.ReplacedBundles.All;

    public IBundle Add(string name, byte[] bundle, string group = "Custom") =>
        CustomContent.Bundles.Add(new CustomBundle
        {
            Name = CustomAsset.Clean(name, "Bundle"),
            Group = CustomAsset.Clean(group, "Custom"),
            Data = bundle,
            Engine = UnityArchive.EngineOf(bundle),
            Version = CustomAsset.VersionOf(bundle),
        });

    public bool Remove(IBundle bundle) => CustomContent.Bundles.Remove(bundle.Id);

    public void Replace(string path, byte[] bundle)
    {
        string known = GameBundles.Find(path) ?? throw new ArgumentException($"the game has no bundle {path}, Bundles.Game lists them", nameof(path));
        ReplacedBundle replaced;
        try
        {
            replaced = ReplacedBundle.Of(known, bundle);
        }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or ArgumentOutOfRangeException or IndexOutOfRangeException)
        {
            throw new ArgumentException($"the server cannot read this bundle: {error.Message}", nameof(bundle));
        }
        CustomContent.ReplacedBundles.Set(replaced);

        List<string> sharing = [.. GameBundles.SharingCache(known)];
        if (sharing.Count > 0)
            Console.WriteLine($"bundle {known}: the game caches it under one name with {string.Join(", ", sharing)}, replace those too or a map may still show the old one");
    }

    public bool Restore(string path) => CustomContent.ReplacedBundles.Remove(path);
}
