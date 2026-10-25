using System.Numerics;

namespace OpenKogama.Physics;

sealed class ImpactState
{
    const float MaxAccelerationBeforeDamage = 55f;
    const float DamageMultiplier = 4f;

    readonly List<ControllerHit> _hits = [];
    Vector3 _previousChange;
    bool _collidedBefore;
    float _softnessBefore = 1f;
    int _suspended = 1;

    public void Hit(ControllerHit hit)
    {
        if (!hit.TestWithoutMoving) _hits.Add(hit);
    }

    public void Suspend()
    {
        _suspended = 1;
        _previousChange = Vector3.Zero;
        _collidedBefore = false;
    }

    public void Reset() => _previousChange = Vector3.Zero;

    public float Update(Vector3 velocity, Vector3 previous, ModifierPackages modifiers)
    {
        Vector3 change = (velocity - previous) * AvatarMotor.FixedDeltaTime;
        if (_suspended > 0)
        {
            _suspended--;
            _hits.Clear();
            return 0f;
        }

        float damage = 0f;
        if (_collidedBefore)
        {
            float magnitude = (_previousChange + change).Length() * _softnessBefore;
            if (magnitude > MaxAccelerationBeforeDamage * AvatarMotor.FixedDeltaTime) damage = magnitude * DamageMultiplier / AvatarMotor.FixedDeltaTime;
            _previousChange = Vector3.Zero;
        }
        else
        {
            _previousChange = change;
        }

        _collidedBefore = _hits.Count > 0;
        _softnessBefore = -1f;
        if (_collidedBefore)
        {
            float softness = 0f;
            foreach (ControllerHit hit in _hits) softness += modifiers.Apply(ModifierEffect.Softness, MaterialPhysics.For(hit.Hit.Material).Softness);
            _softnessBefore = softness / _hits.Count;
        }
        _hits.Clear();
        return damage;
    }
}
