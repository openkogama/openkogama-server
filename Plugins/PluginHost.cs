using System.Reflection;
using System.Runtime.Loader;
using OpenKogama.Api;
using OpenKogama.Game;

namespace OpenKogama.Plugins;

public static class PluginHost
{
    const byte ChatText = 5;

    static readonly PluginServer Server = new();

    public static void LoadAll(string folder)
    {
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

    public static string? Chat(Player player, string text) => Server.OnChat(new PluginPlayer(player), text);

    public static bool Chat(Player player, Dictionary<object, object?> data)
    {
        object? key = data.Keys.FirstOrDefault(key => Convert.ToInt32(key) == ChatText);
        if (key is null || data[key] is not string text) return true;

        if (Chat(player, text) is not { } result) return false;
        data[key] = result;
        return true;
    }

    sealed class PluginServer : IServer
    {
        public event Action<ChatMessage>? Chat;

        public void Log(string text) => Console.WriteLine($"plugin: {text}");

        public string? OnChat(IPlayer player, string text)
        {
            var message = new ChatMessage(player, text);
            foreach (Action<ChatMessage> handler in Chat?.GetInvocationList().Cast<Action<ChatMessage>>() ?? [])
            {
                try
                {
                    handler(message);
                }
                catch (Exception error)
                {
                    Console.WriteLine($"plugin chat handler failed: {error}");
                }
            }
            return message.Cancel ? null : message.Text;
        }
    }
}
