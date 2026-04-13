using Godot;

public partial class GroundGrid : Node2D
{
    public static GroundGrid Instance { get; private set; }

    private static readonly Vector2 ArenaHalfSize = new(1200f, 900f);

    // ── Trail-map resolution ───────────────────────────────────────────────────
    // The full arena is 2400×1800. Running SubViewports at full resolution costs
    // ~33 MB VRAM and ~518 M pixels/s GPU fill @ 60 fps.
    // 512×384 (same 4:3 aspect) reduces that to ~1.5 MB and ~24 M pixels/s —
    // a ~22× improvement with no perceptible quality loss at typical game zoom.
    private const int   TrailMapWidth  = 512;
    private const int   TrailMapHeight = 384;
    // Uniform scale factor: viewport pixels per arena pixel
    private float _vpScale;

    private ShaderMaterial _mat;
    private Node2D         _car;
    private Camera2D       _camera;      // cached — GetCamera2D() traverses a list each call
    private float          _currentBrakeLight;
    private float          _time;

    private FastNoiseLite _puddleNoise;

    private SubViewport _trailViewport;
    private Sprite2D    _trailBrush;
    private SubViewport _depletionViewport;
    private Sprite2D    _depletionBrush;

    public override void _Ready()
    {
        Instance = this;

        var poly = GetNode<Polygon2D>("../Background");
        _mat = poly.Material as ShaderMaterial;
        _car = GetNodeOrNull<Node2D>("../PlayerCar");

        if (_mat == null) return;

        // _vpScale = viewport pixels per arena pixel (uniform — aspect ratios match)
        _vpScale = TrailMapWidth / (ArenaHalfSize.X * 2f);

        _mat.SetShaderParameter("puddle_noise_tex",  _BakeSimplex(512));
        _mat.SetShaderParameter("asphalt_noise_tex", _BakeCellular(512));
        _mat.SetShaderParameter("arena_half_size",   ArenaHalfSize);

        _SetupViewports();

        // Cache camera — avoids a list traversal every _Process frame
        _camera = GetViewport().GetCamera2D() as Camera2D;
    }

    private ImageTexture _BakeSimplex(int size)
    {
        _puddleNoise = new FastNoiseLite
        {
            NoiseType      = FastNoiseLite.NoiseTypeEnum.SimplexSmooth,
            Frequency      = 0.004f,
            FractalType    = FastNoiseLite.FractalTypeEnum.Fbm,
            FractalOctaves = 4,
        };
        return _NoiseToTexture(_puddleNoise, size);
    }

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
            float wx = (float)x / size;
            float wy = (float)y / size;
            wx = wx * wx * (3f - 2f * wx);
            wy = wy * wy * (3f - 2f * wy);

            float a = fn.GetNoise2D(x,        y)        * 0.5f + 0.5f;
            float b = fn.GetNoise2D(x - size, y)        * 0.5f + 0.5f;
            float c = fn.GetNoise2D(x,        y - size) * 0.5f + 0.5f;
            float d = fn.GetNoise2D(x - size, y - size) * 0.5f + 0.5f;

            float n = Mathf.Lerp(Mathf.Lerp(a, b, wx), Mathf.Lerp(c, d, wx), wy);
            img.SetPixel(x, y, new Color(n, n, n));
        }
        return ImageTexture.CreateFromImage(img);
    }

    private void _SetupViewports()
    {
        // ── 1. Temporary Wake Map ─────────────────────────────────────────────
        _trailViewport = new SubViewport
        {
            Size                   = new Vector2I(TrailMapWidth, TrailMapHeight),
            RenderTargetClearMode  = SubViewport.ClearMode.Never,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            TransparentBg          = false,
        };
        AddChild(_trailViewport);

        var root = new Node2D();
        _trailViewport.AddChild(root);

        // Full-viewport fade rect — sized to the viewport, not the arena
        root.AddChild(new ColorRect
        {
            Color    = new Color(0f, 0f, 0f, 0.006f),
            Size     = new Vector2(TrailMapWidth, TrailMapHeight),
            Position = Vector2.Zero,
        });

        _trailBrush = new Sprite2D
        {
            Texture  = _CreateBrushTexture(96),
            Position = ArenaHalfSize * _vpScale,   // center of the scaled viewport
        };
        root.AddChild(_trailBrush);

        // ── 2. Permanent Depletion Map ────────────────────────────────────────
        _depletionViewport = new SubViewport
        {
            Size                   = new Vector2I(TrailMapWidth, TrailMapHeight),
            RenderTargetClearMode  = SubViewport.ClearMode.Never,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            TransparentBg          = true,
        };
        AddChild(_depletionViewport);

        _depletionBrush = new Sprite2D
        {
            Texture  = _CreateBrushTexture(64),
            Position = ArenaHalfSize * _vpScale,
        };
        _depletionViewport.AddChild(_depletionBrush);

        // Pass texture references to shader ONCE here.
        // SubViewportTextures are live objects — their contents update automatically
        // every frame when UpdateMode = Always, so there is no need to re-call
        // SetShaderParameter("trail_map"/"depletion_map") in _Process.
        _mat.SetShaderParameter("trail_map",    _trailViewport.GetTexture());
        _mat.SetShaderParameter("depletion_map", _depletionViewport.GetTexture());
    }

    private static ImageTexture _CreateBrushTexture(int size)
    {
        var img = Image.Create(size, size, false, Image.Format.Rgba8);
        float c = size * 0.5f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
            float t = 1f - Mathf.Clamp(d / c, 0f, 1f);
            t = t * t;
            img.SetPixel(x, y, new Color(1, 1, 1, t));
        }
        return ImageTexture.CreateFromImage(img);
    }

    private void _FindPlayer()
    {
        var parent = GetParent();
        _car = parent?.GetNodeOrNull<Node2D>("PlayerCar")
            ?? parent?.GetNodeOrNull<Node2D>("PlayerBicycle")
            ?? parent?.GetNodeOrNull<Node2D>("Player");
    }

    public override void _Process(double delta)
    {
        if (_mat == null) return;

        float dt = (float)delta;

        // Wrap _time to prevent float precision loss after long sessions.
        // At 60 fps it would take ~38 days to overflow; wrapping at 1000 s
        // is imperceptible since the shader only uses it for slow animation.
        _time = (_time + dt) % 1000f;
        _mat.SetShaderParameter("game_time", _time);

        // trail_map and depletion_map are NOT re-sent here.
        // SubViewportTexture is a live reference — its contents update automatically
        // each frame; sending the same pointer again is pure overhead.

        if (!GodotObject.IsInstanceValid(_car))
            _FindPlayer();

        if (_car == null || !GodotObject.IsInstanceValid(_car)) return;

        if (GodotObject.IsInstanceValid(_trailBrush))
        {
            // Convert world position to viewport pixel coordinates
            Vector2 targetPos = (_car.GlobalPosition + ArenaHalfSize) * _vpScale;
            _trailBrush.Position    = targetPos;
            _depletionBrush.Position = targetPos;

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

            float speedScale      = Mathf.Clamp(speed / 300f, 0.25f, 0.85f);
            float driftMultiplier = Mathf.Lerp(1.0f, 1.6f, drift);
            // Scale by _vpScale so the brush covers the same arena area regardless
            // of viewport resolution
            float targetScale     = speedScale * driftMultiplier * _vpScale;

            _trailBrush.Scale    = Vector2.One * targetScale;
            _depletionBrush.Scale = Vector2.One * targetScale;

            float violence = Mathf.Clamp(speed / 150f, 0.1f, 1.0f);
            _trailBrush.Modulate    = new Color(1f, 1f, 1f, violence);
            _depletionBrush.Modulate = new Color(1f, 1f, 1f, violence * 0.05f);
        }

        var carScript = _car as PlayerCar;
        if (carScript == null) return;

        float targetBrake = carScript.IsBraking ? 1f : 0f;
        _currentBrakeLight = Mathf.Lerp(_currentBrakeLight, targetBrake, 15f * dt);
        _mat.SetShaderParameter("brake_strength", _currentBrakeLight);

        // Use cached camera reference — GetCamera2D() traverses a list every call
        if (_camera == null || !GodotObject.IsInstanceValid(_camera))
            _camera = GetViewport().GetCamera2D() as Camera2D;

        if (_camera != null)
            _mat.SetShaderParameter("screen_center", _camera.GlobalPosition);
    }

    public float GetOilIntensity(Vector2 worldPos)
    {
        var cell = new Vector2(
            Mathf.Floor(worldPos.X * 0.00115f + 77.3f),
            Mathf.Floor(worldPos.Y * 0.00115f + 91.1f));
        float cellHash = _Hash21(cell);
        if (cellHash < 0.82f) return 0f;

        float oilN = _puddleNoise != null
            ? _puddleNoise.GetNoise2D(worldPos.X * 0.9216f + 281.6f,
                                       worldPos.Y * 0.9216f + 168.9f) * 0.5f + 0.5f
            : 0.63f;
        return Mathf.SmoothStep(0.55f, 0.72f, oilN)
             * Mathf.SmoothStep(0.82f, 0.88f, cellHash);
    }

    public float GetPuddleIntensity(Vector2 worldPos)
    {
        if (_puddleNoise == null) return 0f;
        float n1 = _puddleNoise.GetNoise2D(worldPos.X * 0.8704f + 153.6f,
                                            worldPos.Y * 0.8704f + 358.4f) * 0.5f + 0.5f;
        float n2 = _puddleNoise.GetNoise2D(worldPos.X * 2.1504f + 358.4f,
                                            worldPos.Y * 2.1504f + 102.4f) * 0.5f + 0.5f;
        return Mathf.SmoothStep(0.65f, 0.80f, n1 * 0.65f + n2 * 0.35f);
    }

    private static float _Hash21(Vector2 p)
    {
        float px = _Fract(p.X * 123.34f);
        float py = _Fract(p.Y * 456.21f);
        float d  = px * (px + 45.32f) + py * (py + 45.32f);
        return _Fract((px + d) * (py + d));
    }

    private static float _Fract(float x) => x - Mathf.Floor(x);
}
