using Godot;
using System.Collections.Generic;

/// <summary>
/// Autoload singleton — vehicle catalogue and current selection.
/// Register in Project Settings → Autoload, Name: VehicleRegistry.
/// </summary>
public partial class VehicleRegistry : Node
{
    public static VehicleRegistry Instance { get; private set; }

    [Export] public string VehicleDataPath = "res://data/vehicles/";

    [Signal] public delegate void VehicleSelectedEventHandler(VehicleData data);

    public VehicleData SelectedVehicle { get; private set; }
    public List<VehicleData> AllVehicles { get; private set; } = new();

    public override void _Ready()
    {
        Instance = this;
        _LoadVehicleCatalogue();

        if (SelectedVehicle == null && AllVehicles.Count > 0)
            SelectedVehicle = AllVehicles[0];
    }

    public void SelectVehicle(string displayName)
    {
        foreach (var v in AllVehicles)
        {
            if (v.DisplayName == displayName)
            {
                SelectedVehicle = v;
                EmitSignal(SignalName.VehicleSelected, v);
                return;
            }
        }
        GD.PushWarning($"VehicleRegistry: No vehicle named '{displayName}'.");
    }

    public void SelectVehicle(int index)
    {
        if (index >= 0 && index < AllVehicles.Count)
        {
            SelectedVehicle = AllVehicles[index];
            EmitSignal(SignalName.VehicleSelected, SelectedVehicle);
        }
    }

    public List<VehicleData> GetUnlockedVehicles()
    {
        var unlocked = new List<VehicleData>();
        foreach (var v in AllVehicles)
            if (v.UnlockedByDefault) unlocked.Add(v);
        return unlocked;
    }

    private void _LoadVehicleCatalogue()
    {
        if (!DirAccess.DirExistsAbsolute(VehicleDataPath))
        {
            _CreateDefaultVehicles();
            return;
        }

        using var dir = DirAccess.Open(VehicleDataPath);
        if (dir == null) { _CreateDefaultVehicles(); return; }

        dir.ListDirBegin();
        string fileName = dir.GetNext();
        while (!string.IsNullOrEmpty(fileName))
        {
            if (fileName.EndsWith(".tres") || fileName.EndsWith(".res"))
            {
                var resource = GD.Load<VehicleData>(VehicleDataPath + fileName);
                if (resource != null) AllVehicles.Add(resource);
            }
            fileName = dir.GetNext();
        }
        dir.ListDirEnd();

        if (AllVehicles.Count == 0) _CreateDefaultVehicles();
        AllVehicles.Sort((a, b) => a.Tier.CompareTo(b.Tier));
    }

    private void _CreateDefaultVehicles()
    {
        AllVehicles.Clear();

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        //  STOCK SEDAN — Balanced baseline. Everything average, nothing weak.
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        var sedan = new VehicleData();
        sedan.DisplayName = "Wraith Sedan";
        sedan.Description = "The reliable workhorse. No stat stands out, but nothing lets you down. A perfect first ride for learning the arena.";
        // Cool blue/cyan identity
        sedan.BaseTint    = new Color(0.2f, 0.75f, 1f, 1f);
        sedan.AccentTint  = new Color(0.1f, 0.4f, 0.9f, 1f);
        sedan.GlowColor   = new Color(0.2f, 0.7f, 1f, 1f);
        // Engine
        sedan.MaxSpeed = 460f;
        sedan.Acceleration = 900f;
        sedan.BrakeForce = 2400f;
        sedan.CoastDrag = 150f;
        sedan.AeroDrag = 0.8f;
        sedan.DriftThrottleBlend = 0.75f;
        // Steering
        sedan.Wheelbase = 150f;
        sedan.MaxSteerAngle = 42f;
        sedan.MinSteerSpeed = 80f;
        sedan.HighSpeedSteerReduction = 0.2f;
        // Traction
        sedan.GripFriction = 6f;
        sedan.DriftFriction = 3.8f;
        sedan.TireGrip = 0.85f;
        sedan.GripSpeedFalloff = 0.15f;
        sedan.SlipStartThreshold = 110f;
        sedan.SlipEndThreshold = 35f;
        sedan.SteerDriftSpeed = 180f;
        sedan.SteerDriftInput = 0.62f;
        // Handbrake
        sedan.HandbrakeDrag = 150f;
        sedan.HandbrakeYawRate = 4.2f;
        sedan.KickImpulse = 0.15f;
        sedan.HandbrakeFrictionMul = 0.15f;
        sedan.CounterSteerRecovery = 9f;
        // Boost
        sedan.BoostLaunchSpeed = 480f;
        sedan.BoostBuildRate = 1.2f;
        sedan.BoostDecayRate = 2.5f;
        sedan.BoostEntrySpeed = 80f;
        sedan.BoostMinCharge = 0.25f;
        // Combat
        sedan.MaxHealth = 100f;
        sedan.RamDamage = 30f;
        sedan.Armor = 0f;
        sedan.CollisionSize = new Vector2(56, 28);
        AllVehicles.Add(sedan);

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        //  INTERCEPTOR — Glass cannon. Blistering speed, paper armor.
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        var interceptor = new VehicleData();
        interceptor.DisplayName = "Viper Interceptor";
        interceptor.Description = "Blistering speed and razor handling. Paper-thin armor — one wrong angle and you're scrap. Hit-and-run specialist.";
        // Hot orange/red identity
        interceptor.BaseTint    = new Color(1f, 0.4f, 0.1f, 1f);
        interceptor.AccentTint  = new Color(1f, 0.8f, 0f, 1f);
        interceptor.GlowColor   = new Color(1f, 0.5f, 0f, 1f);
        // Engine — fast, responsive
        interceptor.MaxSpeed = 580f;
        interceptor.Acceleration = 1100f;
        interceptor.BrakeForce = 2800f;
        interceptor.CoastDrag = 120f;
        interceptor.AeroDrag = 0.6f;  // less drag = higher effective top speed
        interceptor.DriftThrottleBlend = 0.65f;
        // Steering — sharp, but loses control at top speed
        interceptor.Wheelbase = 130f;
        interceptor.MaxSteerAngle = 48f;
        interceptor.MinSteerSpeed = 70f;
        interceptor.HighSpeedSteerReduction = 0.35f;  // harsh reduction at speed
        // Traction — good grip, but twitchy at limits
        interceptor.GripFriction = 7f;
        interceptor.DriftFriction = 4.2f;
        interceptor.TireGrip = 0.9f;
        interceptor.GripSpeedFalloff = 0.2f;  // more grip loss at speed
        interceptor.SlipStartThreshold = 120f;
        interceptor.SlipEndThreshold = 40f;
        interceptor.SteerDriftSpeed = 220f;
        interceptor.SteerDriftInput = 0.7f;
        // Handbrake — responsive but short slides
        interceptor.HandbrakeDrag = 180f;
        interceptor.HandbrakeYawRate = 4.8f;
        interceptor.KickImpulse = 0.2f;
        interceptor.HandbrakeFrictionMul = 0.2f;  // higher = grip returns faster
        interceptor.CounterSteerRecovery = 12f;  // snappy recovery
        // Boost — powerful launch, fast charge
        interceptor.BoostLaunchSpeed = 620f;
        interceptor.BoostBuildRate = 1.5f;
        interceptor.BoostDecayRate = 3f;
        interceptor.BoostEntrySpeed = 100f;
        interceptor.BoostMinCharge = 0.2f;
        // Combat — fragile, weak ram
        interceptor.MaxHealth = 60f;
        interceptor.RamDamage = 20f;
        interceptor.Armor = 0f;
        interceptor.CollisionSize = new Vector2(52, 24);
        AllVehicles.Add(interceptor);

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        //  JUGGERNAUT — Unstoppable tank. Slow, impossible to kill.
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        var juggernaut = new VehicleData();
        juggernaut.DisplayName = "Rhino Juggernaut";
        juggernaut.Description = "An armored battering ram. Turns like a freight train, hits like one too. Plants itself through drifts instead of sliding.";
        // Military green/red identity
        juggernaut.BaseTint    = new Color(0.3f, 0.38f, 0.28f, 1f);
        juggernaut.AccentTint  = new Color(0.85f, 0.15f, 0.1f, 1f);
        juggernaut.GlowColor   = new Color(1f, 0.2f, 0.05f, 1f);
        // Engine — slow and heavy
        juggernaut.MaxSpeed = 340f;
        juggernaut.Acceleration = 600f;
        juggernaut.BrakeForce = 3000f;  // heavy brakes
        juggernaut.CoastDrag = 200f;  // heavy = more coast drag
        juggernaut.AeroDrag = 1.2f;   // big frontal area
        juggernaut.DriftThrottleBlend = 0.5f;
        // Steering — sluggish, wide turns
        juggernaut.Wheelbase = 200f;
        juggernaut.MaxSteerAngle = 30f;
        juggernaut.MinSteerSpeed = 60f;
        juggernaut.HighSpeedSteerReduction = 0.12f;  // barely any reduction (already slow)
        // Traction — planted, hard to drift
        juggernaut.GripFriction = 9f;  // very grippy
        juggernaut.DriftFriction = 5f;
        juggernaut.TireGrip = 0.92f;
        juggernaut.GripSpeedFalloff = 0.08f;  // minimal grip loss at speed
        juggernaut.SlipStartThreshold = 160f;  // hard to break traction
        juggernaut.SlipEndThreshold = 50f;
        juggernaut.SteerDriftSpeed = 280f;   // very hard to steer-drift
        juggernaut.SteerDriftInput = 0.85f;
        // Handbrake — sluggish rotation, planted through slides
        juggernaut.HandbrakeDrag = 100f;  // less drag = maintains momentum
        juggernaut.HandbrakeYawRate = 2.8f;  // slow rotation
        juggernaut.KickImpulse = 0.08f;  // small kick
        juggernaut.HandbrakeFrictionMul = 0.25f;  // more friction = less slide
        juggernaut.CounterSteerRecovery = 6f;  // slow recovery
        // Boost — slow charge, moderate launch
        juggernaut.BoostLaunchSpeed = 400f;
        juggernaut.BoostBuildRate = 0.9f;
        juggernaut.BoostDecayRate = 2f;
        juggernaut.BoostEntrySpeed = 60f;
        juggernaut.BoostMinCharge = 0.3f;
        // Combat — devastating ram, massive health
        juggernaut.MaxHealth = 200f;
        juggernaut.RamDamage = 55f;
        juggernaut.Armor = 12f;
        juggernaut.CollisionSize = new Vector2(66, 36);
        AllVehicles.Add(juggernaut);

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        //  DRIFT KING — Lives sideways. Extreme drift control. High skill.
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        var drifter = new VehicleData();
        drifter.DisplayName = "Phantom Drift";
        drifter.Description = "Lives sideways. Enters drift at a whisper, holds angles forever. Fragile — but if you can stay sliding, nothing can touch you.";
        // Purple/green neon identity
        drifter.BaseTint    = new Color(0.7f, 0.1f, 1f, 1f);
        drifter.AccentTint  = new Color(0.1f, 1f, 0.5f, 1f);
        drifter.GlowColor   = new Color(0.6f, 0f, 1f, 1f);
        // Engine — moderate, rewards momentum management
        drifter.MaxSpeed = 440f;
        drifter.Acceleration = 850f;
        drifter.BrakeForce = 2200f;
        drifter.CoastDrag = 130f;
        drifter.AeroDrag = 0.75f;
        drifter.DriftThrottleBlend = 0.9f;  // almost full velocity-aligned throttle
        // Steering — good angle, but the real turning is via drift
        drifter.Wheelbase = 140f;
        drifter.MaxSteerAngle = 45f;
        drifter.MinSteerSpeed = 70f;
        drifter.HighSpeedSteerReduction = 0.25f;
        // Traction — loose, enters drift easily, holds long slides
        drifter.GripFriction = 4.5f;  // low grip
        drifter.DriftFriction = 2.5f;  // very low drift correction = long slides
        drifter.TireGrip = 0.72f;  // less overall grip
        drifter.GripSpeedFalloff = 0.2f;
        drifter.SlipStartThreshold = 75f;  // enters drift at very low lateral speed
        drifter.SlipEndThreshold = 25f;  // stays in drift longer
        drifter.SteerDriftSpeed = 140f;  // can steer-drift at lower speeds
        drifter.SteerDriftInput = 0.5f;  // less input needed to trigger
        // Handbrake — aggressive, dramatic slides
        drifter.HandbrakeDrag = 120f;
        drifter.HandbrakeYawRate = 5.5f;  // fastest rotation
        drifter.KickImpulse = 0.22f;  // big kick
        drifter.HandbrakeFrictionMul = 0.08f;  // nearly zero friction = massive slides
        drifter.CounterSteerRecovery = 14f;  // fast recovery when you counter
        // Boost — fast charge, good launch, rewards burnout play
        drifter.BoostLaunchSpeed = 520f;
        drifter.BoostBuildRate = 1.6f;
        drifter.BoostDecayRate = 2f;
        drifter.BoostEntrySpeed = 90f;
        drifter.BoostMinCharge = 0.2f;
        // Combat — fragile, moderate ram
        drifter.MaxHealth = 75f;
        drifter.RamDamage = 25f;
        drifter.Armor = 0f;
        drifter.CollisionSize = new Vector2(54, 26);
        AllVehicles.Add(drifter);
    }
}