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
Phase 0 from `README.md`:
- Fixed tick game loop
- Time scaling (pause / fast forward)
- Rectangular overworld bounds

### Phase 0 acceptance criteria
In a runnable Godot scene:
- A **tick counter** advances from a **fixed timestep** (not frame-dependent).
- **Pause** yields zero ticks.
- **Fast-forward** increases tick rate by discrete multipliers.
- A visible **rectangular overworld bounds** is rendered (placeholder debug draw).

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
  - Controls (Phase 0):
    - Space: pause/unpause
    - `[` / `]`: slower/faster time scale
- **Tests** (core logic): from repo root:

```bash
dotnet test
```

## Determinism notes (Phase 0 scope)
- The simulation advances in fixed ticks produced by an accumulator-based clock.
- Real-time `delta` only determines *how many* fixed ticks to execute; simulation state updates should only happen inside ticks.
- A **catch-up clamp** limits max ticks per frame to avoid spiral-of-death under hitches.

