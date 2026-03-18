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

	// Phase 2 Step 2.1: spawn N roaming AI parties.
	[Export(PropertyHint.Range, "0,500,1")]
	public int AiCount { get; set; } = 0;

	// From an AI party node (child of SimulationRoot), the WorldBounds node in Main.tscn is at ../../WorldBounds.
	// If the scene structure changes, update this in the editor.
	[Export] public NodePath WorldBoundsPathFromParty { get; set; } = new("../../WorldBounds");

	private static readonly RandomNumberGenerator Rng = new();

	private EncounterModal? _encounterModal;
	private RandomAI? _encounterAi;
	private int _encounterCooldownTicksLeft;

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
		_cachedControlsText = BuildControlsText();
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
			var partyText = player == null ? "" : $"  |  Party: {player.PartySize}  |  Speed: {player.CurrentSpeedPxPerSec:0.##} px/s";
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

		TryStartEncounter();
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
			GD.Print($"[Encounter] Player wins. AI now {ai.PartySize}.");
		}
		else
		{
			player.PartySize = Mathf.Max(player.PartySizeMin, player.PartySize - 1);
			ai.PartySize = Mathf.Max(ai.PartySizeMin, ai.PartySize);
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
