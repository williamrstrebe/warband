using Godot;

namespace VN;

public partial class VNCommandResource : Resource
{
	[Export] public string Id { get; set; } = "";
	[Export] public string Type { get; set; } = "";

	// cmd.next (for most command types)
	[Export] public string Next { get; set; } = "";

	// cmd.branches (command-specific string branches)
	[Export] public Godot.Collections.Dictionary Branches { get; set; } = new();

	// cmd.data (command payload, arbitrary variants/dicts/arrays)
	[Export] public Godot.Collections.Dictionary Data { get; set; } = new();
}

