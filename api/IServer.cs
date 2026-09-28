namespace OpenKogama.Api;

public interface IServer
{
    event Action<ChatMessage>? Chat;

    void Log(string text);
}
