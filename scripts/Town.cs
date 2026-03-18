using Godot;

public partial class Town : Area2D
{
	[Export] public string TownName { get; set; } = "Town";
	[Export(PropertyHint.Range, "4,256,1")] public float HalfSizePx { get; set; } = 14f;
	[Export] public Color Color { get; set; } = new(0.25f, 0.55f, 1.0f, 1.0f);

	public override void _Ready()
	{
		EnsureCollider();
		QueueRedraw();
	}

	private void EnsureCollider()
	{
		var shape = GetNodeOrNull<CollisionShape2D>("TownShape");
		if (shape == null)
		{
			shape = new CollisionShape2D { Name = "TownShape" };
			AddChild(shape);
		}

		if (shape.Shape is not RectangleShape2D rect)
		{
			rect = new RectangleShape2D();
			shape.Shape = rect;
		}

		rect.Size = new Vector2(HalfSizePx * 2f, HalfSizePx * 2f);
	}

	public override void _Draw()
	{
		DrawRect(new Rect2(new Vector2(-HalfSizePx, -HalfSizePx), new Vector2(HalfSizePx * 2f, HalfSizePx * 2f)), Color);
	}
}
