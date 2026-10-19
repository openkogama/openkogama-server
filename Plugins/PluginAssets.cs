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
}

sealed class PluginBundles : IBundles
{
    public IReadOnlyList<IBundle> All => CustomContent.Bundles.All;

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
}
