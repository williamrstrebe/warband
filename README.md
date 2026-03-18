# Overworld Army Sandbox Prototype

Technical prototype focused on validating the **core systemic gameplay loop** of an overworld army sandbox (Mount & Blade-like).

The project prioritizes **simulation, emergence, and testability**.  
All visuals are placeholders (colored shapes, simple colliders).

**CURRENT STATUS: finished phase 4.1, starting next step.**

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

# Technical Prototype Roadmap

This document describes a **technical-first prototype roadmap** for building an overworld army sandbox game (Mount-and-Blade-like structure) using **placeholder visuals and fully testable incremental steps**.

The objective is to validate **systems, simulation, and emergent gameplay** before investing in visuals, content, or polish.

All visuals are placeholders:

- World map = simple 2D rectangle with solid fill
- Player / AI parties = geometric shapes with colliders
- Towns / POIs = colored squares
- Encounters = modal menus
- Battles = auto-resolve formulas

The prototype must remain **playable at every phase.**

---

# ⭐ Phase 0 — Core Foundation (Time + Loop)

## Step 0.1 — Game Loop + Time Scale

**Goal:** deterministic simulation tick.

### Implement

- Fixed timestep simulation (ex: 10 ticks/sec)
- Pause / 1x / 2x / 5x speed
- Global time counter

### Test criteria

- Objects move the same distance regardless of FPS  
- Speed multiplier changes simulation rate correctly  
- Pause freezes AI + player logic  

---

## Step 0.2 — Basic World Bounds

**Goal:** create a navigable overworld container.

### Implement

- World = simple rectangle
- Clamp positions inside bounds
- Debug grid optional

### Test

- Player cannot exit map  
- AI cannot exit map  
- Movement along edges stable  

---

# ⭐ Phase 1 — Player Party Movement

## Step 1.1 — Click-to-Move Navigation

### Implement

- Player entity (circle collider)
- Target position on click
- Constant speed movement
- Stop threshold

### Test

- Player reaches clicked point reliably  
- Multiple clicks update target smoothly  

---

## Step 1.2 — Movement Speed Model (First Systemic Variable)

Add:

- BaseSpeed
- PartySizePenalty

Example:
speed = baseSpeed / (1 + partySize * k)


### Test

- Increasing party size visibly reduces speed  
- Speed affects arrival time measurably  

---

# ⭐ Phase 2 — Roaming AI Parties (Core Emergence Begins)

## Step 2.1 — Random Walk AI

### Implement

- Spawn N red circles
- Pick random destination inside map
- Move → idle → pick new destination

### Test

- No AI freezing  
- Uniform map coverage  
- No boundary jitter  

---

## Step 2.2 — Detection Radius System

Add:

- Circular detection collider
- Player + AI both detectable

### Test

- Draw debug radius  
- Log when entities enter/exit  

---

## Step 2.3 — Simple Behavior Switch

AI states:

- Wander
- Chase player if inside radius
- Flee if weaker

Requires:

- StrengthValue property

### Test

- Strong AI chases  
- Weak AI flees  
- State transitions stable  

---

# ⭐ Phase 3 — Encounter Trigger System

## Step 3.1 — Collision Encounter

### Implement

- If colliders overlap → pause world  
- Show simple modal:  
  - Fight  
  - Auto-resolve  
  - Flee (speed roll)

### Test

- World freezes during encounter  
- Resume works  
- No repeated trigger loop  

---

## Step 3.2 — Auto-Resolve Combat v1

Simple formula:
power = troopCount * troopTier * morale


Random modifier ±20%

Outcome:

- loser loses % troops  
- morale penalty  
- winner gains loot value

### Test

- Results statistically consistent  
- Larger army wins most fights  

---

# ⭐ Phase 4 — Party Logistics Layer

## Step 4.1 — Troop Recruitment Stub

Add towns (blue squares).

Entering town:

- recruit +5 troops button  
- cost deducted

### Test

- troop count increases  
- speed decreases  
- strength increases  

---

## Step 4.2 — Wage Tick System

Every X hours:
gold -= troopCount * wage


### Test

- Bankruptcy triggers troop desertion  
- Desertion reduces party size  

---

## Step 4.3 — Morale System v1

Morale affected by:

- recent victory +  
- starvation –  
- outnumbered –

Morale affects:

- flee chance  
- combat power  

### Test

- low morale causes more auto losses  

---

# ⭐ Phase 5 — Map Content Density

## Step 5.1 — Static POIs

Add:

- ruins  
- camps  
- neutral caravans  

Each = different interaction menu.

### Test

- Player can farm gold via exploration  
- Travel decisions become meaningful  

---

## Step 5.2 — Procedural Party Spawn

Spawn rules:

- bandits near wilderness  
- patrols near towns  
- caravans between towns  

### Test

- emergent hotspots appear  
- roads feel safer  

---

# ⭐ Phase 6 — Faction Ownership Layer

## Step 6.1 — Settlement Ownership Data Model

Each town:

- factionId  
- garrisonStrength  

Map overlay colored by faction.

### Test

- ownership visible  
- factions trackable  

---

## Step 6.2 — AI Army Objectives

Armies periodically choose:

- defend
- attack nearest enemy town
- escort caravan

### Test

- map frontlines form  
- towns change hands  

---

# ⭐ Phase 7 — Player Strategic Progression

## Step 7.1 — Contract System

Town offers:

- hunt bandits
- escort caravan
- raid enemy

Rewards:

- gold  
- reputation  

### Test

- directed travel emerges  
- player stops random wandering  

---

## Step 7.2 — Reputation → Political Unlocks

Thresholds unlock:

- larger recruit pool  
- ability to join faction  
- ability to capture town  

### Test

- progression stages clearly felt  

---

# ⭐ Phase 8 — Capture Mechanics

## Step 8.1 — Siege Auto-Resolve

If player power > garrison:

- town ownership changes  
- player installs governor (simple stat buff)

### Test

- map color changes  
- AI reacts (retaliation attacks)

---

# ⭐ Phase 9 — Pressure Engine

## Step 9.1 — War Cycle Generator

Every X days:

- factions declare wars  
- spawn invasion armies  

### Test

- player cannot remain passive forever  
- world state changes without player  

---

# ⭐ Phase 10 — System Balancing Tools (CRITICAL)

Before any graphics:

### Implement

- debug panel:
  - spawn party
  - add gold
  - change morale
  - force war
- simulation speed x20

### Test

- ability to run “AI-only world” and observe emergent behavior  

This is **the most important step for sandbox validation.**

---

# ⭐ Final Prototype Validation Goal

The prototype is considered successful when:

- Player can survive 30–60 minutes sandbox session  
- Meaningful decisions exist:
  - chase / flee  
  - recruit / save gold  
  - join war / trade  
- Map changes without scripting  
- Snowball and collapse both possible  

At this stage the project has achieved:

**A real systemic overworld army sandbox core.**
