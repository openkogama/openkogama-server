using OpenKogama.Api;

namespace Online;

public sealed class OnlinePlugin : IPlugin
{
    const string Client = "client/";

    public void Load(IServer server)
    {
        server.Scripts.Add("online", ClientFiles());
        server.ScriptMessage += (player, name, data) =>
        {
            if (name == "online:ping") player.SendScriptMessage("online:pong", data);
        };
        server.Tick += () =>
        {
            string names = string.Join(", ", server.Players.Select(player => player.Name));
            foreach (IPlayer player in server.Players) player.SendScriptMessage("online:players", names);
        };
    }

    static Dictionary<string, byte[]> ClientFiles()
    {
        Dictionary<string, byte[]> files = [];
        foreach (string name in typeof(OnlinePlugin).Assembly.GetManifestResourceNames().Where(name => name.StartsWith(Client)))
        {
            using Stream stream = typeof(OnlinePlugin).Assembly.GetManifestResourceStream(name)!;
            using MemoryStream copy = new();
            stream.CopyTo(copy);
            files[name[Client.Length..]] = copy.ToArray();
        }
        return files;
    }
}
