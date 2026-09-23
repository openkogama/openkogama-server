using OpenKogama.Photon;
using OpenKogama.World;

namespace OpenKogama.Game;

public sealed class Session
{
    const string SavePath = "worlds/default.kgmap";
    static readonly string TemplatePath = Path.Combine(AppContext.BaseDirectory, "data", "maps", "default.kgmap");

    readonly List<Player> _players = [];
    readonly int[] _avatarPrototypes;

    public Session()
    {
        World = File.Exists(SavePath)
            ? GameWorld.Load(SavePath)
            : GameWorld.Load(TemplatePath, onlyImportable: true);
        _avatarPrototypes = Avatar.AddPrototypes(World);
        Logic = new Logic(this);
        Logic.Reset(World.ToSnapshot().Objects.Where(obj => obj.Type == WorldObjectType.TimeTrigger).Select(obj => obj.Id));
    }

    public Logic Logic { get; }

    public GameWorld World { get; }
    public Triggers Triggers { get; } = new();
    public IReadOnlyList<Player> Players => _players;

    public Player Add(PhotonPeer peer)
    {
        List<WorldObject> avatar = Avatar.Build(World, peer.Id, World.RootId, _avatarPrototypes);
        foreach (WorldObject obj in avatar) World.Add(obj);

        Player player = new(peer, peer.Id, avatar[0].Id);
        _players.Add(player);
        return player;
    }

    public Player? For(PhotonPeer peer) => _players.FirstOrDefault(p => p.Peer == peer);

    public void Remove(Player player)
    {
        _players.Remove(player);
        World.Remove(player.AvatarId);
    }

    public void SaveIfChanged()
    {
        if (!World.TakeChanged()) return;
        World.Save(SavePath);
        Console.WriteLine("world saved");
    }
}
