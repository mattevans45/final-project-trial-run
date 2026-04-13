using Godot;

/// <summary>
/// Manages two GpuParticles2D systems attached to the player vehicle:
///
///   Water splash — blue droplets sprayed sideways when crossing puddles at speed.
///   Oil shimmer  — slowly drifting rainbow sparkles while over an oil slick.
///
/// Drop as a child of PlayerCar or PlayerBicycle (added programmatically in
/// PlayerCar._Ready so no scene edits are required).
/// </summary>
public partial class SurfaceParticles : Node2D
{
    private SurfaceDetector _detector;
    private CharacterBody2D _car;

    private GpuParticles2D _water;
    private GpuParticles2D _oil;

    // Speed threshold (px/s) below which water splash suppressed
    private const float WaterMinSpeed = 80f;

    public override void _Ready()
    {
        _car      = GetParent<CharacterBody2D>();
        _detector = GetParent().GetNodeOrNull<SurfaceDetector>("SurfaceDetector");

        _water = _BuildWaterParticles();
        _oil   = _BuildOilParticles();

        AddChild(_water);
        AddChild(_oil);
    }

    public override void _Process(double delta)
    {
        if (_car == null || _detector == null) return;

        float speed = _car.Velocity.Length();

        // ── Water splash ───────────────────────────────────────────────────
        bool wantWater = _detector.IsOnPuddle && speed > WaterMinSpeed;
        if (_water.Emitting != wantWater)
            _water.Emitting = wantWater;

        if (wantWater && speed > 1f)
        {
            // Rotate emitter so spray fans out perpendicular to travel direction
            var velDir = _car.Velocity.Normalized();
            _water.Rotation = Mathf.Atan2(velDir.Y, velDir.X);
        }

        // ── Oil shimmer ────────────────────────────────────────────────────
        bool wantOil = _detector.IsOnOil;
        if (_oil.Emitting != wantOil)
            _oil.Emitting = wantOil;
    }

    // ── Factory helpers ───────────────────────────────────────────────────

    private static GpuParticles2D _BuildWaterParticles()
    {
        var mat = new ParticleProcessMaterial
        {
            Direction          = new Vector3(0f, -1f, 0f),
            Spread             = 65f,
            InitialVelocityMin = 40f,
            InitialVelocityMax = 120f,
            Gravity            = new Vector3(0f, 100f, 0f),
            DampingMin         = 18f,
            DampingMax         = 38f,
            ScaleMin           = 1.2f,
            ScaleMax           = 3.0f,
        };

        // Blue droplets that fade out
        var grad = new Gradient();
        grad.SetColor(0, new Color(0.45f, 0.72f, 1.00f, 0.90f));
        grad.SetColor(1, new Color(0.60f, 0.85f, 1.00f, 0.00f));
        mat.ColorRamp = new GradientTexture1D { Gradient = grad };

        return new GpuParticles2D
        {
            Amount          = 16,
            Lifetime        = 0.45,
            Emitting        = false,
            ProcessMaterial = mat,
        };
    }

    private static GpuParticles2D _BuildOilParticles()
    {
        var mat = new ParticleProcessMaterial
        {
            Direction          = new Vector3(0f, -1f, 0f),
            Spread             = 180f,                       // full circle
            InitialVelocityMin = 5f,
            InitialVelocityMax = 20f,
            Gravity            = new Vector3(0f, -6f, 0f),  // float gently upward
            DampingMin         = 3f,
            DampingMax         = 8f,
            ScaleMin           = 1.0f,
            ScaleMax           = 2.2f,
        };

        // Rainbow gradient cycling magenta → orange → green → cyan → violet → fade
        var grad = new Gradient();
        grad.Colors  = new Color[]
        {
            new Color(1.0f, 0.0f, 0.55f, 0.95f),  // magenta
            new Color(1.0f, 0.5f, 0.0f,  0.85f),  // orange
            new Color(0.2f, 1.0f, 0.2f,  0.85f),  // green
            new Color(0.0f, 0.8f, 1.0f,  0.85f),  // cyan
            new Color(0.55f,0.0f, 1.0f,  0.90f),  // violet
            new Color(1.0f, 0.0f, 0.55f, 0.0f),   // fade out
        };
        grad.Offsets = new float[] { 0f, 0.2f, 0.4f, 0.6f, 0.8f, 1f };
        mat.ColorRamp = new GradientTexture1D { Gradient = grad };

        return new GpuParticles2D
        {
            Amount          = 10,
            Lifetime        = 1.2,
            Emitting        = false,
            ProcessMaterial = mat,
        };
    }
}
