# Warband (Overworld Army Sandbox) — Notes for Contributors

This repo is a **technical prototype** to validate the *core systemic loop* of a Mount & Blade-like overworld army sandbox. Visuals are placeholders; priority is **simulation, emergence, and testability**.

Source of truth for goals/roadmap: [`README.md`](README.md).

## Project intent (high level)
- **Overworld traversal** with parties moving in real time.
- **AI parties** with simple strategic behavior (wander/chase/flee).
- **Encounter loop** (trigger → resolve).
- **Logistics** (troops, morale, wages) and **faction territorial simulation**.
- Incremental delivery: *every step remains playable*, with debug tooling early.

## Roadmap focus: Phase 0 (current)
Per `README.md`, Phase 0 is now split into:

### Step 0.1 — Game Loop + Time Scale
- Fixed timestep simulation (README example: **10 ticks/sec**)
- Pause / 1x / 2x / 5x speed
- Global time counter

**Acceptance criteria (0.1):**
- Tick-driven logic behaves consistently regardless of FPS
- Speed multiplier changes simulation rate correctly
- Pause freezes simulation logic

### Step 0.2 — Basic World Bounds
- World = simple rectangle
- Clamp positions inside bounds
- Debug grid optional

**Acceptance criteria (0.2):**
- Entities cannot exit map bounds
- Movement along edges is stable (no jitter)

### Step 0.2 implementation plan (bounds, offsets, framing)
- **Single source of truth**: introduce a `WorldBounds` component (Godot-side) that owns:
  - `OuterRect` (the “map rectangle” in world coordinates)
  - `VisualPaddingPx` (keeps the visible map away from viewport edges)
  - `ClampInsetPx` (keeps entities away from the border to avoid jitter/collider overlap)
- **Framing / keeping the map visible**:
  - Add a `Camera2D` (or configure an existing one) to center on `OuterRect`’s center.
  - Auto-adjust `Camera2D.Zoom` (or shrink the map) so the full `OuterRect` fits within the viewport **minus `VisualPaddingPx`**.
- **Clamping rules**:
  - Clamp entity *center* position into `InnerRect = OuterRect.Grow(-ClampInsetPx)`.
  - If entities have radius later, clamp using `(ClampInsetPx + entityRadius)` to ensure their collider stays inside.
- **Border-adjacent “fixed elements”** (towns/POIs):
  - Place them relative to `InnerRect` corners/edges (e.g. `InnerRect.Position + new Vector2(margin, margin)`) rather than hardcoding coordinates.
  - This keeps content stable if the map size or padding changes.

## Architecture choices (why C# here)
Phase 0 uses **C#-first** to keep the simulation core:
- **pure / deterministic** (no Godot references),
- **unit-testable** via `dotnet test`,
- coupled to Godot only through a thin adapter Node that feeds real `delta` into the clock.

GDScript can still be used later for rapid UI/debug, but Phase 0’s core is intentionally engine-agnostic.

## Folder conventions (added by Phase 0)
- `src/Warband.Core/`: **pure C#** simulation foundation (no Godot types).
- `tests/Warband.Core.Tests/`: NUnit tests for the core library.
- `scripts/`: Godot C# scripts (thin adapter + bounds drawing).
- `scenes/`: Godot scenes (`Main.tscn` is the Phase 0 harness).

## How to run
- **Godot**: open the project and run the main scene (set in `project.godot`).
  - Controls (Phase 0 Step 0.1 harness):
    - Bound through **InputMap actions** (see `project.godot [input]`):
      - `sim_toggle_pause` (default: Space)
      - `sim_time_slower` (default: `[`)
      - `sim_time_faster` (default: `]`)
- **Tests** (core logic): from repo root:

```bash
dotnet test
```

## Input best practices (foundation for future key remapping)
- Always code against **action names** (e.g. `Input.IsActionJustPressed("sim_toggle_pause")`), not hardcoded `Keycode`s.
- Keep action names **stable** and **semantically named** (what the action means, not which key triggers it).
- Define default bindings in `project.godot` under `[input]` so a fresh checkout is playable.
- For future remapping:
  - Read current bindings with `InputMap.ActionGetEvents(action)`
  - Replace bindings by `InputMap.ActionEraseEvents(action)` + `InputMap.ActionAddEvent(action, ev)`
  - Persist user bindings in a config file (e.g. `user://settings.cfg`) and apply them on boot.
- Avoid using `_UnhandledInput` to inspect raw key events unless you are implementing a remap UI that needs to capture “next key pressed”.

### Debug UI best practice: show resolved bindings
- The on-screen debug overlay should **not** print action names (e.g. `sim_toggle_pause`) because those are not user-facing.
- Instead, resolve current bindings from the InputMap and render them:
  - `InputMap.ActionGetEvents(action)` → list bound `InputEvent`s
  - Convert to a friendly string (examples):
    - `InputEventKey` → `OS.GetKeycodeString(key.Keycode)` plus modifiers (`Ctrl+`, `Alt+`, …)
    - `InputEventMouseButton` → `LMB`/`RMB`/etc
- This keeps debug output correct automatically after future key remapping.

### Suggested future implementation plan (key remapping)
- Create a small service (e.g. `scripts/InputRemapService.cs`) that:
  - Knows the canonical action list (Phase 0+: sim + later player movement, camera, UI).
  - Loads saved mappings at startup and applies them to `InputMap`.
  - Exposes `Rebind(action, InputEvent)` and `ResetToDefaults()`.
- Create a minimal debug UI later (can be GDScript or C#) that:
  - Lists actions → current bindings
  - Enters “listening mode” to capture the next input event and rebind it
  - Saves to `user://settings.cfg`

## Determinism notes (Phase 0 scope)
- The simulation advances in fixed ticks produced by an accumulator-based clock.
- Real-time `delta` only determines *how many* fixed ticks to execute; simulation state updates should only happen inside ticks.
- A **catch-up clamp** limits max ticks per frame to avoid spiral-of-death under hitches.

## Current implementation status vs README
- **Implemented (0.1)**: fixed-tick clock + pause/1x/2x/5x scaling + visible tick/sim time overlay.
- **Implemented (0.2)**: overworld bounds are rendered as a rectangle; `WorldBounds2D` exposes `OuterRect`/`InnerRect` and provides clamping helpers; camera framing keeps the rect visible with padding.
- **Implemented (1.1)**: player party placeholder (circle), click-to-move sets target, constant speed movement, stop threshold; movement advances on fixed ticks and clamps to `WorldBounds2D.InnerRect`.

## Phase 1 — Step 1.1 notes (click-to-move)
- **Determinism rule**: movement uses `IFixedTick.FixedTick(tickDeltaSeconds)` called by `SimulationRoot` on each simulation tick (not `_Process`).
- **Input rule**: target setting uses an InputMap action (`player_set_move_target`) instead of raw mouse button checks.
- **Bounds rule**: both target and movement are clamped using `WorldBounds2D.ClampPointToInnerRect(...)` to satisfy Phase 0 Step 0.2 constraints.

## Phase 1 — Step 1.2 notes (speed model)
- Implemented README formula: \(speed = baseSpeed / (1 + partySize * k)\).
- `PlayerParty` now exposes:
  - `BaseSpeedPxPerSec`
  - `PartySize`
  - `PartySizePenaltyK`
  - `CurrentSpeedPxPerSec` (computed)
- Debug/testing support:
  - Added actions `party_size_decrease` / `party_size_increase` (defaults: `PageDown` / `PageUp`) to adjust `PartySize` at runtime.
  - Debug overlay shows current **PartySize** and computed **Speed**.
- Implementation note:
  - Party size changes are polled in `SimulationRoot._Process` (using `Input.IsActionJustPressed`) so the hotkeys still work even if UI layers consume arrow key events.
- Debug binding display:
  - For keys, debug uses `InputEventKey.AsText()` so special keys like arrows render correctly (and modifiers are accurate).
- Determinism: speed affects movement only inside `FixedTick(...)`, preserving Phase 0 tick-driven behavior.

