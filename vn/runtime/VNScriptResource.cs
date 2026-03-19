using Godot;

namespace VN;

// Data container expected to be produced by the (future) VN compiler.
public partial class VNScriptResource : Resource
{
	[Export] public string ScriptId { get; set; } = "";
	[Export] public string StartId { get; set; } = "";

	// command_id -> VNCommandResource
	[Export] public Godot.Collections.Dictionary Commands { get; set; } = new();
}

