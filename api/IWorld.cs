namespace OpenKogama.Api;

public interface IWorld
{
    int Id { get; }
    string Name { get; }
    bool Play { get; }
    IReadOnlyList<IPlayer> Players { get; }

    void Broadcast(string text, string? color = null);
}
