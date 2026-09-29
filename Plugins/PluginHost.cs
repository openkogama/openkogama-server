using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using OpenKogama.Api;
using OpenKogama.Game;

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
    static long _lastTick;

    public static void LoadAll(string folder, Func<IReadOnlyList<Session>> sessions)
    {
        _sessions = sessions;
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
        if (now - Interlocked.Read(ref _lastTick) < TickMs) return;
        Interlocked.Exchange(ref _lastTick, now);
        Server.OnTick();
    }

    public static string? Chat(Session session, Player player, string text) => Server.OnChat(PlayerOf(session, player), text);

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

        public IReadOnlyList<IPlayer> Players => [.. Sessions.SelectMany(session => session.Players.Select(player => PlayerOf(session, player)))];

        public IReadOnlyList<IWorld> Worlds => [.. Sessions.Select(WorldOf)];

        public void Broadcast(string text, string? color = null)
        {
            foreach (Session session in Sessions)
                WorldOf(session).Broadcast(text, color);
        }

        public void Log(string text) => Console.WriteLine($"plugin: {text}");

        public string? OnChat(IPlayer player, string text)
        {
            var message = new ChatMessage(player, text);
            Raise(Chat, message);
            return message.Cancel ? null : message.Text;
        }

        public void OnJoined(IPlayer player) => Raise(PlayerJoined, player);

        public void OnLeft(IPlayer player) => Raise(PlayerLeft, player);

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
