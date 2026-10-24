using System.Numerics;

namespace OpenKogama.Api;

public interface INpc
{
    int Id { get; }
    int ObjectId { get; }
    string Name { get; }
    IWorld World { get; }
    AvatarSkin Skin { get; }
    Team? Team { get; }
    Vector3 Position { get; }
    Vector3 EyePosition { get; }
    float Yaw { get; }
    string Animation { get; }
    AvatarAnimation AnimationState => Enum.TryParse(Animation, ignoreCase: true, out AvatarAnimation state) ? state : AvatarAnimation.Unknown;
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
    void Shoot(Vector3 direction, float holdSeconds);
    void Aim(Vector3 direction);
    bool Hold(IWorldObject weapon);
    bool BecomeColossus(IWorldObject trigger);
    bool BecomeColossus(ColossusSize size);
    void PlayAnimation(string? state);
    void PlayAnimation(AvatarAnimation state) => PlayAnimation(state == AvatarAnimation.Unknown ? null : state.ToString());
    void Damage(float amount, IPlayer? by = null);
    void Damage(float amount, IPlayer? by, Vector3 impulse);
    void Damage(float amount, INpc by);
    void Damage(float amount, INpc by, Vector3 impulse);
    bool SetTeam(Team? team);
    void Say(string text);
    void Respawn(Vector3 position);
    void Remove();
}
