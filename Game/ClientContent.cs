using OpenKogama.Kogama.Protocols;
using OpenKogama.Photon;

namespace OpenKogama.Game;

public static class ClientContent
{
    public static StreamingAssetCatalog Streaming(PhotonPeer peer, string? build) =>
        peer.Translator is not LegacyTranslator && build is not null && BundleSets.For(build) is string set
            ? StreamingAssets.ForSet(set)
            : StreamingAssets.For(Rule(peer)?.Streaming ?? "2015");

    static readonly (Version Since, int[] Ids)[] AddedMaterials = [(new(1, 30), [59]), (new(2, 30, 4), [60, 61, 62])];

    public static IEnumerable<Material> Materials(PhotonPeer peer, string? build)
    {
        ProtocolTable.ContentRule? rule = Rule(peer);
        IEnumerable<Material> materials = rule?.Materials is int count ? Game.Materials.For("2015").Take(count) : Game.Materials.For("2015");
        if (peer.Translator is not LegacyTranslator && Version.TryParse(build, out Version? client))
        {
            var added = AddedMaterials.Where(entry => client >= entry.Since).SelectMany(entry => entry.Ids).ToHashSet();
            materials = materials.Concat(Game.Materials.For("2025").Where(material => added.Contains(material.Id)));
        }
        return rule?.MaterialPaths is { } paths
            ? materials.Select(material => paths.TryGetValue(material.Id, out string? path) ? material.WithPath(path) : material)
            : materials;
    }

    static ProtocolTable.ContentRule? Rule(PhotonPeer peer) => (peer.Translator as LegacyTranslator)?.Content;
}
