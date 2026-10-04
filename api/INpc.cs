using System.Numerics;

namespace OpenKogama.Api;

public interface INpc
{
    int Id { get; }
    int ObjectId { get; }
    string Name { get; }
    Vector3 Position { get; }
    float Yaw { get; }
    string Animation { get; }
    float Speed { get; set; }
    float Size { get; }
    int Level { get; }
    bool OnFire { get; set; }
    string? Holding { get; set; }
    float MaxHealth { get; set; }
    bool Walking { get; }
    bool Grounded { get; }
    float Health { get; }
    bool Alive { get; }
    bool Exists { get; }

    void WalkTo(Vector3 target);
    void Stop();
    void Jump(float holdSeconds = 0.5f);
    void Teleport(Vector3 position, float? yaw = null);
    void Face(float yaw);
    void Shoot(Vector3 direction);
    void PlayAnimation(string? state);
    void Damage(float amount, IPlayer? by = null);
    void Respawn(Vector3 position);
    void Remove();
}
