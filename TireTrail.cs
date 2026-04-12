using Godot;
using System.Collections.Generic;

/// <summary>
/// Tire marks (Line2D) + drift smoke (GPUParticles2D) at each rear wheel.
/// Smoke uses a CanvasItemMaterial shader for soft, billowy puffs.
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

    [ExportGroup("Smoke")]
    [Export] public float SmokeScale     = 1.0f;

    private PlayerCar _car;
    private Line2D _currentLine;
    private bool _wasMarking;
    private readonly List<TrailSegment> _trails = new();
    private GpuParticles2D _smoke;
    private Vector2 _localOffset; // our position relative to car before TopLevel

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
            if (parent is PlayerCar car)
            {
                _car = car;
                break;
            }
            parent = parent.GetParent();
        }

        _localOffset = Position;
        TopLevel = true;

        _CreateSmoke();
    }

    private void _CreateSmoke()
    {
        _smoke = new GpuParticles2D();
        _smoke.Emitting = false;
        _smoke.Amount = 32;
        _smoke.Lifetime = 0.8f;
        _smoke.SpeedScale = 1.2f;
        _smoke.Explosiveness = 0.0f;
        _smoke.TopLevel = true;
        _smoke.ZIndex = 1;

        // Process material for particle behavior
        var mat = new ParticleProcessMaterial();
        mat.Direction = new Vector3(0, -1, 0);
        mat.Spread = 35f;
        mat.InitialVelocityMin = 15f;
        mat.InitialVelocityMax = 40f;
        mat.AngularVelocityMin = -90f;
        mat.AngularVelocityMax = 90f;
        mat.Gravity = new Vector3(0, -8f, 0);  // drift upward (Y- in 2D)
        mat.LinearAccelMin = -10f;
        mat.LinearAccelMax = -5f;   // decelerate
        mat.ScaleMin = 0.4f * SmokeScale;
        mat.ScaleMax = 1.2f * SmokeScale;
        mat.DampingMin = 8f;
        mat.DampingMax = 15f;

        // Scale over lifetime: grow then shrink
        var scaleCurve = new CurveTexture();
        var curve = new Curve();
        curve.AddPoint(new Vector2(0f, 0.3f));
        curve.AddPoint(new Vector2(0.3f, 1.0f));
        curve.AddPoint(new Vector2(1f, 0.0f));
        scaleCurve.Curve = curve;
        mat.ScaleOverVelocityCurve = null;

        // Color: white-gray fading to transparent
        var colorRamp = new GradientTexture1D();
        var gradient = new Gradient();
        gradient.SetColor(0, new Color(0.85f, 0.83f, 0.8f, 0.6f));
        gradient.AddPoint(0.3f, new Color(0.7f, 0.68f, 0.65f, 0.4f));
        gradient.SetColor(gradient.GetPointCount() - 1, new Color(0.5f, 0.48f, 0.45f, 0.0f));
        colorRamp.Gradient = gradient;
        mat.ColorRamp = colorRamp;

        _smoke.ProcessMaterial = mat;

        // Soft circle texture for the particle quad
        _smoke.Texture = _CreateSoftCircle(32);

        AddChild(_smoke);
    }

    private GradientTexture2D _CreateSoftCircle(int size)
    {
        var tex = new GradientTexture2D();
        tex.Width = size;
        tex.Height = size;
        tex.Fill = GradientTexture2D.FillEnum.Radial;
        tex.FillFrom = new Vector2(0.5f, 0.5f);
        tex.FillTo = new Vector2(0.5f, 0f);

        var grad = new Gradient();
        grad.SetColor(0, new Color(1, 1, 1, 1));
        grad.AddPoint(0.4f, new Color(1, 1, 1, 0.7f));
        grad.SetColor(grad.GetPointCount() - 1, new Color(1, 1, 1, 0f));
        tex.Gradient = grad;

        return tex;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_car == null) return;

        float dt = (float)delta;
        bool shouldMark = _car.IsDrifting && _car.Speed > 30f;

        // Update smoke position to follow the wheel
        Vector2 worldPos = _car.ToGlobal(_localOffset);

        if (_smoke != null)
        {
            _smoke.GlobalPosition = worldPos;

            // Scale emission rate with drift intensity
            bool shouldSmoke = shouldMark && _car.DriftIntensity > 0.2f;
            _smoke.Emitting = shouldSmoke;

            if (shouldSmoke && _smoke.ProcessMaterial is ParticleProcessMaterial pmat)
            {
                // More intense drift = more/faster smoke
                float intensity = _car.DriftIntensity;
                pmat.InitialVelocityMin = 15f + 30f * intensity;
                pmat.InitialVelocityMax = 40f + 60f * intensity;
            }
        }

        // ── Tire marks ──
        if (shouldMark)
        {
            if (!_wasMarking)
            {
                _currentLine = new Line2D();
                _currentLine.Width = TrailWidth;
                _currentLine.DefaultColor = TrailColor;
                _currentLine.TopLevel = true;
                _currentLine.ZIndex = -1;
                _currentLine.Antialiased = true;
                _currentLine.BeginCapMode = Line2D.LineCapMode.Round;
                _currentLine.EndCapMode   = Line2D.LineCapMode.Round;
                GetTree().CurrentScene.AddChild(_currentLine);

                _trails.Add(new TrailSegment { Line = _currentLine, Age = 0f });
                _currentLine.AddPoint(worldPos);
            }

            if (_currentLine.GetPointCount() == 0 ||
                worldPos.DistanceTo(_currentLine.GetPointPosition(_currentLine.GetPointCount() - 1)) > MinSegmentDist)
            {
                _currentLine.AddPoint(worldPos);

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
                seg.Line.DefaultColor = new Color(TrailColor.R, TrailColor.G, TrailColor.B, alpha);
            }
        }

        while (_trails.Count > MaxTrails)
        {
            _trails[0].Line.QueueFree();
            _trails.RemoveAt(0);
        }
    }
}