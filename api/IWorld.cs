namespace OpenKogama.Api;

public interface IWorld : IWorldBuilder
{
    int Id { get; }
    string Name { get; }
    bool Play { get; }
    IReadOnlyList<IPlayer> Players { get; }
    IReadOnlyList<INpc> Npcs { get; }

    INpc SpawnNpc(string name, System.Numerics.Vector3 position, float yaw = 0, IPlayer? skinOf = null);
    INpc SpawnNpc(string name, System.Numerics.Vector3 position, float yaw, AvatarSkin? skin, float size = 1f, int level = 1);

    bool LineOfSight(System.Numerics.Vector3 from, System.Numerics.Vector3 to);
    bool? Toggled(int id);
    bool SetToggled(int id, bool on);
    bool Press(int id);

    void Broadcast(string text, string? color = null);
    void Notify(string text, bool brief = false);
    void Announce(string text, string? icon = null);
}
