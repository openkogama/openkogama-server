using System.Text;

namespace OpenKogama.Hosting;

public sealed class LogFile : TextWriter
{
    static readonly TimeSpan Kept = TimeSpan.FromDays(30);

    readonly TextWriter _console;
    readonly string _folder;
    StreamWriter _file;
    DateTime _day;

    LogFile(TextWriter console, string folder)
    {
        _console = console;
        _folder = folder;
        _file = Open();
    }

    public override Encoding Encoding => Encoding.UTF8;

    public static void Start(string folder)
    {
        Directory.CreateDirectory(folder);
        Console.SetOut(Synchronized(new LogFile(Console.Out, folder)));
    }

    StreamWriter Open()
    {
        DateTime now = DateTime.Now;
        _day = now.Date;
        DateTime before = DateTime.UtcNow - Kept;
        foreach (string old in Directory.EnumerateFiles(_folder, "server-*.log"))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(old) < before) File.Delete(old);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
            }
        }
        string path = Path.Combine(_folder, $"server-{now:yyyyMMdd-HHmmss}.log");
        return new StreamWriter(path, append: false, Encoding.UTF8) { AutoFlush = true };
    }

    StreamWriter Today()
    {
        if (DateTime.Now.Date != _day)
        {
            _file.Dispose();
            _file = Open();
        }
        return _file;
    }

    public override void Write(char value)
    {
        _console.Write(value);
        Today().Write(value);
    }

    public override void Write(string? value)
    {
        _console.Write(value);
        Today().Write(value);
    }

    public override void WriteLine(string? value)
    {
        string line = $"{DateTime.Now:HH:mm:ss.fff} {value}";
        _console.WriteLine(line);
        Today().WriteLine(line);
    }
}
