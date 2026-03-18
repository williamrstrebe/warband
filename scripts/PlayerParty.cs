using Godot;

public partial class PlayerParty : PartyBase
{
	private bool _draggingTarget;

	protected override void AfterBaseReady()
	{
		
		this.Gold = 10;
		// Player defaults (can still be overridden in the editor).
		if (Color == new Color(1, 1, 1, 1))
			Color = new Color(1.0f, 0.85f, 0.2f, 1.0f);
		if (!DrawDetectionRadius)
			DrawDetectionRadius = true;
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (Input.IsActionJustPressed(InputActions.PlayerSetMoveTarget))
		{
			_draggingTarget = true;
			SetTarget(GetGlobalMousePosition());
			return;
		}

		if (Input.IsActionJustReleased(InputActions.PlayerSetMoveTarget))
		{
			_draggingTarget = false;
			return;
		}

		if (_draggingTarget && @event is InputEventMouseMotion)
		{
			SetTarget(GetGlobalMousePosition());
		}
	}

	public override void FixedTick(double tickDeltaSeconds)
	{
		TickMoveTowardTarget(tickDeltaSeconds, stopWhenClose: true);
	}

	public override void _Draw()
	{
		base._Draw();

		if (HasTarget)
		{
			var localTarget = ToLocal(Target);
			DrawCircle(localTarget, 4f, new Color(1, 1, 1, 0.9f));
			DrawLine(Vector2.Zero, localTarget, new Color(1, 1, 1, 0.35f), 1.5f);
		}
	}
}
