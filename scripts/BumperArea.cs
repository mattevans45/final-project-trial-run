using Godot;
using System;

public partial class BumperArea : Area2D
{
    [Export] public int RamDamage = 50;
    
    // Must match player.tscn signal connection: method="_on_body_entered"
    private void _on_body_entered(Node2D body)
    {
        // 1. Check if the thing we hit is actually an enemy
        if (body.IsInGroup("Enemies"))
        {
            // 2. Get the vector pointing from the car to the enemy
            Vector2 directionToEnemy = (body.GlobalPosition - GlobalPosition).Normalized();
            
            // 3. Get the direction the car is currently facing
            // Assuming Transform.X is the forward direction of your sprite
            Vector2 carForward = GlobalTransform.X.Normalized();
            
            // 4. Calculate the Dot Product
            float hitAngle = carForward.Dot(directionToEnemy);
            
            // The Dot Product returns a value between 1.0 and -1.0
            // 1.0 = Facing them perfectly
            // 0.0 = Hit them directly on the side
            // -1.0 = Hit them perfectly in the rear
            
            if (hitAngle > 0.7f) // Roughly a 45-degree cone in front of the car
            {
                // Successful Ram!
                GD.Print("Front Impact: Dealt Damage!");
                
                // If the enemy has a TakeDamage method (like our DummyEnemy), call it
                if (body.HasMethod("TakeDamage"))
                {
                    body.Call("TakeDamage", RamDamage);
                }
            }
            else
            {
                // Bad Impact! We got T-Boned or rear-ended.
                GD.Print("Side/Rear Impact: Player takes damage!");
                
                // Emit a signal so the UI can lower the health bar
                EmitSignal(SignalName.PlayerTookDamage);
            }
        }
    }

    // Don't forget to declare the custom signal at the top of your class if it isn't elsewhere!
    [Signal]
    public delegate void PlayerTookDamageEventHandler();
}