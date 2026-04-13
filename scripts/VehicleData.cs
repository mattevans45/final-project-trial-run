using Godot;

/// <summary>
/// Vehicle data resource — defines ALL stats and visuals for a vehicle type.
/// Every field here maps 1:1 to a PlayerCar export. Nothing is missed.
/// Create .tres files in res://data/vehicles/ for each vehicle.
/// </summary>
[GlobalClass]
public partial class VehicleData : Resource
{
    [ExportGroup("Identity")]
    [Export] public string DisplayName = "Stock Sedan";
    [Export(PropertyHint.MultilineText)] public string Description = "A balanced all-rounder. Reliable speed, solid armor, predictable handling.";
    [Export] public Texture2D SpriteTexture;
    [Export] public Color BaseTint = new Color(0.2f, 0.8f, 1f, 1f);
    [Export] public Color AccentTint = new Color(1f, 0.1f, 0.8f, 1f);
    [Export] public Color GlowColor = new Color(0f, 1f, 1f, 1f);

    [ExportGroup("Engine")]
    [Export] public float MaxSpeed = 460f;
    [Export] public float Acceleration = 900f;
    [Export] public float BrakeForce = 2400f;
    [Export] public float CoastDrag = 150f;
    [Export] public float AeroDrag = 0.8f;
    [Export] public float DriftThrottleBlend = 0.75f;

    [ExportGroup("Steering")]
    [Export] public float Wheelbase = 150f;
    [Export] public float MaxSteerAngle = 42f;
    [Export] public float MinSteerSpeed = 80f;
    [Export] public float HighSpeedSteerReduction = 0.2f;

    [ExportGroup("Traction")]
    [Export] public float GripFriction = 6f;
    [Export] public float DriftFriction = 3.8f;
    [Export] public float TireGrip = 0.85f;
    [Export] public float GripSpeedFalloff = 0.15f;
    [Export] public float SlipStartThreshold = 110f;
    [Export] public float SlipEndThreshold = 35f;
    [Export] public float SteerDriftSpeed = 180f;
    [Export] public float SteerDriftInput = 0.62f;

    [ExportGroup("Handbrake")]
    [Export] public float HandbrakeDrag = 150f;
    [Export] public float HandbrakeYawRate = 4.2f;
    [Export] public float KickImpulse = 0.15f;
    [Export] public float HandbrakeFrictionMul = 0.15f;
    [Export] public float CounterSteerRecovery = 9f;

    [ExportGroup("Boost")]
    [Export] public float BoostLaunchSpeed = 480f;
    [Export] public float BoostBuildRate = 1.2f;
    [Export] public float BoostDecayRate = 2.5f;
    [Export] public float BoostEntrySpeed = 80f;
    [Export] public float BoostMinCharge = 0.25f;

    [ExportGroup("Combat")]
    [Export] public float MaxHealth = 100f;
    [Export] public float RamDamage = 30f;
    [Export] public float Armor = 0f;
    [Export] public Vector2 CollisionSize = new Vector2(56, 28);

    [ExportGroup("Progression")]
    [Export] public int Tier = 1;
    [Export] public int UnlockCost = 0;
    [Export] public bool UnlockedByDefault = true;

    // UI stat ratings (0–1)
    public float SpeedRating    => Mathf.Clamp(MaxSpeed / 600f, 0f, 1f);
    public float AccelRating    => Mathf.Clamp(Acceleration / 1200f, 0f, 1f);
    public float HealthRating   => Mathf.Clamp(MaxHealth / 200f, 0f, 1f);
    public float RamRating      => Mathf.Clamp(RamDamage / 60f, 0f, 1f);
    public float HandlingRating => Mathf.Clamp(MaxSteerAngle / 50f, 0f, 1f);
}