# GalHack architecture: the rules the code follows

**Status: APPROVED, 26 Nov 2026** (the owner left the approval to Claude: "I'm trusting you on the approvals"), after seven independent reviews. Nothing here is built yet. Work starts on the owner's go.
- **v2:** revised after an adversarial review that checked v1 against the code, DESIGN.md and the owner's rules. It found 19 problems; all are folded in here, and the largest are rules 2 and 12, section 2b, section 4c and the new order of work.
- **Where it comes from:** the root fixes in [offworld-audit.md](offworld-audit.md) and [structure-audit.md](structure-audit.md), the rules from [silent-failure-audit.md](silent-failure-audit.md) and [test-coverage-audit.md](test-coverage-audit.md), and the owner's decisions of 25 Nov.
- **Once approved:** every brief cites it, and every merge review checks against it (section 10).
- **Names:** class names are proposals. Where a current class keeps its job, its name is given.

**Why it exists.** Every audit found one habit: a feature solved one case beside the old code and never became the rule every case answers. The results:
- systems tied to the starter planet;
- one player baked in;
- the same job done two to nine ways;
- data with no lasting identity;
- failures that make no noise;
- tests that only prove the starter.

These rules are the general case. No band-aids: a fix that patches one case breaks a rule here, and is not merged.

---

## 1. The rules
1. **Everything physical is held by one world, and reads it from itself.**
   - Each world-held thing (a sleeper's body, a vessel, a creature, a bag, a shot, an effect) has exactly one `World` field, set only by `Attach`, `Detach` and `Move` (rule 5).
   - A system acting for a thing reads `thing.World` on each call and keeps no copy. No system stores a world, planet, weather, wildlife, places or sound reference in a field.
   - There is no game-wide "current world".
   - A worker job (world building, place building, sky baking) carries the world key plus that world's generation number. It drops its result if the world was freed or rebuilt meanwhile.
     - Today's jobs with no such check: `Place.RebuildStale` (broken chunks meshed on a worker and put in a piece at a time), the `DepositFinder` recount, `TerrainStreamer`'s voxel and height jobs, `WildlifePatch.RollAhead`, and `MakeOrbit`'s wreck shapes built on a worker (`StructureShapes.Hold`/`Return`). Step 1 sweeps them.
2. **Each world in play has its own Godot world** (DESIGN.md 1578): its own World3D, physics space, navigation and scene root.
   - Rays and shape queries go only through that world's `WorldQuery` (section 6), never through `GetWorld3D()` of whatever node is at hand.
   - Parking a world pauses its space. It never shelves bodies by clearing collision layers, as `Spaceflight.Park` does today.
   - Two worlds can never touch each other's bodies.
3. **There is no special starter.** The start world is the first world, and the start system is just where you begin (owner: "it's unlikely we'd ever return to it once we leave").
   - Code never asks "is this the starter", "is this home" or "is this the first orbit".
   - The start system parks, frees and restores by the same rules as any other. Nothing keeps it in memory.
4. **There are many players** (up to four, one per cryopod). A sleeper is one of a list. Simulation code takes the sleeper it acts for. Only the view (camera, visor, menus, audio listener) reads "the local sleeper".
5. **Joining a world is one step.**
   - `Attach(world)` makes every registration (physics, weather, tracking, lists, events) and returns a handle holding them.
   - `Detach` disposes that handle. It never undoes by looking the world up again.
   - `Move(from, to)` is the only way across (section 4c). Spawning, crossing and removal call only these.
6. **Who claims a thing and where it is held are two separate facts** (section 3). Every physical thing is held by the world it is in and carries a `Source` (the sleeper or NPC that made it, for blame and XP) and a `Claim` (sleeper faction, NPC faction, or none). Only non-physical state is held by a sleeper or by the game.
7. **Everything that lasts has a stable identity** (section 4).
   - IDs are a typed `WorldKey` plus a generation key or a persistent counter.
   - Stored positions are a `WorldKey` plus coordinates in that world's fixed frame.
   - Seeds come from one helper.
   - Never list order, object references, session counters, or a key parsed from a string.
8. **One job, one path.** Damage, salvage, digging, shelter, ground and gravity, queries, events and markers each have one shared path (section 6).
   - A second implementation is not merged. A missing case is added to the shared path.
   - A replacement merges only together with deleting what it replaces.
9. **Facts, not names.** What a thing is comes from a data field or tag, never from comparing an ID, name or label string. "Space" means facts about the place (no air, no ground under you), never "the zone is an orbit".
10. **Every world has every system, and missing ones fail loudly** (section 8). A system with nothing to hold is the same class holding empty data, never a stub class and never a null. A world attaches only once all its systems are `Ready`, so no caller checks or waits. `?.` on a system is not allowed.
11. **Game-wide settings and game data live in one place.** Mode and the owner's options sit in one game-level object, read when acting, never copied into worlds or machines. Items and blueprints are game-wide. Planet and climate rules come from each world's spec, read through `thing.World`. Simulation size is never set by a client's display settings.
12. **Simulation and view are separate.** A world nobody is looking at runs simulation only: physics, AI, weather and survival, no drawing, sky field, foliage, sound or effects. View systems exist only for the drawn world and are made in its view attach. Engine-wide state (global shader values, environment, `AudioServer` buses) is written only by the drawn world's view (section 2b).

---

## 2. The world model
```
Game (one per host: the seed, the clock, the faction record, the list of sleepers)
 └─ Galaxy (made from the game seed)
     └─ Star (stable key from its seed, not its catalogue index)
         └─ Body: world, moon, rock field, station site (key from its orbital path)
             └─ World, one per layer: surface, orbit, and each occupied ship's cabin
                 Each is its own Godot world (rule 2), in play or a record on disk
                 ├─ its systems: ground and diggable bodies, weather, wildlife, places, loot, devices, query, events
                 └─ things held in it: each with one World field (rule 1)
```
- **The galaxy comes from the game seed.** The start star and start body are chosen inside it. The start world is generated like any other body: one path, no `SkySystem.For(spec)` beside `StarProfile.Of`. *(Structure A-6.1 and A-6.2.)*
- **One star model.** Each star carries its class's temperature, radius, mass and brightness, and everything it lights comes from those: colour, light, its size in the sky, the zones and its worlds' temperatures. There is no second star class (today's `StarClass` and the 12-to-5 `SkyRig.ClassOf` go). The start star is picked first, and is always G or K (solar-systems.md).
- **What keeps a world in play (interest points), and nothing else:**
  - a sleeper on it;
  - an occupied cabin on it;
  - a destination being prepared for a crossing, for up to its tunnel's length.

  Bases, turrets, machines and parked ships never hold a world. A world with no points is frozen at once (owner: "there is literally no reason to have them active in memory"). It stays loaded but frozen for about 2 minutes (owner, 25 Nov), so hopping between surface and orbit doesn't regenerate it. Nothing in it ticks during that time. Then it is freed to its record.
- **Several points on one world:** streaming, wildlife, weather hazards and places run around each point, not one `Focus`. *(A-R2.)*
  - Collision streams around every interest point, and around any fight the host simulates.
  - Today `Streamer.CollisionRadius` is set from one sleeper's vehicle (Game.cs:925), and rounds past it fall back to the height map.
  - Collision streaming is separate from drawing. An undrawn world streams collision only (rule 12).
- **Frames:** the host simulates each world in one fixed frame centred on the zone (DESIGN.md 1580: pinned origin, the map wraps, about 2 mm precision). Simulation positions never shift. Only clients offset what they draw. An orbit zone's size is limited so precision holds. *(A-2.2, A-4.6.)*
- **Cabins:** each occupied ship's cabin is its own world (DESIGN.md 1582), made when a sleeper boards and freed when the ship is empty and parked. Freeing it commits everything loose in it (cargo, bags, creatures) into the ship's record, through the ledger, and they come back when the cabin is made again. It isn't made for every vessel; today every vessel, wrecks included, builds a `CabinWorld` (Vessel.cs:406-409).
  - A thing aboard has `World = cabin` (where it moves). It reads the ship first: its sealed rooms' air and its core's fixed 1 g while the core works. Then it reads its parent: the structure the ship is docked or parked in (`ParkedOn`), else `Outside = ship.World`, for weather, and for air and gravity where the ship gives none (owner, 25 Nov). A ship with its core off inside a powered carrier gets the carrier's 1 g.
  - A base never moves, so it isn't a cabin world: it is a fixed body on its world, found by position through the "what am I inside" lookup (T4.4). Its core's 1 g covers anyone inside its bounds, and stays when its roof is gone while the core runs; its sealed rooms answer air.
  - When a ship moves, only the ship goes through the ledger. Things aboard stay in the cabin and re-read `Outside`, in one call from the ship's `Move`.
  - **Cabins nest to any depth** (owner, 26 Nov: "A cabin can be inside a cabin can be inside a cabin"). A shuttle can sit in a frigate's cabin, which sits in a carrier's cabin, and so on. Nothing assumes one level:
    - `Outside = ship.World`, and that world may itself be a cabin. Every lookup (air, gravity, weather, exposure, "what am I inside") walks the chain outward until it reaches a world that isn't a cabin. Nothing caches a resolved chain past a `Move`.
    - An occupied cabin keeps every world up its chain in play, since its ship is a body in the cabin around it. Each cabin in an occupied chain counts against section 2b's budget.
    - Freeing works from the inside out: an inner cabin is freed before the one around it, and freeing a cabin commits the ships in it into its ship's record, each with its own record inside, to any depth (section 4c).
    - Jumps, climbs and docking carry the whole nest: ships inside a cabin ride along with their own cabins and everything in them.
- **Diggable bodies:** a world's ground and each asteroid in a field are each a diggable body, with their own terrain, ore, change record and gravity. Digging acts on the body the ray hits, never on "the zone's planet".
- **The world manager** (today mixed into `Spaceflight`) makes, parks, frees and restores worlds and runs the commit queue (section 4c). Travel (a vessel's climb, warp or fold) belongs to the vessel.
  - Any vessel, NPC ships included, may `Move` between two worlds that are both in play: pirates following you up, sky traffic.
  - **Jumps** (warp and fold) are described in section 2c.
  - NPC travel between stars is not simulated. NPC traffic is placed on a world, by rule, when the world is made (owner: "we don't need to track every trader and raider").
- **References across worlds** (an AI's target, a marker, an open container, the ledger's `_ids` map) are IDs resolved through the world manager. A world out of play gives back "not here", never a stale object.
- **The start system** is freed like any other (rule 3). Today `ParkHome` keeps it loaded and `FreeAbroad` frees every other star's (Spaceflight.Fold.cs:288-289). No design calls for that. It survives because about 20 systems hold the starter zone. `ParkHome` goes only after section 4's change record exists, or the start system's digging, places and deaths would be lost on the first fold.

## 2b. Running several worlds
Co-op players may be any distance apart, systems apart included (owner). The host runs every world with an interest point, up to:
- one per sleeper (4);
- plus each occupied cabin;
- plus any destination being prepared.

The frame budget has to hold that peak.
- **Simulation only, off the view:** the worlds not drawn run simulation only (rule 12).
- **A tick budget per world:** each extra world runs AI, wildlife and places at a lower rate. *Its numbers come from the step 2a prototype.*
- **Process-wide budgets are split or shared by a stated rule:**
  - `FramePacer` paces at 60, and `TerrainStreamer.UploadBudgetMs = Pacer.Room` (Game.cs:924) is one room per frame.
  - `GcPacer` collects every 16 MB, and the garbage from every world shares it. Damage work doubled the allocations.

  Each gets a per-world share, or is split per world.
  - Freeing a world must also give its memory back. `GcPacer` only collects young objects, so a parked or freed world's native memory waits for a full collection (warp work's memory trace). A freed world triggers one, spread so it doesn't cost a frame.
- **Threads:** heavy world work (generation, place building, AI batches, record writing) runs on workers across the cores (owner: "use all the cores"). The plan for which thread runs which world comes from the prototype.
- **No whole-world writes on the main thread:** records are dirty-tracked and written in small pieces off the main thread. *(The ledger's own note, Fold.cs:487-490: one wreck's blueprint at every commit "was 140 ms a fold".)*
- **The gate:** 4 occupied worlds plus 2 cabins, on the 4-core GTX 1050 stand-in at the lowest preset, holding 16.7 ms with headroom.
  - It runs as a multi-world PopRoute route.
  - There is no headroom to assume. Today's single world on the owner's settings already fails bike and fly frame checks (13 fails on 18 Nov), so step 2a starts from a measured baseline.
  - It's measured on the Mobile renderer. Forward+ was rolled back on 23 Nov (far lamps bloomed into flickering blobs) and stays off until that's understood.
- **View-side jobs follow rule 12:** far tree models (proto/forests) and voxel ground to the horizon (`TerrainStreamer`). They exist only for the drawn world.
- **Engine options for the owner** (engine work is on the table): physics spaces stepped on their own threads, and a simulation-only World3D with no rendering cost.

## 2c. Jumps with several players
Players warp and fold independently and may each be in any system (owner, 25 Nov). Several sleepers may be aboard one ship that another sleeper jumps, seated or standing.

**What exists today:**
- One jump, one target and one fold hook for the whole game: `Spaceflight.Jumping`, `Target`, `FoldTo` (Spaceflight.Jump.cs:22, 80, 83).
- The tunnel isn't a place. The ship stays frozen in the world it left (`Hold`, Spaceflight.Jump.cs:169), and the tunnel is only drawn over the screen. A sleeper who opened a door mid-tunnel would step into the old world and be left behind.

**The rules:**
1. **Every vessel has its own jump.** Spool, tunnel and arrival are the vessel's state. Any number of ships can jump at once, to different destinations. Each destination is prepared as its own interest point (section 2b), and each ship's tunnel lasts until its own destination is ready.
2. **The pilot jumps the ship.**
   - The sleeper in the pilot seat chooses the target and starts the spool.
   - Leaving the pilot seat during the spool calls it off.
   - Once committed at the end of the spool, the jump finishes without a pilot.
   - **Calling it off:** only the sleeper piloting from the command chair can, by pressing J during the spool (owner, 25 Nov). Passengers can't. Once the jump commits it can't be called off: J does nothing in the tunnel.
3. **Everyone aboard is warned.** The spool shows on every aboard sleeper's visor and its sound is heard, seated or standing, on the hull or inside.
4. **The tunnel is a real world.** At commit the ship moves into its own transit world, which exists only for that jump, with the tunnel as its sky. Everything always has a world (rule 1). Two ships' tunnels never meet.
5. **What goes is decided at commit:**
   - **Only the jumping ship enters the transit world,** with the ships it carries (owner, 25 Nov):
     - ships clamped to its outside;
     - ships inside its cabin world.

     Every other ship is unaffected, however near.
   - Clamped ships keep their place on the hull and arrive clamped. Their own cabins and everyone in them ride along untouched.
   - Everything in the ship's cabin rides inside it: sleepers seated or standing, cargo, and ships inside the cabin world.
   - Sleepers on the outer hull go into the transit world with the ship.
   - Anything that stepped off before commit stays behind.
6. **Stepping out of the cabin mid-tunnel** puts the sleeper in the transit world, outside the hull. At arrival they come out floating beside the ship at the destination (owner, 25 Nov: "arrive beside the ship"). The same goes for hull riders. Nobody is lost in a jump.
7. **Saves:**
   - The ship's record stays in its source world until arrival commits, through section 4c's queue. A crash mid-tunnel reloads the ship where the jump began.
   - Passengers ride inside the ship's record.
   - Sleepers outside the hull in the transit world are committed with the ship's arrival, in the same commit.
8. **Disconnects:** a sleeper who drops out mid-tunnel stays where they were, in the cabin or in the transit world. They're aboard, or beside the ship, at the destination when they reconnect.

## 3. Who claims what, and where it is held
Owner decisions, 25 Nov.

| Thing | Claim (owner) | Held by (where it lives) |
|---|---|---|
| Body, suit | that sleeper | the world they're in (the cabin when aboard) |
| Vitals, pack contents, level, XP, unlock points, tech tree, destination, chosen star | that sleeper | the sleeper's record (not physical) |
| Shots in flight, effects, debris, gibs, blood | none; `Source` = who made it | the world they're in |
| Structures, vessels, bags of any sleeper | Sleepers faction | the world they're in; the Sleepers faction record keeps only an index of them by ID (T3.1b) |
| Lockers in the four cryopods | Sleepers faction, labelled with the pod's sleeper | the start world |
| Star charts (visited stars, marks, wrecks seen), standing | Sleepers faction | the faction record |
| Towns, raiders, NPC ships, owned wrecks | that NPC faction | the world they're in |
| Terrain edits, felled plants, deposits, wildlife, places' memory, unowned loot | none | the world's change record |
| Seed, galaxy, clock, mode, settings, list of sleepers | the game | the game record |
| Camera, visor, menus, map pan and zoom, audio listener, hands' viewmodel, torch light | the local client | the local view only (rule 12) |

- A bag records which sleeper died, for its label. A teammate's bag is "ours".
- `Lootables.Spill` and every spill take a claim, so a stranger's cargo is never "ours".
- NPC factions have no tech tree.

## 4. Identity and the change record
- **`WorldKey`** is a typed value: game seed, star key (from the star's seed), body's orbital path, and layer (surface, orbit, cabin, rock). Code reads its fields. It is never parsed from a string. Today's `StartsWith` checks on zone names go (ZoneLedger.cs:284, Fold.cs:440), as do the `"b"+index`, `moon<index>` and `star<n>` names. It is stable across generator changes that don't move the body.
- **The ID of anything that lasts** = `WorldKey` + a generation key (site seed, cell, species and spawn slot) or a counter kept in the world record. This covers places, caches, creatures worth remembering, structures, containers, devices, marks and discoveries. Maps that persist are keyed by ID. Runtime caches keyed by reference are rebuilt from IDs.
- **One seed helper:** `Seed(worldKey, siteId, purpose, …)`. Every persistent roll goes through it. *(Fixes structure A-5.1, the same loot in every copy of a layout.)*
- **The same seed always builds the same world,** on every run and every machine. Storage keeps only the seed and the changes, so a world that rebuilds differently puts its changes on the wrong things. No generation step may depend on run order, time or threads. The fingerprint check (build twice, compare) runs in every merge gate. *(Today city planning differs between runs on some seeds: root-fix task T1.5.)*
- **Change record per world:**
  - Terrain edits per chunk; for a rock field, one record keyed by rock ID, written only for rocks that were touched.
  - Felled plants, mined deposits, deaths, place memory (doors, damage, loot taken), salvage progress, standing changes, things set down.
  - It's what a save writes (DESIGN.md 747: edit lists on top of the seed).
- **Time passing while frozen:** one game-owned galaxy clock. A frozen world doesn't tick (owner). When it's made again, a catch-up hook works out what should have moved on from the elapsed time: the sun's and moons' places, running timers and rooms' air, on every record, claimed or not (root-fix-tasks.md T3.2). Regrowth, decay and resource meteorites below about 10% deposits (DESIGN.md 706) are feature F9 on that hook, and touch only records with no claim (DESIGN.md 1592). Rolls use `Seed(worldKey, …, elapsed bucket)`, so a reload gives the same result.

## 4b. Storage
The foundation is designed and proven already. GalHack builds on it and does not invent a new one:
- **The design:** DESIGN.md "Persistence: nothing ever disappears".
- **The proof:** `prototypes/saves`. It keeps one small record per thing, each with a checksum, and commits with shadow paging (one atomic rename). A crash leaves the old save or the new one, never a mix. Every load is audited against a manifest of counts, backups roll, and versions migrate.
- **Already in the slice:** `ZoneLedger` uses this core today for crossings.
- **Not a database:** the owner considered one and kept this record store.

**What is stored, and what isn't.**
- **Stored:** the seed and the differences from it: what play changed, and everything claimed.
- **Not stored:** anything the generator makes again from the seed, runtime caches, physics IDs.

So an untouched world costs nothing on disk.

**Layout (proposed)**
```
saves/<game>/
  manifest              last commit, counts of records, items, blocks and claimed structures
  game                  seed, galaxy clock, mode and settings, list of sleepers
  faction               star charts, standing, an index of claimed things by ID
  sleepers/<id>         vitals, pack, progress and tech, which world (key) and where, or which cabin
  worlds/<world key>/
    world               last visited (galaxy-clock time), world-level facts
    terrain/<chunk>     edit lists for that chunk
    rocks               a field's touched rocks, by rock ID
    places/<site id>    place memory
    life                felled plants, mined deposits, remembered creatures, deaths
    things/<id>         structures, containers, bags, devices, bikes, replicators, respawn points
```
- **Writing:**
  - The host is the only writer, and clients are sent the differences (section 9b).
  - A world writes its dirty records in pieces while it runs, and its last ones before it is freed.
  - Timed autosave and rolling backups run as in the saves prototype.
- **Loading a world:** generate it from the seed, apply the clock catch-up (section 4), then apply its folder. Finally check that every recorded thing came back.
- **One choice is left until it's built:** a file per chunk, or chunks packed per region. The record format is the same either way.

## 4c. Crossings, parking and freeing never lose a thing
These are the paths the new model adds. Item loss is a release blocker (DESIGN.md 1587), and today's ledger isn't safe for them:
- `Refresh` deletes every record not in `Holders()` unless its zone counts as out of play, and only star zones count (ZoneLedger.cs:127-137, Fold.cs:446).
- It handles one crossing at a time (ZoneLedger.cs:194-196).

The rules:
- **One commit queue.** Commits run one at a time, on a worker. Parks, frees and every sleeper's crossings queue up; none overlap.
- **Gone only when destroyed.** A record is removed only when its holder reports it destroyed: a body emptied, a block taken apart, or a death bag that vanishes when its last item is taken (`Lootables.Tick`, owner 17 Nov). A holder that is merely missing, because its world is parked, freed or loading, is never "gone".
- **The order of a `Move`:**
  1. commit the thing's new world;
  2. `Detach(from)`;
  3. `Attach(to)`.

  If `Attach` fails, the thing is put back in `from` with a second commit, and the failure is reported loudly.
- **Every sleeper is a holder, by ID.** Today `Holders()` yields one constant sleeper record (Spaceflight.cs:684-687).
- **The conservation audit refuses.** A crossing or jump that loses or duplicates an item is refused and rolled back, never only printed (today `Ledger.Audit` prints, Spaceflight.cs:677, Jump.cs:251). A failed restore keeps its record and blocks the commit.
- **A record whose world won't load** is moved to the faction's last base, never dropped (DESIGN.md 1583).
- **The test:** crash at every step of a park, a free, and two crossings at once; reload; every count matches.

## 5. Joining and leaving a world
```
handle = Attach(world)   every registration with the world's physics, weather, tracking, lists and events
Detach(handle)           disposes exactly those
Move(from, to)           section 4c's order
```
- `Town.Open`/`Close` (Town.cs:428-452) is the nearest existing model. It isn't exact: `Close` finds what to unsubscribe from by reading `_places.Zone.Wildlife` again, rather than through a handle.
- Player-claimed systems that act in a world (suit, tools, arms, the doors' animator) attach to their sleeper's world, through the sleeper's `Move`.
- **No other `+=` on a world is allowed.** This fixes the vessel weather leak (structure C-B1) and the shots' frame shift (C-B2).

## 6. One path per job
| Job | The one path (proposed) | It replaces |
|---|---|---|
| Queries | `WorldQuery` per world, on that world's own physics space (rule 2), with named masks (Sight, ShotStop, Cover, Use); the look, the cabin and the ignore list handled once | About 29 direct `GetWorld3D().DirectSpaceState` calls, ArmsHost's own `RayCast3D` node (ArmsHost.cs:50, 68), separate AI sight masks, along-the-look tests, the E-prompt order (B-R7) |
| Harm | `Harm` event (amount, type, point, direction, radius, `Source`, tags) plus one spatial query; each target kind has an adapter with its own armour, resistance and HP; one falloff, one health unit. Weather hits and exposure (suffocation, cold, heat, radiation) are Harm sources, replacing `Suit.Hit`'s own damage kind; the adapter carries a "doesn't breathe" flag | Three blast copies, two ram copies, two people sims' damage, two health units (B-R1); the per-target damage copies in `ArmsHost.World.cs`, `PoiPlaces.Damage.cs` and `SurfaceZone.Felling.cs` (`WearPlant`) |
| Take-apart | Salvage is harm by the tool class on a thing with HP and a bill; one yield formula, with the claim check inside it | Seven yield formulas, `SleeperTool.CutPlant`, and orbit's hand salvage (B-R2, offworld #26) |
| Built things | One block-world interface for `Structure` and `Place`: cells, HP, doors, rooms, collapse, damage look; doors, caches and collapse written once against it. It carries a read-only surface query that runs on a worker. Wear stays paint, apart from the HP damage look | Three forms of built thing, three door systems, two collapse rules (B-R3) |
| Shelter and air | `Exposure(point)` on the world: sky open, roof, depth, and the innermost air volume at the point. Air volumes work like cabin worlds: each sealed room owns its air, held by its structure or place; volumes nest, each reading its parent and then the world; openings (doors, breaches) link a volume to its parent. Powered life support (the existing pod, on the power model) eases a sealed room to full oxygen and 20 °C; unpowered, the room holds a while, then drifts to its parent's air. Area sources such as the climate pod warm a radius and aren't volumes. One "what am I inside" lookup (innermost volume and structure, then the parent) serves air and gravity | Nine shelter tests, the bunker special case (B-R4, C-E) |
| Ground and gravity | A `Ground` service per diggable body (`Top`, `Natural`). Gravity follows cabin-world logic: the innermost working core gives a fixed 1 g, else its parent structure, else the world (its own: standard g × its scale; orbit 0 g), read through the same "what am I inside" lookup | Five ground lookups; `ArmsHost.SolidHit` (ArmsHost.cs around 559-638), whose height-map fallback is `Streamer.CollidesAt` plus `TerrainStore.Raycast`; gravity constants per system (B-R5) |
| Vehicles and seats | The bike is a small hover body in the shared physics; riding is one seated state | The bike's own integrator, the `Seated`/`OnBike` split (B-R6) |
| Events | `WorldEvent` (kind, point, loudness, material, `Source`); audio, AI hearing, visor cues and effects subscribe | Four hand-made calls per event, four voice pools (B-R8); `WildlifePatch.Noise` and `Town.Gunfire` from the NPC reactions work, turret gunfire included |
| Markers | `Markers(world, viewer)`; the visor and maps only filter and draw. It keeps the owner's 17 Nov rule: only the viewer's own map marks and scan results, plus always our bags, our structures, and the bunker on its own world | Three marker lists (B-R9), `Visor.Markers`' `Game.Bunker` and `Zone == Surface` checks (Visor.cs:415) |
| Entry points | The shared path is the only way in (`Planet.Felled` private to `Fell`; one inventory `Receive(bill, at, from)`) | Callers going round `Fell`, `Add`, `Spill` (B-R10) |
| Wreck | One wreck state on the structure (no core, no claim) | Four definitions (C-F) |

**The owner's damage rules (17 Nov) that Harm and Take-apart must keep** (playtest list, Combat, "SHOTS DAMAGE EVERYTHING"):
- Any weapon, from any source, damages any block, place piece, door, piece of dressing, the ground and trees.
- Only the Sleeper tool gives materials. In GATHER mode it wears blocks at 5 HP/s.
- Each tool blast that hurts a person, animal or Husk gives +1 biomass.
- Gathering a corpse gives 1 bio. Its loot goes to the bag, and the overflow to a sack.
- A destroyed corpse spills one bag. Loot stays on corpses.
- Trees, plants and boulders have HP, worn by the same path.

## 7. Facts, not names
- **Item use:** an item's use and hand kind come from its data, not from its ID (`"hover-bike"`, `"scanner"`, `"meat"`).
- **Harm type:** a weather harm type is a field, not a display label.
- **Role:** a person's role is one field (the `Role` string and `Job` enum become one).
- **Sites:** place families, world layers and space-site kinds carry tags, and code reads the tags.
- **Space:** "in space" questions become questions about the place: air here, ground or a diggable body in reach, gravity, life present.

## 8. Every world has every system
From [silent-failure-audit.md](silent-failure-audit.md).

**What that audit found:**
- About 224 null-conditional calls on systems: about 95 legitimately optional, about 105 hiding a system that should exist, about 25 unsure.
- 14 early returns on missing state, 17 catches and 13 events never subscribed.
- Worst: `Zone?.Planet == null` returns silence everything in orbit, because an orbit zone never has a `Planet`. That covers ArmsHost.cs:110 (no shots), BlockDoors:166, ForceFields:62, SkyLight:91, PoiPlaces:127/144, RuinLoot:36, Underworld:57, PlaceFauna:599 and Hulks:61.

**The rule:**
- **Every world has the same set of systems, made in one place.** "Nothing here" is the same class holding empty data from the generator, plus a named fact (`HasAir`, `HasLife`, `HasWater`). An orbit's weather holds no weather, and its wildlife holds whatever lives there. Never a stub class: that is how the empty stand-in wildlife in orbit (structure C-G1) went silent.
- **"Not ready" is never seen.** A world attaches only once every system is `Ready` (rule 10), so no caller checks or waits.
- **A missing system throws** (tests and debug), so broken wiring shows the first time it runs. In release, the host stops and offers a backup, as it does on a failed load audit (DESIGN.md 1595). There is no half-working mode.
- **No swallowed errors.** A failure that would drop a thing keeps the record and blocks the commit. Fold.cs:473 drops a structure that won't restore, with only a console line.
- **Every event has a subscriber or is deleted.** The XP line, `Progress.Earned` to `Visor.Xp`, is never joined.

## 9. Tests
From [test-coverage-audit.md](test-coverage-audit.md).

**What that audit found:** the tests prove travel, not that systems work after it.
- Of the 26 off-world findings, three are partly covered.
- Travel tests calm the starter's weather and freeze its wildlife first (MoonTest.cs:101-103).
- Two tests assert bugs (SleeperToolTest.cs:286-297, ClimateTest.cs:466).
- About 30 checks pass silently, `Check` is copied into 81 files, and the gate never passes `--airless`.

**The rule:**
- **One shared harness, `Harness`, beside Driver.cs.**
  - `WorldChoice` takes a start `WorldKey`, so a test can start on the starter, a moon, a vacuum world, orbit or another star.
  - It gives `Arrive(body)`, `AddSleeper(world)`, `SaveAndReload()`, and a probe that checks every system of a world is present, `Ready` and attached (section 8).
- **Each rule in section 1 has a check that fails when it's broken,** run off the starter. Every audit finding gets its check before its fix lands.
- **Tests that span several worlds:**
  - two sleepers on two worlds at once;
  - a crossing while another world parks;
  - section 4c's crash points;
  - a 4-world soak against section 2b's budget.
- **Tests don't quiet the world to pass.** Calming weather or freezing wildlife is only for a check about something else, and never across a crossing.
  - The one allowed quieting is frame measurement. PopRoute `--check=1` freezes wildlife for a stable baseline.
  - Every test-build frame check also runs once `--live`, and off the starter.
- **No silent pass.** A check that finds nothing to test rolls again, up to a set number of tries, then fails and prints the seeds. Tests always quit themselves.
- **One `Check`** in the shared testing code. Tests that assert a bug are rewritten to assert the rule.
- **Seeds stay random every run.** At most 4 Godot runs machine-wide, through the queue scripts. Each multi-world test counts as one run.

## 9b. Host and clients
Co-op is host-authoritative (DESIGN.md 1532, 1550, 1581). To be confirmed against `prototypes/coop` before step 2b.

| What | Simulated where | Sent how |
|---|---|---|
| Worlds: physics, AI, weather, wildlife, places, survival | The host, every world in play | Only to clients on that world. Every networked object carries its `WorldKey`. |
| A sleeper's own movement and the shots they fire | The client, trusted (inputs named in one list) | To the host, which checks and applies them |
| Positions aboard a ship | The host | Relative to the ship (DESIGN.md 1088) |
| Saves | The host only | Never sent |
| View (rule 12) | Each client, for its own world | Not sent |

## 10. Merge review checklist
Reject a change that:
- adds a second `World` field, or stores a world or world system anywhere but the thing's one `World` field;
- uses `Game.Surface`, "home" or "first orbit" outside the world manager;
- reads `Game.Zone` in simulation code (it is the local view's world only);
- reads `Game.Survivor` in simulation code (use the sleeper acted for);
- adds a `+=` on a world outside `Attach`;
- adds a static holding per-world state;
- writes engine-wide state (global shader values, environment, `AudioServer`) outside the drawn world's view;
- casts a ray or query outside that world's `WorldQuery`;
- holds a reference to a thing in another world rather than its ID;
- starts a worker job without a world generation check;
- writes a second implementation of a job in section 6, or merges a replacement without deleting the old;
- compares an ID or name string to decide behaviour, or parses a world key or ID from a string;
- makes an ID, seed or stored position without the `WorldKey`;
- makes world generation depend on run order, time or threads (the fingerprint check fails);
- uses `?.` on a system;
- removes a ledger record without its holder reporting it destroyed;
- patches one case of a rule instead of fixing the rule;
- swallows an exception, or drops a record with only a log;
- moves vertices from `CAMERA_POSITION_WORLD` in a shadow-casting shader;
- lands a fix without its failing check, written first, off the starter;
- sets simulation size (such as the spawn distance) from a client's display settings;
- is frame-sensitive and hasn't passed the frame check both quiet and live.

## 11. Owner decisions, 25 Nov
1. **Worlds with no one on them stay frozen.** Nothing ticks in the background. What should have moved on is worked out from the clock when the world is made again (section 4).
2. **NPC travel is light, and later.** NPC traffic isn't tracked between worlds, systems or stars. It is placed by rule when a world is made. NPC ships may still `Move` between two worlds in play (section 2).
3. **Co-op players can be any distance apart,** systems apart included, and it must not matter. The world model has no shared frame, focus or "system you are in". The host's cost is set by how many worlds are occupied (section 2b), not by distance.
4. **Each sleeper has their own numbered cryopod and locker,** four in all, so co-op is up to four sleepers. Each pod is its sleeper's wake point and first respawn record. Each locker is a Sleepers-faction container labelled with its sleeper. Today only one pod is alive and only `Pods[0]` is a locker (structure A-6.3).

5. **A frozen world stays loaded for about 2 minutes before it is freed,** so hopping between surface and orbit doesn't regenerate it. It is frozen the whole time and nothing ticks. DESIGN.md "Zones under the hood" is reworded to match.

6. **Air and gravity follow cabin-world logic.** Inside a functioning ship or base you breathe its rooms' air and stand at its core's 1 g, whatever the world outside. Gravity follows the structure, air follows its sealed rooms: blow a base's roof off and its air vents, but you stay at 1 g while the core runs. Life support regulates oxygen and temperature in every enclosed space. Sealed volumes nest and are linked by openings; a core that's off hands gravity to its parent. Orbit is 0 g, and loose things drift there.

7. **Respawn points are cryopods we build on our structures:** one block, the start bunker's pod reused (1 × 3 × 1 cells), respawn only. The healing booth is its own block (root-fix-tasks.md F8).
8. **Up to 4 occupied worlds on a player host;** ships' cabins and destinations being prepared run on top of those 4.

**Still open:** the engine options in section 2b. These come after the step 2a prototype measures the cost.

## 12. Order of work
The jobs, in build order, with what each closes, its proof and its size, are in [root-fix-tasks.md](root-fix-tasks.md) (approved 26 Nov). That list is the order of record; this is its outline. Every task sits below everything it needs, so where this outline and the list differ, the list wins.

Each step is a root fix, merged green with its checks. A replacement merges only with the deletion of what it replaces. No step leaves the game broken.
0. **The proof first (Tier 0):** repo tools (the test queue, the quiet frame check, the owner's settings file), then one scripted merge gate with its known-failure list and a frame check against the baseline, then the harness (`WorldChoice`, `Harness` with `Arrive`, `AddSleeper` and `SaveAndReload`, the world probe) beside the multi-world prototype (T0.4, the decision gate for step 2's world model). Each later task writes its own findings' failing checks first.
1. **Small root fixes (Tier 1):** a per-world build context in place of the statics (S3); `Attach`/`Detach` with handles (S4); one settings object and rules per world (S5), with the core's 1 g a constant and a world's gravity from its own scale; every world has every system, with one oxygen figure; the same seed builds the same world; the Skiff's stalled climb.
2. **The world model (Tier 2),** in slices:
   - the galaxy from the game seed, with one star model;
   - each world its own Godot world and `WorldQuery` (rule 2), after the prototype;
   - the sleeper list, the four cryopods, claims as a field, and world things and room air out of the suit;
   - the world manager, interest points, freezing, one galaxy clock and the multi-world budget;
   - the places stack per world, then player systems reading `thing.World`;
   - `WorldKey` everywhere and the ledger for many crossings (section 4c's commit queue), both listed here because the jumps' proofs cross two sleepers at once;
   - simulation and view apart, and cabins (a freed cabin's things kept in the ship's record);
   - jumps per vessel and the transit world.
3. **References and records (Tier 3):** references across worlds as IDs and the change record per world with the clock catch-up. Then `ParkHome` goes and the start system is freed like any other, to its record in memory.
4. **One path per job (Tier 4, beside Tier 3):** harm, then the block world and salvage, then shelter and air (air volumes and the one "what am I inside" lookup), then ground, gravity and vehicles (which reads that lookup), then events, sound and markers, then shared entry points.
5. **Facts, not names (Tier 5).**
6. **After the core (owner, 26 Nov):** the climb and descent between surface and orbit, then crash safety and the saved game (the game, faction and sleeper records, and loading).

New behaviour found in the reviews is listed apart in root-fix-tasks.md as features (F1 to F9), built on the core once the owner picks them.
