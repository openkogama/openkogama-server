using System.Security.Cryptography;
using OpenKogama.Api;

namespace OpenKogama.Content;

public abstract class CustomAsset : IAsset
{
    public const int MaxName = 40;

    public int Id { get; internal set; }
    public required string Name { get; init; }
    public required string Group { get; init; }
    public required string Version { get; init; }
    public abstract string Kind { get; }
    public string Url => $"Custom/{Kind}/{Id}-{Version}.unity3d";

    public abstract bool Fits(string engine);
    public abstract byte[]? BundleFor(string engine);

    public static string VersionOf(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data))[..8];

    public static string Clean(string text, string fallback)
    {
        string trimmed = text.Trim();
        if (trimmed.Length == 0) return fallback;
        return trimmed.Length > MaxName ? trimmed[..MaxName] : trimmed;
    }
}

public sealed class CustomBundle : CustomAsset, IBundle
{
    public required byte[] Data { get; init; }
    public required string Engine { get; init; }
    public override string Kind => "bundle";

    public override bool Fits(string engine) => CustomContent.SameEngine(Engine, engine);
    public override byte[]? BundleFor(string engine) => Fits(engine) ? Data : null;
}

public sealed class CustomSound : CustomAsset, ISound
{
    public const string Prebuilt = "unity3d";
    public static readonly string[] Formats = ["mp3", "ogg", "wav", Prebuilt];

    public required byte[] Data { get; init; }
    public required string Format { get; init; }
    public override string Kind => "sound";

    public static string FormatOf(byte[] file)
    {
        ReadOnlySpan<byte> head = file.AsSpan(0, Math.Min(file.Length, 12));
        if (head.StartsWith("OggS"u8)) return "ogg";
        if (head.StartsWith("RIFF"u8) && head.Length == 12 && head[8..].SequenceEqual("WAVE"u8)) return "wav";
        if (head.StartsWith("ID3"u8) || head.Length >= 2 && head[0] == 0xFF && (head[1] & 0xE0) == 0xE0) return "mp3";
        if (head.StartsWith("Unity"u8)) return Prebuilt;
        throw new ArgumentException($"a sound has to be {string.Join(", ", Formats)}");
    }

    public override bool Fits(string key) => Format == Prebuilt
        ? CustomContent.SameEngine(UnityArchive.EngineOf(Data), key)
        : CustomContent.Template(Kind, key) is not null;

    public override byte[]? BundleFor(string key)
    {
        if (Format == Prebuilt) return Fits(key) ? Data : null;
        if (CustomContent.Template(Kind, key) is not byte[] template) return null;

        string cached = Path.Combine("bundles", "custom", key + "-" + VersionOf(template), $"{Kind}-{Id}-{Version}.unity3d");
        return CustomContent.Built(cached, () =>
        {
            Pcm audio = AudioFiles.Decode(Data, "." + Format);
            byte[] bundle = SoundBundles.Build(template, audio, $"{Id}-{Version}", new SoundFile(Data, Format));
            Console.WriteLine($"sound {Id} {Name}: {audio.Frames / audio.Rate} s, {audio.Channels} channels, {audio.Rate} Hz, bundle for {key} {bundle.Length / 1024} KB");
            return bundle;
        });
    }
}

public sealed class AssetLibrary<T>(string kind) where T : CustomAsset
{
    const int FirstId = 1_000_000;
    const int IdRange = 1_000_000;
    readonly object _sync = new();
    readonly List<T> _items = [];

    public IReadOnlyList<T> All
    {
        get { lock (_sync) return [.. _items]; }
    }

    public T Add(T item)
    {
        lock (_sync)
        {
            _items.RemoveAll(existing => existing.Name.Equals(item.Name, StringComparison.OrdinalIgnoreCase));
            int id = FirstId + (int)(Hash(kind + "/" + item.Name.ToLowerInvariant()) % IdRange);
            while (_items.Any(existing => existing.Id == id)) id = FirstId + (id - FirstId + 1) % IdRange;
            item.Id = id;
            _items.Add(item);
            return item;
        }
    }

    public bool Remove(int id)
    {
        lock (_sync) return _items.RemoveAll(item => item.Id == id) > 0;
    }

    public T? Find(int id)
    {
        lock (_sync) return _items.Find(item => item.Id == id);
    }

    public void Clear()
    {
        lock (_sync) _items.Clear();
    }

    static uint Hash(string text)
    {
        uint hash = 2166136261;
        foreach (char c in text) hash = (hash ^ c) * 16777619;
        return hash;
    }
}
