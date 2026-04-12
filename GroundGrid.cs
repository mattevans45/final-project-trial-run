using Godot;

/// <summary>
/// Shader-based asphalt ground. Creates a large ColorRect with a procedural
/// asphalt shader featuring checkerboard tiles, grid lines, dashed lane
/// markings, noise grit, and wear stains — all GPU-rendered.
/// 
/// Place the ground_grid.gdshader file in your project, then assign it via
/// the ShaderPath export or let this script find it at "res://ground_grid.gdshader".
/// </summary>
public partial class GroundGrid : Node2D
{
    [Export] public float WorldSize = 10240f;  // total size in pixels
    [Export(PropertyHint.File, "*.gdshader")] 
    public string ShaderPath = "res://ground_grid.gdshader";

    public override void _Ready()
    {
        ZIndex = -10;

        var rect = new ColorRect();
        rect.Size = new Vector2(WorldSize, WorldSize);
        rect.Position = new Vector2(-WorldSize * 0.5f, -WorldSize * 0.5f);
        rect.Color = new Color(0.15f, 0.15f, 0.18f, 1f);

        // Load and apply shader
        var shaderFile = GD.Load<Shader>(ShaderPath);
        if (shaderFile != null)
        {
            var mat = new ShaderMaterial();
            mat.Shader = shaderFile;
            rect.Material = mat;
        }
        else
        {
            GD.PushWarning($"GroundGrid: Could not load shader at '{ShaderPath}'. Using flat color.");
        }

        AddChild(rect);
    }
}