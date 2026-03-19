using Godot;
using System;
using System.Collections.Generic;
using System.IO;

namespace VN;

/// <summary>
/// Godot-side storage compiler runner.
/// Scans res://vn/scripts_json/*.json and writes res://vn/compiled/{script_id}.res
/// </summary>
public partial class VNStorageCompiler : Node
{
	[Export] public bool CompileOnReady { get; set; } = false;

	public override void _Ready()
	{
		if (CompileOnReady)
			CompileAll();
	}

	public void CompileAll()
	{
		var scriptsJsonFsPath = ProjectSettings.GlobalizePath("res://vn/scripts_json");
		var compiledResDir = "res://vn/compiled";
		var compiledFsPath = ProjectSettings.GlobalizePath(compiledResDir);

		if (!Directory.Exists(scriptsJsonFsPath))
		{
			GD.PushError($"[VNStorageCompiler] Missing scripts dir: {scriptsJsonFsPath}");
			return;
		}

		Directory.CreateDirectory(compiledFsPath);

		var inputs = new List<VNStorageCompilerCore.VNJsonInput>();
		foreach (var file in Directory.GetFiles(scriptsJsonFsPath, "*.json", SearchOption.TopDirectoryOnly))
		{
			var fileName = Path.GetFileName(file);
			var fileResPath = $"res://vn/scripts_json/{fileName}";
			var jsonText = File.ReadAllText(file);
			inputs.Add(new VNStorageCompilerCore.VNJsonInput(fileResPath, jsonText));
		}

		if (inputs.Count == 0)
		{
			GD.Print("[VNStorageCompiler] No JSON scripts found.");
			return;
		}

		var compileResult = VNStorageCompilerCore.CompileAll(inputs);
		foreach (var w in compileResult.Report.Warnings)
			GD.Print($"[VNStorageCompiler][warn] {w}");

		if (!compileResult.Report.Success)
		{
			foreach (var e in compileResult.Report.Errors)
				GD.PushError($"[VNStorageCompiler][error] {e}");
			return;
		}

		foreach (var kvp in compileResult.Scripts)
		{
			var scriptId = kvp.Key;
			var resource = kvp.Value;
			var resPath = $"{compiledResDir}/{scriptId}.res";

			var err = ResourceSaver.Save(resource, resPath);
			if (err != Error.Ok)
				GD.PushError($"[VNStorageCompiler] Failed saving {resPath}: {err}");
		}

		GD.Print($"[VNStorageCompiler] Compiled {compileResult.Scripts.Count} VN scripts.");
	}
}

