using Godot;

/// <summary>
/// Pistol turret mounted on the player car. Implements <see cref="IWeapon"/>.
///
/// Automatically rotates toward the nearest enemy (aim assist) at 10 Hz
/// to avoid allocating a Godot Array every frame.
///
/// Input is intentionally NOT read here. PlayerCar reads the shoot/reload
/// actions and calls <see cref="TryFire"/> / <see cref="StartManualReload"/>.
/// This keeps weapon logic decoupled from the input system and makes it
/// usable by AI-controlled vehicles without modification.
/// </summary>
public partial class PistolWeapon : Node2D, IWeapon
{
    [Export] public int MaxAmmo { get; set; } = 10;
    [Export] public float BulletDamage = 25f;
    [Export] public float BulletSpeed  = 720f;
    [Export] public float ReloadTime   = 1.8f;
    [Export] public float AimRange     = 600f;

    [Signal] public delegate void AmmoChangedEventHandler(int current, int max, bool reloading);
    [Signal] public delegate void ReloadProgressChangedEventHandler(float t); // 0→1 during reload

    // IWeapon
    public int  CurrentAmmo { get; private set; }
    public bool IsReloading { get; private set; }

    private float  _reloadTimer;
    private Node2D _turret;
    private Node2D _muzzle;

    // Aim-assist cache — re-scanned at AimScanHz instead of every frame
    private const float AimScanInterval = 0.10f;  // 10 Hz
    private float  _aimScanTimer;
    private Node2D _aimTarget;

    // ──────────────────────────────────────────────────────────────────────────

    public override void _Ready()
    {
        CurrentAmmo = MaxAmmo;

        _EnsureActions();
        _BuildVisuals();
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;

        if (IsReloading)
        {
            _reloadTimer -= dt;
            EmitSignal(SignalName.ReloadProgressChanged, 1f - (_reloadTimer / ReloadTime));
            if (_reloadTimer <= 0f)
                _CompleteReload();
            return;
        }

        _UpdateAim(dt);
    }

    // ── IWeapon ───────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public void TryFire()
    {
        if (!IsReloading && CurrentAmmo > 0)
            _Fire();
    }

    /// <inheritdoc/>
    public void StartManualReload()
    {
        if (!IsReloading && CurrentAmmo < MaxAmmo)
            _StartReload();
    }

    // ── Aim assist ────────────────────────────────────────────────────────────

    private void _UpdateAim(float dt)
    {
        if (_turret == null) return;

        // Re-scan for a new target only at AimScanInterval, not every frame.
        // GetNodesInGroup allocates a new Godot Array each call; 10 Hz keeps it cheap.
        _aimScanTimer -= dt;
        if (_aimScanTimer <= 0f)
        {
            _aimTarget    = _FindNearestEnemy();
            _aimScanTimer = AimScanInterval;
        }

        float targetAngle;
        if (_aimTarget != null && GodotObject.IsInstanceValid(_aimTarget))
        {
            var dir = (_aimTarget.GlobalPosition - _turret.GlobalPosition).Normalized();
            targetAngle = Mathf.Atan2(dir.Y, dir.X);
        }
        else
        {
            _aimTarget  = null;  // clear stale reference
            targetAngle = GetParent<Node2D>()?.GlobalRotation ?? 0f;
        }

        _turret.GlobalRotation = Mathf.LerpAngle(
            _turret.GlobalRotation, targetAngle, 14f * dt);
    }

    private Node2D _FindNearestEnemy()
    {
        var enemies = GetTree().GetNodesInGroup("enemies");
        Node2D closest = null;
        float minDist = AimRange;

        foreach (var node in enemies)
        {
            if (node is not Node2D e) continue;
            if (!GodotObject.IsInstanceValid(e)) continue;
            float d = GlobalPosition.DistanceTo(e.GlobalPosition);
            if (d < minDist) { minDist = d; closest = e; }
        }
        return closest;
    }

    // ── Firing & reload ────────────────────────────────────────────────────────

    private void _Fire()
    {
        if (_muzzle == null) return;

        CurrentAmmo--;
        EmitSignal(SignalName.AmmoChanged, CurrentAmmo, MaxAmmo, false);

        _ShowMuzzleFlash();

        var bulletDir = new Vector2(
            Mathf.Cos(_turret.GlobalRotation),
            Mathf.Sin(_turret.GlobalRotation));

        var bullet = new Bullet
        {
            Direction = bulletDir,
            Speed     = BulletSpeed,
            Damage    = BulletDamage,
        };

        GetTree().CurrentScene.AddChild(bullet);
        bullet.GlobalPosition = _muzzle.GlobalPosition;

        if (CurrentAmmo == 0)
            _StartReload();
    }

    private void _StartReload()
    {
        IsReloading  = true;
        _reloadTimer = ReloadTime;
        EmitSignal(SignalName.AmmoChanged, 0, MaxAmmo, true);
        EmitSignal(SignalName.ReloadProgressChanged, 0f);
    }

    private void _CompleteReload()
    {
        CurrentAmmo = MaxAmmo;
        IsReloading = false;
        EmitSignal(SignalName.AmmoChanged, MaxAmmo, MaxAmmo, false);
        EmitSignal(SignalName.ReloadProgressChanged, 1f);
    }

    // ── Visual construction ────────────────────────────────────────────────────

    private void _BuildVisuals()
    {
        _turret = new Node2D { Name = "Turret", ZIndex = 2 };
        AddChild(_turret);

        _turret.AddChild(new ColorRect
        {
            Color    = new Color(0.20f, 0.20f, 0.20f),
            Size     = new Vector2(11f, 9f),
            Position = new Vector2(-5.5f, -4.5f),
        });
        _turret.AddChild(new ColorRect
        {
            Color    = new Color(0.50f, 0.48f, 0.44f),
            Size     = new Vector2(11f, 2f),
            Position = new Vector2(-5.5f, -4.5f),
        });
        _turret.AddChild(new ColorRect
        {
            Color    = new Color(0.14f, 0.14f, 0.14f),
            Size     = new Vector2(18f, 4f),
            Position = new Vector2(5f, -2f),
        });
        _turret.AddChild(new ColorRect
        {
            Color    = new Color(0.40f, 0.38f, 0.34f),
            Size     = new Vector2(18f, 1f),
            Position = new Vector2(5f, -2f),
        });

        _muzzle = new Node2D { Name = "Muzzle", Position = new Vector2(23f, 0f) };
        _turret.AddChild(_muzzle);

        _muzzle.AddChild(new Polygon2D
        {
            Name    = "MuzzleFlash",
            Polygon = new Vector2[] { new(0f, -5f), new(13f, 0f), new(0f, 5f) },
            Color   = new Color(1f, 0.88f, 0.28f, 0.92f),
            Visible = false,
        });
    }

    private void _ShowMuzzleFlash()
    {
        var flash = _muzzle?.GetNodeOrNull<Polygon2D>("MuzzleFlash");
        if (flash == null) return;
        flash.Visible = true;
        GetTree().CreateTimer(0.06f).Timeout += () =>
        {
            if (GodotObject.IsInstanceValid(flash))
                flash.Visible = false;
        };
    }

    // ── Input action registration ─────────────────────────────────────────────

    private static void _EnsureActions()
    {
        if (!InputMap.HasAction("shoot"))
        {
            InputMap.AddAction("shoot");
            InputMap.ActionAddEvent("shoot",
                new InputEventMouseButton { ButtonIndex = MouseButton.Left });
        }

        if (!InputMap.HasAction("reload"))
        {
            InputMap.AddAction("reload");
            InputMap.ActionAddEvent("reload",
                new InputEventKey { Keycode = Key.R });
        }
    }
}
