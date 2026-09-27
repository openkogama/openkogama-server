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
    public byte BuildTarget { get; set; }
    public DateTime? BoostSince { get; set; }
    public long? NextFreeSpin { get; set; }
    public int? PendingReward { get; set; }
    public string Username => $"Player{actor}";
    public string Region => "en_US";

    const int MaxUpload = 16 * 1024 * 1024;

    readonly HashSet<int> _seen = [];
    readonly MemoryStream _upload = new();
    int _uploadId = -1;

    public void Receive(int id, byte[] chunk)
    {
        lock (_upload)
        {
            if (id != _uploadId || _upload.Length + chunk.Length > MaxUpload)
            {
                _upload.SetLength(0);
                _uploadId = id;
            }
            _upload.Write(chunk);
        }
    }

    public byte[]? TakeUpload()
    {
        lock (_upload)
        {
            byte[]? data = _upload.Length > 0 ? _upload.ToArray() : null;
            _upload.SetLength(0);
            _uploadId = -1;
            return data;
        }
    }

    public void Sees(int other)
    {
        lock (_seen) _seen.Add(other);
    }

    public bool Saw(int other)
    {
        lock (_seen) return _seen.Contains(other);
    }

    public IEnumerable<(Kogama.ParameterKey Key, object Value)> Info() =>
    [
        (Kogama.ParameterKey.ProfileID, ProfileId),
        (Kogama.ParameterKey.Username, Username),
        (Kogama.ParameterKey.RegionCode, Region),
        (Kogama.ParameterKey.TeamID, (int)Team),
        (Kogama.ParameterKey.Level, Level),
        (Kogama.ParameterKey.ClientBuildTarget, BuildTarget),
    ];

    public bool LinkState => NativeSince(LinkStateVersion) && !NativeSince(StatelessLinksVersion);
    public bool LogicFrames => peer.Translator is Kogama.Protocols.OperationRemap remap && remap.Knows(Kogama.EventCode.LogicFrame);
    public bool LogicSteps => LogicFrames && NativeSince(LogicStepsVersion);
    public bool ObjectLinkState => NativeSince(ObjectLinkStateVersion) && !NativeSince(StatelessLinksVersion);

    static readonly Version LinkStateVersion = new(1, 30);
    static readonly Version ObjectLinkStateVersion = new(1, 32, 4);
    static readonly Version StatelessLinksVersion = new(1, 42, 6);
    static readonly Version LogicStepsVersion = new(1, 42, 10);

    bool NativeSince(Version since) => peer.Translator is not Kogama.Protocols.LegacyTranslator && Version.TryParse(ClientVersion, out Version? version) && version >= since;

    public byte[] WorldData(World.Snapshot snapshot) => World.WorldSerializer.Write(snapshot, linkState: LinkState, objectLinkState: ObjectLinkState);

    public bool Knows(World.Snapshot snapshot)
    {
        if (peer.Translator is Kogama.Protocols.LegacyTranslator) return true;
        var table = Kogama.Protocols.ProtocolTable.For(ClientVersion);
        return snapshot.Objects.All(obj => Items.KnownBy(obj, table));
    }
}
