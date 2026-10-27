using OpenKogama.Api;
using OpenKogama.Mods.Patches;

namespace OpenKogama.Content;

public sealed record ReplacedSound(GameSound Sound, byte[] Data, string Format)
{
    public string Version { get; } = CustomAsset.VersionOf(Data);
}

public sealed class ReplacedSounds
{
    readonly object _sync = new();
    readonly Dictionary<GameSound, ReplacedSound> _sounds = [];

    public IReadOnlyList<GameSound> All
    {
        get { lock (_sync) return [.. _sounds.Keys.Order()]; }
    }

    public void Set(ReplacedSound sound)
    {
        lock (_sync) _sounds[sound.Sound] = sound;
    }

    public bool Remove(GameSound sound)
    {
        lock (_sync) return _sounds.Remove(sound);
    }

    public void Clear()
    {
        lock (_sync) _sounds.Clear();
    }

    public byte[] Apply(string set, string file, byte[] original)
    {
        if (GameSoundFiles.Find(file) is not GameSound sound) return original;
        ReplacedSound? replaced;
        lock (_sync) replaced = _sounds.GetValueOrDefault(sound);
        if (replaced is null) return original;

        try
        {
            return replaced.Format == CustomSound.Prebuilt ? Prebuilt(replaced, set, original) : Built(replaced, set, original);
        }
        catch (Exception error)
        {
            Console.WriteLine($"game sound {sound}: {error.Message}");
            return original;
        }
    }

    static byte[] Prebuilt(ReplacedSound replaced, string set, byte[] original) =>
        CustomContent.SameEngine(UnityArchive.EngineOf(replaced.Data), CustomContent.SoundKey(set)) ? replaced.Data : original;

    static byte[] Built(ReplacedSound replaced, string set, byte[] original)
    {
        string cached = Path.Combine("bundles", "custom", "replaced", set, $"{replaced.Sound}-{replaced.Version}-{CustomAsset.VersionOf(original)}.unity3d");
        return CustomContent.Built(cached, () =>
        {
            Pcm audio = AudioFiles.Decode(replaced.Data, "." + replaced.Format);
            byte[] bundle = SoundBundles.Swap(original, audio, new SoundFile(replaced.Data, replaced.Format));
            Console.WriteLine($"game sound {replaced.Sound}: {audio.Frames / audio.Rate} s, {audio.Channels} channels, {audio.Rate} Hz, bundle for {set} {bundle.Length / 1024} KB");
            return bundle;
        });
    }
}
