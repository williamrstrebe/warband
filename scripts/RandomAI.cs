using Godot;
using System;

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

	[Export] public Color Color { get; set; } = new(0.97f, 0.17f, 0.24f, 1.0f);

	[Export] public NodePath WorldBoundsPath { get; set; } = new("");

	private WorldBounds2D? _bounds;
	private Vector2 _target;
	private bool _hasTarget;
	private bool _draggingTarget;
	
	private int _retargetTicksLeft;
	private static RandomNumberGenerator rng = new RandomNumberGenerator();

	public float CurrentSpeedPxPerSec =>
		BaseSpeedPxPerSec / (1f + Mathf.Max(0, PartySize) * PartySizePenaltyK);

	public override void _Ready()
	{
		_bounds = GetNodeOrNull<WorldBounds2D>(WorldBoundsPath);
		GD.Print($"New target1: {_target}");
		_target = GetRandomPoint(_bounds);
		GD.Print($"New target2: {_target}");
		SetTarget(_target);
		_retargetTicksLeft = GetRandomRetargetTicks();
		QueueRedraw();
	}
	
	private Vector2 GetRandomPoint(WorldBounds2D bounds)
	{
		Rect2 r = bounds.InnerRect;

		return new Vector2(
			rng.RandfRange(r.Position.X, r.End.X),
			rng.RandfRange(r.Position.Y, r.End.Y)
		);
	}
	
	private int GetRandomRetargetTicks()
	{
		float seconds = rng.RandfRange(1f, 3f);
		return (int)(seconds * 10);
	}

	public void FixedTick(double tickDeltaSeconds){
		if (!_hasTarget) return;
		
		_retargetTicksLeft--;

		_target = GetRandomPoint(_bounds);
		GD.Print($"New target3: {_target}");
		if (_retargetTicksLeft <= 0)
		{
			_retargetTicksLeft = GetRandomRetargetTicks();
		}

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

	public override void _Draw()
	{
		DrawCircle(Vector2.Zero, RadiusPx, Color);
	}
}
