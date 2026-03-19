using Godot;

public abstract partial class PartyBase : Area2D, IFixedTick
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

	// Phase 3+4: logistics values. HUD display is controlled by SimulationRoot.
	[Export(PropertyHint.Range, "0,100,1")]
	public int Morale { get; set; } = 10;

	[Export(PropertyHint.Range, "0,999999,1")]
	public int Gold { get; set; } = 0;

	[Export(PropertyHint.Range, "0.5,64,0.5")]
	public float StopThresholdPx { get; set; } = 6f;

	[Export(PropertyHint.Range, "2,128,1")]
	public float RadiusPx { get; set; } = 10f;

	// Phase 2 Step 2.2: detection radius
	[Export(PropertyHint.Range, "0,2048,1")]
	public float DetectionRadiusPx { get; set; } = 160f;

	[Export] public bool DrawDetectionRadius { get; set; } = false;

	[Export] public Color Color { get; set; } = new(1, 1, 1, 1);

	[Export] public NodePath WorldBoundsPath { get; set; } = new("");

	private int _ticksPerSecond = 10;

	/// <summary>
	/// Configured by `SimulationRoot` so tick-based timers use the real tick rate.
	/// </summary>
	public void SetTicksPerSecond(int ticksPerSecond)
	{
		_ticksPerSecond = Mathf.Max(1, ticksPerSecond);
	}

	protected int TickRate => _ticksPerSecond;

	protected WorldBounds2D? Bounds { get; private set; }

	protected Vector2 Target { get; private set; }
	protected bool HasTarget { get; private set; }

	private Area2D? _detectionArea;

	public float CurrentSpeedPxPerSec =>
		BaseSpeedPxPerSec / (1f + Mathf.Max(0, PartySize) * PartySizePenaltyK);

	public override void _Ready()
	{
		Bounds = GetNodeOrNull<WorldBounds2D>(WorldBoundsPath);
		Target = GlobalPosition;
		EnsureCollisionAndDetection();
		AfterBaseReady();
		QueueRedraw();
	}

	protected virtual void AfterBaseReady() { }

	public abstract void FixedTick(double tickDeltaSeconds);

	protected void SetTarget(Vector2 worldPoint)
	{
		HasTarget = true;
		Target = Clamp(worldPoint);
	}

	protected void ClearTarget()
	{
		HasTarget = false;
	}

	protected Vector2 Clamp(Vector2 p)
	{
		if (Bounds == null) return p;
		// Clamp the party center into the inner rect; radius-aware clamp can be added later.
		return Bounds.ClampPointToInnerRect(p);
	}

	protected bool TickMoveTowardTarget(double tickDeltaSeconds, bool stopWhenClose = true)
	{
		if (!HasTarget)
			return false;

		var pos = GlobalPosition;
		var toTarget = Target - pos;
		var dist = toTarget.Length();

		if (stopWhenClose && dist <= StopThresholdPx)
		{
			ClearTarget();
			GlobalPosition = Clamp(pos);
			QueueRedraw();
			return true;
		}

		if (dist <= 0.0001f)
			return false;

		var step = (float)(CurrentSpeedPxPerSec * tickDeltaSeconds);
		var newPos = dist <= step ? Target : pos + (toTarget / dist) * step;
		GlobalPosition = Clamp(newPos);
		QueueRedraw();
		return true;
	}

	private void EnsureCollisionAndDetection()
	{
		EnsureBodyCollider();

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

		_detectionArea.AreaEntered -= HandleDetectionAreaEntered;
		_detectionArea.AreaExited -= HandleDetectionAreaExited;
		_detectionArea.AreaEntered += HandleDetectionAreaEntered;
		_detectionArea.AreaExited += HandleDetectionAreaExited;
	}

	private void EnsureBodyCollider()
	{
		var body = GetNodeOrNull<CollisionShape2D>("BodyShape");
		if (body == null)
		{
			// Reuse any existing collider the scene might have added.
			foreach (var child in GetChildren())
			{
				if (child is not CollisionShape2D cs) continue;
				if (cs.Name == "DetectionShape") continue;
				body = cs;
				body.Name = "BodyShape";
				break;
			}
		}

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
	}

	private void HandleDetectionAreaEntered(Area2D area)
	{
		if (!ShouldDetect(area))
			return;

		if (area is PartyBase otherParty)
			OnPartyDetected(otherParty);
		else
			OnOtherDetected(area);
	}

	private void HandleDetectionAreaExited(Area2D area)
	{
		if (!ShouldDetect(area))
			return;

		if (area is PartyBase otherParty)
			OnPartyLost(otherParty);
		else
			OnOtherLost(area);
	}

	protected virtual bool ShouldDetect(Area2D area)
	{
		if (area == this) return false;
		// Ignore our own Detection area, etc.
		if (area.GetParent() == this) return false;
		return true;
	}

	protected virtual void OnPartyDetected(PartyBase other) { }
	protected virtual void OnPartyLost(PartyBase other) { }
	protected virtual void OnOtherDetected(Area2D area) { }
	protected virtual void OnOtherLost(Area2D area) { }

	public override void _Draw()
	{
		DrawCircle(Vector2.Zero, RadiusPx, Color);
		DrawPartySizeLabel();

		if (DrawDetectionRadius && DetectionRadiusPx > 0)
			DrawArc(Vector2.Zero, DetectionRadiusPx, 0, Mathf.Tau, 64, new Color(1, 1, 1, 0.12f), 2f);
	}

	protected void DrawPartySizeLabel()
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
