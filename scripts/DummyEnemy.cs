using Godot;

public partial class DummyEnemy : StaticBody2D
{
    [Export] public int Health = 100;

    public void TakeDamage(int amount)
    {
        Health -= amount;
        GD.Print($"Enemy took {amount} damage! Health left: {Health}");

        // Quick visual feedback: Flash white, then back to normal
        Modulate = Colors.White;
        
        // A quick lambda timer to reset the color after 0.1 seconds
        GetTree().CreateTimer(0.1f).Timeout += () => Modulate = new Color(1, 1, 1, 1); // Reset

        if (Health <= 0)
        {
            GD.Print("Enemy Destroyed!");
            QueueFree(); // Removes the enemy from the game
        }
    }
}