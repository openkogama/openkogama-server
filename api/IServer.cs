namespace OpenKogama.Api;

public interface IServer
{
    event Action<ChatMessage>? Chat;
    event Action<IPlayer>? PlayerJoined;
    event Action<IPlayer>? PlayerLeft;
    event Action? Tick;
    event Action<float>? Update;
    event Action<IPlayer, IPlayer>? PlayerKilled;
    event Action<IPlayer, IPlayer>? PlayerHit;
    event Action<IPlayer, INpc>? PlayerKilledByNpc;
    event Action<IPlayer, INpc, float>? PlayerDamagedByNpc;
    event Action<IPlayer, IWorldObject>? ItemCollected;
    event Action<IWorld>? RoundStarted;
    event Action<IWorld, IWorldObject, IPlayer?>? ObjectAdded;
    event Action<IWorld, int, IPlayer?>? ObjectRemoved;
    event Action<INpc, IPlayer?, float>? NpcDamaged;
    event Action<INpc, IPlayer?>? NpcKilled;
    event Action<IPlayer, Gesture>? PlayerGestured;
    event Action<IPlayer, AvatarAnimation>? PlayerAnimated;
    event Action<IPlayer, AvatarItem>? PlayerSwitchedItem;
    event Action<IPlayer, AvatarItem>? PlayerPickedUp;
    event Action<IPlayer, IWorldObject, bool>? PlayerTriggered;
    event Action<CubeChanges>? CubesChanged;
    event Action<ThemeChange>? ThemeChanged;
    [Obsolete("Use PlayerGestured")]
    event Action<IPlayer, string>? Gesture;
    [Obsolete("Use PlayerAnimated")]
    event Action<IPlayer, string>? Animation;
    ITemplates Templates { get; }
    IAssets Assets { get; }

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
