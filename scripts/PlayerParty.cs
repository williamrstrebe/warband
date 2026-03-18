using Godot;

public partial class PlayerParty : Area2D, IFixedTick
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

	[Export] public bool DrawDetectionRadius { get; set; } = true;

	[Export] public Color Color { get; set; } = new(1.0f, 0.85f, 0.2f, 1.0f);

	[Export] public NodePath WorldBoundsPath { get; set; } = new("");

	private WorldBounds2D? _bounds;
	private Vector2 _target;
	private bool _hasTarget;
	private bool _draggingTarget;

	private Area2D? _detectionArea;

	public float CurrentSpeedPxPerSec =>
		BaseSpeedPxPerSec / (1f + Mathf.Max(0, PartySize) * PartySizePenaltyK);

	public override void _Ready()
	{
		_bounds = GetNodeOrNull<WorldBounds2D>(WorldBoundsPath);
		_target = GlobalPosition;
		EnsureCollisionAndDetection();
		QueueRedraw();
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (Input.IsActionJustPressed(InputActions.PlayerSetMoveTarget))
		{
			_draggingTarget = true;
			SetTarget(GetGlobalMousePosition());
			return;
		}

		if (Input.IsActionJustReleased(InputActions.PlayerSetMoveTarget))
		{
			_draggingTarget = false;
			return;
		}

		if (_draggingTarget && @event is InputEventMouseMotion)
		{
			SetTarget(GetGlobalMousePosition());
		}
	}

	public void FixedTick(double tickDeltaSeconds)
	{
		if (!_hasTarget) return;

		var pos = GlobalPosition;
		var toTarget = _target - pos;
		var dist = toTarget.Length();

		if (dist <= StopThresholdPx)
		{
			_hasTarget = false;
			GlobalPosition = Clamp(pos);
			QueueRedraw();
			return;
		}

		var step = (float)(CurrentSpeedPxPerSec * tickDeltaSeconds);
		var newPos = dist <= step ? _target : pos + (toTarget / dist) * step;
		GlobalPosition = Clamp(newPos);
		QueueRedraw();
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
		GD.Print($"[Detect] Player sees {area.Name}");
	}

	private void OnDetectionAreaExited(Area2D area)
	{
		if (area == this) return;
		if (area.GetParent() == this) return;
		GD.Print($"[Detect] Player lost {area.Name}");
	}

	public override void _Draw()
	{
		DrawCircle(Vector2.Zero, RadiusPx, Color);

		DrawPartySizeLabel();

		if (DrawDetectionRadius && DetectionRadiusPx > 0)
			DrawArc(Vector2.Zero, DetectionRadiusPx, 0, Mathf.Tau, 64, new Color(1, 1, 1, 0.12f), 2f);

		if (_hasTarget)
		{
			var localTarget = ToLocal(_target);
			DrawCircle(localTarget, 4f, new Color(1, 1, 1, 0.9f));
			DrawLine(Vector2.Zero, localTarget, new Color(1, 1, 1, 0.35f), 1.5f);
		}
	}

	private void DrawPartySizeLabel()
	{
		var font = RandomAI.PartyFont;
		if (font == null) return;

		var text = $"{PartySize}";
		var size = font.GetStringSize(text, HorizontalAlignment.Left, -1, 14);
		var pos = new Vector2(-size.X / 2f, -(RadiusPx + 6f));
		DrawString(font, pos, text, HorizontalAlignment.Left, -1, 14, new Color(1, 1, 1, 0.95f));
	}
}
