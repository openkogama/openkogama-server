using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using OpenKogama.Handlers.Legacy;
using OpenKogama.Photon;
using OpenKogama.World;

namespace OpenKogama.Game;

public static partial class AntiCheat
{
    const int MaxText = 256;
    const int MaxMessages = 8;
    const long MessageWindowMs = 5000;
    const int AvatarKilled = 0;
    const byte Victim = 0;
    const byte Killer = 1;
    const byte Reason = 2;
    const string TextKey = "text";

    static readonly Regex Markup = new("(?<kept></?[bi]>|<color=#?[0-9a-z]{1,8}>|</color>)|<[^<>]*>|[<>]", RegexOptions.IgnoreCase);
    static readonly Regex Tag = new("<[^<>]*>");
    static readonly Regex Crasher = new(@"^<\s*/?\s*(quad|material)\b|^<\s*size\s*=\s*[""']?-|(?<![#\w])\d{5,}", RegexOptions.IgnoreCase);
    static readonly ConditionalWeakTable<Player, Queue<long>> Sent = new();

    public static bool Crash { get; set; } = true;
    public static bool Spam { get; set; } = true;
    public static bool Spoof { get; set; } = true;

    public static string? Text(string text)
    {
        if (!Crash) return text;
        string clean = string.Concat(text.EnumerateRunes().Where(rune => !Rune.IsControl(rune)).Take(MaxText));
        clean = Markup.Replace(clean, tag => tag.Groups["kept"].Success ? tag.Value : "").Trim();
        return clean.Length > 0 ? clean : null;
    }

    public static string Name(string name) => Crash ? name.Replace("<", "").Replace(">", "") : name;

    public static string TextObject(string text)
    {
        if (!Crash) return text;
        for (string last = ""; last != text;)
            text = Tag.Replace(last = text, tag => Crasher.IsMatch(tag.Value) ? "" : tag.Value);
        return text;
    }

    public static WorldObject TextObject(WorldObject obj)
    {
        if (obj.Type != WorldObjectType.TextMsg || obj.Data.Find(pair => pair.Key == TextKey).Value is not string text
            || TextObject(text) is var clean && clean == text)
            return obj;
        return new WorldObject
        {
            Id = obj.Id,
            ParentId = obj.ParentId,
            ItemId = obj.ItemId,
            Type = obj.Type,
            Position = obj.Position,
            Rotation = obj.Rotation,
            Scale = obj.Scale,
            Data = [.. obj.Data.Select(pair => pair.Key == TextKey ? (pair.Key, pair.Type, (object)clean) : pair)],
            Owner = obj.Owner,
            PreviewOwner = obj.PreviewOwner,
            Runtime = obj.Runtime,
            Transient = obj.Transient,
        };
    }

    public static void TextObject(WorldObject? target, object? data)
    {
        if (target?.Type != WorldObjectType.TextMsg) return;
        Dictionary<object, object?>? entries = data switch
        {
            PhotonDictionary dictionary => dictionary.Entries,
            Dictionary<object, object?> table => table,
            _ => null,
        };
        if (entries?.GetValueOrDefault(TextKey) is string text) entries[TextKey] = TextObject(text);
    }

    public static string? Chat(Player sender, string? text) =>
        text is not null && !Floods(sender) ? Text(text) : null;

    public static Dictionary<object, object?>? Chat(Player sender, Dictionary<object, object?> data)
    {
        object? key = data.Keys.FirstOrDefault(key => Convert.ToInt32(key) == SendChatMsg.Text);
        if (key is null || Chat(sender, data[key] as string) is not { } text) return null;
        if (Spoof) return new() { [SendChatMsg.Sender] = sender.Actor, [SendChatMsg.Text] = text };
        data[key] = text;
        return data;
    }

    public static Dictionary<object, object?>? GameMessage(Player sender, int type, Dictionary<object, object?> data)
    {
        if (Crash && type != AvatarKilled || Floods(sender)) return null;
        if (!Spoof || type != AvatarKilled) return data;
        if (Actor(data, Victim) != sender.Actor || Actor(data, Killer) is not int killer) return null;
        var message = new Dictionary<object, object?> { [Victim] = sender.Actor, [Killer] = killer };
        if (Value(data, Reason) is byte or short or int) message[Reason] = Value(data, Reason);
        return message;
    }

    static bool Floods(Player player) => Spam && Floods(Sent.GetOrCreateValue(player), MaxMessages, MessageWindowMs);

    static bool Floods(Queue<long> sent, int max, long windowMs)
    {
        long now = Environment.TickCount64;
        lock (sent)
        {
            while (sent.Count > 0 && now - sent.Peek() >= windowMs) sent.Dequeue();
            if (sent.Count >= max) return true;
            sent.Enqueue(now);
            return false;
        }
    }

    static object? Value(Dictionary<object, object?> data, byte key) =>
        data.FirstOrDefault(entry => Convert.ToInt32(entry.Key) == key).Value;

    static int? Actor(Dictionary<object, object?> data, byte key) =>
        Value(data, key) is byte or short or int ? Convert.ToInt32(Value(data, key)) : null;
}
