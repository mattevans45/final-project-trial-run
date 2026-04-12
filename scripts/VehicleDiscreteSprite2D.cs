using Godot;

/// <summary>
/// 8-directional vehicle sprite — classic arcade / isometric style.
/// The pre-rendered art already encodes each world direction, so we cancel
/// the inherited parent rotation and just snap the animation to the nearest octant.
///
/// SpriteFrames must contain animations named:
///   EAST  SOUTHEAST  SOUTH  SOUTHWEST  WEST  NORTHWEST  NORTH  NORTHEAST
/// </summary>
public partial class VehicleDiscreteSprite2D : AnimatedSprite2D
{
    [ExportGroup("Vehicle")]
    [Export] public NodePath VehiclePath;

    [ExportGroup("Animation")]
    [Export] public float AnimationFps      = 12f;
    [Export] public float MinSpeedToAnimate = 30f;

    [ExportGroup("Static Prop")]
    /// <summary>Pin to one direction (0=E 1=SE 2=S 3=SW 4=W 5=NW 6=N 7=NE). -1 = rotation-driven.</summary>
    [Export] public int FixedDirectionIndex = -1;

    // Clockwise from East — matches Godot 2D rotation (+Y down = South).
    private static readonly StringName[] AnimNames =
    {
        "EAST", "SOUTHEAST", "SOUTH", "SOUTHWEST",
        "WEST", "NORTHWEST", "NORTH", "NORTHEAST"
    };

    private RigidBody2D _vehicle;

    public override void _Ready()
    {
        _vehicle = (VehiclePath != null && !VehiclePath.IsEmpty)
            ? GetNodeOrNull<RigidBody2D>(VehiclePath)
            : GetParent() as RigidBody2D;

        if (SpriteFrames != null)
        {
            SpriteFrames = (SpriteFrames)SpriteFrames.Duplicate();
            foreach (var anim in AnimNames)
                if (SpriteFrames.HasAnimation(anim))
                    SpriteFrames.SetAnimationSpeed(anim, AnimationFps);
        }

        int startDir = FixedDirectionIndex >= 0 ? Mathf.Clamp(FixedDirectionIndex, 0, 7) : 0;
        SwitchAnimation(AnimNames[startDir]);
        if (FixedDirectionIndex >= 0) { Pause(); Frame = 0; }

        GlobalRotation = 0f;
    }

    public override void _PhysicsProcess(double delta)
    {
        // The art is pre-rendered at world-space canonical angles.
        // Cancel the inherited parent rotation so the art renders pixel-perfect.
        GlobalRotation = 0f;

        // ── Static prop ───────────────────────────────────────────────────────
        if (FixedDirectionIndex >= 0)
        {
            SwitchAnimation(AnimNames[Mathf.Clamp(FixedDirectionIndex, 0, 7)]);
            if (IsPlaying()) { Pause(); Frame = 0; }
            return;
        }

        // ── Direction (snap to nearest octant) ────────────────────────────────
        float h      = Mathf.PosMod(_vehicle?.GlobalRotation ?? 0f, Mathf.Tau);
        int   dirIdx = Mathf.PosMod(Mathf.RoundToInt(h / (Mathf.Tau / 8f)), 8);
        SwitchAnimation(AnimNames[dirIdx]);

        // ── Playback ─────────────────────────────────────────────────────────
        float speed = _vehicle?.LinearVelocity.Length() ?? 0f;
        if (speed < MinSpeedToAnimate)
        {
            if (IsPlaying()) { Pause(); Frame = 0; }
        }
        else if (!IsPlaying())
        {
            Play(AnimNames[dirIdx]);
        }
    }

    private void SwitchAnimation(StringName animName)
    {
        if (Animation == animName) return;
        int saved = Frame;
        Play(animName);
        Frame = saved;
    }
}
