using Godot;
using System.Collections.Generic;

/// <summary>
/// Full-screen vehicle selection UI. Reads from VehicleRegistry autoload.
/// Displays vehicle cards with stat bars and color-coded visuals.
/// 
/// Set this scene as the main scene or call it before the game scene.
/// On selection, it sets VehicleRegistry.SelectedVehicle and changes
/// to the gameplay scene.
/// </summary>
public partial class VehicleSelectScreen : Control
{
    [Export] public string GameScenePath = "res://scenes/main.tscn";
    [Export] public float CardWidth = 260f;
    [Export] public float CardSpacing = 20f;

    // Cyberpunk palette
    private static readonly Color BgDark     = new(0.04f, 0.05f, 0.08f);
    private static readonly Color PanelBg    = new(0.08f, 0.10f, 0.14f);
    private static readonly Color PanelBorder = new(0.0f, 0.85f, 0.95f, 0.4f);
    private static readonly Color AccentCyan  = new(0.0f, 0.9f, 1.0f);
    private static readonly Color AccentPink  = new(1.0f, 0.15f, 0.6f);
    private static readonly Color TextDim     = new(0.5f, 0.55f, 0.6f);
    private static readonly Color TextBright  = new(0.9f, 0.95f, 1.0f);

    private int _selectedIndex = 0;
    private List<VehicleData> _vehicles;
    private VBoxContainer _cardContainer;
    private HBoxContainer _cardsRow;
    private Label _descriptionLabel;
    private List<Panel> _cards = new();

    public override void _Ready()
    {
        // Full screen dark background
        var bg = new ColorRect();
        bg.Color = BgDark;
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        // Main vertical layout
        var vbox = new VBoxContainer();
        vbox.SetAnchorsPreset(LayoutPreset.FullRect);
        vbox.AddThemeConstantOverride("separation", 30);
        vbox.Set("offset_left", 60f);
        vbox.Set("offset_right", -60f);
        vbox.Set("offset_top", 40f);
        vbox.Set("offset_bottom", -40f);
        AddChild(vbox);

        // Title
        var title = new Label();
        title.Text = "SELECT VEHICLE";
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.AddThemeColorOverride("font_color", AccentCyan);
        title.AddThemeFontSizeOverride("font_size", 36);
        vbox.AddChild(title);

        // Subtitle
        var subtitle = new Label();
        subtitle.Text = "// SCRAP & SURVIVE — VEHICLE BAY";
        subtitle.HorizontalAlignment = HorizontalAlignment.Center;
        subtitle.AddThemeColorOverride("font_color", TextDim);
        subtitle.AddThemeFontSizeOverride("font_size", 14);
        vbox.AddChild(subtitle);

        // Spacer
        var spacer1 = new Control();
        spacer1.CustomMinimumSize = new Vector2(0, 10);
        vbox.AddChild(spacer1);

        // Cards row (centered)
        var centerContainer = new CenterContainer();
        centerContainer.SizeFlagsVertical = SizeFlags.ExpandFill;
        vbox.AddChild(centerContainer);

        _cardsRow = new HBoxContainer();
        _cardsRow.AddThemeConstantOverride("separation", (int)CardSpacing);
        centerContainer.AddChild(_cardsRow);

        // Description area
        _descriptionLabel = new Label();
        _descriptionLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _descriptionLabel.AddThemeColorOverride("font_color", TextDim);
        _descriptionLabel.AddThemeFontSizeOverride("font_size", 16);
        _descriptionLabel.CustomMinimumSize = new Vector2(0, 40);
        vbox.AddChild(_descriptionLabel);

        // Controls hint
        var hint = new Label();
        hint.Text = "← → SELECT  |  ENTER CONFIRM  |  ESC BACK";
        hint.HorizontalAlignment = HorizontalAlignment.Center;
        hint.AddThemeColorOverride("font_color", new Color(TextDim, 0.5f));
        hint.AddThemeFontSizeOverride("font_size", 13);
        vbox.AddChild(hint);

        // Load vehicles
        _vehicles = VehicleRegistry.Instance?.AllVehicles ?? new List<VehicleData>();
        if (_vehicles.Count == 0)
        {
            GD.PushWarning("VehicleSelectScreen: No vehicles in registry.");
            return;
        }

        // Build cards
        for (int i = 0; i < _vehicles.Count; i++)
            _BuildCard(_vehicles[i], i);

        _UpdateSelection();
    }

    private void _BuildCard(VehicleData data, int index)
    {
        var card = new Panel();
        card.CustomMinimumSize = new Vector2(CardWidth, 360);

        var stylebox = new StyleBoxFlat();
        stylebox.BgColor = PanelBg;
        stylebox.BorderColor = PanelBorder;
        stylebox.SetBorderWidthAll(1);
        stylebox.SetCornerRadiusAll(4);
        stylebox.ContentMarginLeft = 16;
        stylebox.ContentMarginRight = 16;
        stylebox.ContentMarginTop = 16;
        stylebox.ContentMarginBottom = 16;
        card.AddThemeStyleboxOverride("panel", stylebox);

        _cardsRow.AddChild(card);
        _cards.Add(card);

        // Inner layout
        var inner = new VBoxContainer();
        inner.SetAnchorsPreset(LayoutPreset.FullRect);
        inner.Set("offset_left", 16f);
        inner.Set("offset_right", -16f);
        inner.Set("offset_top", 16f);
        inner.Set("offset_bottom", -16f);
        inner.AddThemeConstantOverride("separation", 8);
        card.AddChild(inner);

        // Vehicle name
        var nameLabel = new Label();
        nameLabel.Text = data.DisplayName.ToUpper();
        nameLabel.HorizontalAlignment = HorizontalAlignment.Center;
        nameLabel.AddThemeColorOverride("font_color", TextBright);
        nameLabel.AddThemeFontSizeOverride("font_size", 18);
        inner.AddChild(nameLabel);

        // Color swatch (represents the vehicle's color scheme)
        var swatchRow = new HBoxContainer();
        swatchRow.Alignment = BoxContainer.AlignmentMode.Center;
        swatchRow.AddThemeConstantOverride("separation", 4);
        inner.AddChild(swatchRow);

        foreach (var col in new[] { data.BaseTint, data.AccentTint, data.GlowColor })
        {
            var swatch = new ColorRect();
            swatch.Color = col;
            swatch.CustomMinimumSize = new Vector2(28, 8);
            swatchRow.AddChild(swatch);
        }

        // Spacer
        var spacer = new Control();
        spacer.CustomMinimumSize = new Vector2(0, 8);
        inner.AddChild(spacer);

        // Stat bars
        _AddStatBar(inner, "SPEED",    data.SpeedRating,    AccentCyan);
        _AddStatBar(inner, "ACCEL",    data.AccelRating,    AccentCyan);
        _AddStatBar(inner, "HEALTH",   data.HealthRating,   new Color(0.2f, 0.9f, 0.3f));
        _AddStatBar(inner, "RAM DMG",  data.RamRating,      AccentPink);
        _AddStatBar(inner, "HANDLING", data.HandlingRating,  new Color(1f, 0.8f, 0.2f));

        // Tier label
        var tierLabel = new Label();
        tierLabel.Text = $"TIER {data.Tier}";
        tierLabel.HorizontalAlignment = HorizontalAlignment.Center;
        tierLabel.AddThemeColorOverride("font_color", TextDim);
        tierLabel.AddThemeFontSizeOverride("font_size", 11);
        inner.AddChild(tierLabel);
    }

    private void _AddStatBar(VBoxContainer parent, string label, float value, Color barColor)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        parent.AddChild(row);

        var lbl = new Label();
        lbl.Text = label;
        lbl.CustomMinimumSize = new Vector2(75, 0);
        lbl.AddThemeColorOverride("font_color", TextDim);
        lbl.AddThemeFontSizeOverride("font_size", 11);
        row.AddChild(lbl);

        // Bar background
        var barBg = new ColorRect();
        barBg.Color = new Color(0.15f, 0.15f, 0.2f);
        barBg.CustomMinimumSize = new Vector2(120, 10);
        barBg.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(barBg);

        // Bar fill (overlaid)
        var barFill = new ColorRect();
        barFill.Color = barColor;
        barFill.CustomMinimumSize = new Vector2(120 * value, 10);
        barFill.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        // Position fill on top of background
        barFill.SetAnchorsPreset(LayoutPreset.CenterLeft);
        barBg.AddChild(barFill);
        barFill.Position = Vector2.Zero;
        barFill.Size = new Vector2(120 * value, 10);
    }

    private void _UpdateSelection()
    {
        for (int i = 0; i < _cards.Count; i++)
        {
            var stylebox = _cards[i].GetThemeStylebox("panel") as StyleBoxFlat;
            if (stylebox == null) continue;

            if (i == _selectedIndex)
            {
                stylebox.BorderColor = AccentCyan;
                stylebox.SetBorderWidthAll(2);
                stylebox.BgColor = new Color(0.06f, 0.12f, 0.18f);
            }
            else
            {
                stylebox.BorderColor = PanelBorder;
                stylebox.SetBorderWidthAll(1);
                stylebox.BgColor = PanelBg;
            }
        }

        if (_selectedIndex >= 0 && _selectedIndex < _vehicles.Count)
            _descriptionLabel.Text = _vehicles[_selectedIndex].Description;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey key && key.Pressed)
        {
            if (key.Keycode == Key.Right || key.Keycode == Key.D)
            {
                _selectedIndex = (_selectedIndex + 1) % _vehicles.Count;
                _UpdateSelection();
                GetViewport().SetInputAsHandled();
            }
            else if (key.Keycode == Key.Left || key.Keycode == Key.A)
            {
                _selectedIndex = (_selectedIndex - 1 + _vehicles.Count) % _vehicles.Count;
                _UpdateSelection();
                GetViewport().SetInputAsHandled();
            }
            else if (key.Keycode == Key.Enter || key.Keycode == Key.Space)
            {
                GetViewport().SetInputAsHandled();
                _ConfirmSelection();
            }
        }
    }

    private void _ConfirmSelection()
    {
        VehicleRegistry.Instance?.SelectVehicle(_selectedIndex);
        GetTree().ChangeSceneToFile(GameScenePath);
    }
}