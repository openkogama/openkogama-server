using System.Numerics;

namespace OpenKogama.Api;

public interface IPlayer
{
    int Actor { get; }
    int ProfileId { get; }
    long? AccountId { get; }
    string Name { get; }
    string ClientVersion { get; }
    IWorld World { get; }
    int Xp { get; }
    int Gold { get; }
    AvatarSkin Skin { get; }
    Vector3? Position { get; }
    bool Alive { get; }
    bool IsAdmin { get; }
    Team Team { get; }
    AvatarItem Holding { get; }
    int? SoundEmitter { get; }
    int ReauthsLeft { get; }
    string Language { get; }

    int AddXp(int amount);
    int AddGold(int amount);
    void Message(string text) => Message(text, null);
    void Message(string text, string? color = null);
    bool SendScriptMessage(string name, string data);
    void Say(string text);
    void Notify(string text, bool brief = false);
    void Announce(string text, string? icon = null);
    void Damage(float amount, INpc? by = null, System.Numerics.Vector3? impulse = null);
    bool SetTeam(Team team);
    bool Teleport(Vector3 position, TeleportMethod method);
    bool PressLocally(int id);
    ICubeEdit EditTerrainLocally();
    bool SetDataLocally(int id, string key, object value) => SetDataLocally(id, new Dictionary<string, object> { [key] = value });
    bool SetDataLocally(int id, IReadOnlyDictionary<string, object> values);
    bool Equip(AvatarItem item);
    bool Equip(AvatarItem item, Material material);
    bool GiveLightningSpeed();
    bool EnterVehicle(IWorldObject vehicle);
    bool BecomeColossus(IWorldObject trigger);
    bool Reauth(ReauthOptions options);
    bool MoveToWorld(int world, WorldMode mode = WorldMode.Play);
    void Kick();
}
