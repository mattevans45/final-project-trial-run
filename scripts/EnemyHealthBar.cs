using Godot;

/// <summary>
/// Procedural floating health and armor bar rendered above an enemy in world space.
/// Spawned programmatically from DummyEnemy._Ready — no scene edits needed.
///
/// Layout (above the enemy sprite):
///
///   ┌──────────────────────┐   ← dark background
///   │ ████████░░░░         │   ← health fill  (green→yellow→red)
///   ├──────────────────────┤
///   │ ████░░░░░░░░         │   ← armor fill   (steel blue, only if Armor > 0)
///   └──────────────────────┘
///
/// The bar is hidden at full health and appears as soon as the first hit lands.
/// </summary>
public partial class EnemyHealthBar : Node2D
{
    // ── Layout constants (world pixels) ───────────────────────────────────────
    private const float BarW    = 46f;
    private const float HpH     = 5f;
    private const float ArmH    = 3f;
    private const float Pad     = 1f;
    private const float YOffset = -34f;    // above sprite centre

    private ColorRect _bgRect;
    private ColorRect _hpFill;
    private ColorRect _armBg;
    private ColorRect _armFill;

    // ── Setup ─────────────────────────────────────────────────────────────────

    public override void _Ready()
    {
        ZIndex = 3;   // above enemy sprite and shadows

        float bgW = BarW + Pad * 2;

        // ── Background ──────────────────────────────────────────────────────
        _bgRect = _Rect(
            new Color(0.05f, 0.05f, 0.05f, 0.88f),
            new Vector2(bgW, HpH + Pad * 2),
            new Vector2(-bgW * 0.5f, YOffset - Pad));
        AddChild(_bgRect);

        // ── Health fill ─────────────────────────────────────────────────────
        _hpFill = _Rect(
            new Color(0.20f, 0.85f, 0.28f, 1f),
            new Vector2(BarW, HpH),
            new Vector2(-BarW * 0.5f, YOffset));
        AddChild(_hpFill);

        // ── Armor background (hidden until we know there is armor) ──────────
        float armY = YOffset + HpH + Pad + 1f;
        _armBg = _Rect(
            new Color(0.10f, 0.16f, 0.25f, 0.88f),
            new Vector2(bgW, ArmH + Pad * 2),
            new Vector2(-bgW * 0.5f, armY - Pad));
        _armBg.Visible = false;
        AddChild(_armBg);

        // ── Armor fill ──────────────────────────────────────────────────────
        _armFill = _Rect(
            new Color(0.30f, 0.55f, 0.92f, 1f),
            new Vector2(BarW, ArmH),
            new Vector2(-BarW * 0.5f, armY));
        _armFill.Visible = false;
        AddChild(_armFill);

        // ── Connect to the parent's Damageable ──────────────────────────────
        var dmg = GetParent().GetNodeOrNull<Damageable>("Damageable");
        if (dmg == null) return;

        dmg.HealthChanged += _OnHealthChanged;

        if (dmg.Armor > 0f)
        {
            // Armor bar width represents relative toughness (caps at BarW)
            float armorFrac = Mathf.Clamp(dmg.Armor / 30f, 0.15f, 1f);
            _armFill.Size = new Vector2(BarW * armorFrac, ArmH);
            _armBg.Visible  = true;
            _armFill.Visible = true;
        }

        Visible = false;   // hidden until the first hit
    }

    // ── Callbacks ─────────────────────────────────────────────────────────────

    private void _OnHealthChanged(float current, float max)
    {
        Visible = true;   // reveal once any damage is taken

        float frac = max > 0f ? Mathf.Max(0f, current / max) : 0f;
        _hpFill.Size  = new Vector2(BarW * frac, HpH);
        // Smooth hue gradient: green (0.33) → yellow (0.17) → red (0)
        _hpFill.Color = Color.FromHsv(frac * 0.33f, 0.88f, 0.90f, 1f);
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private static ColorRect _Rect(Color color, Vector2 size, Vector2 pos)
        => new ColorRect { Color = color, Size = size, Position = pos };
}
