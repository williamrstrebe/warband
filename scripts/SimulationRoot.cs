using Godot;
using Warband.Core;

public partial class SimulationRoot : Node2D
{
	// README Phase 0 Step 0.1 example: 10 ticks/sec.
	[Export] public int TicksPerSecond { get; set; } = 10;
	[Export] public int MaxTicksPerFrame { get; set; } = 120;

	private FixedTickClock? _clock;
	private int _timeScaleIndex = 1; // 1x
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
			var partyText = player == null
				? ""
				: $"  |  Party: {player.PartySize}  |  Morale: {player.Morale}  |  Gold: {player.Gold}  |  Speed: {player.CurrentSpeedPxPerSec:0.##} px/s";
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
		TryStartEncounter();
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
		// For now: use PartySize as "power" with a ±20% random modifier; loser loses 1 troop.
		var pPower = ApplyModifier(player.PartySize);
		var aPower = ApplyModifier(ai.PartySize);

		var playerWins = pPower >= aPower;
		if (playerWins)
		{
			ai.PartySize = Mathf.Max(ai.PartySizeMin, ai.PartySize - 1);
			player.PartySize = Mathf.Max(player.PartySizeMin, player.PartySize); // winner keeps size (v1)
			player.Morale = Mathf.Clamp(player.Morale + 2, 0, 100);
			player.Gold = Mathf.Max(0, player.Gold + 3);
			ai.Morale = Mathf.Clamp(ai.Morale - 2, 0, 100);
			ai.Gold = Mathf.Max(0, ai.Gold - 2);
			GD.Print($"[Encounter] Player wins. AI now {ai.PartySize}.");
		}
		else
		{
			player.PartySize = Mathf.Max(player.PartySizeMin, player.PartySize - 1);
			ai.PartySize = Mathf.Max(ai.PartySizeMin, ai.PartySize);
			player.Morale = Mathf.Clamp(player.Morale - 2, 0, 100);
			player.Gold = Mathf.Max(0, player.Gold - 2);
			ai.Morale = Mathf.Clamp(ai.Morale + 2, 0, 100);
			ai.Gold = Mathf.Max(0, ai.Gold + 3);
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

	private static float ApplyModifier(int partySize)
	{
		var basePower = Mathf.Max(0, partySize);
		var mod = Rng.RandfRange(0.8f, 1.2f);
		return basePower * mod;
	}

	private static bool TryFlee(PlayerParty player, RandomAI ai)
	{
		// Very simple v1:
		// - If player is faster, guaranteed escape.
		// - Otherwise 50/50.
		if (player.CurrentSpeedPxPerSec > ai.CurrentSpeedPxPerSec)
			return true;
		return Rng.Randf() < 0.5f;
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
