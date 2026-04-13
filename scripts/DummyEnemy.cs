using Godot;

/// <summary>
/// Test enemy that fully delegates health to its child Damageable node.
///
/// Set MaxHealth and Armor in the Inspector (or before AddChild when spawning
/// programmatically) to differentiate difficulty tiers without needing separate
/// scene files.
///
/// Groups: automatically adds itself to both "enemies" (used by RamSystem) and
/// "Enemies" (used by BumperArea) so either damage path works.
/// </summary>
public partial class DummyEnemy : StaticBody2D
{
    [ExportGroup("Stats")]
    [Export] public float MaxHealth  = 100f;
    [Export] public float Armor      = 0f;
    [Export] public int   ScoreValue = 100;

    private Damageable _damageable;

    public override void _Ready()
    {
        // Unify group names — RamSystem uses "enemies", BumperArea uses "Enemies"
        AddToGroup("enemies");
        AddToGroup("Enemies");

        // All arena obstacles use CollisionLayer=2 so the player can detect them.
        // The scene default is layer 1, which the player's mask doesn't include.
        CollisionLayer = 2;
        CollisionMask  = 0;   // static enemies don't need to detect others

        _damageable = GetNodeOrNull<Damageable>("Damageable");
        if (_damageable == null)
        {
            GD.PushError($"DummyEnemy '{Name}': missing Damageable child node.");
            return;
        }

        // Push exported stats into the component before resetting
        _damageable.MaxHealth = MaxHealth;
        _damageable.Armor     = Armor;
        _damageable.Reset();

        _damageable.Died += _OnDied;

        // Attach the floating health bar
        var bar = new EnemyHealthBar();
        AddChild(bar);
    }

    // ── Death sequence ────────────────────────────────────────────────────────

    private void _OnDied()
    {
        // Stop taking hits immediately
        RemoveFromGroup("enemies");
        RemoveFromGroup("Enemies");

        // Brief white flash
        Modulate = Colors.White;

        // Short delay so the flash is visible, then remove from scene
        GetTree().CreateTimer(0.10).Timeout += QueueFree;
    }
}
