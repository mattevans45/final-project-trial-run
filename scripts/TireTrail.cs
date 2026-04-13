using Godot;
using System.Collections.Generic;

/// <summary>
/// Tire marks (Line2D) + drift sparks (GPUParticles2D) + tire heat glow at each rear wheel.
/// Cleans up Line2D nodes on tree exit to prevent leaks.
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
    [Export] public float GlowOutwardPush = 4f;

    private PlayerCar _car;
    private Line2D _currentLine;
    private bool _wasMarking;
    private readonly List<TrailSegment> _trails = new();

    private GpuParticles2D _sparks;
    private GpuParticles2D _smoke;
    private ShaderMaterial _tireGlowMat;
    private Sprite2D _tireGlowSprite;
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

        // Clean up Line2D nodes when leaving the tree (scene change, QueueFree, etc.)
        TreeExiting += _CleanupTrails;

        _sparks = GetNodeOrNull<GpuParticles2D>("DriftSparks");
        if (_sparks != null)
        {
            _sparks.TopLevel = true;
            _ConfigureSparks(_sparks);
        }

        _smoke = new GpuParticles2D();
        _smoke.TopLevel = true;
        _smoke.ZIndex   = -1;
        _ConfigureSmoke(_smoke);
        AddChild(_smoke);

        _tireGlowSprite = GetNodeOrNull<Sprite2D>("TireGlow");
        if (_tireGlowSprite != null && _tireGlowSprite.Material is ShaderMaterial sharedMat)
        {
            _tireGlowMat = (ShaderMaterial)sharedMat.Duplicate();
            _tireGlowSprite.Material = _tireGlowMat;
            _tireGlowSprite.Texture = _CreateSoftCircle(32);
            _tireGlowSprite.Scale = new Vector2(0.42f, 0.18f);
            _tireGlowSprite.ZIndex = -1;
            _tireGlowSprite.TopLevel = true;
        }
    }

    private void _CleanupTrails()
    {
        foreach (var seg in _trails)
        {
            if (IsInstanceValid(seg.Line))
                seg.Line.QueueFree();
        }
        _trails.Clear();
        _currentLine = null;
    }

    private void _ConfigureSparks(GpuParticles2D sparks)
    {
        sparks.Emitting      = false;
        sparks.Amount        = 18;
        sparks.Lifetime      = 0.4f;
        sparks.SpeedScale    = 1.0f;
        sparks.Explosiveness = 0.85f;
        sparks.OneShot       = false;
        sparks.ZIndex        = 2;

        var cim = new CanvasItemMaterial();
        cim.BlendMode = CanvasItemMaterial.BlendModeEnum.Add;
        sparks.Material = cim;

        var mat = new ParticleProcessMaterial();
        mat.Direction            = new Vector3(0, 0, 0);
        mat.Spread               = 180f;
        mat.InitialVelocityMin   = 90f;
        mat.InitialVelocityMax   = 220f;
        mat.AngularVelocityMin   = -360f;
        mat.AngularVelocityMax   = 360f;
        mat.Gravity              = new Vector3(0, 0, 0);
        mat.LinearAccelMin       = -180f;
        mat.LinearAccelMax       = -80f;
        mat.DampingMin           = 120f;
        mat.DampingMax           = 200f;
        mat.ScaleMin             = 0.06f;
        mat.ScaleMax             = 0.14f;

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
        smoke.Amount        = 16;
        smoke.Lifetime      = 0.75f;
        smoke.SpeedScale    = 0.4f;
        smoke.Explosiveness = 0.0f;

        var mat = new ParticleProcessMaterial();
        mat.Direction          = new Vector3(0, -1, 0);
        mat.Spread             = 30f;
        mat.InitialVelocityMin = 12f;
        mat.InitialVelocityMax = 30f;
        mat.AngularVelocityMin = -60f;
        mat.AngularVelocityMax = 60f;
        mat.Gravity            = new Vector3(0, -6f, 0);
        mat.LinearAccelMin     = -8f;
        mat.LinearAccelMax     = -3f;
        mat.ScaleMin           = 0.25f;
        mat.ScaleMax           = 0.55f;
        mat.DampingMin         = 10f;
        mat.DampingMax         = 18f;

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

        float dt        = (float)delta;
        float intensity = _car.TireSpinIntensity;

        bool shouldMark  = intensity > 0.08f && _car.Speed > 20f;
        bool shouldEffect = intensity > 0.08f;

        Vector2 wheelWorldPos = _car.ToGlobal(_localOffset);

        // Sparks
        if (_sparks != null)
        {
            _sparks.GlobalPosition = wheelWorldPos;
            _sparks.Emitting = shouldEffect && intensity > 0.12f;

            if (_sparks.Emitting && _sparks.ProcessMaterial is ParticleProcessMaterial pmat)
            {
                pmat.InitialVelocityMin = 90f  + 160f * intensity;
                pmat.InitialVelocityMax = 220f + 260f * intensity;
            }
        }

        // Smoke
        if (_smoke != null)
        {
            _smoke.GlobalPosition = wheelWorldPos;
            _smoke.Emitting = shouldEffect;
            _smoke.SpeedScale = 0.3f + 1.4f * intensity;
        }

        // Tire heat glow
        if (_tireGlowSprite != null)
        {
            float lateralSign = _localOffset.Y < 0f ? -1f : 1f;
            Vector2 localGlowOffset = _localOffset
                + new Vector2(0f, lateralSign * GlowOutwardPush);
            _tireGlowSprite.GlobalPosition = _car.ToGlobal(localGlowOffset);
            _tireGlowSprite.GlobalRotation = _car.GlobalRotation;
        }

        if (_tireGlowMat != null)
        {
            float current = (float)_tireGlowMat.GetShaderParameter("intensity");
            float target  = shouldEffect ? intensity : 0f;
            float rate    = shouldEffect ? 8f : 3f;
            _tireGlowMat.SetShaderParameter("intensity",
                Mathf.MoveToward(current, target, rate * dt));
        }

        // Tire marks
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
                if (IsInstanceValid(seg.Line))
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
            if (IsInstanceValid(_trails[0].Line))
                _trails[0].Line.QueueFree();
            _trails.RemoveAt(0);
        }
    }
}