using Godot;
using System.Collections.Generic;

/// <summary>
/// Tire marks (Line2D) + drift sparks (GPUParticles2D) + tire heat glow at each rear wheel.
/// Reuses DriftSparks and TireGlow nodes placed in the scene.
/// </summary>
public partial class TireTrail : Node2D
{
    [ExportGroup("Tire Marks")]
    [Export] public int MaxPoints        = 512;
    [Export] public float MinSegmentDist = 4f;
    [Export] public float TrailWidth     = 3.5f;
    [Export] public Color TrailColor     = new Color(0.15f, 0.15f, 0.15f, 0.55f);
    [Export] public float FadeSpeed      = 0.15f;
    [Export] public int MaxTrails        = 30;

    [ExportGroup("Glow")]
    [Export] public float GlowOutwardPush = 5f;  // units to push glow toward car edge

    private PlayerCar _car;
    private Line2D _currentLine;
    private bool _wasMarking;
    private readonly List<TrailSegment> _trails = new();

    private GpuParticles2D _sparks;
    private GpuParticles2D _smoke;
    private ShaderMaterial _tireGlowMat;
    private Sprite2D _tireGlowSprite;

    // Local offset stores our position relative to the car before TopLevel is applied.
    // The Y sign tells us which side of the car this wheel is on (-=left, +=right).
    private Vector2 _localOffset;

    private class TrailSegment
    {
        public Line2D Line;
        public float Age;
    }

    public override void _Ready()
    {
        Node parent = GetParent();
        while (parent != null)
        {
            if (parent is PlayerCar car) { _car = car; break; }
            parent = parent.GetParent();
        }

        _localOffset = Position;
        TopLevel = true;

        // ── Sparks ────────────────────────────────────────────────────────────
        _sparks = GetNodeOrNull<GpuParticles2D>("DriftSparks");
        if (_sparks != null)
        {
            _sparks.TopLevel = true;
            _ConfigureSparks(_sparks);
        }

        // ── Smoke ─────────────────────────────────────────────────────────────
        _smoke = new GpuParticles2D();
        _smoke.TopLevel = true;
        _smoke.ZIndex   = 1;
        _ConfigureSmoke(_smoke);
        AddChild(_smoke);

        // ── Tire heat glow ────────────────────────────────────────────────────
        _tireGlowSprite = GetNodeOrNull<Sprite2D>("TireGlow");
        if (_tireGlowSprite != null && _tireGlowSprite.Material is ShaderMaterial sharedMat)
        {
            _tireGlowMat = (ShaderMaterial)sharedMat.Duplicate();
            _tireGlowSprite.Material = _tireGlowMat;
            _tireGlowSprite.Texture = _CreateSoftCircle(32);

            // Squished ellipse: wide along car forward, narrow laterally.
            // GlobalRotation is updated each frame to match the car heading.
            _tireGlowSprite.Scale = new Vector2(0.42f, 0.18f);
            _tireGlowSprite.ZIndex = -1;   // render behind the car body
            _tireGlowSprite.TopLevel = true;
        }
    }

    private void _ConfigureSparks(GpuParticles2D sparks)
    {
        sparks.Emitting      = false;
        sparks.Amount        = 18;
        sparks.Lifetime      = 0.4f;
        sparks.SpeedScale    = 1.0f;
        sparks.Explosiveness = 0.85f;  // emit in bursts
        sparks.OneShot       = false;
        sparks.ZIndex        = 2;

        // Additive blend so sparks visibly brighten everything beneath them
        var cim = new CanvasItemMaterial();
        cim.BlendMode = CanvasItemMaterial.BlendModeEnum.Add;
        sparks.Material = cim;

        var mat = new ParticleProcessMaterial();
        mat.Direction            = new Vector3(0, 0, 0);
        mat.Spread               = 180f;    // omni-directional burst
        mat.InitialVelocityMin   = 90f;
        mat.InitialVelocityMax   = 220f;
        mat.AngularVelocityMin   = -360f;
        mat.AngularVelocityMax   = 360f;
        mat.Gravity              = new Vector3(0, 0, 0);
        mat.LinearAccelMin       = -180f;
        mat.LinearAccelMax       = -80f;    // decelerate sharply = short streaks
        mat.DampingMin           = 120f;
        mat.DampingMax           = 200f;
        mat.ScaleMin             = 0.06f;
        mat.ScaleMax             = 0.14f;

        // White → yellow → orange → fade
        var colorRamp = new GradientTexture1D();
        var gradient  = new Gradient();
        gradient.SetColor(0, new Color(1.0f, 1.0f, 0.9f, 1.0f));
        gradient.AddPoint(0.25f, new Color(1.0f, 0.85f, 0.2f, 1.0f));
        gradient.AddPoint(0.6f,  new Color(1.0f, 0.45f, 0.1f, 0.7f));
        gradient.SetColor(gradient.GetPointCount() - 1, new Color(0.9f, 0.2f, 0.0f, 0.0f));
        colorRamp.Gradient = gradient;
        mat.ColorRamp = colorRamp;

        sparks.ProcessMaterial = mat;
        sparks.Texture = _CreateSparkDot(8);
    }

    private void _ConfigureSmoke(GpuParticles2D smoke)
    {
        smoke.Emitting      = false;
        smoke.Amount        = 32;
        smoke.Lifetime      = 0.9f;
        smoke.SpeedScale    = 1.2f;
        smoke.Explosiveness = 0.0f;

        var mat = new ParticleProcessMaterial();
        mat.Direction          = new Vector3(0, -1, 0);
        mat.Spread             = 35f;
        mat.InitialVelocityMin = 15f;
        mat.InitialVelocityMax = 40f;
        mat.AngularVelocityMin = -90f;
        mat.AngularVelocityMax = 90f;
        mat.Gravity            = new Vector3(0, -8f, 0);
        mat.LinearAccelMin     = -10f;
        mat.LinearAccelMax     = -5f;
        mat.ScaleMin           = 0.5f;
        mat.ScaleMax           = 1.4f;
        mat.DampingMin         = 8f;
        mat.DampingMax         = 15f;

        var colorRamp = new GradientTexture1D();
        var gradient  = new Gradient();
        gradient.SetColor(0, new Color(0.85f, 0.83f, 0.8f, 0.55f));
        gradient.AddPoint(0.3f, new Color(0.7f, 0.68f, 0.65f, 0.35f));
        gradient.SetColor(gradient.GetPointCount() - 1, new Color(0.5f, 0.48f, 0.45f, 0f));
        colorRamp.Gradient = gradient;
        mat.ColorRamp = colorRamp;

        smoke.ProcessMaterial = mat;
        smoke.Texture = _CreateSoftCircle(32);
    }

    // Tiny bright centre dot for each spark particle
    private GradientTexture2D _CreateSparkDot(int size)
    {
        var tex = new GradientTexture2D();
        tex.Width    = size;
        tex.Height   = size;
        tex.Fill     = GradientTexture2D.FillEnum.Radial;
        tex.FillFrom = new Vector2(0.5f, 0.5f);
        tex.FillTo   = new Vector2(0.5f, 0f);
        var g = new Gradient();
        g.SetColor(0, new Color(1, 1, 1, 1));
        g.SetColor(g.GetPointCount() - 1, new Color(1, 1, 1, 0f));
        tex.Gradient = g;
        return tex;
    }

    // Soft radial circle used for the tire heat glow
    private GradientTexture2D _CreateSoftCircle(int size)
    {
        var tex = new GradientTexture2D();
        tex.Width    = size;
        tex.Height   = size;
        tex.Fill     = GradientTexture2D.FillEnum.Radial;
        tex.FillFrom = new Vector2(0.5f, 0.5f);
        tex.FillTo   = new Vector2(0.5f, 0f);
        var g = new Gradient();
        g.SetColor(0, new Color(1, 1, 1, 1));
        g.AddPoint(0.4f, new Color(1, 1, 1, 0.7f));
        g.SetColor(g.GetPointCount() - 1, new Color(1, 1, 1, 0f));
        tex.Gradient = g;
        return tex;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_car == null) return;

        float dt       = (float)delta;
        bool shouldMark = _car.IsDrifting && _car.Speed > 30f;
        float intensity = _car.DriftIntensity;

        Vector2 wheelWorldPos = _car.ToGlobal(_localOffset);

        // ── Sparks ────────────────────────────────────────────────────────────
        if (_sparks != null)
        {
            _sparks.GlobalPosition = wheelWorldPos;

            bool shouldSpark = shouldMark && intensity > 0.15f;
            _sparks.Emitting = shouldSpark;

            if (shouldSpark && _sparks.ProcessMaterial is ParticleProcessMaterial pmat)
            {
                pmat.InitialVelocityMin = 90f  + 160f * intensity;
                pmat.InitialVelocityMax = 220f + 260f * intensity;
            }
        }

        // ── Smoke ─────────────────────────────────────────────────────────────
        if (_smoke != null)
        {
            _smoke.GlobalPosition = wheelWorldPos;

            bool shouldSmoke = shouldMark && intensity > 0.2f;
            _smoke.Emitting = shouldSmoke;

            if (shouldSmoke && _smoke.ProcessMaterial is ParticleProcessMaterial smat)
            {
                smat.InitialVelocityMin = 15f + 25f * intensity;
                smat.InitialVelocityMax = 40f + 55f * intensity;
            }
        }

        // ── Tire heat glow ────────────────────────────────────────────────────
        if (_tireGlowSprite != null)
        {
            // Push glow outward to the outer edge of the car body.
            // _localOffset.Y sign tells us which side (-=left, +=right).
            float lateralSign = _localOffset.Y < 0f ? -1f : 1f;
            Vector2 localGlowOffset = _localOffset
                + new Vector2(0f, lateralSign * GlowOutwardPush);
            _tireGlowSprite.GlobalPosition = _car.ToGlobal(localGlowOffset);

            // Rotate so the squished ellipse aligns with the car's heading
            _tireGlowSprite.GlobalRotation = _car.GlobalRotation;
        }

        if (_tireGlowMat != null)
        {
            float current = (float)_tireGlowMat.GetShaderParameter("intensity");
            float target  = shouldMark ? intensity : 0f;
            float rate     = shouldMark ? 8f : 3f;  // ramp up fast, decay slowly
            _tireGlowMat.SetShaderParameter("intensity",
                Mathf.MoveToward(current, target, rate * dt));
        }

        // ── Tire marks ────────────────────────────────────────────────────────
        if (shouldMark)
        {
            if (!_wasMarking)
            {
                _currentLine = new Line2D();
                _currentLine.Width        = TrailWidth;
                _currentLine.DefaultColor = TrailColor;
                _currentLine.TopLevel     = true;
                _currentLine.ZIndex       = -1;
                _currentLine.Antialiased  = true;
                _currentLine.BeginCapMode = Line2D.LineCapMode.Round;
                _currentLine.EndCapMode   = Line2D.LineCapMode.Round;
                GetTree().CurrentScene.AddChild(_currentLine);
                _trails.Add(new TrailSegment { Line = _currentLine, Age = 0f });
                _currentLine.AddPoint(wheelWorldPos);
            }

            if (_currentLine.GetPointCount() == 0 ||
                wheelWorldPos.DistanceTo(
                    _currentLine.GetPointPosition(_currentLine.GetPointCount() - 1))
                > MinSegmentDist)
            {
                _currentLine.AddPoint(wheelWorldPos);
                if (_currentLine.GetPointCount() > MaxPoints)
                    _currentLine.RemovePoint(0);
            }
        }
        else
        {
            _currentLine = null;
        }

        _wasMarking = shouldMark;

        // Fade and cull old trails
        for (int i = _trails.Count - 1; i >= 0; i--)
        {
            var seg = _trails[i];
            if (seg.Line == _currentLine) continue;

            seg.Age += dt * FadeSpeed;
            float alpha = Mathf.Max(0f, TrailColor.A * (1f - seg.Age));

            if (alpha <= 0.01f)
            {
                seg.Line.QueueFree();
                _trails.RemoveAt(i);
            }
            else
            {
                seg.Line.DefaultColor = new Color(
                    TrailColor.R, TrailColor.G, TrailColor.B, alpha);
            }
        }

        while (_trails.Count > MaxTrails)
        {
            _trails[0].Line.QueueFree();
            _trails.RemoveAt(0);
        }
    }
}
