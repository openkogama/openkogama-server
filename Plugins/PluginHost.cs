using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using OpenKogama.Api;
using OpenKogama.Content;
using OpenKogama.Game;
using OpenKogama.World;

namespace OpenKogama.Plugins;

public static class PluginHost
{
    const byte ChatText = 5;
    const int TickMs = 1000;

    static readonly AssemblyName ApiAssembly = typeof(IPlugin).Assembly.GetName();
    static PluginServer Server = new();
    static readonly object Sync = new();
    static readonly HashSet<string> UnknownAnimations = [];
    static readonly List<AssemblyLoadContext> Contexts = [];
    static readonly List<IPlugin> Loaded = [];
    static string _folder = "plugins";
    static readonly ConditionalWeakTable<Player, PluginPlayer> Players = new();
    static readonly ConditionalWeakTable<Session, PluginWorld> Worlds = new();
    static readonly HashSet<Player> Present = [];
    static Func<IReadOnlyList<Session>> _sessions = () => [];
    static string _dataFolder = Path.Combine("plugins", "data");
    static long _lastTick;
    static long _lastUpdate = Environment.TickCount64;

    public static IReadOnlyCollection<string> Admins { get; set; } = [];

    public static void LoadAll(string folder, Func<IReadOnlyList<Session>> sessions)
    {
        _sessions = sessions;
        _folder = folder;
        _dataFolder = Path.Combine(folder, "data");
        Directory.CreateDirectory(folder);
        lock (Sync) Load();
    }

    public static int Reload()
    {
        lock (Sync)
        {
            UnloadPlugins();
            foreach (AssemblyLoadContext context in Contexts) context.Unload();
            Contexts.Clear();

            int count = Load();
            lock (Present)
                foreach (Player player in Present)
                    if (Players.TryGetValue(player, out PluginPlayer? wrapper)) Server.OnJoined(wrapper);
            return count;
        }
    }

    public static void Shutdown()
    {
        lock (Sync) UnloadPlugins();
    }

    static void UnloadPlugins()
    {
        foreach (IPlugin plugin in Loaded)
        {
            try
            {
                plugin.Unload();
            }
            catch (Exception error)
            {
                Console.WriteLine($"plugin {plugin.GetType().FullName} failed to unload: {error}");
            }
        }
        Loaded.Clear();
        CustomContent.Clear();
        ClientScripts.ClearAdded();
        Themes.Reset();
        Server = new PluginServer();
    }

    static int Load()
    {
        foreach (string path in Directory.GetFiles(_folder, "*.dll"))
        {
            var context = new AssemblyLoadContext(Path.GetFileNameWithoutExtension(path), isCollectible: true);
            context.Resolving += (resolver, name) =>
                File.Exists(Path.Combine(_folder, name.Name + ".dll")) ? FromFile(resolver, Path.Combine(_folder, name.Name + ".dll")) : null;
            try
            {
                Assembly assembly = FromFile(context, path);
                if (assembly.GetReferencedAssemblies().FirstOrDefault(reference => reference.Name == ApiAssembly.Name)?.Version is Version needed && needed > ApiAssembly.Version)
                {
                    Console.WriteLine($"plugin {Path.GetFileName(path)} needs plugin API {needed.ToString(2)}, this server has {ApiAssembly.Version!.ToString(2)}, update the server");
                    context.Unload();
                    continue;
                }
                foreach (Type type in assembly.GetTypes().Where(type => typeof(IPlugin).IsAssignableFrom(type) && !type.IsAbstract))
                {
                    var plugin = (IPlugin)Activator.CreateInstance(type)!;
                    plugin.Load(Server);
                    Loaded.Add(plugin);
                    Console.WriteLine($"plugin {type.FullName} loaded from {Path.GetFileName(path)}");
                }
                Contexts.Add(context);
            }
            catch (Exception error)
            {
                Console.WriteLine($"plugin {Path.GetFileName(path)} failed to load: {error}");
                context.Unload();
            }
        }
        return Loaded.Count;
    }

    static Assembly FromFile(AssemblyLoadContext context, string path)
    {
        using var stream = new MemoryStream(File.ReadAllBytes(path));
        return context.LoadFromStream(stream);
    }

    internal static bool Admin(Player player)
    {
        IPAddress address = Access.Plain(player.Peer.EndPoint.Address);
        bool tunneled = P2p.Tunnel.Carries(address);
        return IPAddress.IsLoopback(address) && !tunneled || Access.Admin(tunneled ? null : address, player.Account);
    }

    internal static PluginPlayer PlayerOf(Session session, Player player) => Players.GetValue(player, _ => new PluginPlayer(player, session));

    internal static PluginWorld WorldOf(Session session) => Worlds.GetValue(session, _ => new PluginWorld(session));

    internal static IReadOnlyList<Session> Sessions => _sessions();

    public static void Joined(Session session, Player player)
    {
        lock (Present)
            if (!Present.Add(player)) return;
        Server.OnJoined(PlayerOf(session, player));
    }

    public static void Left(Session session, Player player)
    {
        lock (Present)
            if (!Present.Remove(player)) return;
        Server.OnLeft(PlayerOf(session, player));
    }

    public static void Tick()
    {
        long now = Environment.TickCount64;
        if (Server.WatchesTriggers)
            foreach (Session session in Sessions)
                TeleporterPads.Watch(session, Triggered);
        Server.OnUpdate((now - Interlocked.Exchange(ref _lastUpdate, now)) / 1000f);
        foreach ((string token, ScriptMessages.Message message) in ScriptMessages.Take())
            if (ByToken(token) is (Session session, Player player)) Server.OnScriptMessage(PlayerOf(session, player), message.Name, message.Data);
        if (now - Interlocked.Read(ref _lastTick) < TickMs) return;
        Interlocked.Exchange(ref _lastTick, now);
        ScriptMessages.Sweep();
        Server.OnTick();
    }

    static (Session, Player)? ByToken(string token)
    {
        foreach (Session session in Sessions)
            foreach (Player player in session.Players)
                if (player.Token == token) return (session, player);
        return null;
    }

    public static string? Chat(Session session, Player player, string text)
    {
        if (text.Trim() is not ("/reload" or "!reload")) return Server.OnChat(PlayerOf(session, player), text);

        PluginPlayer me = PlayerOf(session, player);
        if (!Admin(player))
        {
            me.Message("Only the server's admins can reload plugins");
            return null;
        }
        int count = Reload();
        me.Message($"{count} plugins reloaded");
        Console.WriteLine($"{player.Username} reloaded {count} plugins");
        return null;
    }

    public static void Killed(Session session, Player victim, Player killer) =>
        Server.OnKilled(PlayerOf(session, victim), PlayerOf(session, killer));

    public static void Hit(Session session, Player victim, Player attacker) =>
        Server.OnHit(PlayerOf(session, victim), PlayerOf(session, attacker));

    public static bool WatchesCubes => Server.WatchesCubes;

    public static bool CubesChanged(Session session, Player player, Prototype prototype, List<CubeChange> changes)
    {
        GameWorld world = session.World;
        bool terrain = world.FindFirst(WorldObjectType.CubeModelPrototypeTerrain)?.PrototypeId == prototype.Id;
        ICubeModel? model = !terrain && world.FirstUser(prototype.Id) is int id ? new CubesEditor(world, session, id) : null;
        var edit = new CubeChanges(PlayerOf(session, player), WorldOf(session), model, changes);
        Server.OnCubesChanged(edit);
        return edit.Cancel;
    }

    public static bool ThemeChanging(Session session, Player player, ThemeEntry? theme, Dictionary<string, object>? settings)
    {
        var change = new ThemeChange(PlayerOf(session, player), WorldOf(session), theme is null ? null : PluginThemes.Of(theme),
            settings is null ? null : ThemeSettingsMap.ToSettings(settings));
        Server.OnThemeChanged(change);
        return change.Cancel;
    }

    public static void KilledByNpc(Session session, Player victim, Npc npc) =>
        Server.OnKilledByNpc(PlayerOf(session, victim), new PluginNpc(session, npc));

    public static void DamagedByNpc(Session session, Player victim, Npc npc, float amount) =>
        Server.OnDamagedByNpc(PlayerOf(session, victim), new PluginNpc(session, npc), amount);

    public static void Collected(Session session, Player player, WorldObject obj) =>
        Server.OnCollected(PlayerOf(session, player), new PluginObject(obj));

    public static void Triggered(Session session, Player player, WorldObject obj, bool on) =>
        Server.OnTriggered(PlayerOf(session, player), new PluginObject(obj), on);

    public static void RoundStarted(Session session) => Server.OnRoundStarted(WorldOf(session));

    public static void Gestured(Session session, Player player, Gesture gesture) => Server.OnGestured(PlayerOf(session, player), gesture);

    public static AvatarItem ItemOf(int type) => Enum.IsDefined((AvatarItem)type) ? (AvatarItem)type : AvatarItem.Unknown;

    public static void SwitchedItem(Session session, Player player, int type) => Server.OnSwitchedItem(PlayerOf(session, player), ItemOf(type));

    public static void PickedUp(Session session, Player player, int? type) =>
        Server.OnPickedUp(PlayerOf(session, player), type is int known ? ItemOf(known) : AvatarItem.Unknown);

    public static void Animated(Session session, Player player, string state)
    {
        if (!Enum.TryParse(state, ignoreCase: true, out AvatarAnimation animation))
        {
            animation = AvatarAnimation.Unknown;
            lock (UnknownAnimations)
                if (UnknownAnimations.Add(state)) Console.WriteLine($"animation state {state} has no AvatarAnimation value");
        }
        Server.OnAnimated(PlayerOf(session, player), animation, state);
    }

    public static void NpcDamaged(Session session, Npc npc, Player? by, float amount) =>
        Server.OnNpcDamaged(new PluginNpc(session, npc), by is null ? null : PlayerOf(session, by), amount);

    public static void NpcKilled(Session session, Npc npc, Player? by) =>
        Server.OnNpcKilled(new PluginNpc(session, npc), by is null ? null : PlayerOf(session, by));

    public static void Added(Session session, WorldObject obj, Player? by) =>
        Server.OnAdded(WorldOf(session), new PluginObject(obj), by is null ? null : PlayerOf(session, by));

    public static void Removed(Session session, int objectId, Player? by) =>
        Server.OnRemoved(WorldOf(session), objectId, by is null ? null : PlayerOf(session, by));

    public static bool Chat(Session session, Player player, Dictionary<object, object?> data)
    {
        object? key = data.Keys.FirstOrDefault(key => Convert.ToInt32(key) == ChatText);
        if (key is null || data[key] is not string text) return true;

        if (Chat(session, player, text) is not { } result) return false;
        data[key] = result;
        return true;
    }

    sealed class PluginServer : IServer
    {
        public event Action<ChatMessage>? Chat;
        public event Action<IPlayer>? PlayerJoined;
        public event Action<IPlayer>? PlayerLeft;
        public event Action? Tick;
        public event Action<float>? Update;
        public event Action<IPlayer, IPlayer>? PlayerKilled;
        public event Action<IPlayer, IPlayer>? PlayerHit;
        public event Action<IPlayer, INpc>? PlayerKilledByNpc;
        public event Action<IPlayer, INpc, float>? PlayerDamagedByNpc;
        public event Action<IPlayer, IWorldObject>? ItemCollected;
        public event Action<IWorld>? RoundStarted;
        public event Action<IWorld, IWorldObject, IPlayer?>? ObjectAdded;
        public event Action<IWorld, int, IPlayer?>? ObjectRemoved;
        public event Action<INpc, IPlayer?, float>? NpcDamaged;
        public event Action<INpc, IPlayer?>? NpcKilled;
        public event Action<IPlayer, Gesture>? PlayerGestured;
        public event Action<IPlayer, AvatarAnimation>? PlayerAnimated;
        public event Action<IPlayer, AvatarItem>? PlayerSwitchedItem;
        public event Action<IPlayer, AvatarItem>? PlayerPickedUp;
        public event Action<IPlayer, IWorldObject, bool>? PlayerTriggered;
        public event Action<CubeChanges>? CubesChanged;
        public event Action<ThemeChange>? ThemeChanged;
        public event Action<IPlayer, string, string>? ScriptMessage;
        public event Action<IPlayer, string>? Gesture;
        public event Action<IPlayer, string>? Animation;

        readonly Dictionary<string, Action<IPlayer, string[]>> _commands = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, PluginStorage> _storage = [];

        public ITemplates Templates { get; } = new PluginTemplates();
        public IAssets Assets { get; } = new PluginAssets();
        public IScripts Scripts { get; } = new PluginScripts();

        public IReadOnlyList<IPlayer> Players => [.. Sessions.SelectMany(session => session.Players.Select(player => PlayerOf(session, player)))];

        public IReadOnlyList<IWorld> Worlds => [.. Sessions.Select(WorldOf)];

        public void AddIcon(string name, byte[] png) => CustomBadges.Register(name, png);

        public void Announce(string text, string? icon = null)
        {
            foreach (Session session in Sessions)
                WorldOf(session).Announce(text, icon);
        }

        public void Notify(string text, bool brief = false)
        {
            foreach (Session session in Sessions)
                WorldOf(session).Notify(text, brief);
        }

        public void Broadcast(string text, string? color = null)
        {
            foreach (Session session in Sessions)
                WorldOf(session).Broadcast(text, color);
        }

        public void Log(string text) => Console.WriteLine($"plugin: {text}");

        public void AddCommand(string name, Action<IPlayer, string[]> handler)
        {
            lock (_commands) _commands[name.TrimStart('/')] = handler;
        }

        public IPluginStorage Storage(string name)
        {
            lock (_storage)
            {
                if (!_storage.TryGetValue(name, out PluginStorage? storage))
                    _storage[name] = storage = new PluginStorage(_dataFolder, name);
                return storage;
            }
        }

        public string? OnChat(IPlayer player, string text)
        {
            if (Command(text) is (Action<IPlayer, string[]> handler, string[] arguments))
            {
                Guard(() => handler(player, arguments));
                return null;
            }

            var message = new ChatMessage(player, text);
            Raise(Chat, message);
            return message.Cancel ? null : message.Text;
        }

        (Action<IPlayer, string[]>, string[])? Command(string text)
        {
            if (!text.StartsWith('/') && !text.StartsWith('!')) return null;
            string[] parts = text[1..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return null;
            lock (_commands)
                return _commands.TryGetValue(parts[0], out Action<IPlayer, string[]>? handler) ? (handler, parts[1..]) : null;
        }

        public void OnKilled(IPlayer victim, IPlayer killer)
        {
            foreach (Action<IPlayer, IPlayer> handler in Handlers(PlayerKilled))
                Guard(() => handler(victim, killer));
        }

        public void OnHit(IPlayer victim, IPlayer attacker)
        {
            foreach (Action<IPlayer, IPlayer> handler in Handlers(PlayerHit))
                Guard(() => handler(victim, attacker));
        }

        public void OnKilledByNpc(IPlayer victim, INpc killer)
        {
            foreach (Action<IPlayer, INpc> handler in Handlers(PlayerKilledByNpc))
                Guard(() => handler(victim, killer));
        }

        public void OnDamagedByNpc(IPlayer victim, INpc attacker, float amount)
        {
            foreach (Action<IPlayer, INpc, float> handler in Handlers(PlayerDamagedByNpc))
                Guard(() => handler(victim, attacker, amount));
        }

        public void OnCollected(IPlayer player, IWorldObject obj)
        {
            foreach (Action<IPlayer, IWorldObject> handler in Handlers(ItemCollected))
                Guard(() => handler(player, obj));
        }

        public bool WatchesTriggers => PlayerTriggered is not null;

        public bool WatchesCubes => CubesChanged is not null;

        public void OnCubesChanged(CubeChanges changes)
        {
            foreach (Action<CubeChanges> handler in Handlers(CubesChanged))
                Guard(() => handler(changes));
        }

        public void OnThemeChanged(ThemeChange change)
        {
            foreach (Action<ThemeChange> handler in Handlers(ThemeChanged))
                Guard(() => handler(change));
        }

        public void OnTriggered(IPlayer player, IWorldObject obj, bool on)
        {
            foreach (Action<IPlayer, IWorldObject, bool> handler in Handlers(PlayerTriggered))
                Guard(() => handler(player, obj, on));
        }

        public void OnRoundStarted(IWorld world) => Raise(RoundStarted, world);

        public void OnAdded(IWorld world, IWorldObject obj, IPlayer? by)
        {
            foreach (Action<IWorld, IWorldObject, IPlayer?> handler in Handlers(ObjectAdded))
                Guard(() => handler(world, obj, by));
        }

        public void OnRemoved(IWorld world, int objectId, IPlayer? by)
        {
            foreach (Action<IWorld, int, IPlayer?> handler in Handlers(ObjectRemoved))
                Guard(() => handler(world, objectId, by));
        }

        public void OnNpcDamaged(INpc npc, IPlayer? by, float amount)
        {
            foreach (Action<INpc, IPlayer?, float> handler in Handlers(NpcDamaged))
                Guard(() => handler(npc, by, amount));
        }

        public void OnNpcKilled(INpc npc, IPlayer? by)
        {
            foreach (Action<INpc, IPlayer?> handler in Handlers(NpcKilled))
                Guard(() => handler(npc, by));
        }

        static IEnumerable<T> Handlers<T>(T? handlers) where T : Delegate =>
            handlers?.GetInvocationList().Cast<T>() ?? [];

        public void OnScriptMessage(IPlayer player, string name, string data)
        {
            foreach (Action<IPlayer, string, string> handler in Handlers(ScriptMessage))
                Guard(() => handler(player, name, data));
        }

        public void OnJoined(IPlayer player) => Raise(PlayerJoined, player);

        public void OnLeft(IPlayer player) => Raise(PlayerLeft, player);

        public void OnGestured(IPlayer player, Gesture gesture)
        {
            foreach (Action<IPlayer, Gesture> handler in Handlers(PlayerGestured))
                Guard(() => handler(player, gesture));
            string name = gesture.ToString().ToLowerInvariant();
            foreach (Action<IPlayer, string> handler in Handlers(Gesture))
                Guard(() => handler(player, name));
        }

        public void OnAnimated(IPlayer player, AvatarAnimation animation, string state)
        {
            foreach (Action<IPlayer, AvatarAnimation> handler in Handlers(PlayerAnimated))
                Guard(() => handler(player, animation));
            foreach (Action<IPlayer, string> handler in Handlers(Animation))
                Guard(() => handler(player, state));
        }

        public void OnSwitchedItem(IPlayer player, AvatarItem item)
        {
            foreach (Action<IPlayer, AvatarItem> handler in Handlers(PlayerSwitchedItem))
                Guard(() => handler(player, item));
        }

        public void OnPickedUp(IPlayer player, AvatarItem item)
        {
            foreach (Action<IPlayer, AvatarItem> handler in Handlers(PlayerPickedUp))
                Guard(() => handler(player, item));
        }

        public void OnUpdate(float seconds) => Raise(Update, seconds);

        public void OnTick()
        {
            foreach (Action handler in Tick?.GetInvocationList().Cast<Action>() ?? [])
                Guard(() => handler());
        }

        static void Raise<T>(Action<T>? handlers, T argument)
        {
            foreach (Action<T> handler in handlers?.GetInvocationList().Cast<Action<T>>() ?? [])
                Guard(() => handler(argument));
        }

        static void Guard(Action action)
        {
            try
            {
                action();
            }
            catch (Exception error)
            {
                Console.WriteLine($"plugin handler failed: {error}");
            }
        }
    }
}
