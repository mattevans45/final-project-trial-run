using Godot;

public partial class GroundGrid : Node2D
{
    private ShaderMaterial _mat;
    private Node2D _car;
    private float _currentBrakeLight = 0f;
    private float _time = 0f;

    public override void _Ready()
    {
        var poly = GetNode<Polygon2D>("../Background");
        _mat = poly.Material as ShaderMaterial;

        _car = GetNode<Node2D>("../PlayerCar");
    }

    /// <summary>
    /// Try to locate the active player node. Called when the cached reference
    /// becomes invalid (e.g. PlayerSwapper replaced it with a different scene).
    /// </summary>
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

        _time += (float)delta;
        _mat.SetShaderParameter("game_time", _time);

        // QueueFree makes the C# wrapper disposed — IsInstanceValid catches that.
        if (!GodotObject.IsInstanceValid(_car))
            _FindPlayer();

        if (_car == null || !GodotObject.IsInstanceValid(_car)) return;

        var carScript = _car as PlayerCar;

        // Non-PlayerCar controllers don't expose the car-specific properties;
        // update only position and reset dynamic params to neutral values.
        if (carScript == null)
        {
            _mat.SetShaderParameter("car_pos", _car.GlobalPosition);
            _mat.SetShaderParameter("car_speed", 0f);
            _mat.SetShaderParameter("velocity_dir", Vector2.Right);
            _mat.SetShaderParameter("car_drift", 0f);
            _mat.SetShaderParameter("car_dir", Vector2.Right);
            _mat.SetShaderParameter("brake_strength", 0f);
            var cam2 = GetViewport().GetCamera2D();
            if (cam2 != null)
                _mat.SetShaderParameter("screen_center", cam2.GlobalPosition);
            return;
        }

        // Pass Car Position and Speed
        _mat.SetShaderParameter("car_pos", _car.GlobalPosition);
        _mat.SetShaderParameter("car_speed", carScript.TotalSpeed);
        
        // Pass Dynamics for the Wake
        _mat.SetShaderParameter("velocity_dir", carScript.VelocityDirection);
        _mat.SetShaderParameter("car_drift", carScript.DriftIntensity);

        // 1. Pass positional data
        _mat.SetShaderParameter("car_pos", _car.GlobalPosition);
        _mat.SetShaderParameter("car_dir", carScript.ForwardDir);

        // 2. Smooth Brake Light Logic
        float targetBrakeLight = carScript.IsBraking ? 1.0f : 0.0f;
        
        // Lerp the value so the brake lights fade on/off smoothly like real incandescent/LED bulbs
        _currentBrakeLight = Mathf.Lerp(_currentBrakeLight, targetBrakeLight, 15f * (float)delta);
        _mat.SetShaderParameter("brake_strength", _currentBrakeLight);

        // 3. Environmental Displacement (If you kept the puddle wake / skid marks)
        _mat.SetShaderParameter("car_speed", carScript.TotalSpeed);
        _mat.SetShaderParameter("car_drift", carScript.DriftIntensity);
        
        // Ensure screen center is still updated for your vignette or localized effects
        var cam = GetViewport().GetCamera2D();
        if (cam != null)
            _mat.SetShaderParameter("screen_center", cam.GlobalPosition);
    }
}