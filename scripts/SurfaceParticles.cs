using Godot;

/// <summary>
/// Three-tier surface effect system for the player vehicle:
///
///   1. Water ripple rings — sprite-based expanding ring tweens anchored to the
///      world (not the car), so they stay put as the car drives through puddles.
///      Each ring is spawned at the car's current position, scales up and fades out,
///      then is freed automatically.
///
///   2. Water spray — GpuParticles2D with a procedural teardrop texture, fanned
///      perpendicular to the car's velocity so it reads as water kicked up by wheels.
///
///   3. Oil shimmer — a Sprite2D with an inline animated canvas-item shader that
///      produces an iridescent rainbow disc rendered behind the car while it is on
///      an oil slick.  The hue rotates over time so it is unmistakable as oil.
///
/// Add as a child of PlayerCar (done programmatically in PlayerCar._Ready).
/// </summary>
public partial class SurfaceParticles : Node2D
{
    private SurfaceDetector _detector;
    private CharacterBody2D _car;
    private float           _time;

    // ── Water ripple state ─────────────────────────────────────────────────
    private Texture2D       _ringTex;
    private float           _rippleTimer;
    private const float     RippleInterval = 0.14f;   // seconds between ripple spawns

    // ── Spray particles ────────────────────────────────────────────────────
    private GpuParticles2D  _spray;

    // ── Oil shimmer ────────────────────────────────────────────────────────
    private Sprite2D _oilGlow;

    private const float WaterMinSpeed = 75f;

    // ══════════════════════════════════════════════════════════════════════════

    public override void _Ready()
    {
        _car      = GetParent<CharacterBody2D>();
        _detector = GetParent().GetNodeOrNull<SurfaceDetector>("SurfaceDetector");

        // Pre-bake reusable textures once
        _ringTex = _CreateRingTexture(128);

        _spray   = _BuildSprayParticles();
        _oilGlow = _BuildOilGlow();

        AddChild(_spray);
        AddChild(_oilGlow);
    }

    public override void _Process(double delta)
    {
        if (_car == null || _detector == null)
        {
            // Safety: keep effects off if detector isn't ready yet
            _spray.Emitting  = false;
            _oilGlow.Visible = false;
            return;
        }

        float dt    = (float)delta;
        float speed = _car.Velocity.Length();
        _time += dt;

        // ── 1. Water: ripple rings + spray ─────────────────────────────────
        bool onPuddle = _detector.IsOnPuddle && speed > WaterMinSpeed;
        _spray.Emitting = onPuddle;

        if (onPuddle)
        {
            // Fan spray perpendicular to travel direction
            var velDir = _car.Velocity.Normalized();
            _spray.Rotation = Mathf.Atan2(velDir.Y, velDir.X);

            _rippleTimer -= dt;
            if (_rippleTimer <= 0f)
            {
                _SpawnRipple(_car.GlobalPosition, speed);
                _rippleTimer = RippleInterval;
            }
        }
        else
        {
            _rippleTimer = 0f;
        }

        // ── 2. Oil shimmer ─────────────────────────────────────────────────
        bool onOil = _detector.IsOnOil;
        _oilGlow.Visible = onOil;
        if (onOil)
        {
            // Cycle through hues over ~4 s — rainbow iridescence with no shader.
            // Additive blend mode means it lights up the surface rather than
            // painting over it, giving a glowing oil-slick look.
            float hue = (_time * 0.25f) % 1.0f;
            _oilGlow.Modulate = Color.FromHsv(hue, 0.90f, 1.00f, 0.50f);
        }
    }

    // ── Ripple ring spawner ────────────────────────────────────────────────

    /// <summary>
    /// Spawns one ring at <paramref name="worldPos"/>, adds it directly to the
    /// scene root so it stays anchored in world space as the car moves away, then
    /// tweens it outward and fades it to nothing before freeing it.
    /// </summary>
    private void _SpawnRipple(Vector2 worldPos, float speed)
    {
        var ring = new Sprite2D
        {
            Texture        = _ringTex,
            GlobalPosition = worldPos,
            Scale          = Vector2.One * 0.12f,
            Modulate       = new Color(0.50f, 0.82f, 1.00f, 0.80f),
            ZIndex         = 0,
        };

        // Root the ring in the scene so it stays in world space
        GetTree().CurrentScene.AddChild(ring);

        // Scale grows from tiny → large; alpha fades from 0.8 → 0
        float maxScale = Mathf.Lerp(0.55f, 1.30f, Mathf.Clamp(speed / 420f, 0f, 1f));

        var tween = ring.CreateTween();
        tween.Parallel()
             .TweenProperty(ring, "scale", Vector2.One * maxScale, 0.70f)
             .SetTrans(Tween.TransitionType.Quart)
             .SetEase(Tween.EaseType.Out);
        tween.Parallel()
             .TweenProperty(ring, "modulate:a", 0f, 0.65f)
             .SetTrans(Tween.TransitionType.Cubic)
             .SetEase(Tween.EaseType.In);
        tween.TweenCallback(Callable.From(ring.QueueFree));
    }

    // ── Spray particle system ──────────────────────────────────────────────

    private GpuParticles2D _BuildSprayParticles()
    {
        var mat = new ParticleProcessMaterial
        {
            Direction          = new Vector3(0f, -1f, 0f),
            Spread             = 55f,
            InitialVelocityMin = 50f,
            InitialVelocityMax = 140f,
            Gravity            = new Vector3(0f, 95f, 0f),
            DampingMin         = 22f,
            DampingMax         = 45f,
            ScaleMin           = 0.8f,
            ScaleMax           = 2.5f,
        };

        // Blue droplets that fade to transparent over lifetime
        var grad = new Gradient();
        grad.SetColor(0, new Color(0.55f, 0.82f, 1.00f, 1.00f));
        grad.SetColor(1, new Color(0.72f, 0.92f, 1.00f, 0.00f));
        mat.ColorRamp = new GradientTexture1D { Gradient = grad };

        return new GpuParticles2D
        {
            Amount          = 16,
            Lifetime        = 0.42,
            Emitting        = false,
            ProcessMaterial = mat,
            Texture         = _CreateDropletTexture(14, 30),
        };
    }

    // ── Oil shimmer sprite ─────────────────────────────────────────────────

    private Sprite2D _BuildOilGlow()
    {
        // No shader — use a CanvasItemMaterial with additive blending instead.
        // The Modulate colour is cycled through the rainbow in _Process so it
        // reads as iridescent oil without risking any shader compilation fallback.
        var mat = new CanvasItemMaterial
        {
            BlendMode = CanvasItemMaterial.BlendModeEnum.Add,
        };

        return new Sprite2D
        {
            Texture  = _CreateCircleTexture(128),
            Material = mat,
            Scale    = Vector2.One * 1.2f,
            Visible  = false,
            ZIndex   = -1,    // render below car sprite
        };
    }

    // ── Procedural texture builders ────────────────────────────────────────

    /// <summary>
    /// Soft solid circle: alpha 1 at centre, 0 at edge.
    /// Used as the oil shimmer base sprite so the shader has a smooth mask.
    /// </summary>
    private static Texture2D _CreateCircleTexture(int size)
    {
        var img = Image.Create(size, size, false, Image.Format.Rgba8);
        float c = size * 0.5f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
            float a = 1f - Mathf.Clamp(d, 0f, 1f);
            img.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        return ImageTexture.CreateFromImage(img);
    }

    /// <summary>
    /// Circle ring (donut outline): opaque on the ring centre-line,
    /// feathered to transparent inward and outward.
    /// Used for expanding water ripples.
    /// </summary>
    private static Texture2D _CreateRingTexture(int size)
    {
        var img    = Image.Create(size, size, false, Image.Format.Rgba8);
        float c     = size * 0.5f;
        float rMid  = c * 0.80f;   // ring centre-line radius
        float thick = c * 0.16f;   // half-thickness of the ring

        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float d    = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
            float dist = Mathf.Abs(d - rMid);           // distance from ring centre-line
            float t    = 1f - Mathf.Clamp(dist / thick, 0f, 1f);
            t = t * t;                                   // quadratic softening
            img.SetPixel(x, y, new Color(1f, 1f, 1f, t));
        }
        return ImageTexture.CreateFromImage(img);
    }

    /// <summary>
    /// Teardrop / elongated water droplet: rounded at bottom, tapers to a point
    /// at the top.  Oriented vertically so it aligns with the particle direction.
    /// </summary>
    private static Texture2D _CreateDropletTexture(int w, int h)
    {
        var img = Image.Create(w, h, false, Image.Format.Rgba8);
        float cx = w * 0.5f;

        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            // Normalise to [-1, 1] in each axis
            float nx = (x - cx) / (w * 0.5f);
            float ny = (float)y / h;             // 0 = top (tip), 1 = bottom (round)

            // Horizontal radius widens toward the bottom — makes a teardrop shape
            float rx = 0.30f + ny * 0.70f;
            float a  = 1f - Mathf.Clamp(Mathf.Abs(nx) / rx, 0f, 1f);
            // Smooth the tip
            float tipFade = Mathf.Clamp(ny / 0.25f, 0f, 1f);
            img.SetPixel(x, y, new Color(1f, 1f, 1f, a * tipFade));
        }
        return ImageTexture.CreateFromImage(img);
    }
}
