using Godot;

public partial class GroundGrid : Node2D
{
    private ShaderMaterial _mat;
    private Node2D _car;
    
    // Store the current visual intensity of the brake lights
    private float _currentBrakeLight = 0f;

    public override void _Ready()
    {
        var poly = GetNode<Polygon2D>("../Background");
        _mat = poly.Material as ShaderMaterial;

        _car = GetNode<Node2D>("../PlayerCar");
    }

    public override void _Process(double delta)
    {
        if (_mat == null || _car == null) return;

        var carScript = _car as PlayerCar;
        if (carScript == null) return;

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
        _mat.SetShaderParameter("screen_center", GetViewport().GetCamera2D().GlobalPosition);
    }
}