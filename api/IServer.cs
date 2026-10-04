namespace OpenKogama.Api;

public interface IServer
{
    event Action<ChatMessage>? Chat;
    event Action<IPlayer>? PlayerJoined;
    event Action<IPlayer>? PlayerLeft;
    event Action? Tick;
    event Action<float>? Update;
    event Action<IPlayer, IPlayer>? PlayerKilled;
    event Action<IPlayer, IWorldObject>? ItemCollected;
    event Action<IWorld>? RoundStarted;
    event Action<IWorld, IWorldObject, IPlayer?>? ObjectAdded;
    event Action<IWorld, int, IPlayer?>? ObjectRemoved;
    event Action<INpc, IPlayer?, float>? NpcDamaged;
    event Action<INpc, IPlayer?>? NpcKilled;
    ITemplates Templates { get; }

    IReadOnlyList<IPlayer> Players { get; }
    IReadOnlyList<IWorld> Worlds { get; }

    void Broadcast(string text, string? color = null);
    void Notify(string text, bool brief = false);
    void Announce(string text, string? icon = null);
    void AddIcon(string name, byte[] png);
    void Log(string text);
    void AddCommand(string name, Action<IPlayer, string[]> handler);
    IPluginStorage Storage(string name);
}
