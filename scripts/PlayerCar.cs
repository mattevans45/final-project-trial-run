using Godot;

/// <summary>
/// Arcade-physics car on CharacterBody2D.
/// Forza-style: snappy grip, momentum-preserving handbrake drift, counter-steer recovery.
/// </summary>
public partial class PlayerCar : CharacterBody2D
{
	// ── Engine ────────────────────────────────────────────────────────────────

	[ExportGroup("Engine")]

	/// <summary>
	/// Top forward speed in pixels per second.
	/// Throttle accelerates toward this value; aero drag prevents exceeding it.
	/// Typical range: 300 (sluggish) – 600 (very fast).
	/// </summary>
	[Export] public float MaxSpeed { get; set; } = 460f;

	/// <summary>
	/// Rate of forward-speed change when throttle is applied, in px/s².
	/// Higher values make the car feel snappier off the line.
	/// </summary>
	[Export] public float Acceleration { get; set; } = 900f;

	/// <summary>
	/// Deceleration rate when opposite throttle is applied (engine-braking), in px/s².
	/// This is intentionally much higher than <see cref="CoastDrag"/> to give the
	/// car a "locked-wheels" brake feel.
	/// </summary>
	[Export] public float BrakeForce { get; set; } = 2400f;

	/// <summary>
	/// Passive deceleration applied when no throttle input is present, in px/s².
	/// Keep this low for an arcade "coasting" feel.
	/// </summary>
	[Export] public float CoastDrag { get; set; } = 150f;

	/// <summary>
	/// Quadratic aerodynamic drag coefficient. The actual drag force per frame is
	/// <c>AeroDrag × speed × (speed / MaxSpeed)</c>, so it grows with the square
	/// of speed — this is what caps the car at MaxSpeed.
	/// </summary>
	[Export] public float AeroDrag { get; set; } = 0.8f;

	/// <summary>
	/// Controls how throttle energy is split between forward acceleration and
	/// sustaining lateral drift momentum (0 = all forward, 1 = balanced split).
	/// Higher values make the car maintain a drift longer under power.
	/// </summary>
	[Export] public float DriftThrottleBlend { get; set; } = 0.75f;

	// ── Steering ──────────────────────────────────────────────────────────────

	[ExportGroup("Steering")]

	/// <summary>
	/// Simulated front-to-rear axle distance in pixels, used in the Ackermann
	/// turn-radius formula: <c>ω = speed × tan(steerAngle) / Wheelbase</c>.
	/// Larger values produce gentler arcs at the same steer angle.
	/// </summary>
	[Export] public float Wheelbase { get; set; } = 150f;

	/// <summary>
	/// Maximum angle of the virtual front wheels in degrees.
	/// Higher values enable tighter low-speed turns. Effective angle is reduced
	/// at speed by <see cref="HighSpeedSteerReduction"/>.
	/// </summary>
	[Export] public float MaxSteerAngle { get; set; } = 42f;

	/// <summary>
	/// Minimum speed (px/s) substituted into the turn-radius formula when the
	/// car is moving slowly. Prevents division-by-zero and ensures the car still
	/// rotates at very low speeds.
	/// </summary>
	[Export] public float MinSteerSpeed { get; set; } = 80f;

	/// <summary>
	/// Fraction by which max steer angle is reduced at full speed.
	/// <c>effectiveAngle = MaxSteerAngle × (1 − HighSpeedSteerReduction × speedRatio)</c>.
	/// 0 = no reduction (same handling at all speeds), 1 = zero steering at MaxSpeed.
	/// </summary>
	[Export] public float HighSpeedSteerReduction { get; set; } = 0.2f;

	// ── Traction ──────────────────────────────────────────────────────────────

	[ExportGroup("Traction")]

	/// <summary>
	/// Lateral friction coefficient applied in normal grip mode.
	/// Used in an exponential decay: <c>correctionFrac = 1 − exp(−GripFriction × dt)</c>.
	/// Higher values snap the car onto its heading direction faster.
	/// </summary>
	[Export] public float GripFriction { get; set; } = 6f;

	/// <summary>
	/// Lateral friction coefficient while in drift mode.
	/// Should be lower than <see cref="GripFriction"/> so that drift slides persist.
	/// </summary>
	[Export] public float DriftFriction { get; set; } = 3.8f;

	/// <summary>
	/// Master grip multiplier (0–1) applied on top of the computed friction correction.
	/// Lower values make the car slide more on all surfaces.
	/// </summary>
	[Export] public float TireGrip { get; set; } = 0.85f;

	/// <summary>
	/// How much grip diminishes at high speed.
	/// At <see cref="MaxSpeed"/>, effective grip is reduced by this fraction.
	/// 0 = grip is constant with speed; 0.15 = 15% less grip at top speed.
	/// </summary>
	[Export] public float GripSpeedFalloff { get; set; } = 0.15f;

	/// <summary>
	/// Lateral speed (px/s) that triggers the transition from grip to drift mode.
	/// The car enters a drift when its sideways velocity exceeds this value.
	/// Pair with <see cref="SlipEndThreshold"/> to create a hysteresis band.
	/// </summary>
	[Export] public float SlipStartThreshold { get; set; } = 110f;

	/// <summary>
	/// Lateral speed (px/s) below which the car recovers from drift back to grip.
	/// Must be lower than <see cref="SlipStartThreshold"/> to create a stable
	/// hysteresis (prevents rapid oscillation between states).
	/// </summary>
	[Export] public float SlipEndThreshold { get; set; } = 35f;

	/// <summary>
	/// Minimum forward speed (px/s) required for a steer-induced drift to trigger.
	/// Below this speed, hard steering does not initiate a drift.
	/// </summary>
	[Export] public float SteerDriftSpeed { get; set; } = 180f;

	/// <summary>
	/// Minimum steer axis magnitude (0–1) required to trigger a steer-induced drift
	/// when moving faster than <see cref="SteerDriftSpeed"/>.
	/// 1.0 = only full stick initiates a drift; 0.5 = half-stick is enough.
	/// </summary>
	[Export] public float SteerDriftInput { get; set; } = 0.62f;

	// ── Handbrake ─────────────────────────────────────────────────────────────

	[ExportGroup("Handbrake")]

	/// <summary>
	/// Forward deceleration rate (px/s²) applied while the handbrake is held.
	/// Unlike <see cref="BrakeForce"/> this is constant, not input-direction dependent.
	/// </summary>
	[Export] public float HandbrakeDrag { get; set; } = 150f;

	/// <summary>
	/// Instantaneous heading rotation applied (in radians, scaled by speed fraction)
	/// when the handbrake is first engaged. Creates the initial "snap" into a drift.
	/// Direction is taken from current steer input, or defaults to right if centered.
	/// </summary>
	[Export] public float KickImpulse { get; set; } = 0.15f;

	/// <summary>
	/// Continuous yaw (rotation) rate in rad/s applied while the handbrake is held
	/// and there is steering input. Controls how quickly the car can spin in place.
	/// Reverse yaw rate is automatically increased by 1.8× for tank-turn maneuvers.
	/// </summary>
	[Export] public float HandbrakeYawRate { get; set; } = 4.2f;

	/// <summary>
	/// Multiplier applied to lateral friction while the handbrake is held.
	/// Very low values (0.10–0.20) simulate locked rear wheels with maximum slide.
	/// </summary>
	[Export] public float HandbrakeFrictionMul { get; set; } = 0.15f;

	/// <summary>
	/// Bonus lateral friction added when counter-steering during a post-handbrake drift.
	/// Higher values reward precise counter-steering with a faster snap-back to straight.
	/// </summary>
	[Export] public float CounterSteerRecovery { get; set; } = 9f;

	// ── Boost Start ───────────────────────────────────────────────────────────

	[ExportGroup("Boost Start")]

	/// <summary>
	/// Rate at which the boost charge (0–1) builds per second during a burnout.
	/// A burnout requires: handbrake held + throttle &gt; 0.2 + speed &lt; BoostEntrySpeed.
	/// </summary>
	[Export] public float BoostBuildRate { get; set; } = 1.2f;

	/// <summary>
	/// Rate at which stored boost charge decays per second when not actively
	/// charging. Prevents charge from persisting indefinitely between attempts.
	/// </summary>
	[Export] public float BoostDecayRate { get; set; } = 2.5f;

	/// <summary>
	/// The forward speed the car launches to on a full-charge (charge = 1.0) boost release.
	/// Actual launch speed = <c>BoostLaunchSpeed × charge</c>, clamped to at least current speed.
	/// </summary>
	[Export] public float BoostLaunchSpeed { get; set; } = 480f;

	/// <summary>
	/// Maximum forward speed (px/s) at which boost charge can accumulate.
	/// Above this speed, throttle + handbrake just accelerates/decelerates normally.
	/// Keep below <see cref="MaxSpeed"/> (typically 15–20% of MaxSpeed).
	/// </summary>
	[Export] public float BoostEntrySpeed { get; set; } = 80f;

	/// <summary>
	/// Minimum charge level (0–1) required for a launch boost to activate on
	/// handbrake release. Below this threshold the stored charge is discarded.
	/// Prevents accidental micro-boosts from brief handbrake taps.
	/// </summary>
	[Export] public float BoostMinCharge { get; set; } = 0.25f;

	// ── Debug ─────────────────────────────────────────────────────────────────

	[ExportGroup("Debug")]

	/// <summary>
	/// When true, renders debug vectors via <c>_Draw</c>:
	/// green = forward speed, red/orange = lateral speed, and the Ackermann turn arc.
	/// Disable before shipping.
	/// </summary>
	[Export] public bool ShowDebugVectors { get; set; } = false;

	// ── Signals ───────────────────────────────────────────────────────────────

	/// <summary>
	/// Fired on significant collisions (wall impact, boost launch, etc.).
	/// <paramref name="intensity"/> is 0–1; connect to <see cref="SmoothCamera.AddTrauma"/>.
	/// </summary>
	[Signal] public delegate void ImpactEventHandler(float intensity);

	// ── Public read-only state ────────────────────────────────────────────────
	public Vector2 ForwardDir    => GlobalTransform.X;
	public bool    IsBraking     { get; private set; }
	public bool    IsDrifting    => _drifting;
	public bool    HandbrakeActive => _handbrakeHeld;
	public float   Speed         => Mathf.Abs(_forwardSpeed);
	public float   LateralSpeed  => _lateralSpeed;
	public float   DriftIntensity => Mathf.Clamp(Mathf.Abs(_lateralSpeed) / SlipStartThreshold, 0f, 1f);
	public float   TotalSpeed    => Velocity.Length();
	public bool    IsReversing   => _forwardSpeed < -5f;
	public float   BoostCharge   => _boostCharge;
	public bool    IsBurningOut  => _isBurningOut;
	public float   TireSpinIntensity =>
		Mathf.Max(DriftIntensity, _isBurningOut ? _boostCharge : 0f);
	public Vector2 VelocityDirection =>
		Velocity.Length() > 1f ? Velocity.Normalized() : Transform.X;

	// ── Private state ─────────────────────────────────────────────────────────
	private float _forwardSpeed;
	private float _lateralSpeed;
	private float _heading;

	private ShaderMaterial _mat;
	private ShaderMaterial _glowMat;
	private SmoothCamera   _camera;
	private RamSystem      _ramSystem;
	private float          _shakeCooldown;
	private bool           _drifting;
	private float          _friction;
	private bool           _prevHandbrake;
	private bool           _handbrakeHeld;
	private bool           _wasHandbraking;
	private float          _boostCharge;
	private bool           _isBurningOut;

	private SurfaceDetector _surfaceDetector;
	private float           _oilTractionMul = 1f;
	private bool            _wasOnOil;

	private PlayerHUD _hud;
	private IWeapon   _weapon;

	// ── Lifecycle ─────────────────────────────────────────────────────────────

	public override void _Ready()
	{
		_heading  = GlobalRotation;
		_friction = GripFriction;
		MotionMode = MotionModeEnum.Floating;

		var sprite = GetNodeOrNull<Sprite2D>("Sprite2D");
		_mat = sprite?.Material as ShaderMaterial;
		if (_mat == null)
			GD.PushWarning("PlayerCar: No ShaderMaterial on Sprite2D.");

		var glowSprite = GetNodeOrNull<Sprite2D>("GlowSprite");
		_glowMat = glowSprite?.Material as ShaderMaterial;

		_camera    = GetNodeOrNull<SmoothCamera>("Camera2D");
		_ramSystem = GetNodeOrNull<RamSystem>("RamSystem");

		if (_camera != null)
			Impact += (intensity) => _camera.AddTrauma(intensity);

		// Surface effects
		var sd = new SurfaceDetector { Name = "SurfaceDetector" };
		AddChild(sd);
		_surfaceDetector = sd;
		AddChild(new SurfaceParticles { Name = "SurfaceParticles" });

		// HUD
		var damageable = GetNodeOrNull<Damageable>("Damageable");
		if (damageable != null)
		{
			_hud = new PlayerHUD { Name = "PlayerHUD" };
			AddChild(_hud);
			_hud.Connect(damageable);
		}

		// Weapon — store as concrete type so we can wire up the Godot signals,
		// then expose only IWeapon to the rest of the car logic.
		var pistol = new PistolWeapon { Name = "PistolWeapon" };
		AddChild(pistol);
		_weapon = pistol;
		if (_hud != null)
		{
			pistol.AmmoChanged           += (cur, max, rel) => _hud.UpdateAmmo(cur, max, rel);
			pistol.ReloadProgressChanged += (t)             => _hud.UpdateReloadProgress(t);
		}

		// Apply vehicle data last so it can override defaults set above
		var registry = VehicleRegistry.Instance;
		if (registry?.SelectedVehicle != null)
			ApplyVehicleData(registry.SelectedVehicle);
	}

	// ── Vehicle data ──────────────────────────────────────────────────────────

	/// <summary>
	/// Apply a VehicleData resource to this car, overwriting all tuning
	/// parameters, visuals, and collision shape. Safe to call at runtime.
	/// </summary>
	public void ApplyVehicleData(VehicleData data)
	{
		if (data == null) return;

		MaxSpeed            = data.MaxSpeed;
		Acceleration        = data.Acceleration;
		BrakeForce          = data.BrakeForce;
		CoastDrag           = data.CoastDrag;
		AeroDrag            = data.AeroDrag;
		DriftThrottleBlend  = data.DriftThrottleBlend;

		Wheelbase               = data.Wheelbase;
		MaxSteerAngle           = data.MaxSteerAngle;
		MinSteerSpeed           = data.MinSteerSpeed;
		HighSpeedSteerReduction = data.HighSpeedSteerReduction;

		GripFriction       = data.GripFriction;
		DriftFriction      = data.DriftFriction;
		TireGrip           = data.TireGrip;
		GripSpeedFalloff   = data.GripSpeedFalloff;
		SlipStartThreshold = data.SlipStartThreshold;
		SlipEndThreshold   = data.SlipEndThreshold;
		SteerDriftSpeed    = data.SteerDriftSpeed;
		SteerDriftInput    = data.SteerDriftInput;

		HandbrakeDrag        = data.HandbrakeDrag;
		HandbrakeYawRate     = data.HandbrakeYawRate;
		KickImpulse          = data.KickImpulse;
		HandbrakeFrictionMul = data.HandbrakeFrictionMul;
		CounterSteerRecovery = data.CounterSteerRecovery;

		BoostLaunchSpeed = data.BoostLaunchSpeed;
		BoostBuildRate   = data.BoostBuildRate;
		BoostDecayRate   = data.BoostDecayRate;
		BoostEntrySpeed  = data.BoostEntrySpeed;
		BoostMinCharge   = data.BoostMinCharge;

		_friction = GripFriction;

		var sprite = GetNodeOrNull<Sprite2D>("Sprite2D");
		if (sprite != null && data.SpriteTexture != null)
			sprite.Texture = data.SpriteTexture;

		var glowSprite = GetNodeOrNull<Sprite2D>("GlowSprite");
		if (glowSprite != null && data.SpriteTexture != null)
			glowSprite.Texture = data.SpriteTexture;

		if (_mat != null)
		{
			_mat.SetShaderParameter("base_tint",   data.BaseTint);
			_mat.SetShaderParameter("accent_tint", data.AccentTint);
		}

		if (_glowMat != null)
			_glowMat.SetShaderParameter("glow_color", data.GlowColor);

		foreach (Node child in GetChildren())
		{
			if (child is PointLight2D hl && child.Name.ToString().StartsWith("Headlight"))
				hl.Color = data.GlowColor.Lightened(0.35f);
		}

		var collShape = GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
		if (collShape?.Shape is RectangleShape2D rect)
			rect.Size = data.CollisionSize;

		var damageable = GetNodeOrNull<Damageable>("Damageable");
		if (damageable != null)
		{
			damageable.MaxHealth = data.MaxHealth;
			damageable.Armor     = data.Armor;
			damageable.Reset();
			// PlayerHUD.Connect handles de-duplication of the HealthChanged subscription
			_hud?.Connect(damageable);
		}

		if (_ramSystem != null)
			_ramSystem.RamDamage = data.RamDamage;
	}

	// ── Physics ───────────────────────────────────────────────────────────────

	public override void _PhysicsProcess(double delta)
	{
		float dt = (float)delta;
		if (_shakeCooldown > 0f) _shakeCooldown -= dt;

		// ── Input ──────────────────────────────────────────────────────────
		float throttle  = Input.GetAxis("drive_reverse", "drive_forward");
		float steerIn   = Input.GetAxis("steer_left", "steer_right");
		bool  handbrake = Input.IsActionPressed("handbrake");

		bool engineBraking = Mathf.Abs(_forwardSpeed) > 20f
						  && !Mathf.IsZeroApprox(throttle)
						  && Mathf.Sign(throttle) != Mathf.Sign(_forwardSpeed);

		IsBraking = handbrake || engineBraking;

		bool justPulled   = handbrake && !_prevHandbrake;
		bool justReleased = !handbrake && _prevHandbrake;
		_prevHandbrake = handbrake;
		_handbrakeHeld = handbrake;

		// Weapon input — PlayerCar owns the shoot action; weapon is ignorant of input
		if (Input.IsActionJustPressed("shoot"))
			_weapon?.TryFire();
		if (Input.IsActionJustPressed("reload"))
			_weapon?.StartManualReload();

		// ── 1. Decompose velocity into local frame ──────────────────────────
		Vector2 vel  = Velocity;
		float cosH   = Mathf.Cos(_heading);
		float sinH   = Mathf.Sin(_heading);
		_forwardSpeed = vel.X * cosH + vel.Y * sinH;
		_lateralSpeed = -vel.X * sinH + vel.Y * cosH;

		bool goingForward = _forwardSpeed > 5f;
		bool goingReverse = _forwardSpeed < -5f;

		// ── 2. Handbrake kick ───────────────────────────────────────────────
		if (justPulled && Mathf.Abs(_forwardSpeed) > 40f)
		{
			_drifting = true;
			if (goingReverse)
			{
				float kickDir    = Mathf.Abs(steerIn) > 0.1f ? Mathf.Sign(steerIn) : 1f;
				float speedFactor = Mathf.Clamp(Mathf.Abs(_forwardSpeed) / MaxSpeed, 0.4f, 1f);
				_heading += kickDir * KickImpulse * 2.5f * speedFactor;
			}
			else
			{
				float kickDir    = Mathf.Abs(steerIn) > 0.1f ? Mathf.Sign(steerIn) : 1f;
				float speedFactor = Mathf.Clamp(_forwardSpeed / MaxSpeed, 0.3f, 1f);
				_heading += kickDir * KickImpulse * speedFactor;
			}
		}

		// ── 3. Handbrake yaw ────────────────────────────────────────────────
		float totalSpeed = vel.Length();
		if (handbrake && totalSpeed > 10f)
		{
			float yawDir = 0f;
			if (Mathf.Abs(steerIn) > 0.05f)
			{
				yawDir = steerIn;
			}
			else if (goingReverse)
			{
				yawDir = Mathf.Abs(_lateralSpeed) > 5f ? Mathf.Sign(_lateralSpeed) : 1f;
			}

			if (!Mathf.IsZeroApprox(yawDir))
			{
				float speedRatio = Mathf.Clamp(totalSpeed / MaxSpeed, 0.2f, 1f);
				float yawRate    = goingReverse ? HandbrakeYawRate * 1.8f : HandbrakeYawRate;
				_heading += yawDir * yawRate * speedRatio * dt;
			}

			cosH = Mathf.Cos(_heading);
			sinH = Mathf.Sin(_heading);
			_forwardSpeed =  vel.X * cosH + vel.Y * sinH;
			_lateralSpeed = -vel.X * sinH + vel.Y * cosH;

			goingForward = _forwardSpeed > 5f;
			goingReverse = _forwardSpeed < -5f;
		}

		// ── 4. Throttle & drag ──────────────────────────────────────────────
		if (handbrake)
		{
			_forwardSpeed = Mathf.MoveToward(_forwardSpeed, 0f, HandbrakeDrag * dt);
		}
		else if (!Mathf.IsZeroApprox(throttle))
		{
			bool braking = Mathf.Abs(_forwardSpeed) > 20f
						&& Mathf.Sign(throttle) != Mathf.Sign(_forwardSpeed);

			if (braking)
			{
				_forwardSpeed = Mathf.MoveToward(_forwardSpeed, 0f, BrakeForce * dt);
			}
			else if (_drifting && Mathf.Abs(_lateralSpeed) > SlipEndThreshold)
			{
				float absF     = Mathf.Abs(_forwardSpeed);
				float absL     = Mathf.Abs(_lateralSpeed);
				float totalLoc = absF + absL;
				if (totalLoc > 1f)
				{
					float fwdRatio  = absF / totalLoc;
					float latRatio  = absL / totalLoc;
					float accel     = Acceleration * Mathf.Abs(throttle) * dt;
					float fwdAccel  = accel * Mathf.Lerp(1f, fwdRatio, DriftThrottleBlend);
					_forwardSpeed   = Mathf.MoveToward(_forwardSpeed, throttle * MaxSpeed, fwdAccel);
					float latAccel  = accel * latRatio * DriftThrottleBlend;
					_lateralSpeed  += Mathf.Sign(_lateralSpeed) * latAccel;
				}
				else
				{
					_forwardSpeed = Mathf.MoveToward(_forwardSpeed, throttle * MaxSpeed, Acceleration * dt);
				}
			}
			else
			{
				_forwardSpeed = Mathf.MoveToward(_forwardSpeed, throttle * MaxSpeed, Acceleration * dt);
			}
		}
		else
		{
			_forwardSpeed = Mathf.MoveToward(_forwardSpeed, 0f, CoastDrag * dt);
		}

		// Quadratic aero drag
		{
			float s   = Mathf.Abs(_forwardSpeed);
			float df  = AeroDrag * s * (s / MaxSpeed);
			_forwardSpeed = Mathf.MoveToward(_forwardSpeed, 0f, df * dt);

			float ls  = Mathf.Abs(_lateralSpeed);
			float ldf = AeroDrag * 0.5f * ls * (ls / MaxSpeed);
			_lateralSpeed = Mathf.MoveToward(_lateralSpeed, 0f, ldf * dt);
		}

		// ── 4b. Boost start ─────────────────────────────────────────────────
		bool canBurnout = handbrake && throttle > 0.2f
						&& Mathf.Abs(_forwardSpeed) < BoostEntrySpeed;

		_isBurningOut = canBurnout;

		if (canBurnout)
		{
			_boostCharge = Mathf.Min(1f, _boostCharge + BoostBuildRate * throttle * dt);
		}
		else if (justReleased && _boostCharge >= BoostMinCharge)
		{
			float launch = BoostLaunchSpeed * _boostCharge;
			_forwardSpeed = Mathf.Max(_forwardSpeed, launch);
			EmitSignal(SignalName.Impact, 0.35f + _boostCharge * 0.3f);
			_boostCharge = 0f;
			_drifting    = true;
		}
		else
		{
			_boostCharge = Mathf.MoveToward(_boostCharge, 0f, BoostDecayRate * dt);
		}

		// ── 5. Steering ─────────────────────────────────────────────────────
		bool handbrakeYawActive = handbrake && goingForward;

		if (!handbrakeYawActive && !Mathf.IsZeroApprox(steerIn))
		{
			float steerSource = Mathf.Abs(_forwardSpeed);
			if (_drifting)
				steerSource = Mathf.Max(steerSource, Mathf.Abs(_lateralSpeed) * 0.5f);

			if (steerSource > 1f)
			{
				float absForward  = Mathf.Abs(_forwardSpeed);
				float speedRatio  = Mathf.Clamp(absForward / MaxSpeed, 0f, 1f);
				float reduction   = goingReverse
					? HighSpeedSteerReduction * 0.3f
					: HighSpeedSteerReduction;
				float steerScale  = 1f - reduction * speedRatio;
				float steerAngle  = steerIn * Mathf.DegToRad(MaxSteerAngle) * steerScale;
				float steerSpeed  = Mathf.Max(absForward, MinSteerSpeed);

				float turnSign;
				if (_drifting && absForward < 30f)
					turnSign = 1f;
				else if (goingReverse)
					turnSign = -1f;
				else
					turnSign = 1f;

				float omega  = steerSpeed * Mathf.Tan(steerAngle) / Wheelbase;
				_heading += turnSign * omega * dt;
			}
		}

		// ── 6. Traction state machine ────────────────────────────────────────
		float absLateral     = Mathf.Abs(_lateralSpeed);
		bool  steerDriftEntry = goingForward
							 && Mathf.Abs(steerIn) >= SteerDriftInput
							 && _forwardSpeed > SteerDriftSpeed;

		if (handbrake || absLateral > SlipStartThreshold || steerDriftEntry)
		{
			if (!_drifting && handbrake)
				_wasHandbraking = true;

			if (!_drifting && steerDriftEntry && !handbrake)
				_lateralSpeed -= Mathf.Sign(steerIn) * 40f;

			_drifting = true;
		}
		else if (_drifting && absLateral < SlipEndThreshold && !steerDriftEntry)
		{
			_drifting       = false;
			_wasHandbraking = false;
		}

		// ── 7. Lateral correction ────────────────────────────────────────────
		float baseFriction = _drifting ? DriftFriction : GripFriction;
		float targetFriction;

		if (handbrake)
			targetFriction = baseFriction * HandbrakeFrictionMul;
		else if (_drifting && _wasHandbraking && _IsCounterSteering(steerIn))
			targetFriction = baseFriction + CounterSteerRecovery;
		else
			targetFriction = baseFriction;

		bool  onOil          = _surfaceDetector?.IsOnOil ?? false;
		float targetOilMul   = onOil ? 0.20f : 1f;
		_oilTractionMul      = Mathf.Lerp(_oilTractionMul, targetOilMul, 4f * dt);
		targetFriction      *= _oilTractionMul;

		if (onOil && !_wasOnOil)
			_hud?.ShowStatus("⚠  OIL SLICK", 1.8f);
		_wasOnOil = onOil;

		_friction = Mathf.Lerp(_friction, targetFriction, 10f * dt);

		float speedGripFactor = 1f - GripSpeedFalloff
			* Mathf.Clamp(Mathf.Abs(_forwardSpeed) / MaxSpeed, 0f, 1f);
		float grip = 1f - Mathf.Exp(-_friction * dt);
		_lateralSpeed = Mathf.Lerp(_lateralSpeed, 0f, grip * TireGrip * speedGripFactor);

		// ── 8. Rebuild world velocity ────────────────────────────────────────
		cosH = Mathf.Cos(_heading);
		sinH = Mathf.Sin(_heading);
		Velocity = new Vector2(
			_forwardSpeed * cosH - _lateralSpeed * sinH,
			_forwardSpeed * sinH + _lateralSpeed * cosH
		);

		MoveAndSlide();

		// ── 9. Ram system ────────────────────────────────────────────────────
		_ramSystem?.ProcessCollisions();

		// ── 10. Wall impact absorption ───────────────────────────────────────
		int slideHits = GetSlideCollisionCount();
		if (slideHits > 0)
		{
			float worstImpact = 0f;

			for (int i = 0; i < slideHits; i++)
			{
				var collision = GetSlideCollision(i);
				var collider  = collision.GetCollider();

				if (collider is Node2D node && node.IsInGroup("enemies"))
					continue;

				Vector2 n         = collision.GetNormal();
				float penetration = Velocity.Dot(n);
				if (penetration < 0f)
				{
					Velocity     -= penetration * n;
					worstImpact   = Mathf.Max(worstImpact, -penetration);
				}
			}

			if (worstImpact > 0f)
			{
				float hitSpeed      = Mathf.Max(Velocity.Length(), 1f);
				float impactFrac    = Mathf.Clamp(worstImpact / hitSpeed, 0f, 1f);
				float damp          = Mathf.Lerp(0.85f, 0.40f, impactFrac);
				Velocity           *= damp;

				float cosH2   = Mathf.Cos(_heading);
				float sinH2   = Mathf.Sin(_heading);
				_forwardSpeed =  Velocity.X * cosH2 + Velocity.Y * sinH2;
				_lateralSpeed = -(Velocity.X * sinH2) + Velocity.Y * cosH2;

				if (impactFrac > 0.5f && _shakeCooldown <= 0f)
				{
					EmitSignal(SignalName.Impact, impactFrac * 0.55f);
					_shakeCooldown = 0.4f;
					_drifting      = false;
					_boostCharge   = 0f;
				}
			}
		}

		// ── 11. Shader update (post-physics, so velocity is final this frame) ──
		Vector2 velDir = Velocity.Length() > 1f ? Velocity.Normalized() : Vector2.Right;

		if (_mat != null)
		{
			_mat.SetShaderParameter("speed",        TotalSpeed);
			_mat.SetShaderParameter("drift",        TireSpinIntensity);
			_mat.SetShaderParameter("velocity_dir", velDir);
		}

		if (_glowMat != null)
			_glowMat.SetShaderParameter("speed", TotalSpeed);

		_heading      = Mathf.Wrap(_heading, -Mathf.Pi, Mathf.Pi);
		GlobalRotation = _heading;

		if (ShowDebugVectors)
			QueueRedraw();
	}

	// ── Helpers ───────────────────────────────────────────────────────────────

	private bool _IsCounterSteering(float steerIn)
	{
		if (Mathf.Abs(steerIn) < 0.1f || Mathf.Abs(_lateralSpeed) < SlipEndThreshold)
			return false;
		return Mathf.Sign(steerIn) != Mathf.Sign(_lateralSpeed);
	}

	// ── Debug draw ────────────────────────────────────────────────────────────

	public override void _Draw()
	{
		if (!ShowDebugVectors) return;

		DrawLine(Vector2.Zero, Vector2.Right * _forwardSpeed * 0.2f, Colors.Green, 3f);
		Color slipColor = _drifting ? Colors.Orange : Colors.Red;
		DrawLine(Vector2.Zero, Vector2.Down * _lateralSpeed * 0.5f, slipColor, 3f);

		DrawString(ThemeDB.FallbackFont, new Vector2(-40, -30),
			$"F:{_forwardSpeed:F0} L:{_lateralSpeed:F0} D:{(_drifting ? "Y" : "N")}",
			HorizontalAlignment.Left, -1, 12, Colors.White);

		float steerIn = Input.GetAxis("steer_left", "steer_right");
		if (!Mathf.IsZeroApprox(steerIn))
		{
			float steerAngle = steerIn * Mathf.DegToRad(MaxSteerAngle);
			float radius     = Wheelbase / Mathf.Tan(steerAngle);
			DrawArc(new Vector2(0, radius), Mathf.Abs(radius), 0, Mathf.Tau, 64, Colors.Cyan, 1f);
		}
	}
}
