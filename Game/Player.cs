using OpenKogama.Photon;

namespace OpenKogama.Game;

public sealed class Player(PhotonPeer peer, int actor, int avatarId)
{
    public PhotonPeer Peer => peer;
    public int Actor => actor;
    public int AvatarId { get; set; } = avatarId;
    public bool InWorld { get; set; }
    public string ClientVersion { get; set; } = Kogama.Protocols.ClientProtocols.ServerVersion;
    public GameMode Mode { get; set; } = GameMode.Edit;
    public string Build { get; set; } = Kogama.Protocols.ClientProtocols.ServerVersion;

    public int ProfileId { get; set; } = actor;
    public Team Team { get; set; }
    public int Level { get; set; } = 1;
    public string Username => $"Player{actor}";
    public string Region => "en_US";

    public bool LinkState => NativeSince(LinkStateVersion);
    public bool ObjectLinkState => NativeSince(ObjectLinkStateVersion);

    static readonly Version LinkStateVersion = new(1, 30);
    static readonly Version ObjectLinkStateVersion = new(1, 32, 4);

    bool NativeSince(Version since) => peer.Translator is not Kogama.Protocols.LegacyTranslator && Version.TryParse(ClientVersion, out Version? version) && version >= since;

    public byte[] WorldData(World.Snapshot snapshot) => World.WorldSerializer.Write(snapshot, linkState: LinkState, objectLinkState: ObjectLinkState);

    public bool Knows(World.Snapshot snapshot)
    {
        if (peer.Translator is Kogama.Protocols.LegacyTranslator) return true;
        var table = Kogama.Protocols.ProtocolTable.For(ClientVersion);
        return snapshot.Objects.All(obj => Items.KnownBy(obj, table));
    }
}
