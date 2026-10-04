using System.Numerics;

namespace OpenKogama.Physics;

readonly record struct ControllerHit(VoxelHit Hit, Vector3 MoveDirection, Vector3 SlopeNormal, Vector3 ImpactVelocity, bool TestWithoutMoving);

sealed class CharacterController(CollisionWorld world, float radius, float height, Vector3 centerBase)
{
    const float VeryCloseDistance = 0.005f;
    const int MaxRecursions = 7;
    const float CollisionMaxAngle = 89.95f;
    const float OffsetBase = 0.1f;

    readonly Vector3 _radiusBase = new(radius, height / 2f, radius);
    Vector3 _radius = new(radius, height / 2f, radius);
    readonly Vector3 _centerBase = centerBase;
    Vector3 _center = centerBase;
    float _offset = OffsetBase;
    int _recursion;

    public CollisionWorld World { get; set; } = world;
    public Vector3 Position { get; set; }
    public Vector3 Velocity { get; private set; }
    public Vector3 Center => _center;
    public Vector3 EllipsoidRadius => _radius;
    public Action<ControllerHit>? OnHit { get; set; }

    public void SetScale(float scale)
    {
        _center = _centerBase * scale;
        _radius = _radiusBase * scale;
        _offset = OffsetBase * scale;
    }

    public void Move(Vector3 motion)
    {
        Vector3 result = CollideAndSlide(motion, Position + _center);
        Position = result - _center;
    }

    public bool TestWithoutSliding(float distance, Vector3 direction, Vector3 motion, out ControllerHit hit)
    {
        hit = default;
        Vector3 radius = new(_radius.X, _radius.Y - _offset, _radius.Z);
        direction = UnityMath.Normalized(direction);
        Vector3 origin = Position + _center;
        if (World.Cast(origin, direction, radius, distance + _offset) is not VoxelHit found) return false;
        hit = CreateHit(found, origin, radius, motion, testWithoutMoving: true);
        OnHit?.Invoke(hit);
        return true;
    }

    public Vector3 GradientDirection(VoxelHit hit)
    {
        Vector3 bottom = Position + _center + Vector3.UnitY * -hit.Distance;
        Vector3 point = hit.Point / _radius;
        Vector3 scaled = bottom / _radius;
        Vector3 normal = UnityMath.Normalized(scaled - point);
        if (normal.Y == 0f) return -Vector3.UnitY;
        if ((normal - Vector3.UnitY).LengthSquared() < 9.99999944E-11f) return Vector3.Zero;
        Vector3 side = Vector3.Cross(Vector3.UnitY, normal);
        Vector3 along = Vector3.Cross(normal, side);
        return -UnityMath.Normalized(along * _radius);
    }

    Vector3 CollideAndSlide(Vector3 motion, Vector3 position)
    {
        if (motion.LengthSquared() == 0f)
        {
            Velocity = Vector3.Zero;
            return position;
        }

        bool valid = true;
        Vector3 ePosition = position / _radius;
        Vector3 eVelocity = motion / _radius;
        Vector3 start = ePosition;
        _recursion = 0;
        Vector3 moved = CollideWithWorld(ref ePosition, ref eVelocity, ref valid);
        Vector3 worldMoved = moved * _radius;
        bool overlapping = World.Overlap(worldMoved, _radius);
        Vector3 offset = Vector3.Zero;
        if (overlapping && FreeOffset(worldMoved, motion, ref offset)) overlapping = false;
        if (valid && !overlapping) start = moved;

        Vector3 result = start * _radius + offset;
        position += offset;
        Velocity = result - position;
        return result;
    }

    Vector3 CollideWithWorld(ref Vector3 ePosition, ref Vector3 eVelocity, ref bool valid)
    {
        if (_recursion > MaxRecursions)
        {
            (Vector3 settled, bool ok) = NoCollision(ePosition, eVelocity);
            if (ok) return settled;
            valid = false;
            return ePosition;
        }

        Vector3 origin = ePosition * _radius;
        Vector3 velocity = eVelocity * _radius;
        if (World.Cast(origin, UnityMath.Normalized(velocity), _radius, velocity.Length()) is not VoxelHit hit)
        {
            (Vector3 settled, bool ok) = NoCollision(ePosition, eVelocity);
            return ok ? settled : ePosition;
        }

        float distance = ToEllipsoid(hit.Distance, velocity);
        Vector3 ePoint = hit.Point / _radius;
        float angle = CollisionAngle(ePosition, eVelocity, distance, ePoint);
        if (angle > CollisionMaxAngle && distance != 0f)
        {
            Vector3 away = MoveAway(ePosition, eVelocity, distance, ePoint) * eVelocity.Length();
            _recursion++;
            return CollideWithWorld(ref ePosition, ref away, ref valid);
        }

        Vector3 destination = ePosition + eVelocity;
        Vector3 direction = UnityMath.Normalized(eVelocity);
        float back = MoveBackDistance(ePosition, eVelocity, distance, ePoint);
        if (distance - back >= 0f && back > 0f)
        {
            Vector3 basePoint = ePosition + direction * (distance - back);
            ePoint -= back * direction;
            Vector3 next = NextVelocity(ePoint, basePoint, destination);
            OnHit?.Invoke(CreateHit(hit, origin, _radius, velocity, testWithoutMoving: false));
            _recursion++;
            return CollideWithWorld(ref basePoint, ref next, ref valid);
        }

        Vector3 escape = MoveAway(ePosition, eVelocity, distance, ePoint) * eVelocity.Length();
        _recursion++;
        return CollideWithWorld(ref ePosition, ref escape, ref valid);
    }

    (Vector3 Position, bool Valid) NoCollision(Vector3 ePosition, Vector3 eVelocity)
    {
        Vector3 origin = ePosition * _radius;
        Vector3 velocity = eVelocity * _radius;
        (Vector3 Position, bool Valid) result = (ePosition + eVelocity, true);
        float probe = ToWorld(VeryCloseDistance, -Vector3.UnitY);
        if (World.Cast(origin + velocity, -Vector3.UnitY, _radius, probe) is VoxelHit below)
        {
            float distance = ToEllipsoid(below.Distance, -Vector3.UnitY);
            Vector3 lifted = ePosition + eVelocity + (VeryCloseDistance - distance) * Vector3.UnitY;
            bool blocked = World.Cast(origin + velocity, Vector3.UnitY, _radius, probe) is not null;
            result = (lifted, !blocked);
        }
        return result;
    }

    bool FreeOffset(Vector3 position, Vector3 direction, ref Vector3 offset)
    {
        direction = UnityMath.Normalized(direction);
        float root2 = MathF.Sqrt(2f);
        float vertical = Vector3.Dot(direction, Vector3.UnitY);
        Vector3 reference = vertical > 0.99f || vertical < -0.99f ? Vector3.UnitX : Vector3.UnitY;
        Vector3 first = UnityMath.Normalized(Vector3.Cross(reference, direction));
        Vector3 second = UnityMath.Normalized(Vector3.Cross(first, direction));
        first *= VeryCloseDistance;
        second *= VeryCloseDistance;

        Vector3 diagonal = (first + second) / root2;
        Vector3 crossDiagonal = (first - second) / root2;
        foreach (Vector3 candidate in (ReadOnlySpan<Vector3>)[first, -first, second, -second, diagonal, -diagonal, crossDiagonal, -crossDiagonal])
        {
            if (World.Overlap(position + candidate, _radius)) continue;
            offset = candidate;
            return true;
        }
        offset = Vector3.Zero;
        return false;
    }

    static Vector3 NextVelocity(Vector3 ePoint, Vector3 basePoint, Vector3 destination)
    {
        Vector3 normal = UnityMath.Normalized(basePoint - ePoint);
        double signed = UnityMath.SignedDistance(normal, ePoint, destination);
        return destination - (float)signed * normal - ePoint;
    }

    static Vector3 MoveAway(Vector3 ePosition, Vector3 eDirection, float distance, Vector3 ePoint)
    {
        eDirection = UnityMath.Normalized(eDirection);
        Vector3 contact = ePosition + eDirection * distance;
        Vector3 away = UnityMath.Normalized(contact - ePoint);
        return UnityMath.Normalized(contact + away * VeryCloseDistance - ePosition);
    }

    static float MoveBackDistance(Vector3 ePosition, Vector3 eDirection, float distance, Vector3 ePoint)
    {
        float angle = CollisionAngle(ePosition, eDirection, distance, ePoint);
        float factor = 1f / MathF.Cos(angle * UnityMath.Deg2Rad);
        return factor - factor * 0.995f;
    }

    static float CollisionAngle(Vector3 ePosition, Vector3 eDirection, float distance, Vector3 ePoint)
    {
        Vector3 direction = UnityMath.Normalized(eDirection);
        Vector3 normal = UnityMath.Normalized(ePoint - (ePosition + direction * distance));
        return UnityMath.Angle(direction, normal);
    }

    float ToEllipsoid(float distance, Vector3 direction) => (UnityMath.Normalized(direction) * distance / _radius).Length();

    float ToWorld(float distance, Vector3 direction) => (UnityMath.Normalized(direction) * distance * _radius).Length();

    ControllerHit CreateHit(VoxelHit hit, Vector3 position, Vector3 radius, Vector3 velocity, bool testWithoutMoving)
    {
        Vector3 direction = UnityMath.Normalized(velocity);
        Vector3 touching = direction * hit.Distance + position;
        Vector3 normal = UnityMath.Normalized(touching - hit.Point);
        Vector3 slope = UnityMath.Normalized(normal / radius / radius);
        return new ControllerHit(hit, direction, slope, velocity / AvatarMotor.FixedDeltaTime, testWithoutMoving);
    }
}
