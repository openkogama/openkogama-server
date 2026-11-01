using OpenKogama.Api;
using OpenKogama.Handlers.Operations;
using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.World;

namespace OpenKogama.Game;

public sealed partial class Session
{
    const string VisitorKey = "visitor";
    public const int VisitorProfileId = 900_000_000;
    const long VisitorAfterMs = 60 * 60 * 1000;
    const long VisitorStaysMs = 60 * 1000;
    const long TrailEveryMs = 5000;
    const int TrailLength = 720;
    const long TrailMaxAgeMs = 5 * 60 * 1000;
    const float TrailNear = 12f;
    const float TrailFar = 80f;
    const float TrailAbove = 2f;
    const float TrailCeiling = 40f;
    const float VisitorClearance = 6f;
    const float VisitorHover = 1.5f;

    static readonly Lazy<(List<AvatarPart> Parts, List<WornAccessory> Accessories)?> VisitorSkin = new(LoadVisitorSkin);

    readonly Dictionary<Player, long> _playingSince = [];
    readonly List<(float[] Position, long At)> _trail = [];
    long _trailAt;
    int _visitorAvatar = -1;
    int _visitorActor = -1;
    long _visitorLeavesAt;
    long _visitorToolAt;

    public void TickVisitor() => TickVisitor(Environment.TickCount64, DateTime.Now);

    void TickVisitor(long now, DateTime date)
    {
        if (_visitorAvatar >= 0)
        {
            if (now >= _visitorLeavesAt || !Seasonal.Enabled || !Seasonal.Halloween(date)) RemoveVisitor();
            else if (now - _visitorToolAt >= 1000) SendVisitorTool(now);
            return;
        }
        if (!Seasonal.Enabled || !Seasonal.Halloween(date) || Seasonal.Seen(VisitorKey))
        {
            lock (_playingSince) _playingSince.Clear();
            _trail.Clear();
            return;
        }

        Player[] playing = [.. Players.Where(player => player.InWorld)];
        lock (_playingSince)
        {
            foreach (Player gone in _playingSince.Keys.Where(player => !playing.Contains(player)).ToArray())
                _playingSince.Remove(gone);
            foreach (Player player in playing)
                _playingSince.TryAdd(player, now);
        }

        _trail.RemoveAll(sample => now - sample.At > TrailMaxAgeMs);
        if (now - _trailAt >= TrailEveryMs)
        {
            _trailAt = now;
            foreach (Player player in playing)
                if (player.LastPosition is [float x, float y, float z] && float.IsFinite(x) && float.IsFinite(y) && float.IsFinite(z))
                    _trail.Add(([x, y, z], now));
            if (_trail.Count > TrailLength) _trail.RemoveRange(0, _trail.Count - TrailLength);
        }

        bool due;
        lock (_playingSince) due = _playingSince.Values.Any(since => now - since >= VisitorAfterMs);
        if (due && playing.Any(CanSeeVisitor) && VisitorSpot(playing) is float[] spot) AddVisitor(spot, now);
    }

    static bool CanSeeVisitor(Player player) => player.SpawnRoles && player.ReadyEvents && player.SeesNpcs;

    public void IntroduceVisitor(Player player)
    {
        if (_visitorAvatar < 0 || !CanSeeVisitor(player) || player.Saw(_visitorActor)) return;
        var join = new EventData((byte)EventCode.Join)
        {
            Parameters = { [(byte)ParameterKey.ActorNr] = _visitorActor },
        };
        foreach ((ParameterKey key, object value) in new Npc(_visitorActor, _visitorAvatar, " ", [0f, 0f, 0f], 0f).Info())
            join.Parameters[(byte)key] = value;
        join.Parameters[(byte)ParameterKey.IsActorReady] = false;
        join.Parameters[(byte)ParameterKey.ProfileID] = VisitorProfileId;
        join.Parameters[(byte)ParameterKey.Bool] = false;
        player.Peer.Send(join);
        player.Peer.Send(new EventData((byte)EventCode.ReplicateSpawnRoleData)
        {
            Parameters =
            {
                [(byte)ParameterKey.ActorNr] = _visitorActor,
                [(byte)ParameterKey.Data] = System.Text.Json.JsonSerializer.Serialize(new
                {
                    activeSpawnRole = _visitorAvatar,
                    spawnRoleAvatarIds = new[] { _visitorAvatar },
                }),
            },
        });
        player.Sees(_visitorActor);
    }

    public Snapshot VisitorFor(Player player, Snapshot snapshot)
    {
        if (_visitorAvatar < 0 || CanSeeVisitor(player)) return snapshot;
        HashSet<int> hidden = [.. World.Subtree(_visitorAvatar).Select(obj => obj.Id)];
        return snapshot with { Objects = [.. snapshot.Objects.Where(obj => !hidden.Contains(obj.Id))] };
    }

    float[]? VisitorSpot(Player[] playing)
    {
        float[] spawn = World.Spawn;
        float Flat(float[] at) => MathF.Sqrt((at[0] - spawn[0]) * (at[0] - spawn[0]) + (at[2] - spawn[2]) * (at[2] - spawn[2]));
        bool Clear(float[] at) => playing.All(player => player.LastPosition is not [float x, float y, float z]
            || MathF.Sqrt((x - at[0]) * (x - at[0]) + (y - at[1]) * (y - at[1]) + (z - at[2]) * (z - at[2])) > VisitorClearance);

        List<float[]> candidates = [.. _trail
            .Select(sample => sample.Position)
            .Where(at => Flat(at) is >= TrailNear and <= TrailFar && at[1] >= spawn[1] + TrailAbove && at[1] <= spawn[1] + TrailCeiling && Clear(at))
            .OrderBy(Flat)];
        return candidates.Count == 0 ? null : candidates[(int)(candidates.Count * 0.4f)];
    }

    void AddVisitor(float[] spot, long now)
    {
        if (VisitorSkin.Value is not { } skin) return;

        int actor = Interlocked.Increment(ref _nextNpcActor);
        List<WorldObject> avatar = Avatar.Build(World, actor, World.RootId, _avatarPrototypes, WorldObjectType.BuildModeAvatar);
        float[] spawn = World.Spawn;
        float yaw = MathF.Atan2(spawn[0] - spot[0], spawn[2] - spot[2]);
        avatar[0].Position = [spot[0], spot[1] + VisitorHover, spot[2]];
        avatar[0].Rotation = [0f, MathF.Sin(yaw / 2f), 0f, MathF.Cos(yaw / 2f)];
        PackedData.Merge(avatar[0].Runtime, VisitorTool(avatar[0]));
        foreach (WorldObject obj in avatar)
        {
            obj.Transient = true;
            World.Add(obj);
        }
        if (BodyOf(avatar[0].Id) is int body and >= 0)
        {
            if (skin.Parts.Count > 0) Avatar.UseParts(World, body, skin.Parts);
            if (skin.Accessories.Count > 0) Avatar.WearAccessories(World, body, skin.Accessories);
        }

        try
        {
            if (!Seasonal.TryMarkSeen(VisitorKey))
            {
                World.RemoveTree(avatar[0].Id);
                return;
            }
        }
        catch
        {
            World.RemoveTree(avatar[0].Id);
            throw;
        }
        _visitorActor = actor;
        _visitorAvatar = avatar[0].Id;
        _visitorLeavesAt = now + VisitorStaysMs;
        _visitorToolAt = now;
        foreach (Player player in Players.Where(player => player.InWorld)) IntroduceVisitor(player);
        Snapshot added = World.SubtreeSnapshot(_visitorAvatar);
        GetNextGameBatch.SendAdded(this, actor, added with { Prototypes = [.. added.Prototypes.Where(prototype => !_avatarPrototypes.Contains(prototype.Id))] },
            player => player.InWorld && player.Saw(actor));
    }

    static Dictionary<object, object?> VisitorTool(WorldObject avatar)
    {
        float yaw = 2f * MathF.Atan2(avatar.Rotation[1], avatar.Rotation[3]);
        return new Dictionary<object, object?>
        {
            ["currentItem"] = new Dictionary<object, object?>
            {
                ["type"] = (int)AvatarItemType.LaserPointer,
                ["st"] = (byte)0,
                ["cm"] = (byte)0,
                ["tx"] = MathF.Sin(yaw) * 3f,
                ["ty"] = -2.5f,
                ["tz"] = MathF.Cos(yaw) * 3f,
                ["fire"] = false,
            },
        };
    }

    void SendVisitorTool(long now)
    {
        if (World.Find(_visitorAvatar) is not { } avatar) return;
        _visitorToolAt = now;
        var update = new EventData((byte)EventCode.UpdateWorldObjectRunTimeData)
        {
            Parameters =
            {
                [(byte)ParameterKey.WorldObjectID] = _visitorAvatar,
                [(byte)ParameterKey.ActorNr] = _visitorActor,
                [(byte)ParameterKey.WorldObjectRunTimeData] = VisitorTool(avatar),
            },
        };
        foreach (Player player in Players.Where(player => player.InWorld && player.Saw(_visitorActor)))
            player.Peer.Send(update);
    }

    void RemoveVisitor()
    {
        int id = _visitorAvatar;
        int actor = _visitorActor;
        _visitorAvatar = -1;
        _visitorActor = -1;
        World.RemoveTree(id);
        var unregister = new EventData((byte)EventCode.UnregisterWorldObject)
        {
            Parameters = { [(byte)ParameterKey.WorldObjectID] = id },
        };
        var leave = new EventData((byte)EventCode.Leave)
        {
            Parameters = { [(byte)ParameterKey.ActorNr] = actor },
        };
        foreach (Player player in Players.Where(player => player.Saw(actor)))
        {
            player.Peer.Send(unregister);
            player.Peer.Send(leave);
        }
    }

    static (List<AvatarPart> Parts, List<WornAccessory> Accessories)? LoadVisitorSkin()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "data", "avatars", "visitor.kgavatar");
        if (!File.Exists(path)) return null;
        AvatarSkin skin = AvatarSkin.FromJson(File.ReadAllText(path));
        return ([.. skin.Parts.Select(part => new AvatarPart { Bone = part.Bone, Scale = part.Scale, Cubes = part.Cubes })],
            [.. skin.Accessories.Select(worn => new WornAccessory(worn.Item, worn.Slot, worn.Offset, worn.Scale))]);
    }
}
