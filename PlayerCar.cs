using Godot;

/// <summary>
/// Arcade-physics car on CharacterBody2D.
/// Forza Horizon style: snappy grip, satisfying handbrake drift, natural recovery.
/// </summary>
public partial class PlayerCar : CharacterBody2D
{
    [ExportGroup("Engine")]
    [Export] public float MaxSpeed       = 460f;
    [Export] public float Acceleration   = 900f;
    [Export] public float BrakeForce     = 2400f;
    [Export] public float CoastDrag      = 450f;

    [ExportGroup("Steering")]
    [Export] public float Wheelbase      = 150f;
    [Export] public float MaxSteerAngle  = 32f;
    [Export] public float MinSteerSpeed  = 80f;
    /// <summary>At top speed, effective steer angle is reduced by this fraction (0–1).</summary>
    [Export] public float HighSpeedSteerReduction = 0.5f;

    [ExportGroup("Traction")]
    [Export] public float GripFriction        = 12f;
    [Export] public float DriftFriction       = 3.8f;   // higher than before — sole lateral correction
    [Export] public float TireGrip            = 0.85f;
    /// <summary>Grip falls off by this fraction at max speed (0–1).</summary>
    [Export] public float GripSpeedFalloff    = 0.15f;
    [Export] public float SlipStartThreshold  = 140f;
    [Export] public float SlipEndThreshold    = 40f;
    /// <summary>Steer-input drift entry: full lock above this speed triggers drift.</summary>
    [Export] public float SteerDriftSpeed     = 280f;
    /// <summary>Steer magnitude (0–1) required to trigger steer-initiated drift.</summary>
    [Export] public float SteerDriftInput     = 0.9f;

    [ExportGroup("Handbrake")]
    [Export] public float HandbrakeDecel      = 600f;
    [Export] public float HandbrakeOversteer  = 2.0f;
    /// <summary>One-shot angular impulse in radians applied on handbrake pull.</summary>
    [Export] public float KickImpulse         = 0.12f;

    [ExportGroup("Debug")]
    [Export] public bool ShowDebugVectors = false;

    // ── public read-only state (for tire trails, UI, etc.) ──────────────────
    public bool  IsDrifting   => _drifting;
    public float Speed        => Mathf.Abs(_forwardSpeed);
    public float LateralSpeed => _lateralSpeed;

    // ── private state ────────────────────────────────────────────────────────
    private float _forwardSpeed;
    private float _lateralSpeed;
    private float _heading;

    private bool  _drifting;
    private float _friction;
    private bool  _prevHandbrake;

    public override void _Ready()
    {
        _heading  = GlobalRotation;
        _friction = GripFriction;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt         = (float)delta;
        float throttle   = Input.GetAxis("drive_reverse", "drive_forward");
        float steerIn    = Input.GetAxis("steer_left", "steer_right");
        bool  handbrake  = Input.IsActionPressed("handbrake");
        bool  justPulled = handbrake && !_prevHandbrake;
        _prevHandbrake   = handbrake;

        // ── 1. Decompose velocity into local frame ────────────────────────────
        Vector2 vel = Velocity;
        float cosH  = Mathf.Cos(_heading);
        float sinH  = Mathf.Sin(_heading);

        _forwardSpeed =  vel.X * cosH + vel.Y * sinH;
        _lateralSpeed = -vel.X * sinH + vel.Y * cosH;

        // ── 2. Handbrake kick (angular impulse, not sustained multiplier) ─────
        if (justPulled && Mathf.Abs(_forwardSpeed) > 40f)
        {
            _drifting = true;
            // Impulse in the direction the player is steering (or slight default)
            float kickDir = Mathf.Abs(steerIn) > 0.1f ? Mathf.Sign(steerIn) : 1f;
            float speedFactor = Mathf.Clamp(Mathf.Abs(_forwardSpeed) / MaxSpeed, 0.3f, 1f);
            _heading += kickDir * KickImpulse * speedFactor;
        }

        // ── 3. Longitudinal (forward) speed ───────────────────────────────────
        if (handbrake)
        {
            _forwardSpeed = Mathf.MoveToward(_forwardSpeed, 0f, HandbrakeDecel * dt);
        }
        else if (!Mathf.IsZeroApprox(throttle))
        {
            bool braking = Mathf.Abs(_forwardSpeed) > 20f
                        && Mathf.Sign(throttle) != Mathf.Sign(_forwardSpeed);
            _forwardSpeed = Mathf.MoveToward(
                _forwardSpeed,
                braking ? 0f : throttle * MaxSpeed,
                (braking ? BrakeForce : Acceleration) * dt);
        }
        else
        {
            _forwardSpeed = Mathf.MoveToward(_forwardSpeed, 0f, CoastDrag * dt);
        }

        // ── 4. Steering (speed-dependent lock reduction) ──────────────────────
        if (!Mathf.IsZeroApprox(steerIn) && Mathf.Abs(_forwardSpeed) > 1f)
        {
            float speedRatio = Mathf.Clamp(Mathf.Abs(_forwardSpeed) / MaxSpeed, 0f, 1f);
            float steerScale = 1f - HighSpeedSteerReduction * speedRatio;
            float steerAngle = steerIn * Mathf.DegToRad(MaxSteerAngle) * steerScale;

            float steerSpeed = Mathf.Max(Mathf.Abs(_forwardSpeed), MinSteerSpeed);
            float turnSign   = _forwardSpeed >= 0f ? 1f : -1f;
            float omega      = steerSpeed * Mathf.Tan(steerAngle) / Wheelbase;

            float oversteer  = handbrake ? HandbrakeOversteer : 1f;

            _heading += turnSign * omega * oversteer * dt;
        }

        // ── 5. Traction state machine ─────────────────────────────────────────
        float absLateral = Mathf.Abs(_lateralSpeed);

        // Steer-initiated drift: hard steering at high speed enters drift even
        // before lateral speed has built up.
        bool steerDriftEntry = Mathf.Abs(steerIn) >= SteerDriftInput
                            && Mathf.Abs(_forwardSpeed) > SteerDriftSpeed;

        if (handbrake || absLateral > SlipStartThreshold || steerDriftEntry)
        {
            _drifting = true;
        }
        else if (_drifting && absLateral < SlipEndThreshold && !steerDriftEntry)
        {
            _drifting = false;
        }

        // ── 6. Lateral correction (single system — exponential decay only) ────
        float targetFriction = _drifting ? DriftFriction : GripFriction;
        _friction = Mathf.Lerp(_friction, targetFriction, 8f * dt);

        // Speed-dependent grip falloff: less grip at higher speeds
        float speedGripFactor = 1f - GripSpeedFalloff
            * Mathf.Clamp(Mathf.Abs(_forwardSpeed) / MaxSpeed, 0f, 1f);
        float grip = 1f - Mathf.Exp(-_friction * dt);
        _lateralSpeed = Mathf.Lerp(_lateralSpeed, 0f, grip * TireGrip * speedGripFactor);

        // ── 7. Rebuild world velocity ─────────────────────────────────────────
        cosH = Mathf.Cos(_heading);
        sinH = Mathf.Sin(_heading);

        Velocity = new Vector2(
            _forwardSpeed * cosH - _lateralSpeed * sinH,
            _forwardSpeed * sinH + _lateralSpeed * cosH
        );

        MoveAndSlide();

        // Normalize heading to prevent float drift over long sessions
        _heading = Mathf.Wrap(_heading, -Mathf.Pi, Mathf.Pi);
        GlobalRotation = _heading;

        if (ShowDebugVectors)
            QueueRedraw();
    }

    public override void _Draw()
    {
        if (!ShowDebugVectors) return;

        DrawLine(Vector2.Zero, Vector2.Right * _forwardSpeed * 0.2f, Colors.Green, 3f);
        Color slipColor = _drifting ? Colors.Orange : Colors.Red;
        DrawLine(Vector2.Zero, Vector2.Down * _lateralSpeed * 0.5f, slipColor, 3f);

        float steerIn = Input.GetAxis("steer_left", "steer_right");
        if (!Mathf.IsZeroApprox(steerIn))
        {
            float steerAngle = steerIn * Mathf.DegToRad(MaxSteerAngle);
            float radius     = Wheelbase / Mathf.Tan(steerAngle);
            DrawArc(new Vector2(0, radius), Mathf.Abs(radius), 0, Mathf.Tau, 64, Colors.Cyan, 1f);
        }
    }
}