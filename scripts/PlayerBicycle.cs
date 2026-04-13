using Godot;

/// <summary>
/// Ackermann kinematic bicycle model — CharacterBody2D edition.
///
/// Core steering formula (single-track bicycle model):
///   ω = v · tan(δ) / L
/// where ω is the yaw rate (rad/s), v is the actual forward speed,
/// δ is the front-wheel steer angle, and L is the wheelbase.
/// This produces a constant turn radius  R = L / tan(δ)  regardless of speed,
/// with the yaw rate correctly scaling with velocity.
///
/// Lateral grip is an exponential-decay drag on the sideways velocity component.
/// Handbrake lowers the friction coefficient and multiplies the yaw rate,
/// allowing controlled oversteer slides.
///
/// Arcade features:
///   • Drift countersteer assist — steering back into the slide restores grip faster.
///   • Natural drift yaw — the car rotates organically with the lateral slide.
///   • Speed preserved during handbrake so drifts don't kill momentum.
/// </summary>
public partial class PlayerBicycle : CharacterBody2D
{
    // ── Engine ────────────────────────────────────────────────────────────────
    [ExportGroup("Engine")]
    [Export] public float MaxSpeed     = 460f;
    [Export] public float Acceleration = 950f;
    [Export] public float BrakeForce   = 2600f;
    [Export] public float CoastDrag    = 400f;

    // ── Ackermann Steering ────────────────────────────────────────────────────
    [ExportGroup("Ackermann Steering")]
    /// <summary>Front-to-rear axle distance in world pixels. Longer = wider turning radius.</summary>
    [Export] public float Wheelbase     = 210f;
    /// <summary>Full front-wheel deflection angle (degrees). Turn radius = L / tan(δ).</summary>
    [Export] public float MaxSteerAngle = 28f;
    /// <summary>
    /// Minimum effective speed used in the ω = v·tan(δ)/L denominator.
    /// Keeps the car steerable from a standstill without div-by-zero.
    /// Higher = less twitchy at low speeds.
    /// </summary>
    [Export] public float MinSteerSpeed = 130f;

    // ── Traction ──────────────────────────────────────────────────────────────
    [ExportGroup("Traction")]
    /// <summary>Lateral-friction coefficient while gripping.</summary>
    [Export] public float GripFriction      = 18f;
    /// <summary>
    /// Lateral-friction coefficient while sliding. Lower = slide sustains longer.
    /// At 1.4 lateral velocity halves in ~0.6 s — long enough to feel like a real drift.
    /// </summary>
    [Export] public float DriftFriction     = 1.4f;
    /// <summary>Lateral speed (px/s) that breaks traction.</summary>
    [Export] public float SlipThreshold     = 120f;
    /// <summary>Lateral speed (px/s) below which grip is restored. Low = harder to exit drift.</summary>
    [Export] public float RecoveryThreshold = 28f;
    /// <summary>Overall tire grip multiplier [0–1].</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float TireGrip = 0.90f;

    // ── Handbrake ─────────────────────────────────────────────────────────────
    [ExportGroup("Handbrake")]
    /// <summary>Forward decel while handbrake held — low value keeps drift momentum alive.</summary>
    [Export] public float HandbrakeBrakeRate = 900f;
    [Export] public float HandbrakeOversteer = 2.8f;
    [Export] public float HandbrakeKickMul   = 2.2f;
    [Export] public float HandbrakeKickTime  = 0.22f;

    // ── Arcade / Drift Feel ───────────────────────────────────────────────────
    [ExportGroup("Arcade")]
    /// <summary>
    /// Extra friction added when the player countersteers during a drift.
    /// Higher = easier to "catch" the slide and recover.
    /// </summary>
    [Export] public float DriftCountersteer = 10f;
    /// <summary>
    /// How much lateral slide velocity naturally rotates the car heading (rad/s per px/s of vLat).
    /// Subtle — too high makes the car spin instead of slide.
    /// </summary>
    [Export] public float DriftYawRate = 0.0015f;

    // ── Debug ─────────────────────────────────────────────────────────────────
    [ExportGroup("Debug")]
    /// <summary>Draw velocity vectors and the Ackermann turn-radius arc.</summary>
    [Export] public bool ShowDebugLines = false;

    // ── Private state ──────────────────────────────────────────────────────────
    private float _heading;
    private float _speed;
    private bool  _drifting;
    private float _smoothFriction;
    private float _kickTimer;
    private bool  _prevHandbrake;

    public override void _Ready()
    {
        _heading        = GlobalRotation;
        _smoothFriction = GripFriction;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt       = (float)delta;
        float throttle = Input.GetAxis("drive_reverse", "drive_forward");
        float steerIn  = Input.GetAxis("steer_left", "steer_right");
        bool  hb       = Input.IsActionPressed("handbrake");

        bool hbPressed = hb && !_prevHandbrake;
        _prevHandbrake = hb;
        if (hbPressed) _kickTimer = HandbrakeKickTime;
        _kickTimer = Mathf.Max(0f, _kickTimer - dt);

        // ── Local-frame velocity (pre-rotation) ───────────────────────────────
        Vector2 fwdVec   = Transform.X;
        Vector2 rightVec = Transform.Y;
        float   vFwd     = Velocity.Dot(fwdVec);
        float   vLat     = Velocity.Dot(rightVec);

        // ── Forward speed scalar ──────────────────────────────────────────────
        if (hb)
        {
            _speed = Mathf.MoveToward(_speed, 0f, HandbrakeBrakeRate * dt);
        }
        else if (!Mathf.IsZeroApprox(throttle))
        {
            bool braking = Mathf.Abs(_speed) > 20f
                        && Mathf.Sign(throttle) != Mathf.Sign(_speed);
            _speed = Mathf.MoveToward(_speed,
                braking ? 0f : throttle * MaxSpeed,
                (braking ? BrakeForce : Acceleration) * dt);
        }
        else
        {
            _speed = Mathf.MoveToward(_speed, 0f, CoastDrag * dt);
        }

        // ── Ackermann yaw rate:  ω = v · tan(δ) / L ──────────────────────────
        if (!Mathf.IsZeroApprox(steerIn))
        {
            float steerAngle = steerIn * Mathf.DegToRad(MaxSteerAngle);
            float effSpeed   = Mathf.Max(Mathf.Abs(vFwd), MinSteerSpeed);
            float omega      = effSpeed * Mathf.Tan(steerAngle) / Wheelbase;
            float turnSign   = vFwd >= 0f ? 1f : -1f;
            float oversteer  = hb ? HandbrakeOversteer : 1f;
            if (_kickTimer > 0f) oversteer *= HandbrakeKickMul;

            _heading += turnSign * omega * oversteer * dt;
        }

        // ── Natural drift yaw: lateral slide rotates the car organically ──────
        // The car's heading drifts in the direction of the slide,
        // making the rear naturally step out without extra steering input.
        if (_drifting)
        {
            _heading += vLat * DriftYawRate * dt;
        }

        GlobalRotation = _heading;

        // Re-read basis in post-rotation frame
        fwdVec   = Transform.X;
        rightVec = Transform.Y;

        // ── Traction hysteresis ───────────────────────────────────────────────
        float absLat = Mathf.Abs(vLat);
        if (hb || absLat > SlipThreshold)
            _drifting = true;
        else if (_drifting && absLat < RecoveryThreshold)
            _drifting = false;

        // ── Countersteer assist: steering into the slide restores grip faster ──
        float counterBoost = 0f;
        if (_drifting && !Mathf.IsZeroApprox(steerIn) && !Mathf.IsZeroApprox(vLat))
        {
            // Countersteer when steer direction is opposite to lateral slip direction
            bool countersteering = Mathf.Sign(steerIn) != Mathf.Sign(vLat);
            if (countersteering)
                counterBoost = DriftCountersteer * Mathf.Abs(steerIn);
        }

        float targetFriction = _drifting ? DriftFriction + counterBoost : GripFriction;
        _smoothFriction      = Mathf.Lerp(_smoothFriction, targetFriction, 10f * dt);

        // ── Build velocity ────────────────────────────────────────────────────
        float newFwd = Mathf.Lerp(vFwd, _speed, 1f - Mathf.Exp(-12f * dt));

        float gripBlend = (1f - Mathf.Exp(-_smoothFriction * dt)) * TireGrip;
        float newLat    = Mathf.Lerp(vLat, 0f, gripBlend);

        Velocity = fwdVec * newFwd + rightVec * newLat;
        MoveAndSlide();

        _speed = Velocity.Dot(Transform.X);

        if (ShowDebugLines) QueueRedraw();
    }

    // ── Debug overlay ──────────────────────────────────────────────────────────
    public override void _Draw()
    {
        if (!ShowDebugLines) return;

        Vector2 fwd   = Transform.X;
        Vector2 right = Transform.Y;
        float   vF    = Velocity.Dot(fwd);
        float   vL    = Velocity.Dot(right);

        DrawLine(Vector2.Zero, Vector2.Right * vF * 0.2f, Colors.Green, 3f);
        DrawLine(Vector2.Zero, Vector2.Down  * vL * 0.5f,
                 _drifting ? Colors.Orange : Colors.Red, 3f);

        float steerIn = Input.GetAxis("steer_left", "steer_right");
        if (!Mathf.IsZeroApprox(steerIn))
        {
            float steerAngle = steerIn * Mathf.DegToRad(MaxSteerAngle);
            float radius     = Wheelbase / Mathf.Tan(steerAngle);
            if (Mathf.Abs(radius) < 8000f)
                DrawArc(new Vector2(0f, radius), Mathf.Abs(radius),
                        0f, Mathf.Tau, 64, Colors.Cyan, 1f);
        }
    }
}
