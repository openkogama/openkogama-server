namespace OpenKogama.Api;

public interface IPlayer
{
    int Actor { get; }
    int ProfileId { get; }
    string Name { get; }
    string ClientVersion { get; }
    IWorld World { get; }
    int Xp { get; }

    int AddXp(int amount);
    void Message(string text, string? color = null);
    void Say(string text);
    void Kick();
}
