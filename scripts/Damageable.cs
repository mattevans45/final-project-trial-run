using Godot;

/// <summary>
/// Universal health component. Attach as a child of any Node2D that can
/// take damage (player car, enemies, destructible props).
/// 
/// Usage:
///   var dmg = someNode.GetNode&lt;Damageable&gt;("Damageable");
///   dmg.TakeDamage(25f, attackerPosition);
/// </summary>
public partial class Damageable : Node
{
    [Export] public float MaxHealth = 100f;
    [Export] public float CurrentHealth { get; private set; }
    /// <summary>Seconds of invincibility after taking damage. Prevents
    /// multi-hit from a single collision frame.</summary>
    [Export] public float IFrameDuration = 0.15f;

    [Signal] public delegate void HealthChangedEventHandler(float current, float max);
    [Signal] public delegate void TookDamageEventHandler(float amount, Vector2 from);
    [Signal] public delegate void DiedEventHandler();

    public bool IsAlive => CurrentHealth > 0f;
    public float HealthFraction => MaxHealth > 0f ? CurrentHealth / MaxHealth : 0f;

    private float _iFrameTimer;

    public override void _Ready()
    {
        CurrentHealth = MaxHealth;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_iFrameTimer > 0f)
            _iFrameTimer -= (float)delta;
    }

    /// <summary>
    /// Deal damage to this entity. Returns actual damage dealt (after i-frame check).
    /// </summary>
    /// <param name="amount">Raw damage amount.</param>
    /// <param name="from">World position of the damage source (for directional feedback).</param>
    public float TakeDamage(float amount, Vector2 from = default)
    {
        if (!IsAlive || amount <= 0f) return 0f;
        if (_iFrameTimer > 0f) return 0f;

        float actual = Mathf.Min(amount, CurrentHealth);
        CurrentHealth -= actual;
        _iFrameTimer = IFrameDuration;

        EmitSignal(SignalName.TookDamage, actual, from);
        EmitSignal(SignalName.HealthChanged, CurrentHealth, MaxHealth);

        if (CurrentHealth <= 0f)
        {
            CurrentHealth = 0f;
            EmitSignal(SignalName.Died);
        }

        return actual;
    }

    /// <summary>Heal, clamped to MaxHealth.</summary>
    public void Heal(float amount)
    {
        if (!IsAlive || amount <= 0f) return;
        CurrentHealth = Mathf.Min(CurrentHealth + amount, MaxHealth);
        EmitSignal(SignalName.HealthChanged, CurrentHealth, MaxHealth);
    }

    /// <summary>Reset to full health (e.g. on respawn or pool return).</summary>
    public void Reset()
    {
        CurrentHealth = MaxHealth;
        _iFrameTimer = 0f;
        EmitSignal(SignalName.HealthChanged, CurrentHealth, MaxHealth);
    }
}