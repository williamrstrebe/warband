using Godot;

public partial class WorldCameraFit2D : Node
{
	[Export] public NodePath CameraPath { get; set; } = new("");
	[Export] public NodePath WorldBoundsPath { get; set; } = new("");
	[Export] public NodePath PlayerPath { get; set; } = new("");

	// Legacy export kept so the existing scene doesn't break (no-op now).
	[Export(PropertyHint.Range, "0,512,1")]
	public float VisualPaddingPx { get; set; } = 48f;

	// In Godot, Camera2D.Zoom > 1 zooms IN, < 1 zooms OUT.
	[Export(PropertyHint.Range, "0.1,4,0.01")]
	public float DefaultZoom { get; set; } = 1f;

	private Camera2D? _camera;
	private WorldBounds2D? _bounds;
	private Node2D? _player;

	public override void _Ready()
	{
		_camera = GetNodeOrNull<Camera2D>(CameraPath);
		_bounds = GetNodeOrNull<WorldBounds2D>(WorldBoundsPath);
		_player = GetNodeOrNull<Node2D>(PlayerPath);

		if (_camera != null)
			_camera.Zoom = new Vector2(DefaultZoom, DefaultZoom);

		UpdateCamera();
	}

	public override void _Process(double delta)
	{
		UpdateCamera();
	}

	private void UpdateCamera()
	{
		if (_camera == null) return;

		// Minimal follow:
		// - Always center on the player every frame.
		// - No viewport/world clamp logic here; bounds clamping of entities handles map edges.
		Vector2 target =
			_player != null
				? _player.GlobalPosition
				: (_bounds != null ? _bounds.OuterRect.GetCenter() : _camera.GlobalPosition);

		_camera.GlobalPosition = target;
		_camera.MakeCurrent();
	}
}
