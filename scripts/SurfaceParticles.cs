using Godot;

/// <summary>
/// Handles tire spray (GpuParticles2D) when driving over puddles.
/// Rings and Oil are handled natively by the ground shader.
/// </summary>
public partial class SurfaceParticles : Node2D
{
    private SurfaceDetector _detector;
    private CharacterBody2D _car;

    private GpuParticles2D  _spray;
    private const float WaterMinSpeed = 55f;

    public override void _Ready()
    {
        _car      = GetParent<CharacterBody2D>();
        _detector = GetParent().GetNodeOrNull<SurfaceDetector>("SurfaceDetector");

        _spray   = _BuildSprayParticles();
        AddChild(_spray);
    }

    public override void _Process(double delta)
    {
        if (_car == null || _detector == null)
        {
            _spray.Emitting  = false;
            return;
        }

        float speed = _car.Velocity.Length();
        bool onPuddle = _detector.IsOnPuddle && speed > WaterMinSpeed;
        
        _spray.Emitting = onPuddle;

        if (onPuddle)
        {
            var velDir = _car.Velocity.Normalized();
            _spray.Rotation = Mathf.Atan2(velDir.Y, velDir.X);
        }
    }

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

    private static Texture2D _CreateDropletTexture(int w, int h)
    {
        var img = Image.Create(w, h, false, Image.Format.Rgba8);
        float cx = w * 0.5f;

        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            float nx = (x - cx) / (w * 0.5f);
            float ny = (float)y / h;             

            float rx = 0.30f + ny * 0.70f;
            float a  = 1f - Mathf.Clamp(Mathf.Abs(nx) / rx, 0f, 1f);
            float tipFade = Mathf.Clamp(ny / 0.25f, 0f, 1f);
            img.SetPixel(x, y, new Color(1f, 1f, 1f, a * tipFade));
        }
        return ImageTexture.CreateFromImage(img);
    }
}