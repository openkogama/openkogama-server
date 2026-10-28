using System.Collections.Concurrent;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using OpenKogama.Api;

namespace Worldmap;

sealed record Shared(MapImage Layout, string Overview, IReadOnlyDictionary<(int Col, int Row), string> Tiles, DateTime At);

sealed record Head(string Key, string? Face, string? Ring);

sealed record SavedMap(float MinX, float MinZ, float Unit, int Width, int Height, uint Background,
    int Cols, int Rows, float TileWorld, float DetailTop, int[][] Tiles);

public sealed partial class WorldmapPlugin : IPlugin
{
    const string Script = "worldmap";
    const string Client = "client/";
    const string Gold = "#FFD700";
    const string PaleGold = "#EEE8AA";
    const string DefaultKey = "F4";
    static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(2);
    static readonly TimeSpan MinAge = TimeSpan.FromSeconds(15);
    static readonly TimeSpan PlayersEvery = TimeSpan.FromSeconds(0.2);
    static readonly TimeSpan SweepEvery = TimeSpan.FromSeconds(5);
    static readonly TimeSpan KeepRetired = TimeSpan.FromMinutes(1);
    static readonly TimeSpan KeepIdle = TimeSpan.FromMinutes(5);
    static readonly string Folder = Path.Combine("plugins", "data", "worldmap");
    static readonly Dictionary<string, string> Keys = KeyNames();
    static readonly Dictionary<string, Team> Spawns = new()
    {
        ["SpawnPointBlue"] = Team.Blue,
        ["SpawnPointRed"] = Team.Red,
        ["SpawnPointGreen"] = Team.Green,
        ["SpawnPointYellow"] = Team.Yellow,
    };

    readonly object _sync = new();
    readonly Dictionary<int, Shared> _maps = [];
    readonly Dictionary<int, DateTime> _idle = [];
    readonly HashSet<int> _dirty = [];
    readonly HashSet<int> _painting = [];
    readonly HashSet<int> _again = [];
    readonly HashSet<IPlayer> _open = [];
    readonly HashSet<IPlayer> _greet = [];
    readonly Dictionary<(int Profile, int World), string> _views = [];
    readonly Dictionary<int, Head> _heads = [];
    readonly Dictionary<int, bool> _teams = [];
    readonly Dictionary<int, IReadOnlySet<int>> _hidden = [];
    readonly List<(DateTime At, string Id)> _retired = [];
    readonly ConcurrentQueue<Action> _later = new();
    readonly Textures? _textures = Textures.Load(Path.Combine(Folder, "materials.bin"));
    DateTime _sent = DateTime.MinValue;
    DateTime _swept = DateTime.MinValue;
    bool _links;
    IServer _server = null!;
    IPluginStorage _settings = null!;

    [GeneratedRegex(@"^map-(\d+)--?\d+--?\d+\.png$")]
    private static partial Regex TileName();

    public void Load(IServer server)
    {
        _server = server;
        if (!typeof(IScripts).GetMethods().Any(method => method.Name == "AddFile"))
        {
            server.Log("worldmap: this server is too old for the map, it needs Scripts.AddFile, update the server and restart it");
            return;
        }
        _settings = server.Storage("worldmap");
        server.Scripts.Add(Script, ClientFiles());
        server.ScriptMessage += Message;
        server.CubesChanged += changes => Dirty(changes.World, false);
        server.ObjectAdded += (world, _, _) => Dirty(world, true);
        server.ObjectRemoved += (world, _, _) => Dirty(world, true);
        server.AddCommand("worldmap", Command);
        server.PlayerLeft += Left;
        server.PlayerJoined += player =>
        {
            lock (_sync)
                _greet.Add(player);
        };
        _links = typeof(IWorld).GetMethods().Any(method => method.Name == "Powered") && typeof(IServer).GetEvents().Any(each => each.Name == "LinkAdded");
        if (_links)
            WatchLinks(server);
        server.Update += _ =>
        {
            while (_later.TryDequeue(out Action? action))
                action();
            DateTime now = DateTime.UtcNow;
            if (now - _sent >= PlayersEvery)
            {
                _sent = now;
                SendPlayers();
            }
            if (now - _swept >= SweepEvery)
            {
                _swept = now;
                Sweep();
            }
        };
        if (_textures is null)
            server.Log($"worldmap: no {Path.Combine(Folder, "materials.bin")}, close up maps use flat colors");
    }

    void WatchLinks(IServer server)
    {
        server.LinkAdded += (world, _, _) => Dirty(world, false);
        server.LinkRemoved += (world, _, _) => Dirty(world, false);
    }

    IReadOnlySet<int> Hidden(IWorld world) =>
        _links && world.Play && !Setting(world.Id, "hidden", false) ? HiddenBy(world) : new HashSet<int>();

    static HashSet<int> HiddenBy(IWorld world)
    {
        HashSet<int> hiders = [];
        foreach (IWorldObject obj in world.Objects)
            if (obj.Type == "ModelToggle" && world.Powered(obj.Id) != true)
                hiders.Add(obj.Id);
        HashSet<int> hidden = [];
        foreach (WorldLink link in world.ObjectLinks)
            if (hiders.Contains(link.From))
                hidden.Add(link.To);
        return hidden;
    }

    void Dirty(IWorld world, bool objects)
    {
        lock (_sync)
        {
            _dirty.Add(world.Id);
            if (objects)
                _teams.Remove(world.Id);
        }
    }

    void Message(IPlayer player, string name, string data)
    {
        switch (name)
        {
            case "worldmap:hello":
                ShowStyle(player);
                Greet(player);
                ShowHead(player);
                Show(player);
                break;
            case "worldmap:open":
                Open(player, data == "1");
                break;
            case "worldmap:view" when View(data) is string view:
                lock (_sync)
                    _views[(player.ProfileId, player.World.Id)] = view;
                break;
        }
    }

    void Left(IPlayer player)
    {
        lock (_sync)
        {
            _open.Remove(player);
            _greet.Remove(player);
            if (!_server.Players.Any(other => other != player && other.ProfileId == player.ProfileId) && _heads.Remove(player.ProfileId, out Head? head))
                Retire(head.Face, head.Ring);
        }
    }

    void ShowStyle(IPlayer player) =>
        player.SendScriptMessage("worldmap:style", $"{KeyName} {AccentColor.TrimStart('#')}");

    void Greet(IPlayer player)
    {
        bool joined;
        lock (_sync)
            joined = _greet.Remove(player);
        if (!joined || _settings.Get("welcome") == "off")
            return;
        player.Message($"Press {Accent(KeyName)} to open the map", Pale);
        if (player.IsAdmin)
            player.Message($"Type {Accent("!worldmap help")} for help with the map", Pale);
    }

    void ShowHead(IPlayer player)
    {
        string? key = AvatarHead.Key(player.Skin);
        Head? head;
        lock (_sync)
            _heads.TryGetValue(player.ProfileId, out head);
        if (key is not null && head?.Key != key)
        {
            HeadImages? images = null;
            try
            {
                images = AvatarHead.Render(player.Skin, _textures);
            }
            catch (Exception error) when (error is FormatException or ArgumentException or IndexOutOfRangeException)
            {
                _server.Log($"worldmap: no head for {player.Name}: {error.Message}");
            }
            Head fresh = new(key, images is null ? null : _server.Scripts.AddFile(images.Head), images is null ? null : _server.Scripts.AddFile(images.Ring));
            lock (_sync)
            {
                if (head is not null)
                    Retire(head.Face, head.Ring);
                _heads[player.ProfileId] = fresh;
            }
            head = fresh;
        }
        player.SendScriptMessage("worldmap:me", head is { Face: string face, Ring: string ring } ? $"{face} {ring}" : "-");
    }

    void Show(IPlayer player)
    {
        IWorld world = player.World;
        Shared? map = MapOf(world.Id);
        bool open;
        lock (_sync)
            open = _open.Contains(player);
        if (map is null || map.At == DateTime.MinValue)
            Paint(world);
        if (map is not null)
            SendMap(player);
        else if (open)
            player.SendScriptMessage("worldmap:wait", "1");
    }

    void Open(IPlayer player, bool open)
    {
        lock (_sync)
        {
            if (open)
                _open.Add(player);
            else
                _open.Remove(player);
        }
        if (!open)
            return;
        int id = player.World.Id;
        bool known;
        lock (_sync)
            known = _maps.ContainsKey(id);
        if (!known && MapOf(id) is not null)
            SendMap(player);
        if (Stale(id))
            Paint(player.World);
    }

    void SendMap(IPlayer player)
    {
        int id = player.World.Id;
        Shared? map;
        bool open;
        string view;
        lock (_sync)
        {
            if (!_maps.TryGetValue(id, out map))
                return;
            open = _open.Contains(player);
            view = _views.GetValueOrDefault((player.ProfileId, id)) ?? "-";
        }
        MapImage layout = map.Layout;
        string tiles = map.Tiles.Count == 0 ? "-" : string.Join(',', map.Tiles.Select(tile => $"{tile.Key.Col}:{tile.Key.Row}:{tile.Value}"));
        player.SendScriptMessage("worldmap:map", string.Join(' ',
            map.Overview, Number(layout.MinX), Number(layout.MinZ), Number(layout.Unit), layout.Width, layout.Height, open ? 1 : 0,
            layout.Background.ToString("x6"), Number(layout.TileWorld), tiles, view));
    }

    static string? View(string data)
    {
        string[] parts = data.Split(',');
        if (parts.Length != 3)
            return null;
        int[] values = new int[3];
        for (int i = 0; i < 3; i++)
            if (!int.TryParse(parts[i], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out values[i]))
                return null;
        return values[2] > 0 ? string.Join(',', values) : null;
    }

    Shared? MapOf(int id)
    {
        lock (_sync)
            if (_maps.TryGetValue(id, out Shared? known))
                return known;
        if (Load(id) is not MapImage image || Share(image, DateTime.MinValue) is not Shared shared)
            return null;
        lock (_sync)
        {
            if (_maps.TryGetValue(id, out Shared? raced))
                return raced;
            _maps[id] = shared;
            return shared;
        }
    }

    Shared? Share(MapImage image, DateTime at)
    {
        if (_server.Scripts.AddFile(image.Png) is not string overview)
        {
            _server.Log("worldmap: the server took no more map files");
            return null;
        }
        Dictionary<(int, int), string> tiles = [];
        foreach (MapTile tile in image.Tiles)
            if (_server.Scripts.AddFile(tile.Png) is string id)
                tiles[(tile.Col, tile.Row)] = id;
        return new Shared(image with { Png = [], Tiles = [] }, overview, tiles, at);
    }

    void Retire(params string?[] ids)
    {
        foreach (string? id in ids)
            if (id is not null)
                _retired.Add((DateTime.UtcNow, id));
    }

    void RetireFiles(Shared map)
    {
        Retire(map.Overview);
        Retire([.. map.Tiles.Values]);
    }

    void Sweep()
    {
        DateTime now = DateTime.UtcNow;
        List<IWorld> worlds = [.. _server.Worlds.Where(world => world.Players.Count > 0)];
        HashSet<int> played = [.. worlds.Select(world => world.Id)];
        foreach (IWorld world in worlds)
        {
            IReadOnlySet<int>? painted;
            lock (_sync)
                if (!_hidden.TryGetValue(world.Id, out painted))
                    continue;
            if (!Hidden(world).SetEquals(painted))
                lock (_sync)
                    _dirty.Add(world.Id);
        }
        List<string> remove = [];
        lock (_sync)
        {
            foreach (int id in _maps.Keys.ToList())
            {
                if (played.Contains(id) || _painting.Contains(id))
                    _idle.Remove(id);
                else if (!_idle.TryGetValue(id, out DateTime since))
                    _idle[id] = now;
                else if (now - since >= KeepIdle)
                {
                    RetireFiles(_maps[id]);
                    _maps.Remove(id);
                    _idle.Remove(id);
                }
            }
            if (_retired.Count > 0 && now - _retired[0].At >= KeepRetired)
            {
                HashSet<string> used = [.. _maps.Values.SelectMany(map => map.Tiles.Values.Append(map.Overview)),
                    .. _heads.Values.SelectMany(head => new[] { head.Face, head.Ring }).OfType<string>()];
                while (_retired.Count > 0 && now - _retired[0].At >= KeepRetired)
                {
                    if (!used.Contains(_retired[0].Id))
                        remove.Add(_retired[0].Id);
                    _retired.RemoveAt(0);
                }
            }
        }
        foreach (string id in remove.Distinct())
            _server.Scripts.RemoveFile(id);
    }

    bool Stale(int world)
    {
        lock (_sync)
        {
            if (!_maps.TryGetValue(world, out Shared? map))
                return true;
            TimeSpan age = DateTime.UtcNow - map.At;
            return age > MinAge && (_dirty.Contains(world) || age > MaxAge);
        }
    }

    void Paint(IWorld world, bool again = false)
    {
        int id = world.Id;
        lock (_sync)
            if (!_painting.Add(id))
            {
                if (again)
                    _again.Add(id);
                return;
            }
        IReadOnlySet<int> hidden = Hidden(world);
        Scene scene = MapPainter.Read(world, Top(id), Shows(id, "models"), hidden);
        Shared? old;
        lock (_sync)
        {
            _hidden[id] = hidden;
            _dirty.Remove(id);
            _maps.TryGetValue(id, out old);
        }
        _ = Task.Run(() =>
        {
            MapImage? image = null;
            bool changed = false;
            string? error = null;
            try
            {
                image = MapPainter.Paint(scene, _textures);
                changed = image is not null && (old is null || !Same(old, image));
                if (changed)
                    Save(id, image!);
            }
            catch (Exception failure)
            {
                error = failure.ToString();
            }
            _later.Enqueue(() => Painted(id, image, changed, error));
        });
    }

    void Painted(int id, MapImage? image, bool changed, string? error)
    {
        if (error is not null)
            _server.Log($"worldmap: world {id} failed: {error}");
        Shared? shared = image is not null && changed ? Share(image, DateTime.UtcNow) : null;
        List<IPlayer> nothing = [];
        bool again;
        lock (_sync)
        {
            _painting.Remove(id);
            again = _again.Remove(id);
            if (shared is not null)
            {
                if (_maps.TryGetValue(id, out Shared? old))
                    RetireFiles(old);
                _maps[id] = shared;
            }
            else if (image is not null && _maps.TryGetValue(id, out Shared? same))
                _maps[id] = same with { At = DateTime.UtcNow };
            else if (!_maps.ContainsKey(id))
            {
                nothing = [.. _open.Where(player => player.World.Id == id)];
                _open.ExceptWith(nothing);
            }
        }
        foreach (IPlayer player in nothing)
            player.SendScriptMessage("worldmap:wait", "0");
        IWorld? world = _server.Worlds.FirstOrDefault(each => each.Id == id);
        if (shared is not null)
        {
            _server.Log($"worldmap: world {id} is {image!.Width}x{image.Height} at {Number(image.Unit)} units a pixel, "
                + $"{image.Tiles.Count} close up tiles, {(image.Png.Length + image.Tiles.Sum(tile => tile.Png.Length)) / 1024} KB");
            foreach (IPlayer player in world?.Players ?? [])
                SendMap(player);
        }
        if (again && world is not null)
            Paint(world);
    }

    static bool Same(Shared old, MapImage image)
    {
        MapImage known = old.Layout;
        return (known.MinX, known.MinZ, known.Unit, known.Width, known.Height, known.Background, known.Cols, known.Rows, known.TileWorld, known.DetailTop)
                == (image.MinX, image.MinZ, image.Unit, image.Width, image.Height, image.Background, image.Cols, image.Rows, image.TileWorld, image.DetailTop)
            && old.Overview == Hash(image.Png) && old.Tiles.Count == image.Tiles.Count
            && image.Tiles.All(tile => old.Tiles.GetValueOrDefault((tile.Col, tile.Row)) == Hash(tile.Png));
    }

    static string Hash(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));

    void SendPlayers()
    {
        List<IPlayer> watching;
        lock (_sync)
            watching = [.. _open.Where(player => _maps.ContainsKey(player.World.Id))];
        foreach (IPlayer viewer in watching)
        {
            IWorld world = viewer.World;
            bool teams = HasTeams(world);
            bool everyone = !teams || Shows(world.Id, "enemies");
            StringBuilder text = new(teams ? ((int)viewer.Team).ToString(CultureInfo.InvariantCulture) : "-");
            if (Shows(world.Id, "players"))
                foreach (IPlayer other in world.Players)
                {
                    if (other.Actor == viewer.Actor || other.Position is not Vector3 at || !everyone && other.Team != viewer.Team)
                        continue;
                    Head? head;
                    lock (_sync)
                        _heads.TryGetValue(other.ProfileId, out head);
                    text.Append('\n').Append(other.ProfileId).Append('\t').Append(Number(at.X)).Append('\t').Append(Number(at.Z))
                        .Append('\t').Append(teams ? ((int)other.Team).ToString(CultureInfo.InvariantCulture) : "-")
                        .Append('\t').Append(head?.Face ?? "-").Append('\t').Append(head?.Ring ?? "-").Append('\t').Append(other.Name);
                }
            viewer.SendScriptMessage("worldmap:players", text.ToString());
        }
    }

    bool HasTeams(IWorld world)
    {
        lock (_sync)
            if (_teams.TryGetValue(world.Id, out bool known))
                return known;
        HashSet<Team> teams = [];
        foreach (IWorldObject obj in world.Objects)
        {
            if (Spawns.TryGetValue(obj.Type, out Team team))
                teams.Add(team);
            else if (obj.Type == "AvatarSpawnRoleCreator" && obj.Data.GetValueOrDefault("team") is int value && Enum.IsDefined((Team)value))
                teams.Add((Team)value);
        }
        lock (_sync)
            _teams[world.Id] = teams.Count > 1;
        return teams.Count > 1;
    }

    static string Key(int world, string name) => $"{world}.{name}";

    bool Shows(int world, string name) => Setting(world, name, true);

    bool Setting(int world, string name, bool fallback) => _settings.Get(Key(world, name)) switch
    {
        "on" => true,
        "off" => false,
        _ => fallback,
    };

    float? Top(int world) =>
        float.TryParse(_settings.Get(Key(world, "height")), NumberStyles.Float, CultureInfo.InvariantCulture, out float top) ? top : null;

    void Command(IPlayer player, string[] arguments)
    {
        IWorld world = player.World;
        string option = arguments.Length > 0 ? arguments[0].ToLowerInvariant() : "";
        string value = arguments.Length > 1 ? arguments[1].ToLowerInvariant() : "";
        if (option is "help" or "?")
        {
            Help(player);
            return;
        }
        if (!player.IsAdmin)
        {
            player.Message($"Only the server's admins can change the map, {Accent("!worldmap help")} shows how to use it", Pale);
            return;
        }
        switch (option)
        {
            case "players" or "enemies" when value is "on" or "off":
                _settings.Set(Key(world.Id, option), value == "off" ? "off" : null);
                player.Message(option == "players"
                    ? $"Other players are {Accent(value == "on" ? "shown" : "hidden")} on the map of this world"
                    : $"Players of other teams are {Accent(value == "on" ? "shown" : "hidden")} on the map of this world", Pale);
                break;
            case "models" when value is "on" or "off":
                _settings.Set(Key(world.Id, "models"), value == "off" ? "off" : null);
                player.Message($"The map of this world {Accent(value == "on" ? "shows" : "leaves out")} models, the new map comes in a moment", Pale);
                Repaint(world);
                break;
            case "hidden" when value is "on" or "off":
                _settings.Set(Key(world.Id, "hidden"), value == "on" ? "on" : null);
                player.Message($"The map of this world {Accent(value == "on" ? "shows" : "leaves out")} models hidden by a Cube Model Hider, the new map comes in a moment", Pale);
                Repaint(world);
                break;
            case "height" when value == "off":
                _settings.Set(Key(world.Id, "height"), null);
                player.Message($"The map of this world shows {Accent("everything")} again, the new map comes in a moment", Pale);
                Repaint(world);
                break;
            case "height" when float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float top) && float.IsFinite(top):
                _settings.Set(Key(world.Id, "height"), Number(top));
                player.Message($"The map of this world leaves out what is higher than {Accent(Number(top))}, the new map comes in a moment", Pale);
                Repaint(world);
                break;
            case "poster":
                Poster(player, arguments.Length > 1 ? arguments[1] : null);
                break;
            case "key" when Keys.TryGetValue(value, out string? named):
                _settings.Set("key", named == DefaultKey ? null : named);
                player.Message($"The map opens with {Accent(named)} on this server now", Pale);
                ShowStyleToAll();
                break;
            case "key":
                player.Message($"The key can be {Accent("F1")} to {Accent("F24")}, {Accent("Tab")}, {Accent("Insert")}, {Accent("Delete")}, "
                    + $"{Accent("Home")}, {Accent("End")}, {Accent("PageUp")} or {Accent("PageDown")}, keys that type would stop working in the chat", Pale);
                break;
            case "hue" when value is "default" or "off":
                _settings.Set("hue", null);
                player.Message($"The map is {Accent("gold")} again", Pale);
                ShowStyleToAll();
                break;
            case "hue" when float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float hue) && hue >= 0 && hue <= 360:
                _settings.Set("hue", Number(hue));
                player.Message($"The map has the hue {Accent(Number(hue))} now", Pale);
                ShowStyleToAll();
                break;
            case "welcome" when value is "on" or "off":
                _settings.Set("welcome", value == "off" ? "off" : null);
                player.Message($"The welcome message is {Accent(value)} on this server", Pale);
                break;
            default:
                string height = Top(world.Id) is float cut ? $"up to {Number(cut)}" : "everything";
                string standing = player.Position is Vector3 at ? $", you stand at {Accent(Number(MathF.Round(at.Y, 1)))}" : "";
                player.Message($"Map of this world: other players {Accent(OnOff(Shows(world.Id, "players")))}, other teams {Accent(OnOff(Shows(world.Id, "enemies")))}, "
                    + $"models {Accent(OnOff(Shows(world.Id, "models")))}, hidden models {Accent(OnOff(Setting(world.Id, "hidden", false)))}, height {Accent(height)}{standing}", Pale);
                player.Message($"On this server: key {Accent(KeyName)}, hue {Accent(Hue() is float shade ? Number(shade) : "gold")}, "
                    + $"welcome {Accent(OnOff(_settings.Get("welcome") != "off"))}", Pale);
                player.Message($"{Accent("!worldmap help")} shows the commands", Pale);
                break;
        }
    }

    void Help(IPlayer player)
    {
        player.Message($"{Accent(KeyName)} opens the map, {Accent("drag")} moves it, {Accent("scroll")} zooms, the button next to {Accent("X")} finds you, "
            + $"{Accent(KeyName)}, {Accent("Esc")} or {Accent("X")} closes it", Pale);
        if (!player.IsAdmin)
            return;
        player.Message($"{Accent("!worldmap")}: the map settings of this world", Pale);
        player.Message($"{Accent("!worldmap players on|off")}: other players on the map", Pale);
        player.Message($"{Accent("!worldmap enemies on|off")}: players of other teams on the map", Pale);
        player.Message($"{Accent("!worldmap models on|off")}: models on the map, off leaves only the terrain", Pale);
        player.Message($"{Accent("!worldmap hidden on|off")}: models hidden by a Cube Model Hider, off at first", Pale);
        player.Message($"{Accent("!worldmap height y|off")}: leaves out everything above height y, for lobbies in the sky", Pale);
        player.Message($"{Accent("!worldmap poster [times]")}: saves a big picture of the whole map on the server", Pale);
        player.Message($"{Accent("!worldmap key F1-F24")}: the key that opens the map on the whole server", Pale);
        player.Message($"{Accent("!worldmap hue 0-360|default")}: the color of the map's texts, gold at first", Pale);
        player.Message($"{Accent("!worldmap welcome on|off")}: the message about the map when someone joins", Pale);
    }

    string KeyName => _settings.Get("key") is string key && Keys.ContainsKey(key) ? key : DefaultKey;

    float? Hue() => float.TryParse(_settings.Get("hue"), NumberStyles.Float, CultureInfo.InvariantCulture, out float hue) ? hue : null;

    string AccentColor => Hue() is float hue ? Hsv(hue, 1, 1) : Gold;

    string Pale => Hue() is float hue ? Hsv(hue, 0.29f, 0.93f) : PaleGold;

    string Accent(object text) => $"<color={AccentColor}>{text}</color>";

    void ShowStyleToAll()
    {
        foreach (IPlayer player in _server.Players)
            ShowStyle(player);
    }

    static string Hsv(float hue, float saturation, float value)
    {
        float chroma = value * saturation, x = chroma * (1 - MathF.Abs(hue / 60 % 2 - 1)), lift = value - chroma;
        (float r, float g, float b) = (int)(hue / 60) switch
        {
            0 => (chroma, x, 0f),
            1 => (x, chroma, 0f),
            2 => (0f, chroma, x),
            3 => (0f, x, chroma),
            4 => (x, 0f, chroma),
            _ => (chroma, 0f, x),
        };
        return $"#{(int)MathF.Round((r + lift) * 255):X2}{(int)MathF.Round((g + lift) * 255):X2}{(int)MathF.Round((b + lift) * 255):X2}";
    }

    static Dictionary<string, string> KeyNames()
    {
        Dictionary<string, string> keys = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 1; i <= 24; i++)
            keys[$"F{i}"] = $"F{i}";
        foreach (string name in new[] { "Tab", "Insert", "Delete", "Home", "End", "PageUp", "PageDown" })
            keys[name] = name;
        return keys;
    }

    static string OnOff(bool on) => on ? "on" : "off";

    void Repaint(IWorld world)
    {
        lock (_sync)
            _dirty.Add(world.Id);
        Paint(world, true);
    }

    void Poster(IPlayer player, string? argument)
    {
        float times = argument is not null && float.TryParse(argument.TrimStart('x', 'X'), NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed) ? parsed : 4;
        int id = player.World.Id;
        Scene scene = MapPainter.Read(player.World, Top(id), Shows(id, "models"), Hidden(player.World));
        if (scene.Quads.Count == 0)
        {
            player.Message("This world has no cubes to draw", Pale);
            return;
        }
        float scale = MapPainter.OverviewScale(scene) * Math.Clamp(times, 1, 16);
        player.Message($"Drawing world {Accent(id)} at {Accent($"{Number(times)}x")}, this can take a while...", Pale);
        _ = Task.Run(() =>
        {
            try
            {
                (byte[] png, int width, int height) = MapPainter.Poster(scene, _textures, scale);
                Directory.CreateDirectory(Folder);
                string file = Path.GetFullPath(Path.Combine(Folder, $"map-{id}-x{Number(times)}.png"));
                File.WriteAllBytes(file, png);
                _later.Enqueue(() =>
                {
                    player.Message($"Saved {Accent($"{width}x{height}")} to {file}", Pale);
                    _server.Log($"worldmap: {player.Name} saved world {id} at {Number(times)}x, {width}x{height}, {png.Length / 1024} KB, {file}");
                });
            }
            catch (Exception error)
            {
                _later.Enqueue(() => _server.Log($"worldmap: poster of world {id} failed: {error}"));
            }
        });
    }

    static void Save(int id, MapImage image)
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllBytes(Path.Combine(Folder, FileOf(id)), image.Png);
        HashSet<string> tiles = [];
        foreach (MapTile tile in image.Tiles)
        {
            string name = TileOf(id, tile.Col, tile.Row);
            File.WriteAllBytes(Path.Combine(Folder, name), tile.Png);
            tiles.Add(name);
        }
        foreach (string file in Directory.GetFiles(Folder, $"map-{id}-*.png"))
        {
            string name = Path.GetFileName(file);
            if (TileName().Match(name) is { Success: true } match && match.Groups[1].Value == id.ToString(CultureInfo.InvariantCulture) && !tiles.Contains(name))
                File.Delete(file);
        }
        SavedMap saved = new(image.MinX, image.MinZ, image.Unit, image.Width, image.Height, image.Background,
            image.Cols, image.Rows, image.TileWorld, image.DetailTop, [.. image.Tiles.Select(tile => new[] { tile.Col, tile.Row })]);
        File.WriteAllText(Path.Combine(Folder, $"map-{id}.json"), JsonSerializer.Serialize(saved));
    }

    MapImage? Load(int id)
    {
        string meta = Path.Combine(Folder, $"map-{id}.json");
        if (!File.Exists(meta))
            return null;
        try
        {
            SavedMap saved = JsonSerializer.Deserialize<SavedMap>(File.ReadAllText(meta)) ?? throw new JsonException("empty");
            List<MapTile> tiles = [.. (saved.Tiles ?? []).Select(tile => new MapTile(tile[0], tile[1], File.ReadAllBytes(Path.Combine(Folder, TileOf(id, tile[0], tile[1])))))];
            return new MapImage(File.ReadAllBytes(Path.Combine(Folder, FileOf(id))), saved.MinX, saved.MinZ, saved.Unit, saved.Width, saved.Height,
                saved.Background, saved.Cols, saved.Rows, saved.TileWorld, saved.DetailTop, tiles);
        }
        catch (Exception error) when (error is IOException or JsonException or IndexOutOfRangeException or UnauthorizedAccessException)
        {
            _server.Log($"worldmap: could not read the saved map of world {id}: {error.Message}");
            return null;
        }
    }

    static string FileOf(int world) => $"map-{world}.png";

    static string TileOf(int world, int col, int row) => $"map-{world}-{col}-{row}.png";

    static string Number(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    static Dictionary<string, byte[]> ClientFiles()
    {
        Dictionary<string, byte[]> files = [];
        foreach (string name in typeof(WorldmapPlugin).Assembly.GetManifestResourceNames().Where(name => name.StartsWith(Client)))
        {
            using Stream stream = typeof(WorldmapPlugin).Assembly.GetManifestResourceStream(name)!;
            using MemoryStream copy = new();
            stream.CopyTo(copy);
            files[name[Client.Length..]] = copy.ToArray();
        }
        return files;
    }
}
