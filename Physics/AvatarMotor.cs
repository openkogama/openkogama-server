using System.Numerics;

namespace OpenKogama.Physics;

sealed class AvatarMotor
{
    public const float FixedDeltaTime = 0.02f;
    public const float DefaultWalkSpeed = 8f;

    const float Radius = 0.45f;
    const float Height = 1.9f;
    const float InAirControlFactor = 3f;
    const float LerpTime = 0.4f;
    const float GroundDepth = 0.1f;

    const float ExtraHeight = 4.1f;
    const float BaseHeight = 1f;
    const float SlipperyMin = 0.3f;
    const float SlipperyMax = 0.6f;
    const float RegularButtonDownTimeLimit = 0.2f;
    const float BouncyButtonDownTimeLimit = 3f;
    const float BouncinessThreshold = 0.3f;

    readonly CharacterController _controller;

    Vector3 _velocityPrevFrame;
    float _currentLerp;
    float _time;

    bool _grounded;
    Vector3 _groundNormal;
    float _gradientAngle;
    Vector3 _gradientDirection;
    MaterialPhysics _groundMaterial = MaterialPhysics.Air;

    bool _jumping;
    bool _holdingJump;
    float _lastJumpStart;
    float _lastButtonDown = -100f;
    float _jumpTimeOut;
    float _extraHeight;
    Vector3 _jumpDirection = Vector3.UnitY;

    Vector3 _bounceVelocity;
    Vector3 _impulse;
    float _scale = 1f;

    public AvatarMotor(CollisionWorld world, Vector3 position)
    {
        _controller = new CharacterController(world, Radius, Height, new Vector3(0f, Height / 2f, 0f)) { Position = position };
        _controller.OnHit = Bounce;
    }

    public CollisionWorld World
    {
        get => _controller.World;
        set => _controller.World = value;
    }

    public Vector3 Position => _controller.Position;
    public Vector3 Velocity => _velocityPrevFrame;
    public bool Grounded => _grounded;
    public bool Jumping => _jumping;
    public float WalkSpeed { get; set; } = DefaultWalkSpeed;

    public float Scale
    {
        get => _scale;
        set
        {
            if (_scale == value) return;
            _scale = value;
            _controller.SetScale(value);
        }
    }
    public bool NoFriction { get; set; }

    float Friction => NoFriction ? 0f : _groundMaterial.Friction;

    public void AddImpulse(Vector3 impulse) => _impulse += impulse;

    public void Teleport(Vector3 position)
    {
        _controller.Position = position;
        _velocityPrevFrame = Vector3.Zero;
        _bounceVelocity = Vector3.Zero;
        _impulse = Vector3.Zero;
    }

    public void Step(Vector3 direction, bool jump)
    {
        _time += FixedDeltaTime;
        float gravity = World.Gravity;
        Vector3 velocity = NextVelocity(_velocityPrevFrame, direction, jump, gravity);
        _controller.Move(velocity * FixedDeltaTime);
        UpdateGround(velocity);
        _velocityPrevFrame = _controller.Velocity / FixedDeltaTime;
    }

    Vector3 NextVelocity(Vector3 velocity, Vector3 direction, bool jump, float gravity)
    {
        if (_grounded)
        {
            velocity = Sliding(velocity, gravity);
            float grip = Friction * Friction;
            velocity -= velocity * grip * FixedDeltaTime;
            velocity = InputGrounded(velocity, direction);
        }
        else
        {
            velocity = InputInAir(velocity, direction);
            velocity.Y = _velocityPrevFrame.Y - gravity * FixedDeltaTime;
        }

        if (_bounceVelocity != Vector3.Zero) velocity = _bounceVelocity;
        _bounceVelocity = Vector3.Zero;

        velocity = Jump(velocity, jump, gravity);
        velocity += _impulse * 0.02f;
        _impulse = Vector3.Zero;
        return velocity;
    }

    Vector3 Sliding(Vector3 velocity, float gravity)
    {
        float grip = Friction * Friction;
        Vector3 slide = _gradientDirection * MathF.Sin(_gradientAngle * UnityMath.Deg2Rad) * (1f - grip) * gravity;
        return slide.Length() > _groundMaterial.StaticFriction ? velocity + slide * FixedDeltaTime : velocity;
    }

    Vector3 InputGrounded(Vector3 velocity, Vector3 direction)
    {
        float speed = Speed(direction);
        Vector3 wanted = direction * speed;
        wanted = UnityMath.Normalized(Vector3.Cross(Vector3.Cross(Vector3.UnitY, wanted), _groundNormal)) * wanted.Length();
        float grip = Friction * Friction;
        velocity += (wanted - velocity) * grip * FixedDeltaTime / 0.02f;
        if (grip < 0.1f && wanted.Length() != 0f) velocity += wanted * 0.5f * FixedDeltaTime;
        return velocity;
    }

    Vector3 InputInAir(Vector3 velocity, Vector3 direction)
    {
        float speed = Speed(direction);
        Vector3 wanted = direction * speed;
        if (wanted.Length() == 0f) return velocity;

        var horizontal = new Vector3(velocity.X, 0f, velocity.Z);
        float before = horizontal.Length();
        horizontal += wanted * InAirControlFactor * FixedDeltaTime;
        float after = horizontal.Length();
        if (after > before && after > speed) horizontal = UnityMath.Normalized(horizontal) * before;
        velocity.X = horizontal.X;
        velocity.Z = horizontal.Z;
        return velocity;
    }

    float Speed(Vector3 direction)
    {
        _currentLerp = MathF.Min(_currentLerp + FixedDeltaTime, LerpTime);
        if (direction.Length() == 0f) _currentLerp = 0f;
        return MathF.Sin(_currentLerp / LerpTime * 0.5f * MathF.PI) * WalkSpeed;
    }

    void UpdateGround(Vector3 velocity)
    {
        _groundNormal = Vector3.Zero;
        if (_controller.TestWithoutSliding(GroundDepth, -Vector3.UnitY, velocity * FixedDeltaTime, out ControllerHit hit))
        {
            _groundNormal = hit.SlopeNormal;
            _gradientDirection = _controller.GradientDirection(hit.Hit);
            _gradientAngle = UnityMath.Angle(Vector3.UnitY, _gradientDirection) - 90f;
            _groundMaterial = MaterialPhysics.For(hit.Hit.Material);
        }
        else
        {
            _groundMaterial = MaterialPhysics.Air;
        }

        bool touching = _groundNormal.Y > 0.01f;
        if (_grounded && !touching) _grounded = false;
        else if (!_grounded && touching)
        {
            _grounded = true;
            _jumping = false;
        }
    }

    Vector3 Jump(Vector3 velocity, bool jump, float gravity)
    {
        if (!jump)
        {
            _holdingJump = false;
            _lastButtonDown = -100f;
        }
        if (jump && _lastButtonDown < 0f) _lastButtonDown = _time;

        float launchSpeed = MathF.Sqrt(2f * BaseHeight * gravity);
        if (!_grounded && _jumping && _holdingJump && _time < _lastJumpStart + _extraHeight / launchSpeed)
            velocity += _jumpDirection * gravity * FixedDeltaTime;

        if (!_grounded || _time - _lastJumpStart <= _jumpTimeOut) return velocity;

        float limit = _groundMaterial.Bounciness > BouncinessThreshold ? BouncyButtonDownTimeLimit : RegularButtonDownTimeLimit;
        _holdingJump = false;
        if (_time - _lastButtonDown >= limit) return velocity;

        float spread = 0f - Friction * Friction + 2f * Friction;
        float slippery = MathF.Sin(_gradientAngle * UnityMath.Deg2Rad) * (1f - spread);
        if (slippery < SlipperyMin) slippery = 0f;

        _jumping = true;
        _lastJumpStart = _time;
        _lastButtonDown = -100f;
        _holdingJump = true;
        _jumpTimeOut = 0f;
        _extraHeight = ExtraHeight - ExtraHeight * slippery;

        float jumpSpeed = launchSpeed - launchSpeed * slippery;
        _jumpDirection = slippery > SlipperyMax ? _groundNormal : Vector3.UnitY;
        if (velocity.Y < 0f) velocity.Y = 0f;
        return velocity + _jumpDirection * jumpSpeed;
    }

    void Bounce(ControllerHit hit)
    {
        if (hit.TestWithoutMoving) return;
        float bounciness = MaterialPhysics.For(hit.Hit.Material).Bounciness;
        if (bounciness <= 0f) return;

        Vector3 incoming = -UnityMath.Normalized(hit.ImpactVelocity);
        float strength = hit.ImpactVelocity.Length() * 0.5f * Vector3.Dot(incoming, hit.SlopeNormal);
        if (strength <= 1f) return;

        float gravity = World.Gravity;
        float speed = hit.ImpactVelocity.Length();
        float height = Math.Clamp(speed * speed / 2f / gravity * bounciness, 0f, 10f * bounciness);
        float launch = MathF.Sqrt(2f * height * gravity);
        Vector3 inverted = -hit.ImpactVelocity;
        Vector3 projected = UnityMath.Project(inverted, hit.SlopeNormal * inverted.Length());
        Vector3 outgoing = UnityMath.Normalized(projected + (projected - inverted));
        if (launch < 10f) launch *= launch / 10f;
        _bounceVelocity = outgoing * launch;
    }
}
