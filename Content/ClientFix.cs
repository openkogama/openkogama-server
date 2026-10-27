using OpenKogama.Mods.Patches;

namespace OpenKogama.Content;

public static class ClientFix
{
    const int NothingToChange = 3;

    public static int Run(string[] args)
    {
        if (args is not [string input, string output, .. string[] fixes])
        {
            Console.WriteLine($"usage: client fix <Assembly-CSharp.dll or GameAssembly.dll> <output> [{string.Join("] [", Fixes.All)}]");
            return 2;
        }
        try
        {
            List<string> applied = Fixes.Apply(input, output, fixes, line => Console.WriteLine($"client fix: {line}"));
            if (applied.Count == 0)
            {
                Console.WriteLine("client fix: nothing to change");
                return NothingToChange;
            }
            foreach (string fix in applied) Console.WriteLine($"client fix: applied {fix}");
            return 0;
        }
        catch (Exception error)
        {
            Console.WriteLine($"client fix: {error.Message}");
            return 1;
        }
    }
}
