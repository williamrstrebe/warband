using Godot;

public partial class WorldCameraFit2D : Node
{
	[Export] public NodePath CameraPath { get; set; } = new("");
	[Export] public NodePath WorldBoundsPath { get; set; } = new("");
	[Export] public NodePath PlayerPath { get; set; } = new("");

	// Screen-space padding so the map rectangle doesn't touch viewport edges.
	[Export(PropertyHint.Range, "0,512,1")]
	public float VisualPaddingPx { get; set; } = 48f;

	private Camera2D? _camera;
	private WorldBounds2D? _bounds;
	private Vector2 _lastViewportSize;
	private Node2D? _player;

	// Keep a consistent world->screen scale and just move the camera.
	[Export(PropertyHint.Range, "0.1,4,0.01")]
	public float DefaultZoom { get; set; } = 1f;

	private Vector2 _halfVisibleWorld;

	public override void _Ready()
	{
		_camera = GetNodeOrNull<Camera2D>(CameraPath);
		_bounds = GetNodeOrNull<WorldBounds2D>(WorldBoundsPath);
		_player = GetNodeOrNull<Node2D>(PlayerPath);

		_lastViewportSize = GetViewport().GetVisibleRect().Size;
		if (_camera != null)
			_camera.Zoom = new Vector2(DefaultZoom, DefaultZoom);

		UpdateHalfVisibleWorld();
		FollowPlayer();
	}

	public override void _Process(double delta)
	{
		/**var size = GetViewport().GetVisibleRect().Size;
		if (size != _lastViewportSize)
		{
			_lastViewportSize = size;
			UpdateHalfVisibleWorld();
		}**/

		
	}
	
	public override void _PhysicsProcess(double delta){
		// Follow continuously; viewport size rarely changes but player position constantly does.
		FollowPlayer();
	}

	private void UpdateHalfVisibleWorld()
	{
		/**if (_camera == null) return;

		var viewport = GetViewport();
		var viewportSize = viewport.GetVisibleRect().Size;
		var available = viewportSize - new Vector2(VisualPaddingPx * 2f, VisualPaddingPx * 2f);
		available.X = Mathf.Max(1f, available.X);
		available.Y = Mathf.Max(1f, available.Y);

		// Convert available viewport pixels into world units using current zoom.
		var zoom = _camera.Zoom;
		_halfVisibleWorld = new Vector2(
			available.X / (2f * zoom.X),
			available.Y / (2f * zoom.Y));**/
	}

	private void FollowPlayer()
	{
		if (_camera == null || _bounds == null) return;

		var outer = _bounds.OuterRect;
		var targetPos = _player != null ? _player.GlobalPosition : outer.GetCenter();

		/**var minX = outer.Position.X + _halfVisibleWorld.X;
		var maxX = outer.End.X - _halfVisibleWorld.X;
		var minY = outer.Position.Y + _halfVisibleWorld.Y;
		var maxY = outer.End.Y - _halfVisibleWorld.Y;

		var center = outer.GetCenter();
		if (minX <= maxX)
			targetPos.X = Mathf.Clamp(targetPos.X, minX, maxX);
		else
			targetPos.X = center.X;

		if (minY <= maxY)
			targetPos.Y = Mathf.Clamp(targetPos.Y, minY, maxY);
		else
			targetPos.Y = center.Y;**/

		_camera.GlobalPosition = targetPos;
		_camera.MakeCurrent();
	}
}
