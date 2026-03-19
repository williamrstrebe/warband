using Godot;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace VN;

public partial class VNRunner : Control
{
	// Host-facing signals (Godot-style C# signals).
	[Signal]
	public delegate void FinishedEventHandler();

	private Label? _dialogueLabel;
	private VBoxContainer? _choiceVBox;
	private Control? _imagesRoot;
	private Control? _videoRoot;

	private AudioStreamPlayer? _voicePlayer;
	private AudioStreamPlayer? _bgmPlayer;
	private AudioStreamPlayer? _sfxPlayer;

	private readonly Dictionary<string, VideoStreamPlayer> _videoPlayers = new();

	private bool _running;
	private readonly Dictionary<string, VNScriptResource> _loadedScripts = new();
	private readonly Stack<string> _includeReturnStack = new();
	private readonly HashSet<string> _visitedCommands = new();
	private string? _currentId = "";

	// VN runtime state.
	private readonly Dictionary<string, Variant> _vars = new();
	private TaskCompletionSource<bool>? _advanceTcs;
	private TaskCompletionSource<int>? _choiceTcs;

	private Dictionary<string, TextureRect> _imageElements = new();
	private readonly Dictionary<string, TaskCompletionSource<bool>> _videoWaiters = new();

	public override void _Ready()
	{
		_dialogueLabel = GetNodeOrNull<Label>("DialogueLabel");
		_choiceVBox = GetNodeOrNull<VBoxContainer>("ChoiceVBox");
		_imagesRoot = GetNodeOrNull<Control>("ImagesRoot");
		_videoRoot = GetNodeOrNull<Control>("VideoRoot");

		_voicePlayer = GetNodeOrNull<AudioStreamPlayer>("VoiceAudio");
		_bgmPlayer = GetNodeOrNull<AudioStreamPlayer>("BgmAudio");
		_sfxPlayer = GetNodeOrNull<AudioStreamPlayer>("SfxAudio");

		HideChoices();
	}

	/// <summary>
	/// Starts execution from the script's configured StartId.
	/// The script is loaded from: res://vn/compiled/{scriptId}.res
	/// </summary>
	public void Start(string scriptId)
	{
		var resPath = $"res://vn/compiled/{scriptId}.res";
		var loaded = GD.Load<VNScriptResource>(resPath);
		if (loaded == null)
		{
			GD.PushError($"[VNRunner] Missing script resource: {resPath}");
			return;
		}

		Show();
		_loadedScripts.Clear();
		_loadedScripts[scriptId] = loaded;
		_includeReturnStack.Clear();
		_visitedCommands.Clear();
		_currentId = loaded.StartId;
		_vars.Clear();
		_running = true;

		_ = RunLoopAsync();
	}

	private async Task RunLoopAsync()
	{
		try
		{
			while (_running)
			{
				if (string.IsNullOrEmpty(_currentId))
				{
					if (_includeReturnStack.Count > 0)
					{
						_currentId = _includeReturnStack.Pop();
						continue;
					}
					break;
				}

				var cmd = GetCommandById(_currentId);
				if (cmd == null)
				{
					// Treat missing command as "end of script".
					_currentId = null;
					continue;
				}

				_visitedCommands.Add(_currentId);
				string? nextOverride = null;

				// Execute command; blocking commands will await user action.
				nextOverride = await ExecuteCommandAsync(cmd);

				_currentId = ResolveNext(cmd, nextOverride);

				// If a nested include ended, resume from return stack.
				if (_currentId == null && _includeReturnStack.Count > 0)
					_currentId = _includeReturnStack.Pop();
			}
		}
		finally
		{
			_running = false;
			HideChoices();
			Hide();
			EmitSignal(nameof(FinishedEventHandler));
		}
	}

	private string? ResolveNext(VNCommandResource cmd, string? nextOverride)
	{
		// Priority: override set by command execution.
		if (nextOverride != null)
			return nextOverride;

		if (!string.IsNullOrEmpty(cmd.Next))
		{
			return cmd.Next;
		}

		return null;
	}

	public sealed class VNSnapshotImageState
	{
		public Texture2D? Texture;
		public Vector2 Position;
		public int ZIndex;
		public bool Visible;
	}

	public sealed class VNSnapshot
	{
		public string? CurrentId;
		public Dictionary<string, Variant> Variables = new();
		public List<string> VisitedCommands = new();
		// Stored in Stack.ToArray() order (top-first).
		public List<string> IncludeReturnStack = new();
		public Dictionary<string, VNSnapshotImageState> Images = new();

		public AudioStream? BgmStream;
		public bool BgmPlaying;
		public float BgmVolumeDb;
	}

	public VNSnapshot SaveSnapshot()
	{
		var snapshot = new VNSnapshot
		{
			CurrentId = _currentId,
			BgmStream = _bgmPlayer?.Stream,
			BgmPlaying = _bgmPlayer?.Playing ?? false,
			BgmVolumeDb = _bgmPlayer != null ? (float)_bgmPlayer.VolumeDb : 0f
		};

		foreach (var kvp in _vars)
			snapshot.Variables[kvp.Key] = kvp.Value;
		foreach (var id in _visitedCommands)
			snapshot.VisitedCommands.Add(id);
		foreach (var id in _includeReturnStack.ToArray())
			snapshot.IncludeReturnStack.Add(id);

		foreach (var kvp in _imageElements)
		{
			var rect = kvp.Value;
			snapshot.Images[kvp.Key] = new VNSnapshotImageState
			{
				Texture = rect.Texture,
				Position = rect.Position,
				ZIndex = rect.ZIndex,
				Visible = rect.Visible
			};
		}

		return snapshot;
	}

	public void LoadSnapshot(VNSnapshot snapshot)
	{
		// This is a prototype runner; snapshot restore is only safe when we are not actively running.
		if (_running)
			GD.PushWarning("[VNRunner] LoadSnapshot called while running; behavior may be inconsistent.");

		_running = false;
		_currentId = snapshot.CurrentId;

		_vars.Clear();
		foreach (var kvp in snapshot.Variables)
			_vars[kvp.Key] = kvp.Value;

		_visitedCommands.Clear();
		foreach (var id in snapshot.VisitedCommands)
			_visitedCommands.Add(id);

		_includeReturnStack.Clear();
		// Reconstruct Stack so that Pop() returns the same top element as in SaveSnapshot.
		for (int i = snapshot.IncludeReturnStack.Count - 1; i >= 0; i--)
			_includeReturnStack.Push(snapshot.IncludeReturnStack[i]);

		// Rebuild viewport image elements.
		foreach (var rect in _imageElements.Values)
			rect.QueueFree();
		_imageElements.Clear();

		foreach (var kvp in snapshot.Images)
		{
			var elementId = kvp.Key;
			var state = kvp.Value;
			var rect = GetOrCreateImageRect(elementId);
			rect.Texture = state.Texture;
			rect.Position = state.Position;
			rect.ZIndex = state.ZIndex;
			rect.Visible = state.Visible;
		}

		// Restore BGM.
		if (_bgmPlayer != null)
		{
			_bgmPlayer.Stop();
			_bgmPlayer.Stream = snapshot.BgmStream;
			_bgmPlayer.VolumeDb = snapshot.BgmVolumeDb;
			if (snapshot.BgmPlaying && snapshot.BgmStream != null)
				_bgmPlayer.Play();
		}

		// Resume runner if we have an id.
		if (!string.IsNullOrEmpty(_currentId))
		{
			_running = true;
			_ = RunLoopAsync();
		}
	}

	private VNCommandResource? GetCommandById(string namespacedId)
	{
		var parts = namespacedId.Split('.', 2);
		if (parts.Length != 2)
			return null;

		var scriptId = parts[0];
		if (!_loadedScripts.TryGetValue(scriptId, out var scriptRes) || scriptRes == null)
		{
			var resPath = $"res://vn/compiled/{scriptId}.res";
			scriptRes = GD.Load<VNScriptResource>(resPath);
			if (scriptRes == null)
				return null;
			_loadedScripts[scriptId] = scriptRes;
		}

		if (scriptRes.Commands == null)
			return null;
		if (!scriptRes.Commands.ContainsKey(namespacedId))
			return null;

		return (VNCommandResource)scriptRes.Commands[namespacedId];
	}

	private void StopVoice()
	{
		_voicePlayer?.Stop();
	}

	private void HideImages()
	{
		// Optional helper: not used by the current minimal command set.
		foreach (var kvp in _imageElements)
			kvp.Value.Visible = false;
	}

	private async Task<string?> ExecuteCommandAsync(VNCommandResource cmd)
	{
		var type = cmd.Type;

		switch (type)
		{
			case "ShowText":
				await ExecuteShowTextAsync(cmd);
				return cmd.Next;

			case "Choice":
				return await ExecuteChoiceAsync(cmd);

			case "Include":
			{
				var targetScriptId = cmd.Data.GetValueOrDefault("target_script", "").AsString();
				var entryId = cmd.Data.GetValueOrDefault("entry_id", "").AsString();
				// Include's cmd.next is the return command id.
				if (!string.IsNullOrEmpty(cmd.Next))
					_includeReturnStack.Push(cmd.Next);

				// Runtime uses namespaced id: targetScript.entryId
				if (string.IsNullOrEmpty(targetScriptId) || string.IsNullOrEmpty(entryId))
					return null;
				return entryId.Contains('.')
					? entryId
					: $"{targetScriptId}.{entryId}";
			}

			case "Jump":
				return cmd.Branches.GetValueOrDefault("target", "").AsString();

			case "ConditionalJump":
			{
				var cond = cmd.Data.GetValueOrDefault("condition", "").AsString();
				var isTrue = EvaluateCondition(cond);
				return isTrue ? cmd.Branches.GetValueOrDefault("true", "").AsString() : cmd.Branches.GetValueOrDefault("false", "").AsString();
			}

			case "SetVariable":
				ExecuteSetVariable(cmd);
				return cmd.Next;

			case "MoveImage":
				ExecuteMoveImage(cmd);
				return cmd.Next;

			case "ClearImage":
				ExecuteClearImage(cmd);
				return cmd.Next;

			case "ClearVideo":
				ExecuteClearVideo(cmd);
				return cmd.Next;

			case "ShowImage":
				ExecuteShowImage(cmd);
				return cmd.Next;

			case "PlayVoice":
				ExecutePlayVoice(cmd);
				return cmd.Next;

			case "PlayBGM":
				ExecutePlayBgm(cmd);
				return cmd.Next;

			case "PlaySFX":
				ExecutePlaySfx(cmd);
				return cmd.Next;

			case "PlayVideo":
				return await ExecutePlayVideoAsync(cmd);

			case "Delay":
				// Intentionally not implemented (per user request). Treat as immediate no-op.
				return cmd.Next;

			default:
				// Unsupported commands are treated as immediate no-ops for now.
				GD.Print($"[VNRunner] Unsupported/ignored command type: {type}");
				return cmd.Next;
		}
	}

	private async Task ExecuteShowTextAsync(VNCommandResource cmd)
	{
		if (_dialogueLabel == null)
			return;

		// Localization intentionally not implemented: these fields are treated as plain literal text.
		// "Require text": if we can't find any literal-text field, we show a handled message.
		string? text = null;
		if (cmd.Data.ContainsKey("fallback_text"))
			text = cmd.Data["fallback_text"].AsString();
		else if (cmd.Data.ContainsKey("text"))
			text = cmd.Data["text"].AsString();
		else if (cmd.Data.ContainsKey("text_key"))
			text = cmd.Data["text_key"].AsString();

		if (string.IsNullOrEmpty(text))
			text = "[VN] Text missing (ShowText).";

		_dialogueLabel.SetText(text);

		// Blocking: wait for user advance (real-time UX, no Delay support).
		_advanceTcs = new TaskCompletionSource<bool>();
		SubscribeAdvanceOnce();
		await _advanceTcs.Task;
		StopVoice();
		_advanceTcs = null;
	}

	private async Task<string?> ExecuteChoiceAsync(VNCommandResource cmd)
	{
		if (_choiceVBox == null)
			return cmd.Next;

		var options = cmd.Data.GetValueOrDefault("options", null);
		if (options == null)
			return cmd.Next;

		// Build visible options; condition filtering is supported.
		var optionArray = options as Godot.Collections.Array;
		if (optionArray == null || optionArray.Count == 0)
			return cmd.Next;

		_choiceVBox.QueueFreeChildren();
		var nextIdByPressedIndex = new List<string>();

		for (var i = 0; i < optionArray.Count; i++)
		{
			var optVariant = optionArray[i];
			if (optVariant.VariantType != Variant.Type.Dictionary)
				continue;
			var opt = optVariant.AsGodotDictionary();

			var condStr = opt.GetValueOrDefault("condition", "").AsString();
			if (!string.IsNullOrEmpty(condStr) && !EvaluateCondition(condStr))
				continue;

			// Treat localization fields as literal text (no lookup).
			var text = opt.ContainsKey("text") ? opt["text"].AsString() : opt.GetValueOrDefault("text_key", "").AsString();
			if (string.IsNullOrEmpty(text) && opt.ContainsKey("fallback_text"))
				text = opt["fallback_text"].AsString();
			if (string.IsNullOrEmpty(text))
				text = "[VN] Choice missing text.";

			var nextId = opt.GetValueOrDefault("next", "").AsString();
			if (string.IsNullOrEmpty(nextId))
				nextId = cmd.Next; // best-effort

			var button = new Button { Text = text };
			var optionIndex = nextIdByPressedIndex.Count;
			var capturedOpt = opt;
			var capturedNextId = nextId;
			button.Pressed += () => OnChoicePressed(cmd, capturedOpt, capturedNextId, optionIndex);
			_choiceVBox.AddChild(button);

			nextIdByPressedIndex.Add(nextId);
		}

		if (nextIdByPressedIndex.Count == 0)
			return cmd.Next;

		_choiceTcs = new TaskCompletionSource<int>();
		ShowChoices();

		// Wait for press, then return the command id for that option.
		var pressedIndex = await _choiceTcs.Task;
		StopVoice();

		return pressedIndex >= 0 && pressedIndex < nextIdByPressedIndex.Count
			? nextIdByPressedIndex[pressedIndex]
			: cmd.Next;
	}

	private void OnChoicePressed(VNCommandResource cmd, Godot.Collections.Dictionary opt, string nextId, int pressedIndex)
	{
		// Apply optional set mutations before jump.
			if (opt.ContainsKey("set") && opt["set"] is Variant setVal && setVal.VariantType == Variant.Type.Dictionary)
		{
				var setDict = setVal.AsGodotDictionary();
				foreach (var keyObj in setDict.Keys)
			{
				var varName = keyObj.AsString();
				var valueVar = setDict[varName];
				_vars[varName] = valueVar;
			}
		}

		HideChoices();

		_choiceTcs?.TrySetResult(pressedIndex);
	}

	private bool EvaluateCondition(string condition)
	{
		try
		{
			var parser = new VNConditionParser(condition, _vars);
			return parser.ParseExpressionToBool();
		}
		catch
		{
			// Any malformed condition is treated as false to keep flow safe.
			return false;
		}
	}

	private void ExecuteSetVariable(VNCommandResource cmd)
	{
		var name = cmd.Data.GetValueOrDefault("name", "").AsString();
		var operation = cmd.Data.GetValueOrDefault("operation", "set").AsString();
		var valueVariant = cmd.Data.GetValueOrDefault("value", new Variant());

		if (string.IsNullOrEmpty(name))
			return;

		_vars.TryGetValue(name, out var current);

		switch (operation)
		{
			case "set":
				_vars[name] = valueVariant;
				break;
			case "add":
				_vars[name] = TryNumericBinaryOp(current, valueVariant, (a, b) => a + b) ?? valueVariant;
				break;
			case "sub":
				_vars[name] = TryNumericBinaryOp(current, valueVariant, (a, b) => a - b) ?? valueVariant;
				break;
			default:
				_vars[name] = valueVariant;
				break;
		}
	}

	private Variant? TryNumericBinaryOp(Variant a, Variant b, Func<double, double, double> op)
	{
		if (a.VariantType is Variant.Type.Int or Variant.Type.Float || b.VariantType is Variant.Type.Int or Variant.Type.Float)
		{
			var aNum = a.VariantType is Variant.Type.String ? ParseDoubleOrNull(a.AsString()) ?? 0 : a.AsDouble();
			var bNum = b.VariantType is Variant.Type.String ? ParseDoubleOrNull(b.AsString()) ?? 0 : b.AsDouble();
			return op(aNum, bNum);
		}

		return null;
	}

	private double? ParseDoubleOrNull(string s)
	{
		if (string.IsNullOrWhiteSpace(s)) return null;
		if (double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v))
			return v;
		return null;
	}

	private void ExecuteShowImage(VNCommandResource cmd)
	{
		if (_imagesRoot == null)
			return;

		var elementId = cmd.Data.GetValueOrDefault("element_id", "").AsString();
		if (string.IsNullOrEmpty(elementId))
			elementId = cmd.Id;

		// Texture is ignored; we use an "image" field only.
		var imagePathRel = cmd.Data.ContainsKey("image")
			? cmd.Data["image"].AsString()
			: "";

		if (string.IsNullOrEmpty(imagePathRel))
			return;

		var texPath = imagePathRel.StartsWith("res://", StringComparison.Ordinal)
			? imagePathRel
			: $"res://vn/images/{imagePathRel}";
		var tex = GD.Load<Texture2D>(texPath);
		if (tex == null)
		{
			GD.Print($"[VNRunner] Missing image texture: {texPath}");
			return;
		}

		var texRect = GetOrCreateImageRect(elementId);
		texRect.Texture = tex;

		var x = cmd.Data.GetValueOrDefault("x_percent", 0.5).AsDouble();
		var y = cmd.Data.GetValueOrDefault("y_percent", 0.5).AsDouble();

		var vp = GetViewport();
		var vpSize = vp?.GetVisibleRect().Size ?? Vector2.Zero;
		texRect.Position = new Vector2((float)(x * vpSize.X), (float)(y * vpSize.Y));
		texRect.ZIndex = (int)cmd.Data.GetValueOrDefault("z", 0).AsDouble();
		texRect.Visible = true;
	}

	private void ExecuteMoveImage(VNCommandResource cmd)
	{
		if (_imageElements.Count == 0)
			return;
		var elementId = cmd.Data.GetValueOrDefault("element_id", "").AsString();
		if (string.IsNullOrEmpty(elementId))
			return;

		if (!_imageElements.TryGetValue(elementId, out var rect) || rect == null)
			return;

		var x = cmd.Data.GetValueOrDefault("x_percent", 0.5).AsDouble();
		var y = cmd.Data.GetValueOrDefault("y_percent", 0.5).AsDouble();
		var duration = cmd.Data.GetValueOrDefault("duration", 0).AsDouble();

		var vp = GetViewport();
		var vpSize = vp?.GetVisibleRect().Size ?? Vector2.Zero;
		var targetPos = new Vector2((float)(x * vpSize.X), (float)(y * vpSize.Y));

		rect.Visible = true;
		if (duration <= 0)
		{
			rect.Position = targetPos;
			return;
		}

		// Non-blocking tween.
		var tween = CreateTween();
		tween.TweenProperty(rect, "position", targetPos, duration);
	}

	private void ExecuteClearImage(VNCommandResource cmd)
	{
		var elementId = cmd.Data.GetValueOrDefault("element_id", "").AsString();
		if (string.IsNullOrEmpty(elementId))
			return;
		if (!_imageElements.TryGetValue(elementId, out var rect) || rect == null)
			return;

		_imageElements.Remove(elementId);
		rect.QueueFree();
	}

	private void ExecuteClearVideo(VNCommandResource cmd)
	{
		var elementId = cmd.Data.GetValueOrDefault("element_id", "").AsString();
		if (string.IsNullOrEmpty(elementId))
		{
			foreach (var p in _videoPlayers.Values)
				p.Stop();
			_videoPlayers.Clear();
			foreach (var waiter in _videoWaiters.Values)
				waiter.TrySetResult(true);
			_videoWaiters.Clear();
			return;
		}

		if (_videoPlayers.TryGetValue(elementId, out var player) && player != null)
		{
			player.Stop();
			_videoPlayers.Remove(elementId);
			if (_videoWaiters.TryGetValue(elementId, out var waiter) && waiter != null)
			{
				waiter.TrySetResult(true);
				_videoWaiters.Remove(elementId);
			}
		}
	}

	private void ExecutePlayVoice(VNCommandResource cmd)
	{
		if (_voicePlayer == null)
			return;

		var audio = cmd.Data.GetValueOrDefault("audio", "").AsString();
		if (string.IsNullOrEmpty(audio))
			return;

		var stream = GD.Load<AudioStream>(audio);
		if (stream == null)
		{
			GD.Print($"[VNRunner] Missing voice stream: {audio}");
			return;
		}

		_voicePlayer.Stream = stream;
		_voicePlayer.Play();
	}

	private void ExecutePlayBgm(VNCommandResource cmd)
	{
		if (_bgmPlayer == null)
			return;

		var audio = cmd.Data.GetValueOrDefault("audio", "").AsString();
		if (string.IsNullOrEmpty(audio))
			return;

		var stream = GD.Load<AudioStream>(audio);
		if (stream == null)
		{
			GD.Print($"[VNRunner] Missing BGM stream: {audio}");
			return;
		}

		var fade = cmd.Data.ContainsKey("fade") ? cmd.Data["fade"].AsDouble() : 0.0;
		_bgmPlayer.Stream = stream;

		if (fade > 0)
		{
			_bgmPlayer.VolumeDb = -80.0f;
			_bgmPlayer.Play();
			var tween = CreateTween();
			tween.TweenProperty(_bgmPlayer, "volume_db", 0.0f, (float)fade);
		}
		else
		{
			_bgmPlayer.VolumeDb = 0.0f;
			_bgmPlayer.Play();
		}
	}

	private void ExecutePlaySfx(VNCommandResource cmd)
	{
		if (_sfxPlayer == null)
			return;

		var audio = cmd.Data.GetValueOrDefault("audio", "").AsString();
		if (string.IsNullOrEmpty(audio))
			return;

		var stream = GD.Load<AudioStream>(audio);
		if (stream == null)
		{
			GD.Print($"[VNRunner] Missing SFX stream: {audio}");
			return;
		}

		_sfxPlayer.Stream = stream;
		_sfxPlayer.Play();
	}

	private async Task<string?> ExecutePlayVideoAsync(VNCommandResource cmd)
	{
		if (_videoRoot == null)
			return cmd.Next;

		var video = cmd.Data.GetValueOrDefault("video", "").AsString();
		if (string.IsNullOrEmpty(video))
			return cmd.Next;

		var elementId = cmd.Id;
		var waiter = new TaskCompletionSource<bool>();
		_videoWaiters[elementId] = waiter;

		if (!_videoPlayers.TryGetValue(elementId, out var player) || player == null)
		{
			player = new VideoStreamPlayer();
			_videoRoot.AddChild(player);
			_videoPlayers[elementId] = player;

			// Ensure the current waiters complete when the video finishes.
			player.Finished += () =>
			{
				if (_videoWaiters.TryGetValue(elementId, out var activeWaiter))
					activeWaiter.TrySetResult(true);
			};
		}

		var stream = GD.Load<VideoStream>(video);
		if (stream == null)
		{
			GD.Print($"[VNRunner] Missing video stream: {video}");
			_videoWaiters.Remove(elementId);
			return cmd.Next;
		}

		player.Stream = stream;
		player.Play();

		await waiter.Task;
		_videoWaiters.Remove(elementId);
		return cmd.Next;
	}

	private TextureRect GetOrCreateImageRect(string elementId)
	{
		if (_imageElements.TryGetValue(elementId, out var existing))
			return existing;

		var rect = new TextureRect
		{
			Name = elementId,
			// Keep aspect; placeholder visuals.
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			Visible = true
		};

		_imagesRoot.AddChild(rect);
		_imageElements[elementId] = rect;
		return rect;
	}

	private void ShowChoices()
	{
		if (_choiceVBox != null)
			_choiceVBox.Visible = true;
	}

	private void HideChoices()
	{
		if (_choiceVBox != null)
			_choiceVBox.Visible = false;
	}

	private void SubscribeAdvanceOnce()
	{
		// Input advance uses built-in ui_accept if present; otherwise left click.
		EnableProcessUnhandledInput(true);
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (_advanceTcs == null)
			return;

		if (@event is InputEventMouseButton mb && mb.Pressed)
		{
			if (mb.ButtonIndex == MouseButton.Left)
				_advanceTcs.TrySetResult(true);
			return;
		}

		if (@event is InputEventKey ik && ik.Pressed)
		{
			// Godot default action for confirmation.
			if (Input.IsActionJustPressed("ui_accept"))
				_advanceTcs.TrySetResult(true);
		}
	}

	private void EnableProcessUnhandledInput(bool value) => base.SetProcessUnhandledInput(value);
}

internal sealed class VNConditionParser
{
	private readonly string _s;
	private int _i;
	private readonly Dictionary<string, Variant> _vars;

	private enum TokType
	{
		End,
		Ident,
		Number,
		String,
		True,
		False,
		LParen,
		RParen,
		And,
		Or,
		Eq,
		Neq,
		Lt,
		Gt,
		Lte,
		Gte
	}

	private readonly struct Token
	{
		public TokType Type { get; }
		public string Text { get; }
		public Token(TokType type, string text)
		{
			Type = type;
			Text = text;
		}
	}

	public VNConditionParser(string condition, Dictionary<string, Variant> vars)
	{
		_s = condition ?? "";
		_i = 0;
		_vars = vars;
	}

	public bool ParseExpressionToBool()
	{
		var value = ParseOrExpression();
		return ToTruthiness(value);
	}

	private object ParseOrExpression()
	{
		var left = ParseAndExpression();
		while (true)
		{
			SkipWs();
			if (TryConsumeKeyword("OR") || TryConsumeKeyword("||"))
			{
				var right = ParseAndExpression();
				left = ToBoolValue(left) || ToBoolValue(right);
				continue;
			}

			break;
		}

		return left;
	}

	private object ParseAndExpression()
	{
		var left = ParseComparisonExpression();
		while (true)
		{
			SkipWs();
			if (TryConsumeKeyword("AND") || TryConsumeKeyword("&&"))
			{
				var right = ParseComparisonExpression();
				left = ToBoolValue(left) && ToBoolValue(right);
				continue;
			}

			break;
		}

		return left;
	}

	private object ParseComparisonExpression()
	{
		var left = ParseValueOrParenExpression();
		SkipWs();
		var op = TryConsumeComparisonOp();
		if (op == null)
			return left;

		SkipWs();
		var right = ParseValueOrParenExpression();
		return Compare(left, op.Value, right);
	}

	private object ParseValueOrParenExpression()
	{
		SkipWs();
		if (TryConsume('('))
		{
			var inner = ParseOrExpression();
			SkipWs();
			ConsumeExpected(')');
			return inner;
		}

		var tok = NextToken();
		switch (tok.Type)
		{
			case TokType.True:
				return true;
			case TokType.False:
				return false;
			case TokType.Number:
				return ParseDoubleOrThrow(tok.Text);
			case TokType.String:
				return tok.Text;
			case TokType.Ident:
				return LookupVar(tok.Text);
			default:
				throw new InvalidOperationException($"Unexpected token: {tok.Type}");
		}
	}

	private enum CmpOp { Eq, Neq, Lt, Gt, Lte, Gte }

	private CmpOp? TryConsumeComparisonOp()
	{
		SkipWs();
		if (TryConsume("==")) return CmpOp.Eq;
		if (TryConsume("!=")) return CmpOp.Neq;
		if (TryConsume(">=")) return CmpOp.Gte;
		if (TryConsume("<=")) return CmpOp.Lte;
		if (TryConsume(">")) return CmpOp.Gt;
		if (TryConsume("<")) return CmpOp.Lt;
		return null;
	}

	private object Compare(object left, CmpOp op, object right)
	{
		// Equality supports any combination via Variant truthy conversion when needed.
		if (op is CmpOp.Eq or CmpOp.Neq)
		{
			var eq = AreEqual(left, right);
			return op == CmpOp.Eq ? eq : !eq;
		}

		// Ordering requires numbers where possible.
		var lNum = TryToDouble(left, out var l) ? l : (double?)null;
		var rNum = TryToDouble(right, out var r) ? r : (double?)null;
		if (lNum == null || rNum == null)
			return false;

		return op switch
		{
			CmpOp.Gt => l > r,
			CmpOp.Gte => l >= r,
			CmpOp.Lt => l < r,
			CmpOp.Lte => l <= r,
			_ => false
		};
	}

	private bool AreEqual(object left, object right)
	{
		if (left is bool lb && right is bool rb)
			return lb == rb;
		if (TryToDouble(left, out var l) && TryToDouble(right, out var r))
			return Math.Abs(l - r) < 0.000001;

		var ls = left?.ToString() ?? "";
		var rs = right?.ToString() ?? "";
		return ls == rs;
	}

	private bool ToTruthiness(object? value)
	{
		if (value is null) return false;
		if (value is bool b) return b;
		if (value is double d) return Math.Abs(d) > 0.000001;
		if (value is string s) return !string.IsNullOrEmpty(s);
		return false;
	}

	private bool ToBoolValue(object? value) => ToTruthiness(value);

	private bool TryToDouble(object value, out double v)
	{
		switch (value)
		{
			case double d:
				v = d;
				return true;
			case bool b:
				v = b ? 1.0 : 0.0;
				return true;
			case string s:
				return double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v);
			default:
				v = 0;
				return false;
		}
	}

	private object LookupVar(string name)
	{
		if (!_vars.TryGetValue(name, out var val))
			return false;

		return val.VariantType switch
		{
			Variant.Type.Bool => val.AsBool(),
			Variant.Type.Int => val.AsDouble(),
			Variant.Type.Float => val.AsDouble(),
			Variant.Type.String => val.AsString(),
			_ => false
		};
	}

	private double ParseDoubleOrThrow(string s)
	{
		if (double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v))
			return v;
		throw new InvalidOperationException($"Invalid number literal: {s}");
	}

	private void SkipWs()
	{
		while (_i < _s.Length && char.IsWhiteSpace(_s[_i]))
			_i++;
	}

	private bool PeekKeyword(string kw)
	{
		SkipWs();
		return _s.Substring(_i).StartsWith(kw, StringComparison.Ordinal);
	}

	private bool TryConsumeKeyword(string kw)
	{
		if (!PeekKeyword(kw))
			return false;
		_i += kw.Length;
		return true;
	}

	private void ConsumeKeyword(string kw)
	{
		if (!TryConsumeKeyword(kw))
			throw new InvalidOperationException($"Expected keyword: {kw}");
	}

	private bool TryConsume(char c)
	{
		SkipWs();
		if (_i < _s.Length && _s[_i] == c)
		{
			_i++;
			return true;
		}
		return false;
	}

	private void ConsumeExpected(char c)
	{
		if (!TryConsume(c))
			throw new InvalidOperationException($"Expected '{c}'");
	}

	private bool TryConsume(string s)
	{
		SkipWs();
		if (_s.Substring(_i).StartsWith(s, StringComparison.Ordinal))
		{
			_i += s.Length;
			return true;
		}
		return false;
	}

	private Token NextToken()
	{
		SkipWs();
		if (_i >= _s.Length)
			return new Token(TokType.End, "");

		var c = _s[_i];

		if (c == '(')
		{
			_i++;
			return new Token(TokType.LParen, "(");
		}
		if (c == ')')
		{
			_i++;
			return new Token(TokType.RParen, ")");
		}

		if (c == '"')
		{
			_i++; // opening quote
			var start = _i;
			var sb = new System.Text.StringBuilder();
			while (_i < _s.Length)
			{
				var ch = _s[_i++];
				if (ch == '\\' && _i < _s.Length)
				{
					var next = _s[_i++];
					if (next == '"' || next == '\\')
						sb.Append(next);
					else
						sb.Append(next);
					continue;
				}
				if (ch == '"')
					break;
				sb.Append(ch);
			}
			return new Token(TokType.String, sb.ToString());
		}

		if (char.IsDigit(c) || c == '-' || c == '+')
		{
			var start = _i;
			_i++; // first char
			while (_i < _s.Length && (char.IsDigit(_s[_i]) || _s[_i] == '.'))
				_i++;
			return new Token(TokType.Number, _s.Substring(start, _i - start));
		}

		if (char.IsLetter(c) || c == '_')
		{
			var start = _i;
			_i++;
			while (_i < _s.Length && (char.IsLetterOrDigit(_s[_i]) || _s[_i] == '_'))
				_i++;
			var ident = _s.Substring(start, _i - start);

			if (string.Equals(ident, "true", StringComparison.OrdinalIgnoreCase))
				return new Token(TokType.True, ident);
			if (string.Equals(ident, "false", StringComparison.OrdinalIgnoreCase))
				return new Token(TokType.False, ident);
			if (string.Equals(ident, "AND", StringComparison.OrdinalIgnoreCase))
				return new Token(TokType.And, ident);
			if (string.Equals(ident, "OR", StringComparison.OrdinalIgnoreCase))
				return new Token(TokType.Or, ident);

			return new Token(TokType.Ident, ident);
		}

		// Operators will be consumed by comparison op reader; treat unknown as identifier error.
		throw new InvalidOperationException($"Unexpected character in condition: '{c}'");
	}
}

internal static class VNDictExtensions
{
	public static Variant GetValueOrDefault(this Godot.Collections.Dictionary dict, string key, Variant defaultValue)
	{
		if (dict == null) return defaultValue;
		if (!dict.ContainsKey(key)) return defaultValue;
		return (Variant)dict[key];
	}

	public static Godot.Collections.Array? GetValueOrDefault(this Godot.Collections.Dictionary dict, string key, Godot.Collections.Array? defaultValue)
	{
		if (dict == null) return defaultValue;
		if (!dict.ContainsKey(key)) return defaultValue;
		var v = (Variant)dict[key];
		return v.VariantType == Variant.Type.Array ? v.AsGodotArray() : defaultValue;
	}
}

internal static class VNNodeExtensions
{
	public static void QueueFreeChildren(this VBoxContainer vbox)
	{
		if (vbox == null) return;
		foreach (var child in vbox.GetChildren())
			child.QueueFree();
	}
}

