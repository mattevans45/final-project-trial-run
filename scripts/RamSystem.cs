using Godot;

/// <summary>
/// Directional ram / collision damage system for the player car.
/// 
/// After MoveAndSlide(), inspects each collision to determine:
///   • FRONT hit (dot > FrontThreshold)  → deal damage to enemy, bounce player
///   • SIDE  hit (between thresholds)    → reduced damage both ways
///   • REAR  hit (dot &lt; RearThreshold)   → player takes damage, enemy unharmed
///
/// Attach as a child Node of the PlayerCar. Requires a sibling Damageable node.
/// </summary>
public partial class RamSystem : Node
{
    [ExportGroup("Damage")]
    /// <summary>Base ram damage before speed scaling.</summary>
    [Export] public float RamDamage = 30f;
    /// <summary>Minimum speed fraction for damage. Even slow hits deal this much.</summary>
    [Export] public float MinSpeedDamage = 0.3f;
    /// <summary>Damage multiplier applied to the PLAYER on side impacts.</summary>
    [Export] public float SideDamageMul = 0.5f;
    /// <summary>Damage multiplier applied to the PLAYER on rear impacts.</summary>
    [Export] public float RearDamageMul = 1.0f;

    [ExportGroup("Angle Thresholds")]
    /// <summary>Dot product above this = front ram (player deals damage).</summary>
    [Export] public float FrontThreshold = 0.5f;
    /// <summary>Dot product below this = rear hit (player takes damage).</summary>
    [Export] public float RearThreshold = -0.3f;

    [ExportGroup("Knockback")]
    /// <summary>Impulse applied to the enemy on a front ram.</summary>
    [Export] public float EnemyKnockback = 350f;
    /// <summary>Velocity retained by the player after a front ram (0–1).</summary>
    [Export] public float PlayerBounceRetain = 0.6f;
    /// <summary>Minimum impact speed to trigger the ram system.</summary>
    [Export] public float MinImpactSpeed = 60f;

    [Signal] public delegate void RamHitEventHandler(Node2D enemy, float damage, float dot);
    [Signal] public delegate void PlayerHitEventHandler(Node2D enemy, float damage, float dot);

    private CharacterBody2D _car;
    private Damageable _playerHealth;
    private SmoothCamera _camera;

    public override void _Ready()
    {
        _car = GetParent<CharacterBody2D>();
        _playerHealth = GetParent().GetNodeOrNull<Damageable>("Damageable");
        _camera = GetParent().GetNodeOrNull<SmoothCamera>("Camera2D");

        if (_playerHealth == null)
            GD.PushWarning("RamSystem: No Damageable sibling found on player car.");
    }

    /// <summary>
    /// Call this from PlayerCar._PhysicsProcess AFTER MoveAndSlide().
    /// Processes all slide collisions for the current frame.
    /// </summary>
    public void ProcessCollisions()
    {
        if (_car == null) return;

        int hitCount = _car.GetSlideCollisionCount();
        if (hitCount == 0) return;

        Vector2 carForward = _car.Transform.X.Normalized();
        float carSpeed = _car.Velocity.Length();

        for (int i = 0; i < hitCount; i++)
        {
            var collision = _car.GetSlideCollision(i);
            var collider = collision.GetCollider();

            // Only process enemy bodies (check by group membership)
            if (collider is not Node2D enemyNode) continue;
            if (!enemyNode.IsInGroup("enemies")) continue;

            // Skip low-speed bumps
            if (carSpeed < MinImpactSpeed) continue;

            // Direction from player to enemy
            Vector2 toEnemy = (enemyNode.GlobalPosition - _car.GlobalPosition).Normalized();

            // Dot product: +1 = enemy is directly in front, -1 = directly behind
            float dot = carForward.Dot(toEnemy);

            // Speed-scaled damage: faster = more damage
            float speedFrac = Mathf.Max(MinSpeedDamage, carSpeed / GetCarMaxSpeed());
            float baseDmg = RamDamage * speedFrac;

            if (dot > FrontThreshold)
            {
                // ── FRONT RAM: player deals damage to enemy ──────────────
                _DealDamageToEnemy(enemyNode, baseDmg, dot);
                _ApplyKnockback(enemyNode, toEnemy, carSpeed);
                _BouncePlayer(collision.GetNormal());
                _camera?.AddTrauma(0.25f + 0.2f * speedFrac);

                EmitSignal(SignalName.RamHit, enemyNode, baseDmg, dot);
            }
            else if (dot > RearThreshold)
            {
                // ── SIDE HIT: both take reduced damage ───────────────────
                _DealDamageToEnemy(enemyNode, baseDmg * 0.3f, dot);
                _playerHealth?.TakeDamage(baseDmg * SideDamageMul, enemyNode.GlobalPosition);
                _ApplyKnockback(enemyNode, toEnemy, carSpeed * 0.4f);
                _camera?.AddTrauma(0.15f + 0.15f * speedFrac);

                EmitSignal(SignalName.PlayerHit, enemyNode, baseDmg * SideDamageMul, dot);
            }
            else
            {
                // ── REAR HIT: player takes full damage, enemy unharmed ───
                _playerHealth?.TakeDamage(baseDmg * RearDamageMul, enemyNode.GlobalPosition);
                _camera?.AddTrauma(0.3f + 0.25f * speedFrac);

                EmitSignal(SignalName.PlayerHit, enemyNode, baseDmg * RearDamageMul, dot);
            }
        }
    }

    private void _DealDamageToEnemy(Node2D enemy, float damage, float dot)
    {
        var damageable = enemy.GetNodeOrNull<Damageable>("Damageable");
        if (damageable != null)
        {
            damageable.TakeDamage(damage, _car.GlobalPosition);
        }
    }

    private void _ApplyKnockback(Node2D enemy, Vector2 direction, float speed)
    {
        // If enemy is a CharacterBody2D, apply velocity directly
        if (enemy is CharacterBody2D cb)
        {
            float knockForce = EnemyKnockback * Mathf.Clamp(speed / GetCarMaxSpeed(), 0.3f, 1.5f);
            cb.Velocity += direction * knockForce;
        }
    }

    private void _BouncePlayer(Vector2 collisionNormal)
    {
        // Reflect some velocity off the collision normal for a satisfying bounce
        Vector2 vel = _car.Velocity;
        float into = vel.Dot(collisionNormal);
        if (into < 0f)
        {
            // Remove into-wall component and scale remainder
            _car.Velocity = (vel - into * collisionNormal) * PlayerBounceRetain;
        }
    }

    private float GetCarMaxSpeed()
    {
        if (_car is PlayerCar pc)
            return pc.MaxSpeed;
        return 460f; // fallback
    }
}