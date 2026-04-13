using Godot;

/// <summary>
/// Attached to the main scene root. On _Ready it checks VehicleRegistry.SelectedPlayerScene
/// and, if it differs from the default (player_car.tscn), removes the hardcoded PlayerCar
/// node and instantiates the selected controller scene in its place.
///
/// This lets the VehicleSelectScreen's controller-type row be tested without duplicating
/// the arena scene for each controller type.
/// </summary>
public partial class PlayerSwapper : Node
{
    private const string DefaultScene      = "res://scenes/player_car.tscn";
    private const string DefaultPlayerName = "PlayerCar";

    // Matches the position set for PlayerCar in main.tscn
    private static readonly Vector2 FallbackSpawn = new(-800f, 0f);

    // Zoom used by SmoothCamera; mirrored here so the fallback Camera2D looks the same.
    private const float FallbackZoom = 1.4f;

    public override void _Ready()
    {
        var registry = VehicleRegistry.Instance;
        string target = registry?.SelectedPlayerScene ?? DefaultScene;

        if (target == DefaultScene) return;  // nothing to do — car is already in the scene

        // ── Load the replacement scene up-front (before any deferred call) ────
        var packed = ResourceLoader.Load<PackedScene>(target);
        if (packed == null)
        {
            GD.PushError($"PlayerSwapper: failed to load '{target}'.");
            return;
        }

        // ── Measure spawn position from the existing node ─────────────────────
        var parent = GetParent();
        var existing = parent.GetNodeOrNull<Node2D>(DefaultPlayerName);
        Vector2 spawnPos = existing?.GlobalPosition ?? FallbackSpawn;

        // ── Prepare the replacement (ok to build outside the scene tree) ──────
        var player = packed.Instantiate<Node2D>();
        player.Position = spawnPos;

        // Add the fallback camera before the node enters the scene tree so its
        // own _Ready doesn't race with ours.
        if (!_HasCamera(player))
        {
            player.AddChild(new Camera2D { Zoom = new Vector2(FallbackZoom, FallbackZoom) });
            GD.Print("PlayerSwapper: added fallback Camera2D (selected player has none).");
        }

        // ── Defer both removal and insertion ──────────────────────────────────
        // _Ready is called while the parent node is busy initialising children;
        // AddChild (and QueueFree on siblings) are blocked until that finishes.
        // CallDeferred schedules both actions for the start of the next frame.
        Callable.From(() =>
        {
            existing?.QueueFree();
            parent.AddChild(player);
            GD.Print($"PlayerSwapper: spawned '{player.Name}' from '{target}'.");
        }).CallDeferred();
    }

    private static bool _HasCamera(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            if (child is Camera2D) return true;
            if (_HasCamera(child)) return true;
        }
        return false;
    }
}
