namespace OpenKogama.Storage;

public static class Revision
{
    static int _value;

    public static int Value => Volatile.Read(ref _value);

    public static void Bump() => Interlocked.Increment(ref _value);
}
