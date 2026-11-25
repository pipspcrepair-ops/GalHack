# GalHack architecture: the rules the code follows

**Status: DRAFT for the owner's design pass (25 Nov 2026). Nothing here is built yet.**
- **Where it comes from:** the root fixes in [offworld-audit.md](offworld-audit.md) and [structure-audit.md](structure-audit.md), plus the owner's co-op decisions in DESIGN.md (Tech tree).
- **Once approved:** every brief cites it, and every merge review checks against it.
- **Names:** class names below are proposals. Where a current class keeps its job, its name is given.

**Why it exists.** Both audits found one habit: a feature solved one case beside the old code and never became the rule every case answers. The results:
- systems tied to the starter planet;
- one player baked in;
- the same job done two to nine ways;
- data with no lasting identity.

These rules are the general case. No band-aids: a fix that patches one case breaks a rule here, and is not merged.

---

## 1. The rules
1. **Everything is in a world, and asks its own.** Every system acts for a thing (a sleeper, a vessel, a creature, a bag, a shot). It reads that thing's world when it acts. Nothing stores a world, planet, weather, wildlife, places or sound reference past one call. There is no game-wide "current world".
2. **There is no special starter.** The start world is the first world. Code never asks "is this the starter", "is this home" or "is this the first orbit".
3. **There are many players.** A sleeper is one of a list. Simulation code takes the sleeper it acts for. Only the view (camera, visor, menus, audio listener) reads "the local sleeper".
4. **Ownership is decided once per system** (section 3). Something owned by a world is made with that world and dies with it. Something owned by a player travels with them. Something owned by the faction is kept by the game.
5. **Joining a world is one step.** Each world-held thing has one `Attach(world)` and one `Detach(world)`, which hold every subscription and registration. Spawning, crossing between worlds and removal call only those two.
6. **Everything that lasts has a stable identity.** IDs are built from a world key plus a generation key or a persistent counter. Positions are stored as world key plus canonical coordinates. Seeds come from one helper. Never list order, object references or session counters.
7. **One job, one path.** Damage, salvage, digging, shelter, ground and gravity, queries, events and markers each have one shared path (section 6). A second implementation is not merged. A missing case is added to the shared path.
8. **Facts, not names.** What a thing is comes from a data field or tag, never from comparing an ID, name or label string. "Space" means facts about the place (no air, no ground under you), never "the zone is an orbit".
9. **Required systems fail loudly.** A world declares which systems it always has. A missing required system throws in tests and debug builds. Only declared-optional systems may be absent (no weather in a vacuum). `?.` on a required system is not allowed. *(Section 8, finalised from the silent-failure audit.)*
10. **Settings live in one place.** Game-wide settings (mode, the owner's options) sit in one game-level object, read when acting and never copied into worlds or machines. Simulation size is never set by a client's display settings.

---

## 2. The world model
```
Game (one per server: the seed, the clock, the faction record, the list of sleepers)
 └─ Galaxy (made from the game seed)
     └─ Star (stable star key)
         └─ Body: world, moon, rock field, station site (stable body key)
             └─ World: a SurfaceZone or OrbitZone in play, or a record on disk
                 ├─ world-owned systems: ground and terrain, weather, wildlife, places, loot, devices, vessels in it
                 └─ things in it: each knows its world
```
- **The galaxy comes from the game seed.** The start star and start body are chosen inside it. The start world is generated like any other body: one path, no `SkySystem.For(spec)` beside `StarProfile.Of`. *(Fixes structure A-6.1 and A-6.2.)*
- **A world is in play while anything wants it.** Each world holds interest points: one per sleeper on it, plus any vessel or system that asks. Streaming, wildlife, weather hazards and places run around each point, not one `Focus`. A world parks when it has no points, and is freed to its record when nothing needs it in memory. *(Fixes A-R2.)*
- **Frames:** positions are stored in canonical planet coordinates. Each viewer's floating origin is that viewer's own. Moving a frame never teleports another player. *(Fixes A-2.2 and A-4.6.)*
- **Diggable bodies:** a world's ground and each asteroid in a field are each a diggable body, with their own terrain, ore, change record and gravity. Digging acts on the body the ray hits, never on "the zone's planet". *(See offworld-audit, asteroids.)*
- **The world manager** (today mixed into `Spaceflight`) makes, parks, frees and restores worlds. Travel (one vessel's or sleeper's climb, warp, fold) is separate and belongs to the traveller. *(Fixes A-3.5.)*

## 3. Who owns what
Owner decisions, 25 Nov.

| Owner | What | Notes |
|---|---|---|
| **Each sleeper** (player) | Body, suit and vitals, pack, hands, tools, arms, shots in flight, level, XP, unlock points, the tech tree, destination and chosen star, the open container | Tech is per player. NPC factions have no tech tree. |
| **Sleepers faction** (shared) | Every sleeper's structures and bags, star charts (visited stars, marks, wrecks seen), town and faction standing | A teammate's base or death bag is yours too. A bag records which sleeper died, for its label. |
| **Each world** | Terrain and its edits, weather, wildlife, places and their memory, loot and containers, devices, replicators, respawn points, bikes and vessels standing there, deposits | Kept in that world's change record (section 4) when it isn't in memory. |
| **Game / server** | Seed, galaxy, clock, mode and settings, the list of sleepers, the faction record | One each. |
| **Local view** (per client, rightly single) | Camera, visor, menus, map pan and zoom, audio listener, the hands' viewmodel, the torch light | Reads "the local sleeper". Never used by the simulation. |

Things owned by another faction (towns, raiders, wrecks with an owner) carry that owner. `Lootables.Spill` and every spill take an owner, so a stranger's cargo is never "ours".

## 4. Identity and the change record
- **The world key** is built from the game seed, the star's key and the body's place in its system's generation (its orbital slot path). It must not depend on the order of any list, so `"b"+index` and `moon<index>` go. The exact format is set when it's built, but it is stable across generator changes that don't move the body.
- **The ID of anything that lasts** = world key + generation key (site seed, cell, species and spawn slot) or a counter kept in the world record. This covers places, caches, creatures worth remembering, structures, containers, devices, marks and discoveries. Maps that persist are keyed by ID, while runtime caches keyed by reference are rebuilt from IDs.
- **One seed helper:** `Seed(worldKey, siteId, purpose, …)`. Every persistent roll goes through it. A raw `Hash.Cell(cell, k)` or `rng.NextU64()` as a persistent seed is not allowed. *(Fixes structure A-5.1, same loot per layout.)*
- **Change record per world** (and per diggable body): terrain edits, felled plants, mined deposits, deaths, place memory (doors, damage, loot taken), salvage progress, standing changes, things set down. It's kept when the world is freed, put back when it's made again, and is what a save writes. Regrowth runs on its timers while no one is near. This matches DESIGN.md 747: per-chunk edit lists on top of the seed.
- **The galaxy clock:** one game-owned clock. Each world's day, season and sky come from it, so a parked world's sun is where it should be on return. *(Fixes A-6.4.)*

## 4b. Storage: how world data is kept
The foundation is designed and proven already. GalHack builds on it and does not invent a new one:
- **The design:** DESIGN.md "Persistence: nothing ever disappears".
- **The proof:** `prototypes/saves`. It keeps one small record per thing, each with a checksum, and commits with shadow paging (one atomic rename), so a crash leaves the old save or the new one, never a mix. Every load is audited against a manifest of counts, backups roll, and versions migrate.
- **Already in the slice:** the slice's `ZoneLedger` uses this core today for crossings.

**What is stored, and what isn't.**
- **Stored:** the seed and the *differences* from it: what players and play changed, and everything a player owns.
- **Not stored:** anything the generator makes again from the seed (terrain, unvisited places, wildlife rolls), runtime caches, physics IDs.

So an untouched world costs nothing on disk, and a visited one costs only what changed (DESIGN.md 747: per-chunk edit lists on top of the seed).

**Layout (proposed)**
```
saves/<game>/
  manifest              last commit, counts of records, items, blocks and owned structures
  game                  seed, galaxy clock, mode and settings, list of sleepers
  faction               star charts, standing, the faction's ownership
  sleepers/<id>         vitals, pack, progress and tech, where they are (world key + position, or a ship's cabin)
  worlds/<world key>/
    world               last visited (for regrowth timers), world-level facts
    terrain/<chunk>     edit lists for that chunk (removed cells)
    bodies/<body key>/  the same for each diggable rock in a field
    places/<site id>    place memory: doors, damage, loot taken, dead, standing
    life                felled plants, mined deposits, remembered creatures, deaths
    things/<id>         structures, containers, bags, devices, bikes, replicators, respawn points
```
- **Location is one field:** every record says which world it is in, by world key, plus a ship ID when it's aboard, so the passengers and cargo ride inside the cabin. That world key replaces today's `surface` / `moon1` / `star3/surface/b2` names.
- **When it's written:**
  - The host is the only writer; clients are sent the differences.
  - A world writes its records before it parks or is freed, and a crossing commits through the ledger.
  - A timed autosave and the rolling backups run as in the saves prototype.
- **Loading a world** = generate from the seed, then apply its folder: terrain edits, place memory, life, then things. Loading then checks that every recorded thing came back.
- **One question settled when it's built:** a file per chunk, or chunks packed per region. Thousands of small files across a galaxy may be slow on some disks. The record format is the same either way.

**Gaps today:**
- The slice has no saves.
- The ledger keeps only four kinds of thing (sleeper, structure, container, device).
- Zone names aren't stable.
- Place memory, terrain edits, deaths and salvage progress aren't records yet.

Step 3 of the order of work (section 12) adds them as record types.

## 5. Joining and leaving a world
```
Attach(world): register with the world's physics, weather, tracking, lists and events
Detach(world): undo every one of them
Move(from, to) = Detach(from) + Attach(to)  (the zone ledger commits around it)
```
- `Town.Open`/`Close` and the `Survivor.Zone` setter already work this way. Follow them.
- **Player-owned systems** (suit, tools, arms, effects, doors' animator, compass) attach to their sleeper's world when the sleeper's world changes.
- **No other `+=` on a world is allowed.** Fixes structure C-B1 (vessel weather leak) and C-B2 (shots and frame shifts).

## 6. One path per job
| Job | The one path (proposed) | It replaces |
|---|---|---|
| Harm | A `Harm` event (amount, type, point, direction, radius, source, tags) plus one spatial query. Each target kind has an adapter with its own armour, resistance and HP. One falloff, one health unit. | Three blast copies, two ram copies, two people sims' damage, two health units (structure B-R1) |
| Take-apart | Salvage is harm by the tool class on a thing with HP and a bill. One yield formula, with the owner check inside it. | Seven yield formulas and orbit's hand salvage (B-R2, offworld #26) |
| Built things | One block-world interface for `Structure` and `Place`: cells, HP, doors, rooms, collapse, damage look. Doors, caches and collapse are written once against it. | Three forms of built thing, three door systems, two collapse rules (B-R3) |
| Shelter | `Exposure(point)` on the world: sky open, roof, enclosed room (sealed or not, whose), depth. | Nine shelter tests and the bunker special case (B-R4, C-E) |
| Ground and gravity | A `Ground` service per body (`Top`, `Natural`, `Gravity`). | Five ground lookups and per-system gravity constants (B-R5) |
| Vehicles and seats | The bike is a small hover body in the shared physics. Riding is one seated state. | The bike's own integrator and the `Seated`/`OnBike` split (B-R6) |
| Queries | `WorldQuery` per world with named masks (Sight, ShotStop, Cover, Use). The look, the cabin and the ignore list are handled once. | Separate AI sight masks, along-the-look tests and E-prompt order (B-R7) |
| Events | `WorldEvent` (kind, point, loudness, material, source). Audio, AI hearing, visor cues and effects subscribe to it. | Four hand-made calls per event, four voice pools (B-R8) |
| Markers | `Markers(world, viewer)`. The visor and maps only filter and draw. | Three marker lists (B-R9) |
| Entry points | The shared path is the only way in. Fields like `Planet.Felled` are private to it, and inventory has one `Receive(bill, at, from)`. | Callers going around `Fell`, `Add` and `Spill` (B-R10) |
| Wreck | One wreck state on the structure (no core, no owner's claim). | Four definitions (C-F) |

## 7. Facts, not names
- **Item use:** an item's use and hand kind come from its data, not from its ID (`"hover-bike"`, `"scanner"`, `"meat"`).
- **Harm type:** a weather harm type is a field, not a display label.
- **Role:** a person's role is one field (the `Role` string and the `Job` enum become one).
- **Sites:** place families, world layers and space-site kinds carry tags, and code reads tags, not ID prefixes or substrings.
- **Space:** "In space" questions become questions about the place: air here, ground or a diggable body in reach, gravity, life present. Orbit gets the same world-owned systems as any world, made from what is in it, never empty stand-ins.

## 8. Required and optional systems
From the silent-failure audit, [silent-failure-audit.md](silent-failure-audit.md).

**What that audit found:**
- About 224 null-conditional calls on systems: about 95 legitimately optional, about 105 hiding a system that should exist, about 25 unsure.
- 14 early returns on missing state, 17 catches and 13 events never subscribed.
- The worst: shots never step in orbit. ArmsHost returns on `Zone?.Planet == null`, and an orbit zone never has a `Planet` (confirmed). Force fields and doors use the same guard.

**The rule:**
- **Every world always has the same systems, made in one place.** "Nothing here" is a real, empty system plus a named fact (`HasAir`, `HasLife`, `HasWater`), never a null. An orbit has a weather system that holds no weather, and a wildlife system that holds whatever lives there (maybe nothing).
- **"Not ready yet" is a state, not a null.** A system that is still building says so (`Ready`), and callers that must wait are written to wait.
- **Optional is declared.** A reference that may truly be absent is typed `T?` with a comment saying why. Every other system reference is non-null, and `?.` on it is not allowed (review checklist).
- **A missing required system fails loudly.**
  - In tests and debug builds it throws, so broken wiring shows the first time it runs.
  - In a release build it logs once and refuses item-holding actions (salvage, take-down, spill, restore) rather than losing or duplicating items.
- **No swallowed errors.** A failure that drops a thing logs it, keeps the record, and blocks the commit, as the ledger does. Spaceflight.Fold.cs:473 drops a structure that won't restore, with only a console line.
- **Every event has a subscriber or is deleted.** An event with no listener is dead code that looks like a feature (the XP line: `Progress.Earned` is never joined to `Visor.Xp`).

## 9. Tests
From the test audit, [test-coverage-audit.md](test-coverage-audit.md).

**What that audit found:** the tests prove travel, not that systems work after it.
- Seven tests leave the starter. They check sky, light, sound, gravity and the suit's vacuum flag.
- Of the 26 off-world findings, three are partly covered.
- All travel tests calm the starter's weather and freeze its wildlife before leaving (MoonTest.cs:101-103), which hides two of the bugs.
- Two tests *assert* bugs:
  - SleeperToolTest.cs:286-297 checks that a coreless block comes back whole (structure B-B8).
  - ClimateTest.cs:466 passes with the tornado-felling bypass (B-B25).
- About 30 checks pass silently when nothing suitable rolls.
- `Check` is copied into 81 files.
- The gate never passes `--airless`.

**The rule:**
- **One shared harness, `Harness`, beside Driver.cs.** Tests never hack their own start.
  - `WorldChoice` gets a start world key (star, body), so any test can start on the starter, a moon, a vacuum world, orbit or another star.
  - The harness gives `Arrive(body)`, `AddSleeper()`, `SaveAndReload()` and a per-world probe that checks every required system (section 8) is present and attached.
- **Each rule in section 1 has a check that fails when it's broken,** run on bodies other than the starter. Every audit finding gets one before its fix lands.
- **Tests don't quiet the world to pass.** Calming weather or freezing wildlife is only for a check about something else, and never across a crossing.
- **No silent pass.** A check that finds nothing suitable to test fails, or rolls again until it finds one. It never prints and passes.
- **One `Check`, in the shared testing code.** The 81 copies go.
- **Tests that assert a bug are rewritten** to assert the rule.
- **Seeds stay random every run** (owner rule). At most 4 Godot runs machine-wide, through the queue scripts.

## 10. Merge review checklist
Reject a change that:
- stores a world or world system in a field, or uses `Game.Surface` / "home" / "first orbit" outside the world manager;
- reads `Game.Survivor` in simulation code (use the sleeper acted for);
- adds a `+=` on a world outside `Attach`;
- adds a static holding per-world state;
- writes a second implementation of a job in section 6;
- compares an ID or name string to decide behaviour;
- makes an ID, seed or stored position without the world key;
- uses `?.` on a required system;
- patches one case of a rule instead of fixing the rule.

## 11. Open questions for the owner
1. **Worlds with no one on them:** parked and frozen (as today), or a slow background tick for worlds with your bases (machines running, crops, raids)? This decides what the server simulates.
2. **NPC travel between worlds** (traders, raiders, convoys): needed soon? It decides whether travel belongs to any vessel or only to sleepers' vessels.
3. **Co-op far apart:** how far apart may two players be on one world before it costs too much? This sets the interest-point budget on the minimum-spec machine.
4. **Start for joining players:** each sleeper has their own cryopod in the bunker, or later joiners wake elsewhere?

## 12. Order of work
Each step is a root fix, gated on this doc's approval.
1. **Statics, attach and settings:** per-world build context (structure S3), `Attach`/`Detach` (S4), one settings object (S5). Small, and they fix live bugs (every world built as a start world, the forced roll everywhere, the weather leak, shots and frame shifts).
2. **The world model:** each thing's world, the sleeper list, interest points, ownership split, no starter special case. This is offworld Root 1 with S1, S2, S7 and S15. It includes the places stack per world, the suit, tools, BuildTool, arms, doors, bikes, effects and the compass.
3. **Identity:** world keys, IDs, the seed helper, the change record, the galaxy clock (offworld Root 2, S6). This comes before saves and before star travel ships.
4. **One path per job,** in this order: harm and the block world (carrying salvage and shelter), then ground, gravity and vehicles, then queries, events and markers, then facts not names, then the placeholder decisions.
