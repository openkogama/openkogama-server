using OpenKogama.Photon;

namespace OpenKogama.Game;

public sealed class Player(PhotonPeer peer, int actor, int avatarId)
{
    public PhotonPeer Peer => peer;
    public int Actor => actor;
    public int AvatarId { get; set; } = avatarId;
    public int BuildAvatarId { get; set; } = -1;
    public Dictionary<int, int> ClassAvatars { get; } = [];
    public int ActiveSpawnRole { get; set; } = avatarId;
    public float[]? LastPosition { get; set; }
    public float[]? LastRotation { get; set; }
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
        (Kogama.ParameterKey.IsActorReady, Ready),
        (Kogama.ParameterKey.UserProfileData, ProfileData()),
        (Kogama.ParameterKey.PlayerPlanetData, PlanetSummary),
    ];

    public string PlanetSummary { get; set; } = """{"highScoreGamePoints":0,"gamePassTier":0}""";

    public string ProfileData() => System.Text.Json.JsonSerializer.Serialize(new
    {
        IsAdmin = false,
        UserName = Username,
        Gold = Storage.Stores.Profiles.Gold(ProfileId),
        SubscriptionData = new { SubscriptionType = 0 },
    });

    public bool LinkState => NativeSince(LinkStateVersion) && !NativeSince(StatelessLinksVersion);
    public bool LogicFrames => peer.Translator is Kogama.Protocols.OperationRemap remap && remap.Knows(Kogama.EventCode.LogicFrame);
    public bool LogicSteps => LogicFrames && NativeSince(LogicStepsVersion);
    public bool ObjectLinkState => NativeSince(ObjectLinkStateVersion) && !NativeSince(StatelessLinksVersion);
    public bool LinkEvents => NativeSince(StatelessLinksVersion);
    public bool Ready { get; set; }
    public bool AdminMessages => NativeSince(AdminMessagesVersion);
    public bool ChatKinds => NativeSince(ChatKindsVersion);
    public bool AccessoryQueries => NativeSince(AccessoryQueriesVersion);
    public bool ModernAccessories => peer.Translator is Kogama.Protocols.OperationRemap remap && remap.Knows(Kogama.OperationCode.RequestAccessoryData);
    public bool RichText => peer.Protocol != PhotonProtocol.Protocol15 && peer.Translator is not Kogama.Protocols.LegacyTranslator;
    public bool ShortRoundStates => peer.Translator is Kogama.Protocols.OperationRemap remap && remap.ShortStates;
    public bool ReadyEvents => peer.Translator is Kogama.Protocols.OperationRemap remap && remap.Knows(Kogama.ParameterKey.IsActorReady);
    public bool ServerExperience => peer.Translator is Kogama.Protocols.OperationRemap remap && remap.Knows(Kogama.EventCode.XPReward);
    public bool SpawnRoles => peer.Translator is Kogama.Protocols.OperationRemap remap && remap.Knows(Kogama.EventCode.SetupUserPlayMode);
    public DateTime? PlayingSince { get; set; }

    public StreamingAssetCatalog Streaming => ClientContent.Streaming(peer, Build);

    public object WorldFormat => (LinkState, ObjectLinkState, ModernAccessories, SpawnRoles, Streaming);

    public bool Silver => peer.Translator is not Kogama.Protocols.OperationRemap remap || remap.Knows(Kogama.ParameterKey.SilverAmount);

    public string SpawnRoleData() => System.Text.Json.JsonSerializer.Serialize(new
    {
        activeSpawnRole = ActiveSpawnRole,
        spawnRoleAvatarIds = (Mode == GameMode.CharacterEditor ? new[] { BuildAvatarId } : new[] { AvatarId, BuildAvatarId }).Where(id => id >= 0).Concat(ClassAvatars.Values),
    });

    public bool ExtraRole(int id) => id == BuildAvatarId || ClassAvatars.ContainsValue(id);

    public int PlayAvatar => ClassAvatars.ContainsValue(ActiveSpawnRole) ? ActiveSpawnRole : AvatarId;

    public bool Owns(int avatar) => avatar == AvatarId || ClassAvatars.ContainsValue(avatar);

    public string SpawnRoleMetaData()
    {
        var roles = new Dictionary<string, int>();
        if (Mode != GameMode.CharacterEditor) roles["DefaultPlayModeSpawnRole"] = AvatarId;
        if (Mode != GameMode.Play) roles["BuildModeSpawnRole"] = BuildAvatarId >= 0 ? BuildAvatarId : AvatarId;
        return System.Text.Json.JsonSerializer.Serialize(new { spawnRolesDefaultTypeWoIDMap = roles });
    }

    static readonly Version LinkStateVersion = new(1, 30);
    static readonly Version ObjectLinkStateVersion = new(1, 32, 4);
    static readonly Version StatelessLinksVersion = new(1, 42, 6);
    static readonly Version LogicStepsVersion = new(1, 42, 10);
    static readonly Version AdminMessagesVersion = new(1, 33);
    static readonly Version ChatKindsVersion = new(1, 81, 2);
    static readonly Version AccessoryQueriesVersion = new(2, 3);

    bool NativeSince(Version since) => peer.Translator is not Kogama.Protocols.LegacyTranslator && Version.TryParse(ClientVersion, out Version? version) && version >= since;

    public byte[] WorldData(World.Snapshot snapshot, bool package = false)
    {
        if (!SpawnRoles) snapshot = Without(Without(snapshot, obj => obj.Type == World.WorldObjectType.BuildModeAvatar), obj => obj.Type == World.WorldObjectType.Avatar && obj.Transient);
        else if (Mode == GameMode.CharacterEditor) snapshot = Without(snapshot, obj => obj.Type == World.WorldObjectType.Avatar);
        else snapshot = snapshot with { Objects = [.. snapshot.Objects.Select(WithoutLaser)] };
        snapshot = snapshot with { Objects = [.. snapshot.Objects.Select(obj => Accessories.For(this, obj))] };
        return World.WorldSerializer.Write(snapshot, linkState: LinkState, objectLinkState: ObjectLinkState, events: !package);
    }

    static bool HoldsLaser(IEnumerable<(string Key, World.PackedType Type, object Value)> item) =>
        item.FirstOrDefault(pair => pair.Key == "type").Value is { } type && Convert.ToInt32(type) == (int)Kogama.AvatarItemType.LaserPointer;

    static World.WorldObject WithoutLaser(World.WorldObject obj)
    {
        if (obj.Type != World.WorldObjectType.Avatar
            || obj.Runtime.Find(pair => pair.Key == "currentItem").Value is not List<(string Key, World.PackedType Type, object Value)> item
            || !HoldsLaser(item))
            return obj;

        return new World.WorldObject
        {
            Id = obj.Id,
            ParentId = obj.ParentId,
            ItemId = obj.ItemId,
            Type = obj.Type,
            Position = obj.Position,
            Rotation = obj.Rotation,
            Scale = obj.Scale,
            Data = obj.Data,
            Owner = obj.Owner,
            PreviewOwner = obj.PreviewOwner,
            Runtime =
            [
                .. obj.Runtime.Where(pair => pair.Key != "currentItem"),
                ("currentItem", World.PackedType.Hashtable, new List<(string Key, World.PackedType Type, object Value)> { ("type", World.PackedType.Int32, (int)Kogama.AvatarItemType.Hand) }),
            ],
            Transient = obj.Transient,
        };
    }

    static World.Snapshot Without(World.Snapshot snapshot, Func<World.WorldObject, bool> hide)
    {
        var hidden = snapshot.Objects.Where(hide).Select(obj => obj.Id).ToHashSet();
        if (hidden.Count == 0) return snapshot;
        int count;
        do
        {
            count = hidden.Count;
            foreach (World.WorldObject obj in snapshot.Objects)
                if (hidden.Contains(obj.ParentId)) hidden.Add(obj.Id);
        }
        while (hidden.Count != count);
        return snapshot with { Objects = [.. snapshot.Objects.Where(obj => !hidden.Contains(obj.Id))] };
    }

    public bool Knows(World.Snapshot snapshot)
    {
        if (peer.Translator is Kogama.Protocols.LegacyTranslator) return true;
        var table = Kogama.Protocols.ProtocolTable.For(ClientVersion);
        return snapshot.Objects.All(obj => Items.KnownBy(obj, table));
    }
}
