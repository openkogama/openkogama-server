namespace OpenKogama.Api;

public sealed class ChatMessage(IPlayer player, string text)
{
    public IPlayer Player { get; } = player;
    public string Text { get; set; } = text;
    public bool Cancel { get; set; }
}
