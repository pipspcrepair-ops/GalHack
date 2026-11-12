# One 1 m grid: plan

*Status: written on `main` (`a2ec0fa`) on 12 Nov 2026 while the test build was out of reach. Checked against the test build (`proto/batch2`, on branch `proto/grid`) the same day: see [What the test build already has](#what-the-test-build-already-has). File and line references below are the test build's.*

## The owner's brief

> Big and small block categories are merged into a single tech tree and single 1m grid which all blueprints, vehicles and POIs will be based on, moving forward. Big components keep their larger dimensions, power and components, but ported to the 1m block grid. Weight rebalancing may be required. Small components are scaled up. The unified 1m core has the usual Base/Hover/Fly toggles. Most if not all POIs appear to be 1m blocks already.

Owner decisions so far (12 Nov):

- **Everything is built on the 1 m block.** Structural blocks (the ones that hold a structure together and shield what's behind them) are uniform 1 m. Devices take up as many 1 m blocks as they need, set by what they are and their tier, as in Empyrion, where higher-tier thrusters span several blocks. **Nothing is labelled small, large or by any other size class.**
- Doors come in variable sizes. The standard ones are **1×2×1** and **2×2×1** (width × height × depth, in metres).
- Stairs already work, for example the cryo bunker's. The test build fixed getting onto the top of some stairs without jumping, NPCs and animals not using stairs, and phasing through blocks. Main may not have these fixes.
- Next, after this: shaped blocks (wedges, half blocks and so on). Stepping onto one gets the player stuck, bouncing up and down. See [Movement on partial blocks](#8-movement-on-partial-blocks).

## What it replaces

Today there is one block model (`prototypes/building/src/Structures`) with two scale profiles in `prototypes/building/data/grids.json`:

| Grid | Cell | Used by | Stats |
| --- | --- | --- | --- |
| large | 2 m | Big Core: Base, Capital | as written in the data |
| small | 0.5 m | Small Core: Vessel, Hover | hp × 0.1, mass × 1/64, CPU × 0.03, function (thrust, power, fuel) × 1/32, storage × 1/64 |

Every block fills exactly one cell. The item layer (`shared/Items/data/blocks.json`) makes two items from each block (`steel@large`, `steel@small`), with two bills, two tech nodes and two tabs. About 40 call sites branch on `GridSize`. The full map, with file and line references, was taken on 12 Nov and is summed up under [Code to change](#9-code-to-change).

POIs already use their own block model on a 1 m grid (`poi-generator/src/Gen/Core.cs:10`, "One cell is one metre"). The plain ruins keep a 2 m lattice so they look player-built. Terrain voxels are 1 m too. A 1 m player grid lines up with both.

## 0. Before building

1. Get the test build onto GitHub, then branch from it, not from `main`. On `main` this would touch the same files as the unpushed work: building, cores, tech tree, blueprints, the slice.
2. Check the test build for work that's already done: multi-cell parts, a 1 m grid, Large Hover, door sizes, the stair and NPC fixes, and a character body that can sit. On `main` the player is a single standing figure with no joints (`slice/src/Player/CharacterModel.cs`), so the game hides it whenever you're seated, and you seem to vanish. The test build replaced the figure with one that sits, but still hides it (see below). Bring this plan up to date with what's there.
3. Pull the Empyrion and 7 Days to Die reference data off the other machine (`Z:\Steam\...\Empyrion - Galactic Survival\Content\Configuration\BlocksConfig.ecf` and the earlier research). Check every number marked **(check)** against it. Empyrion is guidance, not a spec: use its proportions (how big a part is relative to a person and to other parts, what it does for its size), then **size everything in whole 1 m blocks**, our standard. No half-cell or Empyrion-unit footprints. As before, use counts and proportions only, never copied data.

### What the test build already has

Checked on `proto/grid` (the test build, 606 commits ahead of `main`) on 12 Nov:

- **Blocks spanning several cells: none.** Every component in `building/data/components.json` is one cell. Deco is the only multi-cell thing, and it isn't stored as blocks: `BuildTool.Deco.cs` `PlanDeco` (:96) turns a piece's 0.5 m footprint (`DecoCatalogue.cs:70`) into W×H×D cells on whichever grid it's built on, per placement, with no anchor.
- **Door sizes: one.** A single one-cell `door` component; the only variant list is the armour shapes.
- **Stairs: built from blocks, no stairs shape.** `shapes.json` has cube, slope, corner, invcorner, wedge, half, plate and cylinder. The bunker (`slice/src/Start/Bunker.cs`, 0.5 m cells, :47) builds each step as a column 0.5 m up and two cells (1 m) deep, 1.5 m wide, with a `slope` on the last half metre of each riser (:525) and a ramp off the top step (:529-530); the top tread stands 2 cm over a whole metre (:49).
- **Stair, NPC and phasing fixes: in.** Not on `main`: stair lips (`4192c63`), creatures taking stairs as 45° slopes (`99ab233`), routes walking stairs at their height (`c150af1`), real collision capsules for people (`2042258`), nothing inside furniture (`9243797`), insects out of walls (`9d5c54d`). Still open: half blocks and ramps bouncing the player, standing on built structures, the Outpost bounce (the root movement bug, section 8).
- **A body that can sit: yes, but it's hidden.** `CharacterModel.cs` now runs on the people's `HumanRig` and walks, runs, crouches and sits (:15, :60, :187-188); `Game.Gestures.cs:113-120` poses it on the chair. `Game.cs:888` still culls it whenever you're seated (`Survivor.Seated == null` in the cull mask), and the hands hide too (`Hands.cs:145`). The chair is still one 0.5 m cell.
- **Large Hover: not wired.** `CoreMode` is still `Base, Capital, Vessel, Hover` with the grid clamp (`Structure.cs:11`, `:76-81`); a large-hover blueprint loads in Capital mode (`BlueprintLibrary.cs:42`, `:114`).
- **Rotation: all 24 orientations reachable.** R turns about up and T about X (`Game.cs:564-565`, `BuildTool.cs:147`); together they reach all 24. There's no Z key, and one-face components auto-orient to the clicked face (`BuildTool.cs:849`).
- **Core removal: still refused** (`Building.cs:217`, `CoreHoldsStructure`).
- **Cockpits: none** (`Catalogue.cs:142`, "There are no cockpit components").

## 1. One grid

- One `GridDef`: `id "1m"`, `cellSize 1.0`, and every scale 1. Stats in the data are the block's real stats, so nothing is scaled at load.
- `GridSize` and `GridDef.Size` go. So do `HpScale`, `MassScale`, `CpuScale`, `CostScale`, `StorageScale`, `FunctionScale` and `SmallCost`.
- **No CPU** (owner, 12 Nov). **Power draw is the limit** on what a structure can run; CPU's only real job in Empyrion was capping build size. Remove CPU costs, CPU tiers, CPU extenders and `extendersPerTier`. For power to do the job: generators cost fuel, mass and space, with bigger tiers burning more; devices draw while working (thrusters firing, shields absorbing, fabricators running) and little or nothing idle; generators and solar panels fill one capacitor bank and every device draws from it; when draw outruns supply the bank drains, and once it is empty the whole system stutters while it waits for power to bank up again (no priority list). When the power goes out completely, everything goes with it: gravity, life support, thrusters, all of it. Performance stays capped by blocks per axis. One storage cap: start from today's large value (320,000 SU).
- Reference cap: about 250 blocks per axis still applies. In metres that's half what large structures could reach, which is fine for now.
- Blueprints still write a `grid` line, so old files can be told apart. `grid 1m` is the only grid that's built. Old `grid large` and `grid small` files are replaced, not converted ([section 6](#6-blueprints-and-generated-structures)).

## 2. Blocks that span several cells

This is the core engineering piece. It lands first, on its own, before the grid changes.

**Data.** A component gets a `size: [x, y, z]` in cells, local to the block (default `[1, 1, 1]`). Its `parts` boxes stay in the −0.5 to 0.5 range, but across the whole size rather than one cell, so today's large parts scale up to 2×2×2 with no edits.

**Storage: anchor and fillers.**
- The anchor cell holds the block's type, rotation, flags, hp and paint. Every other cell in its size is a filler that points back to the anchor.
- A filler is the same `Type` plus an offset to the anchor. Three `sbyte`s in `Block` allow up to ±127 cells, so even large hangar doors fit.
- `Structure.TryGet` on a filler returns the filler. A new `AnchorOf(p)` gives the anchor.

**Rules.** Each of these works off the anchor:

| System | Rule |
| --- | --- |
| Placement (`Building`, `BlockPlacer`) | Every cell of the rotated size must be free. It attaches through the outer faces of the size. Mirror symmetry mirrors the size too. |
| Removal and pick-up | Removing any cell removes the whole block and returns one item. |
| Damage (`Damage.cs`) | Hits on a filler go to the anchor. A block has one hp pool, and it breaks as a whole. |
| Meshing (`ChunkMesher`, `StructureMesh`) | Only the anchor draws, with parts scaled to the size. Fillers draw nothing. A block crossing a chunk border dirties every chunk it touches. |
| Colliders (`StructureBodies`, `StructureBridge.ChunkColliders`) | Built from the anchor's parts scaled to the size. A filler adds no box of its own. |
| Faces (attach, occlude, seal) | Faces inside the size don't exist. Outer faces take the component's face lists, so a `+z` face means the whole +z side of the size. |
| Rooms and air (`Rooms.cs`) | Interior cells are solid. Outer faces seal as listed. A 2×2×1 door seals its whole opening while it's closed. |
| Connectivity, stats, power |  Counted once, per anchor. |
| Blueprints | Save anchors only. Fillers are rebuilt on load, and a missing or overlapping filler is a load error. |
| Ghost preview, HUD | Show the rotated size, with a red cell wherever it's blocked. |
| Navigation (`NavGrid`) | Interior cells are solid. A door counts for every cell of its opening. |

Tests for this phase, in `building/tests`: placement in all 24 orientations; refusal on any blocked cell; removal from a filler; damage from a filler; the same results from blueprint round trips; face masks on outer and interior faces; the room seal of a 2×2×1 door.

## 3. Block sizes and stats

### The base block and plating (owner, 12 Nov)

There are no separate armour blocks per material. Every structural block starts as one cheap block, and its material comes from plating.

- **Base block:** a cheap, light, weak polymer made from rock, the most plentiful resource. Every shape comes from it.
- **Plates:** general crafted items (iron plates, glass plates and so on), made from ingots at a fabricator. They're not "armour plates": plating is just one use of them.
- **Plating gun:** sprays plates onto any block, devices included. The plating sets the block's hit points, armour, resistances and mass.
- **Changing or stripping plating:** spraying a different material, or stripping the plating, gives back the old material's worth in hit points, rounded down. A damaged block returns less.
- **Repair:** topping a block back up uses plates in proportion to the damage.
- **Shapes:** a smaller shape costs fewer plates and has hit points in proportion to a full block. A half block is 0.5 m tall, the same height as one of today's small-grid stair risers.
- **Paint** stays a separate layer over the plating.
- **Under the hood:** a plated block is just a different block type ("base block plated with iron" is its own type id). Nothing extra is stored per block, which keeps blocks as small as voxel blocks need to be. Main already has an upgrade path between materials (`upgradesTo`).
- **Windows:** a T0 window is clear polymer, free, and breaks from one shot of any weapon. Glass plates are its first real plating. Only transparent plates go on windows, which replaces window tiers.
- **Weak points are a choice:** landing pads, lights, doors and forcefield emitters can all be plated. In Empyrion they were the weakest parts of a ship with no way to harden them.
- **Starting numbers** for each plate material's hit points and mass per 1 m cube: take Empyrion's proportions as guidance **(check)**.

### Devices: each one is its own part, sized in 1 m blocks

**The 1 m block is the base of everything.** Structural blocks (armour and its shapes, the blocks that hold a structure together and shield what's behind them) are exactly one cell. Every device is measured in those same 1 m cells and takes up as many as it needs. That number is set by what the device is and its tier. As in Empyrion, a higher-tier thruster is simply a bigger part that takes several blocks.

**No size labels.** A device is never "small", "large", "big" or "compact". There's no size family, no device is limited to one kind of structure, and any device can go on any structure.

**Every device is its own data entry**, with its own footprint (`size` in cells), thrust, power, fuel, storage, mass, hit points and recipe.

**Tier ladders.** Today's two versions of each device are not kept as a pair. They are reference points on that device's tier ladder:
- **Today's small part**, scaled up to 1 m, gives roughly the bottom rung: one cell, with stats × 8 from the small grid.
- **Today's large part** gives a higher rung, filling its real 2 m space as 2×2×2 cells, with today's stats.
- The rungs between and above, and each one's footprint, come from Empyrion's device data **(check)**. They are not invented here.

The shape of a ladder, using the thruster as the example. The numbers below are placeholders until they're checked against the Empyrion data:

| Device | Footprint (cells) | Thrust | From |
| --- | --- | --- | --- |
| Thruster T1 | 1×1×1 | about 300 kN | today's small thruster × 8 |
| Thruster T2 | 1×1×2 | **(check)** | |
| Thruster T3 | 2×2×2 | 1200 kN | today's large thruster |
| Thruster T4 | for example 2×2×3 or 3×3×4 | **(check)** | |

Footprints don't have to be cubes or even. A thruster can be long, a hover plate flat.

### Which devices tier up (owner, 12 Nov)

**Tiered**, with bigger footprints at higher tiers. The full list is in [section 10](#10-other-decisions-from-the-12-nov-brainstorm). Solar panels scale by area, so they're flat parts.

**The efficiency rule.** A higher tier has more mass and draws more power, but it is more efficient than lower tiers filling the same space. For example, one 2×2×2 Thruster T2 beats eight 1×1×1 Thruster T1s on output per mass and per unit of power. Lower tiers keep three things:
- **Fit:** they go where a big part can't.
- **Redundancy:** losing one of eight costs an eighth of the output.
- **Price:** they cost less and unlock earlier.

**Fixed, with no tiers:** see [section 10](#10-other-decisions-from-the-12-nov-brainstorm). Cloning bays are for home spawns.

### Starting points per device

| Device | Bottom rung (from today's small part) | Today's large part becomes | Notes |
| --- | --- | --- | --- |
| Core | 1×1×1 | — | **One core**, with today's large core's stats. See [section 4](#4-one-core-three-modes). |
| Command chair, cockpits | whole 1 m blocks, Empyrion proportions **(check)** | — | **Owner, 12 Nov (agreed with the project director on the other device):** today's small-grid pilot seat is 1×1 (0.5 m). That is far too small: the character doesn't sit in it, they just disappear. Command chairs take Empyrion's proportions as guidance, sized in whole 1 m blocks, with a footprint the seated character visibly fits in **(check)**. **Cockpits come back** as parts several blocks in size, which reverses `docs/DESIGN.md:843` ("there's no cockpit component") and the note in `Catalogue.cs:142`; update both. Test: the seated pose shows the character in every chair and cockpit, with the camera at their eyes. |
| Door | 1×2×1 | 2×2×1 | Owner: one door item, with its size picked from the block's variant list, the same way you pick a shape. These two are standard, and bigger sizes are more entries in the list. There's no 1×1 door. Wide vehicle openings use forcefield portals instead (section 10). |
| Thruster | 1×1×1 | 2×2×2 | The ladder above. |
| Generator | 1×1×1 | 2×2×2 | **(check)** |
| Fuel tank | 1×1×1 | 2×2×2 | **(check)** |
| Container | 1×1×1 | 2×2×2 | Storage goes with volume: 1000 SU at 1×1×1, 8000 SU at 2×2×2. |
| Gyro | 1×1×1 | 2×2×2 | Torque is 5000 large and 600 small today (`handling.json`) **(check)**. |
| Hover plate | 1×1×1 | 2×1×2 | Lift 400 kN and ride height 3 m at 2×1×2; ride height 1.5 m at 1×1×1 **(check)**. |
| Landing gear and docking pads | 1×1×1, plus longer versions | higher tiers are bigger pads **(check)** | **Tiered, like the other scaling devices (owner, 12 Nov): bigger pads at higher tiers.** Length versions within a tier are for nesting. **An attachment point, not a load-bearing part: in effect a docking pad** (owner, 12 Nov). It marks where a craft sets down and attaches: to the ground, or to another vessel to park on it. **It extends and retracts by itself, with no button.** Today it drops below 15 m and under 20 m/s, and G overrides it (`ToggleGear` in `shared/Physics/Core/VesselController.cs:188`, bound in `slice/src/Game/Game.cs:649`, `ship-handling/src/Presentation/Main.cs:145` and `physics-sandbox/src/World/Main.cs:100`). Keep the automatic rule and remove the override and the G binding. It carries no load and has no leg-count or mass check. A telescoping leg is optional, and it is for function as well as looks: it extends until it reaches the surface below, so the gear touches down and attaches on uneven ground or a host's deck, each foot reaching its own ground as in capital-base. Gear without one attaches only where its own face touches. **Length versions** (for example 1×1×1, 1×2×1, 1×3×1) are for gear nested up inside the hull in a gear bay, where the leg has to reach past the hull to touch down. They differ only in length, not tier. **A weak point:** low hit points, so gear breaks first on a hard landing and can be shot off. Broken gear loses its attachment point until it is repaired or replaced. Today's gear is a load-bearing suspension with legs reaching 1.4 m (small) and 4 m (large), set in the first ship-handling commit (`cb67319`) with no stated reason. That 4 m drop is why the physics-sandbox README says to use the jetpack to get in. Replace it with the attachment rule. |
| Turret, gatling, rocket pod | 1×1×1 | 2×2×2 | **(check)** |
| Warp drive, warp tank | 1×1×1 | 2×2×2 | **(check)** |
| Constructors (portable, small, large, advanced), deconstructor | already tiers | already tiers | One part per tier, each with its own footprint. The tier names stop meaning grid size **(check)**. |
| Light | 1×1×1 | — | A floodlight can be a bigger part later. |
| Ladder | 1×1×1 | — | Stack them. |
| Hyperdrive T1 and T2, fold tank | — | 2×2×2 | Large-only today. Hyperdrives might be longer, for example 2×2×4 **(check)**. |
| Factory (blueprint factory) | — | — | **Replaced by the blueprint gun** (section 10). |

**Ids and names go by tier, not size**, for example `thruster-t1` ("Thruster T1") and `thruster-t3`. Each item's `was` list maps the old ids across, so old saves and tests still load:

- `steel@large` and `steel@small` → the base block plated with iron. Each old armour material maps to the base block plus that material's plating.
- `thruster@small` → `thruster-t1`
- `thruster@large` → the tier with today's large stats (`thruster-t3` in the placeholder ladder)
- `big-core` and `small-core` → `core`

### Rebalancing

**Flight should hold.** A small craft rebuilt in 1 m blocks with bottom-rung parts gets 8 times its mass, from both armour and parts, and 8 times its thrust and lift. So it flies about the same.

**Cost is what moves:**
- Structural blocks are now a cheap base block plus plating, so armour cost is the plates sprayed on, scaled by shape.
- Bottom-rung parts at 8 times today's small recipe get dearer.

Fly the Skiff and the Gadfly after conversion. Retune costs against Empyrion's **(check)** before tuning mass.

## 4. One core, three modes

- **`CoreMode` becomes `Base`, `Hover`, `Flight`.** Old `Capital` and `Vessel` load as `Flight`, and old blueprints with `mode capital` or `mode vessel` read as Flight. The mode clamp in `Structure.Mode` (`Structure.cs:76-81`) goes. Every structure has all three modes.
- What each mode needs is today's `ModeFit.Missing`, applied to every structure:
  - **Base**: nothing. It anchors where it stands.
  - **Hover**: a command chair, hover plates with enough lift, thrusters and power.
  - **Flight**: a command chair, thrusters with enough upward thrust and power.

  Nothing refuses a switch: the list is only a warning, and the switch stays on the CORE tab in warning stripes.
- **Large Hover arrives with this**: hover tanks, land carriers and mobile bases, as `DESIGN.md` already describes. Hover plates give lift only near the ground, so a hover structure can't reach orbit.
- **Clean lift-off and capital fuel burn** apply to every Flight structure. They stay on their own playtest item; capital-base has the reference implementation.

### What replaces the four classes

Today `StructureClass` (Base, Capital, Small, Hover) and the physics `VesselClass` decide five things. Each now comes from the structure itself:

| Today | Uses class for | After |
| --- | --- | --- |
| Area margin (`slice.json:110`: 30 m large, 12 m small) | building and storage range | One rule: 12 m + `sizeShare` × half the structure's longest side (sizeShare is already there). A 40 m base gets about 30 m. |
| Top speeds (`handling.json`: hover 50/50, small 70/130, capital 60/100) | speed caps | Caps by mass band: under 100 t gets 70/130, 100 t and up gets 60/100, and Hover mode gets 50/50 **(check)**. |
| `SmallVesselMaxTurn` (`Game.cs:644`, `GameData.cs:269`) | turn cap | Same mass band. |
| Hosts (`PhysicsWorld.IsHost`: Base, or Capital) | who can carry parked craft | Any structure hosts any craft whose landing gear attaches to it. See **Parking** in section 10. |
| capital-base `IsLarge` (Home, hosting, pumping) | Home flag and so on | Any structure can be Home. Hosting as above. |

## 5. One tech tree

- One **STRUCTURES** tab replaces "Base & Capital" and "Small & Hover". The tabs become Structures, Tools, Weapons, Deco and Misc.
- The root is the **Core**, at level 1 and free.
- Each device is a tier chain: T1 → T2 → T3 and so on, each tier the child of the one below. Bigger footprints come with higher tiers, so early craft are built from one-cell parts and multi-block parts unlock as you level. Start each T1 at the lower of today's two unlock levels for that device, and put the rung with today's large stats at today's large level. Rungs between and above come from Empyrion's unlock levels **(check)**.
- The hover branch runs in parallel with the flight branch. Today `thruster@small` hangs under `hover@small`; keep that order: Thruster T1 under Hover plate T1.
- The base block and its shapes come with the Core. Each plate material is its own unlock.
- `docs/tech-tree-pacing.md` (343 nodes, levels 1 to 25) was written for two tabs. Redo its Structures column from the two old ones; one tree should shrink it by about 30 nodes.

## 6. Blueprints and generated structures

**Replace every blueprint; don't convert them** (owner, 12 Nov). Today's blueprints are deeply flawed: ceilings far too low, and command chairs far too small. New blueprints are designed from scratch on the 1 m grid to these standards:
- **Ceilings 3 to 4 m high, 4 m preferred.** POIs already use 4 m storeys (`poi-generator` README), so ships, bases and ruins match.
- **Command chairs and cockpits at proper size,** so the character visibly sits in them.
- **Doors at the standard 1×2×1 and 2×2×1;** forcefield portals for vehicle openings.
- **The starter set** from the playtest list: Small (the Skiff, a hover rover, a light fighter, a shuttle) and Large (a starter base, a hover tank, a hauler, a small capital ship), as data files. With no size labels, "Small" and "Large" here only describe the craft.

Files to replace: `lancer`, `bastion`, `outpost`, `workshop`, `pad`, `tender`, `starter` (the Skiff), `pod`, `prospector`, `gadfly`, `wreck`, `barge`, `corsair`, `sandflea`. Tests that load them (capital-base, manufacturing, physics-sandbox, slice) move to the new set.

**Generated structures.**
- **The cryo bunker** (`Bunker.cs`) is built from 0.5 m small-grid blocks today: a room 6.5 × 3 × 4.5 m inside (a 3 m ceiling), and a stairwell 1.5 m wide with steps 0.5 m up and 1 m deep. It's rewritten in 1 m cells. A **stairs shape** for the base block (two 0.5 m steps in one 1 m block) keeps the same riser. The stairs use half blocks on the 1 m grid, which keeps the same 0.5 m riser, so they should feel the same. The owner says the stairs work, so keep their riser and run.
- **Derelicts**: written in 2 m cells. Rewrite the generator in 1 m cells, to the same standards as the new blueprints (4 m ceilings, proper chairs).
- **Blueprints written in tests** (`building/tests/BuildTest.cs`, `slice/tests/PlaceTest.cs`, `CoreTest.cs`, `SeatTest.cs`, `DarkTest.cs`, `Staging.Dark.cs`): redo them by hand.

**POIs.**
- POIs already use 1 m cells. The playtest item "POIs on the 2 m grid, using the player's blocks and shapes" becomes "POIs on the player's 1 m blocks and shapes".
- The 2 m look of the plain ruins can stay as a lattice of 1 m blocks.
- Vehicle wrecks "built from small-grid blocks" become 1 m blocks.

**Deco.** Footprints are in 0.5 m units today (`shared/Items/Core/DecoCatalogue.cs:70`, `SmallCell` and `LargeCell`, `LargeCells` at 4:1). Make it one 1 m footprint, rounded up, with the mesh scaled to fit as now. Pieces that were on both grids become one.

## 7. Saves, ids and UI

- Saves aren't in the slice yet, so the `was` lists plus a blueprint version bump cover migration.
- The capital-base and saves prototypes round-trip blueprints. Move their fixtures to the new blueprint set.
- **UI.**
  - Remove the S/L grid badges (`Visor.cs:563,668-670`, `GridView.cs:182,437`, `YouSection.cs:384,401`).
  - The `NAME · LARGE/SMALL` caption (`CoreSection.cs:318`) becomes the structure's name and mode.
  - Tooltips ("Steel block (large)", "large grid") show the part's size instead, for example "2 × 2 × 2 m".
  - `TechTree.cs` regexes that strip "(large|small)" go.
- Claiming a wreck needs a core of the matching grid (`Spaceflight.Hands.cs:223-227`, and the build tool's wreck claim at `BuildTool.cs:411-412`). That rule goes: any wreck takes the core.
- In `Vessel.Orbit.cs:63`, a claimed wreck in orbit becomes a Flight structure. A grounded one becomes a Base.
- XP per block placed (`progress.json`: `blockLarge 3`, `blockSmall 1`) becomes a per-item value in the data. By default it is 1 per 1 m cell a part fills, capped at 8, and 1 for an armour block. Assembly time (`blocks.json:38`: 0.4 s small, 1.6 s large) becomes per item too, by default 0.4 s for a one-cell part and 1.6 s for a 2×2×2 one.

## 8. Movement on partial blocks

This comes after the grid, because the grid changes every collider. The owner's report: stepping onto a half block, wedge or other partial shape that the player built gets them stuck, bouncing up and down.

On the 1 m grid:
- A full block is 1 m, which is a wall: step height is 0.6 m (`shared/Physics/physics.json:114`).
- A half block is 0.5 m, which is a step.
- Slopes are walkable up to 45° (`maxSlope`).

So half blocks and slopes become the normal way up, which makes this bug more important.

**Check the test build first.** It fixed stair, NPC and phasing problems that main may still have.

To do:
1. Write a stand-on test: the player walks onto, over and off every shape in every one of its 24 orientations, at 1 m cells. Pass means no vertical bounce bigger than a few millimetres over 2 s standing still, and no stuck frames walking across.
2. Find where the collider and the drawn shape differ. Shaped blocks may collide as a box or a stepped hull while they're drawn as a slope. The stuck guard nudges by `cell × 0.5` (`StructureBridge.cs:439`), which is 0.5 m at 1 m cells. The step-up and ground-snap logic could be fighting on a top that isn't level.
3. Fix it, then run the same test for NPCs and animals (`NavGrid` headroom, steps).

**The owner's lead (10 to 11 Nov):** it feels like being trapped inside a block, probably because a floor is a full-cell block in the space the player occupies. **Floors are never thin blocks**: a floor finish is painted onto the face of the block below. Apply this to player builds and to places (place floors and paths are thin plates today, per `Homes.cs`). **Urgent: this is the first job after the grid** (owner, 12 Nov).

## 9. Code to change

These are grouped by phase. Line numbers are the test build's (`proto/grid`, 12 Nov).

1. **Blocks that span several cells**:
   - `building/src/Structures`: `Structure.cs`, `Building.cs`, `Catalogue.cs` (face tables per outer face), `Damage.cs`, `Rooms.cs`, `Connectivity.cs`, `NavGrid.cs`, `Blueprint.cs`
   - `building` presentation: `ChunkMesher`, `GhostPreview`, `StructureView`, `DamageFx`
   - `shared/Physics/StructureBodies.cs`
   - `physics-sandbox/src/Bridges/StructureBridge.cs`
   - slice: `StructureMesh.cs`, `VesselDamage.cs`, `BlockDoors.cs`, `BuildTool.cs`
2. **One grid and the data**:
   - `building/data/grids.json`, `materials.json`, `components.json`
   - `physics-sandbox/data/vessel-parts.json`
   - `shared/Items/data/building-parts.json`, `building-materials.json`, `blocks.json`, `deco.json`, `machines.json`
   - `ItemCatalogue.cs` (`AddBlock`), `BlockBills.cs`, `Items.cs` (`SmallGrid`), `Validation.cs`, `BuildingBridge.cs` (`IsSmall`, `BlocksOf`, `ItemOf`, `BillOf`)
   - `BuildTool.cs:258-260,693-699` (wrong-grid refusals), `BuildTool.Deco.cs`
   - `Progress.cs` (tabs), `TechTree.cs`
3. **One core**:
   - `Structure.cs` (`CoreMode`, the clamp), `Building.cs:306-322`, `Vessel.Mode.cs` (`ModesOf`, `Missing`)
   - `StructureBridge.cs:284-313` (handling by grid and class), `handling.json`
   - Landing gear as an attachment point: replace the load-bearing gear suspension in `shared/Physics`; remove `ToggleGear` and the G key from the slice, ship-handling and physics-sandbox, and from their README control tables
   - `PhysicsWorld.cs:275` (`IsHost`), both `StructureClass` enums and `VesselClass`
   - `StructureArea.cs`, `GameData.cs` (`AreaRules`), `slice.json:84`, `Game.cs:520`
   - capital-base `Craft.cs`, `CapitalWorld.cs`
4. **Blueprints**: the new blueprint set replacing every `.gbp` listed in [section 6](#6-blueprints-and-generated-structures), `Bunker.cs`, `Derelicts.cs`, and the test blueprints.
5. **Deco, POIs, UI and docs**:
   - `DecoCatalogue.cs`, the UI files in [section 7](#7-saves-ids-and-ui)
   - `DESIGN.md` §11 (the grid table, "Two classes, Large and Small", class-compatible blocks), `prototypes/building/README.md`, `shared/Items/README.md`, `docs/decided-vs-built.md`
   - Out-of-date notes found along the way: `BlockPlacer.cs:35-36` and the shared/Items README still mention class checks and `WrongClass`, which no longer exist; `Items.cs:112` has a comment with no property; the README's count of 63 block items (`shared/Items/README.md:29`) is out of date (66 now).
6. **Movement**: [section 8](#8-movement-on-partial-blocks).

**Tests that assert the old split and need rewriting:**
- building: `BuildTest.cs` (CPU tiers and extenders, which go; small hp, container caps, Small Core modes, small recipes), `OcclusionTest`, `DamageTest`, `PlayTest` (G key)
- manufacturing: `ItemsTest.cs` (items per grid, two cores, per-grid recipe paths), `ProductionTest.cs` (`ClassOf`)
- slice:
  - `BuildTest.cs` (devices on both grids, the small-grid refusal, classes, area margins, six tabs, tooltips)
  - `CoreTest.cs`, `PlayerTest.cs`, `DamageTest.cs`, `OrbitTest.cs`, `Playthrough.cs`
  - the start and bunker tests (0.5 m risers)
- physics-sandbox: `PhysicsTest.cs` (the 0.5 m lip on the barge as a step; the tender as host)
- capital-base: `CapitalTest.cs`
- deco: `CatalogueTest.cs` (footprints)
- terrain-digging: `DigTest.Contact.cs` (2 m and 0.5 m contact cases, add 1 m)

Each phase ends with `tools/test-all.ps1` green, as every merge does.

The decisions in section 10 (plating, the capacitor bank, shields, forcefields, the comms array and the rest) aren't mapped to code here yet. Map them once the test build is back, since it may already have some of them.

## 10. Other decisions from the 12 Nov brainstorm

These came up while planning the grid. Where they conflict with anything above, these win.

**Sizes from Empyrion.** Empyrion's small blocks are 0.5 m and its large blocks 2 m, so we meet halfway: take Empyrion's block counts as 1 m blocks. Small-ship parts come out twice their Empyrion size, and capital-ship parts half theirs. Use Empyrion's proportions as guidance, never its units.

**Cores and structures**
- One core, a simple block with **no tiers**.
- You can only place blocks on a structure with a core you own, or that your faction owns. You can place a core, build, then remove it. You can't place a core on a structure that already has one.
- **Claiming:** a core can go on any coreless building or wreck, and it becomes yours as it stands.
- **Welding:** a coreless structure welds onto a ship and becomes part of it. Two cored structures can't be welded together. A welded building lifts off if there's enough lift, cutting the ground free.
- **Losing the core:** the structure becomes a coreless wreck that anyone can claim.
- **Sections cut off from the core** become coreless wrecks. Main already does this (`Connectivity.cs`), so keep it working with blocks that span several cells. **Nothing floats on a planet or in a cabin space** (owner, 12 Nov): there a cut-off piece falls or collapses. In space it may drift.
- **Co-op:** all players are one faction (`DESIGN.md`), so everyone shares every structure.

**Progression**
- XP gives levels, and levels give unlock points to spend on tech tree nodes (as on main: level cap 25, points keep coming after it).
- **Unlocks are the gate.** A device you haven't unlocked stays dark on your structure, even on a claimed wreck, until you unlock it. You can still salvage it or strip its plating.

**Power** (see also [section 1](#1-one-grid))
- No CPU and no device caps. Power draw is the limit.
- Generators burn fuel, and fuel has to be gathered. There are three kinds of fuel: **generator fuel, warp fuel and hyperspace fuel.** **Generator fuel comes in several options, following Empyrion's generator fuel options** (owner, 12 Nov), each its own item with its own energy per unit and source **(check)**. Empyrion's generator fuels, for reference: bio fuel (from plant fibres, the basic one), promethium packs, hydrogen and fusion cells. Its warp fuel is pentaxid.
- **No water devices.** Air comes from sealed rooms and the life support block, so there is no water-to-air chain and no pump placed in water (Empyrion's water and O2 devices are not carried over). Ice keeps a place as an ingredient in some fuels.
- **Bases are cheaper to run** because they don't power hover plates or thrusters. They still pay for life support, forcefields, fridges, fabricators and every other powered device.
- **Capacitors** form the shared bank. They **leak** (a bank slowly loses charge) and are **volatile** (they explode when destroyed), so stacking them doesn't pay.
- **Life support** is a small flat draw while anyone's aboard: at most 4 players, no crew NPCs. No vents: every sealed room has air. You top up your suit's air at the life support block. Air has no material cost for now.
- **Gravity** comes from a gravity generator part, not the core, and it draws power. This reverses "there is no separate gravity generator" in `DESIGN.md`.

**Energy flow:** generators and solar panels → capacitors → anything that needs energy.

**Shields.** A shield generator puts a bubble around the vessel, and the bubble takes hits before the plating.
- **It uses energy to build up.** Switched on, a 500 hp shield climbs rapidly from 1 to 500. A hit subtracts and it keeps rebuilding: at 250, a 50-damage shot takes it to 200, and it carries on from there.
- **Hitting 0 or below fries the generator,** which must be repaired before it works again.
- **It stops lasers, rockets and similar fast projectiles.** It doesn't stop shots fired inside the bubble, or collisions with ships and structures.
- **Several shields per ship, as long as their bubbles don't touch.** One fried generator only exposes its own area. **Bubbles that touch fry every shield generator involved,** so they can't be stacked. This applies wherever the bubbles meet: two on your own ship, or yours and another ship's.

**Dismantling wrecks.** The tool dismantles coreless wrecks anywhere, returning each base block plus its plating's worth in hit points, rounded down. The build tool skips wrecks entirely (`slice/src/Construction/BuildTool.cs:324`; claiming one with a core is `:402-419`), and only orbit has a salvage action (`Spaceflight.Hands.cs`), so this extends it to the ground.

**Welding a loose piece back on.** Aim the tool at a coreless piece touching or resting on your structure and weld. The piece snaps onto your structure's grid at the nearest aligned spot, rounding its position and rotation, so there's no lining it up by hand. The piece moves, never your structure, so Base mode doesn't matter. If it can't snap cleanly (tilted too far, too far off the grid, or the spot is blocked), the tool says why, and you dismantle and re-place the blocks instead.

**Tiered devices** (bigger, heavier and hungrier, but more efficient than lower tiers in the same space): thrusters, generators, storage containers, fuel tanks, landing and docking pads, warp drive, hyperdrive, fabricators, capacitors, shield generators, forcefield emitters, hover plates, gyros or RCS, solar panels, drill modules (T1 mechanical, T2 laser turret, T3 laser turret), and the comms array.

**Fixed devices:** cloning bay, medical bay, command chairs, gravity generator, life support block, refrigeration, harvester module, scanners (specialist types such as deep-space scanners later), teleporter, spotlights, core, doors, grav lifts, ladders, lights.

**Parts**
- **Doors and landing gear:** one item each. The size or length is picked from the block's variant list, the same way you pick an armour shape. Cost and hit points scale with the size.
- **Forcefield portals** replace ramps and cargo doors. They're built from **emitter blocks** placed along two opposite edges of an opening, and the field fills the rectangle between them, so a portal can be any size, including very long and thin. The field seals air but lets players and vehicles through, and it only seals while powered. Power draw scales with the field's area. Options such as who can pass sit on the emitters.
- **No repair bays.** Repair is by the plating gun only.
- **Cloning bays:** build as many as you like. You nominate one as where you respawn.
- **Teleporters:** no range limit. Only sending needs power, and only at the moment of sending: nothing is drawn while idle, and the receiving end needs no power at all. The destinations you can pick are every teleporter the sleeper faction (the players) has built.
- **Grav lifts,** borrowed from Empyrion: a lift shaft you step into to float up or down between decks. Each block is **2×2** across so the shaft isn't cramped, and you stack blocks to make the shaft as tall as you need. It costs almost nothing to run, but it still needs power, so it stops when the power goes out and anyone riding it drops down the shaft. It's fixed, with no tiers.
- **No ramps** for now. Ramps in Empyrion were mostly decorative and a design nightmare.
- **Cargo:** one storage pool per core, and every container adds its capacity. There are no extension blocks and no armour locker: gear goes in regular storage.
- **Comms array:** with one, you get a structure's panels from anywhere within range: send items straight to its storage, manage the core's settings, set the fabrication queue. Without one, you're limited to its area radius. Range by tier: T1 a few kilometres, T2 the whole planet and its orbit, T3 the star system **(check)**. It draws power, and it can also set the range for auto-transmit to Home. Since any base can hover or fly with you, it matters most for what you leave behind: harvesters, outposts, parked ships.
- **Harvester module:** mulches whatever you drive it into (trees, creatures, people) into a bloody mist. The drops go into the cargo pool, and a person gives a little biomass along with their gear.
- **Drills:** T1 mechanical (1×6×1, short range), T2 laser turret (a small version of Empyrion's capital drill turret), T3 laser turret. There's no separate capital-ship drill turret. Costs are rebalanced from Empyrion's.
- **Blueprint gun** replaces the blueprint factory. Point it where the blueprint goes, pick the storage near the core to draw from, and manage the build with the gun. If you're short of plates, it builds bare base blocks and you plate them later.
- **Food:** the replicator makes food from biomass only. Better, longer-lasting or more filling food comes from unlocks. No grow plots, no food recipes.

**Weapons.** Use [docs/weapon-ideas.md](weapon-ideas.md). Its mounts become sizes:
- hand: a hand-held item
- placed: 1 cell
- hover and small: merged into fixed guns
- turret: mid-size
- capital heavy: large

Ammo comes from the core's storage, and energy weapons draw from the bank. Ship weapons start with machine guns, rockets, lasers and railguns. Add this mapping to the weapons doc once the test build's newer copy is back.

**Names.** Devices are named by tier ("Thruster T1", `thruster-t1`). Model names are only for exotic or unique components you find. Constructors are the Portable fabricator plus Fabricator T1, T2, T3.

**Parking.**
- If your landing gear attaches to a structure, you're parked on it and ride along with it.
- A parked craft's mass adds to its host's, so it's included in the cost to move: the host's thrust has to cover the total.
- **Moving at all unparks you.** If a parked craft moves even a little under its own power, it detaches.
- **Chains carry down.** A parked on B, B parked on C: when C moves, A and B come along, and C's thrust carries all three. When B moves, it detaches from C and takes A with it.
- **One way only.** Two craft can't be parked on each other: one parks on the other.

**Pocket worlds: what is a child of what.** Every body (player, creature, crate, vessel) has exactly one parent: the ground's world, or a structure. A child lives in its parent's still world, so however the parent flies, what's inside it never notices.
- **Becoming a child.** A body becomes a child of a structure when either holds:
  - **Support:** it's standing or resting on the structure (a character's feet, a vessel's attached landing gear).
  - **Mostly enclosed:** most of the space around it is that structure, even if it touches nothing. This covers floating in a hangar in zero-g, or a small ship flying inside a big ship's bay.
- **Innermost wins.** When more than one structure qualifies, the body takes the innermost. You in a small ship inside a big ship: you're a child of the small ship, and it's a child of the big ship.
- **Leaving.** A body leaves only when neither condition holds. It then moves up **one level** to the next parent out, not straight to the ground. Re-parenting always keeps the body's position and velocity in the world, as `Riding.EnterCabin` and `LeaveCabin` already do.
- **The "mostly enclosed" test.** Cast 26 rays from the body (up, down, sides and diagonals) and count how many hit the structure within a short distance.
  - A body becomes a child above about two-thirds hitting, and leaves below about half. The gap (hysteresis) stops flickering at a hangar's mouth.
  - Check only bodies inside a structure's bounding box, a few times a second.
  - Examples: a hangar with an open mouth is enclosed; a bay open at both ends is enclosed; an open-topped cargo bay is borderline; a flat deck is not (only resting on it makes you a child).
- **Child versus parked.**
  - Being a **child** means being simulated in the parent's world.
  - Being **parked** is stronger: attached by gear, carried exactly, and with its mass counted toward the host's thrust (see **Parking** above).
  - A small ship lifting off inside a hangar unparks, but stays a child until it flies out.
- **On main today** this is two separate mechanisms, each one level deep:
  - **Characters** ride a structure's "cabin world" (`shared/Physics/Core/Riding.cs`). Leaving drops them straight into the ground's world.
  - **Vessels** park on a host in world space (`PhysicsWorld.Park` and `CarryParked`). Only bases and capital ships can be hosts.
  
  Both need to become one nested parent chain.

## Open questions for the owner

None left from the brainstorm. The **(check)** numbers still need the Empyrion data.
