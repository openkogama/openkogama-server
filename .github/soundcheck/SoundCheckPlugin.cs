using System.Numerics;
using OpenKogama.Api;

namespace SoundCheck;

// Worlds for the sound check: a Sound Emitter on every spawn point plays the test tone of one format.
public sealed class SoundCheckPlugin : IPlugin
{
    static readonly string[] Formats = ["wav", "ogg", "mp3"];

    public void Load(IServer server)
    {
        foreach (string format in Formats)
            server.Templates.Add("soundcheck-" + format, "Sound Check " + format, world => Build(server, world, "tone-" + format), "city");
    }

    static void Build(IServer server, IWorldBuilder world, string name)
    {
        ISound sound = server.Assets.Sounds.All.FirstOrDefault(sound => sound.Name == name)
            ?? throw new InvalidOperationException($"no sound called {name}");
        List<Vector3> spawns = [.. world.Objects.Where(obj => obj.Type.StartsWith("SpawnPoint")).Select(obj => obj.Position)];
        if (spawns.Count == 0) spawns.Add(Vector3.Zero);
        foreach (Vector3 spawn in spawns)
        {
            IWorldObject emitter = world.Add("SoundEmitter", spawn + new Vector3(0, 1, 0), new Dictionary<string, object>
            {
                ["range"] = 2,
                ["pitch"] = 1f,
                ["mute"] = false,
                ["loop"] = true,
                ["volume"] = 1f,
            });
            world.SetSound(emitter.Id, sound);
            server.Log($"soundcheck: emitter {emitter.Id} at {spawn} plays {sound.Name}");
        }
    }
}
