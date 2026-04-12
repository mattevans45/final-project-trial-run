using Godot;

/// <summary>
/// Bicycle (Ackermann) car controller with dynamic traction loss, oversteer, and handbrake hysteresis.
/// </summary>
public partial class PlayerBicycle : RigidBody2D
{
    [ExportGroup("Engine")]
    [Export] public float MaxSpeed = 460f;      // px/s
    [Export] public float Acceleration = 900f;  // px/s^2
    [Export] public float BrakeForce = 2400f;   // px/s^2
    [Export] public float HandbrakeForce = 1500f; // px/s^2
    [Export] public float CoastDrag = 450f;     // px/s^2
    [Export] public float ForwardResponse = 12f;
    [Export] public float SlipDrag = 900f;      // px/s^2 removed while sliding sideways

    [ExportGroup("Bicycle Steering")]
    [Export] public float Wheelbase = 150f;
    [Export] public float MaxSteerAngle = 32f;
    [Export] public float MinSteerSpeed = 80f;

    [ExportGroup("Drifting Dynamics")]
    [Export(PropertyHint.Range, "0,1,0.01")] public float TireGrip = 0.82f;
    [Export] public float PeakGripFriction = 18f;
    [Export] public float SlidingFriction = 3f;

    /// <summary>
    /// Speed required to break traction organically.
    /// </summary>
    [Export] public float TractionLossThreshold = 150f;
    /// <summary>
    /// Speed must drop below this to regain full grip. Creates a sustained drift.
    /// </summary>
    [Export] public float TractionRecoveryThreshold = 60f;
    /// <summary>
    /// Multiplier for rotation speed when the handbrake is pulled to swing the rear out.
    /// </summary>
    [Export] public float HandbrakeOversteer = 2.2f;
    /// <summary>
    /// Brief extra rotation boost applied right when the handbrake is engaged.
    /// </summary>
    [Export] public float HandbrakeKickMultiplier = 1.35f;
    /// <summary>
    /// Duration of the handbrake kick in seconds.
    /// </summary>
    [Export] public float HandbrakeKickTime = 0.16f;

    private float _speed;
    private float _heading;

    // State tracking for smooth drifts
    private bool _isDrifting = false;
    private float _currentFriction;
    private bool _wasHandbrakeHeld = false;
    private float _handbrakeKickTimer = 0f;

    public override void _Ready()
    {
        _heading = GlobalRotation;
        _currentFriction = PeakGripFriction;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        float throttle = Input.GetAxis("drive_reverse", "drive_forward");
        float steerIn = Input.GetAxis("steer_left", "steer_right");
        bool handbrake = Input.IsActionPressed("handbrake");
        bool handbrakePressed = handbrake && !_wasHandbrakeHeld;
        _wasHandbrakeHeld = handbrake;

        // 1. Read velocity in PRE-rotation frame
        Vector2 prevForward = Transform.X;
        Vector2 prevRight = Transform.Y;
        float currentForwardSpeed = LinearVelocity.Dot(prevForward);
        float currentLateralSpeed = LinearVelocity.Dot(prevRight);

        // Latch drift + kick on press
        if (handbrakePressed)
        {
            _isDrifting = true;
            _handbrakeKickTimer = HandbrakeKickTime;
        }
        if (_handbrakeKickTimer > 0f)
            _handbrakeKickTimer = Mathf.Max(0f, _handbrakeKickTimer - dt);

        // 2. Forward speed scalar
        if (handbrake)
            _speed = Mathf.MoveToward(_speed, 0f, HandbrakeForce * dt);
        else if (!Mathf.IsZeroApprox(throttle))
        {
            bool braking = Mathf.Abs(_speed) > 20f && Mathf.Sign(throttle) != Mathf.Sign(_speed);
            _speed = Mathf.MoveToward(_speed,
                braking ? 0f : throttle * MaxSpeed,
                (braking ? BrakeForce : Acceleration) * dt);
        }
        else
            _speed = Mathf.MoveToward(_speed, 0f, CoastDrag * dt);

        // 3. Steering — uses pre-rotation forward speed, result written to _heading
        if (!Mathf.IsZeroApprox(steerIn))
        {
            float steerAngle = steerIn * Mathf.DegToRad(MaxSteerAngle);
            float steerSpeed = Mathf.Max(Mathf.Abs(currentForwardSpeed), MinSteerSpeed);
            float turnSign = currentForwardSpeed >= 0f ? 1f : -1f;
            float omega = steerSpeed * Mathf.Tan(steerAngle) / Wheelbase;

            float oversteer = handbrake ? HandbrakeOversteer : 1f;
            if (_handbrakeKickTimer > 0f) oversteer *= HandbrakeKickMultiplier;

            _heading += turnSign * omega * oversteer * dt;
        }

        // 4. Apply rotation — MUST happen before velocity is rebuilt
        GlobalRotation = _heading;

        // 5. Re-read basis in POST-rotation frame
        Vector2 forward = Transform.X;
        Vector2 right = Transform.Y;

        float newForwardSpeed = Mathf.Lerp(currentForwardSpeed, _speed,
            1f - Mathf.Exp(-ForwardResponse * dt));

        // 6. Traction hysteresis
        float absSlip = Mathf.Abs(currentLateralSpeed);
        if (handbrake || absSlip > TractionLossThreshold)
            _isDrifting = true;
        else if (_isDrifting && Mathf.Abs(currentForwardSpeed) < TractionRecoveryThreshold
                             && absSlip < TractionRecoveryThreshold)
            _isDrifting = false;

        float targetFriction = _isDrifting ? SlidingFriction : PeakGripFriction;
        _currentFriction = Mathf.Lerp(_currentFriction, targetFriction, 10f * dt);

        float lateralBlend = (1f - Mathf.Exp(-_currentFriction * dt)) * TireGrip;
        float newLateralSpeed = Mathf.Lerp(currentLateralSpeed, 0f, lateralBlend);

        // 7. Scrub lateral slip only — preserve forward momentum
        if (_isDrifting && Mathf.Abs(newLateralSpeed) > 0.001f)
            newLateralSpeed = Mathf.MoveToward(newLateralSpeed, 0f, SlipDrag * dt);

        LinearVelocity = forward * newForwardSpeed + right * newLateralSpeed;

        QueueRedraw();
    }

    public override void _Draw()
    {
        Vector2 forward = Transform.X;
        Vector2 right = Transform.Y;

        float currentForwardSpeed = LinearVelocity.Dot(forward);
        float currentLateralSpeed = LinearVelocity.Dot(right);

        DrawLine(Vector2.Zero, Vector2.Right * currentForwardSpeed * 0.2f, Colors.Green, 3f);

        // Changing the slip line to Orange when drifting for better visual debugging
        Color slipColor = _isDrifting ? Colors.Orange : Colors.Red;
        DrawLine(Vector2.Zero, Vector2.Down * currentLateralSpeed * 0.5f, slipColor, 3f);

        float steerIn = Input.GetAxis("steer_left", "steer_right");
        if (!Mathf.IsZeroApprox(steerIn))
        {
            float steerAngle = steerIn * Mathf.DegToRad(MaxSteerAngle);
            float radius = Wheelbase / Mathf.Tan(steerAngle);
            Vector2 centerOfRotation = new Vector2(0, radius);
            DrawArc(centerOfRotation, Mathf.Abs(radius), 0, Mathf.Tau, 64, Colors.Cyan, 1f);
        }
    }
}