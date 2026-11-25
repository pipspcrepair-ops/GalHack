> Read-only audit of proto/batch2 at f20ac912 (25 Nov 2026), by an audit agent. The director spot-checked the worst findings: ArmsHost.cs:110 (orbit has no `Planet`), Spaceflight.Fold.cs:473, and `Progress.Earned` with no subscriber. The rule it proposes is in [ARCHITECTURE.md](ARCHITECTURE.md) section 8. Companion to [offworld-audit.md](offworld-audit.md) and [structure-audit.md](structure-audit.md).

# Silent-failure audit: where a missing or wrong system quietly does nothing

- **Branch:** proto/batch2 at f20ac912, worktree G:\GalHack-wt\batch. Read only, no runs. Paths are under `prototypes/slice/src` unless marked.
- **Companions:** docs/offworld-audit.md (OW #n) and docs/structure-audit.md (SA xx). Their findings are not repeated. They are cited where a silent failure hides one.
- **Sure:** C = confirmed by reading, L = likely, U = unsure.

## What was counted

| Pattern in slice/src | Count | Method |
|---|---|---|
| `?.` uses of any kind | 878 | grep `\?\.[A-Za-z]` |
| `?.Method(` calls | 382 | grep `\?\.[A-Z]\w*\(` |
| of those, event or hook `?.Invoke(` | 158 | |
| on effects (Sfx, Dust, Debris, Fx, Sounds) | 65 | |
| on game or zone systems (Places, Weather, Wildlife, Bridge, Hands, Visor, Arms, Builder, Shots, Bunker, Audio, Bikes, Edits, Water, Planet, Streamer, Light, Physics, Sky, Spaceflight, Orbit, Fauna, Town, Sim) | 145 | |
| on static libraries (`Decor.Shared`, `ItemIcons.Shared`) | 14 | |
| early `if (... == null / is not {}) return` | 292 | most are ordinary data checks; the ~25 on systems are sorted below |
| `catch` blocks | 17 | all read |
| `GD.PrintErr` / `PushError` / `PushWarning` | 23 | all read |
| `public event` declared with no `+=` anywhere in src | 13 | script over every event |

### Sort of the 224 system and effect `?.` calls (65 + 145 + 14)

| Class | About | What they are |
|---|---|---|
| (a) legitimately optional | ~95 | `Weather.Sim` / `Weather.Bridge` (no air: no weather); `Wildlife.Sim` (orbit, airless); `Zone.Planet?`, `Sky?`, `Light?`, `Streamer?`, `Water?`, `Edits?` read in orbit, which has no planet; `Memory.Fauna?` / `Memory.Town?` (a place with no creatures or no people); `p.Rig?` (a person not drawn); `Spaceflight?`, `Orbit?`, `Audio?` (start screen); `ItemIcons.Shared?` (UI cache before setup) |
| (b) hides a system that should always exist | ~105 | `Zone.Sfx?` ×34 (made by every zone's `_Ready`, never null; the real fault is *which* zone's, see R1); `Zone.Dust?` ×11 and the tool's `Debris?` ×5 (null on every zone but the starter); the tool's `Sfx?`, `Fx?`, `Arms?`, `Arms?.Sounds?`, `Builder?`, `Places?`; `Game.Hands?`, `Visor?`, `Shots?`, `Arms?`, `Places?` ×10 (all made in `OnPlanetBuilt`, so always there in play); `_zone.Physics?.Rules.Hover ?? DefaultRules`; `Decor.Shared?` ×8 (a data library that should be required) |
| (c) unsure | ~25 | `Bunker?` ×10 (one hand-wired structure, SA Root E); `Zone.Light?.LightAt ?? 1f` for creatures and `Zone.Streamer?.DrawnGround` (fine while orbit has no life, wrong once SA G1 adds space fauna); `Edits?` in orbit (fine now, wrong once asteroids are dug, OW Root 1); `_places?.Zone` in Town (tests make towns without places) |

---

## Root causes, with the (b) cases and the worst of (3) and (4)

### R1. A system's absence and a world's "not ready yet" are the same null

`SurfaceZone.Planet` is null both while a world is being made *and* for good in an orbit zone. `OrbitZone._Ready` replaces the base `_Ready` and never runs `Arriving`, and `Planet` has a private setter (Surface/SurfaceZone.cs:62, Orbit/OrbitZone.cs:159). So every `if (Zone?.Planet == null) return;` written to mean "wait for the world" also means "never run in space". Systems that follow `Game.Zone` (the ones the off-world audit called fine) are switched off by it in orbit.

| # | Where | What breaks silently | Sure |
|---|---|---|---|
| 1 | Arms/ArmsHost.cs:110 | `_PhysicsProcess` returns when `Zone.Planet == null`. In orbit, and in any later zone without a planet (an asteroid field), the shot pool, effects and NPC arms (`People.Tick`) never step. Rounds fired hang where they left the muzzle and never hit. This is a second stop behind the node being a starter child (OW "checked and fine", vacarms). Reparenting alone won't fix weapons in orbit. | C code, L play |
| 2 | Vessels/ForceFields.cs:62 | Same guard. ForceFields reads `ZoneOf = () => Zone` (the current world), so OW lists it as fine. In orbit the whole pass stops: fields never regrow after hits, `Structure.DoorFields` never follows power (so a cabin's air state is frozen as it was at the crossing), and door field meshes are never shown or hidden. | C code, L play |
| 3 | Vessels/BlockDoors.cs:166 | Same guard on the door slide and shut pass. OW #4 proposes "point `Zone` at `Game.Zone`" as its fix. With this guard still in place, doors would stop sliding entirely in orbit. The guard is part of #4's root. | C |
| 4 | Construction/BuildTool.Whole.cs:149, Vessels/Vessel.Mode.cs:222 (`Edits == null`), Game/Game.Scanner.cs:65 | Flush-to-ground and the scanner quietly skip with no planet. That's right for open space today, and wrong for asteroids (OW Root 1, SA G2). | C, (c) |
| 5 | Hidden coupling: Arms/ArmsHost.cs:1069, Game/Game.cs:783, Player/SleeperTool.cs:338, 379, 451 | `Zone.Dust?.Puff(p, Zone.Planet.Grit(p), …)`. `?.` skips evaluating the arguments, so these only avoid a NullReferenceException on `Planet` because Dust happens to be null wherever Planet is. Give the orbit a Dust (OW #12's fix) and these crash. | C |

**Legitimate (a) in this group:** PoiPlaces.cs:127, 144, RuinLoot.cs:36, Hulks.cs:61, Underworld.cs:57, PlaceFauna.cs:599 and SkyLight.cs:91. These are world-owned systems waiting for their own world, where "not built yet" is the real meaning.

### R2. Player systems are wired to the starter once, so another world's copy sits unwired and inert

OW #6, #9, #12 and #14 list the wiring. These are the silent stops it causes, where the symptom is worse than the audit states:

| # | Where | What breaks silently | Sure |
|---|---|---|---|
| 6 | Surface/WildlifePatch.cs:438 (`Refill`), WildlifePatch.Haunts.cs:97, WildlifePatch.Signature.cs:167 (`Own`), WildlifePatch.Lead.cs:23 | Each returns on `Player == null`. `SetPlayer` is called only on the starter (Game/Game.cs:347). On every other world, creatures never refill as you move, haunts are never made, owned kinds never patrol, and no lead is taken. Only what `SetWorld` put down round the landing ever exists, so a moon or another star's world goes empty as you walk. OW #6 says only "don't target you". | C |
| 7 | Game/Game.cs:329, Vessels/Vessel.Hulk.cs:121, Vessels/VesselFx.cs:87, Surface/SurfaceWeather.cs:652, Surface/DeepSite.cs:440-441, Places/PoiPlaces.Damage.cs:120 | `Zone.Dust` is a public field set on the starter only. Every world-owned system on another zone loses its dust and sparks, not just the player's (OW #12 lists the player side): hull lift-off crumble, thruster dust, tornado dust, deep-site sparks and wall collapse. | C |
| 8 | Game/Game.cs:317 (`Tool = new SleeperTool(...) { Sfx = Zone.Sfx }`), BuildTool `_zone.Sfx?` ×11 | The tool and build tool hold the starter's `Sfx`. Off-world the zone is parked (`ProcessMode = Disabled`), so its `AudioStreamPlayer3D` voices don't play. Every dig, place, clank and ore sound is silent with no error. `?.` was never the guard here: Sfx is never null. The wrong one is. Same root as OW Root 1. | L (playback in a disabled node per Godot 4 process rules) |
| 9 | Surface/SurfaceWeather.cs:34, 57, 506; Surface/SurfaceZone.cs:57; Places/PoiPlaces.cs:32, Hulks.cs:30, RuinLoot.cs:22, SkyTraffic.cs:31 | Hooks default to working-looking stand-ins: `Observer = () => Vector3.Zero`, `Enclosed = () => false`, `Shelter = _ => false`, `Focus = () => Vector3.Zero`. An unwired world centres its weather, streaming and places on the origin and treats you as unsheltered, instead of failing. This is the mechanism behind OW #9 and #10. | C |

### R3. Required collaborators are optional fields

| # | Where | What breaks silently | Sure |
|---|---|---|---|
| 10 | Player/SleeperTool.cs:53-60 | `Places`, `Debris`, `Sfx`, `Fx`, `Arms` and `Builder` are public fields set after construction (Game.cs:317-339, Game.Places.cs:75), then called with `?.` 30+ times. A tool made by another path (a second sleeper in co-op, or a test fixture) works with no sound, no impacts, no town provocation (:588), no kill confirm (:605) and no deco salvage (:709). Nothing reports it. | C |
| 11 | Places/Place.Deco.cs:36-45 (`Decor.Shared`) | If the decorations library fails to load, it pushes one warning and returns null for the rest of the game. Every place keeps bare generator props, the build tool's deco mode gives nothing (BuildTool.Deco.cs:161), and deco HP falls back to 120 (SleeperTool.cs:680). It is shipped data, so it should be a hard failure. | C |
| 12 | Vehicles/HoverBike.cs:440 | `_zone.Physics?.Rules.Hover ?? DefaultRules`. Physics is never null, but a missing `Hover` block in the physics data silently uses `new HoverData()` code defaults. | C |

### R4. Errors are logged and play goes on as if it worked (3)

| # | Where | What breaks silently | Sure |
|---|---|---|---|
| 13 | Orbit/Spaceflight.Fold.cs:473 | **Worst of (3).** On return to a star, a kept structure whose blueprint no longer loads (`errors.Count > 0`, even a single warning) gets `PrintErr` and `continue`. The structure and every item it held (`items`) are gone, and the crossing goes ahead. That is item loss with only a console line. | C |
| 14 | Orbit/Spaceflight.cs:677, Orbit/Spaceflight.Jump.cs:251 | The ledger's item-conservation audit (`Ledger.Audit`: totals before vs after a crossing or jump) only prints each problem. Loss or duplication found by the game's own check is not refused, rolled back or shown. Compare `Refused()` at :670, which does refuse. | C |
| 15 | Places/Place.Deco.cs:79, Places/HullVessel.Finish.cs:143, Places/Town.cs:513, Places/Town.Rooms.cs:77, Places/PlaceParts.cs:86 | `catch (Exception) { continue; }` around the deco planner, hull dressing and domestic species, with no log at all. A generator bug shows only as undressed rooms, hulls without fittings or farms without animals. Tests can't see it either. | C |
| 16 | Places/PoiPlaces.cs:312, Places/Hulks.cs:69, Places/Place.cs:419, Places/Town.cs:367, Places/Town.Runs.cs:94 | Generator or meshing task faults go to `PushError`, and the place, hulk, mesh group or route is dropped (`Down[site] = new List<Vessel>()` records the hulk as built with nothing in it). In play the place is just missing. It depends on tests failing on PushError (U whether the runner does). | C code, U tests |
| 17 | Legitimate: Game/AudioSettings.cs:59 and 70, Boot/ResourceMount.cs:75-76, Game/GameSettings.cs:148, Ui/ItemIcons.cs:264, Orbit/ZoneLedger.cs:160-227 (refuses and restores the record, and the caller says so) | These are IO and settings, where a fallback is the right behaviour. | C |

Refusal strings standing in for a fix: only "NO BUILDING IN ORBIT", "SALVAGE · WRECKS ONLY" and "NOT IN SPACE" were found, and they are already in OW #26, SA H5 and SA's "right as they are" list.

### R5. Events nobody listens to, or that stand in for a missing link (4)

| # | Where | What it means | Sure |
|---|---|---|---|
| 18 | Items/Progress.cs:105 `Earned` (no subscriber) + Ui/Visor.cs:85 `Xp()` (no caller) | **Worst of (4).** The "+3 XP ore" line at the upper right never shows. XP is earned and only seen in menus. Dead wiring between two finished halves. | C |
| 19 | Places/PoiPlaces.Interact.cs:327-335 `CoreDestroyed` | Its doc says "the place is nobody's now", but no one subscribes and nothing else changes ownership when a core goes. Shooting out a faction core removes the block and changes nothing else. | L |
| 20 | Places/Town.cs:1252 `TurnedHostile`, Town.Standing.cs:55 `StandingChanged`, Game/Game.Gestures.cs:23 `Gestured`, Player/SleeperTool.cs:79 `MassHit`, Construction/BuildTool.cs:90 `DeviceSet`, Items/Lootables.cs:85 `Removed`, Places/SkyTraffic.cs:55 `Stopped` (trade seam), Places/Hulks.cs `Set`, Game/Game.Start.cs:130 `Woke` | Unused hooks. The work is done by direct calls next to them. Harmless, but against "tight code". Delete them, or wire the one with a purpose (`Stopped` waits on trading). `Dealt` and `Landed` are used only by tests. | C |
| 21 | shared/Bestiary/Sim/Arming.cs:13-22 (`Reach`, `Fire`, `BlowDamage`, `Masked` statics) | If unset, armed creatures fall back to melee (Attacks.cs:185) with no sign. That's right for the bestiary's own tests, wrong in the game. Already a co-op item (SA Root A "one per game"). | C, (a) at the library line |
| 22 | Orbit/OrbitZone.cs:175-181 | The stand-in Weather and Wildlife "so the game layer runs unchanged", and `Loot` without `BodyAt`, `Settle` or `DrawnOf`. Covered by SA G1. It is the model of the stand-in habit: an empty system that looks wired. | C |

---

## Worst five

1. **ArmsHost.cs:110**: `Zone.Planet == null` stops every shot, effect and NPC weapon in orbit. Reparenting won't fix it. (R1, C code)
2. **ForceFields.cs:62** (and BlockDoors.cs:166): the same guard freezes fields, door fields and the cabin air flag in orbit, and blocks OW #4's planned fix. (R1, C code)
3. **WildlifePatch.cs:438 + Haunts:97 + Signature:167**: `Player == null` off-world, so no refill, haunts or patrols, and other worlds empty out as you walk. (R2, C)
4. **Spaceflight.Fold.cs:473**: a kept structure that fails to load is skipped with its contents. Item loss, console line only. With the conservation audit only printing (Spaceflight.cs:677, Jump.cs:251). (R4, C)
5. **Progress.Earned / Visor.Xp**: the XP feedback is built on both sides and never joined. (R5, C)

---

## Proposed rule

**1. A world has a fixed set of systems, always.** Every zone (surface, orbit, later an asteroid field) is built with:
- Physics and Host
- Sfx
- Effects (Dust, Debris, Gibs)
- Loot (with all three hooks)
- Weather
- Wildlife
- Light
- Places (may hold no places)
- the vessel and device lists

They are made by the zone base class, in its constructor or `_Ready`, from one path that `OrbitZone` also runs. They are typed non-nullable and assigned once. No game code assigns a zone's system (`Zone.Dust = debris` goes).

**2. "No such thing here" is a fact and an empty system, never a null.**
- A vacuum's weather is a WeatherSim that reports calm. A lifeless world's wildlife has zero species. Space's Places has none.
- Facts are named (`HasAir`, `HasLife`, `Ground(body)`; SA S12 and S15), and code branches on the fact, not on a null.
- A null-object is allowed only when it is the true answer for that place. It is never allowed as a stand-in for wiring that wasn't done: a default lambda like `() => Vector3.Zero` or `_ => false` is banned.

**3. "Not ready yet" is a state, not a null.**
- A zone has `Ready`. Its systems are not processed until it is, because the zone parks them.
- So no system tests `Planet == null` to mean "wait". `Planet` (or `Ground`) is a question about the body, asked where it matters.

**4. Optional must be declared.**
- slice/src turns on `#nullable enable`, with nullable warnings as errors.
- Only members typed `T?` may be absent, and each `T?` carries a one-line reason ("null on the start screen", "a person not drawn").
- Required collaborators (the tool's Sfx, Arms, Builder and Places; a WildlifePatch's player set) are constructor parameters, not settable fields. The player-owned set travels as one object (SA S1).
- An event with no subscriber in src is deleted.

**5. A missing required system fails loudly.**
- **Tests and debug builds:** the accessor throws `MissingSystemException(zone, system, caller)`. The test runner also treats any `GD.PushError` as a failure.
- **Catches:**
  - Generator and meshing exceptions are never caught bare (`catch (Exception) { continue; }` goes).
  - At one boundary per place or site build, release builds catch, push an error, count it, and mark the site failed. Tests rethrow.
  - IO and settings keep their fallbacks.
- **Release:**
  - Log once per (world, system) with PushError and a dev notice line.
  - Then degrade only cosmetic systems (sound, dust).
  - For anything that holds items or blocks (fold restore, ledger audit), **refuse the action and keep the record**, as `ZoneLedger` already does for a crossing, never continue with a loss.

**6. Merge review greps:**
- `?.` on a member not typed `T?`
- `if (Zone?.Planet == null) return`
- a default stand-in lambda on a hook
- `catch (Exception)` without a rethrow in tests
- an event with no `+=`
