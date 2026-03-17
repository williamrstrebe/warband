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

	public override void _Ready()
	{
		_clock = new FixedTickClock(TicksPerSecond);
		_debugLabel = GetNodeOrNull<Label>(DebugLabelPath);
		_debugHintLabel = GetNodeOrNull<Label>(DebugHintLabelPath);
		_cachedControlsText = BuildControlsText();
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
