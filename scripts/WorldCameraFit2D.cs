using Godot;

public partial class WorldCameraFit2D : Node
{
    [Export] public NodePath CameraPath { get; set; } = new("");
    [Export] public NodePath WorldBoundsPath { get; set; } = new("");

    // Screen-space padding so the map rectangle doesn't touch viewport edges.
    [Export(PropertyHint.Range, "0,512,1")]
    public float VisualPaddingPx { get; set; } = 48f;

    private Camera2D? _camera;
    private WorldBounds2D? _bounds;
    private Vector2 _lastViewportSize;

    public override void _Ready()
    {
        _camera = GetNodeOrNull<Camera2D>(CameraPath);
        _bounds = GetNodeOrNull<WorldBounds2D>(WorldBoundsPath);

        _lastViewportSize = GetViewport().GetVisibleRect().Size;
        Fit();
    }

    public override void _Process(double delta)
    {
        var size = GetViewport().GetVisibleRect().Size;
        if (size == _lastViewportSize) return;
        _lastViewportSize = size;
        Fit();
    }

    private void Fit()
    {
        if (_camera == null || _bounds == null) return;

        var rect = _bounds.OuterRect;
        _camera.Position = rect.GetCenter();

        var viewport = GetViewport();
        var viewportSize = viewport.GetVisibleRect().Size;
        var available = viewportSize - new Vector2(VisualPaddingPx * 2f, VisualPaddingPx * 2f);
        available.X = Mathf.Max(1f, available.X);
        available.Y = Mathf.Max(1f, available.Y);

        // Keep the full rect visible. We choose a zoom that never zooms in beyond 1x;
        // it will zoom out (zoom < 1) if the map doesn't fit.
        var scaleX = available.X / rect.Size.X;
        var scaleY = available.Y / rect.Size.Y;
        var fitScale = Mathf.Min(scaleX, scaleY);
        // In Godot, Camera2D.Zoom < 1 zooms OUT, > 1 zooms IN.
        // If the map doesn't fit (fitScale < 1), zoom out to fit. Otherwise keep 1x.
        var zoomFactor = Mathf.Min(1f, fitScale);
        _camera.Zoom = new Vector2(zoomFactor, zoomFactor);
        _camera.MakeCurrent();
    }
}

