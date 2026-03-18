using Godot;

public partial class RandomAI : Area2D, IFixedTick
{
	// Phase 1 Step 1.2: speed = baseSpeed / (1 + partySize * k)
	[Export(PropertyHint.Range, "1,2000,1")]
	public float BaseSpeedPxPerSec { get; set; } = 260f;

	[Export(PropertyHint.Range, "0,500,1")]
	public int PartySize { get; set; } = 10;

	[Export(PropertyHint.Range, "0,1,0.001")]
	public float PartySizePenaltyK { get; set; } = 0.02f;

	[Export(PropertyHint.Range, "0,500,1")]
	public int PartySizeMin { get; set; } = 0;

	[Export(PropertyHint.Range, "0,500,1")]
	public int PartySizeMax { get; set; } = 200;

	[Export(PropertyHint.Range, "0.5,64,0.5")]
	public float StopThresholdPx { get; set; } = 6f;

	[Export(PropertyHint.Range, "2,128,1")]
	public float RadiusPx { get; set; } = 10f;

	// Phase 2 Step 2.2: detection radius
	[Export(PropertyHint.Range, "0,2048,1")]
	public float DetectionRadiusPx { get; set; } = 160f;

	[Export] public bool DrawDetectionRadius { get; set; } = false;

	// Phase 2 Step 2.3: strength proxy for chase/flee (first pass)
	[Export(PropertyHint.Range, "0,5000,1")]
	public int StrengthValue { get; set; } = 0;

	[Export] public Color Color { get; set; } = new(0.97f, 0.17f, 0.24f, 1.0f);

	[Export] public NodePath WorldBoundsPath { get; set; } = new("");

	private WorldBounds2D? _bounds;
	private Vector2 _target;
	private bool _hasTarget;

	private enum AiState
	{
		WanderIdle,
		WanderMoving,
		Chase,
		Flee
	}

	private AiState _state = AiState.WanderIdle;

	private int _idleTicksLeft;
	private int _retargetTicksLeft;
	private static readonly RandomNumberGenerator Rng = new();

	private Area2D? _detectionArea;
	private PlayerParty? _playerInRange;

	public float CurrentSpeedPxPerSec =>
		BaseSpeedPxPerSec / (1f + Mathf.Max(0, PartySize) * PartySizePenaltyK);

	public override void _Ready()
	{
		_bounds = GetNodeOrNull<WorldBounds2D>(WorldBoundsPath);
		_state = AiState.WanderIdle;
		_idleTicksLeft = GetRandomIdleTicks();
		_target = GlobalPosition;
		_retargetTicksLeft = GetRandomRetargetTicks();
		PartySize = Rng.RandiRange(1,5);
		if (StrengthValue <= 0)
			StrengthValue = PartySize; // first proxy; Phase 2.3 can evolve later
		EnsureCollisionAndDetection();
		QueueRedraw();
	}

	private static Vector2 GetRandomPoint(WorldBounds2D bounds)
	{
		var r = bounds.InnerRect;

		return new Vector2(
			Rng.RandfRange(r.Position.X, r.End.X),
			Rng.RandfRange(r.Position.Y, r.End.Y)
		);
	}

	private static int GetRandomRetargetTicks()
	{
		var seconds = Rng.RandfRange(1f, 3f);
		return (int)(seconds * 10); // matches 10 ticks/sec default
	}

	private static int GetRandomIdleTicks()
	{
		var seconds = Rng.RandfRange(0.5f, 1.5f);
		return (int)(seconds * 10);
	}

	public void FixedTick(double tickDeltaSeconds)
	{
		if (_bounds == null)
			return;

		// Phase 2 Step 2.3 behavior switch: default Wander, but Chase/Flee if player detected.
		if (_playerInRange != null)
		{
			var playerStrength = Mathf.Max(0, _playerInRange.PartySize);
			_state = StrengthValue >= playerStrength ? AiState.Chase : AiState.Flee;
		}
		else if (_state is AiState.Chase or AiState.Flee)
		{
			_state = AiState.WanderIdle;
			_idleTicksLeft = GetRandomIdleTicks();
		}

		switch (_state)
		{
			case AiState.WanderIdle:
				if (--_idleTicksLeft <= 0)
				{
					ChooseNewDestination();
					_state = AiState.WanderMoving;
				}
				break;

			case AiState.WanderMoving:
				TickWanderMovement(tickDeltaSeconds);
				break;

			case AiState.Chase:
				TickChase(tickDeltaSeconds);
				break;

			case AiState.Flee:
				TickFlee(tickDeltaSeconds);
				break;
		}
	}

	private void TickWanderMovement(double tickDeltaSeconds)
	{
		if (!_hasTarget)
			return;

		if (--_retargetTicksLeft <= 0)
		{
			ChooseNewDestination();
			_retargetTicksLeft = GetRandomRetargetTicks();
		}

		var pos = GlobalPosition;
		var toTarget = _target - pos;
		var dist = toTarget.Length();

		if (dist <= StopThresholdPx)
		{
			_hasTarget = false;
			GlobalPosition = Clamp(pos);
			_state = AiState.WanderIdle;
			_idleTicksLeft = GetRandomIdleTicks();
			QueueRedraw();
			return;
		}

		var step = (float)(CurrentSpeedPxPerSec * tickDeltaSeconds);
		var newPos = dist <= step ? _target : pos + (toTarget / dist) * step;
		GlobalPosition = Clamp(newPos);
		QueueRedraw();
	}

	private void TickChase(double tickDeltaSeconds)
	{
		if (_playerInRange == null)
			return;

		SetTarget(_playerInRange.GlobalPosition);
		TickDirectMovement(tickDeltaSeconds, stopWhenClose: false);
	}

	private void TickFlee(double tickDeltaSeconds)
	{
		if (_playerInRange == null || _bounds == null)
			return;

		var away = (GlobalPosition - _playerInRange.GlobalPosition);
		var dir = away.LengthSquared() < 0.001f ? Vector2.Right : away.Normalized();
		var fleeDistance = Mathf.Max(DetectionRadiusPx * 0.9f, 120f);
		var fleeTarget = GlobalPosition + dir * fleeDistance;
		SetTarget(fleeTarget);
		TickDirectMovement(tickDeltaSeconds, stopWhenClose: false);
	}

	private void TickDirectMovement(double tickDeltaSeconds, bool stopWhenClose)
	{
		if (!_hasTarget)
			return;

		var pos = GlobalPosition;
		var toTarget = _target - pos;
		var dist = toTarget.Length();

		if (stopWhenClose && dist <= StopThresholdPx)
		{
			_hasTarget = false;
			GlobalPosition = Clamp(pos);
			QueueRedraw();
			return;
		}

		if (dist <= 0.0001f)
			return;

		var step = (float)(CurrentSpeedPxPerSec * tickDeltaSeconds);
		var newPos = dist <= step ? _target : pos + (toTarget / dist) * step;
		GlobalPosition = Clamp(newPos);
		QueueRedraw();
	}

	private void ChooseNewDestination()
	{
		if (_bounds == null)
			return;

		var worldPoint = GetRandomPoint(_bounds);
		SetTarget(worldPoint);
	}

	private void SetTarget(Vector2 worldPoint)
	{
		_hasTarget = true;
		_target = Clamp(worldPoint);
	}

	private Vector2 Clamp(Vector2 p)
	{
		if (_bounds == null) return p;
		// Clamp the party center into the inner rect; radius-aware clamp can be added later.
		return _bounds.ClampPointToInnerRect(p);
	}

	private void EnsureCollisionAndDetection()
	{
		// Body collider: lets other areas (detection) sense us.
		var body = GetNodeOrNull<CollisionShape2D>("BodyShape");
		if (body == null)
		{
			body = new CollisionShape2D { Name = "BodyShape" };
			AddChild(body);
		}

		if (body.Shape is not CircleShape2D bodyCircle)
		{
			bodyCircle = new CircleShape2D();
			body.Shape = bodyCircle;
		}
		bodyCircle.Radius = RadiusPx;

		// Put parties on layer 1; they don't need to actively detect anything.
		CollisionLayer = 1;
		CollisionMask = 0;

		_detectionArea = GetNodeOrNull<Area2D>("Detection");
		if (_detectionArea == null)
		{
			_detectionArea = new Area2D { Name = "Detection" };
			AddChild(_detectionArea);

			var detShape = new CollisionShape2D { Name = "DetectionShape" };
			detShape.Shape = new CircleShape2D();
			_detectionArea.AddChild(detShape);
		}

		_detectionArea.Monitoring = true;
		_detectionArea.Monitorable = true;
		_detectionArea.CollisionLayer = 0; // detection area doesn't need to be detected
		_detectionArea.CollisionMask = 1; // detect parties

		var shapeNode = _detectionArea.GetNodeOrNull<CollisionShape2D>("DetectionShape");
		if (shapeNode?.Shape is CircleShape2D detCircle)
			detCircle.Radius = DetectionRadiusPx;

		_detectionArea.AreaEntered += OnDetectionAreaEntered;
		_detectionArea.AreaExited += OnDetectionAreaExited;
	}

	private void OnDetectionAreaEntered(Area2D area)
	{
		if (area == this) return;
		if (area.GetParent() == this) return; // ignore our own Detection area

		if (area is PlayerParty player)
		{
			_playerInRange = player;
			GD.Print($"[Detect] {Name} saw Player");
			return;
		}

		GD.Print($"[Detect] {Name} saw {area.Name}");
	}

	private void OnDetectionAreaExited(Area2D area)
	{
		if (area == this) return;
		if (area.GetParent() == this) return;

		if (area == _playerInRange)
		{
			_playerInRange = null;
			GD.Print($"[Detect] {Name} lost Player");
			return;
		}

		GD.Print($"[Detect] {Name} lost {area.Name}");
	}

	public override void _Draw()
	{
		DrawCircle(Vector2.Zero, RadiusPx, Color);

		DrawPartySizeLabel();

		if (DrawDetectionRadius && DetectionRadiusPx > 0)
			DrawArc(Vector2.Zero, DetectionRadiusPx, 0, Mathf.Tau, 64, new Color(1, 1, 1, 0.12f), 2f);
	}

	private void DrawPartySizeLabel()
	{
		var font = PartyFont;
		if (font == null) return;

		var text = $"{PartySize}";
		var size = font.GetStringSize(text, HorizontalAlignment.Left, -1, 14);
		var pos = new Vector2(-size.X / 2f, -(RadiusPx + 6f));
		DrawString(font, pos, text, HorizontalAlignment.Left, -1, 14, new Color(1, 1, 1, 0.95f));
	}
	
	public static readonly Font PartyFont = new SystemFont
	{
		FontNames = new[] { "Arial", "Liberation Sans", "DejaVu Sans" },
		Oversampling = 1.5f
	};
}
