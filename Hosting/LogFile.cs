using System.Text;

namespace OpenKogama.Hosting;

public sealed class LogFile(TextWriter console, StreamWriter file) : TextWriter
{
    public override Encoding Encoding => Encoding.UTF8;

    public static void Start(string folder)
    {
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, $"server-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        var file = new StreamWriter(path, append: false, Encoding.UTF8) { AutoFlush = true };
        Console.SetOut(Synchronized(new LogFile(Console.Out, file)));
    }

    public override void Write(char value)
    {
        console.Write(value);
        file.Write(value);
    }

    public override void Write(string? value)
    {
        console.Write(value);
        file.Write(value);
    }

    public override void WriteLine(string? value)
    {
        string line = $"{DateTime.Now:HH:mm:ss.fff} {value}";
        console.WriteLine(line);
        file.WriteLine(line);
    }
}
