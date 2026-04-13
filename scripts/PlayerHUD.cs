using Godot;

/// <summary>
/// Screen-space HUD showing the player's health, armor, ammo, and status messages.
///
/// Bottom-left  — health / armor bars.
/// Bottom-right — pistol ammo dots + reload bar.
/// Status line auto-fades above the health panel.
/// </summary>
public partial class PlayerHUD : CanvasLayer
{
    // ── Bar dimensions ─────────────────────────────────────────────────────
    private const float BarW   = 130f;
    private const float HpH    = 12f;
    private const float ArmH   =  8f;
    private const float PadX   = 14f;
    private const float PadY   = 14f;
    private const float RowGap =  6f;
    private const float LabelW = 32f;

    // Ammo panel (bottom-right)
    private const float DotW   =  8f;   // bullet dot width
    private const float DotH   = 12f;   // bullet dot height
    private const float DotGap =  3f;   // gap between dots
    private const int   MaxDots = 10;

    // ── Nodes ─────────────────────────────────────────────────────────────
    private ColorRect   _hpFill;
    private ColorRect   _armFill;
    private ColorRect   _armRow;
    private Label       _hpLabel;
    private Label       _armLabel;
    private Label       _statusLabel;
    private float       _statusTimer;

    // Tracks which Damageable is currently subscribed so we can cleanly
    // unsubscribe before re-connecting (prevents double-update on ApplyVehicleData)
    private Damageable _connectedDamageable;

    // Ammo
    private ColorRect[] _ammoDots;
    private ColorRect   _reloadBarFill;
    private Label       _reloadLabel;
    private float       _reloadBarLeft;   // stored so UpdateReloadProgress can resize
    private float       _reloadBarTotalW;

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>
    /// Connect the HUD to a Damageable. Safe to call multiple times
    /// (e.g. when ApplyVehicleData refreshes the car's stats) — the previous
    /// subscription is cleanly removed before the new one is added.
    /// </summary>
    public void Connect(Damageable damageable)
    {
        // Unsubscribe from any previously connected damageable
        if (_connectedDamageable != null && GodotObject.IsInstanceValid(_connectedDamageable))
            _connectedDamageable.HealthChanged -= _OnHealthChanged;

        _connectedDamageable = damageable;
        damageable.HealthChanged += _OnHealthChanged;
        _OnHealthChanged(damageable.CurrentHealth, damageable.MaxHealth);

        bool hasArmor = damageable.Armor > 0f;
        _armRow.Visible = hasArmor;
        if (hasArmor)
            _armLabel.Text = $"ARM  ◆ {Mathf.RoundToInt(damageable.Armor)}";
    }

    /// <summary>
    /// Show a temporary message above the health panel (auto-fades after
    /// <paramref name="duration"/> seconds).
    /// </summary>
    public void ShowStatus(string message, float duration = 1.8f)
    {
        _statusLabel.Text     = message;
        _statusLabel.Modulate = Colors.White;
        _statusLabel.Visible  = true;
        _statusTimer = duration;
    }

    /// <summary>Called by PistolWeapon.AmmoChanged signal.</summary>
    public void UpdateAmmo(int current, int max, bool reloading)
    {
        if (_ammoDots == null) return;

        for (int i = 0; i < _ammoDots.Length; i++)
        {
            if (reloading)
            {
                // All dots dim during reload
                _ammoDots[i].Color = new Color(0.25f, 0.22f, 0.08f, 0.7f);
            }
            else
            {
                _ammoDots[i].Color = i < current
                    ? new Color(0.98f, 0.85f, 0.15f, 1f)   // loaded — bright yellow
                    : new Color(0.15f, 0.15f, 0.12f, 0.7f); // spent  — near-black
            }
        }

        if (_reloadLabel != null)
            _reloadLabel.Visible = reloading;

        if (!reloading && _reloadBarFill != null)
            _reloadBarFill.Size = new Vector2(_reloadBarFill.Size.X, 3f); // keep width, reset
    }

    /// <summary>Called by PistolWeapon.ReloadProgressChanged signal (0→1).</summary>
    public void UpdateReloadProgress(float t)
    {
        if (_reloadBarFill == null) return;

        float clamped = Mathf.Clamp(t, 0f, 1f);
        // OffsetRight drives the visual width because the rect uses right-side anchors
        _reloadBarFill.OffsetRight = _reloadBarLeft + _reloadBarTotalW * clamped;

        // Color shifts orange → bright gold as reload completes
        _reloadBarFill.Color = Color.FromHsv(
            Mathf.Lerp(0.08f, 0.14f, clamped), 0.92f, Mathf.Lerp(0.55f, 1f, clamped));
    }

    // ── Godot callbacks ───────────────────────────────────────────────────

    public override void _Ready()
    {
        Layer = 10;

        var root = new Control();
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);

        _BuildHealthPanel(root);
        _BuildAmmoPanel(root);
    }

    public override void _Process(double delta)
    {
        if (_statusTimer <= 0f) return;

        _statusTimer -= (float)delta;
        float alpha = Mathf.Clamp(_statusTimer / 0.4f, 0f, 1f);
        _statusLabel.Modulate = new Color(1f, 0.92f, 0.25f, alpha);
        if (_statusTimer <= 0f)
            _statusLabel.Visible = false;
    }

    // ── Construction ──────────────────────────────────────────────────────

    private void _BuildHealthPanel(Control root)
    {
        float panelH = HpH + 20f + ArmH + 20f + RowGap + PadY;
        float panelW = LabelW + BarW + 48f;

        var bg = new ColorRect
        {
            Color        = new Color(0f, 0f, 0f, 0.55f),
            AnchorLeft   = 0f, AnchorRight  = 0f,
            AnchorTop    = 1f, AnchorBottom = 1f,
            OffsetLeft   = PadX,
            OffsetRight  = PadX + panelW,
            OffsetTop    = -(panelH + PadY),
            OffsetBottom = -PadY,
        };
        root.AddChild(bg);

        // Health row
        float hpY = -(panelH + PadY) + 8f;
        _AddLabel(root, "HP", PadX + 4f, hpY + (HpH - 14f) * 0.5f,
                  new Color(0.85f, 0.85f, 0.85f));
        _Bar(root, new Color(0.08f, 0.08f, 0.08f, 1f),
             new Vector2(BarW, HpH), new Vector2(PadX + LabelW, hpY));
        _hpFill = _Bar(root, new Color(0.20f, 0.85f, 0.28f, 1f),
                       new Vector2(BarW, HpH), new Vector2(PadX + LabelW, hpY));
        _hpLabel = _AddLabel(root, "100/100",
                             PadX + LabelW + BarW + 6f,
                             hpY + (HpH - 14f) * 0.5f,
                             new Color(0.88f, 0.88f, 0.88f));

        // Armor row
        float armY = hpY + HpH + RowGap + 4f;
        _armRow = new ColorRect { Color = Colors.Transparent, Visible = false };
        _armRow.AnchorLeft = 0f; _armRow.AnchorRight  = 0f;
        _armRow.AnchorTop  = 1f; _armRow.AnchorBottom = 1f;
        root.AddChild(_armRow);
        _AddLabel(root, "ARM", PadX + 4f, armY + (ArmH - 12f) * 0.5f,
                  new Color(0.55f, 0.75f, 1.00f));
        _Bar(root, new Color(0.08f, 0.12f, 0.20f, 1f),
             new Vector2(BarW, ArmH), new Vector2(PadX + LabelW, armY));
        _armFill = _Bar(root, new Color(0.30f, 0.55f, 0.92f, 1f),
                        new Vector2(BarW, ArmH), new Vector2(PadX + LabelW, armY));
        _armLabel = _AddLabel(root, "",
                              PadX + LabelW + BarW + 6f,
                              armY + (ArmH - 12f) * 0.5f,
                              new Color(0.55f, 0.75f, 1.00f));

        // Status line above panel
        float statusY = -(panelH + PadY) - 22f;
        _statusLabel = new Label
        {
            Text         = "",
            Visible      = false,
            AnchorLeft   = 0f, AnchorRight  = 0f,
            AnchorTop    = 1f, AnchorBottom = 1f,
            OffsetLeft   = PadX,
            OffsetTop    = statusY,
            OffsetBottom = statusY + 20f,
            OffsetRight  = PadX + panelW,
        };
        _statusLabel.AddThemeColorOverride("font_color", new Color(1f, 0.92f, 0.25f));
        root.AddChild(_statusLabel);
    }

    private void _BuildAmmoPanel(Control root)
    {
        float totalDotW = MaxDots * DotW + (MaxDots - 1) * DotGap;
        float panelW    = totalDotW + 16f;
        // rows: label(14) + gap(2) + dots(DotH) + gap(4) + reloadBar(3) + gap(4) + reloadLbl(14) + pads
        float panelH    = 14f + 2f + DotH + 4f + 3f + 4f + 14f + PadY;

        // Background panel — anchored to bottom-right
        var bg = new ColorRect
        {
            Color        = new Color(0f, 0f, 0f, 0.55f),
            AnchorLeft   = 1f, AnchorRight  = 1f,
            AnchorTop    = 1f, AnchorBottom = 1f,
            OffsetLeft   = -(panelW + PadX),
            OffsetRight  = -PadX,
            OffsetTop    = -(panelH + PadY),
            OffsetBottom = -PadY,
        };
        root.AddChild(bg);

        float innerL = -(panelW + PadX) + 8f;  // left edge of inner content (screen-relative offset from right)

        // "AMMO" label
        float labelY = -(panelH + PadY) + 4f;
        var ammoLbl = new Label
        {
            Text         = "AMMO",
            AnchorLeft   = 1f, AnchorRight  = 1f,
            AnchorTop    = 1f, AnchorBottom = 1f,
            OffsetLeft   = innerL,
            OffsetRight  = -PadX,
            OffsetTop    = labelY,
            OffsetBottom = labelY + 14f,
        };
        ammoLbl.AddThemeColorOverride("font_color", new Color(0.80f, 0.78f, 0.60f));
        ammoLbl.AddThemeFontSizeOverride("font_size", 10);
        root.AddChild(ammoLbl);

        // Bullet dot row
        float dotsY = labelY + 16f;
        _ammoDots = new ColorRect[MaxDots];
        for (int i = 0; i < MaxDots; i++)
        {
            float x = innerL + i * (DotW + DotGap);
            var dot = new ColorRect
            {
                Color        = new Color(0.98f, 0.85f, 0.15f, 1f),
                AnchorLeft   = 1f, AnchorRight  = 1f,
                AnchorTop    = 1f, AnchorBottom = 1f,
                OffsetLeft   = x,
                OffsetRight  = x + DotW,
                OffsetTop    = dotsY,
                OffsetBottom = dotsY + DotH,
            };
            root.AddChild(dot);
            _ammoDots[i] = dot;
        }

        // Reload bar (thin track + fill, below dots)
        float reloadBgY     = dotsY + DotH + 4f;
        _reloadBarLeft      = innerL;
        _reloadBarTotalW    = totalDotW;

        // Track
        var reloadBg = new ColorRect
        {
            Color        = new Color(0.10f, 0.10f, 0.08f, 1f),
            AnchorLeft   = 1f, AnchorRight  = 1f,
            AnchorTop    = 1f, AnchorBottom = 1f,
            OffsetLeft   = _reloadBarLeft,
            OffsetRight  = _reloadBarLeft + _reloadBarTotalW,
            OffsetTop    = reloadBgY,
            OffsetBottom = reloadBgY + 3f,
        };
        root.AddChild(reloadBg);

        // Fill — OffsetRight starts equal to OffsetLeft (zero width); expanded in UpdateReloadProgress
        _reloadBarFill = new ColorRect
        {
            Color        = new Color(0.98f, 0.85f, 0.15f, 1f),
            AnchorLeft   = 1f, AnchorRight  = 1f,
            AnchorTop    = 1f, AnchorBottom = 1f,
            OffsetLeft   = _reloadBarLeft,
            OffsetRight  = _reloadBarLeft,  // zero width
            OffsetTop    = reloadBgY,
            OffsetBottom = reloadBgY + 3f,
        };
        root.AddChild(_reloadBarFill);

        // "RELOADING..." text
        float rlY = reloadBgY + 5f;
        _reloadLabel = new Label
        {
            Text         = "RELOADING...",
            Visible      = false,
            AnchorLeft   = 1f, AnchorRight  = 1f,
            AnchorTop    = 1f, AnchorBottom = 1f,
            OffsetLeft   = innerL,
            OffsetRight  = -PadX,
            OffsetTop    = rlY,
            OffsetBottom = rlY + 14f,
        };
        _reloadLabel.AddThemeColorOverride("font_color", new Color(1f, 0.65f, 0.10f));
        _reloadLabel.AddThemeFontSizeOverride("font_size", 10);
        root.AddChild(_reloadLabel);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private void _OnHealthChanged(float current, float max)
    {
        if (max <= 0f) return;
        float frac = Mathf.Max(0f, current / max);
        _hpFill.Size  = new Vector2(BarW * frac, HpH);
        _hpFill.Color = Color.FromHsv(frac * 0.33f, 0.88f, 0.90f, 1f);
        _hpLabel.Text = $"{Mathf.RoundToInt(current)}/{Mathf.RoundToInt(max)}";
    }

    private static ColorRect _Bar(Control parent, Color color,
                                   Vector2 size, Vector2 screenPos)
    {
        var r = new ColorRect
        {
            Color        = color,
            Size         = size,
            AnchorLeft   = 0f, AnchorRight  = 0f,
            AnchorTop    = 1f, AnchorBottom = 1f,
            OffsetLeft   = screenPos.X,
            OffsetRight  = screenPos.X + size.X,
            OffsetTop    = screenPos.Y,
            OffsetBottom = screenPos.Y + size.Y,
        };
        parent.AddChild(r);
        return r;
    }

    private static Label _AddLabel(Control parent, string text,
                                    float x, float y, Color color)
    {
        var lbl = new Label
        {
            Text         = text,
            AnchorLeft   = 0f, AnchorRight  = 0f,
            AnchorTop    = 1f, AnchorBottom = 1f,
            OffsetLeft   = x,
            OffsetRight  = x + 100f,
            OffsetTop    = y,
            OffsetBottom = y + 20f,
        };
        lbl.AddThemeColorOverride("font_color", color);
        lbl.AddThemeFontSizeOverride("font_size", 12);
        parent.AddChild(lbl);
        return lbl;
    }
}
