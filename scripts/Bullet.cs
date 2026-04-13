using Godot;

/// <summary>
/// Fast-moving pistol round. Spawned by PistolWeapon at the muzzle marker.
/// Area2D so it doesn't physically push bodies — it detects and deals damage on touch.
/// </summary>
public partial class Bullet : Area2D
{
    public Vector2 Direction { get; set; } = Vector2.Right;
    public float   Speed     { get; set; } = 720f;
    public float   Damage    { get; set; } = 25f;

    private const float Lifetime = 1.4f;

    public override void _Ready()
    {
        // Invisible to physics world; detect layer 2 (enemies + arena obstacles)
        CollisionLayer = 0;
        CollisionMask  = 2;

        BodyEntered += _OnBodyEntered;

        // Tracer visual — a short yellow rectangle aligned with Direction
        Rotation = Mathf.Atan2(Direction.Y, Direction.X);

        var tracer = new Polygon2D
        {
            Polygon = new Vector2[]
            {
                new(-8f, -2f), new(5f, -2f),
                new(5f,  2f),  new(-8f,  2f),
            },
            Color = new Color(1f, 0.94f, 0.42f, 0.88f),
        };
        AddChild(tracer);

        // Small bright core for the tip
        var tip = new Polygon2D
        {
            Polygon = new Vector2[]
            {
                new(2f, -2f), new(6f, 0f), new(2f, 2f),
            },
            Color = new Color(1f, 1f, 0.85f, 1f),
        };
        AddChild(tip);

        // Collision shape — small circle at the bullet tip
        AddChild(new CollisionShape2D { Shape = new CircleShape2D { Radius = 4f } });

        // Self-destruct after max travel time
        GetTree().CreateTimer(Lifetime).Timeout += QueueFree;
    }

    public override void _PhysicsProcess(double delta)
    {
        Position += Direction * Speed * (float)delta;
    }

    private void _OnBodyEntered(Node2D body)
    {
        if (body.IsInGroup("enemies"))
            body.GetNodeOrNull<Damageable>("Damageable")?.TakeDamage(Damage, GlobalPosition);
        QueueFree();
    }
}
