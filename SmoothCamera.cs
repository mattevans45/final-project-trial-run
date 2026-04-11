using Godot;

/// <summary>
/// GTA 1/2-style top-down follow camera.
/// Always faces north (no rotation). Tight follow with velocity lookahead.
/// Subtle speed-driven zoom-out so you can see where you're going at high speed.
/// </summary>
public partial class SmoothCamera : Camera2D
{
    [Export] public NodePath TargetPath;

    [ExportGroup("Follow")]
    /// <summary>How fast the camera tracks the car. Higher = tighter.</summary>
    [Export] public float FollowSharpness = 8f;

    [ExportGroup("Lookahead")]
    [Export] public bool UseLookahead = true;
    /// <summary>World-units the camera pans ahead at full speed.</summary>
    [Export] public float LookaheadMax = 70f;
    /// <summary>Speed at which lookahead reaches its maximum.</summary>
    [Export] public float LookaheadFullSpeed = 480f;
    /// <summary>How snappily the lookahead offset tracks.</summary>
    [Export] public float LookaheadSharpness = 4f;

    [ExportGroup("Zoom")]
    [Export] public bool UseDynamicZoom = false;
    /// <summary>Fixed zoom for a stable isometric-style view. 1.5 shows a good play area.</summary>
    [Export] public float ZoomIdle = 1.5f;
    /// <summary>Zoom at top speed (only used when UseDynamicZoom is true).</summary>
    [Export] public float ZoomFast = 1.2f;
    [Export] public float ZoomFullSpeed = 480f;
    [Export] public float ZoomSharpness = 2f;

    private Node2D _target;
    private RigidBody2D _targetRb;
    private Vector2 _lookaheadOffset;
    private Vector2 _smoothedZoom;

    public override void _Ready()
    {
        // Decouple from any rotating parent (e.g. when embedded inside a vehicle scene).
        // GlobalPosition is then set in pure world space; the view never spins.
        TopLevel = true;

        if (TargetPath != null && !TargetPath.IsEmpty)
            _target = GetNodeOrNull<Node2D>(TargetPath);

        _targetRb = _target as RigidBody2D;

        if (_target != null)
            GlobalPosition = _target.GlobalPosition;

        _smoothedZoom = new Vector2(ZoomIdle, ZoomIdle);
        Zoom = _smoothedZoom;
        MakeCurrent();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_target == null)
            return;

        float dt = (float)delta;
        Vector2 vel = _targetRb != null ? _targetRb.LinearVelocity : Vector2.Zero;
        float speed = vel.Length();

        // --- Lookahead ---
        Vector2 desiredLookahead = Vector2.Zero;
        if (UseLookahead && speed > 18f)
        {
            // Ease-in curve: feels natural without snapping at low speed
            float t = Mathf.Clamp(speed / LookaheadFullSpeed, 0f, 1f);
            t = t * t;
            desiredLookahead = vel.Normalized() * LookaheadMax * t;
        }

        float lookT = 1f - Mathf.Exp(-LookaheadSharpness * dt);
        _lookaheadOffset = _lookaheadOffset.Lerp(desiredLookahead, lookT);

        // --- Position follow ---
        Vector2 focus = _target.GlobalPosition + _lookaheadOffset;
        float followT = 1f - Mathf.Exp(-FollowSharpness * dt);
        GlobalPosition = GlobalPosition.Lerp(focus, followT);

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
