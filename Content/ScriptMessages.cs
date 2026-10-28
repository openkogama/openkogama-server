using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;

namespace OpenKogama.Content;

public static partial class ScriptMessages
{
    const int DataLimit = 64 * 1024;
    const int QueueLimit = 256;
    const int RateLimit = 60;
    const long IdleMs = 5 * 60 * 1000;
    static readonly TimeSpan Wait = TimeSpan.FromSeconds(20);
    static readonly ConcurrentDictionary<string, Mailbox> Mailboxes = new();
    static readonly ConcurrentQueue<(string Token, Message Message)> Arrived = new();

    public sealed record Message(string Name, string Data);

    sealed class Mailbox
    {
        public readonly ConcurrentQueue<Message> Pending = new();
        public readonly SemaphoreSlim Ready = new(0);
        public long Seen = Environment.TickCount64;
        public long Window;
        public int Count;
    }

    [GeneratedRegex(@"^[A-Za-z0-9_.:-]{1,64}$")]
    private static partial Regex NamePattern();

    public static bool Send(string? token, string name, string data)
    {
        if (token is null || !NamePattern().IsMatch(name) || Encoding.UTF8.GetByteCount(data) > DataLimit) return false;
        Mailbox mailbox = Mailboxes.GetOrAdd(token, _ => new Mailbox());
        if (mailbox.Pending.Count >= QueueLimit) return false;
        mailbox.Pending.Enqueue(new Message(name, data));
        mailbox.Ready.Release();
        return true;
    }

    public static async Task<byte[]?> Poll(string? token)
    {
        if (token is null) return null;
        Mailbox mailbox = Mailboxes.GetOrAdd(token, _ => new Mailbox());
        mailbox.Seen = Environment.TickCount64;
        if (mailbox.Pending.IsEmpty) await mailbox.Ready.WaitAsync(Wait);
        while (mailbox.Ready.CurrentCount > 0) mailbox.Ready.Wait(0);
        List<Message> messages = [];
        while (mailbox.Pending.TryDequeue(out Message? message)) messages.Add(message);
        mailbox.Seen = Environment.TickCount64;
        return Encode(messages);
    }

    public static bool Receive(string? token, byte[] body)
    {
        if (token is null) return false;
        Mailbox mailbox = Mailboxes.GetOrAdd(token, _ => new Mailbox());
        long now = Environment.TickCount64;
        foreach (Message message in Decode(body))
        {
            if (now - mailbox.Window >= 1000)
            {
                mailbox.Window = now;
                mailbox.Count = 0;
            }
            if (++mailbox.Count <= RateLimit) Arrived.Enqueue((token, message));
        }
        return true;
    }

    public static IEnumerable<(string Token, Message Message)> Take()
    {
        while (Arrived.TryDequeue(out (string Token, Message Message) item)) yield return item;
    }

    public static void Sweep()
    {
        long now = Environment.TickCount64;
        foreach ((string token, Mailbox mailbox) in Mailboxes)
            if (now - mailbox.Seen > IdleMs) Mailboxes.TryRemove(token, out _);
    }

    static byte[] Encode(IEnumerable<Message> messages)
    {
        using MemoryStream stream = new();
        foreach (Message message in messages)
        {
            byte[] data = Encoding.UTF8.GetBytes(message.Data);
            stream.Write(Encoding.ASCII.GetBytes($"{message.Name} {data.Length}\n"));
            stream.Write(data);
            stream.WriteByte((byte)'\n');
        }
        return stream.ToArray();
    }

    static IEnumerable<Message> Decode(byte[] body)
    {
        int at = 0;
        while (at < body.Length)
        {
            int end = Array.IndexOf(body, (byte)'\n', at);
            if (end < 0) yield break;
            string[] header = Encoding.ASCII.GetString(body, at, end - at).Split(' ');
            if (header.Length != 2 || !int.TryParse(header[1], out int length) || length < 0 || length > DataLimit || end + 1 + length > body.Length) yield break;
            string data = Encoding.UTF8.GetString(body, end + 1, length);
            at = end + 1 + length + 1;
            if (NamePattern().IsMatch(header[0])) yield return new Message(header[0], data);
        }
    }
}
