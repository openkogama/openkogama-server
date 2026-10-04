using OpenKogama.Api;

namespace Deadzone;

public sealed class DeadzonePlugin : IPlugin
{
    public void Load(IServer server)
    {
        using Stream map = typeof(DeadzonePlugin).Assembly.GetManifestResourceStream("deadzone.kgmap")
            ?? throw new InvalidOperationException("deadzone.kgmap is missing from the plugin");
        using var bytes = new MemoryStream();
        map.CopyTo(bytes);

        var effects = new Effects(server.Storage("deadzone"));
        AvatarSkin zombieSkin = AvatarSkin.FromJson(Resource("zombie-skin.json"));
        var npcs = new NpcTest(zombieSkin);
        NightPlan plan = NightPlan.Parse(Resource("nights.json"));
        Dictionary<string, AvatarSkin> skins = plan.Skins.ToDictionary(name => name, name => AvatarSkin.FromJson(Resource($"{name}.kgavatar")));
        var zombies = new Zombies(zombieSkin, skins, plan);
        effects.PhaseStarted = zombies.PhaseStarted;
        effects.NightName = day => plan.Night(day).Name;
        effects.LastNight = plan.Count;
        zombies.NightCleared = effects.EndNight;

        server.Templates.Add("deadzone", "Deadzone", bytes.ToArray(), Prepare);

        server.Tick += () =>
        {
            foreach (IWorld world in server.Worlds)
                effects.Update(world);
        };
        server.Update += _ =>
        {
            npcs.Update();
            zombies.Update(server.Worlds);
        };
        server.NpcKilled += (npc, _) =>
        {
            if (!zombies.Killed(npc)) npcs.Killed(npc);
        };

        server.AddCommand("npc", npcs.Command);

        server.AddIcon("test", Bytes("test-icon.png"));
        server.AddCommand("announce", (player, arguments) =>
            player.Announce(arguments.Length > 0 ? string.Join(' ', arguments) : "Night 3 - survive until dawn!", "test"));

        server.AddCommand("pos", (player, _) => player.Message(player.Position is System.Numerics.Vector3 position
            ? $"You are at {position.X:0.0}, {position.Y:0.0}, {position.Z:0.0}"
            : "Move a bit first"));

        server.AddCommand("deadzone", (player, _) => player.Message(effects.Toggle(player.World) switch
        {
            true => "Deadzone effects on",
            false => "Deadzone effects off",
            null => "Deadzone effects are not available here",
        }));

        server.AddCommand("time", (player, arguments) =>
        {
            bool? night = arguments.FirstOrDefault()?.ToLowerInvariant() switch
            {
                "day" => false,
                "night" => true,
                _ => null,
            };
            if (night is bool target)
            {
                bool done = arguments.ElementAtOrDefault(1) is string text && int.TryParse(text, out int number)
                    ? effects.Set(player.World, new DayNightCycle.Phase(target ? Math.Clamp(number, 1, plan.Count) : Math.Max(1, number), target))
                    : effects.Jump(player.World, target);
                if (!done) player.Message("Turn the effects on first with /deadzone");
                return;
            }
            player.Message(effects.Current(player.World) is { } phase
                ? $"{(phase.Night ? "Night" : "Day")} {phase.Day}, use /time night 7 or /time day 3 to go there, /time day or /time night to skip"
                : "Turn the effects on first with /deadzone");
        });
    }

    static byte[] Bytes(string name)
    {
        using Stream stream = typeof(DeadzonePlugin).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"{name} is missing from the plugin");
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return bytes.ToArray();
    }

    static string Resource(string name)
    {
        using Stream stream = typeof(DeadzonePlugin).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"{name} is missing from the plugin");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    static void Prepare(IWorldBuilder world)
    {
        foreach (IWorldObject obj in world.Objects)
        {
            if (obj.Type == "CameraSettings")
                world.SetData(obj.Id, "distanceToAvatar", Effects.CameraDistance);
            if (obj.Type == "Skybox")
                world.SetData(obj.Id, new Dictionary<string, object>(DayNightCycle.Start) { [Effects.Marker] = true });
        }
    }
}
