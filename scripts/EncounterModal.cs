using Godot;
using System;

public partial class EncounterModal : Control
{
	[Export] public NodePath TitleLabelPath { get; set; } = new("");
	[Export] public NodePath BodyLabelPath { get; set; } = new("");
	[Export] public NodePath FightButtonPath { get; set; } = new("");
	[Export] public NodePath AutoResolveButtonPath { get; set; } = new("");
	[Export] public NodePath FleeButtonPath { get; set; } = new("");

	private Label? _title;
	private Label? _body;
	private Button? _fight;
	private Button? _auto;
	private Button? _flee;

	public event Action? FightPressed;
	public event Action? AutoResolvePressed;
	public event Action? FleePressed;

	public override void _Ready()
	{
		_title = GetNodeOrNull<Label>(TitleLabelPath);
		_body = GetNodeOrNull<Label>(BodyLabelPath);
		_fight = GetNodeOrNull<Button>(FightButtonPath);
		_auto = GetNodeOrNull<Button>(AutoResolveButtonPath);
		_flee = GetNodeOrNull<Button>(FleeButtonPath);

		if (_fight != null) _fight.Pressed += () => FightPressed?.Invoke();
		if (_auto != null) _auto.Pressed += () => AutoResolvePressed?.Invoke();
		if (_flee != null) _flee.Pressed += () => FleePressed?.Invoke();

		Hide();
	}

	public void ShowEncounter(string title, string body)
	{
		_title?.SetText(title);
		_body?.SetText(body);
		Show();
		GrabFocus();
	}

	public void HideEncounter()
	{
		Hide();
	}
}
