# Prototype: the slice (phase 1, the ground loop)

The first playable slice of GalHack's core loop, built from the proven prototypes and nothing else: wake in a buried cryopod bunker on a freshly generated planet, climb out, survive, gather, craft, found a base, run its machines, build a small vessel block by block and fly it. Every system is compiled in from where it lives; the slice itself is glue, a game layer and its tests.

![Waking in the bunker: the one working cryopod open, the other sleepers dead in theirs](screenshots/bunker.png)
![Climbing the bunker's stairwell, its fallen-in top open to the morning sky](screenshots/climb.png)
![The Tab menu, YOU: the bag and toolbar, the paperdoll with its equipment and vitals, the suit fabricator](screenshots/menu-you.png)
![The Tab menu, CORE: a placed survival constructor making a base core](screenshots/menu-core.png)
![The Tab menu, TECH: the base tree](screenshots/menu-tech.png)
![A creature's body opened beside the bag](screenshots/loot.png)
![A bolt in flight from the tool's muzzle, the sparks and scorches of the shots before it](screenshots/bolt.png)
![Inside the bunker: a shot scorches its wall, harmlessly](screenshots/bunkershot.png)
![Night, the head torch on](screenshots/torch.png)
![A base built by hand: core, container, generator, small constructor, steel; the survival constructor beside it](screenshots/base.png)
![A small vessel built by hand on its core, lifting off](screenshots/vessel.png)

Phase 1 is the ground loop. Orbit and boarding (phase 2) wait for the owner's go; saves across everything are phase 3.

## Play it

```powershell
.\tools\godot.ps1 -Project prototypes/slice -Play
```

A new game opens on the start screen: **Survival** or **Creative**, the planet type (**temperate** is the easy one; ice, alien swamp, barren, lava, or random) and a seed (leave it empty for a fresh world, or type one to replay it). Then you wake at dawn in a cryopod. **F** opens it. **E** on the orange locker opens it beside your bag: **TAKE ALL**. Climb the stairs to the surface. The compass shows the nearest deposit of each common ore and drops one once it is dug out.

### The loop (Survival)

1. **The bunker.** Get out of the cryopod and empty the locker (food, patch kits, air cells); the stairwell climbs to the surface. Come back down whenever you like: it is your respawn point, but too small and broken to build on.
2. **Gather.** The survival tool is on toolbar slot 1 (and in your hands). Hold the left button on ore to dig it (12 raw ore a cell), on a plant to cut it (biomass). **Q** switches it to shooting: a bolt you can see, sparks and a scorch where it lands. Shots only hurt creatures.
3. **The suit fabricator** (Tab, YOU) makes the bare minimum from raw finds: the survival tool, rations, patch kits, air cells and a **placeable survival constructor**. It works while you play.
4. **The survival constructor.** Drag it onto the toolbar, select it and click open ground. **E** on it opens its hopper beside your bag (STORE ALL); its panel is on Tab, **CORE**, when you stand by it. It refines ore into parts and makes the **base core**, the first blocks' parts and the next constructor up.
5. **Found a base.** The base core on the toolbar, a click on open ground: a base (large grid, base mode, anchored). Drag blocks from the **build palette** (Tab, YOU) onto the toolbar and build onto it: each block's bill is paid from your bag, then the base's storage; right click takes a block down and refunds the whole bill.
6. **Machines.** A container (storage), a generator (power) and a **small constructor** on the base, run from Tab, **CORE**, pulling from the base's storage. Level 2 unlocks the small constructor in the tech tree.
7. **Level 5** unlocks the small vessel (Tab, **TECH**). The small constructor makes the **small-vessel core**.
8. **Build your vessel.** Set the core down: it stands on a build stand 2 m up, so you can build under it as well as round it. A seat, a generator, a fuel tank, a gyro, thrusters (R / T turn them: a lift jet pushes up), three legs. Tab, CORE shows its four numbers and lift margin. **F** at the seat, **Space** to lift off.

The Skiff blueprint is still there as a shortcut: drag **BLUEPRINT PROJECTOR** from the build palette onto the toolbar, click open ground and hold the button. Every block is paid for as it goes.

**Creative** (`-- --creative` at launch) places everything free, unlocks the whole tech tree and runs every machine without material checks.

### Controls

| Key | On foot | In a seat |
| --- | --- | --- |
| WASD, mouse | Walk and look | Thrust; the mouse moves a stick that recentres |
| Shift | Sprint | Boost |
| Space / Ctrl | Jump, hold in the air for the jetpack | Up / down |
| 1 to 9, wheel | The toolbar slot in your hands | |
| Left button | Use what is in hand: dig, cut, shoot, place, eat | |
| Right button | Take a block down (refunded) | |
| Q | Survival tool: gather / shoot | Roll |
| R / T, X, wheel | A block in hand: turn it, mirror, its shape | |
| E | Open a container, a body, a backpack, a locker, a device's hopper | Roll |
| F | Sit in the seat within reach | Get up |
| G / C | | Landing gear / cruise |
| L | Head torch | |
| Tab | The menu (the game doesn't pause; you can still walk) | |
| H, B | Helmet, the survival replicator | |
| V | Third person | Chase camera |
| R (dead) | Wake in the bunker (your backpack stays where you fell, on the compass) | |
| F3, Esc | Debug numbers, free the mouse | |

### The Tab menu

One full-screen menu, styled as the suit's own terminal (the diegetic UI's screen glass, scanlines and 1970s-paperback colours) but laid over the view, not built into the world. The world keeps running. Every section is a self-contained panel (`IMenuSection`, `MenuPanel`) that lays itself out in any rectangle, so the same panel can go on an in-world screen.

| Tab | What |
| --- | --- |
| **YOU** | Your **bag** (8 x 6 slots) and **toolbar** (1 to 9), stacks to each item's stack size, mass and volume. Drag to move, **Shift-click** to send a stack across, **right-click** to split, drag out of the panels (or onto SET DOWN) to drop it on the ground in a container that never despawns. Equipment slots round a **paperdoll** of your own character model, your vitals, armour, resistances and level beside it. On the right, the **suit fabricator** and the **build palette** (drag blocks onto the toolbar; locked ones greyed with what they need). **E** on a container opens it here beside the bag, with **TAKE ALL** and **STORE ALL** |
| **TECH** | The tech tree: Base, Capital Vessel, Small Vessel, Hover Vessel, Tools, Weapons, Misc. Nodes in columns by level, joined to their parents; locked nodes say what they need (level, points, parent); click one you can afford |
| **STATS** | What you have done since the crash, and the master volume |
| **CORE** | The structure you are in or next to (or a device you stand by), the nearest picked, a button to switch: its manufacturing (each constructor's orders above, its own sub-parts below, missing materials, ammo restock), its storage, its settings (core mode, gravity, power, lights, Home, auto-transmit) and its numbers (mass, power, CPU, storage; a vessel's four numbers, turn cap and lift margin) |
| **MAP** | 300 or 600 m round you: structures, devices, the bunker, your backpack, bodies and drops, the nearest deposits, creatures |

The visor stays minimal: vitals, warnings, the compass with contacts, the toolbar, what is in hand and its keys, the torch, short **+XP** lines and a **LEVEL UP** banner that never blocks play.

## How it fits together

| Piece | From | What the slice does with it |
| --- | --- | --- |
| Physics | `shared/Physics` | One physics world for the zone: the survivor on the shared walk controller, riding hulls in cabin worlds and stepping off them onto the ground (`Riding.StepsOntoGround`), bases as fixed bodies walked on in the zone's world, every vessel one body flown by the handling model through the physics sandbox's bridges |
| Items | `shared/Items` | The catalogue with the slice's additions (`data/items.json`, appended as it loads), `SharedStorage` under every bag, container and structure, `Constructor` for the suit fabricator, the survival constructor and every constructor block, `BlockPlacer` prices and refunds, `WorldRules` for Survival and Creative, the unlock and stack fields |
| Terrain | `shared/Terrain`, `terrain-digging` | The planet as diggable voxels. `PlanetEdits` is the physics module's terrain editor; a base's blocks cut the ground they reach into |
| Planet | `planet-generator`, `planet-surface` | A fresh world every game (the tests pin seed 7, temperate), streamed by planet-surface's `TerrainStreamer` with its sky, sea, fog and flora; trees are solid and felled for good |
| Weather | `weather`, `planet-surface` | The weather model pushes bodies; planet-surface's effects and its wind and rain sounds |
| Wildlife | `wildlife` | The planet's fauna on a patch that follows you, solid to you (blockers on the props layer), each species worth XP by toughness and danger; a dead creature's body is a container holding its drops |
| Survival | `survival-vitals` | The suit (food, oxygen, temperature, radiation, stamina, health), warned before harm |
| Building | `building` | Its structure model, rules, blueprint format, mesher and ghosts |
| Handling | `ship-handling` | The four numbers and the flight rules |
| UI | `diegetic-ui` | Its theme, fonts, glass shader and widgets; the slice's menu, visor and cockpit console |
| Sound | `planet-surface`, `building`, `diegetic-ui`, `wildlife` | All synthesised in code where it lives: digging, ore, clanks, thunder, wind and rain; block place and remove, UI clicks and the upgrade chime; the chirp; creature calls. The slice adds three small loops of its own (the jetpack, a vessel's engines, footsteps) and a master volume |

### The slice's additions to shared/Items (`data/items.json`)

| Item | Made in | From | Why |
| --- | --- | --- | --- |
| Survival tool | suit fabricator | 6 iron ore, 2 copper ore | The tool is an item on the toolbar |
| Trail ration, patch kit, air cell | suit fabricator | biomass (and silicon ore) | The suit's bare minimum: food, a simple heal (GalHack has no ailments), air |
| Survival constructor (recipe replaced) | suit fabricator | 16 iron ore, 8 copper ore, 4 silicon ore | It is placeable here, so the suit makes it from ore |
| Base core | survival constructor (tier 0) | the large core block's bill | A placed core founds a structure (Empyrion's way); the catalogue prices blocks only |
| Small-vessel core, hover core | small constructor (tier 1) | the small core block's bill | |
| Raw meat, hide, rare tissue | creatures' bodies | | wildlife's drop table: meat by size, a hide per three meat, a rare part by chance |

The suit fabricator is a constructor at tier −1, below shared/Items' survival tier: it knows only the recipes above and works from raw finds.

### Levels and the tech tree

Experience comes from playing: 3 XP per ore cell dug, 2 per plant, a point per 2 recipe seconds of every finished craft (sub-parts too), 3 per large block and 1 per small block placed, 15 the first time you come within 30 m of a deposit, 10 the first time you meet a species, and each species' own kill value. Levels run to 25; each grants unlock points (2 to 6), and past the cap a point every 1,500 XP. All of it is in `data/progress.json`.

The tree is built from the unlock fields on every item and block (shared/Items' `items.json` and `blocks.json`, and the slice's `items.json`): level, point cost, parent node, tab. There is no tree file. The starting essentials cost nothing at level 1: the suit fabricator's minimum, the survival constructor, the base core, steel, glass, concrete, ladders, containers, generators, fuel tanks and lights on the large grid. The design's gates: **hover vessel at level 1, portable constructor at 3, small vessel at 5, capital vessel at 7** (the large thruster, which also opens a base's capital mode). A locked recipe can't be ordered in any constructor and a locked block can't be placed; both show greyed with the reason. Creative unlocks everything. Level, XP, points and unlocks live in one serialisable `ProgressState` for phase 3's saves.

Expected time to each gate for an experienced player (from the real-player test's XP and the pacing reference in shared/Items: 45 ore a minute, 1.2 s a block): see **Pacing** below.

### Rules the slice adds

- **Warned before any harm.** Impacts use the physics module's one formula in full; the visor flashes IMPACT only while the body is airborne **and coming down** faster than the safe speed, never while lifting off on the jetpack. A creature's wind-up is heard and shown (THREAT) before its lunge.
- **Nothing a player owns disappears.** A full bag sets things down in a container on the ground; a refund with nowhere to go likewise; a dead player's whole inventory is a backpack where they fell, marked on the compass and the map; bodies and drops stay until emptied.
- **Every interaction asks the world you are in.** Aboard a structure, E, the tool and the build tool ray through its cabin world (a still copy of its blocks) as well as the zone's world beyond, so a locker answers from inside, a shot inside a structure scorches its wall (harmlessly) and the tool never digs through a floor.
- **Walking out of a door hands you back to the planet at once.** A rider out of the interior with no block under its feet and the zone's ground within a step or two below leaves the cabin world there, onto the ground (`Riding.StepsOntoGround`).
- **A fresh world every game.** A new game rolls a fresh seed (`WorldChoice`); the start screen picks the planet type and can take a seed to replay one, and STATS shows the world and its seed. `-- --seed N --planet temperate|ice|swamp|barren|lava` pins both (tests and screenshots pin the tested world, seed 7 temperate, unless told otherwise). A save will keep its world's choice; the slice has no saves yet.
- **Every game starts in a cryo bunker** (`src/Start/Bunker.cs`, `Game.Start.cs`). Its site is searched near the generator's landing site on any seed: dry and above the sea, 1.75 m of earth over its roof, even ground over it, no bedrock in the way, its stairs coming up level with the ground beyond, and turned to face the most striking skyline 300 to 1,200 m off. It is a fixed small-grid structure of the slice's own blocks (a 6.5 x 3 x 4.5 m chamber, a locker, a stairwell of half-metre steps whose top end has fallen open to the sky) set into rooms dug through the terrain's edit API before any ground streams in, dug tight round its own volume so its floor, walls and stairs are always solid and no gap is left to fall into. Four cryopods stand along its back wall; yours opens with F. It is a ruin: nothing can be built onto it and it is never a core of yours.
- **A new vessel is built on a stand.** Its core holds 2 m above the ground where you set it, so its underside can be built; it holds there until first flown, then lands on its own legs. A blueprint's vessel instead stands on the ground as it grows (its lowest block on the highest ground under it), so no part of it hangs in the air.
- **Small vessels turn no faster than 110 °/s** (`build.smallVesselMaxTurn`): the stick asks for a share of the handling model's turn rate.
- **Machines have their own screens.** A placed survival constructor carries a screen on a mast at its front; every constructor block on a base, and the base's core, has one on a free face. They are diegetic-ui's world screens hung at standing eye height (`ScreenMount`), each showing the same panel as the CORE tab (`MachineScreens`). Look at one within 3 m: a cursor sits where you aim (`ScreenPointer`), and the left button clicks there instead of firing the tool. The Tab menu stays the main way in.
- **E answers only containers, bodies, backpacks, lockers and devices.** A cockpit says F; a machine block opens its CORE panel.
- **Gamey cockpits, a calm opening, streaming ahead of a vessel**: unchanged from before.

## Test it

```powershell
.\tools\test.ps1 -Project prototypes/slice -Scene res://tests/PlayerTest.tscn
.\tools\test.ps1 -Project prototypes/slice -Scene res://tests/SliceTest.tscn
```

### The real-player test (`tests/PlayerTest.tscn`)

The loop played the way a person plays it, headless with `--fixed-fps 60`, on the tested world (seed 7, temperate; `-- --seed N --planet type` for another): the castaway wakes in the cryopod, and from then on only the game's own inputs are used (look, walk, keys, the mouse on the Tab menu and on in-world screens: every click and drag lands on a widget the panel laid out). No teleports, no grants, no skipped steps. The one help is a clearly printed **TEST TIME-SCALE** on crafting (40x), so an hour of fabricating fits in a run. Creatures are held still and the weather calm, so a predator can't decide the run.

Latest run: **ALL PASSED, 76 checks** in 60 s of wall time (9.5 min of game time). The route and its numbers:

1. **The bunker.** The castaway wakes in the cryopod, its lid shut, 5.7 m under the ground. F opens it; out into the chamber; E on the locker, TAKE ALL (4 trail rations, 2 patch kits, 2 air cells, 6 biomass). A shot inside: a bolt from the muzzle and a scorch on the wall exactly at the aim, the bunker unharmed; the tool digs nothing through its floor. Up the stairwell's 12 steps to the surface on foot (6 s), 51 m away, back down the stairs into the chamber and out again, never rescued.
2. **Gathering.** 1 puts the tool in hand (seen in the first-person hands); 20 cells of iron give 240 iron ore (12 a cell); the deposits near the start hold the whole loop's ore; a dug-out deposit drops off the compass; a plant cut gives 2 biomass.
3. **Shooting.** A shot at the ground: bolt, sparks and a scorch 0.05 m from the aim. A hammerhead stalker shot down from 41 health, each hit with a bolt, a spray and the visor's marker: +21 XP.
4. **The body and the inventory by hand.** Its body is a container (2 hide, 8 raw meat); E opens it beside the bag; a drag moves the meat, shift-click the hide, take all empties it, right-click splits a stack, a patch kit is dragged onto toolbar slot 8; the empty body goes once closed. Lifting off on the jetpack never flashes IMPACT.
5. **The suit fabricator** (YOU tab) makes a survival constructor in 45 s of play while the castaway walks; dragged to toolbar slot 3 and set down on open ground.
6. **The base core.** E opens the constructor's hopper beside the bag; STORE ALL loads 745 SU of ore. Then its own in-world screen, at eye height: a cursor where you look, +1 on the base core clicked there; it plans 6 lines of sub-parts and the core comes out 53 s later.
7. **The base.** The constructor makes the base's parts; the core set down founds a base (large grid, base mode, fixed body). Level 4 by then (514 XP): the small constructor unlocked on the TECH tab, blocks dragged from the palette to the toolbar, 5 large blocks built by looking and clicking, each paid from the bag; a right click takes one down and refunds its whole bill.
8. **Level 5 and the small vessel's core.** STORE MY ORE AND MATERIALS; the base's small constructor makes the vessel's parts; **level 5 at 686 XP after 3.8 min of play** (crafting at the test time-scale); the small-vessel core, cockpit, thruster and gyro unlocked with points; the constructor makes the core.
9. **The vessel.** The core set down starts a small vessel on its build stand; 17 of 17 blocks built from the toolbar (a seat on top from a jetpack hop, lift jets and a retro turned with R/T, a down jet, three legs), lift margin +67 kN, never in the ground. F at the seat; the CORE tab shows its four numbers; Space lifts it 108 m in 2 s; it flies 574 m at up to 70 m/s and lands on its gear, undamaged.
10. **The rest.** Every tab opens by its own button; L toggles the head torch (45 m, 32°). A running jump off the hovering vessel from 115 m kills the castaway: everything carried is in a backpack where they fell; R wakes them in the bunker; they walk to the marker, E, take all, everything back. The castaway is never rescued from the ground all run.

Game time at each step (min): out 0.6, gathered 2.3, hunted 2.4, looted 2.5, base core 3.2, base 4.1, level 5 4.5, vessel core 4.6, vessel built 5.8, flown 7.6, recovered 9.5. Level 5 came at 693 XP after 4.5 minutes of play; the vessel flew 544 m and landed undamaged; a 113 m fall killed the castaway, R woke them in the bunker and they climbed out to their backpack.

### The systems test (`tests/SliceTest.tscn`)

Fresh seeds, determinism, the bunker, gathering, cutting a tree (its collider gone the frame it falls), the Skiff blueprint built block by block and paid for, the same in Creative for nothing, a 2 km flight and landing, a hop flown twice to the tick, creatures warned before they bite, and nothing stuck in the ground. It starts with the cryopod already open (the real-player test wakes in it shut) and, as a systems test, hands over the Skiff's parts and opens the tech tree: printed when it does.

Latest run: **ALL PASSED, 43 checks** in 35 s. Two fresh games roll different seeds, and `--seed`/`--planet` pin both; the planet, its fauna and the starter blueprint generate identically twice; the weather opens calm for 12 minutes. The cryo bunker is dug in at the start site, 5.8 m under the ground and above the sea, 19 m from the generator's landing site, 12 steps up; the castaway wakes in their cryopod with 3 dead sleepers beside it; the start site is survivable with the planet's own wildlife around it. The locker emptied by TAKE ALL and up the stairwell on foot, never rescued. Ore dug as real terrain (12 a cell, every common ore from the nearest shallow deposits); a tree cut (its collider gone the frame it falls). The Skiff blueprint built block by block (174 of 174, each paid for, never pushed out of the ground), the same in Creative for nothing; flown over 2 km and landed on its gear undamaged; a 4 s hop flown twice identically to the tick; a creature's attack warned (THREAT) before it bites; nothing stuck in the ground.

### The start test (`tests/StartTest.tscn`)

The owner's first concern: that you can always get out. On five fresh worlds (each game rolls its own seed) and one of every other planet type, the castaway wakes in the cryopod, opens it, takes the locker's kit, climbs the stairwell to the surface, walks 50 m away over dry ground, comes back down into the chamber and climbs out again, with the game's inputs only. A watch on every tick proves no teleports (the castaway never moves more than half a metre in a tick) and no rescues. `-- --seeds N` runs more worlds; `-- --seed N --planet type` one.

Latest run: **ALL PASSED, 92 checks** in 43 s. Seeds 650102798, 85240623, 554930613, 472739100 and 124332800 (temperate), 500687335 (ice), 696836522 (alien swamp), 191788404 (barren) and 947958248 (lava): start sites 0 to 224 m from the landing sites, 8 to 22 steps up, each facing a skyline 436 to 1,213 m off; out, 50 m away and back in 34 to 58 s of play each; two fresh games rolled different seeds.

### Screenshots

Rendered windowed and off-screen (`--position 20000,20000`), staged through the game's own controls (`tests/Staging.cs`, `tests/Staging.Loop.cs`; staging hands over materials and uses Creative where it only saves time, and says so):

```powershell
. .\tools\env.ps1
dotnet build prototypes/slice/Slice.csproj
& $GodotEditor --position 20000,20000 --path prototypes/slice res://tests/Screenshot.tscn -- --view=menu --tab=you --out="$PWD/menu-you.png"
```

Views: `bunker`, `climb`, `bunkershot`, `menu` (with `--tab=you|tech|stats|core|map`), `loot`, `bolt`, `torch`, `base`, `vessel`, `dig`, `build`, `liftoff`, `flying`, `cockpit`. Add `--seed=N --planet=type` for another world.

## Pacing

The design target is a small vessel within one to two hours for an experienced player. shared/Items' pacing reference (45 ore a minute with the survival tool, 12 ore a cell, 1.2 s to place a block) and the real-player test's own route give:

| Step | What it takes | An experienced player |
| --- | --- | --- |
| Out of the cryopod, locker emptied, up the stairs | | 1 min |
| Ore for the whole loop (about 800 raw: iron, copper, carbon, silicon, a little ice and stone) | 45 ore/min | 18 min of digging, spread over the run |
| Survival constructor (suit fabricator, 20 s) | 28 ore | at about 4 min |
| Base core and the foothold's parts (survival constructor, half speed) | about 280 runs | 12 to 15 min, while gathering |
| Base placed and built (core, container, generator, small constructor, two steel) | 6 large blocks | 2 min |
| The vessel's parts (small constructor) and core | about 120 runs | 6 to 8 min |
| The vessel built (13 blocks) and flown | | 4 min |

So **about 45 to 55 minutes** from the crash to flying, inside the target with room for food, air, weather and creatures.

**Levels on that route** (`data/progress.json`): experience comes about 11 a minute from digging, 10 to 20 a minute from whatever a constructor is making, plus finding deposits and species, building and kills. Expected time to each gate:

| Gate | Level | Experience | Expected |
| --- | --- | --- | --- |
| Hover vessel | 1 | 0 | from the start |
| Small constructor (the base's) | 2 | 80 | about 5 min |
| Portable constructor | 3 | 220 | about 12 min |
| **Small vessel** | **5** | **680** | **about 35 to 40 min**, as the base's constructor finishes the vessel's parts |
| Capital vessel | 7 | 1,340 | about 75 to 90 min, by playing on |

The real-player test follows the same route at its own speed (a bot digs and walks faster than a person, and its crafting runs at the test time-scale): it reached level 5 with 686 XP after 3.8 minutes of play, so the experience is there whenever the base can make the vessel's parts; the constructors' real time is what paces a person.

## Changes to shared code and other prototypes

All additive; each prototype's own tests still pass.

| Where | Change | Why |
| --- | --- | --- |
| `shared/Physics/Core/Riding.cs` | `StepsOntoGround(rider, standingOnHull, zone, reach, out ground)` | Walking out of a landed vessel's door hands the rider to the zone's world at once, onto the ground |
| `shared/Items/Core` | `ItemDef.StackSize` (data `stack`, a default per category), `ItemDef.Unlock` and `BlockBill.UnlockSmall/UnlockLarge` (data `unlock`), the `Unlock` record, `BlockBill.NodeId` | Empyrion-shaped inventories and a tech tree carried by the items and blocks themselves |
| `shared/Items/data` | `unlock` on every block (per grid) and on tools, consumables and ammo | The tree's nodes; materials and parts have none (always made) |
| `wildlife/src/Core` | `Stats.Xp`, set by the fauna generator from health, damage and hunting | Kill experience by species |
| `planet-surface/src/WeatherView` | `IWeatherScene` (earlier) | |
| `planet-surface/src/Terrain/TerrainStreamer.cs` | `FloraNear(point, radius)` with each plant's scatter id (earlier, now merged with main's solid flora) | Find and cut plants from data |
| `wildlife/src/Core/Terrain.cs`, `ProceduralAudio.cs` | (earlier) | Creatures on a patch of the real planet |
| `survival-vitals/src/Model/SurvivalSim.cs` | (earlier) | The generated planet's climate |

## References: Empyrion (ideas only; nothing copied)

The owner asked "how did Empyrion do it?" for the inventory, the tech tree and the crafting chain; the design doc wins where they differ. Read, never copied:

- [Tech Tree](https://empyrion.fandom.com/wiki/Tech_Tree) and [Leveling and XP](https://empyrion.fandom.com/wiki/Leveling_and_XP) (Official Empyrion wiki): XP from mining, building, kills and exploring; a level cap of 25; points per level spent on nodes that each have a level, a cost and a parent. GalHack's nodes, names, numbers and gates are its own (the design's gates win).
- [Experience Points, Player Level Progression and Tech Trees](https://empyriononline.com/threads/experience-points-player-level-progression-and-tech-trees.3082/) (Empyrion forums): how players paced the early levels.
- [Guide/Survival Best Practice](https://empyrion.fandom.com/wiki/Guide/Survival_Best_Practice) (Official wiki): out of the escape capsule, open its inventory, collect all; tools, constructors and food on toolbar slots 1 to 9.
- [Console commands gameoptions](https://empyrion.fandom.com/wiki/Console_commands_gameoptions) (Official wiki): a dead player's backpack drop.
- The coordinator relayed Empyrion's local configuration (read-only) for the suit constructor's bare minimum, the placed survival constructor making the first core, per-item stack size, mass and volume, and per-item unlock fields. Shape only; no names, recipes or numbers taken.

## Known limits

- **Phase 1 only:** no orbit, boarding or saves yet. Progress lives in one `ProgressState` ready for phase 3's saves.
- **The escape pod and its descent are gone**; the bunker start lives in `Game.Start.cs` and `src/Start/Bunker.cs`, apart from the rest. **No POI on the horizon yet**: the slice doesn't draw POIs (planet-surface's POI layer isn't compiled in), so the stairs face the most striking skyline instead; once POIs are drawn, the site search can prefer one in view 300 to 1,200 m off.
- **Lava and ice worlds** start like any other (the test climbs out and walks 50 m on each), but the slice hasn't tuned survival on them beyond that.
- **Blocks are not items** (shared/Items' rule): the toolbar holds block shortcuts paid from storage at placement, not stacks of blocks as in Empyrion. Cores are the exception, made as items in the slice's own data.
- **Equipment is read-only**: the paperdoll's armour and booster slots show the survival suit's; there are no armour items to drag in yet.
- **Power is a switch**: a generator on a structure powers its machines; fuel isn't burned yet, and the lights toggle doesn't switch light blocks yet.
- **Capital mode** can be set on a base once the capital vessel tech is unlocked, but the base stays anchored: lift-off belongs to capital-base and isn't wired here.
- **A new vessel holds on its build stand** until first flown: it doesn't drop onto its legs by itself.
- **Hover vessels** have their tab, core and pads, but the slice's handling of hover pads isn't tuned or tested.
- **Deposits under deep rock** don't show on the compass: the survival tool can't cut deep rock.
- **Creatures** are solid to the castaway through blockers on the props layer; they don't collide with vessels or each other's blockers, and they read the natural ground, not dug holes (wildlife's limit).
- **Plants are cut whole** and don't regrow; **the open Skiff** has no cabin.
