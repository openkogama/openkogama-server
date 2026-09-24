using OpenKogama.Photon;
using OpenKogama.World;

namespace OpenKogama.Game;

public sealed class Session
{
    const string SavePath = "worlds/default.kgmap";
    static readonly string TemplatePath = Path.Combine(AppContext.BaseDirectory, "data", "maps", "default.kgmap");
    static readonly string EditorPath = Path.Combine(AppContext.BaseDirectory, "data", "maps", "avatar-editor.kgmap");

    readonly List<Player> _players = [];
    readonly int[] _avatarPrototypes;
    readonly string? _savePath;

    public Session() : this(File.Exists(SavePath) ? GameWorld.Load(SavePath) : GameWorld.Load(TemplatePath, onlyImportable: true), SavePath)
    {
    }

    public Session(GameWorld world, string? savePath)
    {
        World = world;
        _savePath = savePath;
        foreach (WorldObject obj in World.ToSnapshot().Objects)
            World.Modify(obj.Id, RuntimeDefaults.Reset);
        _avatarPrototypes = Avatar.AddPrototypes(World);
        Logic = new Logic(this);
        Logic.Reset(World.ToSnapshot().Objects.Where(obj => obj.Type == WorldObjectType.TimeTrigger).Select(obj => obj.Id));
        Teams = new Teams(this);
        Round = new Round(this);
        if (World.FindFirst(WorldObjectType.RoundCube) is not null) Round.Start();
    }

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
        if (_savePath is null || !World.TakeChanged()) return;
        World.Save(_savePath);
        Console.WriteLine("world saved");
    }
}
