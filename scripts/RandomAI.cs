using Godot;

public partial class RandomAI : PartyBase
{
	// Phase 2 Step 2.3: strength proxy for chase/flee (first pass)
	[Export(PropertyHint.Range, "0,5000,1")]
	public int StrengthValue { get; set; } = 0;

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
	private PlayerParty? _playerInRange;

	protected override void AfterBaseReady()
	{
		// AI defaults (can still be overridden in the editor).
		if (Color == new Color(1, 1, 1, 1))
			Color = new Color(0.97f, 0.17f, 0.24f, 1.0f);

		_state = AiState.WanderIdle;
		_idleTicksLeft = GetRandomIdleTicks();
		_retargetTicksLeft = GetRandomRetargetTicks();

		PartySize = Rng.RandiRange(1, 5);
		if (StrengthValue <= 0)
			StrengthValue = PartySize; // first proxy; Phase 2.3 can evolve later
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

	public override void FixedTick(double tickDeltaSeconds)
	{
		if (Bounds == null)
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
		if (--_retargetTicksLeft <= 0)
		{
			ChooseNewDestination();
			_retargetTicksLeft = GetRandomRetargetTicks();
		}

		var arrived = TickMoveTowardTarget(tickDeltaSeconds, stopWhenClose: true);
		if (arrived && !HasTarget)
		{
			_state = AiState.WanderIdle;
			_idleTicksLeft = GetRandomIdleTicks();
		}
	}

	private void TickChase(double tickDeltaSeconds)
	{
		if (_playerInRange == null)
			return;

		SetTarget(_playerInRange.GlobalPosition);
		TickMoveTowardTarget(tickDeltaSeconds, stopWhenClose: false);
	}

	private void TickFlee(double tickDeltaSeconds)
	{
		if (_playerInRange == null || Bounds == null)
			return;

		var away = (GlobalPosition - _playerInRange.GlobalPosition);
		var dir = away.LengthSquared() < 0.001f ? Vector2.Right : away.Normalized();
		var fleeDistance = Mathf.Max(DetectionRadiusPx * 0.9f, 120f);
		var fleeTarget = GlobalPosition + dir * fleeDistance;
		SetTarget(fleeTarget);
		TickMoveTowardTarget(tickDeltaSeconds, stopWhenClose: false);
	}

	private void ChooseNewDestination()
	{
		if (Bounds == null)
			return;

		var worldPoint = GetRandomPoint(Bounds);
		SetTarget(worldPoint);
	}

	protected override void OnPartyDetected(PartyBase other)
	{
		if (other is PlayerParty player)
		{
			_playerInRange = player;
			GD.Print($"[Detect] {Name} saw Player");
			return;
		}

		GD.Print($"[Detect] {Name} saw {other.Name}");
	}

	protected override void OnPartyLost(PartyBase other)
	{
		if (other == _playerInRange)
		{
			_playerInRange = null;
			GD.Print($"[Detect] {Name} lost Player");
			return;
		}

		GD.Print($"[Detect] {Name} lost {other.Name}");
	}
}
