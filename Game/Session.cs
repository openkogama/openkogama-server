using OpenKogama.Photon;
using OpenKogama.Storage;
using OpenKogama.World;

namespace OpenKogama.Game;

public sealed class Session
{
    const string LegacyPath = "worlds/default.kgmap";
    static readonly string TemplatePath = Path.Combine(AppContext.BaseDirectory, "data", "maps", "default.kgmap");
    static readonly string EditorPath = Path.Combine(AppContext.BaseDirectory, "data", "maps", "avatar-editor.kgmap");

    readonly List<Player> _players = [];
    readonly int[] _avatarPrototypes;
    readonly int? _worldId;

    public Session(GameWorld world, int? worldId, string name = "")
    {
        World = world;
        _worldId = worldId;
        Name = name;
        foreach (WorldObject obj in World.ToSnapshot().Objects)
            World.Modify(obj.Id, RuntimeDefaults.Reset);
        _avatarPrototypes = Avatar.AddPrototypes(World);
        Logic = new Logic(this);
        Logic.Reset(World.ToSnapshot().Objects.Where(obj => obj.Type == WorldObjectType.TimeTrigger).Select(obj => obj.Id));
        Teams = new Teams(this);
        Round = new Round(this);
        if (World.FindFirst(WorldObjectType.RoundCube) is not null) Round.Start();
    }

    public string Name { get; }
    public int? WorldId => _worldId;
    public Func<int> Clock { get; set; } = () => Environment.TickCount;
    public Logic Logic { get; }
    public Teams Teams { get; }
    public Round Round { get; }

    public GameWorld World { get; }
    public Triggers Triggers { get; } = new();
    public IReadOnlyList<Player> Players => _players;

    public Player Add(PhotonPeer peer)
    {
        List<WorldObject> avatar = Avatar.Build(World, peer.Id, World.RootId, _avatarPrototypes);
        foreach (WorldObject obj in avatar) World.Add(obj);

        Player player = new(peer, peer.Id, avatar[0].Id) { Team = Teams.Default };
        _players.Add(player);
        return player;
    }

    public void LoadAvatar(Player player)
    {
        List<AvatarPart>? parts = Stores.Profiles.Avatar(player.ProfileId);
        List<WornAccessory> accessories = Stores.Profiles.Accessories(player.ProfileId);

        List<int> bodies = [.. Bodies];
        WorldObject? body = World.Subtree(player.AvatarId)
            .Find(obj => obj.Type == WorldObjectType.Blueprint && obj.ParentId == player.AvatarId);
        if (body is not null) bodies.Add(body.Id);

        foreach (int bodyId in bodies)
        {
            if (parts is not null) Avatar.UseParts(World, bodyId, parts);
            Avatar.WearAccessories(World, bodyId, accessories);
        }
    }

    public void SaveEditedAvatar(Player player)
    {
        if (Bodies.Count == 0) return;
        Stores.Profiles.SaveAvatar(player.ProfileId, Avatar.ReadParts(World, Bodies[0]));
        Console.WriteLine($"profile {player.ProfileId}: avatar saved");
    }

    public Player? For(PhotonPeer peer) => _players.FirstOrDefault(p => p.Peer == peer);

    public void Remove(Player player)
    {
        _players.Remove(player);
        World.Remove(player.AvatarId);
    }

    public static Session CharacterEditor(int actor)
    {
        GameWorld world = GameWorld.Load(EditorPath);
        var session = new Session(world, null);

        List<WorldObject> body = Avatar.BuildBody(world, actor, world.RootId, Avatar.AddPrototypes(world));
        foreach (WorldObject obj in body) world.Add(obj);
        session.Bodies.Add(body[0].Id);

        return session;
    }

    public List<int> Bodies { get; } = [];

    public void SaveIfChanged()
    {
        if (_worldId is not int id || !World.TakeChanged()) return;
        Stores.Worlds.SaveWorld(new StoredWorld(id, Name, 0, World.ToData()));
        Console.WriteLine($"world {id} saved");
    }

    public static Session? Open(int id) =>
        Stores.Worlds.World(id) is { } stored
            ? new Session(GameWorld.FromData(stored.Name, null, [stored.Data]), id, stored.Name)
            : null;

    public static int CreateWorld(string name, string? template = null) =>
        Stores.Worlds.Create(name, 0, WorldConverter.Load(Templates.Find(template)?.Path ?? TemplatePath).ToData());

    public static int ImportWorld(string name, byte[] file)
    {
        GameWorld world = WorldConverter.Import(file, out Dictionary<int, int> dropped);
        int id = Stores.Worlds.Create(name, 0, world.ToData());

        string skipped = string.Join(", ", dropped.Select(pair => $"type {pair.Key} x{pair.Value}"));
        Console.WriteLine($"world {id} imported: {world.ToSnapshot().Objects.Count} objects" + (skipped.Length > 0 ? $", dropped {skipped}" : ""));
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
