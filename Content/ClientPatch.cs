using System.Text;
using OpenKogama.Mods.Patches;

namespace OpenKogama.Content;

public static class ClientPatch
{
    const int NothingToChange = 3;

    public static int Run(string[] args)
    {
        if (args is not [string input, string content, string output])
        {
            Console.WriteLine("usage: client patch <data.unity3d or sharedassets0.assets> <content.json, or - for standard input> <output>");
            return 2;
        }
        try
        {
            ServerContent served = ServerContent.FromJson(content == "-" ? StandardInput() : File.ReadAllText(content));
            byte[] patched = ContentPatch.Apply(File.ReadAllBytes(input), served, out List<string> applied);
            if (served.Sounds.Count == 0 && applied.Count == 0)
            {
                Console.WriteLine("client patch: nothing to change");
                return NothingToChange;
            }
            File.WriteAllBytes(output + ".tmp", patched);
            File.Move(output + ".tmp", output, overwrite: true);
            Console.WriteLine($"client patch: {served.Sounds.Count} sounds added, themes {(applied.Count > 0 ? string.Join(", ", applied) : "unchanged")}");
            return 0;
        }
        catch (Exception error)
        {
            Console.WriteLine($"client patch: {error.Message}");
            return 1;
        }
    }

    static string StandardInput()
    {
        using Stream input = Console.OpenStandardInput();
        using var buffer = new MemoryStream();
        input.CopyTo(buffer);
        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
