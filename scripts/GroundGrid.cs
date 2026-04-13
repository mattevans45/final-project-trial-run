using Godot;

/// <summary>
/// Drives the ground_grid shader each frame and owns the two subsystems
/// described in the "Upgrade" notes:
///
///   1. Pre-baked noise textures
///      FastNoiseLite runs on the CPU once at startup to produce two
///      ImageTextures (simplex → puddle shapes, cellular → cracks/asphalt
///      detail). The GPU only ever does a cheap texture() lookup — no trig
///      noise math in the fragment shader.
///
///   2. Trail splat map (SubViewport)
///      A hidden 2400×1800 SubViewport accumulates every car's path as a
///      soft white brush stroke. A faint black ColorRect fades the content
///      each frame so trails disappear after ~8 s. The resulting
///      ViewportTexture is fed to the shader as "trail_map": the shader
///      reads it to displace standing water wherever a car drove.
///      Works automatically for any number of cars — they all paint into
///      the same viewport without any shader changes.
/// </summary>
public partial class GroundGrid : Node2D
{
    // ── Singleton ─────────────────────────────────────────────────────────────
    /// <summary>Live reference set in _Ready. Used by SurfaceDetector.</summary>
    public static GroundGrid Instance { get; private set; }

    // ── Arena dimensions must match ArenaLevel export values ─────────────────
    private static readonly Vector2 ArenaHalfSize = new(1200f, 900f);

    private ShaderMaterial _mat;
    private Node2D         _car;
    private float          _currentBrakeLight;
    private float          _time;

    // Stored so SurfaceDetector can query puddle/oil intensity at runtime
    private FastNoiseLite _puddleNoise;

    // Trail splat map nodes
    private SubViewport _trailViewport;
    private Sprite2D    _trailBrush;

    // ── Startup ──────────────────────────────────────────────────────────────

    public override void _Ready()
    {
        Instance = this;

        var poly = GetNode<Polygon2D>("../Background");
        _mat = poly.Material as ShaderMaterial;

        _car = GetNodeOrNull<Node2D>("../PlayerCar");

        if (_mat == null) return;

        // Bake noise on the CPU. Each call takes < 30 ms for a 512×512 texture.
        _mat.SetShaderParameter("puddle_noise_tex",  _BakeSimplex(512));
        _mat.SetShaderParameter("asphalt_noise_tex", _BakeCellular(512));
        _mat.SetShaderParameter("arena_half_size",   ArenaHalfSize);

        _SetupTrailViewport();
    }

    // ── Noise texture baking ─────────────────────────────────────────────────

    /// <summary>Smooth simplex: soft organic blobs, ideal for puddle shapes.</summary>
    private ImageTexture _BakeSimplex(int size)
    {
        _puddleNoise = new FastNoiseLite
        {
            NoiseType      = FastNoiseLite.NoiseTypeEnum.SimplexSmooth,
            Frequency      = 0.004f,
            FractalOctaves = 4,
        };
        return _NoiseToTexture(_puddleNoise, size);
    }

    /// <summary>Cellular distance: produces crack/pore patterns for asphalt detail.</summary>
    private static ImageTexture _BakeCellular(int size)
    {
        var fn = new FastNoiseLite
        {
            NoiseType          = FastNoiseLite.NoiseTypeEnum.Cellular,
            Frequency          = 0.012f,
            CellularReturnType = FastNoiseLite.CellularReturnTypeEnum.Distance,
        };
        return _NoiseToTexture(fn, size);
    }

    private static ImageTexture _NoiseToTexture(FastNoiseLite fn, int size)
    {
        var img = Image.Create(size, size, false, Image.Format.Rgba8);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            // 4-corner bilinear blend: each pixel is interpolated between four
            // overlapping copies of the noise, so the left edge matches the right
            // edge and the top matches the bottom — fully seamless tiling.
            float wx = (float)x / size;
            float wy = (float)y / size;
            // Smoothstep weights for softer crossfade at the wrap boundary
            wx = wx * wx * (3f - 2f * wx);
            wy = wy * wy * (3f - 2f * wy);

            float a = fn.GetNoise2D(x,        y)        * 0.5f + 0.5f;  // top-left
            float b = fn.GetNoise2D(x - size, y)        * 0.5f + 0.5f;  // top-right
            float c = fn.GetNoise2D(x,        y - size) * 0.5f + 0.5f;  // bottom-left
            float d = fn.GetNoise2D(x - size, y - size) * 0.5f + 0.5f;  // bottom-right

            float n = Mathf.Lerp(Mathf.Lerp(a, b, wx), Mathf.Lerp(c, d, wx), wy);
            img.SetPixel(x, y, new Color(n, n, n));
        }
        return ImageTexture.CreateFromImage(img);
    }

    // ── Trail splat map ──────────────────────────────────────────────────────

    private void _SetupTrailViewport()
    {
        int w = (int)(ArenaHalfSize.X * 2);   // 2400
        int h = (int)(ArenaHalfSize.Y * 2);   // 1800

        // SubViewport accumulates car trails — never cleared between frames.
        _trailViewport = new SubViewport
        {
            Size                   = new Vector2I(w, h),
            RenderTargetClearMode  = SubViewport.ClearMode.Never,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            TransparentBg          = false,
        };
        AddChild(_trailViewport);

        var root = new Node2D();
        _trailViewport.AddChild(root);

        // Fade rect: drawn every frame with near-zero alpha.
        // With Mix blend mode: new = existing × (1 − 0.006), so trails decay
        // to ~70 % after 1 s, ~17 % after 3 s, effectively gone after 5–8 s.
        var fade = new ColorRect
        {
            Color    = new Color(0f, 0f, 0f, 0.006f),
            Size     = new Vector2(w, h),
            Position = Vector2.Zero,
        };
        root.AddChild(fade);

        // Brush: soft white gradient circle stamped where the car is.
        _trailBrush = new Sprite2D
        {
            Texture  = _CreateBrushTexture(96),
            Position = ArenaHalfSize,      // start at viewport centre
        };
        root.AddChild(_trailBrush);

        // Hand the SubViewport's live texture to the ground shader.
        _mat.SetShaderParameter("trail_map", _trailViewport.GetTexture());
    }

    /// <summary>
    /// Procedural soft circle: alpha = 1 at centre, 0 at edge (quadratic).
    /// Generates a permanent brush from trail strokes.
    /// </summary>
    private static ImageTexture _CreateBrushTexture(int size)
    {
        var img = Image.Create(size, size, false, Image.Format.Rgba8);
        float c = size * 0.5f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
            float t = 1f - Mathf.Clamp(d / c, 0f, 1f);
            t = t * t;                                  // quadratic — soft edge
            img.SetPixel(x, y, new Color(1, 1, 1, t));
        }
        return ImageTexture.CreateFromImage(img);
    }

    // ── Player discovery ─────────────────────────────────────────────────────

    private void _FindPlayer()
    {
        var parent = GetParent();
        _car = parent?.GetNodeOrNull<Node2D>("PlayerCar")
            ?? parent?.GetNodeOrNull<Node2D>("PlayerBicycle")
            ?? parent?.GetNodeOrNull<Node2D>("Player");
    }

    // ── Per-frame update ─────────────────────────────────────────────────────

    public override void _Process(double delta)
    {
        if (_mat == null) return;

        float dt = (float)delta;
        _time += dt;
        _mat.SetShaderParameter("game_time", _time);

        // Re-set the trail texture every frame so the shader always sees the
        // latest SubViewport content (ViewportTexture is a live reference but
        // re-assigning guards against edge cases during scene transitions).
        if (_trailViewport != null)
            _mat.SetShaderParameter("trail_map", _trailViewport.GetTexture());

        if (!GodotObject.IsInstanceValid(_car))
            _FindPlayer();

        if (_car == null || !GodotObject.IsInstanceValid(_car)) return;

        // ── Trail brush: world → viewport coordinate conversion ───────────────
        // The viewport is 2400×1800 with its origin at the top-left.
        // Arena world (0,0) maps to viewport pixel (1200, 900).
        if (GodotObject.IsInstanceValid(_trailBrush))
        {
            _trailBrush.Position = _car.GlobalPosition + ArenaHalfSize;

            // Derive speed and drift from whatever controller is active
            float speed = 0f;
            float drift = 0f;
            if (_car is PlayerCar pc)
            {
                speed = pc.TotalSpeed;
                drift = pc.DriftIntensity;
            }
            else if (_car is CharacterBody2D cb)
            {
                speed = cb.Velocity.Length();
            }

            // Brush widens during drift; stays slightly visible even at rest
            // so stopped cars still leave a faint mark on puddles.
            _trailBrush.Scale    = Vector2.One * Mathf.Lerp(0.7f, 1.6f, drift);
            _trailBrush.Modulate = new Color(1f, 1f, 1f, Mathf.Clamp(speed / 40f + 0.15f, 0f, 1f));
        }

        // ── Ground shader dynamics ────────────────────────────────────────────
        // car_pos / car_speed / car_drift / velocity_dir uniforms were removed
        // when the radar-ring ripple effect was cut. Only brake_strength and
        // screen_center remain as per-frame PlayerCar-specific pushes.
        var carScript = _car as PlayerCar;
        if (carScript == null) return;

        float targetBrake = carScript.IsBraking ? 1f : 0f;
        _currentBrakeLight = Mathf.Lerp(_currentBrakeLight, targetBrake, 15f * dt);
        _mat.SetShaderParameter("brake_strength", _currentBrakeLight);

        var cam = GetViewport().GetCamera2D();
        if (cam != null)
            _mat.SetShaderParameter("screen_center", cam.GlobalPosition);
    }

    // ── Surface queries (used by SurfaceDetector) ─────────────────────────────

    /// <summary>
    /// Returns 0–1 oil-slick intensity at <paramref name="worldPos"/> by
    /// replicating the shader's hash21 + noise cell test.
    /// </summary>
    public float GetOilIntensity(Vector2 worldPos)
    {
        var cell = new Vector2(
            Mathf.Floor(worldPos.X * 0.00115f + 77.3f),
            Mathf.Floor(worldPos.Y * 0.00115f + 91.1f));
        float cellHash = _Hash21(cell);
        if (cellHash < 0.82f) return 0f;          // not an oil cell at all

        // Shader UV: pos * 0.0018 → pixel coord: worldPos * (0.0018 * 512) = worldPos * 0.9216
        float oilN = _puddleNoise != null
            ? _puddleNoise.GetNoise2D(worldPos.X * 0.9216f + 281.6f,
                                       worldPos.Y * 0.9216f + 168.9f) * 0.5f + 0.5f
            : 0.63f;
        return Mathf.SmoothStep(0.55f, 0.72f, oilN)
             * Mathf.SmoothStep(0.82f, 0.88f, cellHash);
    }

    /// <summary>
    /// Returns 0–1 puddle intensity at <paramref name="worldPos"/> using the
    /// same two-octave simplex blend as the shader (no domain warp, close enough
    /// for gameplay hit-detection).
    /// </summary>
    public float GetPuddleIntensity(Vector2 worldPos)
    {
        if (_puddleNoise == null) return 0f;
        // Shader UV: pos * 0.00170 → pixel coord: worldPos * (0.00170 * 512) = worldPos * 0.8704
        // Shader UV: pos * 0.00420 → pixel coord: worldPos * (0.00420 * 512) = worldPos * 2.1504
        // Offsets are UV-space offsets (e.g. vec2(0.30,0.70)) scaled to pixels (×512)
        float n1 = _puddleNoise.GetNoise2D(worldPos.X * 0.8704f + 153.6f,
                                            worldPos.Y * 0.8704f + 358.4f) * 0.5f + 0.5f;
        float n2 = _puddleNoise.GetNoise2D(worldPos.X * 2.1504f + 358.4f,
                                            worldPos.Y * 2.1504f + 102.4f) * 0.5f + 0.5f;
        return Mathf.SmoothStep(0.65f, 0.80f, n1 * 0.65f + n2 * 0.35f);
    }

    // ── Hash helpers ─────────────────────────────────────────────────────────

    private static float _Hash21(Vector2 p)
    {
        float px = _Fract(p.X * 123.34f);
        float py = _Fract(p.Y * 456.21f);
        float d  = px * (px + 45.32f) + py * (py + 45.32f);
        return _Fract((px + d) * (py + d));
    }

    private static float _Fract(float x) => x - Mathf.Floor(x);
}
