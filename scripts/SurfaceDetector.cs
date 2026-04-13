using Godot;

/// <summary>
/// Samples the arena ground for oil-slick and puddle presence at the parent
/// node's world position.  Relies on GroundGrid.Instance which is set during
/// scene load — always attach GroundGrid to the scene before the player.
///
/// Drop as a child of PlayerCar or PlayerBicycle.
/// Other scripts (PlayerCar, SurfaceParticles) read the public properties.
/// </summary>
public partial class SurfaceDetector : Node
{
    // ── Smoothed intensities (0–1) ─────────────────────────────────────────
    public float OilIntensity    { get; private set; }
    public float PuddleIntensity { get; private set; }

    // ── Boolean shortcuts ──────────────────────────────────────────────────
    public bool IsOnOil    => OilIntensity    > 0.12f;
    public bool IsOnPuddle => PuddleIntensity > 0.12f;

    private Node2D _parent;

    // Smoothing speeds (higher = snappier response)
    private const float OilSmooth    = 6f;
    private const float PuddleSmooth = 4f;

    public override void _Ready()
    {
        _parent = GetParent<Node2D>();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_parent == null || GroundGrid.Instance == null) return;

        float dt  = (float)delta;
        var   pos = _parent.GlobalPosition;

        float targetOil    = GroundGrid.Instance.GetOilIntensity(pos);
        float targetPuddle = GroundGrid.Instance.GetPuddleIntensity(pos);

        OilIntensity    = Mathf.Lerp(OilIntensity,    targetOil,    OilSmooth    * dt);
        PuddleIntensity = Mathf.Lerp(PuddleIntensity, targetPuddle, PuddleSmooth * dt);
    }
}
