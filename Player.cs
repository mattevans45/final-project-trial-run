using Godot;

/// <summary>
/// Arcade top-down / isometric car controller.
///
/// Design goals:
///   • Turns responsively at any speed (no "need speed to steer" feel).
///   • Near-zero lateral slide — the car goes where it's pointed, like GTA 1/2.
///   • Simple thrust + braking, no throttle-curve simulation.
///   • Script owns the heading; physics only drives linear movement.
/// </summary>
public partial class Player : RigidBody2D
{
    [ExportGroup("Engine")]
    [Export] public float EnginePower = 950f;
    [Export] public float MaxSpeed    = 480f;
    [Export] public float BrakeForce  = 2000f;
    [Export] public float CoastDrag   = 400f;   // gentle deceleration when no input

    [ExportGroup("Handling")]
    [Export] public float TurnSpeed    = 3.4f;  // rad/s — rotation rate at full speed
    [Export] public float TurnSpeedLow = 1.8f;  // rad/s — rotation rate when nearly stopped
    [Export] public float TurnSpeedRef = 180f;  // speed (px/s) at which TurnSpeed is reached
    [Export] public float Grip         = 0.96f; // lateral grip [0–1]; 1 = no slide at all

    private float _heading;

    public override void _Ready()
    {
        _heading = GlobalRotation;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt       = (float)delta;
        float throttle = Input.GetAxis("drive_reverse", "drive_forward");
        float steering = Input.GetAxis("steer_left",   "steer_right");

        float   speed        = LinearVelocity.Length();
        Vector2 forward      = Vector2.Right.Rotated(_heading);
        float   forwardSpeed = LinearVelocity.Dot(forward);

        // ── Steering ─────────────────────────────────────────────────────────
        // Works at any speed. Blends from TurnSpeedLow (stopped) → TurnSpeed (fast)
        // so it feels tight at speed but still responsive when manoeuvring slowly.
        if (!Mathf.IsZeroApprox(steering))
        {
            float turnSign  = forwardSpeed >= 0f ? 1f : -1f;
            float t         = Mathf.Clamp(speed / TurnSpeedRef, 0f, 1f);
            float turnRate  = Mathf.Lerp(TurnSpeedLow, TurnSpeed, t);
            _heading       += steering * turnSign * turnRate * dt;
        }

        GlobalRotation = _heading;
        forward = Transform.X; // always == Vector2.Right.Rotated(_heading)

        // ── Thrust / brake / coast ────────────────────────────────────────────
        if (!Mathf.IsZeroApprox(throttle))
        {
            bool braking = Mathf.Abs(forwardSpeed) > 30f
                        && Mathf.Sign(throttle) != Mathf.Sign(forwardSpeed);

            if (braking)
                ApplyCentralForce(-forward * Mathf.Sign(forwardSpeed) * BrakeForce);
            else
                ApplyCentralForce(forward * throttle * EnginePower);
        }
        else if (speed > 5f)
        {
            // Light engine-drag when coasting so the car doesn't slide forever.
            ApplyCentralForce(-LinearVelocity.Normalized() * CoastDrag);
        }

        // Speed cap
        if (speed > MaxSpeed)
            LinearVelocity = LinearVelocity.Normalized() * MaxSpeed;

        // ── Lateral grip ─────────────────────────────────────────────────────
        // Strips out the sideways component of velocity each frame.
        // High Grip (≥0.95) = moves exactly where it's pointed — classic isometric feel.
        float   gripBlend   = 1f - Mathf.Exp(-30f * dt);
        Vector2 forwardVel  = forward * forwardSpeed;
        Vector2 sidewaysVel = Transform.Y * LinearVelocity.Dot(Transform.Y);
        LinearVelocity = LinearVelocity.Lerp(forwardVel + sidewaysVel * (1f - Grip), gripBlend);
    }
}
