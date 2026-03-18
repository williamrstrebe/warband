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
	private RandomAI? _weakerAiInRange;
	private int _restTicksLeft;

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

		if (_playerInRange != null && _playerInRange.IsQueuedForDeletion())
			_playerInRange = null;
		if (_weakerAiInRange != null && _weakerAiInRange.IsQueuedForDeletion())
			_weakerAiInRange = null;

		// After AI-vs-AI battles, winners rest for a few seconds to avoid immediate re-engagement.
		if (_restTicksLeft > 0)
		{
			_restTicksLeft--;
			ClearTarget();
			_state = AiState.WanderIdle;
			return;
		}

		// Phase 3 extension: re-evaluate weaker AI targets as strengths change.
		if (_weakerAiInRange != null)
		{
			// If the target is no longer weaker (or drifted out of range), clear it.
			var dist = GlobalPosition.DistanceTo(_weakerAiInRange.GlobalPosition);
			var inRange = dist <= (DetectionRadiusPx + _weakerAiInRange.RadiusPx);
			if (!inRange || _weakerAiInRange.StrengthValue >= StrengthValue)
			{
				_weakerAiInRange = null;
			}
		}
		else
		{
			AcquireWeakerAiTarget();
		}

		// Phase 2 Step 2.3 behavior switch: default Wander, but Chase/Flee if player detected.
		if (_playerInRange != null)
		{
			var playerStrength = Mathf.Max(0, _playerInRange.PartySize);
			_state = StrengthValue >= playerStrength ? AiState.Chase : AiState.Flee;
		}
		else if (_weakerAiInRange != null)
		{
			// Phase 3 extension: chase and attack weaker AI parties.
			_state = AiState.Chase;
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

	// Called by SimulationRoot after AI-vs-AI battles.
	public void StartRestTicks(int ticks)
	{
		_restTicksLeft = Mathf.Max(_restTicksLeft, Mathf.Max(0, ticks));
		ClearTarget();
		_state = AiState.WanderIdle;
		// Debug visuals are label-based; redraw so changes are immediate.
		QueueRedraw();
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
		if (_playerInRange != null)
		{
			SetTarget(_playerInRange.GlobalPosition);
			TickMoveTowardTarget(tickDeltaSeconds, stopWhenClose: false);
			return;
		}

		if (_weakerAiInRange == null)
			return;

		SetTarget(_weakerAiInRange.GlobalPosition);
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

		if (other is RandomAI otherAi)
		{
			// Chase other AI only if they're weaker by strength proxy.
			var otherStrength = otherAi.StrengthValue;
			if (otherStrength < StrengthValue)
			{
				if (_weakerAiInRange == null || otherStrength > _weakerAiInRange.StrengthValue)
					_weakerAiInRange = otherAi;
			}

			return;
		}
	}

	protected override void OnPartyLost(PartyBase other)
	{
		if (other == _playerInRange)
		{
			_playerInRange = null;
			GD.Print($"[Detect] {Name} lost Player");
			return;
		}

		if (other == _weakerAiInRange)
		{
			_weakerAiInRange = null;
			GD.Print($"[Detect] {Name} lost weaker AI");
			return;
		}
	}

	private void AcquireWeakerAiTarget()
	{
		// If AIs change strength due to battles, we must adapt even when no new
		// AreaEntered event fires. So we re-acquire a weaker target by distance each tick.
		var parent = GetParent();
		if (parent == null)
			return;

		var best = (RandomAI?)null;
		var bestDist = float.PositiveInfinity;

		foreach (var child in parent.GetChildren())
		{
			if (child is not RandomAI other)
				continue;
			if (other == this)
				continue;
			if (other.IsQueuedForDeletion())
				continue;

			var dist = GlobalPosition.DistanceTo(other.GlobalPosition);
			var inRange = dist <= (DetectionRadiusPx + other.RadiusPx);
			if (!inRange)
				continue;
			if (other.StrengthValue >= StrengthValue)
				continue; // only chase weaker

			if (dist < bestDist)
			{
				best = other;
				bestDist = dist;
			}
		}

		_weakerAiInRange = best;
	}
}
