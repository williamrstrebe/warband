# Overworld Army Sandbox Prototype

Technical prototype focused on validating the **core systemic gameplay loop** of an overworld army sandbox (Mount & Blade-like).

The project prioritizes **simulation, emergence, and testability**.  
All visuals are placeholders (colored shapes, simple colliders).

---

## Core Goals

- Real-time overworld traversal
- Roaming AI parties with simple strategic behavior
- Encounter → combat resolution loop
- Party logistics (troops, morale, wages)
- Faction territorial simulation
- Emergent sandbox gameplay
- Fully testable incremental development

---

## Tech Principles

- Fixed timestep simulation
- Deterministic behavior where possible
- Data-driven systems
- Minimal UI / placeholder graphics
- Every step must result in a playable state
- Debug tooling early

---

## Prototype Roadmap (Streamlined)

### Phase 0 — Simulation Foundation
- Fixed tick game loop
- Time scaling (pause / fast forward)
- Rectangular overworld bounds

### Phase 1 — Player Party
- Click-to-move navigation
- Movement speed model (party size penalty)

### Phase 2 — Roaming AI Parties
- Random wandering behavior
- Detection radius system
- State switching: wander / chase / flee

### Phase 3 — Encounter System
- Collision-triggered encounters
- Simple modal interaction
- Auto-resolve combat formula

### Phase 4 — Party Logistics
- Town hubs (recruit troops)
- Wage tick system
- Basic morale model

### Phase 5 — Map Content Density
- Static points of interest
- Procedural roaming party spawn rules

### Phase 6 — Faction Simulation
- Settlement ownership data model
- AI army objectives (attack / defend / escort)
- Territorial changes on the map

### Phase 7 — Player Strategic Progression
- Contract / mission system
- Reputation unlocking mechanics

### Phase 8 — Capture Mechanics
- Siege auto-resolve
- Settlement ownership transfer

### Phase 9 — World Pressure Engine
- Periodic war declarations
- Invasion army spawning

### Phase 10 — Debug & Balancing Tools
- Spawn / edit stats / force events
- High simulation speed
- AI-only simulation mode

---

## Validation Criteria

Prototype is successful when:

- Player can play a sandbox session ~30–60 minutes
- Meaningful travel decisions exist (fight / flee / recruit / trade)
- AI factions dynamically change territory
- Economy creates pressure (wages, losses)
- Both snowball and collapse scenarios are possible
- Emergent hotspots and conflicts appear without scripting

---

## Future Extensions (Post-Prototype)

- Tactical battle layer
- Advanced AI strategy
- Economy simulation (trade routes, supply)
- Diplomacy / politics
- Save system
- UI/UX improvements
- Visual polish

---
