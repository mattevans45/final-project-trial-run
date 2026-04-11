using Godot;
using System.Collections.Generic;

/// <summary>
/// Emits tire marks as Line2D segments in world space.
/// Add as a child of the car at each rear-wheel position.
/// Reads drift state from the parent PlayerCar.
/// </summary>
public partial class TireTrail : Node2D
{
    [Export] public int MaxPoints        = 512;
    [Export] public float MinSegmentDist = 4f;
    [Export] public float TrailWidth     = 3.5f;
    [Export] public Color TrailColor     = new Color(0.15f, 0.15f, 0.15f, 0.55f);
    [Export] public float FadeSpeed      = 0.15f;

    /// <summary>Max number of trail segments alive at once. Old ones fade and free.</summary>
    [Export] public int MaxTrails = 30;

    private PlayerCar _car;
    private Line2D _currentLine;
    private bool _wasMarking;
    private readonly List<TrailSegment> _trails = new();

    private class TrailSegment
    {
        public Line2D Line;
        public float Age;
    }

    public override void _Ready()
    {
        // Walk up to find the PlayerCar parent
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

        // Trails are drawn in world space under the scene root so they
        // don't move/rotate with the car.
        TopLevel = true;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_car == null) return;

        float dt = (float)delta;
        bool shouldMark = _car.IsDrifting && _car.Speed > 30f;

        if (shouldMark)
        {
            // Get wheel world position from the car
            Vector2 worldPos = _car.ToGlobal(Position);

            if (!_wasMarking)
            {
                // Start a new trail segment
                _currentLine = new Line2D();
                _currentLine.Width = TrailWidth;
                _currentLine.DefaultColor = TrailColor;
                _currentLine.TopLevel = true;
                _currentLine.ZIndex = -1;    // behind the car
                _currentLine.Antialiased = true;
                _currentLine.BeginCapMode = Line2D.LineCapMode.Round;
                _currentLine.EndCapMode   = Line2D.LineCapMode.Round;
                GetTree().CurrentScene.AddChild(_currentLine);

                _trails.Add(new TrailSegment { Line = _currentLine, Age = 0f });
                _currentLine.AddPoint(worldPos);
            }

            // Add points if we've moved enough
            if (_currentLine.GetPointCount() == 0 ||
                worldPos.DistanceTo(_currentLine.GetPointPosition(_currentLine.GetPointCount() - 1)) > MinSegmentDist)
            {
                _currentLine.AddPoint(worldPos);

                // Cap length
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

            // Don't fade the active segment
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

        // Hard cap on trail count
        while (_trails.Count > MaxTrails)
        {
            _trails[0].Line.QueueFree();
            _trails.RemoveAt(0);
        }
    }
}