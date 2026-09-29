namespace OpenKogama.Api;

public interface IServer
{
    event Action<ChatMessage>? Chat;
    event Action<IPlayer>? PlayerJoined;
    event Action<IPlayer>? PlayerLeft;
    event Action? Tick;

    IReadOnlyList<IPlayer> Players { get; }
    IReadOnlyList<IWorld> Worlds { get; }

    void Broadcast(string text, string? color = null);
    void Log(string text);
}
