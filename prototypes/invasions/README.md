# Prototype: base invasions

Proves the invasion loop. The Vharn Dominion attacks a player base with troops, but only while someone is home. The attack comes from a nearby enemy base (or by ship if none is in range), is announced at 10, 5 and 1 minutes, lands crewed vehicles a few hundred metres out, and fights toward the core through the walls. Loot is never at risk.

![Warning](screenshots/warning.png)
![Arrival](screenshots/arrival.png)
![Firefight](screenshots/firefight.png)
![Offline](screenshots/offline.png)

## How it works

| Layer | Where | What |
| --- | --- | --- |
| Director | `src/Director` | Pure C#, seeded, simulated clock. Scheduling, the presence rule, source choice, the warning timeline and siren state, force composition, outcomes. |
| Base model | `src/Model` | Pure C#. Block grid with hit points, the shared storage network, spill crates, core and power, vehicle crews, fire control, player health, the breach planner. |
| Scene | `src/World` | Builds everything in code. `InvasionController` feeds the director (presence, base stats, invader counts) and turns its events into warnings, sirens, landings and withdrawals. |
| Combat seam | `src/Combat` | `IDamageable`, `IInvaderTroop`, `IInvaderVehicle`, `ICombatFactory` and `IAssaultPlanner`, with simple **placeholder** soldiers, APCs, dropships and navigation behind them. |

The rules it enforces:

- **Presence.** The countdown to the next invasion only runs while a player is in the base's zone. If everyone leaves for 60 s, an incoming invasion is called off and an assault withdraws.
- **Sources.** The nearest active Vharn base within range sends the invasion. If none is in range, it comes by ship (if the server allows ship raids), less often. Destroying or hijacking a base stops its invasions, and calls off any invasion it has incoming.
- **Warnings.** These fire at exactly T-10, T-5 and T-1 minutes on the simulated clock, however coarsely the clock is stepped. The first names the attacker, the source base and the bearing. The second puts the force on long-range scans. The third starts the sirens, which sound until the last invader is down or gone.
- **Scaling.** Threat is stored goods value, plus block count, plus turrets. Troops are scaled by that threat and by the players present, then capped by the source. A small outpost faces a squad; a fortress faces an army.
- **Outcomes.** If the core falls, the base goes offline (no power, turrets dead) and the invaders withdraw to their vehicles. Placing a new core brings it back with every surviving block. All invaders down or gone with the core standing is a victory.
- **Loot.** Storage is one shared inventory across all of a structure's containers. A wrecked container's contents spill into a crate that never despawns, and E puts them back into storage. Invaders never take anything.
- **Crews.** Every vehicle is block-built and crewed by a driver or pilot plus a gunner per gun. Kill a gunner and his gun goes quiet. Kill the driver and the vehicle stops; its crew bail out when the force leaves.
- **Fire.** Damage is never reduced or capped. Instead, a hostile that starts on a player fires warning shots: they land close but can't hit. Its spread then tightens to a baseline over 2 s, and losing sight starts that over. The baseline comes from a shooter class by target class matrix and the weapon. Rounds are projectiles with travel time (capital guns 280 m/s, small-craft guns 600 m/s).
- **Navigation (placeholder).** Obstacles are inflated by the agent's radius on the grid. Walls and doors are passable at a breach cost, and invaders hold and break them rather than grinding. Routes are smoothed only where a full-radius line is clear. Agents separate from each other and avoid things locally. A stuck agent re-paths at 1.5 s, side-steps at 2 s and hops to the nearest free cell at 2.6 s.
- **Siren.** A synthesised WW2 mechanical siren (`src/Audio/SirenSynth.cs`): two rotors a minor third apart winding up and down in a slow wail for the alert, and a steady all-clear when the invasion ends. No recorded audio is used.

## Play it

From the repo root, in PowerShell:

```powershell
.\tools\godot.ps1 -Project prototypes/invasions -Play
```

| Key | Action |
| --- | --- |
| WASD, mouse | Walk and look |
| Shift / Space / hold Ctrl | Sprint / jump / crouch |
| Left mouse | Fire (placeholder rifle) |
| F | Man or leave a turret (the turret AI fights on its own otherwise) |
| E | Put a spill crate's contents back into storage |
| I | Start an invasion now; press again to skip to the next warning |
| T | Fast time: the clock runs 60x, so 10 minutes of warnings take 10 s |
| P | Place a new core when the base is offline |
| K | Destroy the garrison's core (or shoot it out, 600 m north-east) |
| Esc | Free the mouse |

Try I then T, watch the warnings come in, then man the north-east turret.

## Data

Every tunable lives in `data/`:

| File | What |
| --- | --- |
| `server.json` | Invasion frequency, jitter, ship raids on or off, fast-time scale, seed |
| `invasions.json` | Warning timeline and messages, presence grace, source rules, scaling formula, tiers, source kinds, vehicles and their guns, troop roles, approach distances, planner cost |
| `fire_control.json` | Warning shots, accuracy ramp, accuracy grades, the shooter by target matrix, weapons (spread, projectile speed, damage, rate, homing) |
| `base.json` | Base layout (ASCII), block hit points, container capacity, items and their values, starting goods, siren and light positions |

## Test it

```powershell
.\tools\test.ps1 -Project prototypes/invasions -Scene res://tests/InvasionTest.tscn
```

Exits 0 if all 85 checks pass, 1 if any fail (about 2.5 minutes, mostly the soak).

- **Director:**
  - No invasion is scheduled without a player home.
  - Warnings fire at exactly T-10, T-5 and T-1, even with uneven clock steps.
  - The siren runs from T-1 to the end.
  - The force grows with stored value, size, defences and players.
  - The nearest active base is the source.
  - A destroyed or hijacked source stops its invasions; they then come by ship, or stop if ship raids are off.
  - The same seed replays the same invasions.
- **Base model:**
  - The item total in storage plus crates is unchanged by wrecked containers.
  - Core loss takes the base offline and withdraws the invaders; a new core restores exactly the surviving blocks.
  - Crew deaths silence guns and stop vehicles.
  - The planner breaches a wall into the core room.
- **Fire control:**
  - The first shots at a player always miss.
  - Spread reaches its baseline by 2 s.
  - A line-of-sight break resets the ramp.
  - The matrix and weapon modifiers hold, and projectile speeds come from data.
  - Damage is never scaled.
- **Siren:** the alert wails, and the all-clear holds steady and then runs down.
- **Scene (real nodes, headless, faster than real time with 1/60 s steps):**
  - Crouching shrinks the capsule.
  - In a live APC, a dead gunner silences his gun and a dead driver stops it.
  - A squad blasts through a wall into the core room.
  - Warning rounds never hit, and a capital round takes its flight time and kills outright.
  - A full fast-time invasion: warnings, sirens, landing, wrecked containers, core loss, withdrawal, all-clear and a new core.
  - Destroying the garrison turns the next invasion into a ship raid.
- **Navigation soak:**
  - 50 seeded forces among vehicle hulks, crates and rubble.
  - No invader is stuck for more than 3 s.
  - Every invader reaches the core room or a breach within 40 s.
  - `-- --soak=N` changes the count.

Other tools:

- `tests/Screenshot.tscn -- --view=warning|arrival|firefight|offline --out=file.png` renders a view (windowed, not headless).
- `tests/SirenPreview.tscn` renders 20 s of the siren (alert, then all-clear) to `audio-preview/siren-preview.wav`.

## Known limits

- **Combat is a placeholder until the boarding prototype's combat is merged in.**
  - The soldiers, APCs, dropships, turret AI, player rifle and `InvaderNavigator` are deliberately simple.
  - Swap them in through `ICombatFactory` (spawning), `IInvaderTroop` (`Assault`, `Withdraw`), `IInvaderVehicle` (crew, pickup, departure), `IDamageable` (anything shootable, with its target class) and `IAssaultPlanner` (routes on the block grid).
  - `FireControl` and `data/fire_control.json` are meant to stay and be used by the new soldiers.
- Base turrets are very poor against infantry by design (capital/base vs on foot), so the players have to fight the troops themselves. Vehicle guns knock turrets out.
- Only the director (and force composition) replays bit-for-bit from a seed. The fight itself runs on physics and is not deterministic.
- Single player. Presence counts the one local player, though the director takes any count.
- Vehicles move kinematically on flat ground and ignore terrain. A dropship whose pilot dies drops where it is.
- Destroyed blocks leave no rubble. Debris is cosmetic, and only crates and hulks become obstacles.
- The HUD and console text are debug-grade, and the long-range scan is drawn in 2D.
- The siren audio is synthesised; a licensed recording could replace it later.
