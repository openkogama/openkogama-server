namespace OpenKogama.Api;

public interface IPlayer
{
    int ProfileId { get; }
    string Name { get; }

    int AddXp(int amount);
    void Message(string text);
}
