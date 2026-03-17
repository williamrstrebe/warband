using Godot;

public partial class PlayerParty : Area2D, IFixedTick
{
    [Export(PropertyHint.Range, "1,2000,1")]
    public float MoveSpeedPxPerSec { get; set; } = 220f; // Placeholder for Step 1.2

    [Export(PropertyHint.Range, "0.5,64,0.5")]
    public float StopThresholdPx { get; set; } = 6f;

    [Export(PropertyHint.Range, "2,128,1")]
    public float RadiusPx { get; set; } = 10f;

    [Export] public Color Color { get; set; } = new(1.0f, 0.85f, 0.2f, 1.0f);

    [Export] public NodePath WorldBoundsPath { get; set; } = new("");

    private WorldBounds2D? _bounds;
    private Vector2 _target;
    private bool _hasTarget;

    public override void _Ready()
    {
        _bounds = GetNodeOrNull<WorldBounds2D>(WorldBoundsPath);
        _target = GlobalPosition;
        QueueRedraw();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!Input.IsActionJustPressed(InputActions.PlayerSetMoveTarget)) return;

        var worldMouse = GetGlobalMousePosition();
        SetTarget(worldMouse);
    }

    public void FixedTick(double tickDeltaSeconds)
    {
        if (!_hasTarget) return;

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

        var step = (float)(MoveSpeedPxPerSec * tickDeltaSeconds);
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

        if (_hasTarget)
        {
            var localTarget = ToLocal(_target);
            DrawCircle(localTarget, 4f, new Color(1, 1, 1, 0.9f));
            DrawLine(Vector2.Zero, localTarget, new Color(1, 1, 1, 0.35f), 1.5f);
        }
    }
}

