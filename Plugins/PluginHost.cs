using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using OpenKogama.Api;
using OpenKogama.Game;
using OpenKogama.World;

namespace OpenKogama.Plugins;

public static class PluginHost
{
    const byte ChatText = 5;
    const int TickMs = 1000;

    static readonly PluginServer Server = new();
    static readonly ConditionalWeakTable<Player, PluginPlayer> Players = new();
    static readonly ConditionalWeakTable<Session, PluginWorld> Worlds = new();
    static readonly HashSet<Player> Present = [];
    static Func<IReadOnlyList<Session>> _sessions = () => [];
    static string _dataFolder = Path.Combine("plugins", "data");
    static long _lastTick;
    static long _lastUpdate = Environment.TickCount64;

    public static void LoadAll(string folder, Func<IReadOnlyList<Session>> sessions)
    {
        _sessions = sessions;
        _dataFolder = Path.Combine(folder, "data");
        Directory.CreateDirectory(folder);
        foreach (string path in Directory.GetFiles(folder, "*.dll"))
        {
            try
            {
                Assembly assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(path));
                foreach (Type type in assembly.GetTypes().Where(type => typeof(IPlugin).IsAssignableFrom(type) && !type.IsAbstract))
                {
                    ((IPlugin)Activator.CreateInstance(type)!).Load(Server);
                    Console.WriteLine($"plugin {type.FullName} loaded from {Path.GetFileName(path)}");
                }
            }
            catch (Exception error)
            {
                Console.WriteLine($"plugin {Path.GetFileName(path)} failed to load: {error}");
            }
        }
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
        Server.OnUpdate((now - Interlocked.Exchange(ref _lastUpdate, now)) / 1000f);
        if (now - Interlocked.Read(ref _lastTick) < TickMs) return;
        Interlocked.Exchange(ref _lastTick, now);
        Server.OnTick();
    }

    public static string? Chat(Session session, Player player, string text) => Server.OnChat(PlayerOf(session, player), text);

    public static void Killed(Session session, Player victim, Player killer) =>
        Server.OnKilled(PlayerOf(session, victim), PlayerOf(session, killer));

    public static void Collected(Session session, Player player, WorldObject obj) =>
        Server.OnCollected(PlayerOf(session, player), new PluginObject(obj));

    public static void RoundStarted(Session session) => Server.OnRoundStarted(WorldOf(session));

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
        public event Action<IPlayer, IWorldObject>? ItemCollected;
        public event Action<IWorld>? RoundStarted;
        public event Action<IWorld, IWorldObject, IPlayer?>? ObjectAdded;
        public event Action<IWorld, int, IPlayer?>? ObjectRemoved;
        public event Action<INpc, IPlayer?, float>? NpcDamaged;
        public event Action<INpc, IPlayer?>? NpcKilled;

        readonly Dictionary<string, Action<IPlayer, string[]>> _commands = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, PluginStorage> _storage = [];

        public ITemplates Templates { get; } = new PluginTemplates();

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
            if (!text.StartsWith('/')) return null;
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

        public void OnCollected(IPlayer player, IWorldObject obj)
        {
            foreach (Action<IPlayer, IWorldObject> handler in Handlers(ItemCollected))
                Guard(() => handler(player, obj));
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

        public void OnJoined(IPlayer player) => Raise(PlayerJoined, player);

        public void OnLeft(IPlayer player) => Raise(PlayerLeft, player);

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
