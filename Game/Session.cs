using OpenKogama.Kogama;
using OpenKogama.Photon;
using OpenKogama.Storage;
using OpenKogama.World;

namespace OpenKogama.Game;

public sealed partial class Session
{
    const string LegacyPath = "worlds/default.kgmap";
    static readonly string TemplatePath = Templates.DefaultPath;
    static readonly string EditorPath = Path.Combine(AppContext.BaseDirectory, "data", "maps", "avatar-editor.kgmap");

    readonly List<Player> _players = [];
    readonly int[] _avatarPrototypes;
    readonly int? _worldId;

    public Session(GameWorld world, int? worldId, string name = "", bool play = false)
    {
        Play = play;
        World = world;
        _worldId = worldId;
        Name = name;
        foreach (WorldObject obj in World.ToSnapshot().Objects)
            World.Modify(obj.Id, RuntimeDefaults.Reset);
        _avatarPrototypes = Avatar.AddPrototypes(World);
        AvatarClasses.AddPreviews(World);
        Logic = new Logic(this);
        Logic.ResetAll();
        Teams = new Teams(this);
        Round = new Round(this);
        GamePasses = new GamePasses(this, worldId is not null);
        if (worldId is not null && World.FindFirst(WorldObjectType.GameBoosterDataObject) is null)
            World.Add(new WorldObject { Id = World.NewObjectId(), ParentId = World.RootId, Type = WorldObjectType.GameBoosterDataObject, Owner = -1 });
    }

    public string Name { get; }
    public int? WorldId => _worldId;
    public bool Play { get; }
    public bool Published => _worldId is int id && Stores.Worlds.Published(id) is not null;
    public Func<int> Clock { get; private set; } = () => Environment.TickCount;
    public Logic Logic { get; }
    public Teams Teams { get; }
    public Round Round { get; }
    public GamePasses GamePasses { get; }

    public GameWorld World { get; }
    public Triggers Triggers { get; } = new();
    public IReadOnlyList<Player> Players => _players;

    public void Begin(Func<int> clock)
    {
        Clock = clock;
        if (World.FindFirst(WorldObjectType.RoundCube) is not null) Round.Start();
    }

    public Player Add(PhotonPeer peer, bool buildAvatar = true)
    {
        int avatarId = -1;
        if (buildAvatar)
        {
            List<WorldObject> avatar = Avatar.Build(World, peer.Id, World.RootId, _avatarPrototypes);
            foreach (WorldObject obj in avatar) World.Add(obj);
            avatarId = avatar[0].Id;
        }

        Player player = new(peer, peer.Id, avatarId) { Team = Teams.Default };
        _players.Add(player);
        return player;
    }

    public void AddBuildAvatar(Player player)
    {
        if (Play || player.AvatarId < 0 || player.BuildAvatarId >= 0) return;

        List<WorldObject> avatar = Avatar.Build(World, player.Actor, World.RootId, _avatarPrototypes, WorldObjectType.BuildModeAvatar);
        foreach (WorldObject obj in avatar) World.Add(obj);
        player.BuildAvatarId = avatar[0].Id;
        player.ActiveSpawnRole = player.BuildAvatarId;
        World.Modify(player.AvatarId, obj => obj.SetRuntime("spawnRoleModeType", PackedType.Int32, Avatar.Hidden));
    }

    public Snapshot AvatarSnapshot(Player player)
    {
        Snapshot avatar = World.SubtreeSnapshot(player.AvatarId);
        if (player.BuildAvatarId >= 0)
        {
            Snapshot builder = World.SubtreeSnapshot(player.BuildAvatarId);
            avatar = avatar with
            {
                Prototypes = [.. avatar.Prototypes.Concat(builder.Prototypes).DistinctBy(prototype => prototype.Id)],
                Objects = [.. avatar.Objects, .. builder.Objects],
            };
        }
        return avatar with { Prototypes = [.. avatar.Prototypes.Where(prototype => !_avatarPrototypes.Contains(prototype.Id))] };
    }

    public int BodyOf(int avatarId) =>
        World.Subtree(avatarId).Find(obj => obj.Type == WorldObjectType.Blueprint && obj.ParentId == avatarId)?.Id ?? -1;

    public void LoadAvatar(Player player)
    {
        if (Bodies.Count > 0) return;

        int avatar = Stores.Profiles.ActiveAvatar(player.ProfileId);
        WorldObject? body = World.Subtree(player.AvatarId)
            .Find(obj => obj.Type == WorldObjectType.Blueprint && obj.ParentId == player.AvatarId);
        if (body is not null) Dress(body.Id, avatar);
    }

    void Dress(int bodyId, int avatar) => Avatar.Dress(World, bodyId, avatar);

    public void SaveEditedAvatar(Player player)
    {
        foreach ((int bodyId, int avatar) in BodyAvatars)
            Stores.Profiles.SaveAvatar(avatar, Avatar.ReadParts(World, bodyId));
        Console.WriteLine($"profile {player.ProfileId}: {BodyAvatars.Count} avatars saved");
    }

    public int AvatarOfBody(int bodyId, int profile) =>
        BodyAvatars.TryGetValue(bodyId, out int avatar) ? avatar : Stores.Profiles.ActiveAvatar(profile);

    public Snapshot ResetEditorBody(int oldBodyId, int actor)
    {
        int avatar = BodyAvatars[oldBodyId];
        int bodyId;

        if (Stores.Profiles.AvatarSource(avatar) is int source && AvatarShop.Find(source) is { } original)
        {
            bodyId = World.Insert(original.Snapshot(actor), World.RootId).Objects.First(obj => obj.ParentId == World.RootId).Id;
        }
        else
        {
            List<WorldObject> body = Avatar.BuildBody(World, actor, World.RootId, Avatar.AddPrototypes(World));
            foreach (WorldObject obj in body) World.Add(obj);
            bodyId = body[0].Id;
        }

        Avatar.WearAccessories(World, bodyId, Stores.Profiles.Accessories(avatar));
        World.Remove(oldBodyId);
        BodyAvatars.Remove(oldBodyId);
        Bodies[Bodies.IndexOf(oldBodyId)] = bodyId;
        BodyAvatars[bodyId] = avatar;
        Stores.Profiles.SaveAvatar(avatar, Avatar.ReadParts(World, bodyId));
        return World.SubtreeSnapshot(bodyId);
    }

    public int AddEditorBody(Snapshot snapshot, int avatar)
    {
        Snapshot added = World.Insert(snapshot, World.RootId);
        int bodyId = added.Objects.First(obj => obj.ParentId == World.RootId).Id;
        Bodies.Add(bodyId);
        BodyAvatars[bodyId] = avatar;
        return bodyId;
    }

    public PlanetOwnership OwnershipOf(Player player)
    {
        if (_worldId is not int id) return PlanetOwnership.Owner;
        int owner = Stores.Worlds.World(id)?.Owner ?? 0;
        return owner == 0 || owner == player.ProfileId ? PlanetOwnership.Owner : PlanetOwnership.None;
    }

    public Player? For(PhotonPeer peer) => _players.FirstOrDefault(p => p.Peer == peer);

    public void Remove(Player player)
    {
        _players.Remove(player);
        World.Remove(player.AvatarId);
        if (player.BuildAvatarId >= 0) World.Remove(player.BuildAvatarId);
        foreach (int role in player.ClassAvatars.Values) World.Remove(role);
    }

    public int[] AvatarPrototypes => _avatarPrototypes;

    public void ActivateRole(Player player, int role, float[]? position = null, float[]? rotation = null)
    {
        WorldObject? previous = World.Find(player.ActiveSpawnRole);
        position ??= player.LastPosition ?? previous?.Position ?? [.. World.Spawn];
        rotation ??= player.LastRotation ?? previous?.Rotation ?? [0f, 0f, 0f, 1f];
        player.ActiveSpawnRole = role;

        var activate = new EventData((byte)EventCode.SetActiveSpawnRole)
        {
            Parameters =
            {
                [(byte)ParameterKey.ActorNr] = player.Actor,
                [(byte)ParameterKey.Id] = role,
                [(byte)ParameterKey.PosX] = position[0],
                [(byte)ParameterKey.PosY] = position[1],
                [(byte)ParameterKey.PosZ] = position[2],
                [(byte)ParameterKey.RotX] = rotation[0],
                [(byte)ParameterKey.RotY] = rotation[1],
                [(byte)ParameterKey.RotZ] = rotation[2],
                [(byte)ParameterKey.RotW] = rotation[3],
            },
        };
        foreach (Player other in _players)
            if (other.SpawnRoles && (other == player || other.Saw(player.Actor)))
                other.Peer.Send(activate);
    }

    public static Session CharacterEditor(int actor, int profile)
    {
        GameWorld world = GameWorld.Load(EditorPath);
        var session = new Session(world, null);
        int[] prototypes = Avatar.AddPrototypes(world);

        Stores.Profiles.ActiveAvatar(profile);
        foreach (StoredAvatar avatar in Stores.Profiles.Avatars(profile).OrderByDescending(avatar => avatar.Active))
        {
            List<WorldObject> body = Avatar.BuildBody(world, actor, world.RootId, prototypes);
            foreach (WorldObject obj in body) world.Add(obj);
            session.Bodies.Add(body[0].Id);
            session.BodyAvatars[body[0].Id] = avatar.Id;
            session.Dress(body[0].Id, avatar.Id);
        }

        return session;
    }

    public List<int> Bodies { get; } = [];
    public Dictionary<int, int> BodyAvatars { get; } = [];

    public void SaveIfChanged()
    {
        if (Play || _worldId is not int id || !World.TakeChanged()) return;
        Stores.Worlds.SaveWorld(new StoredWorld(id, Name, 0, World.ToData()));
        Console.WriteLine($"world {id} saved");
    }

    public static Session? Open(int id, bool play)
    {
        if (Stores.Worlds.World(id) is not { } stored) return null;

        byte[] data = play ? Stores.Worlds.Published(id) ?? stored.Data : stored.Data;
        Stores.Worlds.MarkPlayed(id);
        return new Session(GameWorld.FromData(stored.Name, null, [data]), id, stored.Name, play);
    }

    public void Publish()
    {
        if (Play || _worldId is not int id) return;
        SaveIfChanged();
        Stores.Worlds.Publish(id, World.ToData());
        Console.WriteLine($"world {id} published");
    }

    public static int CreateWorld(string name, string? template = null) =>
        Stores.Worlds.Create(name, 0, Templates.Create(template).ToData());

    public static int ImportWorld(string name, byte[] file, string? client = null)
    {
        GameWorld world = WorldConverter.Import(file, client, out Dictionary<int, int> dropped);
        int id = Stores.Worlds.Create(KgmapFile.Title(file) ?? name, 0, world.ToData());

        string skipped = string.Join(", ", dropped.Select(pair => $"type {pair.Key} x{pair.Value}"));
        Console.WriteLine($"world {id} imported for {client ?? "2015"}: {world.ToSnapshot().Objects.Count} objects" + (skipped.Length > 0 ? $", dropped {skipped}" : ""));
        return id;
    }

    public static void EnsureDefaultWorld()
    {
        if (Stores.Worlds.List().Count > 0) return;

        GameWorld world = File.Exists(LegacyPath)
            ? GameWorld.Load(LegacyPath)
            : WorldConverter.Load(TemplatePath);
        Stores.Worlds.Create("My World", 0, world.ToData());
    }
}
