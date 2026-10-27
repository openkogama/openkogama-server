using OpenKogama.Game;
using OpenKogama.Mods.Patches;

namespace OpenKogama.Content;

public sealed record ReplacedBundle(string Path, byte[] Data, string Engine, bool WebGL)
{
    public static ReplacedBundle Of(string path, byte[] data)
    {
        (ArchiveFormat format, List<ArchiveNode> nodes) = UnityArchive.ReadWithFormat(data);
        ArchiveNode main = nodes.FirstOrDefault(node => (node.Flags & UnityArchive.SerializedFileNode) != 0)
            ?? throw new InvalidDataException("the bundle has no serialized file");
        return new ReplacedBundle(path, data, format.Engine, new SerializedFile(main.Data).Target == SerializedFile.WebGLTarget);
    }

    public bool Fits(string set)
    {
        bool webgl = set.EndsWith(BundleSets.WebGL, StringComparison.Ordinal);
        return webgl == WebGL && CustomContent.SameEngine(Engine, BundleSets.EngineOf(webgl ? set[..^BundleSets.WebGL.Length] : set));
    }
}

public sealed class ReplacedBundles
{
    readonly object _sync = new();
    readonly Dictionary<string, List<ReplacedBundle>> _bundles = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> All
    {
        get { lock (_sync) return [.. _bundles.Keys.Order(StringComparer.OrdinalIgnoreCase)]; }
    }

    public void Set(ReplacedBundle bundle)
    {
        lock (_sync)
        {
            if (!_bundles.TryGetValue(bundle.Path, out List<ReplacedBundle>? variants)) _bundles[bundle.Path] = variants = [];
            variants.RemoveAll(variant => variant.WebGL == bundle.WebGL && Family(variant.Engine) == Family(bundle.Engine));
            variants.Add(bundle);
        }
    }

    public bool Remove(string path)
    {
        lock (_sync) return _bundles.Remove(GameBundles.Normalize(path));
    }

    public void Clear()
    {
        lock (_sync) _bundles.Clear();
    }

    public byte[]? For(string set, string file)
    {
        lock (_sync)
            return _bundles.TryGetValue(GameBundles.Normalize(file), out List<ReplacedBundle>? variants)
                ? variants.LastOrDefault(variant => variant.Fits(set))?.Data
                : null;
    }

    static string Family(string engine) => string.Join('.', engine.Split('.').Take(2));
}
