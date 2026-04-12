using Godot;

/// <summary>
/// Arcade-physics car on CharacterBody2D.
/// Forza-style: snappy grip, momentum-preserving handbrake drift, counter-steer recovery.
/// Proper reverse handling: capped speed, intuitive steering, no handbrake drift.
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
    [Export] public float HighSpeedSteerReduction = 0.5f;

    [ExportGroup("Traction")]
    [Export] public float GripFriction        = 12f;
    [Export] public float DriftFriction       = 3.8f;
    [Export] public float TireGrip            = 0.85f;
    [Export] public float GripSpeedFalloff    = 0.15f;
    [Export] public float SlipStartThreshold  = 140f;
    [Export] public float SlipEndThreshold    = 40f;
    [Export] public float SteerDriftSpeed     = 280f;
    [Export] public float SteerDriftInput     = 0.9f;

    [ExportGroup("Handbrake")]
    [Export] public float HandbrakeDrag        = 150f;
    [Export] public float KickImpulse          = 0.15f;
    [Export] public float HandbrakeYawRate     = 3.5f;
    [Export] public float HandbrakeFrictionMul = 0.15f;
    [Export] public float CounterSteerRecovery = 6f;

    [ExportGroup("Debug")]
    [Export] public bool ShowDebugVectors = false;

    // ── public read-only state ────────────────────────────────────────────────
    public bool  IsDrifting      => _drifting;
    public bool  HandbrakeActive => _handbrakeHeld;
    public float Speed           => Mathf.Abs(_forwardSpeed);
    public float LateralSpeed    => _lateralSpeed;
    public float DriftIntensity  => Mathf.Clamp(Mathf.Abs(_lateralSpeed) / SlipStartThreshold, 0f, 1f);
    public float TotalSpeed      => Velocity.Length();
    public bool  IsReversing     => _forwardSpeed < -5f;

    // ── private state ────────────────────────────────────────────────────────
    private float _forwardSpeed;
    private float _lateralSpeed;
    private float _heading;

    private bool  _drifting;
    private float _friction;
    private bool  _prevHandbrake;
    private bool  _handbrakeHeld;
    private bool  _wasHandbraking;

    public override void _Ready()
    {
        _heading  = GlobalRotation;
        _friction = GripFriction;
        MotionMode = MotionModeEnum.Floating;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt         = (float)delta;
        float throttle   = Input.GetAxis("drive_reverse", "drive_forward");
        float steerIn    = Input.GetAxis("steer_left", "steer_right");
        bool  handbrake  = Input.IsActionPressed("handbrake");
        bool  justPulled = handbrake && !_prevHandbrake;
        _prevHandbrake   = handbrake;
        _handbrakeHeld   = handbrake;

        bool goingForward = _forwardSpeed > 5f;
        bool goingReverse = _forwardSpeed < -5f;

        // ── 1. Decompose velocity into local frame ────────────────────────────
        Vector2 vel = Velocity;
        float cosH  = Mathf.Cos(_heading);
        float sinH  = Mathf.Sin(_heading);

        _forwardSpeed =  vel.X * cosH + vel.Y * sinH;
        _lateralSpeed = -vel.X * sinH + vel.Y * cosH;

        // Update direction flags after decomposition
        goingForward = _forwardSpeed > 5f;
        goingReverse = _forwardSpeed < -5f;

        // ── 2. Handbrake kick (angular impulse on pull) ─────────────────────
        if (justPulled && Mathf.Abs(_forwardSpeed) > 40f)
        {
            _drifting = true;

            if (goingReverse)
            {
                // J-TURN: big kick to spin the car 180°.
                // Always kicks toward steer direction, or defaults to +1.
                // Larger impulse than forward drift to get the full rotation going.
                float kickDir = Mathf.Abs(steerIn) > 0.1f ? Mathf.Sign(steerIn) : 1f;
                float speedFactor = Mathf.Clamp(Mathf.Abs(_forwardSpeed) / MaxSpeed, 0.4f, 1f);
                _heading += kickDir * KickImpulse * 2.5f * speedFactor;
            }
            else
            {
                // Forward drift kick
                float kickDir = Mathf.Abs(steerIn) > 0.1f ? Mathf.Sign(steerIn) : 1f;
                float speedFactor = Mathf.Clamp(_forwardSpeed / MaxSpeed, 0.3f, 1f);
                _heading += kickDir * KickImpulse * speedFactor;
            }
        }

        // ── 3. Handbrake yaw (sustained heading rotation while held) ──────────
        //    Forward: heading rotates, velocity re-decomposes → drift slide.
        //    Reverse: faster yaw to spin 180° (J-turn). As heading passes 90°
        //    the decomposition naturally flips _forwardSpeed from negative to
        //    positive — the car comes out going forward.
        float totalSpeed = vel.Length();
        if (handbrake && totalSpeed > 10f)
        {
            float yawDir = 0f;
            if (Mathf.Abs(steerIn) > 0.05f)
            {
                yawDir = steerIn;
            }
            else if (goingReverse)
            {
                // No steer input during reverse handbrake: pick a direction
                // based on existing lateral speed, or default to +1
                yawDir = Mathf.Abs(_lateralSpeed) > 5f ? Mathf.Sign(_lateralSpeed) : 1f;
            }

            if (!Mathf.IsZeroApprox(yawDir))
            {
                float speedRatio = Mathf.Clamp(totalSpeed / MaxSpeed, 0.2f, 1f);
                // Faster yaw in reverse for the J-turn snap
                float yawRate = goingReverse ? HandbrakeYawRate * 1.8f : HandbrakeYawRate;
                _heading += yawDir * yawRate * speedRatio * dt;
            }

            // Re-decompose with new heading
            cosH = Mathf.Cos(_heading);
            sinH = Mathf.Sin(_heading);
            _forwardSpeed =  vel.X * cosH + vel.Y * sinH;
            _lateralSpeed = -vel.X * sinH + vel.Y * cosH;

            // Update direction flags — the J-turn can flip these mid-frame
            goingForward = _forwardSpeed > 5f;
            goingReverse = _forwardSpeed < -5f;
        }

        // ── 4. Longitudinal speed ─────────────────────────────────────────────
        if (handbrake)
        {
            // Gentle drag in both directions — preserve momentum through drifts/J-turns
            _forwardSpeed = Mathf.MoveToward(_forwardSpeed, 0f, HandbrakeDrag * dt);
        }
        else if (!Mathf.IsZeroApprox(throttle))
        {
            bool braking = Mathf.Abs(_forwardSpeed) > 20f
                        && Mathf.Sign(throttle) != Mathf.Sign(_forwardSpeed);

            float targetSpeed;
            if (braking)
            {
                targetSpeed = 0f;
            }
            else
            {
                targetSpeed = throttle * MaxSpeed;
            }

            float accel = braking ? BrakeForce : Acceleration;
            _forwardSpeed = Mathf.MoveToward(_forwardSpeed, targetSpeed, accel * dt);
        }
        else
        {
            _forwardSpeed = Mathf.MoveToward(_forwardSpeed, 0f, CoastDrag * dt);
        }

        // ── 5. Steering ──────────────────────────────────────────────────────
        // During forward handbrake, yaw is handled in step 3. Otherwise use
        // bicycle model. In reverse, steer direction stays intuitive (left = left).
        bool handbrakeYawActive = handbrake && goingForward;

        if (!handbrakeYawActive && !Mathf.IsZeroApprox(steerIn))
        {
            float steerSource = Mathf.Abs(_forwardSpeed);
            if (_drifting)
                steerSource = Mathf.Max(steerSource, Mathf.Abs(_lateralSpeed) * 0.5f);

            if (steerSource > 1f)
            {
                float absForward = Mathf.Abs(_forwardSpeed);
                float speedRatio = Mathf.Clamp(absForward / MaxSpeed, 0f, 1f);

                // Less steer reduction in reverse (you need full lock at low speed)
                float reduction = goingReverse
                    ? HighSpeedSteerReduction * 0.3f
                    : HighSpeedSteerReduction;
                float steerScale = 1f - reduction * speedRatio;
                float steerAngle = steerIn * Mathf.DegToRad(MaxSteerAngle) * steerScale;

                float steerSpeed = Mathf.Max(absForward, MinSteerSpeed);

                // In reverse, keep steer direction intuitive: left input = car goes left.
                // The bicycle model naturally inverts for negative forward speed via turnSign,
                // but arcade games feel better with direct mapping. We negate the inversion.
                // In forward or during drift recovery, use physics-correct sign.
                float turnSign;
                if (_drifting && absForward < 30f)
                {
                    turnSign = 1f;  // direct mapping during drift recovery
                }
                else if (goingReverse)
                {
                    turnSign = -1f; // physics-correct: rear-steer inverts heading
                                    // This makes the car's nose go the steer direction
                                    // because the rear pushes opposite
                }
                else
                {
                    turnSign = 1f;  // forward
                }

                float omega = steerSpeed * Mathf.Tan(steerAngle) / Wheelbase;
                _heading += turnSign * omega * dt;
            }
        }

        // ── 6. Traction state machine ─────────────────────────────────────────
        float absLateral = Mathf.Abs(_lateralSpeed);

        // Only allow steer-drift entry when going forward
        bool steerDriftEntry = goingForward
                            && Mathf.Abs(steerIn) >= SteerDriftInput
                            && _forwardSpeed > SteerDriftSpeed;

        if (handbrake || absLateral > SlipStartThreshold || steerDriftEntry)
        {
            if (!_drifting && handbrake)
                _wasHandbraking = true;
            _drifting = true;
        }
        else if (_drifting && absLateral < SlipEndThreshold && !steerDriftEntry)
        {
            _drifting = false;
            _wasHandbraking = false;
        }

        // ── 7. Lateral correction ─────────────────────────────────────────────
        float baseFriction = _drifting ? DriftFriction : GripFriction;
        float targetFriction;

        if (handbrake)
        {
            // Low lateral friction while handbrake held — car slides freely
            // for both forward drifts and reverse J-turns
            targetFriction = baseFriction * HandbrakeFrictionMul;
        }
        else if (_drifting && _wasHandbraking && _IsCounterSteering(steerIn))
        {
            // Counter-steer recovery: boosted friction to straighten out
            targetFriction = baseFriction + CounterSteerRecovery;
        }
        else
        {
            targetFriction = baseFriction;
        }

        _friction = Mathf.Lerp(_friction, targetFriction, 10f * dt);

        float speedGripFactor = 1f - GripSpeedFalloff
            * Mathf.Clamp(Mathf.Abs(_forwardSpeed) / MaxSpeed, 0f, 1f);
        float grip = 1f - Mathf.Exp(-_friction * dt);
        _lateralSpeed = Mathf.Lerp(_lateralSpeed, 0f, grip * TireGrip * speedGripFactor);

        // ── 8. Rebuild world velocity ─────────────────────────────────────────
        cosH = Mathf.Cos(_heading);
        sinH = Mathf.Sin(_heading);

        Velocity = new Vector2(
            _forwardSpeed * cosH - _lateralSpeed * sinH,
            _forwardSpeed * sinH + _lateralSpeed * cosH
        );

        MoveAndSlide();

        _heading = Mathf.Wrap(_heading, -Mathf.Pi, Mathf.Pi);
        GlobalRotation = _heading;

        if (ShowDebugVectors)
            QueueRedraw();
    }

    /// <summary>
    /// Counter-steer: steering opposite the slide direction to recover.
    /// Only meaningful after a handbrake drift.
    /// </summary>
    private bool _IsCounterSteering(float steerIn)
    {
        if (Mathf.Abs(steerIn) < 0.1f || Mathf.Abs(_lateralSpeed) < SlipEndThreshold)
            return false;
        return Mathf.Sign(steerIn) != Mathf.Sign(_lateralSpeed);
    }

    public override void _Draw()
    {
        if (!ShowDebugVectors) return;

        DrawLine(Vector2.Zero, Vector2.Right * _forwardSpeed * 0.2f, Colors.Green, 3f);
        Color slipColor = _drifting ? Colors.Orange : Colors.Red;
        DrawLine(Vector2.Zero, Vector2.Down * _lateralSpeed * 0.5f, slipColor, 3f);

        // Speed text
        DrawString(ThemeDB.FallbackFont, new Vector2(-40, -30),
            $"F:{_forwardSpeed:F0} L:{_lateralSpeed:F0} D:{(_drifting ? "Y" : "N")}",
            HorizontalAlignment.Left, -1, 12, Colors.White);

        float steerIn = Input.GetAxis("steer_left", "steer_right");
        if (!Mathf.IsZeroApprox(steerIn))
        {
            float steerAngle = steerIn * Mathf.DegToRad(MaxSteerAngle);
            float radius     = Wheelbase / Mathf.Tan(steerAngle);
            DrawArc(new Vector2(0, radius), Mathf.Abs(radius), 0, Mathf.Tau, 64, Colors.Cyan, 1f);
        }
    }
}