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
  - Add a `Camera2D` (or configure an existing one).
  - Early prototype behavior: when the world fits, center on `OuterRect`’s center and (optionally) adjust zoom so the full `OuterRect` fits within the viewport minus `VisualPaddingPx`.
  - After Phase 5 scales the world beyond the viewport, switch to “camera follows player” behavior (clamped to world bounds) instead of trying to keep the entire `OuterRect` visible.
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

### Step 1.2 verification status (2026-03-17)
- The runtime behavior matches README expectations:
  - Increasing `PartySize` via `party_size_increase` / `party_size_decrease` visibly reduces/increases `CurrentSpeedPxPerSec` in the debug overlay.
  - Movement distance per tick is derived from `CurrentSpeedPxPerSec` inside `PlayerParty.FixedTick(...)`, so arrival time scales with party size.
- Core unit tests (`Warband.Core.Tests`) currently cover the fixed-tick clock and time scaling; there are **no dedicated C# unit tests** for the speed model itself, but the model is a pure property and easily testable if needed.
- All tests pass (`dotnet test` from repo root), so Phase 1 Step 1.2 is treated as **implemented and stable** for Phase 2 work.

## Phase 2 — Step 2.1 notes (Random Walk AI)
- `RandomAI` is a Godot-side `Area2D, IFixedTick` that reuses the same speed model fields as `PlayerParty` (including `BaseSpeedPxPerSec`, `PartySize`, `PartySizePenaltyK`, and `CurrentSpeedPxPerSec`).
- Behavior model:
  - AI alternates between an **Idle** state and a **Moving** state.
  - While idle, it waits for a random duration (0.5–1.5 seconds). The waiting time is converted to ticks using the configured simulation tick rate.
  - When moving, it:
	- Picks a random point inside `WorldBounds2D.InnerRect` as the current target.
	- Moves toward that point using `CurrentSpeedPxPerSec` inside `FixedTick(...)`.
	- Periodically re-targets to a new random point after a random interval (1–3 seconds, converted to ticks using the configured simulation tick rate) to encourage broad map coverage.
	- Transitions back to Idle once it comes within `StopThresholdPx` of its current target.
- Bounds rule: positions are always clamped via `WorldBounds2D.ClampPointToInnerRect(...)`, so AI circles cannot leave the map and have no boundary jitter.
- Tests:
  - No dedicated unit tests yet (logic is Godot-side), but the class is pure apart from `WorldBounds2D` and uses deterministic tick-driven updates, so it is a good candidate for future harness-style tests if needed.

## Phase 2 — Step 2.2 notes (Detection Radius System)
- Implemented a per-party **detection radius** using a child `Area2D` named `Detection` with a `CircleShape2D` sized by `DetectionRadiusPx`.
- Both `PlayerParty` and `RandomAI` now ensure, at runtime, that:
  - A **body collider** exists (`CollisionShape2D` named `BodyShape`, circle radius = `RadiusPx`) so they can be detected.
  - The body is placed on **collision layer 1** and does not actively detect (`CollisionMask = 0`).
  - The detection area uses `CollisionMask = 1` to detect parties on layer 1.
- Debug:
  - Optional radius visualization via `DrawDetectionRadius` (drawn as a faint circle).
  - Logs to output on enter/exit via `[Detect] ...` messages.
- Note: the colliders are created in code so **spawned** AI parties automatically work (no per-instance scene wiring required).

## Phase 2 — Step 2.3 notes (Simple Behavior Switch)
- `RandomAI` now supports 3 behaviors:
  - **Wander** (Idle/Move random walk as in Step 2.1)
  - **Chase** the player when the player is inside the detection radius *and* AI `StrengthValue >= player.PartySize`
  - **Flee** when the player is inside the radius but AI is weaker (`StrengthValue < player.PartySize`)
- `StrengthValue` defaults to `PartySize` if not explicitly set; this is a temporary proxy until a richer strength model lands.
- Determinism: state changes and movement decisions happen only inside `FixedTick(...)`.

## Phase 3 — Step 3.1 notes (Collision Encounter)
- Implemented an encounter trigger in `SimulationRoot` that, after each simulation tick, checks for **overlap** between the player and any `RandomAI` party using circle radius distance:
  - overlap when `distance(player, ai) <= player.RadiusPx + ai.RadiusPx`
- When an overlap is detected:
  - Simulation is paused by setting the time scale index to `0`.
  - A modal UI (`EncounterModal`) is shown with three options: **Fight**, **Auto-resolve**, **Flee**.
- Loop safety:
  - While an encounter is active, no new encounter can start.
  - After resolving, a short tick-based cooldown prevents immediate re-trigger.
  - Parties are pushed apart and clamped to `WorldBounds2D.InnerRect` to avoid repeated triggers.

## Phase 3 — Step 3.2 notes (Auto-Resolve Combat v1)
- Implemented a first-pass auto-resolve in `SimulationRoot` using **arbitrary placeholder values** (per Phase 3 scope):
  - Power proxy: `PartySize` multiplied by a random modifier in \([0.8, 1.2]\).
  - Winner: higher power.
  - Loser troop loss: `-1` troop (clamped to min).
  - AI parties are removed (`QueueFree`) when their `PartySize` reaches `0`.
- Flee v1:
  - Guaranteed escape if player speed is higher than AI speed; otherwise a 50% chance.

### Morale + Loot (gold) additions
- Added `Morale` and `Gold` to both `PlayerParty` and `RandomAI`:
  - Defaults: `Morale = 10`, `Gold = 0`.
- Auto-resolve now also updates these values (placeholder-friendly constants):
  - On **win**: player `Morale += 2`, player `Gold += 3`; AI `Morale -= 2`, AI `Gold -= 2` (clamped).
  - On **loss**: player `Morale -= 2`, player `Gold -= 2`; AI `Morale += 2`, AI `Gold += 3` (clamped).
- HUD rule: only the **player** has `Morale`/`Gold` shown on the top debug label; AI keeps values only in vars.

## Phase 3 extension — AI vs AI Battles (2026-03-18)
- `RandomAI` now can detect other `RandomAI` parties inside its detection radius and will:
  - **Chase** only targets that are **weaker** (by `StrengthValue`).
  - Continue using the tick-driven state machine (`FixedTick(...)`) for deterministic behavior.
- `SimulationRoot` now resolves AI vs AI combat automatically when two `RandomAI` parties **overlap**:
  - Winner determination: higher `StrengthValue` wins (tie-broken by gold, then instance id).
  - Transfer rules (spec):
	- Winner gains `loser.PartySize / 2` troops.
	- Winner gains **all** `loser.Gold`.
	- Loser is destroyed via `QueueFree()`.
  - Consistency: after absorption, winner’s `StrengthValue` is set to `winner.PartySize` so future chase/battle decisions remain aligned.
- HUD rule preserved:
  - Only the player’s `PartySize`, `Morale`, and `Gold` are displayed on-screen (AI values remain internal vars).

## Phase 3 extension follow-up — Correct weaker-target adaptation (2026-03-18)
- When two AIs start at equal strength, the initial `AreaEntered`-based detection does not select a weaker target.
- However, after AI-vs-AI battles, troop size (and thus `StrengthValue`) changes while `AreaEntered` may not fire again.
- Fix: `RandomAI.FixedTick(...)` now re-evaluates weaker targets every tick:
  - Clears the stored `_weakerAiInRange` if the target is no longer weaker or drifts outside range.
  - If `_weakerAiInRange` is null, it scans sibling `RandomAI` instances to acquire the nearest AI that is currently weaker and inside `DetectionRadiusPx`.

## Phase 3 extension follow-up — Battle winner rests (2026-03-18)
- After AI-vs-AI battles resolve, the **winner AI** rests for a short period (“reused idle” behavior).
- Implementation:
  - `RandomAI` gained `StartRestTicks(int ticks)`.
  - `SimulationRoot.ResolveAiBattle(...)` calls `winner.StartRestTicks(...)` after troop/gold transfer.
  - While resting, `RandomAI.FixedTick(...)` clears its target and returns early, so it stays idle even if new weaker enemies are detected.
- Idle response to approaching enemies:
  - When not resting, an AI in `WanderIdle` will switch to `Chase` on the next tick if a weaker enemy AI is within detection range (because `_weakerAiInRange` is re-acquired/revalidated every tick).

## Phase 4 — Step 4.1 notes (Troop Recruitment Stub)
- Implemented **town POIs** as `Town` nodes (blue squares) spawned by `SimulationRoot` near the corners of `WorldBounds2D.InnerRect`.
- When the player overlaps a town:
  - Simulation pauses (time scale index set to `0`).
  - A `TownModal` is shown with current player `Gold` and party size.
  - A **Recruit** button attempts to buy `+5` troops for a gold cost (defaults: `RecruitTroopsAmount = 5`, `RecruitCostGold = 3`).
  - A **Leave** button closes the modal, resumes simulation, and nudges the player away to prevent immediate re-trigger.

## Phase 4 — Step 4.2 notes (Wage Tick System)
- Added a global day/hour counter to the top debug HUD, derived from the fixed-tick clock.
- Implemented wage payment cadence:
  - Wage runs every **7 in-game days** (`WageIntervalDays = 7`).
  - Prototype mapping: `SimSecondsPerInGameHour` (default `1f`) defines how sim-time seconds map to in-game hours.
  - Wage cost applied as: `player.Gold -= player.PartySize * WageGoldPerTroopPerDay * WageIntervalDays`.
- On-screen indicator:
  - Debug HUD now shows `Wage in: {days}d {hours}h` alongside `Day` and `Hr`.
- Prototype bankruptcy/desertion stub:
  - If gold is reduced to 0 during a wage tick, player loses 1 troop and morale -1.

## Phase 4 — Step 4.3 notes (Morale System v1)
- Starvation:
  - Implemented “once per game day” morale reduction.
  - Every in-game day (`24h`, using the same `SimSecondsPerInGameHour` mapping) `MoraleStarvationPerDay` (default `1`) is applied to all `PartyBase` instances (player + AI).
- Outnumbered penalty:
  - Applied immediately before battle resolution when a party has fewer troops than its opponent.
  - Reduces morale by `MoraleOutnumberedPenalty` (default `2`).
- Recent victory / defeat:
  - After battle outcome is determined:
	- winner morale increases by `MoraleVictoryBonus` (default `+2`)
	- loser morale decreases by `MoraleDefeatPenalty` (default `2`)
  - This is applied to both:
	- player vs AI (`AutoResolve(...)`)
	- AI vs AI (`ResolveAiBattle(...)`)
- Morale-aware combat power:
  - Combat power now uses a `moraleFactor` so morale affects who wins:
	- `power = PartySize * random(0.8..1.2) * moraleFactor`
	- `moraleFactor = MoraleCombatPowerFactorBase + (morale/100)*MoraleCombatPowerFactorScale`
- Flee behavior:
  - The `Flee` choice (player modal option) now uses flee odds influenced by morale difference (not just speed).

## Debug readability
- `PlayerParty` and `RandomAI` now draw the current `PartySize` above each party circle for quick on-map readability.

## OOP refactor — `PartyBase` (shared party mechanics)
- Introduced `PartyBase : Area2D, IFixedTick` to centralize shared behavior previously duplicated across `PlayerParty` and `RandomAI`.
- `PartyBase` owns:
  - Shared exported stats: speed model inputs, `PartySize` bounds, `Morale`, `Gold`, radii, color, bounds path.
  - Computed speed: `CurrentSpeedPxPerSec`.
  - Bounds clamping: `Clamp(...)` backed by `WorldBounds2D`.
  - Collider setup: ensures a body `CollisionShape2D` (reuses any scene-authored collider if present) and creates a detection `Area2D` (`Detection`) with a `CircleShape2D`.
  - Detection dispatch hooks: `OnPartyDetected/OnPartyLost` virtual methods for subclasses to extend behavior.
  - Shared tick movement primitive: `TickMoveTowardTarget(...)` plus `SetTarget(...)`.
  - Shared debug drawing: circle + party size label + optional detection radius ring.
- `PlayerParty` now focuses only on:
  - Input (drag-to-set target)
  - Using `TickMoveTowardTarget(...)` in `FixedTick(...)`
  - Drawing the move target indicator
- `RandomAI` now focuses only on:
  - Wander/Chase/Flee state machine
  - Overriding party-detection hooks to track the player in range

## Viewport defaults (1920 x 1080)
- Updated `project.godot` to default the game window viewport size to `1920x1080`.
- Updated `WorldCameraFit2D` to follow the player rather than keeping the entire `WorldBounds2D.OuterRect` visible.
  - The camera is clamped so the viewport stays inside the world bounds (using `VisualPaddingPx` and the current camera zoom), and it follows continuously (every frame).
  - This matches Phase 5’s need for a larger world where the map can be bigger than the viewport.

## Camera zoom scaling tweak
- Updated `WorldCameraFit2D.DefaultZoom` so the visible world scale is 1/4 of the previous default.

## Map scale-up prerequisite (Phase 5)
- Updated `WorldBounds2D.Size` default from `1200x800` to `3600x2400` (3x width and height), so there is enough space for map density work.

## Tick rate change (smoother movement placeholders)
- Increased `SimulationRoot.TicksPerSecond` default from `10` to `60` to reduce visible per-tick stepping for placeholder movement visuals.
- Updated `RandomAI` idle/retarget timers to use the configured tick rate (no more hardcoded `* 10` assumptions).

## Camera behavior (Phase 5+)
- `WorldCameraFit2D` is now minimal: it continuously centers `Camera2D` on the player each frame.
- Removed viewport/world clamp logic to prevent camera-boundary-induced visual stepping while the player moves near the screen edges.

## Visual Novel plugin system (simplified)
- Started `VNRunner` scene + runtime in `vn/`:
  - Scene: `res://vn/scenes/VNRunner.tscn`
  - Runtime: `res://vn/runtime/VNRunner.cs`
- Current simplifications per user request:
  - Localization and Delay are ignored.
  - `ShowImage` uses `image` only (texture ignored).
- Runtime expects `res://vn/compiled/{scriptId}.res`, produced by the VN storage compiler:
  - JSON: `res://vn/scripts_json/*.json`
  - `.res` output: `res://vn/compiled/{script_id}.res`
