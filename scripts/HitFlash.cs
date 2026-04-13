using Godot;

/// <summary>
/// Flashes the parent CanvasItem white when damage is taken.
/// Attach as a sibling or child near the sprite. Connects to a
/// Damageable node's TookDamage signal automatically if one exists
/// as a sibling.
/// </summary>
public partial class HitFlash : Node
{
    [Export] public float FlashDuration = 0.08f;
    [Export] public Color FlashColor = Colors.White;

    private CanvasItem _target;
    private Color _originalModulate;
    private float _timer;
    private bool _flashing;

    public override void _Ready()
    {
        // Find the CanvasItem to flash (parent or first CanvasItem sibling)
        _target = GetParent() as CanvasItem;
        if (_target == null)
        {
            foreach (var child in GetParent().GetChildren())
            {
                if (child is CanvasItem ci && child != this)
                {
                    _target = ci;
                    break;
                }
            }
        }

        if (_target != null)
            _originalModulate = _target.Modulate;

        // Auto-connect to sibling Damageable if present
        var damageable = GetParent().GetNodeOrNull<Damageable>("Damageable");
        if (damageable != null)
            damageable.TookDamage += OnTookDamage;
    }

    private void OnTookDamage(float amount, Vector2 from)
    {
        Flash();
    }

    public void Flash()
    {
        if (_target == null) return;
        _target.Modulate = FlashColor;
        _timer = FlashDuration;
        _flashing = true;
    }

    public override void _Process(double delta)
    {
        if (!_flashing) return;

        _timer -= (float)delta;
        if (_timer <= 0f)
        {
            _target.Modulate = _originalModulate;
            _flashing = false;
        }
    }
}