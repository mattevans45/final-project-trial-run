using Godot;

/// <summary>
/// GTA 1/2-style top-down follow camera.
/// Always faces north (no rotation). Tight follow with velocity lookahead.
/// Heading-blended lookahead stays stable during drifts.
/// </summary>
public partial class SmoothCamera : Camera2D
{
    [Export] public NodePath TargetPath;

    [ExportGroup("Follow")]
    [Export] public float FollowSharpness = 8f;

    [ExportGroup("Lookahead")]
    [Export] public bool UseLookahead = true;
    [Export] public float LookaheadMax = 70f;
    [Export] public float LookaheadFullSpeed = 480f;
    [Export] public float LookaheadSharpness = 4f;
    /// <summary>
    /// Blend between velocity direction (0) and car heading (1) for lookahead.
    /// Higher values keep the camera steadier during drifts.
    /// </summary>
    [Export(PropertyHint.Range, "0,1,0.05")]
    public float LookaheadHeadingBlend = 0.5f;

    [ExportGroup("Zoom")]
    [Export] public bool UseDynamicZoom = false;
    [Export] public float ZoomIdle = 1.5f;
    [Export] public float ZoomFast = 1.2f;
    [Export] public float ZoomFullSpeed = 480f;
    [Export] public float ZoomSharpness = 2f;

    [ExportGroup("Shake")]
    [Export] public float MaxShakeOffset = 10f;
    [Export] public float ShakeDecayRate = 4f;

    private Node2D _target;
    private RigidBody2D _targetRb;
    private CharacterBody2D _targetCb;
    private Vector2 _lookaheadOffset;
    private Vector2 _smoothedZoom;
    private Vector2 _lastTargetPos;
    private Vector2 _smoothPos;
    private float _trauma;
    private bool _firstFrame = true;

    /// <summary>Add camera trauma (0–1). Stacks up to 1. Shake = trauma².</summary>
    public void AddTrauma(float amount) => _trauma = Mathf.Min(_trauma + amount, 1f);

    public override void _Ready()
    {
        TopLevel = true;

        if (TargetPath != null && !TargetPath.IsEmpty)
            _target = GetNodeOrNull<Node2D>(TargetPath);

        _targetRb = _target as RigidBody2D;
        _targetCb = _target as CharacterBody2D;

        // Snap immediately so the very first render frame is at the target,
        // not at (0,0). The _firstFrame guard in _PhysicsProcess re-snaps in
        // case the target wasn't positioned yet during _Ready.
        if (_target != null)
        {
            GlobalPosition = _target.GlobalPosition;
            _smoothPos = _target.GlobalPosition;
            _lastTargetPos = _target.GlobalPosition;
        }

        _smoothedZoom = new Vector2(ZoomIdle, ZoomIdle);
        Zoom = _smoothedZoom;
        MakeCurrent();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_target == null)
            return;

        // Hard-snap on first frame so there's never a lerp from (0,0)
        if (_firstFrame)
        {
            _smoothPos = _target.GlobalPosition;
            GlobalPosition = _target.GlobalPosition;
            _lastTargetPos = _target.GlobalPosition;
            _firstFrame = false;
            return;
        }

        float dt = (float)delta;

        Vector2 vel;
        if (_targetRb != null)
            vel = _targetRb.LinearVelocity;
        else if (_targetCb != null)
            vel = _targetCb.Velocity;
        else
            vel = dt > 0f ? (_target.GlobalPosition - _lastTargetPos) / dt : Vector2.Zero;

        _lastTargetPos = _target.GlobalPosition;
        float speed = vel.Length();

        // --- Lookahead (heading-blended for drift stability) ---
        Vector2 desiredLookahead = Vector2.Zero;
        if (UseLookahead && speed > 18f)
        {
            float t = Mathf.Clamp(speed / LookaheadFullSpeed, 0f, 1f);
            t *= t; // ease-in

            // Blend velocity direction with car's facing direction
            Vector2 velDir = vel.Normalized();
            float rot = _target.GlobalRotation;
            Vector2 headingDir = new Vector2(Mathf.Cos(rot), Mathf.Sin(rot));
            Vector2 lookDir = velDir.Lerp(headingDir, LookaheadHeadingBlend).Normalized();

            desiredLookahead = lookDir * LookaheadMax * t;
        }

        float lookT = 1f - Mathf.Exp(-LookaheadSharpness * dt);
        _lookaheadOffset = _lookaheadOffset.Lerp(desiredLookahead, lookT);

        // --- Position follow ---
        Vector2 focus = _target.GlobalPosition + _lookaheadOffset;
        float followT = 1f - Mathf.Exp(-FollowSharpness * dt);
        _smoothPos = _smoothPos.Lerp(focus, followT);

        // --- Shake ---
        Vector2 shakeOffset = Vector2.Zero;
        if (_trauma > 0.001f)
        {
            float shake = _trauma * _trauma;
            shakeOffset = new Vector2(
                GD.Randf() * 2f - 1f,
                GD.Randf() * 2f - 1f
            ) * MaxShakeOffset * shake;
            _trauma = Mathf.MoveToward(_trauma, 0f, ShakeDecayRate * dt);
        }
        GlobalPosition = _smoothPos + shakeOffset;

        // --- Zoom ---
        if (UseDynamicZoom)
        {
            float zt = Mathf.Clamp(speed / ZoomFullSpeed, 0f, 1f);
            zt = zt * zt * (3f - 2f * zt); // smoothstep
            float targetZoom = Mathf.Lerp(ZoomIdle, ZoomFast, zt);
            float zT = 1f - Mathf.Exp(-ZoomSharpness * dt);
            _smoothedZoom = _smoothedZoom.Lerp(new Vector2(targetZoom, targetZoom), zT);
            Zoom = _smoothedZoom;
        }
    }
}