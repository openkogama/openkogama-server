using System.Text.Json;
using OpenKogama.Api;

namespace Welcome;

public sealed class WelcomePlugin : IPlugin
{
    const string ConfigPath = "plugins/welcome.json";
    const string DefaultMessage = "Welcome to {world}, {player}!";
    const string DefaultColor = "yellow";

    public void Load(IServer server)
    {
        Config config = ReadConfig();
        server.PlayerJoined += player =>
        {
            if (player.World.Id == 0) return;
            player.Message(config.Message.Replace("{player}", player.Name).Replace("{world}", player.World.Name), config.Color);
        };
    }

    static Config ReadConfig()
    {
        if (!File.Exists(ConfigPath))
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(new Config(DefaultMessage, DefaultColor), new JsonSerializerOptions { WriteIndented = true }));
        Config? config = JsonSerializer.Deserialize<Config>(File.ReadAllText(ConfigPath));
        return new Config(config?.Message ?? DefaultMessage, config?.Color);
    }

    sealed record Config(string Message, string? Color);
}
