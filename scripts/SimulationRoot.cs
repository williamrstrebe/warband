using Godot;
using Warband.Core;

public partial class SimulationRoot : Node2D
{
	[Export] public int TicksPerSecond { get; set; } = 60;
	[Export] public int MaxTicksPerFrame { get; set; } = 120;

	[Export] public Vector2 BoundsCenter { get; set; } = Vector2.Zero;
	[Export] public Vector2 BoundsSize { get; set; } = new(1200, 800);

	[Export] public Color BoundsColor { get; set; } = new(0.2f, 0.8f, 1.0f, 1.0f);
	[Export] public float BoundsLineWidth { get; set; } = 3f;

	private FixedTickClock? _clock;
	private int _timeScaleIndex = 1; // 1x

	public override void _Ready()
	{
		_clock = new FixedTickClock(TicksPerSecond);
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
			OnTick();

		// Keep the overlay responsive; bounds are static but the label changes.
		QueueRedraw();
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

	private void OnTick()
	{
		// Phase 0: no world state yet; tick count/time are shown via overlay.
	}

	public override void _Draw()
	{
		var rect = new Rect2(BoundsCenter - (BoundsSize / 2f), BoundsSize);
		DrawRect(rect, BoundsColor, filled: false, width: BoundsLineWidth);

		if (_clock != null)
		{
			var font = ThemeDB.FallbackFont;
			var fontSize = ThemeDB.FallbackFontSize;

			var text = $"Ticks: {_clock.TotalTicks}  |  SimTime: {_clock.SimTimeSeconds:0.000}s  |  Scale: {TimeScale.FromIndex(_timeScaleIndex)}x";
			DrawString(font, new Vector2(16, 28), text, HorizontalAlignment.Left, -1, fontSize, new(1, 1, 1, 1));

			var hint = "Pause: sim_toggle_pause  |  Slower/Faster: sim_time_slower / sim_time_faster";
			DrawString(font, new Vector2(16, 52), hint, HorizontalAlignment.Left, -1, fontSize, new(1, 1, 1, 0.85f));
		}
	}
}
