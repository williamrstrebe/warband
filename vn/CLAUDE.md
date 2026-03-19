## Visual Novel Plugin System — Simplified Runtime Notes

These notes reflect the current “Phase 0 / prototype-first” direction and the simplifications you requested:

- Localization: ignored. `ShowText` uses literal text from `fallback_text` (or `text` / literal `text_key`).
- Delay: ignored / not implemented.
- Texture key: ignored for `ShowImage`. The runner uses `image` only.
- Command flow: VNRunner executes the locked command set except `Delay` and localization lookup, and `ShowImage` uses `image` only.

### VNRunner
- Scene: `res://vn/scenes/VNRunner.tscn`
- Runtime script: `res://vn/runtime/VNRunner.cs`
- Signals:
  - `FinishedEventHandler` emitted when the VN ends.

### Resource loading (runtime)
- `VNRunner.Start(scriptId)` loads: `res://vn/compiled/{scriptId}.res`
- This repo now includes:
  - Storage compiler core: `res://vn/compiler/VNStorageCompilerCore.cs`
  - Godot-side compiler runner: `res://vn/editor/VNStorageCompiler.cs`

When compiling:
- JSON is read from `res://vn/scripts_json/*.json`
- `.res` output is written to `res://vn/compiled/{script_id}.res`
- Per your requirement, compiler maps spec `ShowImage.data.texture` -> runtime `ShowImage.data.image`.

