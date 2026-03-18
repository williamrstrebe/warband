using Godot;
using System;

public partial class TownModal : Control
{
	[Export] public NodePath TitleLabelPath { get; set; } = new("");
	[Export] public NodePath BodyLabelPath { get; set; } = new("");
	[Export] public NodePath RecruitButtonPath { get; set; } = new("");
	[Export] public NodePath LeaveButtonPath { get; set; } = new("");

	private Label? _title;
	private Label? _body;
	private Button? _recruit;
	private Button? _leave;

	public event Action? RecruitPressed;
	public event Action? LeavePressed;

	public override void _Ready()
	{
		_title = GetNodeOrNull<Label>(TitleLabelPath);
		_body = GetNodeOrNull<Label>(BodyLabelPath);
		_recruit = GetNodeOrNull<Button>(RecruitButtonPath);
		_leave = GetNodeOrNull<Button>(LeaveButtonPath);

		if (_recruit != null) _recruit.Pressed += () => RecruitPressed?.Invoke();
		if (_leave != null) _leave.Pressed += () => LeavePressed?.Invoke();

		Hide();
	}

	public void ShowTown(string title, string body)
	{
		_title?.SetText(title);
		_body?.SetText(body);
		Show();
		GrabFocus();
	}

	public void HideTown()
	{
		Hide();
	}
}
