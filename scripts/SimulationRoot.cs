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

	[Export] public NodePath DebugLabelPath { get; set; } = new("");
	[Export] public NodePath DebugHintLabelPath { get; set; } = new("");

	public override void _Ready()
	{
		_clock = new FixedTickClock(TicksPerSecond);
		_debugLabel = GetNodeOrNull<Label>(DebugLabelPath);
		_debugHintLabel = GetNodeOrNull<Label>(DebugHintLabelPath);
		QueueRedraw();
	}

	public override void _Process(double delta)
	{
		if (_clock == null) return;

		var produced = _clock.Advance(
			realDeltaSeconds: delta,
			timeScale: TimeScale.FromIndex(_timeScaleIndex),
			maxTicksThisAdvance: MaxTicksPerFrame);

		for (var i = 0; i < produced; i++)
			OnTick(_clock.TickDeltaSeconds);

		if (_clock != null)
		{
			_debugLabel?.SetText($"Ticks: {_clock.TotalTicks}  |  SimTime: {_clock.SimTimeSeconds:0.000}s  |  Scale: {TimeScale.FromIndex(_timeScaleIndex)}x");
			_debugHintLabel?.SetText("Pause: sim_toggle_pause  |  Slower/Faster: sim_time_slower / sim_time_faster  |  Move: click");
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
}
