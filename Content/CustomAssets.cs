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
    static readonly object Building = new();

    public required byte[] Data { get; init; }
    public required string Format { get; init; }
    public override string Kind => "sound";

    public override bool Fits(string engine) => Format == Prebuilt
        ? CustomContent.SameEngine(UnityArchive.EngineOf(Data), engine)
        : CustomContent.Template(Kind, engine) is not null;

    public override byte[]? BundleFor(string engine)
    {
        if (Format == Prebuilt) return Fits(engine) ? Data : null;
        if (CustomContent.Template(Kind, engine) is not byte[] template) return null;

        string cached = Path.Combine("bundles", "custom", engine, $"{Kind}-{Id}-{Version}.unity3d");
        lock (Building)
        {
            if (File.Exists(cached)) return File.ReadAllBytes(cached);
            Pcm audio = AudioFiles.Decode(Data, "." + Format);
            byte[] bundle = SoundBundles.Build(template, audio, $"{Id}-{Version}");
            Directory.CreateDirectory(Path.GetDirectoryName(cached)!);
            File.WriteAllBytes(cached + ".tmp", bundle);
            File.Move(cached + ".tmp", cached, overwrite: true);
            Console.WriteLine($"sound {Id} {Name}: {audio.Frames / audio.Rate} s, {audio.Channels} channels, {audio.Rate} Hz, bundle for {engine} {bundle.Length / 1024} KB");
            return bundle;
        }
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
