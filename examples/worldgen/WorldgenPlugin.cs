using System.Numerics;
using OpenKogama.Api;

namespace Worldgen;

public sealed class WorldgenPlugin : IPlugin
{
    const string Marker = "worldgen";
    const int DefaultRadius = 3;
    const int MinRadius = 1;
    const int MaxRadius = 10;
    const int CubesPerUpdate = 256;
    const int UnloadsPerUpdate = 8;
    const int AnchorY = -8;
    const float CubeSize = 4;
    const double ColorLeftAtEdge = 0.05;
    const string Turquoise = "#40E0D0";
    const string PaleTurquoise = "#AFEEEE";

    static readonly double HalfFov = Math.Atan(Math.Tan(Math.PI / 6) * 16 / 9);
    static readonly (int X, int Z)[][] Offsets = [.. Enumerable.Range(0, MaxRadius + 1).Select(Around)];

    readonly object _sync = new();
    readonly Dictionary<IPlayer, Dictionary<(int X, int Z), bool[]>> _loaded = [];
    readonly Dictionary<IPlayer, int> _radius = [];
    readonly Dictionary<IPlayer, bool> _fogOn = [];
    List<IWorld> _worlds = [];
    HashSet<(int X, int Z)> _spawns = [];
    IServer _server = null!;
    IPluginStorage _storage = null!;
    Hills _hills = null!;
    Chunks _chunks = null!;

    public void Load(IServer server)
    {
        _server = server;
        _storage = server.Storage("worldgen");
        if (!int.TryParse(_storage.Get("seed"), out int seed))
        {
            seed = Random.Shared.Next();
            _storage.Set("seed", seed.ToString());
        }
        _hills = new Hills(seed);
        _chunks = new Chunks(_hills, server.Log);

        server.Templates.Add("worldgen", "Infinite Terrain", Build);
        server.AddCommand("help", (player, _) => Help(player));
        server.AddCommand("render", Render);
        server.AddCommand("fog", ToggleFog);
        server.Tick += FindWorlds;
        server.Update += _ => Stream();
        server.PlayerJoined += Joined;
        server.PlayerLeft += player =>
        {
            lock (_sync)
            {
                _loaded.Remove(player);
                _radius.Remove(player);
                _fogOn.Remove(player);
            }
        };
    }

    public void Unload() => _chunks.Dispose();

    static (int X, int Z)[] Around(int radius) =>
        [.. from x in Enumerable.Range(-radius, 2 * radius + 1)
            from z in Enumerable.Range(-radius, 2 * radius + 1)
            orderby x * x + z * z, x, z
            select (x, z)];

    static float Fog(int radius) =>
        (float)(Math.Sqrt(-Math.Log(ColorLeftAtEdge)) / (radius * Chunks.Size * CubeSize * Math.Cos(HalfFov)));

    static string Identity(IPlayer player) => player.AccountId is long account ? $"account {account}" : $"profile {player.ProfileId}";

    int RadiusOf(IPlayer player)
    {
        lock (_sync)
        {
            if (!_radius.TryGetValue(player, out int radius))
                _radius[player] = radius = int.TryParse(_storage.Get("render " + Identity(player)), out int stored)
                    ? Math.Clamp(stored, MinRadius, MaxRadius)
                    : DefaultRadius;
            return radius;
        }
    }

    bool FogOn(IPlayer player)
    {
        lock (_sync)
        {
            if (!_fogOn.TryGetValue(player, out bool on))
                _fogOn[player] = on = _storage.Get("fog " + Identity(player)) != "off";
            return on;
        }
    }

    static string Accent(object text) => $"<color={Turquoise}>{text}</color>";

    void Joined(IPlayer player)
    {
        if (player.World.Markers(Marker).Count == 0)
            return;
        player.Message($"Type {Accent("!help")} for help", PaleTurquoise);
        string seen = "help " + Identity(player);
        if (_storage.Get(seen) is not null)
            return;
        _storage.Set(seen, "shown");
        Help(player);
    }

    void Help(IPlayer player)
    {
        player.Message($"{Accent($"!render {MinRadius}-{MaxRadius}")}: view distance", PaleTurquoise);
        player.Message($"{Accent("!fog")}: fog on or off", PaleTurquoise);
    }

    void Render(IPlayer player, string[] arguments)
    {
        int radius = RadiusOf(player);
        if (arguments.Length == 0 || !int.TryParse(arguments[0], out int wanted))
        {
            player.Message($"Render distance: {Accent(radius)} ({MinRadius}-{MaxRadius})", PaleTurquoise);
            return;
        }
        int chosen = Math.Clamp(wanted, MinRadius, MaxRadius);
        lock (_sync) _radius[player] = chosen;
        _storage.Set("render " + Identity(player), chosen.ToString());
        player.Message($"Render distance set to {Accent(chosen)}", PaleTurquoise);
    }

    void ToggleFog(IPlayer player, string[] arguments)
    {
        bool on = arguments.FirstOrDefault()?.ToLowerInvariant() switch
        {
            "on" => true,
            "off" => false,
            _ => !FogOn(player),
        };
        lock (_sync) _fogOn[player] = on;
        _storage.Set("fog " + Identity(player), on ? null : "off");
        player.Message($"Fog {Accent(on ? "on" : "off")}", PaleTurquoise);
    }

    void Build(IWorldBuilder world)
    {
        world.Terrain.Clear();
        foreach (IWorldObject model in world.Objects.Where(obj => obj.Type == "CubeModel").ToList())
            world.Remove(model.Id);
        world.Add("TextMsg", Vector3.Zero, new Dictionary<string, object> { ["text"] = "$$" + Marker });
        foreach (IWorldObject spawn in world.Objects.Where(IsSpawn).ToList())
        {
            (int x, _, int z) = world.Terrain.CubeAt(spawn.Position);
            world.Move(spawn.Id, spawn.Position with { Y = Top(x, z) });
        }
        Prepare(world);
    }

    static void Prepare(IWorldBuilder world)
    {
        if (world.Terrain.Count == 0)
            world.Terrain.Set(0, AnchorY, 0, Material.Green);
        if (world.Objects.Any(obj => obj.Type == "Skybox"))
            return;
        Vector3 spawn = world.Objects.FirstOrDefault(IsSpawn)?.Position ?? Vector3.Zero;
        world.Add("Skybox", spawn + new Vector3(6, 3, 0), new Dictionary<string, object>
        {
            ["color"] = new[] { 95f / 255f, 180f / 255f, 254f / 255f },
            ["sunAngle"] = 80f,
            ["fogDensity"] = Fog(DefaultRadius),
        });
    }

    float Top(int x, int z)
    {
        double[] corners = [_hills.Height(x, z), _hills.Height(x + 1, z), _hills.Height(x, z + 1), _hills.Height(x + 1, z + 1)];
        return KogamaTerrain.HeightsFromZ(corners, 1, 1).Cast<int>().Max();
    }

    static bool IsSpawn(IWorldObject obj) => obj.Type.StartsWith("SpawnPoint", StringComparison.Ordinal);

    void FindWorlds()
    {
        var worlds = new List<IWorld>();
        var spawns = new HashSet<(int X, int Z)>();
        foreach (IWorld world in _server.Worlds)
        {
            if (world.Markers(Marker).Count == 0)
                continue;
            Prepare(world);
            worlds.Add(world);
            List<IWorldObject> objects = [.. world.Objects];
            foreach (IWorldObject spawn in objects.Where(IsSpawn))
            {
                (int X, int Z) centre = Chunks.Of(world.Terrain.CubeAt(spawn.Position));
                foreach ((int x, int z) in Offsets[DefaultRadius])
                    spawns.Add((centre.X + x, centre.Z + z));
            }
            foreach (IPlayer player in world.Players)
            {
                float fog = FogOn(player) ? Fog(RadiusOf(player)) : 0f;
                foreach (IWorldObject sky in objects.Where(obj => obj.Type == "Skybox"))
                    player.SetDataLocally(sky.Id, "fogDensity", fog);
            }
        }
        lock (_sync)
        {
            _worlds = worlds;
            _spawns = spawns;
            foreach ((int X, int Z) chunk in spawns)
                _chunks.Get(chunk);
        }
    }

    void Stream()
    {
        lock (_sync)
        {
            var wanted = new HashSet<(int X, int Z)>(_spawns);
            foreach (IWorld world in _worlds)
                foreach (IPlayer player in world.Players)
                    if (player.Position is Vector3 position)
                        Stream(world, player, position, wanted);
            foreach (Dictionary<(int X, int Z), bool[]> loaded in _loaded.Values)
                wanted.UnionWith(loaded.Keys);
            _chunks.Keep(wanted);
        }
    }

    void Stream(IWorld world, IPlayer player, Vector3 position, HashSet<(int X, int Z)> wanted)
    {
        int radius = RadiusOf(player);
        (int x, int y, int z) = world.Terrain.CubeAt(position);
        (int X, int Z) centre = Chunks.Of((x, y, z));
        if (!_loaded.TryGetValue(player, out Dictionary<(int X, int Z), bool[]>? loaded))
            _loaded[player] = loaded = [];

        int Away((int X, int Z) chunk) => Math.Max(Math.Abs(chunk.X - centre.X), Math.Abs(chunk.Z - centre.Z));
        foreach ((int X, int Z) far in loaded.Keys.Where(chunk => Away(chunk) > radius + 1).OrderByDescending(Away).Take(UnloadsPerUpdate).ToList())
        {
            if (_chunks.Cached(far) is Chunk chunk)
                Unload(player, chunk, loaded[far]);
            loaded.Remove(far);
        }

        (int X, int Z) nearest = default;
        Chunk? next = null;
        int best = int.MaxValue;
        foreach ((int dx, int dz) in Offsets[radius])
        {
            (int X, int Z) key = (centre.X + dx, centre.Z + dz);
            wanted.Add(key);
            if (_chunks.Get(key) is not Chunk chunk || Chunks.Distance(key, x, z) >= best)
                continue;
            bool[]? sent = loaded.GetValueOrDefault(key);
            for (int i = 0; i < chunk.Cubes.Length; i++)
            {
                int distance = Distance(chunk.Cubes[i], x, z);
                if (distance < best && (sent is null || !sent[i]))
                {
                    best = distance;
                    nearest = key;
                    next = chunk;
                }
            }
        }
        if (next is null)
            return;
        if (!loaded.TryGetValue(nearest, out bool[]? marks))
            loaded[nearest] = marks = new bool[next.Cubes.Length];
        Load(player, next, marks, x, z);
    }

    static int Distance(ChunkCube cube, int x, int z) => (cube.X - x) * (cube.X - x) + (cube.Z - z) * (cube.Z - z);

    static void Load(IPlayer player, Chunk chunk, bool[] sent, int x, int z)
    {
        int[] closest = [.. Enumerable.Range(0, chunk.Cubes.Length).Where(i => !sent[i]).OrderBy(i => Distance(chunk.Cubes[i], x, z)).Take(CubesPerUpdate)];
        using ICubeEdit edit = player.EditTerrainLocally();
        foreach (int i in closest)
        {
            ChunkCube cube = chunk.Cubes[i];
            edit.Set(cube.X, cube.Y, cube.Z, cube.Cube);
            sent[i] = true;
        }
    }

    static void Unload(IPlayer player, Chunk chunk, bool[] sent)
    {
        using ICubeEdit edit = player.EditTerrainLocally();
        for (int i = 0; i < chunk.Cubes.Length; i++)
            if (sent[i])
                edit.Remove(chunk.Cubes[i].X, chunk.Cubes[i].Y, chunk.Cubes[i].Z);
    }
}
