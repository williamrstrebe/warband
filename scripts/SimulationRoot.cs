using Godot;
using Warband.Core;

public partial class SimulationRoot : Node2D
{
	[Export] public int TicksPerSecond { get; set; } = 60;
	[Export] public int MaxTicksPerFrame { get; set; } = 120;

	private FixedTickClock? _clock;
	private int _timeScaleIndex = 1;
	private Label? _debugLabel;
	private Label? _debugHintLabel;
	private string? _cachedControlsText;

	[Export] public NodePath DebugLabelPath { get; set; } = new("");
	[Export] public NodePath DebugHintLabelPath { get; set; } = new("");
	[Export] public NodePath WorldBoundsPath { get; set; } = new("");
	[Export] public NodePath EncounterModalPath { get; set; } = new("");
	[Export] public NodePath TownModalPath { get; set; } = new("");

	// Phase 2 Step 2.1: spawn N roaming AI parties.
	[Export(PropertyHint.Range, "0,500,1")]
	public int AiCount { get; set; } = 0;

	// Phase 4 Step 4.1: simple towns + recruit stub.
	[Export(PropertyHint.Range, "0,50,1")]
	public int TownCount { get; set; } = 2;

	[Export(PropertyHint.Range, "0,9999,1")]
	public int RecruitCostGold { get; set; } = 3;

	[Export(PropertyHint.Range, "1,100,1")]
	public int RecruitTroopsAmount { get; set; } = 5;

	// Phase 4 Step 4.2: wage tick system (prototype values).
	// Timeline mapping (temporary):
	// - 1 sim second = `SimSecondsPerInGameHour` in-game hours.
	// - Wages are applied every `WageIntervalDays` days.
	[Export(PropertyHint.Range, "0.01,24,0.01")]
	public float SimSecondsPerInGameHour { get; set; } = 1f;

	[Export(PropertyHint.Range, "1,365,1")]
	public int WageIntervalDays { get; set; } = 7;

	[Export(PropertyHint.Range, "0,1000,1")]
	public int WageGoldPerTroopPerDay { get; set; } = 1;

	// Phase 4 Step 4.3: morale algorithm (v1).
	[Export(PropertyHint.Range, "0,10,1")]
	public int MoraleVictoryBonus { get; set; } = 2;

	[Export(PropertyHint.Range, "0,10,1")]
	public int MoraleDefeatPenalty { get; set; } = 2;

	[Export(PropertyHint.Range, "0,10,1")]
	public int MoraleOutnumberedPenalty { get; set; } = 2;

	[Export(PropertyHint.Range, "0,10,1")]
	public int MoraleStarvationPerDay { get; set; } = 1;

	// Morale influences battle power via:
	// moraleFactor = MoraleCombatPowerFactorBase + (morale/100)*MoraleCombatPowerFactorScale
	[Export(PropertyHint.Range, "0,2,0.01")]
	public float MoraleCombatPowerFactorBase { get; set; } = 0.5f;

	[Export(PropertyHint.Range, "0,3,0.01")]
	public float MoraleCombatPowerFactorScale { get; set; } = 1f;

	// From an AI party node (child of SimulationRoot), the WorldBounds node in Main.tscn is at ../../WorldBounds.
	// If the scene structure changes, update this in the editor.
	[Export] public NodePath WorldBoundsPathFromParty { get; set; } = new("../../WorldBounds");

	private static readonly RandomNumberGenerator Rng = new();

	private EncounterModal? _encounterModal;
	private RandomAI? _encounterAi;
	private int _encounterCooldownTicksLeft;

	private TownModal? _townModal;
	private Town? _activeTown;
	private int _townCooldownTicksLeft;

	// Wage tick scheduling.
	private double _wageIntervalSeconds;
	private double _wageSecondsLeft;

	// Starvation tick scheduling.
	private double _starvationIntervalSeconds;
	private double _starvationSecondsLeft;

	public override void _Ready()
	{
		_clock = new FixedTickClock(TicksPerSecond);
		_debugLabel = GetNodeOrNull<Label>(DebugLabelPath);
		_debugHintLabel = GetNodeOrNull<Label>(DebugHintLabelPath);
		_encounterModal = GetNodeOrNull<EncounterModal>(EncounterModalPath);
		if (_encounterModal != null)
		{
			_encounterModal.FightPressed += () => ResolveEncounter(EncounterChoice.Fight);
			_encounterModal.AutoResolvePressed += () => ResolveEncounter(EncounterChoice.AutoResolve);
			_encounterModal.FleePressed += () => ResolveEncounter(EncounterChoice.Flee);
		}
		_townModal = GetNodeOrNull<TownModal>(TownModalPath);
		if (_townModal != null)
		{
			_townModal.RecruitPressed += TryRecruitFromTown;
			_townModal.LeavePressed += LeaveTown;
		}
		_cachedControlsText = BuildControlsText();

		_wageIntervalSeconds = WageIntervalDays * 24.0 * SimSecondsPerInGameHour;
		_wageSecondsLeft = _wageIntervalSeconds;

		_starvationIntervalSeconds = 24.0 * SimSecondsPerInGameHour;
		_starvationSecondsLeft = _starvationIntervalSeconds;

		// Propagate tick rate into all party nodes so tick-based timers behave correctly.
		foreach (var child in GetChildren())
		{
			if (child is PartyBase party)
				party.SetTicksPerSecond(TicksPerSecond);
		}

		SpawnTowns();
		SpawnAiParties();
		QueueRedraw();
	}

	public override void _Process(double delta)
	{
		if (_clock == null) return;

		var player = GetNodeOrNull<PlayerParty>("Player");
		if (player != null)
		{
			// Handle party size changes here (polled) so it works even if UI consumes arrow keys.
			if (Input.IsActionJustPressed(InputActions.PartySizeIncrease))
				player.PartySize = Mathf.Clamp(player.PartySize + 1, player.PartySizeMin, player.PartySizeMax);
			else if (Input.IsActionJustPressed(InputActions.PartySizeDecrease))
				player.PartySize = Mathf.Clamp(player.PartySize - 1, player.PartySizeMin, player.PartySizeMax);
		}

		var produced = _clock.Advance(
			realDeltaSeconds: delta,
			timeScale: TimeScale.FromIndex(_timeScaleIndex),
			maxTicksThisAdvance: MaxTicksPerFrame);

		for (var i = 0; i < produced; i++)
			OnTick(_clock.TickDeltaSeconds);

		if (_clock != null)
		{
			var inGameHoursTotal = _clock.SimTimeSeconds / SimSecondsPerInGameHour;
			var day = (int)(inGameHoursTotal / 24.0);
			var hour = (int)(inGameHoursTotal % 24.0);

			var wageLeftHoursTotal = Math.Max(0.0, _wageSecondsLeft / SimSecondsPerInGameHour);
			var wageLeftDays = (int)(wageLeftHoursTotal / 24.0);
			var wageLeftHour = (int)(wageLeftHoursTotal % 24.0);

			var partyText = player == null
				? ""
				: $"  |  Day: {day}  Hr: {hour}  |  Party: {player.PartySize}  |  Morale: {player.Morale}  |  Gold: {player.Gold}  |  Wage in: {wageLeftDays}d {wageLeftHour}h  |  Speed: {player.CurrentSpeedPxPerSec:0.##} px/s";
			_debugLabel?.SetText($"Ticks: {_clock.TotalTicks}  |  SimTime: {_clock.SimTimeSeconds:0.000}s  |  Scale: {TimeScale.FromIndex(_timeScaleIndex)}x{partyText}");
			_debugHintLabel?.SetText(_cachedControlsText ?? BuildControlsText());
		}
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (!@event.IsPressed()) return;

		if (Input.IsActionJustPressed(InputActions.SimTogglePause))
		{
			_timeScaleIndex = _timeScaleIndex == 0 ? 1 : 0;
		}
		else if (Input.IsActionJustPressed(InputActions.SimTimeSlower))
		{
			_timeScaleIndex = TimeScale.ClampIndex(_timeScaleIndex - 1);
		}
		else if (Input.IsActionJustPressed(InputActions.SimTimeFaster))
		{
			_timeScaleIndex = TimeScale.ClampIndex(_timeScaleIndex + 1);
		}
	}

	private void OnTick(double tickDeltaSeconds)
	{
		foreach (var child in GetChildren())
		{
			if (child is IFixedTick tickable)
				tickable.FixedTick(tickDeltaSeconds);
		}

		if (_encounterCooldownTicksLeft > 0)
			_encounterCooldownTicksLeft--;
		if (_townCooldownTicksLeft > 0)
			_townCooldownTicksLeft--;

		TryEnterTown();
		TryResolveAiBattles();
		UpdateWagesCountdown(tickDeltaSeconds);
		UpdateStarvationCountdown(tickDeltaSeconds);
		TryStartEncounter();
	}

	private void UpdateStarvationCountdown(double tickDeltaSeconds)
	{
		if (MoraleStarvationPerDay <= 0)
			return;
		if (SimSecondsPerInGameHour <= 0 || _starvationIntervalSeconds <= 0)
			return;

		_starvationSecondsLeft -= tickDeltaSeconds;

		while (_starvationSecondsLeft <= 0)
		{
			ApplyStarvationTick();
			_starvationSecondsLeft += _starvationIntervalSeconds;
		}
	}

	private void ApplyStarvationTick()
	{
		// Phase 4 Step 4.3: starvation reduces morale once per game day.
		foreach (var child in GetChildren())
		{
			if (child is PartyBase party && party is not null && !party.IsQueuedForDeletion())
			{
				party.Morale = Mathf.Clamp(party.Morale - MoraleStarvationPerDay, 0, 100);
				// AI morale isn't shown, but it affects future combat resolution.
			}
		}
	}

	private void UpdateWagesCountdown(double tickDeltaSeconds)
	{
		// Wage interval schedule is kept in sim-time ticks so it remains deterministic.
		if (WageIntervalDays <= 0 || WageGoldPerTroopPerDay <= 0 || SimSecondsPerInGameHour <= 0)
			return;

		_wageSecondsLeft -= tickDeltaSeconds;

		while (_wageSecondsLeft <= 0)
		{
			ApplyWageTick();
			_wageSecondsLeft += _wageIntervalSeconds;
		}
	}

	private void ApplyWageTick()
	{
		// Phase 4 Step 4.2: gold -= troopCount * wage.
		// Here: wage is `WageGoldPerTroopPerDay` applied over `WageIntervalDays`.
		var player = GetNodeOrNull<PlayerParty>("Player");
		if (player == null)
			return;

		var troopCount = player.PartySize;
		var wageCost = troopCount * WageGoldPerTroopPerDay * WageIntervalDays;
		if (wageCost <= 0)
			return;

		player.Gold = Mathf.Max(0, player.Gold - wageCost);

		// Prototype desertion stub: if we hit 0 gold, lose 1 troop and morale -1.
		if (player.Gold <= 0 && troopCount > player.PartySizeMin)
		{
			player.PartySize = Mathf.Max(player.PartySizeMin, player.PartySize - 1);
			player.Morale = Mathf.Clamp(player.Morale - 1, 0, 100);
		}

		player.QueueRedraw();
	}

	private void TryResolveAiBattles()
	{
		if (_timeScaleIndex == 0)
			return; // paused world (encounter/town)

		// Collect AIs (skip queued-for-deletion) so we don't try to resolve battles with already-dying parties.
		var ais = new List<RandomAI>();
		foreach (var child in GetChildren())
		{
			if (child is RandomAI ai && !ai.IsQueuedForDeletion())
				ais.Add(ai);
		}

		// Pairwise overlap check. Counts are small in Phase 2/3, so O(N^2) is fine.
		for (var i = 0; i < ais.Count; i++)
		{
			for (var j = i + 1; j < ais.Count; j++)
			{
				var a = ais[i];
				var b = ais[j];
				if (a == null || b == null) continue;
				if (a.IsQueuedForDeletion() || b.IsQueuedForDeletion()) continue;

				var dist = a.GlobalPosition.DistanceTo(b.GlobalPosition);
				var threshold = a.RadiusPx + b.RadiusPx;
				if (dist > threshold)
					continue;

				ResolveAiBattle(a, b);
			}
		}
	}

	private void ResolveAiBattle(RandomAI a, RandomAI b)
	{
		// Phase 4 Step 4.3: outnumbered penalty before battle starts, then combat power includes morale.
		if (a.PartySize < b.PartySize)
			a.Morale = Mathf.Clamp(a.Morale - MoraleOutnumberedPenalty, 0, 100);
		else if (b.PartySize < a.PartySize)
			b.Morale = Mathf.Clamp(b.Morale - MoraleOutnumberedPenalty, 0, 100);

		var aPower = ApplyCombatPower(a.PartySize, a.Morale);
		var bPower = ApplyCombatPower(b.PartySize, b.Morale);

		RandomAI winner;
		RandomAI loser;

		if (aPower > bPower)
		{
			winner = a;
			loser = b;
		}
		else if (bPower > aPower)
		{
			winner = b;
			loser = a;
		}
		else
		{
			// Tie-breakers:
			// 1) higher strength proxy (StrengthValue)
			// 2) higher gold
			// 3) higher instance id
			if (a.StrengthValue != b.StrengthValue)
			{
				winner = a.StrengthValue > b.StrengthValue ? a : b;
				loser = winner == a ? b : a;
			}
			else if (a.Gold != b.Gold)
			{
				winner = a.Gold > b.Gold ? a : b;
				loser = winner == a ? b : a;
			}
			else if (a.GetInstanceId() > b.GetInstanceId())
			{
				winner = a;
				loser = b;
			}
			else
			{
				winner = b;
				loser = a;
			}
		}

		// Recent victory morale after the battle outcome.
		winner.Morale = Mathf.Clamp(winner.Morale + MoraleVictoryBonus, 0, 100);
		loser.Morale = Mathf.Clamp(loser.Morale - MoraleDefeatPenalty, 0, 100);

		var halfLoserTroops = loser.PartySize / 2; // spec: half the troops
		winner.PartySize = Mathf.Clamp(winner.PartySize + halfLoserTroops, winner.PartySizeMin, winner.PartySizeMax);
		winner.StrengthValue = winner.PartySize; // keep proxy consistent with absorbed troops

		winner.Gold = Mathf.Max(0, winner.Gold + loser.Gold); // all gold
		winner.QueueRedraw(); // debug label must update immediately
		// loser.QueueRedraw() is unnecessary because loser will be freed.
		// Winner rests after battle so it doesn't immediately re-engage.
		var restTicks = Mathf.Max(1, (int)(TicksPerSecond * 2.5f));
		winner.StartRestTicks(restTicks);
		loser.QueueFree();
		GD.Print($"[AI Battle] {winner.Name} won {loser.Name} (gain {halfLoserTroops} troops, +{loser.Gold} gold).");
	}

	private void TryEnterTown()
	{
		// Phase 4 Step 4.1: entering a town opens a simple recruit UI.
		if (_activeTown != null)
			return;
		if (_townCooldownTicksLeft > 0)
			return;
		if (_timeScaleIndex == 0)
			return;

		var player = GetNodeOrNull<PlayerParty>("Player");
		if (player == null)
			return;

		foreach (var child in GetChildren())
		{
			if (child is not Town town)
				continue;

			// Treat town as a square with half-size; use a circle approximation for overlap test (good enough for stub).
			var threshold = player.RadiusPx + town.HalfSizePx;
			if (player.GlobalPosition.DistanceTo(town.GlobalPosition) <= threshold)
			{
				_activeTown = town;
				_timeScaleIndex = 0;
				UpdateTownModalText();
				_townModal?.ShowTown(town.TownName, _cachedTownBody ?? "");
				return;
			}
		}
	}

	private string? _cachedTownBody;

	private void UpdateTownModalText()
	{
		var player = GetNodeOrNull<PlayerParty>("Player");
		if (player == null || _activeTown == null)
			return;

		_cachedTownBody =
			$"Gold: {player.Gold}\n" +
			$"Party: {player.PartySize}\n\n" +
			$"Recruit +{RecruitTroopsAmount} troops for {RecruitCostGold} gold.";
	}

	private void TryRecruitFromTown()
	{
		var player = GetNodeOrNull<PlayerParty>("Player");
		if (player == null || _activeTown == null)
			return;

		if (player.Gold < RecruitCostGold)
		{
			GD.Print("[Town] Not enough gold to recruit.");
			UpdateTownModalText();
			_townModal?.ShowTown(_activeTown.TownName, _cachedTownBody ?? "");
			return;
		}

		player.Gold = Mathf.Max(0, player.Gold - RecruitCostGold);
		player.PartySize = Mathf.Clamp(player.PartySize + RecruitTroopsAmount, player.PartySizeMin, player.PartySizeMax);
		player.QueueRedraw(); // debug label must update immediately
		GD.Print($"[Town] Recruited +{RecruitTroopsAmount} for {RecruitCostGold} gold.");
		UpdateTownModalText();
		_townModal?.ShowTown(_activeTown.TownName, _cachedTownBody ?? "");
	}

	private void LeaveTown()
	{
		if (_activeTown == null)
		{
			_townModal?.HideTown();
			_timeScaleIndex = 1;
			return;
		}

		var player = GetNodeOrNull<PlayerParty>("Player");
		var bounds = GetNodeOrNull<WorldBounds2D>(WorldBoundsPath);
		if (player != null && bounds != null)
		{
			// Nudge player away so we don't instantly re-enter.
			var dir = (player.GlobalPosition - _activeTown.GlobalPosition);
			if (dir.LengthSquared() < 0.001f)
				dir = Vector2.Right;
			dir = dir.Normalized();
			player.GlobalPosition = bounds.ClampPointToInnerRect(player.GlobalPosition + dir * (player.RadiusPx + _activeTown.HalfSizePx + 10f));
		}
		player?.QueueRedraw();

		_townModal?.HideTown();
		_timeScaleIndex = 1;
		_activeTown = null;
		_townCooldownTicksLeft = Mathf.Max(1, TicksPerSecond / 2);
	}

	private enum EncounterChoice
	{
		Fight,
		AutoResolve,
		Flee
	}

	private void TryStartEncounter()
	{
		// Phase 3 Step 3.1: if colliders overlap → pause world → show modal.
		if (_encounterAi != null)
			return;
		if (_encounterCooldownTicksLeft > 0)
			return;
		if (_timeScaleIndex == 0)
			return;

		var player = GetNodeOrNull<PlayerParty>("Player");
		if (player == null)
			return;

		foreach (var child in GetChildren())
		{
			if (child is not RandomAI ai)
				continue;
			if (ai.IsQueuedForDeletion())
				continue;

			var dist = player.GlobalPosition.DistanceTo(ai.GlobalPosition);
			var threshold = player.RadiusPx + ai.RadiusPx;
			if (dist <= threshold)
			{
				StartEncounter(player, ai);
				return;
			}
		}
	}

	private void StartEncounter(PlayerParty player, RandomAI ai)
	{
		_encounterAi = ai;
		_timeScaleIndex = 0;

		var body = $"You ran into {ai.Name}.\n\nYour party: {player.PartySize}\nEnemy party: {ai.PartySize}";
		_encounterModal?.ShowEncounter("Encounter", body);
	}

	private void ResolveEncounter(EncounterChoice choice)
	{
		var player = GetNodeOrNull<PlayerParty>("Player");
		if (player == null || _encounterAi == null)
		{
			_encounterModal?.HideEncounter();
			_timeScaleIndex = 1;
			_encounterAi = null;
			return;
		}

		var ai = _encounterAi;

		switch (choice)
		{
			case EncounterChoice.Fight:
			case EncounterChoice.AutoResolve:
				AutoResolve(player, ai);
				break;

			case EncounterChoice.Flee:
				if (TryFlee(player, ai))
				{
					GD.Print("[Encounter] Player fled successfully.");
					PushApart(player, ai);
				}
				else
				{
					GD.Print("[Encounter] Flee failed; auto-resolving.");
					AutoResolve(player, ai);
				}
				break;
		}

		_encounterModal?.HideEncounter();
		_timeScaleIndex = 1;
		_encounterAi = null;
		_encounterCooldownTicksLeft = Mathf.Max(1, TicksPerSecond / 2);
	}

	private void AutoResolve(PlayerParty player, RandomAI ai)
	{
		// Phase 3 Step 3.2: Auto-resolve v1 (placeholder values allowed).
		// Phase 4 Step 4.3: power includes morale, plus outnumbered penalty before battle.
		if (player.PartySize < ai.PartySize)
			player.Morale = Mathf.Clamp(player.Morale - MoraleOutnumberedPenalty, 0, 100);
		else if (ai.PartySize < player.PartySize)
			ai.Morale = Mathf.Clamp(ai.Morale - MoraleOutnumberedPenalty, 0, 100);

		// Power includes morale (morale factor) and a ±20% random modifier.
		var pPower = ApplyCombatPower(player.PartySize, player.Morale);
		var aPower = ApplyCombatPower(ai.PartySize, ai.Morale);

		var playerWins = pPower >= aPower;
		if (playerWins)
		{
			ai.PartySize = Mathf.Max(ai.PartySizeMin, ai.PartySize - 1);
			player.PartySize = Mathf.Max(player.PartySizeMin, player.PartySize); // winner keeps size (v1)
			// Recent victory morale.
			player.Morale = Mathf.Clamp(player.Morale + MoraleVictoryBonus, 0, 100);
			player.Gold = Mathf.Max(0, player.Gold + 3);
			// Recent defeat morale.
			ai.Morale = Mathf.Clamp(ai.Morale - MoraleDefeatPenalty, 0, 100);
			ai.Gold = Mathf.Max(0, ai.Gold - 2);
			player.QueueRedraw();
			ai.QueueRedraw();
			GD.Print($"[Encounter] Player wins. AI now {ai.PartySize}.");
		}
		else
		{
			player.PartySize = Mathf.Max(player.PartySizeMin, player.PartySize - 1);
			ai.PartySize = Mathf.Max(ai.PartySizeMin, ai.PartySize);
			player.Morale = Mathf.Clamp(player.Morale - MoraleDefeatPenalty, 0, 100);
			player.Gold = Mathf.Max(0, player.Gold - 2);
			ai.Morale = Mathf.Clamp(ai.Morale + MoraleVictoryBonus, 0, 100);
			ai.Gold = Mathf.Max(0, ai.Gold + 3);
			player.QueueRedraw();
			ai.QueueRedraw();
			GD.Print($"[Encounter] Player loses. Player now {player.PartySize}.");
		}

		if (ai.PartySize <= 0)
		{
			ai.QueueFree();
			GD.Print("[Encounter] AI defeated and removed.");
		}
		else
		{
			PushApart(player, ai);
		}
	}

	private float ApplyCombatPower(int partySize, int morale)
	{
		// Combat power = troops * random(±20%) * moraleFactor.
		var basePower = Mathf.Max(0, partySize);
		var mod = Rng.RandfRange(0.8f, 1.2f);

		var moraleClamped = Mathf.Clamp(morale, 0, 100);
		var moraleFactor = MoraleCombatPowerFactorBase + (moraleClamped / 100f) * MoraleCombatPowerFactorScale;
		return basePower * mod * moraleFactor;
	}

	private bool TryFlee(PlayerParty player, RandomAI ai)
	{
		// Phase 4 Step 4.3: flee odds influenced by morale difference.
		var speedRatio = player.CurrentSpeedPxPerSec / Mathf.Max(0.01f, ai.CurrentSpeedPxPerSec);

		var baseChance = speedRatio >= 1f ? 0.75f : 0.5f;
		var moraleDiff = player.Morale - ai.Morale; // [-100..100]
		var moraleBonus = moraleDiff / 200f; // [-0.5..0.5]

		var fleeChance = Mathf.Clamp(baseChance + moraleBonus, 0f, 1f);
		return Rng.Randf() < fleeChance;
	}

	private void PushApart(PlayerParty player, RandomAI ai)
	{
		var bounds = GetNodeOrNull<WorldBounds2D>(WorldBoundsPath);
		if (bounds == null)
			return;

		var dir = (ai.GlobalPosition - player.GlobalPosition);
		if (dir.LengthSquared() < 0.001f)
			dir = Vector2.Right;
		dir = dir.Normalized();

		var separation = (player.RadiusPx + ai.RadiusPx) + 12f;
		ai.GlobalPosition = bounds.ClampPointToInnerRect(ai.GlobalPosition + dir * separation);
		player.GlobalPosition = bounds.ClampPointToInnerRect(player.GlobalPosition - dir * separation);
		// Positions changed too; redraw immediately so debug labels don't look stale.
		player.QueueRedraw();
		ai.QueueRedraw();
	}

	private void SpawnAiParties()
	{
		if (AiCount <= 0)
			return;

		var bounds = GetNodeOrNull<WorldBounds2D>(WorldBoundsPath);
		if (bounds == null)
			return;

		for (var i = 0; i < AiCount; i++)
		{
			var ai = new RandomAI
			{
				Name = $"AI_{i + 1}",
				WorldBoundsPath = WorldBoundsPathFromParty,
			};
			ai.SetTicksPerSecond(TicksPerSecond);

			var p = GetRandomPoint(bounds.InnerRect);
			ai.GlobalPosition = p;
			AddChild(ai);
		}
	}

	private void SpawnTowns()
	{
		if (TownCount <= 0)
			return;

		var bounds = GetNodeOrNull<WorldBounds2D>(WorldBoundsPath);
		if (bounds == null)
			return;

		// Place towns near corners of the inner rect so they're always in-bounds and stable.
		var inner = bounds.InnerRect;
		var margin = 60f;
		var positions = new[]
		{
			inner.Position + new Vector2(margin, margin),
			new Vector2(inner.End.X - margin, inner.Position.Y + margin),
			new Vector2(inner.Position.X + margin, inner.End.Y - margin),
			inner.End - new Vector2(margin, margin),
		};

		for (var i = 0; i < TownCount; i++)
		{
			var t = new Town
			{
				Name = $"Town_{i + 1}",
				TownName = $"Town {i + 1}",
			};
			t.GlobalPosition = positions[i % positions.Length];
			AddChild(t);
		}
	}

	private static Vector2 GetRandomPoint(Rect2 rect)
	{
		return new Vector2(
			Rng.RandfRange(rect.Position.X, rect.End.X),
			Rng.RandfRange(rect.Position.Y, rect.End.Y)
		);
	}

	public override void _Draw()
	{
		// Debug overlay is drawn in screen-space via a CanvasLayer (see Main.tscn).
	}

	private static string BuildControlsText()
	{
		var pause = DescribeAction(InputActions.SimTogglePause);
		var slower = DescribeAction(InputActions.SimTimeSlower);
		var faster = DescribeAction(InputActions.SimTimeFaster);
		var move = DescribeAction(InputActions.PlayerSetMoveTarget);
		var sizeUp = DescribeAction(InputActions.PartySizeIncrease);
		var sizeDown = DescribeAction(InputActions.PartySizeDecrease);

		return $"Pause: {pause}  |  Slower/Faster: {slower} / {faster}  |  Move: {move} (drag)  |  Size: {sizeDown}/{sizeUp}";
	}

	private static string DescribeAction(string action)
	{
		var events = InputMap.ActionGetEvents(action);
		if (events == null || events.Count == 0)
			return "(unbound)";

		var parts = new List<string>(events.Count);
		foreach (var ev in events)
			parts.Add(DescribeEvent(ev));

		return string.Join(" / ", parts);
	}

	private static string DescribeEvent(InputEvent ev)
	{
		if (ev is InputEventKey k)
		{
			// Godot provides a good user-facing string for key events (includes modifiers).
			return k.AsText();
		}

		if (ev is InputEventMouseButton mb)
		{
			return mb.ButtonIndex switch
			{
				MouseButton.Left => "LMB",
				MouseButton.Right => "RMB",
				MouseButton.Middle => "MMB",
				MouseButton.WheelUp => "WheelUp",
				MouseButton.WheelDown => "WheelDown",
				_ => $"Mouse{(int)mb.ButtonIndex}",
			};
		}

		// Future: joypad buttons/axes, etc.
		return ev.AsText();
	}
}
