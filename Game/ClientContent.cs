using OpenKogama.Kogama.Protocols;
using OpenKogama.Photon;

namespace OpenKogama.Game;

public static class ClientContent
{
    public static StreamingAssetCatalog Streaming(PhotonPeer peer) => StreamingAssets.For(Rule(peer)?.Streaming ?? "2015");

    public static IEnumerable<Material> Materials(PhotonPeer peer)
    {
        ProtocolTable.ContentRule? rule = Rule(peer);
        IEnumerable<Material> materials = rule?.Materials is int count ? Game.Materials.For("2015").Take(count) : Game.Materials.For("2015");
        return rule?.MaterialPaths is { } paths
            ? materials.Select(material => paths.TryGetValue(material.Id, out string? path) ? material.WithPath(path) : material)
            : materials;
    }

    static ProtocolTable.ContentRule? Rule(PhotonPeer peer) => (peer.Translator as LegacyTranslator)?.Content;
}
