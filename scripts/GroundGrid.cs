using Godot;

public partial class GroundGrid : Node2D
{
    public static GroundGrid Instance { get; private set; }

    private static readonly Vector2 ArenaHalfSize = new(1200f, 900f);

    private ShaderMaterial _mat;
    private Node2D         _car;
    private float          _currentBrakeLight;
    private float          _time;

    private FastNoiseLite _puddleNoise;

    // Trail splat map nodes (Temporary Wake)
    private SubViewport _trailViewport;
    private Sprite2D    _trailBrush;

    // Depletion map nodes (Permanent Dryness)
    private SubViewport _depletionViewport;
    private Sprite2D    _depletionBrush;

    public override void _Ready()
    {
        Instance = this;

        var poly = GetNode<Polygon2D>("../Background");
        _mat = poly.Material as ShaderMaterial;
        _car = GetNodeOrNull<Node2D>("../PlayerCar");

        if (_mat == null) return;

        _mat.SetShaderParameter("puddle_noise_tex",  _BakeSimplex(512));
        _mat.SetShaderParameter("asphalt_noise_tex", _BakeCellular(512));
        _mat.SetShaderParameter("arena_half_size",   ArenaHalfSize);

        _SetupViewports();
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
        int w = (int)(ArenaHalfSize.X * 2);   
        int h = (int)(ArenaHalfSize.Y * 2);   

        // 1. Temporary Wake Map
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

        var fade = new ColorRect
        {
            Color    = new Color(0f, 0f, 0f, 0.006f),
            Size     = new Vector2(w, h),
            Position = Vector2.Zero,
        };
        root.AddChild(fade);

        _trailBrush = new Sprite2D
        {
            Texture  = _CreateBrushTexture(96),
            Position = ArenaHalfSize,      
        };
        root.AddChild(_trailBrush);

        // 2. Permanent Depletion Map
        _depletionViewport = new SubViewport
        {
            Size                   = new Vector2I(w, h),
            RenderTargetClearMode  = SubViewport.ClearMode.Never,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            TransparentBg          = true, // Start completely transparent
        };
        AddChild(_depletionViewport);

        _depletionBrush = new Sprite2D
        {
            Texture  = _CreateBrushTexture(64), // Slightly smaller
            Position = ArenaHalfSize,
        };
        _depletionViewport.AddChild(_depletionBrush);

        // Pass to shader
        _mat.SetShaderParameter("trail_map", _trailViewport.GetTexture());
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
        _time += dt;
        _mat.SetShaderParameter("game_time", _time);

        if (_trailViewport != null)
            _mat.SetShaderParameter("trail_map", _trailViewport.GetTexture());
        if (_depletionViewport != null)
            _mat.SetShaderParameter("depletion_map", _depletionViewport.GetTexture());

        if (!GodotObject.IsInstanceValid(_car))
            _FindPlayer();

        if (_car == null || !GodotObject.IsInstanceValid(_car)) return;

        if (GodotObject.IsInstanceValid(_trailBrush))
        {
            Vector2 targetPos = _car.GlobalPosition + ArenaHalfSize;
            _trailBrush.Position = targetPos;
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

            _trailBrush.Scale    = Vector2.One * Mathf.Lerp(0.7f, 1.6f, drift);
            _trailBrush.Modulate = new Color(1f, 1f, 1f, Mathf.Clamp(speed / 40f + 0.15f, 0f, 1f));

            // Depletion brush paints very slowly to simulate gradual drying
            _depletionBrush.Scale = Vector2.One * Mathf.Lerp(0.5f, 1.2f, drift);
            _depletionBrush.Modulate = new Color(1f, 1f, 1f, 0.05f);
        }

        var carScript = _car as PlayerCar;
        if (carScript == null) return;

        float targetBrake = carScript.IsBraking ? 1f : 0f;
        _currentBrakeLight = Mathf.Lerp(_currentBrakeLight, targetBrake, 15f * dt);
        _mat.SetShaderParameter("brake_strength", _currentBrakeLight);

        var cam = GetViewport().GetCamera2D();
        if (cam != null)
            _mat.SetShaderParameter("screen_center", cam.GlobalPosition);
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