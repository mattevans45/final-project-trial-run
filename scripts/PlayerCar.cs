using Godot;

/// <summary>
/// Arcade-physics car on CharacterBody2D.
/// Forza-style: snappy grip, momentum-preserving handbrake drift, counter-steer recovery.
/// </summary>
public partial class PlayerCar : CharacterBody2D

{

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
	[Export] public float KickImpulse = 0.15f;
	[Export] public float HandbrakeYawRate = 4.2f;
	[Export] public float HandbrakeFrictionMul = 0.15f;
	[Export] public float CounterSteerRecovery = 9f;

	[ExportGroup("Boost Start")]
	[Export] public float BoostBuildRate = 1.2f;
	[Export] public float BoostDecayRate = 2.5f;
	[Export] public float BoostLaunchSpeed = 480f;
	[Export] public float BoostEntrySpeed = 80f;
	[Export] public float BoostMinCharge = 0.25f;

	[ExportGroup("Debug")]
	[Export] public bool ShowDebugVectors = false;

	// ── Signals (decoupled feedback) ──────────────────────────────────────────
	[Signal] public delegate void ImpactEventHandler(float intensity);

	// ── public read-only state ────────────────────────────────────────────────
	public Vector2 ForwardDir => GlobalTransform.X;
    public bool IsBraking { get; private set; }
	public bool IsDrifting => _drifting;
	public bool HandbrakeActive => _handbrakeHeld;
	public float Speed => Mathf.Abs(_forwardSpeed);
	public float LateralSpeed => _lateralSpeed;
	public float DriftIntensity => Mathf.Clamp(Mathf.Abs(_lateralSpeed) / SlipStartThreshold, 0f, 1f);
	public float TotalSpeed => Velocity.Length();
	public bool IsReversing => _forwardSpeed < -5f;
	public float BoostCharge => _boostCharge;
	public bool IsBurningOut => _isBurningOut;
	public float TireSpinIntensity =>
		Mathf.Max(DriftIntensity, _isBurningOut ? _boostCharge : 0f);

	// ── private state ────────────────────────────────────────────────────────
	private float _forwardSpeed;
	private float _lateralSpeed;
	private float _heading;

	private ShaderMaterial _mat;
	private ShaderMaterial _glowMat;
	private SmoothCamera _camera;
	private RamSystem _ramSystem;
	private float _shakeCooldown;
	private bool _drifting;
	private float _friction;
	private bool _prevHandbrake;
	private bool _handbrakeHeld;
	private bool _wasHandbraking;
	private float _boostCharge;
	private bool _isBurningOut;
	public Vector2 VelocityDirection => Velocity.Length() > 1f ? Velocity.Normalized() : Transform.X;
	public override void _Ready()
	{
		_heading = GlobalRotation;
		_friction = GripFriction;
		MotionMode = MotionModeEnum.Floating;

		var sprite = GetNodeOrNull<Sprite2D>("Sprite2D");
		_mat = sprite?.Material as ShaderMaterial;
		if (_mat == null)
			GD.PushWarning("PlayerCar: No ShaderMaterial on Sprite2D.");

		var glowSprite = GetNodeOrNull<Sprite2D>("GlowSprite");
		_glowMat = glowSprite?.Material as ShaderMaterial;

		_camera = GetNodeOrNull<SmoothCamera>("Camera2D");
		_ramSystem = GetNodeOrNull<RamSystem>("RamSystem");

		// Connect impact signal to camera (decoupled)
		if (_camera != null)
			Impact += (intensity) => _camera.AddTrauma(intensity);

		// ── Headlight setup ──────────────────────────────────────────────────────
		// Position is left to the inspector (no override here). Place each
		// PointLight2D node at the headlight lens on your car sprite.
		//
		// Shadow note: shadow_enabled is intentionally OFF.
		//   In Godot 4 the shadow algorithm breaks when a PointLight2D source
		//   lands inside any LightOccluder2D polygon (the entire light goes
		//   black). Because the headlight nodes sit at the car's visual nose —
		//   well ahead of the physics collision shape — they inevitably enter
		//   obstacle polygons as the car drives close. Disabling shadows removes
		//   that artifact entirely. The ground illumination effect (asphalt glow
		//   ahead of the car) is preserved; only hard obstacle silhouettes are
		//   lost, which are barely noticeable in a top-down racing context.
		//
		// Scale: forward reach = (texWidth × scale) / 2
		//        → scale = (2 × reach) / texWidth
		const float HeadlightReach = 400f;

		var beamTex = ResourceLoader.Load<Texture2D>(
			"res://assets/sprite/pngimg.com - light_beam_PNG29.png");

		// if (beamTex != null)
		// {
		// 	float beamScale = (HeadlightReach * 2f) / beamTex.GetWidth();

		// 	foreach (var child in GetChildren())
		// 	{
		// 		if (child is not PointLight2D hl) continue;
		// 		hl.Texture       = beamTex;
		// 		hl.TextureScale  = beamScale;
		// 		hl.BlendMode     = Light2D.BlendModeEnum.Add;
		// 		hl.ShadowEnabled = true;  // see note above
		// 		hl.RangeZMax     = -1;      // don't illuminate z=1 obstacle sprites
		// 	}
		// }
		// else
		// {
		// 	GD.PushWarning("PlayerCar: headlight texture not found — check the res:// path.");
		// }

		// Apply vehicle data from registry if available
		var registry = VehicleRegistry.Instance;
		if (registry?.SelectedVehicle != null)
			ApplyVehicleData(registry.SelectedVehicle);
	}

	/// <summary>
	/// Apply a VehicleData resource to this car, overwriting all tuning
	/// parameters, visuals, and collision shape. Safe to call at runtime.
	/// </summary>
	public void ApplyVehicleData(VehicleData data)
	{
		if (data == null) return;

		// Engine
		MaxSpeed = data.MaxSpeed;
		Acceleration = data.Acceleration;
		BrakeForce = data.BrakeForce;
		CoastDrag = data.CoastDrag;
		AeroDrag = data.AeroDrag;
		DriftThrottleBlend = data.DriftThrottleBlend;

		// Steering
		Wheelbase = data.Wheelbase;
		MaxSteerAngle = data.MaxSteerAngle;
		MinSteerSpeed = data.MinSteerSpeed;
		HighSpeedSteerReduction = data.HighSpeedSteerReduction;

		// Traction
		GripFriction = data.GripFriction;
		DriftFriction = data.DriftFriction;
		TireGrip = data.TireGrip;
		GripSpeedFalloff = data.GripSpeedFalloff;
		SlipStartThreshold = data.SlipStartThreshold;
		SlipEndThreshold = data.SlipEndThreshold;
		SteerDriftSpeed = data.SteerDriftSpeed;
		SteerDriftInput = data.SteerDriftInput;

		// Handbrake
		HandbrakeDrag = data.HandbrakeDrag;
		HandbrakeYawRate = data.HandbrakeYawRate;
		KickImpulse = data.KickImpulse;
		HandbrakeFrictionMul = data.HandbrakeFrictionMul;
		CounterSteerRecovery = data.CounterSteerRecovery;

		// Boost
		BoostLaunchSpeed = data.BoostLaunchSpeed;
		BoostBuildRate = data.BoostBuildRate;
		BoostDecayRate = data.BoostDecayRate;
		BoostEntrySpeed = data.BoostEntrySpeed;
		BoostMinCharge = data.BoostMinCharge;

		// Reset friction to new grip value
		_friction = GripFriction;

		// Visuals
		var sprite = GetNodeOrNull<Sprite2D>("Sprite2D");
		if (sprite != null && data.SpriteTexture != null)
			sprite.Texture = data.SpriteTexture;

		var glowSprite = GetNodeOrNull<Sprite2D>("GlowSprite");
		if (glowSprite != null && data.SpriteTexture != null)
			glowSprite.Texture = data.SpriteTexture;

		if (_mat != null)
		{
			_mat.SetShaderParameter("base_tint", data.BaseTint);
			_mat.SetShaderParameter("accent_tint", data.AccentTint);
		}

		if (_glowMat != null)
			_glowMat.SetShaderParameter("glow_color", data.GlowColor);

		// Collision shape
		var collShape = GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
		if (collShape?.Shape is RectangleShape2D rect)
			rect.Size = data.CollisionSize;

		// Health (via Damageable component)
		var damageable = GetNodeOrNull<Damageable>("Damageable");
		if (damageable != null)
		{
			damageable.MaxHealth = data.MaxHealth;
			damageable.Reset();
		}

		// Ram damage (via RamSystem component)
		if (_ramSystem != null)
			_ramSystem.RamDamage = data.RamDamage;
	}

	public override void _PhysicsProcess(double delta)
	{
		float dt = (float)delta;
		if (_shakeCooldown > 0f) _shakeCooldown -= dt;
		float throttle = Input.GetAxis("drive_reverse", "drive_forward");
		float steerIn = Input.GetAxis("steer_left", "steer_right");
		bool handbrake = Input.IsActionPressed("handbrake");
		bool engineBraking = Mathf.Abs(_forwardSpeed) > 20f 
                          && !Mathf.IsZeroApprox(throttle) 
                          && Mathf.Sign(throttle) != Mathf.Sign(_forwardSpeed);
        
        IsBraking = handbrake || engineBraking;
		bool justPulled = handbrake && !_prevHandbrake;
		bool justReleased = !handbrake && _prevHandbrake;
		_prevHandbrake = handbrake;
		_handbrakeHeld = handbrake;

		bool goingForward = _forwardSpeed > 5f;
		bool goingReverse = _forwardSpeed < -5f;

		if (_mat != null)
		{
			_mat.SetShaderParameter("speed", TotalSpeed);
			_mat.SetShaderParameter("drift", TireSpinIntensity);
		}

		if (_glowMat != null)
			_glowMat.SetShaderParameter("speed", TotalSpeed);

		// ── 1. Decompose velocity into local frame ────────────────────────────
		Vector2 vel = Velocity;
		float cosH = Mathf.Cos(_heading);
		float sinH = Mathf.Sin(_heading);

		_forwardSpeed = vel.X * cosH + vel.Y * sinH;
		_lateralSpeed = -vel.X * sinH + vel.Y * cosH;

		goingForward = _forwardSpeed > 5f;
		goingReverse = _forwardSpeed < -5f;

		// ── 2. Handbrake kick ─────────────────────────────────────────────────
		if (justPulled && Mathf.Abs(_forwardSpeed) > 40f)
		{
			_drifting = true;

			if (goingReverse)
			{
				float kickDir = Mathf.Abs(steerIn) > 0.1f ? Mathf.Sign(steerIn) : 1f;
				float speedFactor = Mathf.Clamp(Mathf.Abs(_forwardSpeed) / MaxSpeed, 0.4f, 1f);
				_heading += kickDir * KickImpulse * 2.5f * speedFactor;
			}
			else
			{
				float kickDir = Mathf.Abs(steerIn) > 0.1f ? Mathf.Sign(steerIn) : 1f;
				float speedFactor = Mathf.Clamp(_forwardSpeed / MaxSpeed, 0.3f, 1f);
				_heading += kickDir * KickImpulse * speedFactor;
			}
		}

		// ── 3. Handbrake yaw ──────────────────────────────────────────────────
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
				float yawRate = goingReverse ? HandbrakeYawRate * 1.8f : HandbrakeYawRate;
				_heading += yawDir * yawRate * speedRatio * dt;
			}

			cosH = Mathf.Cos(_heading);
			sinH = Mathf.Sin(_heading);
			_forwardSpeed = vel.X * cosH + vel.Y * sinH;
			_lateralSpeed = -vel.X * sinH + vel.Y * cosH;

			goingForward = _forwardSpeed > 5f;
			goingReverse = _forwardSpeed < -5f;
		}

		// ── 4. Throttle & drag ────────────────────────────────────────────────
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
				float absF = Mathf.Abs(_forwardSpeed);
				float absL = Mathf.Abs(_lateralSpeed);
				float totalLocal = absF + absL;

				if (totalLocal > 1f)
				{
					float fwdRatio = absF / totalLocal;
					float latRatio = absL / totalLocal;
					float accelAmount = Acceleration * Mathf.Abs(throttle) * dt;

					float fwdAccel = accelAmount * Mathf.Lerp(1f, fwdRatio, DriftThrottleBlend);
					_forwardSpeed = Mathf.MoveToward(_forwardSpeed, throttle * MaxSpeed, fwdAccel);

					float latAccel = accelAmount * latRatio * DriftThrottleBlend;
					float latSign = Mathf.Sign(_lateralSpeed);
					_lateralSpeed += latSign * latAccel;
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
			float speed = Mathf.Abs(_forwardSpeed);
			float dragForce = AeroDrag * speed * (speed / MaxSpeed);
			_forwardSpeed = Mathf.MoveToward(_forwardSpeed, 0f, dragForce * dt);

			float latSpeed = Mathf.Abs(_lateralSpeed);
			float latDrag = AeroDrag * 0.5f * latSpeed * (latSpeed / MaxSpeed);
			_lateralSpeed = Mathf.MoveToward(_lateralSpeed, 0f, latDrag * dt);
		}

		// ── 4b. Boost start ───────────────────────────────────────────────────
		bool canBurnout = handbrake
			&& throttle > 0.2f
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
			_drifting = true;
		}
		else
		{
			_boostCharge = Mathf.MoveToward(_boostCharge, 0f, BoostDecayRate * dt);
		}

		// ── 5. Steering ──────────────────────────────────────────────────────
		bool handbrakeYawActive = handbrake && goingForward;

		if (!handbrakeYawActive && !Mathf.IsZeroApprox(steerIn))
		{
			float steerSource = Mathf.Abs(_forwardSpeed);
			if (_drifting)
				steerSource = Mathf.Max(steerSource, Mathf.Abs(_lateralSpeed) * 0.5f);

			if (steerSource > 1f)
			{
				float absForward = Mathf.Abs(_forwardSpeed);
				float speedRatio = Mathf.Clamp(absForward / MaxSpeed, 0f, 1f);

				float reduction = goingReverse
					? HighSpeedSteerReduction * 0.3f
					: HighSpeedSteerReduction;
				float steerScale = 1f - reduction * speedRatio;
				float steerAngle = steerIn * Mathf.DegToRad(MaxSteerAngle) * steerScale;

				float steerSpeed = Mathf.Max(absForward, MinSteerSpeed);

				float turnSign;
				if (_drifting && absForward < 30f)
					turnSign = 1f;
				else if (goingReverse)
					turnSign = -1f;
				else
					turnSign = 1f;

				float omega = steerSpeed * Mathf.Tan(steerAngle) / Wheelbase;
				_heading += turnSign * omega * dt;
			}
		}

		// ── 6. Traction state machine ─────────────────────────────────────────
		float absLateral = Mathf.Abs(_lateralSpeed);

		bool steerDriftEntry = goingForward
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
			_drifting = false;
			_wasHandbraking = false;
		}

		// ── 7. Lateral correction ─────────────────────────────────────────────
		float baseFriction = _drifting ? DriftFriction : GripFriction;
		float targetFriction;

		if (handbrake)
		{
			targetFriction = baseFriction * HandbrakeFrictionMul;
		}
		else if (_drifting && _wasHandbraking && _IsCounterSteering(steerIn))
		{
			targetFriction = baseFriction + CounterSteerRecovery;
		}
		else
		{
			targetFriction = baseFriction;
		}

		_friction = Mathf.Lerp(_friction, targetFriction, 10f * dt);

		float speedGripFactor = 1f - GripSpeedFalloff
			* Mathf.Clamp(Mathf.Abs(_forwardSpeed) / MaxSpeed, 0f, 1f);
		float grip = 1f - Mathf.Exp(-_friction * dt);
		_lateralSpeed = Mathf.Lerp(_lateralSpeed, 0f, grip * TireGrip * speedGripFactor);

		// ── 8. Rebuild world velocity ─────────────────────────────────────────
		cosH = Mathf.Cos(_heading);
		sinH = Mathf.Sin(_heading);

		Velocity = new Vector2(
			_forwardSpeed * cosH - _lateralSpeed * sinH,
			_forwardSpeed * sinH + _lateralSpeed * cosH
		);

		MoveAndSlide();

		// ── 9. Ram system (processes enemy collisions) ────────────────────────
		_ramSystem?.ProcessCollisions();

		// ── 10. Wall impact absorption (environment only) ─────────────────────
		int slideHits = GetSlideCollisionCount();
		if (slideHits > 0)
		{
			float worstImpact = 0f;

			for (int i = 0; i < slideHits; i++)
			{
				var collision = GetSlideCollision(i);
				var collider = collision.GetCollider();

				// Skip enemies — RamSystem handles those
				if (collider is Node2D node && node.IsInGroup("enemies"))
					continue;

				Vector2 n = collision.GetNormal();
				float penetration = Velocity.Dot(n);
				if (penetration < 0f)
				{
					Velocity -= penetration * n;
					worstImpact = Mathf.Max(worstImpact, -penetration);
				}
			}

			if (worstImpact > 0f)
			{
				float hitSpeed = Mathf.Max(Velocity.Length(), 1f);
				float impactFraction = Mathf.Clamp(worstImpact / hitSpeed, 0f, 1f);
				float damp = Mathf.Lerp(0.85f, 0.40f, impactFraction);
				Velocity *= damp;

				// Sync internal frame
				float cosH2 = Mathf.Cos(_heading);
				float sinH2 = Mathf.Sin(_heading);
				_forwardSpeed = Velocity.X * cosH2 + Velocity.Y * sinH2;
				_lateralSpeed = -(Velocity.X * sinH2) + Velocity.Y * cosH2;

				if (impactFraction > 0.5f)
				{
					if (_shakeCooldown <= 0f)
					{
						EmitSignal(SignalName.Impact, impactFraction * 0.55f);
						_shakeCooldown = 0.4f;
					}
					_drifting = false;
					_boostCharge = 0f;
				}
			}
		}

		Vector2 velDir = Velocity.Length() > 1f ? Velocity.Normalized() : Vector2.Right;

		// You already have most of this, just ensure it's present:
		if (_mat != null)
		{
			_mat.SetShaderParameter("velocity_dir", velDir);
		}

		
		_heading = Mathf.Wrap(_heading, -Mathf.Pi, Mathf.Pi);
		GlobalRotation = _heading;

		if (ShowDebugVectors)
			QueueRedraw();
	}

	private bool _IsCounterSteering(float steerIn)
	{
		if (Mathf.Abs(steerIn) < 0.1f || Mathf.Abs(_lateralSpeed) < SlipEndThreshold)
			return false;
		return Mathf.Sign(steerIn) != Mathf.Sign(_lateralSpeed);
	}

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
			float radius = Wheelbase / Mathf.Tan(steerAngle);
			DrawArc(new Vector2(0, radius), Mathf.Abs(radius), 0, Mathf.Tau, 64, Colors.Cyan, 1f);
		}
	}
}