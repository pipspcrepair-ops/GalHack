# Structure audit: where the code solves one case instead of the rule

- **Branch:** proto/batch2 at f20ac912. Read only, 25 Nov 2026. Three audit agents read the code, and the director spot-checked the key claims (marked ✔).
- **Companion:** [offworld-audit.md](offworld-audit.md). Nothing here repeats it.
- **Sure:** C = confirmed by reading, L = likely, U = unsure. **Fix:** S / M / L.
- **No band-aids (owner, 25 Nov):** every fix below is a root fix. Each group's root fix is the job; the finding rows are what that fix has to make go away.

**The habit behind all of it.** New features were written beside the old ones, for one case, from whatever was at hand. They were never written as a rule that every case answers. The shapes it takes:
- One of something baked in: one player, one focus, one start world.
- State captured at setup: statics, references, subscriptions.
- The same job done two to nine ways.
- Identity written as strings, list order or object references.

---

## Severity summary

### 1. Wrong in play today
These need no co-op or saves to show.

| Finding | What the player gets | Sure | Fix |
|---|---|---|---|
| C-A1 ✔ | **Every moon and star world is built as a start world.** `PoiSiting.StartWorld` is set once and never cleared, so there is no faction ground and no defenders, and a start town is planned. Hidden today only because other worlds have no places (offworld #3). | C | M |
| C-A2 ✔ | **The new game's forced roll applies to every world.** For example, force a dead world and every moon is dead. | C | S |
| C-B1 ✔ | **Vessels never unsubscribe from the weather.** Every hulk leaks into the starter's weather, and the leak is hidden by a guard. Ships flown to another world never get lightning there. | C | S |
| C-B2 ✔ | **Shots in flight follow only the starter's frame shifts.** Rounds jump when another world re-centres. | C / L | S |
| B-B1 | **Blasts are written three times with different falloff and targets.** A fuel drum never hurts townsfolk, and animals blame *you* for a drum a raider shot. | C | M |
| B-B5 | **Townsfolk and wild people live by different rules.** Fire and gas never burn townsfolk, and tornadoes never move them. | C | L |
| B-B8 | **A coreless ground hulk is taken down 1:1 into your bag.** The same wreck in orbit pays about 33%. This is the biggest economy hole. | L | with offworld #26 |
| B-B14 | **Shelter is decided nine ways.** A hole 3 m deep counts as sheltered: wind stops and wildlife stops attacking. Under a POI roof creatures can't reach you, even through the open doorway. | C | M |
| B-B25 | **Tornado felling bypasses the one felling path.** A vessel landed on a tree a tornado takes stays in mid-air, which breaks "nothing floats". | C | S |
| B-B26 | **Overflow is lost or mislabelled.** What doesn't fit when you rob someone is lost, and a stranger ship's spilled cargo becomes "Your bag". | C | S |
| A-5.1 ✔ | **Loot is seeded by the cache's cell in its layout only.** Every copy of a layout, on every world, gives the same loot. | C | S |
| A-4.4 | **A creature carried across a patch move gets a new ID.** It rolls new drops and can give its gift twice. | C / L | M |
| B-B10 | **Wreck-site salvage progress resets** when the site streams out and back. This is a duplication route. | L | S |
| B-B16 | **Gravity is a constant in each system.** On a low-gravity moon the bike drops and creatures hop as on Earth. | C | S |
| B-B3 | **POI blocks ignore armour and damage type and never show damage.** Ship blocks crack, ruin walls just vanish. | C | L |
| C-D3 ✔ | **Guards are defined two ways.** A text role is checked 11 times and a job type once, so a guard made by job alone gets half the guard behaviour. | C | M |
| C-D2 | **Storm harm is chosen by a display label.** Any alien weather that isn't "heat" does cold damage. | C | S |
| B-B2 | **Player health has two units** (a share of max, and raw points). Raising max health weakens lightning and blasts. | C / L | S |
| B-B24 | **The visor, local map and world map show different things for one marker.** A mark made on another world shows on this world's local map. | C | M |
| C-C1/C2 | **Creative mode lives in three flags and is copied per world.** Machines made before you arrive charge Survival costs. | L | S |

### 2. Blocks co-op (DESIGN.md 14, 85)
These are fine for one player and can't hold two.
- **A-R1, one sleeper:** wildlife, towns, shots and death know exactly one player. Player 2 can't be targeted, can't earn XP and isn't hurt by shots.
- **A-R2, a world lives around one focus point:** terrain streaming, the wildlife patch and the frame shift follow one player. The frame shift teleports the other player a planet's width. Worlds are parked or freed by one player's location, and there is one `Away` and one `Orbit`, so two players at two stars can't exist.
- **A-2.7:** the local graphics slider sizes how much life exists for everyone.
- **A-R3, world things stored in one player's models:** replicators, bushes, pods and backpacks sit inside the suit's survival sim. Charts and travel state sit inside `Spaceflight`.
- **A-6.3, A-6.4:** the start has one live cryopod. Time is kept in several clocks that stop while a world is parked.

### 3. Blocks saves and star travel
- **A-R4:** `PoiSite` has no ID, and place memory is keyed by object and mixed with live nodes, so all place state is lost on reload. Counters, list-index keys for stars, bodies and POIs, and positions in a shifting frame make up the rest.
- **A-R5:** seeds are made without the world or site. Placement-order seeds mean a generator change reseeds every place.
- **A-6.1/6.2:** the galaxy is derived from the start planet's seed, and the home system is built by a different path from every other star.

### 4. Blocks content growth
Each of these makes new content silently fall back to a default.
- **C-D1:** hand kind is decided by item ID. A new vehicle or scanner does nothing in the hand.
- **C-D4:** place families are told apart by `flavour_` / `alien_` prefixes and named IDs.
- **C-D5:** a new space site kind, including the planned `hollow_rock`, becomes a station box.
- **C-G1:** orbit gets empty stand-in wildlife, so no space fauna and no Husks in derelicts, though `SpaceFaunaGenerator` exists.
- **C-H1:** turrets on generated warships never fire.
- **C-G2/G3:** the scanner refuses in orbit, and the cockpit impact warning is off in orbit.

---

## Root causes merged, and the root fix for each

Each one names the detailed groups below (A-, B-, C- sections). Roots S1, S3, S4, S6 and S15 extend the off-world audit's Root 1 and Root 2.

| # | Root cause | Groups | Root fix |
|---|---|---|---|
| S1 | **One player baked in** | A-R1 | A Sleeper object owns body, suit, pack, progress, tools, shots and death bag. The game holds a list plus "the local one". Simulation code takes the sleeper it acts for; only the view reads the local one. |
| S2 | **A world lives around one focus, and one player parks or frees worlds** | A-R2, A-3.5 | Each world holds a set of interest points, one per sleeper or vessel. Streaming, wildlife, weather hazards and places run per point. A world parks only when no point is on it. Simulation radius is separate from draw radius. Split the world manager out of `Spaceflight`. |
| S3 | **Per-world facts in process-wide statics** | C-A | `PlanetWorld.Build` takes a per-world context (start or not, forced roll, counters) made from the world key. Statics only for caches, loaded data and test switches. |
| S4 | **Joining a world is a hand-kept list in three places** | C-B | One `Attach(world)` / `Detach(world)` per world-held thing, holding every subscription. `Town.Open`/`Close` is the model to follow. |
| S5 | **Game-wide settings copied into worlds and machines** | C-C, A-3.6 | One game-level settings object, read when acting. No copies. |
| S6 | **No stable identity; seeds made without context** | A-R4, A-R5, A-6.1 | Every persistent thing gets an ID from world key plus generation key. Persistent maps are keyed by ID. Positions are canonical planet coordinates plus world key. One seed helper, `Seed(worldKey, siteId, purpose)`. The galaxy comes from a game seed, and the start is chosen in it. |
| S7 | **World things kept in player models** | A-R3 | Split the models: the survival sim keeps only vitals, and replicators, pods, bags and respawn devices go to the per-world record. |
| S8 | **Nothing defines "a thing that can be hurt"** | B-R1 | One `Harm` event and one spatial query. Each target kind has an adapter with its own armour, resistance and HP. Every source only raises `Harm`. One falloff, one health unit. |
| S9 | **Every take-apart has its own yield** | B-R2 (+ offworld #26) | Salvage is wear on a thing with HP and a bill. One formula, with the owner check inside it. Delete the other six formulas. |
| S10 | **Three forms of built thing** | B-R3 | One block-world interface for Structure and Place: cells, HP, doors, rooms, collapse and damage look. Doors, damage, salvage, shelter and caches are written once against it. Flavour ruins become Places. |
| S11 | **Shelter asked nine ways; the bunker hand-wired** | B-R4, C-E | One `Exposure(point)` query built from structure rooms, place rooms and terrain. The bunker is an ordinary ownerless structure. |
| S12 | **Ground, gravity and the bike outside the shared systems** | B-R5, B-R6 | One `Ground` service per body (`Top`, `Natural`, `Gravity`). The bike is a small hover body in the shared physics, and riding is one seated state. |
| S13 | **Queries, events, markers and entry points duplicated** | B-R7 to B-R10 | One `WorldQuery` with named masks. One `WorldEvent` that audio, AI hearing, visor cues and effects subscribe to. One `Markers(world, viewer)` source. The shared entry points become the only way in. |
| S14 | **Identity written as strings; "wreck" defined four ways** | C-D, C-F | Data fields and tags read by the code. One wreck state on the structure. |
| S15 | **Zone-type tests and stand-in systems** | C-G | Ask about the place (air, ground or a diggable body, gravity, life). Orbit gets the same world-owned systems, made from what's in it. |
| S16 | **Placeholders the game relies on** | C-H | Each one gets an owner decision: build it or remove it. |

**Owner decisions (25 Nov, also in DESIGN.md under Tech tree):**
1. **Progress and tech: per player.** NPC factions have no tech tree.
   - `Progress` moves onto the Sleeper object (S1).
   - Each player's kills, discoveries and XP are credited to the sleeper who made them (A-1.3, A-1.4).
2. **Structures and bags: owned by the Sleepers faction.** `Building.DefaultOwner` stands for that faction, which is right.
   - Every sleeper's bag is "ours": shown on every teammate's compass and lootable by them.
   - Still needed: the bag records which sleeper died, for its label.
   - `Lootables.Spill` must take an owner, so a stranger's cargo is never "ours" (B-B26).
3. **Charts: shared by the faction.** Visited stars, marks and wrecks seen go to the faction record.
   - Each player keeps their own destination and chosen star (A-3.4).
4. **Town standing: per faction**, as it is now (A-1.8 is not a bug). One player's crime sets it for everyone.

**Suggested order** (all root work, after the design pass is brought to the owner):
1. **S3, S4, S5:** small, fix live bugs, and the rest builds on them.
2. **The world model:** offworld Root 1 with S1, S2, S7 and S15, designed together because co-op changes them all.
3. **Identity:** offworld Root 2 with S6, before saves.
4. **The unifications:**
   - S8 harm and S10 block world, which together carry S9 and S11.
   - Then S12 and S13.
   - Then S14 and S16.

**Stop it recurring:**
- The merge review flags:
  - a new static holding per-world state;
  - a `+=` without its `Detach`;
  - a second implementation of a job that has a shared path;
  - a string-ID compare;
  - an ID or seed made without the world key.
- Each brief names the shared path the work must go through.

---

# Detail

## Audit A: one-of assumptions and data without identity

Read-only, branch proto/batch2 (G:\GalHack-wt\batch). Paths are relative to `prototypes/slice/src` unless they start with `shared/` or another prototype. Nothing from docs/offworld-audit.md is repeated; where a finding extends one of its items, the item number is given.

Sure: C = confirmed by reading, L = likely, U = unsure. Fix: S / M / L.

There are six root causes. Root R1 and R2 belong under the off-world audit's Root 1 (the world of the thing). Roots R4 and R5 belong under its Root 2 (the world key). R3 and R6 are new.

---

### R1. `Game` is one sleeper. Per-player state hangs on game singletons

`Game` (Game/Game.cs:67-95) holds exactly one of each player-side system: `Survivor`, `Suit`, `Pack`, `Progress`, `Tool`, `Builder`, `Shots`, `Target`, `Hands`, `Torch`, `Stats`, `Opened`, `DeathBag`, `LinkedStructure`, `CoresInReach`, `LightEdit`. The sim systems reach for these directly. They don't ask "which player". `Game.Survivor` uses by file: ArmsHost 15, Spaceflight 14 (+6 Hands, +5 Seam), SliceAudio 11, Game 6, Visor 5, Screens 4, MapLevels 3, CreatureBodies 3, and others.

**Root fix:** a `Sleeper` (player) object that owns body, suit, pack, progress, tools, shots, death bag, the open container and the linked structure. `Game` holds a list of them plus "the local one". Sim code takes the sleeper it acts for (the shooter, the victim, the one in the seat). Only the view layer reads "the local sleeper". Ask the owner whether progress and tech unlocks are per player or per faction.

#### Deep (simulation): must become per player
| # | Finding | Where | What breaks in co-op | Sure | Fix |
|---|---|---|---|---|---|
| 1.1 | Wildlife has one `Player` target. `SetPlayer` replaces it. Attack, telegraph, seize and let-go events are filtered to `e.Target == Player`. Refill and retire spawn around `Player.Position`. | Surface/WildlifePatch.cs:65, 271-285, 354, 438-442, 560-562, 784, 803-806 | A second sleeper is never a target. Creatures can't see, bite or seize them, and no life spawns around them. | C | M |
| 1.2 | Deep sites (caves) wrap that one player in one `FrameTarget`. | Surface/DeepSite.cs:25, 116-117, 159-178, 204 | The same as 1.1, underground. | C | S with 1.1 |
| 1.3 | Towns react to one "you". They read `_places.Zone.Wildlife.Player` for hostility, kills, collisions, schedules and "far" culling (`_you`). | Places/Town.cs:549, 612, 646, 655, 1012, 1404; Town.Reactions.cs:186-195; Town.Colliders.cs:76, 124; Town.Aftermath.cs:8; Town.Water.cs:77 | Townsfolk shove, flee from and shoot only one player. People near the second player are culled as "far" and stand frozen. A kill by player 2 isn't credited as "yours". | C | M |
| 1.4 | Shots know one player. `owner == 0` means "the player". `PlayerThing` and `You` are static tokens. `HurtPlayer` hurts `Game.Survivor`. `SolidHit` skips `Game.Survivor.Aboard` for owner 0. NPC aim uses `AtYou(t) => t == Patch.Player`. | Arms/ArmsHost.cs:45, 410, 430, 442, 584, 829, 854-902, 996, 1170, 1270; Arms/SimArms.cs:119, 194 | Player 2's shots count as NPC fire: no XP, no "byPlayer" reactions, no confirm marker. Shots at player 2 can't hurt them. Your own shots pass through your ship only when you are player 1. | C | M |
| 1.5 | Death and aftermath are one body. `DeathBag`, `Body`, the Husk rising (`OnTurned` checks `was != Body`) and the creatures' gifts (`_given`) all live on `Game`. | Game/Game.Items.cs:251, 268; Game/Game.Aftermath.cs:37, 115-121 | A second death overwrites the first bag reference. The Husk-from-your-corpse event fires for only one player. | C | S |
| 1.6 | The crossing to orbit is the player's. `Carrier()` is `Game.Survivor.Seated` or `Aboard`. `WatchSurface` and `HoldAtCap` run only for that vessel. | Orbit/Spaceflight.cs:143-149, 309-330; Spaceflight.Warp.cs:67 | A vessel flown by player 2 (or by any NPC) never crosses, and is held at the cap forever. Crossings belong to vessels, not to "the player". | C | M |
| 1.7 | Structure ownership is a constant. `Structure.Owner == Building.DefaultOwner` ("player"). Bags have `Yours` (a bool) and `Owner` (a creature or null). | prototypes/building/src/Structures/Building.cs:90; Vessels/Vessel.cs:201; Construction/StructureArea.cs:52; Items/Lootables.cs:54-56 | Fine for one shared faction, but there is nothing to record which sleeper placed something or whose death bag it is ("Your bag" shows on everyone's compass). The owner has to decide: faction-owned or player-owned. | C | S |
| 1.8 | Place standing is "with you": one float per settlement. | Places/PlaceMemory.cs:56; Places/Town.Standing.cs:13-50 | It matches "one shared faction" if that's the intent. One player's crime sets everyone's standing. This is a design call, not a bug. | U | S |
| 1.9 | Survival vitals are built once from the start world. `new Suit(Zone, Zone.Data.Survival(), (int)Zone.Spec.Seed)`: the seed is the starter's, cut to `int`. | Game/Game.Start.cs:66; Player/Suit.cs:92 | Every player's survival rolls share the starter seed. The truncation can collide between worlds. | C | S |

#### Local view: rightly one per client
`Camera`, `Visor`, `Alerts`, `Notices`, `TabMenu`, `Console`, `MachineScreens`, `Debug`, `Hands` (the viewmodel), `Audio` (the listener), `ThirdPerson`, `FixedCamera`, `GridView`'s statics (Ui/GridView.cs:20-106), `ItemView.Focused`, `Game.Following`, and the map's pan and zoom. These stay single, but they must read "the local sleeper", never `Game.Survivor` as the sim's player.

**Mixed:**
| # | Finding | Where | Sure | Fix |
|---|---|---|---|---|
| 1.10 | `Torch` is a SpotLight on the local camera. Other players' torches would need a remote copy. | Game/Game.cs:90 | L | S |
| 1.11 | `SoundCues.Heard` is a static event with a position and no world. The visor's sound marker shows any cue from any world. | Game/SoundCues.cs:13-15; Ui/Visor.People.cs:34 | L (once several worlds run) | S |
| 1.12 | `PersonView.Fader` and `PersonView.Arrives` are static. The last-built patch's view wins for every world. | Surface/WildlifePatch.cs:195, 202; shared/Bestiary/View/PersonView.cs:41, 239 | L | S |

---

### R2. A world only lives where the one player is: one focus, one observer, one frame

The zone and its subsystems take one `Focus` (and the weather one `Observer`). Everything streams, simulates, culls, spawns and frame-shifts around that point. Zones the player has left are parked (processing off) and abroad zones are freed. That breaks co-op: two players on one world far apart, or on two worlds at once.

**Root fix:** a world holds a set of interest points (one per sleeper or vessel on it). Streaming, wildlife patches, weather hazard areas and place activation run per interest point (several patches per world, or a patch per point). A world parks or frees only when no interest point is on it. Floating-origin shifts become per view, or positions are kept in canonical planet coordinates (see R4.6).

| # | Finding | Where | What breaks | Sure | Fix |
|---|---|---|---|---|---|
| 2.1 | `SurfaceZone.Focus` is a single `Func<Vector3>`. It drives `Streamer.SetFocus`, the sky's longitude, valley fog, felling and `WrapFrame`. `EnterZone` sets `zone.Focus = Focus` and `zone.Camera = Camera` (the local player's). | Surface/SurfaceZone.cs:57, 569-577, 666-695; Orbit/Game.Orbit.cs:54-55; Game/Game.cs:245, 277-290 | Co-op: terrain and collision load around one player only. The second player falls through unloaded ground. | C | L |
| 2.2 | `ShiftFrame` moves every body by a whole planet when the one focus crosses the seam. | Surface/SurfaceZone.cs:666-695 | Two players on either side of the seam: whenever one crosses, the other is teleported a planet's width. | C | M |
| 2.3 | The wildlife patch is one moving window (`Origin`) around one player. `Swap` rebuilds it, and `SpawnReach.PatchHalf` sizes it. | Surface/WildlifePatch.cs:225-245, 343, 697-701 | Co-op: no living world around player 2 unless they stand inside player 1's patch. | C | L |
| 2.4 | The weather has one `Observer`. Hazards, strikes, `Sim.Here` wind and `WindFells` (felling trees, a world change) all happen around it. | Surface/SurfaceWeather.cs:33, 494, 620, 640; Game/Game.cs:361 | Co-op: storms fell trees and strike only around one player. Player 2 gets player 1's wind. Extends #9 (#9 is about the wrong world; this is about one observer). | C | M |
| 2.5 | Places, ruin loot, hulks and sky traffic each take one `Focus` (and one `Eye`). | Game/Game.Places.cs:48, 55-58; Places/PoiPlaces.cs:32; Places/RuinLoot.cs:22, 47-53; Places/Hulks.cs:30 | Co-op: buildings, caches and wrecks are made and kept only around one player. | C | M (with #3) |
| 2.6 | Parking and freeing follow the one player. `Park` stops a zone's processing ("all of it is about where you are"). `ParkHome` parks the whole start system on a fold. `FreeAbroad` frees a star's zones when you leave. There is one `Away` (the system you're in) and one `Orbit` (the current orbit). | Orbit/Spaceflight.cs:67, 481-497; Orbit/Spaceflight.Fold.cs:60, 85, 349-404; Orbit/Spaceflight.Worlds.cs:240-245 | Co-op: when player 1 folds away, player 2's world freezes or is freed under them. Two players at two stars can't be represented at all. | C | L |
| 2.7 | Client display settings size the simulation. `SpawnReach.Distance` and `DetailReach.*` are static, set from the local settings (Game.Settings.cs:119-120), and size the wildlife patch and its population ("how many more a patch holds"). | Surface/SpawnReach.cs:15-37; prototypes/planet-surface/src/Terrain/DetailReach.cs:21-46; Game/Game.Settings.cs:119-120 | Co-op: the host's graphics slider changes how much life exists for everyone. Two clients disagree. Sim radius and draw radius must be separate settings. | C | M |
| 2.8 | Wildlife population depends on the player's path. The roll is `Rng.For(Fauna.Seed ^ (Origin.X*31 + Origin.Z), "population")` (the patch origin), then refills from `++_refills`. | Surface/WildlifePatch.cs:366, 453; WildlifePatch.Haunts.cs:113; WildlifePatch.Homes.cs:108, 138; WildlifePatch.Signature.cs:193 | The same valley holds different animals depending on where the patch happened to recentre. After a save, or for a second player, the world isn't the same world. | C | M |
| 2.9 | Simulations run only in `Game.Zone`. ArmsHost, SimArms and MachineScreens follow `Game.Zone` (the audit marked them fine). That is right for one player, but in co-op a world with only player 2 on it gets no shot sim. | Arms/ArmsHost.cs (`Zone => Game.Zone`) | Co-op: player 2's shots aren't stepped. | L | M (with Root 1) |

---

### R3. World-owned things are stored inside player-owned models

Several things that stand in a world, or belong to the galaxy, are kept in an object that belongs to one player (the suit's survival sim, the flight controller). When a world is reloaded, or a second player joins, they are in the wrong owner.

**Root fix:** split the models. The survival sim keeps only one sleeper's vitals. Replicators, bushes, pods, backpacks and respawn devices move into the per-world record (under the world key). Charts and the galaxy's travel state go to the faction or the server, and `Spaceflight` keeps only one vessel's or player's travel.

| # | Finding | Where | What breaks | Sure | Fix |
|---|---|---|---|---|---|
| 3.1 | `SurvivalSim` (one per `Suit`) holds the world's `Replicators`, `Pods`, `Bushes` and food caches, all with a bare `Pos` and a `_nextId` counter. | prototypes/survival-vitals/src/Model/SurvivalSim.cs:59-68, 79, 96-102, 282, 444 | A replicator placed on the moon is in player 1's list with moon coordinates. Its marker shows at those coordinates on whatever world is current (#19 is the marker; this is the data). Player 2 can't see or use player 1's replicator. | C | M |
| 3.2 | `Death` holds `Backpacks` with a bare `Pos`, an id counter, and `DroppedAt` on the suit's clock. | prototypes/survival-vitals/src/Model/Death.cs:14, 28-34 | The same as 3.1. The bag's age runs on one player's clock. | C | S |
| 3.3 | Replicator positions sit in the zone frame and aren't shifted. `ShiftFrame` moves bodies, loot, devices and light, but not `Suit.Sim.Replicators` or `RespawnPoints`, and not the plain `Node3D` markers made in `SyncReplicators`. | Surface/SurfaceZone.cs:680-694; Game/Game.cs:963-982 | After a seam wrap the replicator's marker and its stored position are a planet's width off. Respawn at a tent past the seam lands a planet away. | L | S (falls out of R4.6) |
| 3.4 | `Charts` (visited stars, marks, wrecks seen, destination, chosen star) is one object on `Spaceflight`. Your own `Mark(x, z)` has no world (`on` is null, meaning the start world). | Orbit/Spaceflight.cs:73; Orbit/Charts.cs:54, 77-81; Ui/MapLevels.cs:236, 310 | A mark placed on a moon is recorded as a start-world mark. Co-op: is the chart shared (faction) or per player? Destination and chosen star are per player, marks and visits per faction. | C (no world) / U (sharing) | S |
| 3.5 | `Spaceflight` mixes the world manager (orbits, abroad systems, the ledger, `_systems`, charts) with one player's travel state (`EvaThrusting`, `HighestInAir`, `TicksHeldAtCap`, `_salvaging`, `_leftAt`, `Carrier()`). | Orbit/Spaceflight.cs:67-149, 300-330; Spaceflight.Hands.cs:31; Spaceflight.Fold.cs:60-90 | You can't have two travellers without two world managers. | C | L |
| 3.6 | The game mode is "the world's one setting". It is stored per zone (`WorldRules.Mode`), copied in on `EnterZone`, and the setter writes only the current zone. | Game/Game.cs:97-108; Orbit/Game.Orbit.cs:56; Surface/SurfaceZone.cs:114 | Changing the mode while away leaves other worlds on the old one until you enter them. It belongs to the server or the game. | C | S |

---

### R4. Stored things have no stable identity

Records are keyed by object reference, by an in-memory counter, or by list order, and positions are kept in a frame that shifts. None of it survives a save, a reload of a world or a crossing. (The off-world audit already covers ledger record IDs, respawn points, discovery, deposit and wreck IDs, and moon names. These are the rest.)

**Root fix:** every persistent thing gets an ID made from its world key plus a generation key (site seed, cell, species and spawn slot) or a persistent counter in the world record. Persistent maps are keyed by those IDs, never by references. Runtime caches keyed by reference stay, but are rebuilt from the IDs. Stored positions are canonical planet coordinates (wrapped) plus a world key, never frame coordinates.

| # | Finding | Where | What breaks | Sure | Fix |
|---|---|---|---|---|---|
| 4.1 | `PoiSite` has no ID, only `Name`. Place memory is `Dictionary<PoiSite, PlaceMemory>` by reference, and `Hulks.Down` and `_building` are keyed by `PoiSite` too. | prototypes/planet-surface/src/Poi/PoiSiting.cs:37-42; Places/PoiPlaces.cs:64-71; Places/Hulks.cs:37, 46; prototypes/planet-surface/src/Poi/FarLights.cs:105; prototypes/planet-surface/src/World/Infra/InfraNetwork.cs:396 | A save, or a remade world, gets new `PoiSite` objects, and every place's memory (doors, damage, loot taken, standing, mood) is orphaned. This is what makes #7's "POI memory lost" unfixable without IDs. | C | M |
| 4.2 | `PlaceMemory` itself is keyed by references (`Dictionary<LootCache, Lootable>`, `Dictionary<SecretRoom, float>`, `HashSet<SecretRoom>`) and holds live nodes (`Lootable`, `Town`, `PlaceFauna`, `DoorWays`, `Bodies` closures). | Places/PlaceMemory.cs:30-72 | It can't be saved or carried over as it stands. Persistent state and runtime state share one class. | C | M |
| 4.3 | Ruin caches opened are tracked in `HashSet<FlavourRuin>` by reference. | Places/RuinLoot.cs:27, 50-54 | After a remade world, every ruin's caches are made again with full loot (duplication, a case of #7). | C | S |
| 4.4 | Creature IDs come from `WildSim._nextId++`. A creature carried across a patch swap is respawned with `sim.Spawn(... asIs: true)` ("the same one") but gets a new `Id`. Its drops are seeded by `c.Id`, and `_given` (gifts already handed over) is keyed by the old `Creature` reference. | shared/Bestiary/Sim/WildSim.cs:447; Surface/WildlifePatch.cs:289-330; Game/Game.Aftermath.cs:37, 95, 107 | After the patch moves on, the "same" animal rolls new drops and can give its gift again. No creature (a named giant, a tamed beast) can be remembered across a save or a world reload. `Spared`, `_lastAttack`, `Slots` and `_settled` (all reference-keyed in shared/Bestiary/Sim) drop too. | C (new ID) / L (double gift) | M |
| 4.5 | Other counter IDs: `Lootables._nextId` (per zone, so the same numbers on every world), `World.SpillCrate` `_nextId`, `PhysicsWorld` body IDs, `Charts._nextMark` (mark1, mark2 and so on, one game-wide count), the bunker's `"bunker"` respawn ID (one fixed string). | Items/Lootables.cs:107; shared/Items/Core/World.cs:90; shared/Physics/Core/PhysicsWorld.cs:96, 165; Orbit/Charts.cs:79; Game/Game.Start.cs:72-75 | Collide across worlds and after a load. Each needs a world-keyed persistent ID once it's saved. Physics IDs can stay runtime. | C | S each, with the root |
| 4.6 | Positions are stored in zone-frame coordinates. Frames move by whole planets on `ShiftFrame`, and nothing marks a stored position as wrapped or canonical. This applies to `Lootable.Position`, `Replicator`, `RespawnPoint`, `Backpack`, `Pod`, and `Herd` and `Pack` homes. | Surface/SurfaceZone.cs:676-695; Items/Lootables.cs:265 | A save made after a seam crossing holds positions off by a planet's width. Two players in one world (R2.2) can't share a frame. | C (no canonical form) / L (visible bug) | M |
| 4.7 | Chart place keys are list indices: `poi{i}` is the index into `planet.Pois`. | Orbit/Spaceflight.cs:208-213, 242 | A generator change that reorders POIs remaps every charted place. Use the site's ID (4.1). | C | S |
| 4.8 | Star and body keys are list indices. A star is its index in `Galaxy.Stars` (`_systems`, `Charts.Visited`, `Abroad.Star`). A body key is `"b"+i` or `s{star}b{i}`, its index in the system model's body list. A missing body falls back to `"b-1"`, so all such bodies share one key. | Orbit/Spaceflight.Worlds.cs:67-71; Orbit/Spaceflight.Fold.cs:47, 90; Orbit/Charts.cs:60-64 | Extends #15 (moon names) to every star and body: reordering in generation remaps saves, and "b-1" merges unrelated bodies. | C | S |
| 4.9 | Place door state is keyed by an index (`DoorsOpen[int]`, `DoorDamage[int]`, `DecoTaken[int]`). Person run seeds use `People.IndexOf(p)`. | Places/PlaceMemory.cs:32-41; Places/Town.Runs.cs:118 | Stable only while the generator's order is. Acceptable if the order is seeded. Flag it as save-format debt. | L | S |

---

### R5. Seeds and hashes made without the world, site or star

**Root fix:** one seed-derivation helper, `Seed(worldKey, siteId, purpose, ...)`. Every content roll goes through it, and a review check bans raw `Hash.Cell(cell, k)` or `rng.NextU64()` as the seed of anything persistent.

| # | Finding | Where | What breaks | Sure | Fix |
|---|---|---|---|---|---|
| 5.1 | Loot counts and weapon rolls are seeded by the cache's cell alone: `Hash.Cell(c.Cell, ++k*31)` and `Hash.Cell(c.Cell, 0xA77u)`. The cell is local to the layout, with no site seed and no world. | Places/PoiPlaces.cs:509-511; Places/RuinLoot.cs:71-72 | Every copy of a layout, on every world and at every star, gives the same cache the same counts and the same weapon. Players learn "the vault in a type-X bunker always has the rifle". | C | S |
| 5.2 | Site seeds come from sequence order. `SpawnSeed = rng.NextU64()` during placement (and the same in flavour ruins and themes). | prototypes/poi-generator/src/Gen/Placement.cs:209; Gen/Themes.cs:208, 269; prototypes/planet-surface/src/World/Infra/FlavourRuins.cs:660 | Deterministic for one generator version. Any change to placement order reseeds every place, so a save's place memory then points at different contents. It should hash world key and site position or slot. | L | S |
| 5.3 | Wreck seeds use two schemes. The start orbit seeds wrecks with `StarSystem.StarKey` alone, other worlds with `Hash.Combine(model.StarKey, root.Seed)`, and `FrameSeed` uses the starter's `Spec.Seed` for home and `Galaxy.SystemSeed` elsewhere. `Spaceflight.SystemSeed(PlanetSpec)` is unused. | Orbit/Spaceflight.cs:275, 290; Orbit/Spaceflight.Worlds.cs:167; Orbit/Spaceflight.Fold.cs:310 | The home star's orbit is generated by a different rule than the same star's other worlds. Delete the dead method. | C | S |

---

### R6. The start world is the root of the galaxy, and the start has one sleeper

These are new beyond the audit's "home special case": the galaxy is derived from the starter planet, not the planet from the galaxy, and the start is built for one person.

**Root fix:** a game (server) seed makes the galaxy. The start star and start body are chosen in it, and the start world is made like any other body (`StarProfile.Of(galaxy, star)`, then the body). The bunker's pods are the joining sleepers' wake points, each with its own respawn record.

| # | Finding | Where | What breaks | Sure | Fix |
|---|---|---|---|---|---|
| 6.1 | The galaxy is `Galaxy.Make(spec.Seed)`, the starter planet's seed. The home star is then picked to match the starter's star kind. | prototypes/planet-surface/src/World/PlanetWorld.Galaxy.cs:22-48 | The galaxy is a function of the start world. A server that wants a fixed galaxy with several starts, or a new start in the same galaxy, can't have one. | C | M |
| 6.2 | The home system is built by a different path. Home is `Game.Surface.Planet.StarSystem` (from `SkySystem.For(spec)`), while every other star is `StarProfile.Of(galaxy, star)`. `Begin` special-cases `star == HomeStar`. | Orbit/Spaceflight.Fold.cs:69-82, 164-165; prototypes/planet-surface/src/Sky/SkySystem.cs:88-94 | The home star's system doesn't match what the galaxy would generate for that star (two truths for one star). | L | M |
| 6.3 | The bunker has four cryopods, but one is `Mine` ("YourCryopod", the only one alive). Spawn, the intro and the respawn point `"bunker"` use only `CryoFront`. `Suit.Sim.Pods[0]` is the only ration locker. | Start/Bunker.cs:71-73, 132-135, 680-686; Game/Game.Start.cs:64-79; Game/Game.Items.cs:72 | Co-op: a joining sleeper has no pod, no wake point and no locker. | C | M |
| 6.4 | There is no galaxy clock. Time lives in each zone's `SkyRig.Days` (advanced in each zone's `_Process`, so it stops while parked, and is copied over on jumps and moon crossings), and also in `Suit.Sim.Time`, `WildSim.Time`, each `Town._clock` and `GameStats.Seconds`. | prototypes/planet-surface/src/Sky/SkyRig.cs:34-39, 467; Surface/SurfaceZone.cs:577; Orbit/Spaceflight.Jump.cs:279; Orbit/Spaceflight.Moon.cs:110; Places/Town.cs:665 | Extends audit wider-risk #3. Co-op: two players on two worlds run two clocks that drift apart, which breaks "celestial bodies are real". A parked world's sun stops. One server-owned clock is needed, and each world's day and season come from it. | C (several clocks) / L (drift) | M |

---

### Worst five
1. **R2.6:** worlds are parked or freed by where the one player is, with one `Away` and one `Orbit`. Co-op at two stars can't be represented.
2. **R2.1/R2.3:** one `Focus` per world drives terrain streaming, the wildlife patch and the frame shift. Player 2 has no ground or life.
3. **R4.1/R4.2:** `PoiSite` has no ID, and place memory is reference-keyed and mixed with live nodes. All place state is lost on save or reload.
4. **R1.4/R1.1:** shots (`owner == 0`, static `You` and `PlayerThing`) and wildlife (`Player`) know one sleeper. Player 2 can't hurt or be hurt properly.
5. **R5.1:** loot and weapon rolls are seeded by the layout cell only. Every copy of a layout gives identical loot on every world.

---

## Audit B: one job, several rules

- **Branch:** proto/batch2, worktree G:\GalHack-wt\batch. I only read the code. Nothing was run.
- **Paths:** relative to `prototypes/slice/src` unless the path names another root.
- **Left out:** everything in docs/offworld-audit.md (the wrong-world issues, and #26 orbit hands salvage). Where a finding here touches one of those, it says so.
- **Sure:** C = confirmed by reading, L = likely, U = unsure. **Fix:** S / M / L.

---

### Root 1: nothing defines "a thing that can be hurt"
Each kind of thing keeps its own hit points, units and falloff. Each source of harm picks by hand which things it reaches. New sources (weapons, then hazards, then weather) were written next to the old ones. None were written as one path that every target answers.

**Unified design.** One `Harm` event: amount, damage type, point, direction, radius, source (who and what), and tags (fire, force, passes the shield). It goes through one spatial query for "what is here". Each target kind gives an `IDamageable` adapter: structure block, place block/door/piece, salvage site, plant, terrain cell, creature, person, player, worm mass and field. Each adapter applies its own armour, resistance and HP store. Weapons, the tool, hazards, lightning, debris, rams, lunges and falls only raise `Harm`; they never pick targets. Falloff is defined once, and player health uses one unit.

| # | Finding | Where | How the rules differ / what the player notices | Sure | Fix |
|---|---|---|---|---|---|
| B1 | **The blast is written three times** | Arms/ArmsHost.cs:1011-1073 (weapon), Game/Game.Places.cs:96-126 (place hazard), Vessels/VesselDamage.cs:98-105 plus Game/Game.cs:811-822 (lightning) | **Weapon blast:** falloff `1-d/r`. Hits creatures, town people, the player (via HurtPlayer: self-harm rule, Explosive class), fields, vessels, places, plants and a crater. **Hazard** (fuel drum, blasting crate): falloff `1-0.75d/r`. The player takes ×0.5 as raw health through `Suit.Hit`. It hits no town people, plants, ground, force fields or worm masses. It calls `Wildlife.Damage` with the default `byPlayer=true`, so every creature caught blames *you* even when a raider set it off. **Lightning:** reaches vessels, fields and the player only (a flat radius with no falloff, skipped under cover). Creatures, people, trees and POIs never take it. **DebrisImpact** reaches the player only. *Player sees:* a fuel drum kills nobody in a crowd of townsfolk, but a grenade does. Animals turn on you for a drum a raider shot. | C | M |
| B2 | **Player health has two units** | ArmsHost.cs:1003, Game.cs:769, Game.Husks.cs:141,197 (amount/100 × max) vs Game.cs:818 (lightning), Game.Places.cs:102 (hazard) (raw points); Suit.Take (physics, share of integrity) | Bites, weapons and Husk holds treat damage as a percentage of max health. Lightning and hazards treat it as raw health points. The two agree only while `Health.Max == 100`. Gear or tech that raises max health makes lightning and blasts weaker than bites. | C (code) / L (if max changes) | S |
| B3 | **Place blocks and structure blocks are two damage models** | Places/PoiPlaces.Damage.cs:89-99, PoiPlaces.Interact.cs:171-198 vs building/src/Structures/Damage.cs:51-77, Vessels/VesselDamage.cs | **Structure blocks:** armour, resistance by DamageType, `Block.Hp`, damage buckets drawn as cracks, `DamageFx` sparks, chips and marks, repair, split into wrecks (`Connectivity.SplitOff`). **POI blocks:** raw damage against `BlockInfo.HitPoints`, with no type or armour. Damage is stored only in `PlaceMemory.BlockDamage` and never drawn. There are no chips or marks. A lost block takes away whatever no longer reaches ground (`Loose`), with no wreck. Doors, pieces and secret walls each have their own HP dictionary. *Player sees:* a ship wall cracks as you shoot it. The same iron wall in a ruin looks untouched until it vanishes, and energy and kinetic rounds do the same to it. | C | L (part of Root 3) |
| B4 | **Ramming is written twice, and the bike rams nothing** | Surface/CreatureBodies.cs:253-274; Places/Town.Colliders.cs:163-207; Vehicles/HoverBike.cs (none) | The two copies are near-identical, with these differences. Creatures use relative velocity and go through `sim.Damage`. Town people use the vessel's velocity alone and take `p.Health.Current -=` directly. That bypasses `Town.Struck` (damage class, `byPlayer` and the town's reaction). The bike has no physics body, so it passes through creatures and people and takes no crash damage. A vessel that hits a POI wall takes crash damage and the wall takes none (places are never sent a crash). | C (copies, bike); L (POI wall) | M |
| B5 | **People live in two sims with different rules** | Town.Person (Places/Town*.cs) vs figure creatures in WildSim (raiders, wanderers) | **Damage:** `Town.Struck` (DamageClass) vs `Wildlife.Damage` (HitScale, no class). **DoTs:** `Effects.Add` is only called for creatures (ArmsHost.cs:855), and `Alive()` is false for a Person (ArmsHost.cs:1255), so fire and gas never burn townsfolk. **Weather:** gusts and tornado throws only reach the surface patch's creatures (Surface/SurfaceWeather.cs:800-838; no town code there). **Hazard blasts** skip town people (B1). **Giving up goods:** robbing a townsperson gives a slice of the *start kit* (Game/Game.Gestures.cs:60-61). A held-up wild person tosses a share of their own `BodyDrops` (Game.Aftermath.cs:91-111). *Player sees:* a flamethrower sets a raider burning while a townsperson walks it off. A tornado flings the raider and leaves the townsfolk standing. | C | L |
| B6 | **Terrain and tell HP live in the tool** | Player/SleeperTool.cs:313-323 (`_cellHurt`, forgotten after 30 s), :375-387 (`_tellsWorked`); ArmsHost.World.cs:76-77 (crater) | The tool wears terrain cells by material durability and pays ore per share. Weapon craters take the cells at once and pay nothing, so a grenade on an ore body destroys the ore. Tell progress is kept on the tool, not the world: the ledger and other worlds never see it. Half-dug cells heal after 30 s. | C | M |

### Root 2: every way of taking something apart has its own yield rule
Salvage grew one target at a time: place blocks, then pieces, then infra wrecks, then structure blocks, then orbit wrecks (#26). Each brought its own formula and its own refusals.

**Unified design.** Every take-apart is wear (Root 1's `Harm`, tool class) on a thing with HP and a bill. One `Salvage` rule pays `bill × share-of-HP-taken × rate(who owns it, tool tier)`, with fractions carried in that thing's world record. "Ours: comes back whole" is one owner check in that rule, and so are a town's "provoked" and "hold not empty". Delete `SalvageSite`'s share counter, `Places.Work`'s per-type table, the orbit timer, and the separate `Wear`/`WorkPiece`/`Blocks` branches.

| # | Finding | Where | How the rules differ | Sure | Fix |
|---|---|---|---|---|---|
| B7 | **Seven yield formulas for one action** | SleeperTool.cs:690-728 (structure), :391-406 → PoiPlaces.Interact.cs:171-198 (place block), :208-245 (door/piece `Wear`), :247-264 (`WorkPiece`), :282-300 (`SalvageSite`), :427-466 (plants), :369-388 (tells); Orbit/Spaceflight.Hands.cs:97-162 (#26) | (1) Someone else's structure block pays as it wears: bill × ExtractShare × dealt/max. (2) A place block pays only when it breaks: a material table × a hand-written share per BlockType × ReturnRate, carried in PlaceMemory. (3) A place door or piece pays as it wears: bill × ExtractShare. The same piece hit by the *gather pulse* instead goes through `WorkPiece` and pays the whole bill × ReturnRate on breaking. (4) A wreck site pays in fixed shares every HitPoints. (5) Plants pay their biomass share as they wear. (6) Tells pay fixed ore per pulse. (7) In orbit: a mass timer, then `floor(bill × SalvageReturn)`. | C | M |
| B8 | **A coreless ground hulk comes back whole; the same wreck in orbit gives about 33%** (adds to #26) | SleeperTool.cs:683 `Ours => Core == null \|\| Owner == default`, :696-705 → BuildTool.Remove (1 item per block, BuildTool.cs:778-779); Hands.cs:112-117, 147 | `Ours` counts any coreless structure as yours. Hulks are set down coreless (Places/Hulks.cs:23, 149), so the tool takes each hulk block down into the bag 1:1. In orbit the hands refuse your own blocks and pay a third. The biggest economy gap between the two paths. | L (read; not played) | part of #26 |
| B9 | **A container block: orbit refuses, the ground spills** | Hands.cs:120-125 vs BuildTool.cs:794-796, VesselDamage.cs:192-195 | In orbit a full hold refuses salvage ("HOLD NOT EMPTY"). On the ground, taking a container down or blowing it up spills the overflow as a bag. | C | with B7 |
| B10 | **Wreck-site progress isn't remembered** | PoiPlaces.Interact.cs:75-100 (built with a full `Left` each time the site streams in; only `Emptied` writes `Felled`) | `SalvageSite.Damage/Taken/Left` live on an object made anew whenever InfraLayer stands the site (planet-surface/src/Infra/InfraLayer.cs:255). Take 3 of 4 shares, walk out of range and back, and it is full again: a duplication route. POI blocks keep their damage in PlaceMemory; structures keep it in the block. | L | S, with the per-world record (offworld #7) |

### Root 3: three ways to represent built things
Vessels, bases, hulks and the bunker are building's `Structure`. POIs are poi-generator's `BlockGrid` inside a `Place`. Flavour ruins and roadside wrecks are infra deco plus `SalvageSite`. Each library came with its own doors, damage, collapse, rooms and caches, and the slice wraps each one separately.

**Unified design.** One block-world interface that Structure and Place both implement: cell → block, HP, door state, rooms/sealed, collapse rule and a damage-look hook. Doors, damage (Root 1), salvage (Root 2), shelter (Root 4) and caches are written once against it. A flavour ruin should stand up as a Place of its own (it already spawns a `PoiInstance`, RuinLoot.cs:56), not as a crate drawing plus a SalvageSite.

| # | Finding | Where | How the rules differ | Sure | Fix |
|---|---|---|---|---|---|
| B11 | **Three door systems** (rules, not the off-world wiring of #4) | Vessels/BlockDoors.cs:78-160; Places/Place.Fixtures.cs:21-120 + Places/DoorWays.cs; Orbit/Spaceflight.Hands.cs:246-275 | **BlockDoors:** two mesh leaves slide; the block flips solid when they meet; doors shut by themselves past ShutRange; doors held shut stay shut; wrecks are skipped (`Kind == Wreck`, :95). **Place doors:** AnimatableBody leaves with a jammed state, folk shut them on a timer, a settlement turned hostile shuts yours, and they can be worn or broken. **Orbit "Force the door":** an instant `Building.Toggle` with no leaves and no auto-shut, on *any* structure door in orbit, your own included. Its prompt (Game.Items.cs:160) pre-empts every other E prompt. *Player sees:* the same door block slides on the ground and snaps open in orbit. A POI door can jam or be blasted out; a ship door can't. | C | M |
| B12 | **Cache-making is copied** | Places/PoiPlaces.cs:496-518 vs Places/RuinLoot.cs:61-79 | Line-for-line twins. The ruin copy drops `c.InVault` from the rarity roll, names every cache "Cache", and tracks what it made in a HashSet on the node rather than in a place memory. | C | S |
| B13 | **Two collapse rules** | PoiPlaces.Damage.cs:196-248 (`Loose`, which turns blocks to air) vs building `Connectivity.SplitOff` (VesselDamage.cs:189-190, which becomes a wreck) | When a POI wall's support goes, it vanishes in a puff. When a ship's support goes, the piece breaks off and falls as a wreck. The `Loose` doc comment points to `Structure.Floating` as "the rule", but the code is its own. | C | with B3 |

### Root 4: "Am I sheltered?" is asked nine ways
Each system (weather visuals, wind, the suit, lightning, creatures, cave spawns, bikes, physics) asked the question when it needed it, from whatever data it had.

**Unified design.** One `Exposure(point)` query on the world. It returns sky openness, roof overhead, enclosed room (sealed or not, whose) and depth below the real surface. It is built from structure rooms, place rooms and terrain in one place. Every consumer reads a field of it. The bunker is a structure, so it answers through structure rooms like any base.

| # | Test | Where | Rule |
|---|---|---|---|
| | `Game.Sheltered` | Game/Game.Places.cs:161-165 | 2.5 m below the *natural* heightfield, or under a POI roof. Ignores your own base's roof. |
| | `Game.UnderCover` | Game.Places.cs:171-177 | Sheltered, seated, aboard, or a raw Godot ray 20 m straight up hits World\|Hull. |
| | `Suit.Sheltered` | Game/Game.Start.cs:67 | `Bunker.Inside` only. |
| | `Suit.InSealedRoom` | Player/Suit.cs:211-234, 314 | A sealed room of building's Rooms, or the bunker. |
| | `Suit.ClosedPlace` | Game.Start.cs:68 | A POI with its doors shut (`Place.Closed`). |
| | `Weather.Enclosed` | Game/Game.cs:365 | Aboard, not on the hull, and `SkyLight.Stands`. |
| | `SkyAtEye` | Surface/SurfaceWeather.cs:60-67 | The light field's `EyeOpen`. |
| | Physics `IsSheltered` | shared/Physics/Core/PhysicsWorld.cs:274 | A base, parked, or in a frame. |
| | Cave `Indoors` | Game.Places.cs:64 | Any POI footprint, or the bunker. |
| | Creatures `Attackable` | Game.cs:1016 | `OnFoot && !Sheltered()`. |

**What the player sees (B14, C, M):**
- Under your own base's roof with walls open, lightning can't hit you (ray up), but wind still pushes you and creatures can still attack (Sheltered ignores structures).
- Under a POI roof, creatures can't reach you at all, even through the open doorway.
- In a hole dug 3 m deep in the open, the game counts you as sheltered: wind stops and wildlife stops attacking.
- Rain stops by the light field's rule, which none of the gameplay tests above use.

### Root 5: ground height and gravity are looked up per system

**Unified design.** One `Ground` service per world. `Top(x,z)` gives the real top surface (terrain edits, places, structures); `Natural(x,z)` is kept only for generation. `Gravity(point)` comes from `Zone.Physics`. Every system's own constants are deleted.

| # | Finding | Where | Sure | Fix |
|---|---|---|---|---|
| B15 | **Five "ground under me" lookups.** `GroundAt` (natural heightfield, before digging), `GroundTop` (a ray within 3 m of natural), `FloorUnder` (a ray 80 m down, any solid), the bike's own `SolidAt`/`SurfaceAt` rays, and Town's `Top`. Gameplay uses the *natural* one in places: shelter (B14), and the spill height for a destroyed vessel's cargo (VesselDamage.cs:195: in a cave or a pit the bag is placed up at the old surface). Visor markers lift to it too (Ui/Visor.cs:411). Paths: Surface/SurfaceZone.cs:380-412, Vehicles/HoverBike.cs:519-603, Places/Town.Bodies.cs:16, Items/Lootables.cs:280,331 | C | M |
| B16 | **Gravity constants per system.** Bike 12 (HoverBike.cs:105). Creature hops 18 (shared/Bestiary/Sim/Locomotion.cs:14). Spit 9 (Spitting.cs:29). Deep crawl 12 (DeepData.cs:167). Tornado throw 9.8 with an 18×0.6 tumble (SurfaceWeather.cs:823). Shot particles 9.8 (Player/ShotFx.cs:392). Only WildSim, town ragdolls, bags and the survivor read the zone's gravity. *Player sees:* on a low-g moon the bike drops like on Earth, and creatures hop as on Earth. | C | S |

### Root 6: the hover bike is a vehicle outside the vehicle system
It was built as "its own simple model, apart from the vessel code" (HoverBike.cs:13-20) to get the feel quickly.

**Unified design.** The bike is a small Hover-mode body in the shared PhysicsWorld, using the same `HoverController` it already borrows. So it gets crash damage, ramming (B4), the zone's gravity, weather through the one force path and shelter through the one query. Riding is one "seated on a body" state: a bike seat is a chair. `Seated` and `OnBike` become one.

| # | Finding | Where | Sure | Fix |
|---|---|---|---|---|
| B17 | The bike has its own integrator, gravity (B16), ground rays (direct `IntersectRay`/`CollideShape`, no WorldRay), shelter (`Weather.ShelteredAt`), weather force module, and no damage or ram. | Vehicles/HoverBike.cs:105, 325-339, 478, 519-603, 660-680 | C | M |
| B18 | Two ride states: `Survivor.Sit` and `Mount` are near-copies (Player/Survivor.cs:321-374), and every "riding?" check must test both. Many test only `Seated`. Game.Places.cs:63 (cave `OnFoot`) and CreatureBodies.cs:108 count a bike rider as on foot. `UnderCover` counts a seated pilot as covered but not a bike rider. Footsteps (Game/SliceAudio.cs:244) only skip `Seated`. | C (checks); L (what each one shows in play) | S |

### Root 7: rays and sight lines aren't one query
`WorldRay` was made for the player's look (cabin world plus zone, Ignored list). AI eyes, cover tests and per-object aiming were each written beside it.

**Unified design.** One `WorldQuery` per world, with named masks (`Sight`, `ShotStop`, `Cover`, `Use`). The cabin, the ignore list and the force-field emitters are handled once. Per-object aiming (bags, doors, bodies, people, plants) registers with it, so "what is under the crosshair" is answered once and in one order.

| # | Finding | Where | Sure | Fix |
|---|---|---|---|---|
| B19 | **AI line of sight uses different masks.** Wildlife sight and town sight: World\|Hull (Surface/WildlifePatch.cs:603-608, Places/Town.Combat.cs:508-513). Armed wild people: World\|Hull\|**Flora** (Arms/SimArms.cs:108). Flame cone: the shot pipeline (ArmsHost.cs:672). The bunker's: a heightfield walk (Start/Bunker.cs:389-399). *Player sees:* townsfolk shoot at you through a tree trunk a raider won't fire through. | C | S |
| B20 | **The tool and E aim through different tests.** E and the build tool use WorldRay. The tool's blast uses `Arms.Sweep`. Bags, doors, people, bodies and plants each have their own along-the-look test (Items/Lootables.cs:172, BlockDoors.cs:89, PoiPlaces `PersonAlong`, `Wildlife.Raycast`, FloraFinder). The E prompt checks them in a hand-made order (Game/Game.Items.cs:156-181). The orbit door check comes first, so it can claim E over a bag. `UnderCover`'s ray (Game.Places.cs:175) skips the cabin world and the ignore list. | C | M |

### Root 8: an event's sound, heard noise, visor cue and effect are four calls made by hand
Each system plays its own sound pool. The calls that let the AI hear it (`Wildlife.Noise`), the visor's sound marker (`SoundCues`) and the effects are separate, and whoever wrote the event chose which of them to make.

**Unified design.** One `WorldEvent` (kind, point, loudness, material, source) that the audio, AI hearing, the visor cue and the effects all subscribe to. Explosions, impacts, block breaks and digs each raise one event. One look per event kind.

| # | Finding | Where | Sure | Fix |
|---|---|---|---|---|
| B21 | **Hearing and cues are missed.** A hazard explosion plays a boom and a visor cue but never calls `Wildlife.Noise`, so creatures and towns don't hear a fuel drum go up (Game.Places.cs:96-126). The weapon blast calls Noise but no visor cue (ArmsHost.cs:1071). Thunder, crashes and vessel engines give neither. | C | M |
| B22 | **Four voice pools** with their own range, voice cap and loudness ceiling: planet-surface's `Sfx` (planet-surface/src/Fx/Sfx.cs:23-43, 120 m), `GunSounds` (Arms/GunSounds.cs:62), Bestiary's `ProceduralAudio` (shared/Bestiary/Audio/ProceduralAudio.cs:85-95, 90/40 m), and SliceAudio's own players (Game/SliceAudio.cs:136-197). Bus routing and the vacuum low-pass are shared; distance and voice stealing are not. | C | M |
| B23 | **Damage effects differ by target.** Structure hits get `DamageFx` (sparks, marks, chips, smoke). POI block hits get none (B3). Dug terrain gets `Debris.Burst`, but weapon craters get only a puff (ArmsHost.World.cs:77, ArmsHost.cs:1069). A weapon blast is `ArmsFx.Blast` + impact + puff; a hazard blast is five puffs, sparks and a tweened OmniLight (Game.Places.cs:114-125). The two explosions look different. | C | S to M |

### Root 9: each map and the visor build their own marker list
**Unified design.** One `Markers(world, viewer)` source with one set of inclusion rules (yours, found, scanned, live). The visor, the local map and the world map only filter by range and draw.

| # | Finding | Where | Sure | Fix |
|---|---|---|---|---|
| B24 | **Three lists, three rules for one marker.** Visor (Ui/Visor.cs:403-429): your own vessels with a seat, or bases, wrecks excluded; the bunker by `Zone == Surface`; only *your* death bag; map marks filtered by `ChartOf(zone)`. Local map (Ui/InfoSections.cs:165-192): every live creature, *every* non-cache loot (corpses and others' bags too), devices, every non-wreck vessel whoever owns it; the bunker by `v.IsRuin`; chart marks *not* filtered by world. World map (Ui/MapLevels.cs:319-335): all chart marks; `Surface.Vessels` including wrecks; no devices or bags. *Player sees:* a wreck shows on the world map, vanishes on the local map, and never shows on the visor. A mark made on another world shows on this world's local map. | C | M |

### Root 10: one entry point exists, but callers go around it
The shared paths (`SurfaceZone.Fell` "the one way a plant goes", `PlayerInventory.Add` plus `SetDown`, `Lootables.Spill`) exist. Nothing makes callers use them, or use them fully.

**Unified design.** Make the shared path the only way in. `Planet.Felled` is private to `Fell`. The inventory gets one `Receive(bill, at, from)` that always sets down what doesn't fit, so it can't be dropped. `Spill` takes an owner, so "Your bag" is only made for your things.

| # | Finding | Where | Sure | Fix |
|---|---|---|---|---|
| B25 | **Tornadoes fell trees around the one felling path.** `Uproot` and the torn tree call `_planet.Felled.Add` plus `Streamer.Fell` directly (Surface/SurfaceWeather.cs:749-750, 926), skipping `SurfaceZone.Fell` (Surface/SurfaceZone.Felling.cs:47-55). Wildlife isn't told, and whatever rested on the tree isn't woken: a vessel landed on a crown a tornado took stays in mid-air ("nothing floats"). planet-surface/src/Main.cs:419 is a third copy (the prototype's own). | C | S |
| B26 | **Overflow is dropped or mislabelled.** Robbing ignores what `Pack.Add` couldn't fit, so it's lost (Game.Gestures.cs:61). `BuildTool.GiveBack` adds to `Storage` directly instead of `Pack.Add` (Construction/BuildTool.cs:817-818). `Lootables.Spill` always makes or joins "Your bag" (Items/Lootables.cs:143-151): a stranger's ship's spilled cargo (VesselDamage.cs:195) and a restored container (Orbit/Spaceflight.Fold.cs:481) turn into *your* bag, marked on your map. | C | S |

### Smaller splits worth folding in
- **Two settings stores** (C, S): `user://settings.cfg` (Game/GameSettings.cs:22) and `user://audio-settings.json` (Game/SliceAudio.cs:39). Each has its own format and save timer.
- **Spawn reach** (L, S): the spawn distance setting (Surface/SpawnReach.cs) drives surface wildlife and person bodies. The Underworld stands at a fixed 90/150 m (Places/Underworld.cs:44) and ruin caches at 70 m (RuinLoot.cs:25).
- **Orbit shooting is cosmetic** (C, S; overlaps #26): `Spaceflight.Shoot` (Orbit/Spaceflight.Hands.cs:191-201) draws a bolt and a scorch and harms nothing. It doesn't use ArmsHost, so the tool's gather blast in orbit hurts no one and nothing.
- **Orbit claim** (C, S): `Claim` (Hands.cs:210-244) seats a core by its own code and calls `Pack.Storage.Remove` plus `Grid.Reconcile` by hand, rather than going through the build tool's core placement.

### The roots, ranked by how much they unify
1. **Root 1, harm:** B1-B6 and parts of B4/B5.
2. **Root 3, block worlds:** B3, B11-B13, and it carries Roots 2 and 4.
3. **Root 2, salvage:** B7-B10 plus #26.
4. **Root 4, shelter:** B14.
5. **Root 6, bike:** B4, B16-B18.
6. **Roots 7-10:** queries, events, markers, entry points.

All of these are the same habit as the off-world audit's root: a new feature was written beside the old one, for one case, and never as a rule that every case answers.

---

## Audit C: captured references, special cases, placeholders

- **Branch:** proto/batch2 at f20ac912, worktree G:\GalHack-wt\batch. Read only. No runs.
- **Paths:** under `prototypes/slice/src` unless marked otherwise.
- **Not repeated:** everything in docs/offworld-audit.md (zone captures in Game.cs setup, Suit, BuildTool, SleeperTool, HoverBikes, HullBlocks, the places stack, wildlife and weather hooks, Game.Surface uses, the compass, Spaceflight.Hands and its "NO BUILDING IN ORBIT", #1 to #26).
- **Sure:** C = confirmed by reading, L = likely, U = unsure. **Fix:** S / M / L as in the off-world audit.

---

### Root A: facts about one world are kept in process-wide statics

The world build (`PlanetWorld.Build`) reads "is this the start world" and "what did the new game force" from statics. The slice sets them once, for the starter. Every later world (moons through `SurfaceZone.Make`, other stars' bodies) is built off-thread with those statics still set to the starter's values.

**Root fix:** the build takes a per-world context (start world or not, forced roll, load counters) as an argument, made from the world key. Statics are kept only for pure caches, data loaded once, and test or debug switches.

| # | What breaks in play | Where | Sure | Fix |
|---|---|---|---|---|
| A1 | **Every world is built as a "start world".** `PoiSiting.StartWorld = true` is set before the starter is built and is never cleared. So every moon and every other star's body is profiled as a start world (`ProfileFor(spec, StartWorld)`) with no faction ground, no cores and no defenders. Its strikes are planned with the start flag, and a start town (`StartTown.Plan`) is planned by its landing site. Today other worlds get no places (#3), so the roll is mostly hidden. The roads and the start-town plan still run. Once #3 lands, every world will look like a quiet rim start. | Surface/SurfaceZone.cs:214; planet-surface/src/World/PlanetWorld.cs:243, 250; planet-surface/src/World/Infra/PlanetWorld.Infra.cs:44, 61; planet-surface/src/Poi/PoiSiting.cs:228, 261, 273, 295 | C for the code path; L for what play shows today | M |
| A2 | **The new game's forced roll applies to every world.** `WorldForce.Current` (depth, theme, fall, mid-collapse, eras from the start screen) is set once and read by every later `PlanetWorld.Build` and `PoiSiting`. Force a dead world at the start and every moon and star is forced the same way. | Surface/SurfaceZone.cs:216; planet-surface/src/World/PlanetWorld.cs:242; planet-surface/src/Poi/PoiSiting.cs:249 | C | S |
| A3 | **The load counters are reset by any world's build.** `PoiSite.Loading` is set true at the top of every `PlanetWorld.Build`, from a worker thread. It is set false only once, at the start (Game.Start.cs:106). After the first moon is made, `Loading` stays true for the rest of the game and the "must stay 0" counters (`UnaskedLoadBuilds`, `LoadLayouts`) count all play. They are diagnostics, so play doesn't break, but the check they back is wrong after the first moon. | planet-surface/src/World/PlanetWorld.cs:239-241; Game/Game.Start.cs:106; planet-surface/src/Poi/PoiSiting.cs:75-90 | C | S, goes with A1 |
| A4 | **Worker threads race on these statics.** Several bodies build in `Task.Run` (SurfaceZone.cs:230) while the main thread can write `StartWorld`, `WorldForce.Current` and `Loading`. | Surface/SurfaceZone.cs:217, 230 | L | goes with A1 |

**Statics that are fine** (caches with game lifetime, data loaded once, settings mirrors, test switches):
- **Data loaded once:** `GameData.Shared`, `OrbitRules.Shared`, `WarpRules.Shared`, `PlaceRules.Shared`, `Credits.Shared`, `Decor.Shared`, `DecoMaterials.Shared`, `SurfaceZone.PoiCatalog`, `HomeBook/EraBook/FinishBook.Current` (the last catalogue loaded).
- **Shaders, meshes and materials:** fine.
- **`ItemIcons.Shared`:** a UI cache for the life of one Game, cleared on exit (Game.cs:224, 313). Fine.
- **Settings mirrors:** `DetailReach` and `SpawnReach`. They are one game setting and apply on every world, which is right. The gap is the streamer radius (#10, already listed).
- **Node singletons:** `DecoAnimator` and `DecoShadows`, made under the tree root. Fine.
- **Test and debug switches:** every `Force*`, `Off`, `On`, `Enabled`, `Last*Ms`, `MaxSites`. Fine.
- **Hooks with no stored state:** `PersonView.Fader` and `Arrives`, `Weapons.GameDraws`. Fine.
- **One per game, a co-op risk only:** `Arming.Reach/Fire/BlowDamage/Masked` (closures on the one ArmsHost, SimArms.cs:61-64) and the `Selection` and `GridView` drag and click state (Ui/GridView.cs:20-106, static `ItemGrid` refs). They need to be per player when co-op comes.

---

### Root B: joining a world is a hand-kept list in three places, not one attach and detach

A vessel joins a world in `Vessel.Register` (Vessels/Vessel.cs:452-458). It moves in `Handover.Move` (Orbit/Handover.cs:33-65) and leaves in `SurfaceZone.Remove` (Surface/SurfaceZone.cs:534-543). Each place lists the steps by hand, and the lists already differ.

**Root fix:** one `Attach(world)` / `Detach(world)` per world-held thing, with every subscription inside it. Register, Handover and Remove call only those two.

| # | What breaks in play | Where | Sure | Fix |
|---|---|---|---|---|
| B1 | **Vessel weather subscription leaks and stays on the first world.** `Zone.Weather.Happened += Hits.OnWeather` has no `-=` anywhere. Hulks are stood up and dropped as you move (Vessel.Hulk.cs:70), so every hulk ever made stays in the starter weather's list for good. Each lightning strike runs through all of them, and freed vessels stay in memory. `VesselDamage.OnWeather` hides this with a guard (`_v.Zone.VesselOf(_v.Body) != _v`, VesselDamage.cs:100), which is a band-aid. `Handover` moves the stuck fields, the bridge and the tracking, but not this subscription. A ship flown to another world with storms is never hit by lightning there. | Vessels/Vessel.cs:456; Vessels/VesselDamage.cs:98-105; Orbit/Handover.cs:33-65; Surface/SurfaceZone.cs:534-543 | C | S |
| B2 | **Shots don't follow the orbit's frame shifts.** `ArmsHost._Ready` subscribes `Zone.FrameShifted` once, through `Zone => Game.Zone`, which is the starter at that point. The off-world audit lists ArmsHost logic as fine, but this one subscription is captured. Rounds and tracers in flight in orbit or on another world jump by the shift distance whenever that zone re-centres. | Arms/ArmsHost.cs:36, 77 | C for the code; L for what play shows | S |
| B3 | `SkyTraffic` subscribes `Zone.FrameShifted` once, and returns early on `Zone.IsOtherBody`. Goes with #3. | Places/SkyTraffic.cs:108, 153 | C | with #3 |

**Done right, as a model to follow:** `Town.Open`/`Close` (Places/Town.cs:428-452), the `Survivor.Zone` setter (Player/Survivor.cs:31-40), `Sit`/`StandUp` with `SeatLost`, and the jump's `Power.Jumped` (Orbit/Spaceflight.Jump.cs:47, 75).

---

### Root C: a game-wide setting is copied into each world and each machine

| # | What breaks in play | Where | Sure | Fix |
|---|---|---|---|---|
| C1 | **Game mode lives in each zone's `WorldRules` and is copied on entry.** `Game.Mode` reads and writes `Zone.WorldRules.Mode` (Game.cs:100-104), and `EnterZone` copies it into the new zone (Orbit/Game.Orbit.cs:56). Every constructor captures the `WorldRules` object of the zone it was made in: the Fabricator (Game.cs:315), devices (Vessels/Device.cs:81), vessel machines (Vessels/Vessel.cs:254) and the `BlockPlacer`. A zone you haven't entered yet keeps the default, Survival. So in a Creative game, machines on vessels or derelicts made in that zone before you arrive charge full costs. | Game/Game.cs:100-105, 240, 315; Orbit/Game.Orbit.cs:56; Vessels/Device.cs:81; Vessels/Vessel.cs:254 | L | S |
| C2 | **Three flags for Creative.** `Game.Mode` (UI), `Zone.WorldRules.Creative` (build tool, Sleeper tool, vessel damage) and the `Progress.Creative` copy (Items/Progress.cs:102, 239) can disagree after any change. | Construction/BuildTool.cs:135; Player/SleeperTool.cs:829; Vessels/VesselDamage.cs:214; Ui/BlueprintsSection.cs:243 | C | S, with C1 |

**Root fix:** game-wide settings (mode, and the owner's settings) sit in one game-level object that every system reads when it acts. No world or machine keeps a copy.

---

### Root D: what a thing is, written as an id or name string instead of a data field

**Root fix:** each of these becomes a field in the data (item use or hand kind, weather harm kind, person role, family or site traits as tags), and the code reads the field.

| # | What breaks in play | Where | Sure | Fix |
|---|---|---|---|---|
| D1 | **Hand kind by item id.** `"hover-bike"` → Vehicle, `"scanner"` → Scanner, `"meat"` → Consumable. items.json lists meat as category `raw`, and the code overrides it. A new vehicle, scanner or food item falls to `HandKind.Other` and does nothing in the hand. | Items/PlayerInventory.cs:263-271; data/items.json:36 | C | S |
| D2 | **Storm harm type comes from a display label.** `ExposureLabel == "heat"` means heat damage, and anything else means cold. Alien weather (acid, radiation, dust, the exoplanet set) would do cold damage and raise the Cold alert. | Player/Suit.cs:342-343 | C | S |
| D3 | **Two fields for a person's role.** The string `p.Role == "guard"` is used next to the enum `p.Job == PersonRole.Guard`, sometimes both together (Town.Placement.cs:82). A guard made by `Job` alone gets only half the guard behaviour: aims, pursuit, street watch, waiting at home, answers. | Places/Town.cs:538, 769, 1058, 1131; Places/Town.Placement.cs:82, 123; Places/Town.Space.cs:41, 425; Places/Town.Standing.cs:84, 93; Places/Town.Combat.cs:375, 383 | C | M |
| D4 | **Place families and world layers are told apart by id.** The `flavour_` id prefix decides crawlers and skips sky-traffic sightseeing. FlavourRuins checks both the `flavour` tag and the prefix. The `alien_` style prefix sets the alien theme and skips era reshaping. The layer ids `"dead_city_ash"` and `"silence"`, the families `"survivor_town"`, `"landing_clearing"` and `flavour_house/shed/shop`, and the part ids `"link"` and `"terminal"` are all named in code. A new family or layer with the same meaning is ignored. | Places/PlaceFauna.cs:83, 246; Places/SkyTraffic.cs:212; poi-generator/src/Gen/Eras.cs:145; planet-surface/src/World/Infra/FlavourRuins.cs:197, 363-364; planet-surface/src/World/Infra/CityPlan.cs:164; planet-surface/src/World/Infra/WorldDress.cs:296; planet-surface/src/World/Infra/StartTown.cs:130; planet-surface/src/Poi/PoiSiting.Types.cs:66, 321 | C | M |
| D5 | **Space sites are three hand-written shapes picked by kind and id substring.** `"field"` with an id containing `"pod"` gives Pods, `"field"` gives Field, and every other kind gives Station. Any new space-site kind (`hollow_rock`, asteroid families in space-pois.md) silently becomes a station box. | Orbit/Derelicts.cs:71-76 | C | M |
| D6 | **Prop drops in code next to a data table.** `"hoe"`/`"cane"`/`"umbrella"` drops are hard-coded, while `Rules.Carried` right below them already holds drops per role. | Game/Game.Items.cs:380-388 | C | S |
| D7 | Minor ids in code: the Fabricator's machine `"suit"` (Game.cs:315), the `"deco-block"` stand-in icon (Ui/ItemIcons.cs:53, 248), and the ledger type `"bunker"` (Orbit/Spaceflight.cs:723). | as listed | C | S |

---

### Root E: the starter bunker is wired in by hand, not a structure like any other

The bunker is a base vessel marked `IsRuin` (Start/Bunker.cs:594), plus about 25 calls to `Game.Bunker` across the systems. Each call gives it its own rule. The off-world audit covers its respawn (#1), the map marker (#16) and the cave rules (#20). These are the other special cases:

| # | What breaks in play | Where | Sure | Fix |
|---|---|---|---|---|
| E1 | **The suit's shelter check knows only the bunker.** `Suit.Sheltered = () => Bunker.Inside(...)` gives `RoomKind.Bunker`, a full shelter. Your own bases count only through sealed rooms, and no other ruin or cellar ever counts. On another world with air, the check runs at the starter's bunker coordinates (this ties to #2). | Game/Game.Start.cs:67; Player/Suit.cs:257-261, 314 | C | M |
| E2 | **`IsRuin` means "the bunker".** Any vessel marked `IsRuin` is drawn at `Game.Start` and labelled BUNKER on the info map and the local map. The flag also keeps it out of linking and out of the door shut pass. A second ruin structure would be drawn at the bunker's spot. | Ui/InfoSections.cs:184; Ui/MapLevels.cs:331-332; Construction/StructureArea.cs:52; Vessels/BlockDoors.cs:190 | C | S |
| E3 | **Wildlife treats the bunker apart from other bases.** The bunker gets its own `SetBunker`, its own `SolidAt` and its own footprint, separate from `SyncBases`. | Surface/WildlifePatch.cs:257, 510-551 | C | S |

**Root fix:** the bunker is an ordinary structure with no owner. Its shelter, spawn clearance and map marker come from what it is (a sealed room trait, a base clearance rule, its kind) like any other structure. The starter is just the first world.

---

### Root F: "wreck" has four definitions

`Vessel.IsWreck` (adrift, or grounded with no core; Vessels/Vessel.cs:191), `IsDerelict` (Orbit/Vessel.Orbit.cs:20), `Structure.IsWreck` (Ui/Visor.cs:417) and the raw `Body.Kind == BodyKind.Wreck` (Vessels/BlockDoors.cs:95, 190).

- A hulk on the ground keeps working doors, because BlockDoors reads `Kind`. A derelict in orbit doesn't.
- The orbit hands' salvage asks `Derelict != null` (Orbit/Spaceflight.Hands.cs:115).
- So the same broken structure follows different rules depending on where it lies.
- **Sure:** C for the split, L for what play shows. **Fix:** M.
- **Root fix:** one wreck state on the structure (no core, nobody's), read the same everywhere. Goes with #26 (one salvage path).

---

### Root G: zone-type tests and stand-in systems instead of facts about the place

`InSpace` and `InOrbit` are the same test (`Zone is OrbitZone`, Orbit/Game.Orbit.cs:18, 21). "Space" therefore means "the orbit zone", not "no air" or "no ground here". The orbit zone also gets empty stand-in systems.

**Root fix:** ask about the place itself (air here, ground or a diggable body under you, gravity, creatures present). An orbit zone gets the same world-owned systems, made from its contents.

| # | What breaks in play | Where | Sure | Fix |
|---|---|---|---|---|
| G1 | **Orbit has empty stand-in weather and wildlife "so the game layer runs unchanged".** Nothing can live in space: no Husks in derelicts and no space fauna, even though shared/Bestiary/Space/SpaceFaunaGenerator.cs exists. On top of that, wrongness is forced to 0 in space (Game.Wrongness.cs:52). The orbit's `Lootables` is also made without `BodyAt`, `Settle` or `DrawnOf`. | Orbit/OrbitZone.cs:175-180; Game/Game.Wrongness.cs:52 | C | M to L |
| G2 | **The scanner refuses in orbit.** That blocks scanning asteroid fields once they exist; it should ask whether a scannable body is in range. | Game/Game.Scanner.cs:65 | C | S |
| G3 | **The cockpit impact warning is off in orbit,** even when closing on a derelict, a station or (later) a rock. | Ui/Visor.cs:246 | C | S |
| G4 | The music's ground feed stops on `InSpace` (SliceMusic.cs:117), and LoadAhead drops the heading on `InSpace` (Game.cs:954, next to #10). | Game/SliceMusic.cs:117; Game/Game.cs:954 | C | S |

---

### Root H: placeholders the game now relies on

| # | What breaks in play | Where | Sure | Fix |
|---|---|---|---|---|
| H1 | **Turrets on generated warships do nothing.** Warship and carrier hulls place 12 or 8 `"turret"` blocks, and the block is "Turret (placeholder)" with no aim of its own. Enemy hulks show guns that never fire. | building/data/components.json:281; Places/HullVessel.Fit.cs:520-521; Vessels/Vessel.Power.cs:81 | C | M |
| H2 | **Raw parts with no use yet:** hide's "Polymer substitute (later)" and rare tissue's "Research (later)". No coat has its own material, so every animal gives the same hide. | data/items.json:39, 41; Game/Game.Items.cs:362, 392-394 | C | S |
| H3 | **Numbers marked placeholder:** the whole of data/places.json ("Placeholders, tuned by play", line 1) and the invasions breach planner (invasions/data/invasions.json:84). | as listed | C | tuning |
| H4 | **Comments that name a missing piece:** `WildSim` "the guard AI that calls this comes later" (shared/Bestiary/Sim/WildSim.cs:388), and the personal shield seam "not built yet" (shared/Arms/Core/Afflictions.cs:7). Nothing relies on them yet. | as listed | U | none yet |
| H5 | **Already covered, not repeated:** "NO BUILDING IN ORBIT" and "its ground's tools come later" (Spaceflight.Hands.cs:43-65, audit #26), "no places yet" (SurfaceZone.cs:38, 221, audit #3), and "NO SAVES YET" (Ui/EscapeMenu.cs:317). | | | |

**Right as they are, not counted:** "NOT IN SPACE" before a warp or fold (you have to be out of the air), no weather in a vacuum, and the deco "CEILING/WALL/FLOOR ONLY" rules.

---

### Five worst

1. **A1:** `PoiSiting.StartWorld` stays true, so every later world is built as a start world.
2. **B1:** the vessel weather subscription never comes off. It leaks every hulk, is hidden by a guard, and doesn't follow ships to other worlds.
3. **G1:** orbit's empty stand-in wildlife and weather, so nothing can ever live in space.
4. **D3:** guards defined twice (`Role` string and `Job` enum), so guard behaviour splits.
5. **A2:** the new game's forced roll is applied to every world in the galaxy.
