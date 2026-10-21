using OpenKogama.Api;

namespace Sounds;

public sealed class SoundsPlugin : IPlugin
{
    const string Folder = "plugins/data/sounds";
    const string ReplaceFolder = "Replace";
    static readonly string[] Formats = [".mp3", ".ogg", ".wav", ".unity3d"];

    public void Load(IServer server)
    {
        Directory.CreateDirectory(Path.Combine(Folder, ReplaceFolder));
        foreach (string file in Directory.EnumerateFiles(Folder, "*", SearchOption.AllDirectories)
            .Where(file => Formats.Contains(Path.GetExtension(file).ToLowerInvariant())))
        {
            try
            {
                Add(server, file);
            }
            catch (Exception error)
            {
                server.Log($"sounds: {Path.GetFileName(file)} skipped, {error.Message}");
            }
        }
        server.Log($"sounds: {server.Assets.Sounds.All.Count} from {Folder}, {server.Assets.Sounds.Replaced.Count} game sounds replaced");
        server.AddCommand("sound", (player, arguments) => Choose(server, player, arguments));
    }

    static void Add(IServer server, string file)
    {
        string name = Path.GetFileNameWithoutExtension(file);
        byte[] data = File.ReadAllBytes(file);
        string folder = FolderOf(file);
        if (folder.Equals(ReplaceFolder, StringComparison.OrdinalIgnoreCase))
        {
            if (!TryName(name, out GameSound sound)) throw new ArgumentException($"{name} is no game sound, name the file like GunShotgun or HitExplosion");
            server.Assets.Sounds.Replace(sound, data);
        }
        else if (TryName(folder, out SoundGroup group))
            server.Assets.Sounds.Add(name, data, group);
        else
            server.Assets.Sounds.Add(name, data, Path.GetExtension(file), folder);
    }

    static string FolderOf(string file)
    {
        string folder = Path.GetRelativePath(Folder, Path.GetDirectoryName(file)!);
        return folder == "." ? nameof(SoundGroup.MusicLoop) : folder.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
    }

    static bool TryName<T>(string text, out T value) where T : struct, Enum
    {
        string? name = Enum.GetNames<T>().FirstOrDefault(candidate => candidate.Equals(text, StringComparison.OrdinalIgnoreCase));
        value = name is null ? default : Enum.Parse<T>(name);
        return name is not null;
    }

    static void Choose(IServer server, IPlayer player, string[] arguments)
    {
        IReadOnlyList<ISound> sounds = server.Assets.Sounds.All;
        if (sounds.Count == 0)
        {
            player.Message($"No sounds yet, put .mp3, .ogg or .wav files in {Folder}");
            return;
        }
        if (arguments.Length == 0)
        {
            player.Message(string.Join("\n", sounds.GroupBy(sound => sound.Group).OrderBy(group => group.Key)
                .Select(group => $"<b>{group.Key}</b>: {string.Join(", ", group.Select(sound => sound.Name))}")));
            return;
        }

        string wanted = string.Join(' ', arguments);
        List<ISound> matches = [.. sounds.Where(sound => sound.Name.Equals(wanted, StringComparison.OrdinalIgnoreCase))];
        if (matches.Count == 0) matches = [.. sounds.Where(sound => sound.Name.Contains(wanted, StringComparison.OrdinalIgnoreCase))];
        if (matches.Count != 1)
        {
            player.Message(matches.Count == 0 ? $"No sound called {wanted}, type !sound for the list" : $"Which one? {string.Join(", ", matches.Select(sound => sound.Name))}");
            return;
        }

        ISound chosen = matches[0];
        if (player.World.Play)
            player.Message("Change sounds in the editor");
        else if (player.SoundEmitter is not int emitter)
            player.Message($"Pick any sound in a Sound Emitter first, then type !sound {chosen.Name}");
        else
            player.Message(player.World.SetSound(emitter, chosen) ? $"The Sound Emitter plays {chosen.Name} now" : "That Sound Emitter is gone");
    }
}
