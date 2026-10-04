using System.Numerics;
using OpenKogama.Api;

namespace Sandbox;

public sealed class SandboxPlugin : IPlugin
{
    const byte Sand = 19;
    const byte Grass = 11;
    const byte Water = 6;
    const int IslandRadius = 24;

    public void Load(IServer server)
    {
        IPluginStorage kills = server.Storage("sandbox-kills");

        server.Templates.Add("flat-island", "Flat Island", BuildIsland);

        server.AddCommand("pos", (player, _) =>
            player.Message(player.Position is Vector3 position ? $"You are at {position.X:0}, {position.Y:0}, {position.Z:0}" : "Move a bit first"));

        server.AddCommand("gold", (player, _) => player.Message($"You have {player.Gold} gold"));

        server.AddCommand("block", (player, arguments) =>
        {
            if (player.Position is not Vector3 position) return;
            byte material = arguments.Length > 0 && byte.TryParse(arguments[0], out byte chosen) ? chosen : Sand;
            player.World.Terrain.Set((int)MathF.Floor(position.X), (int)MathF.Floor(position.Y) - 1, (int)MathF.Floor(position.Z), material);
        });

        server.AddCommand("kills", (player, _) => player.Message($"Kills: {kills.Get(player.ProfileId.ToString()) ?? "0"}"));

        server.PlayerKilled += (victim, killer) =>
        {
            string key = killer.ProfileId.ToString();
            int total = int.Parse(kills.Get(key) ?? "0") + 1;
            kills.Set(key, total.ToString());
            killer.Message($"{victim.Name} down, {total} kills total", "red");
        };

        server.ItemCollected += (player, item) => player.Message($"Collected {item.Type}", "yellow");

        server.RoundStarted += world => world.Broadcast("A new round has started!", "green");
    }

    static void BuildIsland(IWorldBuilder world)
    {
        world.Terrain.Clear();
        world.Terrain.Fill(-IslandRadius - 8, 0, -IslandRadius - 8, IslandRadius + 8, 0, IslandRadius + 8, Water);
        for (int x = -IslandRadius; x <= IslandRadius; x++)
            for (int z = -IslandRadius; z <= IslandRadius; z++)
            {
                float distance = MathF.Sqrt(x * x + z * z);
                if (distance > IslandRadius) continue;
                world.Terrain.Set(x, 1, z, distance > IslandRadius - 3 ? Sand : Grass);
            }
        world.Add("PointLight", new Vector3(0, 20, 0), new Dictionary<string, object> { ["range"] = 60f });
    }
}
