using Godot;

public partial class WorldBounds2D : Node2D
{
	[Export] public Vector2 Center { get; set; } = Vector2.Zero;
	[Export] public Vector2 Size { get; set; } = new(1200, 800);

	// Gameplay inset used for clamping entity centers.
	[Export(PropertyHint.Range, "0,512,1")]
	public float ClampInsetPx { get; set; } = 16f;

	[Export] public bool DrawGrid { get; set; } = false;
	[Export(PropertyHint.Range, "8,512,1")]
	public float GridCellSizePx { get; set; } = 64f;

	[Export] public Color BoundsColor { get; set; } = new(0.2f, 0.8f, 1.0f, 1.0f);
	[Export(PropertyHint.Range, "1,16,0.5")]
	public float BoundsLineWidth { get; set; } = 3f;

	[Export] public Color GridColor { get; set; } = new(0.2f, 0.8f, 1.0f, 0.25f);
	[Export(PropertyHint.Range, "1,8,0.5")]
	public float GridLineWidth { get; set; } = 1f;

	public Rect2 OuterRect => new(Center - (Size / 2f), Size);
	public Rect2 InnerRect => OuterRect.Grow(-ClampInsetPx);

	public Vector2 ClampPointToInnerRect(Vector2 point) =>
		new(
			Mathf.Clamp(point.X, InnerRect.Position.X, InnerRect.End.X),
			Mathf.Clamp(point.Y, InnerRect.Position.Y, InnerRect.End.Y));

	public override void _Ready()
	{
		QueueRedraw();
	}

	public override void _Draw()
	{
		var outer = OuterRect;
		DrawRect(outer, BoundsColor, filled: false, width: BoundsLineWidth);

		if (!DrawGrid) return;

		var x0 = outer.Position.X;
		var x1 = outer.End.X;
		var y0 = outer.Position.Y;
		var y1 = outer.End.Y;

		for (var x = x0; x <= x1; x += GridCellSizePx)
			DrawLine(new Vector2(x, y0), new Vector2(x, y1), GridColor, GridLineWidth);

		for (var y = y0; y <= y1; y += GridCellSizePx)
			DrawLine(new Vector2(x0, y), new Vector2(x1, y), GridColor, GridLineWidth);
	}
}
