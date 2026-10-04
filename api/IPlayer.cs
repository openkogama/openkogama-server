using System.Numerics;

namespace OpenKogama.Api;

public interface IPlayer
{
    int Actor { get; }
    int ProfileId { get; }
    string Name { get; }
    string ClientVersion { get; }
    IWorld World { get; }
    int Xp { get; }
    int Gold { get; }
    AvatarSkin Skin { get; }
    Vector3? Position { get; }

    int AddXp(int amount);
    int AddGold(int amount);
    void Message(string text, string? color = null);
    void Say(string text);
    void Notify(string text, bool brief = false);
    void Announce(string text, string? icon = null);
    void Damage(float amount, INpc? by = null, System.Numerics.Vector3? impulse = null);
    void Kick();
}
