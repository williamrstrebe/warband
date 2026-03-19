using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace VN;

public static class VNStorageCompilerCore
{
	public readonly record struct VNJsonInput(string FilePathRes, string JsonText);

	public sealed class VNCompileReport
	{
		public readonly List<string> Errors = new();
		public readonly List<string> Warnings = new();
		public bool Success => Errors.Count == 0;
	}

	public sealed class VNCompileResult
	{
		public readonly Dictionary<string, VNScriptResource> Scripts = new();
		public readonly VNCompileReport Report = new();
	}

	private sealed class ParsedScript
	{
		public required string ScriptId;
		public required string StartId;
		public readonly Dictionary<string, ParsedCommand> Commands = new();
	}

	private sealed class ChoiceOption
	{
		public required string TextKey;
		public required string NextId;
		public string? Condition;
		public Dictionary<string, Variant>? Set;
	}

	private sealed class ParsedCommand
	{
		public required string Id;
		public required string Type;

		// Unnamespaced references for validation.
		public string? NextId; // null means end

		// Jump / ConditionalJump
		public string? JumpTargetId;
		public string? CondTrueId;
		public string? CondFalseId;
		public string? CondCondition;

		// Choice
		public readonly List<ChoiceOption> ChoiceOptions = new();

		// Include
		public string? IncludeTargetScriptId;
		public string? IncludeEntryId;

		// Shared command payload data (unmodified except ShowImage texture->image mapping).
		public Godot.Collections.Dictionary Data = new();
		public Godot.Collections.Dictionary Branches = new();
	}

	public static VNCompileResult CompileAll(IReadOnlyList<VNJsonInput> inputs)
	{
		var result = new VNCompileResult();

		var parsedScripts = new Dictionary<string, ParsedScript>();

		// Phase 1: parse + per-script validation (contracts + unique ids).
		foreach (var input in inputs)
		{
			try
			{
				ParseAndValidateRoot(input, parsedScripts, result.Report);
			}
			catch (Exception ex)
			{
				result.Report.Errors.Add($"[{input.FilePathRes}] {ex.Message}");
			}
		}

		if (!result.Report.Success)
			return result;

		// Phase 2: cross-reference validation.
		foreach (var ps in parsedScripts.Values)
		{
			ValidateScriptReferences(ps, parsedScripts, result.Report);
		}

		if (!result.Report.Success)
			return result;

		// Phase 3: reachability analysis (per script).
		foreach (var ps in parsedScripts.Values)
		{
			ReachabilityWarn(ps, parsedScripts, result.Report);
		}

		// Phase 4: build resources.
		foreach (var ps in parsedScripts.Values)
		{
			var resource = BuildResource(ps);
			result.Scripts[ps.ScriptId] = resource;
		}

		return result;
	}

	private static void ParseAndValidateRoot(
		VNJsonInput input,
		Dictionary<string, ParsedScript> scriptsById,
		VNCompileReport report)
	{
		var doc = JsonDocument.Parse(input.JsonText);
		var root = doc.RootElement;
		if (root.ValueKind != JsonValueKind.Object)
			throw new InvalidOperationException("Root must be a JSON object.");

		static HashSet<string> Keys(JsonElement obj)
		{
			var set = new HashSet<string>(StringComparer.Ordinal);
			foreach (var p in obj.EnumerateObject())
				set.Add(p.Name);
			return set;
		}

		var allowedRootKeys = new HashSet<string>(StringComparer.Ordinal)
		{
			"script_id",
			"start",
			"commands"
		};
		foreach (var key in Keys(root))
		{
			if (!allowedRootKeys.Contains(key))
				report.Errors.Add($"Unknown root key '{key}'.");
		}

		if (!root.TryGetProperty("script_id", out var scriptIdEl) || scriptIdEl.ValueKind != JsonValueKind.String)
			report.Errors.Add("Missing/invalid required root key 'script_id'.");
		if (!root.TryGetProperty("start", out var startEl) || startEl.ValueKind != JsonValueKind.String)
			report.Errors.Add("Missing/invalid required root key 'start'.");
		if (!root.TryGetProperty("commands", out var commandsEl) || commandsEl.ValueKind != JsonValueKind.Array)
			report.Errors.Add("Missing/invalid required root key 'commands' (array).");

		if (!report.Success)
			return;

		var scriptId = scriptIdEl.GetString() ?? "";
		var startId = startEl.GetString() ?? "";
		if (string.IsNullOrEmpty(scriptId))
			report.Errors.Add("script_id must be non-empty.");
		if (string.IsNullOrEmpty(startId))
			report.Errors.Add("start must be non-empty.");

		if (scriptsById.ContainsKey(scriptId))
			report.Errors.Add($"Duplicate script_id '{scriptId}'.");

		var parsed = new ParsedScript { ScriptId = scriptId, StartId = startId };

		int cmdIndex = 0;
		foreach (var cmdEl in commandsEl.EnumerateArray())
		{
			cmdIndex++;
			if (cmdEl.ValueKind != JsonValueKind.Object)
			{
				report.Errors.Add($"Command #{cmdIndex} must be an object.");
				continue;
			}

			var allowedCmdKeys = new HashSet<string>(StringComparer.Ordinal) { "id", "type", "next", "data", "branches" };
			foreach (var p in cmdEl.EnumerateObject())
			{
				if (!allowedCmdKeys.Contains(p.Name))
					report.Errors.Add($"[{scriptId}] Unknown command key '{p.Name}' in command #{cmdIndex}.");
			}

			if (!cmdEl.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.String)
			{
				report.Errors.Add($"[{scriptId}] Missing/invalid command.id in command #{cmdIndex}.");
				continue;
			}
			if (!cmdEl.TryGetProperty("type", out var typeEl) || typeEl.ValueKind != JsonValueKind.String)
			{
				report.Errors.Add($"[{scriptId}] Missing/invalid command.type in command #{cmdIndex}.");
				continue;
			}

			var id = idEl.GetString() ?? "";
			var type = typeEl.GetString() ?? "";
			if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(type))
			{
				report.Errors.Add($"[{scriptId}] command.id and command.type must be non-empty strings (#{cmdIndex}).");
				continue;
			}

			if (parsed.Commands.ContainsKey(id))
				report.Errors.Add($"[{scriptId}] Duplicate command id '{id}'.");

			if (!cmdEl.TryGetProperty("data", out var dataEl) || dataEl.ValueKind != JsonValueKind.Object)
			{
				report.Errors.Add($"[{scriptId}] Missing/invalid command.data object for '{id}'.");
				continue;
			}
			if (!cmdEl.TryGetProperty("branches", out var branchesEl) || branchesEl.ValueKind != JsonValueKind.Object)
			{
				report.Errors.Add($"[{scriptId}] Missing/invalid command.branches object for '{id}'.");
				continue;
			}

			// next presence rule: required except Choice / Jump / ConditionalJump / Include
			var hasNext = cmdEl.TryGetProperty("next", out _);
			var nextRequired = type switch
			{
				"Choice" => false,
				"Jump" => false,
				"ConditionalJump" => false,
				"Include" => true,  // payload contract: Include uses cmd.next as return_id
				_ => true
			};

			if (nextRequired && !hasNext)
				report.Errors.Add($"[{scriptId}] Missing command.next for type '{type}' (id='{id}').");

			var cmd = new ParsedCommand { Id = id, Type = type };

			if (hasNext)
			{
				var nextEl = cmdEl.GetProperty("next");
				cmd.NextId = nextEl.ValueKind switch
				{
					JsonValueKind.String => nextEl.GetString(),
					JsonValueKind.Null => null,
					_ => throw new InvalidOperationException($"command.next must be string or null for '{id}'.")
				};
			}

			ParseCommandPayload(parsed, cmd, dataEl, branchesEl, report, scriptId);
			parsed.Commands[id] = cmd;
		}

		if (report.Success)
		{
			scriptsById[scriptId] = parsed;
		}
	}

	private static void ParseCommandPayload(
		ParsedScript parsed,
		ParsedCommand cmd,
		JsonElement dataEl,
		JsonElement branchesEl,
		VNCompileReport report,
		string scriptId)
	{
		// Validate empty branches when required by payload contracts.
		static bool IsEmptyObject(JsonElement el)
		{
			if (el.ValueKind != JsonValueKind.Object) return false;
			foreach (var _ in el.EnumerateObject())
				return false;
			return true;
		}

		static string? GetOptionalString(JsonElement obj, string key)
		{
			if (!obj.TryGetProperty(key, out var v)) return null;
			return v.ValueKind == JsonValueKind.String ? v.GetString() : null;
		}

		void RequireBranchesEmpty(string context)
		{
			if (!IsEmptyObject(branchesEl))
				report.Errors.Add($"[{scriptId}] {context}: branches must be an empty object.");
		}

		void RequireDataOnlyKeys(HashSet<string> allowedKeys, string context)
		{
			foreach (var p in dataEl.EnumerateObject())
			{
				if (!allowedKeys.Contains(p.Name))
					report.Errors.Add($"[{scriptId}] {context}: unknown data key '{p.Name}'.");
			}
		}

		// Helper: convert a JSON value to Godot Variant.
		static Variant ToVariant(JsonElement v)
		{
			return v.ValueKind switch
			{
				JsonValueKind.String => v.GetString() ?? "",
				JsonValueKind.Number => v.GetDouble(),
				JsonValueKind.True => true,
				JsonValueKind.False => false,
				JsonValueKind.Null => new Variant(),
				_ => new Variant()
			};
		}

		switch (cmd.Type)
		{
			case "ShowText":
			{
				var allowedKeys = new HashSet<string>(StringComparer.Ordinal)
				{
					"text_key",
					"fallback_text",
					"speaker",
					"portrait_id",
					"portrait_side"
				};
				RequireDataOnlyKeys(allowedKeys, $"ShowText.data (id='{cmd.Id}')");
				RequireBranchesEmpty($"ShowText (id='{cmd.Id}')");

				if (!dataEl.TryGetProperty("text_key", out var textKeyEl) || textKeyEl.ValueKind != JsonValueKind.String)
					report.Errors.Add($"[{scriptId}] ShowText requires data.text_key string (id='{cmd.Id}').");
				else
					cmd.Data["text_key"] = textKeyEl.GetString() ?? "";

				if (dataEl.TryGetProperty("fallback_text", out var fallbackEl))
				{
					if (fallbackEl.ValueKind != JsonValueKind.String)
						report.Errors.Add($"[{scriptId}] ShowText.data.fallback_text must be string (id='{cmd.Id}').");
					else
						cmd.Data["fallback_text"] = fallbackEl.GetString() ?? "";
				}

				if (dataEl.TryGetProperty("speaker", out var speakerEl))
				{
					if (speakerEl.ValueKind != JsonValueKind.String)
						report.Errors.Add($"[{scriptId}] ShowText.data.speaker must be string (id='{cmd.Id}').");
					else
						cmd.Data["speaker"] = speakerEl.GetString() ?? "";
				}

				if (dataEl.TryGetProperty("portrait_id", out var portraitIdEl))
				{
					if (portraitIdEl.ValueKind != JsonValueKind.String)
						report.Errors.Add($"[{scriptId}] ShowText.data.portrait_id must be string (id='{cmd.Id}').");
					else
						cmd.Data["portrait_id"] = portraitIdEl.GetString() ?? "";
				}

				if (dataEl.TryGetProperty("portrait_side", out var sideEl))
				{
					if (sideEl.ValueKind != JsonValueKind.String)
						report.Errors.Add($"[{scriptId}] ShowText.data.portrait_side must be string (id='{cmd.Id}').");
					else
					{
						var side = sideEl.GetString() ?? "";
						if (side != "left" && side != "right")
							report.Errors.Add($"[{scriptId}] ShowText.data.portrait_side must be 'left' or 'right' (id='{cmd.Id}').");
						cmd.Data["portrait_side"] = side;
					}
				}

				break;
			}

			case "Choice":
			{
				var allowedKeys = new HashSet<string>(StringComparer.Ordinal) { "options" };
				RequireDataOnlyKeys(allowedKeys, $"Choice.data (id='{cmd.Id}')");
				RequireBranchesEmpty($"Choice (id='{cmd.Id}')");

				if (!dataEl.TryGetProperty("options", out var optionsEl) || optionsEl.ValueKind != JsonValueKind.Array)
				{
					report.Errors.Add($"[{scriptId}] Choice requires data.options array (id='{cmd.Id}').");
					break;
				}
				if (optionsEl.GetArrayLength() < 1)
					report.Errors.Add($"[{scriptId}] Choice.options length must be >= 1 (id='{cmd.Id}').");

				var options = new Godot.Collections.Array();
				foreach (var optEl in optionsEl.EnumerateArray())
				{
					if (optEl.ValueKind != JsonValueKind.Object)
					{
						report.Errors.Add($"[{scriptId}] Choice option must be an object (id='{cmd.Id}').");
						continue;
					}

					var allowedOptKeys = new HashSet<string>(StringComparer.Ordinal)
					{
						"text_key",
						"next",
						"condition",
						"set"
					};
					foreach (var p in optEl.EnumerateObject())
					{
						if (!allowedOptKeys.Contains(p.Name))
							report.Errors.Add($"[{scriptId}] Choice option: unknown key '{p.Name}' (id='{cmd.Id}').");
					}

					if (!optEl.TryGetProperty("text_key", out var textKeyEl) || textKeyEl.ValueKind != JsonValueKind.String)
						report.Errors.Add($"[{scriptId}] Choice option requires text_key string (id='{cmd.Id}').");
					if (!optEl.TryGetProperty("next", out var nextEl) || nextEl.ValueKind != JsonValueKind.String)
						report.Errors.Add($"[{scriptId}] Choice option requires next string (id='{cmd.Id}').");

					var textKey = textKeyEl.GetString() ?? "";
					var nextId = nextEl.GetString() ?? "";
					if (string.IsNullOrEmpty(nextId))
						report.Errors.Add($"[{scriptId}] Choice option next cannot be empty (id='{cmd.Id}').");

					var option = new ChoiceOption { TextKey = textKey, NextId = nextId };

					if (optEl.TryGetProperty("condition", out var condEl))
					{
						if (condEl.ValueKind != JsonValueKind.String)
							report.Errors.Add($"[{scriptId}] Choice option.condition must be string (id='{cmd.Id}').");
						else
							option.Condition = condEl.GetString();
					}

					if (optEl.TryGetProperty("set", out var setEl) && setEl.ValueKind == JsonValueKind.Object)
					{
						var set = new Dictionary<string, Variant>(StringComparer.Ordinal);
						foreach (var kv in setEl.EnumerateObject())
							set[kv.Name] = ToVariant(kv.Value);
						option.Set = set;
					}

					cmd.ChoiceOptions.Add(option);

					// We'll set namespaced next ids during final build.
					var optDict = new Godot.Collections.Dictionary
					{
						{ "text_key", option.TextKey },
						{ "next", option.NextId },
						{ "condition", option.Condition ?? "" }
					};
					if (option.Set != null)
					{
						var setDict = new Godot.Collections.Dictionary();
						foreach (var kv in option.Set)
							setDict[kv.Key] = kv.Value;
						optDict["set"] = setDict;
					}

					options.Add(optDict);
				}

				// Store raw options; final build will convert next ids and remove empty condition keys if needed.
				cmd.Data["options"] = options;
				break;
			}

			case "Jump":
			{
				var allowedBranchKeys = new HashSet<string>(StringComparer.Ordinal) { "target" };
				foreach (var p in branchesEl.EnumerateObject())
				{
					if (!allowedBranchKeys.Contains(p.Name))
						report.Errors.Add($"[{scriptId}] Jump.branches: unknown key '{p.Name}' (id='{cmd.Id}').");
				}
				if (!branchesEl.TryGetProperty("target", out var targetEl) || targetEl.ValueKind != JsonValueKind.String)
					report.Errors.Add($"[{scriptId}] Jump requires branches.target string (id='{cmd.Id}').");
				else
					cmd.JumpTargetId = targetEl.GetString();

				RequireDataOnlyKeys(new HashSet<string>(StringComparer.Ordinal), $"Jump.data (id='{cmd.Id}')");
				cmd.Branches["target"] = ""; // placeholder, namespaced later
				break;
			}

			case "ConditionalJump":
			{
				var allowedDataKeys = new HashSet<string>(StringComparer.Ordinal) { "condition" };
				RequireDataOnlyKeys(allowedDataKeys, $"ConditionalJump.data (id='{cmd.Id}')");
				if (!dataEl.TryGetProperty("condition", out var condEl) || condEl.ValueKind != JsonValueKind.String)
					report.Errors.Add($"[{scriptId}] ConditionalJump requires data.condition string (id='{cmd.Id}').");
				else
				{
					cmd.CondCondition = condEl.GetString();
					cmd.Data["condition"] = cmd.CondCondition;
				}

				var allowedBranchKeys = new HashSet<string>(StringComparer.Ordinal) { "true", "false" };
				foreach (var p in branchesEl.EnumerateObject())
				{
					if (!allowedBranchKeys.Contains(p.Name))
						report.Errors.Add($"[{scriptId}] ConditionalJump.branches: unknown key '{p.Name}' (id='{cmd.Id}').");
				}

				if (!branchesEl.TryGetProperty("true", out var trueEl) || trueEl.ValueKind != JsonValueKind.String)
					report.Errors.Add($"[{scriptId}] ConditionalJump requires branches.true string (id='{cmd.Id}').");
				else
					cmd.CondTrueId = trueEl.GetString();
				if (!branchesEl.TryGetProperty("false", out var falseEl) || falseEl.ValueKind != JsonValueKind.String)
					report.Errors.Add($"[{scriptId}] ConditionalJump requires branches.false string (id='{cmd.Id}').");
				else
					cmd.CondFalseId = falseEl.GetString();

				cmd.Branches["true"] = "";
				cmd.Branches["false"] = "";
				break;
			}

			case "Include":
			{
				var allowedKeys = new HashSet<string>(StringComparer.Ordinal) { "target_script", "entry_id" };
				RequireDataOnlyKeys(allowedKeys, $"Include.data (id='{cmd.Id}')");
				if (!dataEl.TryGetProperty("target_script", out var targetScriptEl) || targetScriptEl.ValueKind != JsonValueKind.String)
					report.Errors.Add($"[{scriptId}] Include requires data.target_script string (id='{cmd.Id}').");
				else
					cmd.IncludeTargetScriptId = targetScriptEl.GetString();

				if (!dataEl.TryGetProperty("entry_id", out var entryEl) || entryEl.ValueKind != JsonValueKind.String)
					report.Errors.Add($"[{scriptId}] Include requires data.entry_id string (id='{cmd.Id}').");
				else
					cmd.IncludeEntryId = entryEl.GetString();

				RequireBranchesEmpty($"Include (id='{cmd.Id}')");

				cmd.Data["target_script"] = cmd.IncludeTargetScriptId ?? "";
				cmd.Data["entry_id"] = cmd.IncludeEntryId ?? "";
				break;
			}

			case "SetVariable":
			{
				var allowedKeys = new HashSet<string>(StringComparer.Ordinal) { "name", "operation", "value" };
				RequireDataOnlyKeys(allowedKeys, $"SetVariable.data (id='{cmd.Id}')");
				if (!dataEl.TryGetProperty("name", out var nameEl) || nameEl.ValueKind != JsonValueKind.String)
					report.Errors.Add($"[{scriptId}] SetVariable requires data.name string (id='{cmd.Id}').");
				else
					cmd.Data["name"] = nameEl.GetString() ?? "";

				if (!dataEl.TryGetProperty("operation", out var opEl) || opEl.ValueKind != JsonValueKind.String)
					report.Errors.Add($"[{scriptId}] SetVariable requires data.operation string (id='{cmd.Id}').");
				else
				{
					var op = opEl.GetString() ?? "";
					if (op != "set" && op != "add" && op != "sub")
						report.Errors.Add($"[{scriptId}] SetVariable.operation must be set|add|sub (id='{cmd.Id}').");
					cmd.Data["operation"] = op;
				}

				if (!dataEl.TryGetProperty("value", out var valEl))
					report.Errors.Add($"[{scriptId}] SetVariable requires data.value (id='{cmd.Id}').");
				else
					cmd.Data["value"] = ToVariant(valEl);

				RequireBranchesEmpty($"SetVariable (id='{cmd.Id}')");
				break;
			}

			case "Delay":
			{
				var allowedKeys = new HashSet<string>(StringComparer.Ordinal) { "seconds" };
				RequireDataOnlyKeys(allowedKeys, $"Delay.data (id='{cmd.Id}')");
				if (!dataEl.TryGetProperty("seconds", out var secEl) || secEl.ValueKind != JsonValueKind.Number)
					report.Errors.Add($"[{scriptId}] Delay requires data.seconds number (id='{cmd.Id}').");
				else
				{
					var seconds = secEl.GetDouble();
					if (seconds <= 0)
						report.Errors.Add($"[{scriptId}] Delay.seconds must be > 0 (id='{cmd.Id}').");
					cmd.Data["seconds"] = seconds;
				}
				RequireBranchesEmpty($"Delay (id='{cmd.Id}')");
				break;
			}

			case "ShowImage":
			{
				var allowedKeys = new HashSet<string>(StringComparer.Ordinal) { "element_id", "texture", "x_percent", "y_percent", "z" };
				RequireDataOnlyKeys(allowedKeys, $"ShowImage.data (id='{cmd.Id}')");
				RequireBranchesEmpty($"ShowImage (id='{cmd.Id}')");

				var elementId = GetOptionalString(dataEl, "element_id") ?? "";
				if (string.IsNullOrEmpty(elementId))
					report.Errors.Add($"[{scriptId}] ShowImage requires data.element_id string (id='{cmd.Id}').");
				cmd.Data["element_id"] = elementId;

				var texture = GetOptionalString(dataEl, "texture") ?? "";
				if (string.IsNullOrEmpty(texture))
					report.Errors.Add($"[{scriptId}] ShowImage requires data.texture string (id='{cmd.Id}').");

				// User request: runtime uses `image` only. Compiler maps spec `texture` -> `image`.
				cmd.Data["image"] = texture;

				if (!dataEl.TryGetProperty("x_percent", out var xEl) || xEl.ValueKind != JsonValueKind.Number)
					report.Errors.Add($"[{scriptId}] ShowImage requires data.x_percent number (id='{cmd.Id}').");
				else
				{
					var x = xEl.GetDouble();
					if (x < 0 || x > 1)
						report.Errors.Add($"[{scriptId}] ShowImage.x_percent must be in [0,1] (id='{cmd.Id}').");
					cmd.Data["x_percent"] = x;
				}

				if (!dataEl.TryGetProperty("y_percent", out var yEl) || yEl.ValueKind != JsonValueKind.Number)
					report.Errors.Add($"[{scriptId}] ShowImage requires data.y_percent number (id='{cmd.Id}').");
				else
				{
					var y = yEl.GetDouble();
					if (y < 0 || y > 1)
						report.Errors.Add($"[{scriptId}] ShowImage.y_percent must be in [0,1] (id='{cmd.Id}').");
					cmd.Data["y_percent"] = y;
				}

				if (!dataEl.TryGetProperty("z", out var zEl) || zEl.ValueKind != JsonValueKind.Number || !zEl.TryGetInt32(out var zInt))
					report.Errors.Add($"[{scriptId}] ShowImage requires integer data.z (id='{cmd.Id}').");
				else
					cmd.Data["z"] = zInt;

				break;
			}

			case "MoveImage":
			{
				var allowedKeys = new HashSet<string>(StringComparer.Ordinal) { "element_id", "x_percent", "y_percent", "duration" };
				RequireDataOnlyKeys(allowedKeys, $"MoveImage.data (id='{cmd.Id}')");
				RequireBranchesEmpty($"MoveImage (id='{cmd.Id}')");

				cmd.Data["element_id"] = GetOptionalString(dataEl, "element_id") ?? "";
				if (!dataEl.TryGetProperty("x_percent", out var xEl) || xEl.ValueKind != JsonValueKind.Number)
					report.Errors.Add($"[{scriptId}] MoveImage requires data.x_percent number (id='{cmd.Id}').");
				else
					cmd.Data["x_percent"] = xEl.GetDouble();

				if (!dataEl.TryGetProperty("y_percent", out var yEl) || yEl.ValueKind != JsonValueKind.Number)
					report.Errors.Add($"[{scriptId}] MoveImage requires data.y_percent number (id='{cmd.Id}').");
				else
					cmd.Data["y_percent"] = yEl.GetDouble();

				if (!dataEl.TryGetProperty("duration", out var dEl) || dEl.ValueKind != JsonValueKind.Number)
					report.Errors.Add($"[{scriptId}] MoveImage requires data.duration number (id='{cmd.Id}').");
				else
				{
					var duration = dEl.GetDouble();
					if (duration < 0)
						report.Errors.Add($"[{scriptId}] MoveImage.duration must be >= 0 (id='{cmd.Id}').");
					cmd.Data["duration"] = duration;
				}

				break;
			}

			case "ClearImage":
			{
				var allowedKeys = new HashSet<string>(StringComparer.Ordinal) { "element_id" };
				RequireDataOnlyKeys(allowedKeys, $"ClearImage.data (id='{cmd.Id}')");
				RequireBranchesEmpty($"ClearImage (id='{cmd.Id}')");
				cmd.Data["element_id"] = GetOptionalString(dataEl, "element_id") ?? "";
				if (string.IsNullOrEmpty(cmd.Data["element_id"].AsString()))
					report.Errors.Add($"[{scriptId}] ClearImage requires data.element_id (id='{cmd.Id}').");
				break;
			}

			case "ClearVideo":
			{
				var allowedKeys = new HashSet<string>(StringComparer.Ordinal) { "element_id" };
				RequireDataOnlyKeys(allowedKeys, $"ClearVideo.data (id='{cmd.Id}')");
				RequireBranchesEmpty($"ClearVideo (id='{cmd.Id}')");
				cmd.Data["element_id"] = GetOptionalString(dataEl, "element_id") ?? "";
				break;
			}

			case "PlayVoice":
			{
				var allowedKeys = new HashSet<string>(StringComparer.Ordinal) { "audio" };
				RequireDataOnlyKeys(allowedKeys, $"PlayVoice.data (id='{cmd.Id}')");
				RequireBranchesEmpty($"PlayVoice (id='{cmd.Id}')");
				cmd.Data["audio"] = GetOptionalString(dataEl, "audio") ?? "";
				break;
			}

			case "PlayBGM":
			case "PlaySFX":
			{
				var allowedKeys = new HashSet<string>(StringComparer.Ordinal) { "audio", "fade", "Loop" };
				RequireDataOnlyKeys(allowedKeys, $"{cmd.Type}.data (id='{cmd.Id}')");
				RequireBranchesEmpty($"{cmd.Type} (id='{cmd.Id}')");

				var audio = GetOptionalString(dataEl, "audio") ?? "";
				cmd.Data["audio"] = audio;

				if (dataEl.TryGetProperty("fade", out var fadeEl))
				{
					if (fadeEl.ValueKind != JsonValueKind.Number)
						report.Errors.Add($"[{scriptId}] {cmd.Type}.fade must be number (id='{cmd.Id}').");
					else
						cmd.Data["fade"] = fadeEl.GetDouble();
				}

				if (dataEl.TryGetProperty("Loop", out var loopEl))
				{
					if (loopEl.ValueKind != JsonValueKind.True && loopEl.ValueKind != JsonValueKind.False)
						report.Errors.Add($"[{scriptId}] {cmd.Type}.Loop must be boolean (id='{cmd.Id}').");
					else
						cmd.Data["Loop"] = loopEl.GetBoolean();
				}
				break;
			}

			case "PlayVideo":
			{
				var allowedKeys = new HashSet<string>(StringComparer.Ordinal) { "video", "Loop" };
				RequireDataOnlyKeys(allowedKeys, $"PlayVideo.data (id='{cmd.Id}')");
				RequireBranchesEmpty($"PlayVideo (id='{cmd.Id}')");
				cmd.Data["video"] = GetOptionalString(dataEl, "video") ?? "";
				if (dataEl.TryGetProperty("Loop", out var loopEl) && (loopEl.ValueKind == JsonValueKind.True || loopEl.ValueKind == JsonValueKind.False))
					cmd.Data["Loop"] = loopEl.GetBoolean();
				break;
			}

			default:
				report.Errors.Add($"[{scriptId}] Unsupported command type '{cmd.Type}' (id='{cmd.Id}').");
				break;
		}

		// Validate condition syntax early.
		if (cmd.Type == "ConditionalJump" && cmd.CondCondition != null)
		{
			try
			{
				// Syntax validation: unknown vars evaluate to false.
				var parser = new VNConditionParser(cmd.CondCondition, new Dictionary<string, Variant>());
				parser.ParseExpressionToBool();
			}
			catch
			{
				report.Errors.Add($"[{scriptId}] Invalid condition syntax in ConditionalJump '{cmd.Id}'.");
			}
		}

		if (cmd.Type == "Choice")
		{
			for (var i = 0; i < cmd.ChoiceOptions.Count; i++)
			{
				var opt = cmd.ChoiceOptions[i];
				if (string.IsNullOrEmpty(opt.Condition))
					continue;
				try
				{
					var parser = new VNConditionParser(opt.Condition, new Dictionary<string, Variant>());
					parser.ParseExpressionToBool();
				}
				catch
				{
					report.Errors.Add($"[{scriptId}] Invalid condition syntax in Choice '{cmd.Id}', option #{i}.");
				}
			}
		}
	}

	private static void ValidateScriptReferences(
		ParsedScript script,
		Dictionary<string, ParsedScript> scriptsById,
		VNCompileReport report)
	{
		foreach (var cmd in script.Commands.Values)
		{
			// next reference within the same script.
			if (cmd.NextId != null)
			{
				if (!script.Commands.ContainsKey(cmd.NextId))
					report.Errors.Add($"[{script.ScriptId}] Command '{cmd.Id}' has next='{cmd.NextId}' which does not exist in this script.");
			}

			switch (cmd.Type)
			{
				case "Jump":
					if (cmd.JumpTargetId != null && !script.Commands.ContainsKey(cmd.JumpTargetId))
						report.Errors.Add($"[{script.ScriptId}] Jump '{cmd.Id}' target='{cmd.JumpTargetId}' not found.");
					break;
				case "ConditionalJump":
					if (cmd.CondTrueId != null && !script.Commands.ContainsKey(cmd.CondTrueId))
						report.Errors.Add($"[{script.ScriptId}] ConditionalJump '{cmd.Id}' true='{cmd.CondTrueId}' not found.");
					if (cmd.CondFalseId != null && !script.Commands.ContainsKey(cmd.CondFalseId))
						report.Errors.Add($"[{script.ScriptId}] ConditionalJump '{cmd.Id}' false='{cmd.CondFalseId}' not found.");
					break;
				case "Choice":
					foreach (var opt in cmd.ChoiceOptions)
					{
						if (!script.Commands.ContainsKey(opt.NextId))
							report.Errors.Add($"[{script.ScriptId}] Choice '{cmd.Id}' option.next='{opt.NextId}' not found.");
					}
					break;
				case "Include":
					{
						if (string.IsNullOrEmpty(cmd.IncludeTargetScriptId) || string.IsNullOrEmpty(cmd.IncludeEntryId))
						{
							report.Errors.Add($"[{script.ScriptId}] Include '{cmd.Id}' missing target_script/entry_id.");
							break;
						}

						if (!scriptsById.ContainsKey(cmd.IncludeTargetScriptId))
							report.Errors.Add($"[{script.ScriptId}] Include '{cmd.Id}' target_script='{cmd.IncludeTargetScriptId}' not found.");
						else
						{
							var targetScript = scriptsById[cmd.IncludeTargetScriptId];
							if (!targetScript.Commands.ContainsKey(cmd.IncludeEntryId))
								report.Errors.Add($"[{script.ScriptId}] Include '{cmd.Id}' entry_id='{cmd.IncludeEntryId}' not found in target script '{cmd.IncludeTargetScriptId}'.");
						}
						break;
					}
			}
		}

		// start reference.
		if (!script.Commands.ContainsKey(script.StartId))
			report.Errors.Add($"[{script.ScriptId}] start='{script.StartId}' not found among commands.");
	}

	private static void ReachabilityWarn(
		ParsedScript script,
		Dictionary<string, ParsedScript> scriptsById,
		VNCompileReport report)
	{
		static string Ns(string scriptId, string cmdId) => $"{scriptId}.{cmdId}";

		var startNs = Ns(script.ScriptId, script.StartId);
		var visited = new HashSet<string>(StringComparer.Ordinal);
		var queue = new Queue<string>();
		queue.Enqueue(startNs);

		while (queue.Count > 0)
		{
			var currentNs = queue.Dequeue();
			if (visited.Contains(currentNs))
				continue;
			visited.Add(currentNs);

			var parts = currentNs.Split('.', 2);
			if (parts.Length != 2)
				continue;
			var curScriptId = parts[0];
			var curCmdId = parts[1];

			if (!scriptsById.TryGetValue(curScriptId, out var curScript))
				continue;
			if (!curScript.Commands.TryGetValue(curCmdId, out var cmd))
				continue;

			foreach (var nextNs in GetOutgoingEdges(cmd, curScript))
			{
				if (!string.IsNullOrEmpty(nextNs) && !visited.Contains(nextNs))
					queue.Enqueue(nextNs);
			}
		}

		foreach (var cmdId in script.Commands.Keys)
		{
			var ns = Ns(script.ScriptId, cmdId);
			if (!visited.Contains(ns))
				report.Warnings.Add($"[{script.ScriptId}] Unreachable command: {cmdId}");
		}
	}

	private static IEnumerable<string?> GetOutgoingEdges(ParsedCommand cmd, ParsedScript script)
	{
		static string Ns(string scriptId, string cmdId) => $"{scriptId}.{cmdId}";

		switch (cmd.Type)
		{
			case "ShowText":
			case "SetVariable":
			case "Delay":
			case "ShowImage":
			case "MoveImage":
			case "ClearImage":
			case "ClearVideo":
			case "PlayVoice":
			case "PlayBGM":
			case "PlaySFX":
			case "PlayVideo":
				return cmd.NextId != null ? new[] { Ns(script.ScriptId, cmd.NextId) } : Array.Empty<string>();
			case "Choice":
				{
					var list = new List<string>();
					foreach (var opt in cmd.ChoiceOptions)
						list.Add(Ns(script.ScriptId, opt.NextId));
					return list;
				}
			case "Jump":
				return cmd.JumpTargetId != null ? new[] { Ns(script.ScriptId, cmd.JumpTargetId) } : Array.Empty<string>();
			case "ConditionalJump":
				{
					var list = new List<string>();
					if (cmd.CondTrueId != null) list.Add(Ns(script.ScriptId, cmd.CondTrueId));
					if (cmd.CondFalseId != null) list.Add(Ns(script.ScriptId, cmd.CondFalseId));
					return list;
				}
			case "Include":
				{
					// Edge to include target; also include return id so subsequent nodes don't appear unreachable.
					var list = new List<string>();
					if (!string.IsNullOrEmpty(cmd.IncludeTargetScriptId) && !string.IsNullOrEmpty(cmd.IncludeEntryId))
					{
						list.Add(Ns(cmd.IncludeTargetScriptId, cmd.IncludeEntryId));
					}
					if (cmd.NextId != null)
						list.Add(Ns(script.ScriptId, cmd.NextId));
					return list;
				}
			default:
				return Array.Empty<string?>();
		}
	}

	private static VNScriptResource BuildResource(ParsedScript script)
	{
		static string Ns(string scriptId, string cmdId) => $"{scriptId}.{cmdId}";

		var res = new VNScriptResource
		{
			ScriptId = script.ScriptId,
			StartId = Ns(script.ScriptId, script.StartId)
		};

		var commands = new Godot.Collections.Dictionary();

		foreach (var cmd in script.Commands.Values)
		{
			var nsId = Ns(script.ScriptId, cmd.Id);
			var cmdRes = new VNCommandResource
			{
				Id = nsId,
				Type = cmd.Type,
				Next = cmd.NextId != null ? Ns(script.ScriptId, cmd.NextId) : "",
				Data = cmd.Data,
				Branches = cmd.Branches
			};

			// Build namespaced branches and data reference fields.
			if (cmd.Type == "Jump" && !string.IsNullOrEmpty(cmd.JumpTargetId))
				cmdRes.Branches["target"] = Ns(script.ScriptId, cmd.JumpTargetId);

			if (cmd.Type == "ConditionalJump")
			{
				if (!string.IsNullOrEmpty(cmd.CondTrueId))
					cmdRes.Branches["true"] = Ns(script.ScriptId, cmd.CondTrueId);
				if (!string.IsNullOrEmpty(cmd.CondFalseId))
					cmdRes.Branches["false"] = Ns(script.ScriptId, cmd.CondFalseId);
			}

			if (cmd.Type == "Choice")
			{
				// Overwrite option.next with namespaced ids.
				if (cmdRes.Data.ContainsKey("options"))
				{
					var optionsVar = cmdRes.Data["options"];
					if (optionsVar.VariantType != Variant.Type.Array)
						continue;
					var optionsArr = optionsVar.AsGodotArray();
					for (int i = 0; i < optionsArr.Count; i++)
					{
						var optVar = optionsArr[i];
						if (optVar.VariantType != Variant.Type.Dictionary)
							continue;

						var optDict = optVar.AsGodotDictionary();
						if (optDict.ContainsKey("next"))
						{
							var nextUnNs = optDict["next"].AsString();
							if (script.Commands.ContainsKey(nextUnNs))
								optDict["next"] = Ns(script.ScriptId, nextUnNs);
						}
					}
				}
			}

			if (cmd.Type == "Include")
			{
				// namespaced entry_id for runtime: include target command id.
				if (!string.IsNullOrEmpty(cmd.IncludeTargetScriptId) && !string.IsNullOrEmpty(cmd.IncludeEntryId))
					cmdRes.Data["entry_id"] = Ns(cmd.IncludeTargetScriptId, cmd.IncludeEntryId);
			}

			commands[nsId] = cmdRes;
		}

		res.Commands = commands;
		return res;
	}
}

