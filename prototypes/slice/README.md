# Prototype: the slice (phase 1, the ground loop)

The first playable slice of GalHack's core loop, built from the proven prototypes and nothing else: wake in a buried cryopod bunker on a freshly generated planet, climb out, survive, gather, craft, found a base, run its machines, build a small vessel block by block and fly it. Every system is compiled in from where it lives; the slice itself is glue, a game layer and its tests.

![Waking in the bunker: the one working cryopod open, the other sleepers dead in theirs](screenshots/bunker.png)
![Climbing the bunker's stairwell, its fallen-in top open to the morning sky](screenshots/climb.png)
![The Tab menu, YOU: the bag and toolbar, the paperdoll with its equipment and vitals, the suit fabricator](screenshots/menu-you.png)
![The Tab menu, CORE: a placed survival constructor making a base core](screenshots/menu-core.png)
![A constructor crafting blocks: the list on BLK, two orders with their sub-parts under them, a tooltip](screenshots/constructor.png)
![A big base storage on the CORE tab, filtered by text, a stack's tooltip up](screenshots/storage.png)
![The Tab menu, TECH: the base tree](screenshots/menu-tech.png)
![A creature's body opened beside the bag](screenshots/loot.png)
![A bolt in flight from the tool's muzzle, the sparks and scorches of the shots before it](screenshots/bolt.png)
![Inside the bunker: a shot scorches its wall, harmlessly](screenshots/bunkershot.png)
![Night, the head torch on](screenshots/torch.png)
![A base under construction from carried block stacks, the toolbar counting what is left](screenshots/blockbase.png)
![A block family in hand: its name, shape and count from each source, the shape picker above the toolbar](screenshots/hand.png)
![A small vessel built by hand on its core, lifting off](screenshots/vessel.png)
![Walking up to the place in view](screenshots/poi-place.png)
![A canteen dressed from the decorations library, the Bloom in the corner](screenshots/poi-interior.png)
![The loot view on a vault cache](screenshots/poi-cache.png)
![A creature's tell in a place](screenshots/poi-tell.png)
![A hamlet's people about their day](screenshots/poi-town.png)
![Salvaging a derelict's hull](screenshots/poi-salvage.png)
![The chamber's numbered lockers](screenshots/bunker-lockers.png)

Phase 1 is the ground loop; phase 2 (below) is orbit: flying up out of the atmosphere, the start system's wrecks to board, salvage and claim, and coming back down where you choose. Saves across everything are phase 3.

![Climbing out](screenshots/orbit-climb.png)
![The world as a globe from orbit](screenshots/orbit-globe.png)
![A wreck field near the world](screenshots/orbit-wrecks.png)
![Floating across to a derelict's hull door](screenshots/orbit-eva.png)
![Inside a dead hull, by the head torch](screenshots/orbit-derelict.png)
![Landing back on the surface](screenshots/orbit-landing.png)

## Play it

```powershell
.\tools\godot.ps1 -Project prototypes/slice -Play
```

A new game opens on the start screen: **Survival** or **Creative**, the planet type (**temperate** is the easy one; ice, alien swamp, barren, lava, or random) and a seed (leave it empty for a fresh world, or type one to replay it). Then you wake at dawn in a cryopod, empty-handed. **Tab**: the suit fabricator makes the survival tool, free, in a few seconds; drag it onto the toolbar. Nothing works until you do: no gathering, shooting or building. **F** opens the pod. **E** on the orange locker opens it beside your bag: **TAKE ALL**. Climb the stairs to the surface. The compass shows the nearest deposit of each common ore and drops one once it is dug out.

### The loop (Survival)

1. **The bunker.** Make the tool (Tab, YOU), get out of the cryopod and empty your locker (food, patch kits, air cells); the stairwell climbs to the surface. Come back down whenever you like: it is your respawn point, but too small and broken to build on.
2. **Gather.** The survival tool is on the toolbar (and in your hands). Hold the left button on ore to dig it (12 raw ore a cell), on a plant to cut it (biomass). **Q** switches it to shooting: a bolt you can see, sparks and a scorch where it lands. Shots only hurt creatures.
3. **The suit fabricator** (Tab, YOU) makes the bare minimum from raw finds, out of your own bag: the survival tool, rations, patch kits, air cells and a **placeable survival constructor**. It makes no blocks. It works while you play.
4. **The survival constructor.** Drag it onto the toolbar, select it and click open ground. **E** on it opens its hopper beside your bag (STORE ALL); its panel is on Tab, **CORE**, when you stand by it. Blocks are items: it refines ore into parts, and crafts the **base core**, steel, containers, generators, fuel tanks, lights and the small constructor. Order what you want and the parts plan themselves. **TAKE ALL** moves them to your bag.
5. **Found a base.** The base core on the toolbar, a click on open ground: a base (large grid, base mode, anchored). Every structure starts with its core: no other block stands alone. Drag block stacks onto the toolbar and build onto it, each placement using one from the stack in your hand. **Q** to DECONSTRUCT on the survival tool (or right click with a block in hand) takes a block down and gives its item back.
6. **Machines.** A container (the base's one shared storage), a generator (power) and a **small constructor** on the base, run from Tab, **CORE**, in and out of that storage. Inside the base's area, its **structure toolbar** (**Y**, or a number tapped twice) builds straight from that storage. Level 2 unlocks the small constructor in the tech tree.
7. **Level 5** unlocks the small vessel (Tab, **TECH**). The small constructor makes the **small-vessel core** and the vessel's blocks.
8. **Build your vessel.** Set the core down: it stands on a build stand 2 m up, so you can build under it as well as round it. A vessel needs thrusters, a **command chair** and enough thrust to lift its mass; add a generator, a fuel tank, a gyro and three legs. Thrusters turn with R / T (a lift jet pushes up); nothing else cares which way it faces. Tab, CORE shows its four numbers and lift margin. **F** at the chair, **Space** to lift off. A chair gives no air: that takes a sealed room, or your suit.

Building by hand, block by block, is the game. Blueprints are in the menu, nowhere else: on a structure's CORE tab, **BLUEPRINTS** projects the Skiff, each block one item from your bag.

**Creative** (`-- --creative` at launch) places everything free and unlimited from the build palette (Tab, YOU), unlocks the whole tech tree and runs every machine without material checks. Structures still start with a core, and blocks still go only on their classes.

### Controls

| Key | On foot | In a command chair |
| --- | --- | --- |
| WASD, mouse | Walk and look; W climbs a ladder, S goes down it | Thrust; the mouse moves a stick that recentres |
| Shift | Sprint | Boost |
| Space / Ctrl | Jump, hold in the air for the jetpack; ride a lift shaft up and down | Up / down |
| 1 to 9, wheel | The toolbar slot in your hands (a number again: switch toolbar) | |
| Y | Your toolbar / the linked structure's | |
| Left button | Use what is in hand: dig, cut, salvage, shoot, place, eat, deconstruct | |
| Right button | A block in hand: take the block aimed at down | |
| Q | Survival tool: gather / shoot / deconstruct | Roll |
| R / T, X, wheel | A block in hand: turn it, mirror, its shape (the visor shows the shape picker) | |
| E | Open a container, a body, a backpack, a locker, a place's cache; a constructor, standalone or on a structure, opens its full panel (hopper, recipes, queue); open or shut a door; take a keycard | Roll |
| F | Sit in the command chair within reach | Get up |
| G / C | | Landing gear / cruise |
| L | Head torch | |
| Tab | The menu (the game doesn't pause; you can still walk) | |
| M | The map (Tab, MAP); M again closes it | |
| H, B | Helmet, the survival replicator | |
| V | Third person | Chase camera |
| R (dead) | Wake in the bunker (your backpack stays where you fell, on the compass) | |
| F3, Esc | Debug numbers, free the mouse | |

### The Tab menu

One full-screen menu, styled as the suit's own terminal (the diegetic UI's screen glass, scanlines and 1970s-paperback colours) but laid over the view, not built into the world. The world keeps running. Every section is a self-contained panel (`IMenuSection`, `MenuPanel`) that lays itself out in any rectangle, so the same panel can go on an in-world screen.

| Tab | What |
| --- | --- |
| **YOU** | Your **bag** (8 x 6 slots) and **toolbar** (1 to 9), stacks to each item's stack size, mass and volume, and the linked structure's **toolbar** under it. Drag to move, **Shift-click** to send a stack across, **right-click** to split, drag out of the panels (or onto SET DOWN) to drop it on the ground in a container that never despawns. Drag a stack from a structure's storage onto its toolbar to set a slot: nothing moves. Equipment slots round a **paperdoll** of your own character model, your vitals, armour, resistances and level beside it. On the right, the **suit fabricator** and **BLOCKS**: what each is made of, and how many you and the linked structure hold (in Creative, the **build palette**: drag one to the toolbar). **E** on a container opens it here beside the bag, with **TAKE ALL** and **STORE ALL** |
| **TECH** | The tech tree: Base, Capital Vessel, Small Vessel, Hover Vessel, Tools, Weapons, Deco (a family per theme set), Misc. Nodes in columns by level, joined to their parents; locked nodes say what they need (level, points, parent); click one you can afford |
| **STATS** | What you have done since the crash, and the master volume |
| **CORE** | The structures whose **area** you are in (or a device you stand by), the nearest picked, a button to switch; outside every area it says OUT OF RANGE. Its manufacturing (each constructor's orders with their sub-parts under them, exactly what is missing, ammo restock), its storage, its settings (core mode, gravity, power, lights, Home, auto-transmit) and its numbers (mass, power, CPU, storage; a vessel's four numbers, turn cap and lift margin) |
| **MAP** | 300 or 600 m round you: structures, devices, the bunker, your backpack, bodies and drops, the nearest deposits, creatures |

Every grid and recipe list is the same component. A **text box** filters live, on names and category words, case-insensitive (click it or press **/**; typing doesn't move you; **Escape** clears it, then lets go, then closes the menu; in-world screens have no keyboard and show it read-only). **Toggles** pick categories (ores, materials, parts, blocks, devices, ammo, food and medical, deco, other). **SORT** steps through as arranged, name, category, quantity, mass, rarity and recent, and each view keeps its own. Filtering and sorting change nothing stored. Take-all and store-all act on what is shown while a filter is on, and say so. Every item has a **tooltip**: rarity colour, mass and volume each and for the stack, its recipe and constructor or where it is found, its stack, a block's grid and classes, whether it is locked and at what level.

The visor stays minimal: vitals, warnings, the compass with contacts, what is in hand, the torch, short **+XP** lines and a **LEVEL UP** banner that never blocks play. Along the bottom, centred and never overlapping at any size or UI scale (`HudLayout`, tested): the vitals, the toolbar, the selected item's line (name, shape, count, source), one quiet line of keys, and the shape picker with a block family in hand.

## Phase 2: orbit

Build a small vessel and fly up. There is no loading screen, fade or effect: only the atmosphere fading away as you climb. The sky darkens to space, the haze thins, the stars come out and the wind dies, and the world below becomes a globe. Space is Newtonian-ish: flight assist on, no auto-levelling, 130 m/s top speed for a small vessel. In an open hull the suit breathes its own oxygen (a chair gives no air; only a sealed room of a structure with a running core does).

**The start system** comes from the POI generator's data (`OrbitPlacement`, `orbit.txt`): its `near` and `orbit` sites, brought in close. No live faction anywhere. Each kind is real blocks (`src/Orbit/Derelicts.cs`), so it can be boarded and taken apart: a derelict ship (bridge, crew deck, hold, a hull door, a breach), a drifting hulk broken in two, a broken station on truss tubes with a module torn off, a wreck field, an escape pod cluster. None has a core or power, so no field holds air in their doors and nothing holds you to a floor.

**Boarding.** Stop near one, F to get up, Space to push off, and fly on the suit's thrusters (the look steers, W pushes, S brakes). Mag boots are off: you are held by a hull's gravity only if it has a running core. E forces a dead door open by hand, or go in through a breach. Inside it is dark: L for the head torch, afloat you right yourself with the hull's decks. E on a cargo box opens the wreck's hold beside your bag. The survival tool's gather mode, held on a block, cuts it free for about a third of its bill (the design's T1 return). A base core (or a small-vessel core on a small-grid wreck) set into a wreck claims it: it is yours, a station holding still, and its core's gravity holds you to its floor.

**Back down.** Fly over a point on the globe and descend straight down: you come in over that point with your heading (north stays north). The design's rule: you come down where you crossed into the atmosphere, so fly over a place in orbit first.

### How the seam works

Orbit starts at **1,060 m** on Empyrion's scale (`data/orbit.json`, per kind of air, and clear of the world's highest ground), which is also the physics' one altitude switch: weight, air and the handling model's air rules end there. The clouds sit under it, 600 to 1,000 m.

| What | How |
| --- | --- |
| Zones | Each its own physics world. `OrbitZone` derives from `SurfaceZone`, so the whole game layer (castaway, vessels, menu, visor) plays in both; `Game.Zone` is the one you're in. A zone you aren't in stays in the scene **parked**: `ProcessMode.Disabled` (which also takes its colliders out of the physics) and hidden, as it was left. So nothing reloads and nothing is lost: wrecks stay salvaged, bases stay put |
| Ahead of time | Past half way to the crossing the orbit's data (globe paint, wreck layouts) is made on worker threads, then it enters the scene parked and is set up a piece a frame. On the way down the surface's streamer runs ahead of you, and the crossing waits until the ground under you is there |
| The frame | The surface can't be moved or turned: its terrain and water are drawn from world coordinates. So the orbit is laid out to fit it (`OrbitZone.Align`): its bodies use the surface's own coordinates, with the globe set under the crossing point, tangent to sea level there, its local east, up and south along the world's axes. **A crossing changes no number at all**: position, rotation and velocity carry across to the last bit. Aligning elsewhere (on the way into the band, coming down) moves everything rigidly, the sky too, so nothing shows |
| One mapping | `GlobeMap` (a direction from the globe's centre is a map point) and the globe's ground being sea level. Godot's own SphereMesh is checked against it (the globe test) |
| The air | From 700 m up to 1,000 m over the crossing, by height alone, both ways. planet-surface's sky fades into space's own backdrop (the same stars, nebula and glare, its code copied into `sky.gdshader`), so when the orbit's sky takes over at the top nothing changes. Haze thins to nothing and the wind's sound with it |
| The overlap band | The flat map and the small round globe can't match, so over the 1,000 m above the crossing one becomes the other: the surface stays on screen, frozen, while the globe, showing the same map at the same scale under you, fades in over it (drawn pulled in along each eye ray, so it is in front of the terrain and still behind your vessel) and the land fades out behind it. The star's light takes over from the planet's sun |
| The record | `ZoneLedger`, on saves' persistence core: every holder's record brought up to date and the crosser's new zone committed on a worker thread (one atomic rename); only once that landed does anything move, in one tick (`Handover`: shared physics' `Release` and `Adopt`, so bodies keep their identity and riders stay aboard). The move is audited: counts before against counts after, and zones against the ledger |
| Edges | A vessel nobody is in waits at a zone's edge rather than crossing alone |

**The pilot role** (`src/Vessels/VesselRoles.cs`): every seat is a control seat; one player flies. A vessel's heading is set by the seat of whoever holds the role: `FlightState.Seat` is the control frame, and thrust, strafe, pitch, yaw, roll, cruise and the hover frame all work in it, with thrusters still pushing along their own facing. Alone, you hold it when you sit. It can go to anyone in a seat (`Roles.Assign`, or PILOT on Tab, CORE), which turns what counts as forward at once and nothing else.

**The map** (Tab, MAP) has four levels: HERE (the old 300 and 600 m view), WORLD (the generator's map, wrapping both ways, drag to move, wheel to zoom, right click to mark), SYSTEM (the star's bodies, wrecks found, a destination with a route line; the warp drive isn't fitted) and STARS (home and its neighbours one hyperdrive jump away; not fitted). Each shows only what has been discovered (`Charts`): places within 500 m, deposits the compass finds, wrecks within 3 km.

**Clouds** sit at 600 to 1,000 m and are flown through: a white-out from one sample of the sky's cloud at the eye (it applies on a peak in cloud too), a fog gathering up from the cloud base softens peaks standing into the layer, and from above a cloud deck draws what is under you from the same coverage. The globe's cloud layer is painted from that coverage too.

![The atmosphere fading: just before the crossing](screenshots/seam-before.png)
![The first frame after it](screenshots/seam-after.png)
![At 1,600 m: the air nearly gone, the same world below](screenshots/seam-alt1600.png)
![Clouds from the ground](screenshots/clouds-ground.png)
![Through the layer](screenshots/clouds-alt800.png)
![Just over the clouds, before the crossing](screenshots/clouds-alt1000.png)
![The world map](screenshots/map-world.png)
![The system map](screenshots/map-system.png)
![The star map](screenshots/map-stars.png)

### Test it

```powershell
.\tools\test.ps1 -Project prototypes/slice -Scene res://tests/GlobeTest.tscn
.\tools\test.ps1 -Project prototypes/slice -Scene res://tests/ZoneTest.tscn
.\tools\test.ps1 -Project prototypes/slice -Scene res://tests/SeatTest.tscn
.\tools\test.ps1 -Project prototypes/slice -Scene res://tests/ChartTest.tscn
```

`OrbitTest.tscn` needs `--fixed-fps 60` (test-all reads its .args).

- **OrbitTest**, the route played by the game's own controls (setup is the one shortcut, printed: the Skiff's parts and a base core handed over, the tech tree opened): build the Skiff, climb through the crossing, fly to a derelict, step out, float across, force its door, in (dark, no gravity), take its hold, salvage a block, out, claim it from outside, float back, fly over a chosen map point, turn to a chosen heading, descend, land. Every tick the castaway and the Skiff must be in exactly one zone, the one the game says. Then three hand-overs up and down for the frame cost.
- **ZoneTest**: a crossing crashed at each of its commit's points and reloaded: the crosser is in exactly one zone on disk, the old one before the rename and the new one from it on, totals intact. The move audit catches a loss, a double and a wrong zone.
- **GlobeTest**: the mapping's round trips, eight descents (both poles and the seam among them), and Godot's SphereMesh agreeing with it.
- **SeatTest**: a two-chair hull flown by the handling model, the pilot role handed over with no jolt, hover frames level. **ChartTest**: marks appear when discovered and not before, a world-map point round-trips the wrap, the system and star listings.

## How it fits together

| Piece | From | What the slice does with it |
| --- | --- | --- |
| Physics | `shared/Physics` | One physics world for the zone: the survivor on the shared walk controller, riding hulls in cabin worlds and stepping off them onto the ground (`Riding.StepsOntoGround`), bases as fixed bodies walked on in the zone's world, every vessel one body flown by the handling model through the physics sandbox's bridges |
| Items | `shared/Items` | The catalogue with the slice's additions (`data/items.json`, appended as it loads), `SharedStorage` under every bag, container and structure, `Constructor` for the suit fabricator, the survival constructor and every constructor block, block family items and their classes, `WorldRules` for Survival and Creative, the unlock and stack fields |
| Terrain | `shared/Terrain`, `terrain-digging` | The planet as diggable voxels. `PlanetEdits` is the physics module's terrain editor; a base's blocks cut the ground they reach into |
| Planet | `planet-generator`, `planet-surface` | A fresh world every game (tests too), streamed by planet-surface's `TerrainStreamer` with its sky, sea, fog and flora; trees are solid and felled for good |
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
| Survival tool | suit fabricator | nothing: a free recipe, 3 s, listed first | You wake without it and make it first; a lost tool can always be replaced |
| Trail ration, patch kit, air cell | suit fabricator | biomass (and silicon ore) | The suit's bare minimum: food, a simple heal (GalHack has no ailments), air |
| Survival constructor (recipe replaced) | suit fabricator | 16 iron ore, 8 copper ore, 4 silicon ore | It is placeable here, so the suit makes it from ore |
| Hover core, capital core | small constructor (tier 1) | plates, boards or control units, windings | The base core and small-vessel core are the core block's own items in shared/Items; these two found the other classes |
| Raw meat, hide, rare tissue | creatures' bodies | | wildlife's drop table: meat by size, a hide per three meat, a rare part by chance |

The suit fabricator is a constructor at tier −1, below shared/Items' survival tier: it knows only the recipes above and works from raw finds.

### Levels and the tech tree

Experience comes from playing: 3 XP per ore cell dug, 2 per plant, a point per 2 recipe seconds of every finished craft (sub-parts too), 3 per large block and 1 per small block placed, 15 the first time you come within 30 m of a deposit, 10 the first time you meet a species, and each species' own kill value. Levels run to 25; each grants unlock points (2 to 6), and past the cap a point every 1,500 XP. All of it is in `data/progress.json`.

The tree is built from the unlock fields on every item, block family and deco piece (shared/Items' `items.json`, `blocks.json` and `deco.json`, and the slice's `items.json`): level, point cost, parent node, tab. There is no tree file. The starting essentials cost nothing at level 1: the suit fabricator's minimum, the survival constructor, the base core, the small command chair, steel, glass, concrete, ladders, containers, generators, fuel tanks and lights on the large grid. The design's gates: **hover vessel at level 1, portable constructor at 3, small vessel at 5, capital vessel at 7** (the large thruster, which also opens a base's capital mode). A locked recipe can't be ordered in any constructor, and shows greyed with the reason; a locked part an order needs is listed with what it needs. Unlocks gate crafting, as in Empyrion: a block you carry places whether its node is open or not. Creative unlocks everything. Level, XP, points and unlocks live in one serialisable `ProgressState` for phase 3's saves.

Expected time to each gate for an experienced player (from the real-player test's XP and the pacing reference in shared/Items: 45 ore a minute, 1.2 s a block): see **Pacing** below.

### Rules the slice adds

- **Warned before any harm.** Impacts use the physics module's one formula in full; the visor flashes IMPACT only while the body is airborne **and coming down** faster than the safe speed, never while lifting off on the jetpack. A creature's wind-up is heard and shown (THREAT) before its lunge.
- **Nothing a player owns disappears.** A full bag sets things down in a container on the ground; a block taken down with nowhere to go likewise; a dead player's whole inventory is a backpack where they fell, marked on the compass and the map; bodies and drops stay until emptied.
- **Every interaction asks the world you are in.** Aboard a structure, E, the tool and the build tool ray through its cabin world (a still copy of its blocks) as well as the zone's world beyond, so a locker answers from inside, a shot inside a structure scorches its wall (harmlessly) and the tool never digs through a floor.
- **Walking out of a door hands you back to the planet at once.** A rider out of the interior with no block under its feet and the zone's ground within a step or two below leaves the cabin world there, onto the ground (`Riding.StepsOntoGround`).
- **The sky check.** `--view=skysweep` (Screenshot.tscn, windowed) looks round the horizon, 8 headings by 3 pitches and once straight away from the sun, from the bunker's way out and a hilltop at dawn under broken cloud. It counts dark specks in open sky (a pixel far darker than its neighbours, in smooth sky two pixels out, not perched on a far crest): there must be none. It also flags possible holes in the ground for a look (`--all=1` saves every frame, `--places=exit` sweeps only the way out, `--hidepois=1` hides the places). It found the dotted line down the sky away from the sun: 79 specks in 25 frames before planet-surface's sky shader was fixed, 0 after, and 0 in 50 frames on main 32ed8de with the places' signed glyph strokes.
- **A fresh world every game.** A new game rolls a fresh seed (`WorldChoice`); the start screen picks the planet type and can take a seed to replay one, and STATS shows the world and its seed. `-- --seed N --planet temperate|ice|swamp|barren|lava` pins both (tests roll a fresh world each run and print it). A save will keep its world's choice; the slice has no saves yet.
- **Every game starts in a cryo bunker** (`src/Start/Bunker.cs`, `Game.Start.cs`), sealed since the fall a century or two ago. Its site is searched near the generator's landing site on any seed, in the nearest temperate country with plants and trees about (`Bunker.TemperateNear`): dry and above the sea, 1.75 m of earth over its roof, even ground over it, no bedrock in the way, its stairs coming up level with the ground beyond, and **a place in clear view from its way out**, 300 to 1,200 m off: a ruin, a wreck or an abandoned works (no faction holds anything on a start world). The stairs face it and the compass marks it until you get there. The bunker is a fixed small-grid structure of the slice's ordinary blocks (a 6.5 x 3 x 4.5 m chamber, a locker, a stairwell of half-metre steps whose top end has fallen open to the sky) set into rooms dug through the terrain's edit API before any ground streams in, dug tight round its own volume so its floor, walls and stairs are always solid and no gap is left to fall into. Its stairs are walked, never climbed: every half-metre riser is a short ramp, a ramp off the top step sinks into the ground outside, and the top tread stands on a whole metre so the ground dug level outside meets it flush (the start test asserts the way out and back needs no jump and no jetpack). Four cryopods stand along its back wall, three failed with their sleepers dead inside, dust on the floor and motes in the lamplight; yours opens with F. Being ordinary blocks, it is yours to use: build onto it with small blocks, or take it apart (each block comes back as its item).
- **Places on the planet.** The slice generates and draws the POI generator's places over the whole planet (planet-surface's siting and POI layer) as a start world (`PoiSiting.StartWorld`): no faction holds ground, so faction places stand as ruins; wrecks, abandoned and infested places, settlements and camps stay. See **Places** below.
- **Walkable start sites.** Besides a place in view, a start site must be walkable: from the way out a person can walk 60 m in at least three of eight directions over ground that is really there (no cave mouth), no slope or drop steeper than 31 degrees over 2 m (38 on a last pass for rough worlds), no sea or lava and no water deeper than wading, not back over the bunker. A walkable way toward the place in view is preferred. If nothing near the landing site qualifies, the search goes round each place on the planet in turn.
- **Blocks meet the ground flush.** A base's blocks reaching into the ground clear their cells and register their volumes with the terrain (terrain-digging's `ClearForStructure`), so the ground is drawn and collides flush against their faces; a block taken down lets go of its volume.
- **A clean crosshair.** Text by the crosshair is only a prompt for something usable in reach (E open locker, F take the command chair, F open the pod, E open the survival constructor), the ore the tool is on, or the build tool's verdict; where you are is on the CORE tab, never under the crosshair.
- **A new vessel is built on a stand.** Its core holds 2 m above the ground where you set it, so its underside can be built; it holds there until first flown, then lands on its own legs. A blueprint's vessel instead stands on the ground as it grows (its lowest block on the highest ground under it), so no part of it hangs in the air.
- **Small vessels turn no faster than 110 °/s** (`build.smallVesselMaxTurn`): the stick asks for a share of the handling model's turn rate.
- **Machines have their own screens.** A placed survival constructor carries a screen on a mast at its front; every constructor block on a base, and the base's core, has one on a free face. They are diegetic-ui's world screens hung at standing eye height (`ScreenMount`), each showing the same panel as the CORE tab (`MachineScreens`). Look at one within 3 m: a cursor sits where you aim (`ScreenPointer`), and the left button clicks there instead of firing the tool. The Tab menu stays the main way in.
- **E answers only containers, bodies, backpacks, lockers, devices, doors and keycards.** A command chair says F; a machine block opens its CORE panel.
- **Two inventories that never merge** (and one deliberate difference from Empyrion: **one shared storage per core**, across all its containers). Your bag and toolbar are yours and go everywhere. A structure's storage is one inventory, usable only inside its area. Constructors work only from their structure's storage, the suit fabricator only from your bag; you move things by hand. **You choose the source of a block:** your own toolbar draws on the selected stack; the structure toolbar (Empyrion's virtual toolbar) draws on the storage of the structure you are building on, while you are in its area. Neither falls back on the other. A core or a device set down off a structure comes from your own stack.
- **A structure's area** is its blocks' bounding box plus a margin for its class and a share of its size (`build.area`: 30 m for bases and capital vessels, 12 m for small and hover vessels, plus half the longest half-extent). Inside several (a vessel parked by a base), the structure you are building on or aboard wins, and the CORE tab lets you pick. Containers stay open while you are inside.
- **Nothing is lost taking a block down.** The item goes to your bag (to the structure's storage when building from its toolbar), then the structure's storage if you are in its area, then a container on the ground that never despawns.
- **Every structure starts with its core**, founded by a core item: base, small vessel, hover vessel or capital vessel. The last block taken down comes back as that core. A block goes only on the grid and classes its family allows, with a clear refusal ("base blocks only").
- **A command chair gives no air.** Oxygen and warmth come from a sealed room (building's room check), or from your suit.
- **Blueprints are never forward-facing**: no tool on the toolbar, nothing steers you to one; the first vessel is free-built from blocks.
- **Gamey flight controls, a calm opening, streaming ahead of a vessel**: unchanged from before.
- **Four lockers, four pods.** Each pod has a numbered locker, two on each side wall, tied to it by slot (`CryoPod.Slot`, `BunkerLocker.Slot`). The working pod's locker has a green lamp and holds the kit; the others show red and stay sealed (`Locker 1: sealed`) until their sleepers wake, so on a co-op world a joining player's pod and locker come alive together. The numbers are stencilled on the lockers' doors and on the pods' glass in the UI's display face.
- **Nothing is coplanar.** Flat dressing and small parts stand a little off what they sit on (dust 18 mm, a place's dressing 8 mm, pods' lips overhang their sides, the pods' status strip on the roof's lip, not the glass). Placed containers are one ribbed cube: nothing to fight.

## Places

A place is part of the surface world, Empyrion-style: there is no scene of its own, no loading and no swap at a door. Inside one you are on the planet, under its sky through the breaches, among its creatures, with the weather going on outside; digging in through the ground or a wall reaches the same rooms.

- **Standing up.** planet-surface's layer draws every place, solid on the world layer, so a vessel lands on a roof. Within 320 m (`data/places.json`) a place stands up as a real structure (`src/Places`): meshed and planned on a worker, then a couple of milliseconds a frame, so it never hitches and is done long before you're close enough to see in. Only then do the far drawing's blocks give way, in the same frame, and they are the same blocks. Its lamps, glows and smoke stay. The first build of a session, while the game loads, pays the runtime's one-off costs. Beyond 460 m a place goes back to the far drawing; one you changed stays as drawn blocks, so a hole you cut is still there from a distance.
- **Doors.** One door system: E opens and shuts any door, two leaves sliding into the walls beside it, a prompt under the crosshair and collision that matches. A place's locked doors want a keycard of their clearance (a card opens its own and every lower one; the lamp beside the door says which, and goes green once you can). Keycards lie on their dead owners; E takes them. Jammed doors stay half open: squeeze through. Door blocks on your own bases and vessels work the same way and open for you as you come up (`BlockDoors`, on building's own Open flag); a door you shut by hand stays shut until you step away. `BlockDoors.OpenDoors` lists every open door for the force fields to come.
- **Caches** are containers that stay where they stand, empty or not, in the same inventory view with TAKE ALL and STORE ALL. Amounts are by what the generator rolled: a handful of materials, a few parts, one tool.
- **Hidden rooms.** A cracked panel with a faint draft of dust through it (the crosshair says CRACKED PANEL). A few shots or one dig opens it; the room behind has been sealed since the fall. The Hollow Bloom can grow over the way: the survival tool cuts it.
- **Ladders and lifts.** `WalkController.Climb`: W climbs a ladder and carries you over its top; a lift shaft is ridden Empyrion's way, Space and Ctrl, the car under your feet, easing level with a stop when you let go. E on a shaft door calls the car.
- **Salvage.** The survival tool in gather mode takes a place apart: a block gives its material's yield (`data/places.json`), a piece of dressing its salvage class or bill, at half back. Each is finite, and a place in a town takes it badly. Flavour ruins and vehicle wrecks join with `PoiPlaces.AddSalvage(SalvageSite)`: a body the tool's ray meets, a bill, how many shares it comes away in.
- **Dressing.** Each room is dressed from the decorations library by its purpose, state, age (new, old-world or preserved), exposure and the place's tech. Pieces that block walking collide; litter, things on tables and ceiling lamps are scenery you can aim at. The generic props go from a dressed room. A piece never stands on a cache, a keycard, a stair or a doorway, and every way the architect checked stays open round the solid pieces (`Decor.KeepWaysOpen`).
- **Creatures.** Each place has its own sim anchored at it (`DeepSite`), holding wildlife's population for its rooms: lurkers, prowlers, ambushers with their tells, nests deeper down, subsisters and what eats them, and some places empty and silent. Nothing about them is lost when the surface patch moves on or when you leave and come back. Caves get the same: a chamber found round you underground is filled once, on a worker. Surface creatures keep day and night (`ActivityCycles`), and can't reach you underground or under a roof.
- **Creature bodies.** A creature's collision comes from its own rig (`CreatureBodies`): capsules on its hit spheres, the neck joining them, a big creature's legs down to the feet, pooled, within a range that grows with its size. Flyers have shapes; a swarm doesn't. A head takes half as much again. A shape never shoves you (a capsule that has come down round you lets go until you part). A vessel that hits a creature meets the one damage formula with the reduced mass.
- **Towns.** People go about their day (`Town`): vendors at stalls, workers working, rushers hurrying between far spots, wanderers ambling, sitting and chatting in pairs, guards on short beats, everyone but the guards indoors at night. Wary guards watch you; hostile guards take aim where you can see it (a warning on the visor) before they fire. Shooting in a peaceful place, or taking it apart, turns it hostile and its people run for cover. Domestic animals are wildlife's: working beasts only where tech is low, stock there and on farms, pets anywhere. Wildlife keeps out of town, guards cull strays, and a predator may take one of its people outside it. Bodies are placeholders. The tech level is the settlement's own until the world profile lands.

## Test it

```powershell
.\tools\test.ps1 -Project prototypes/slice -Scene res://tests/PlayerTest.tscn
.\tools\test.ps1 -Project prototypes/slice -Scene res://tests/SliceTest.tscn
```

### The real-player test (`tests/PlayerTest.tscn`)

The loop played the way a person plays it, crafting and carrying blocks, headless with `--fixed-fps 60`, on a fresh temperate world each run (it prints `world: seed N, type`; `-- --seed N --planet type` replays one): the castaway wakes in the cryopod, and from then on only the game's own inputs are used (look, walk, keys, the mouse on the Tab menu and on in-world screens: every click and drag lands on a widget the panel laid out). No teleports, no grants, no skipped steps. The one help is a clearly printed **TEST TIME-SCALE** on crafting (40x), so an hour of fabricating fits in a run. Creatures are held still and the weather calm, so a predator can't decide the run.

Latest run (branch `proto/block-items`): **ALL PASSED, 83 checks** in 129 s of wall time (8.9 min of game time). The start site is 42 m from the landing site, 8 steps up, facing an abandoned deep bunker 860 m off. The route and its numbers:

1. **The bunker.** The castaway wakes in the cryopod, its lid shut, 6.0 m under the ground, empty-handed, the visor saying to press Tab. Tab: the suit fabricator makes the survival tool, free, in 3 s; dragged to the toolbar it is in hand and the hint goes. F opens the pod; out into the chamber; E on the locker, TAKE ALL (4 trail rations, 2 patch kits, 2 air cells, 6 biomass). A shot inside: a bolt from the muzzle and a scorch on the wall exactly at the aim, the bunker unharmed; the tool digs nothing through its floor. Up the stairwell's 8 steps to the surface on foot (5 s), 56 m away, back down the stairs into the chamber and out again, never rescued.
2. **Gathering.** The tool in hand (seen in the first-person hands); 20 cells of iron give 240 iron ore (12 a cell); the deposits near the start hold the whole loop's ore; a dug-out deposit drops off the compass; a plant cut gives 2 biomass.
3. **Shooting.** A shot at the ground: bolt, sparks and a scorch 0.05 m from the aim. A hammerhead stalker shot down from 41 health, each hit with a bolt, a spray and the visor's marker: +21 XP.
4. **The body and the inventory by hand.** Its body is a container (2 hide, 8 raw meat); E opens it beside the bag; a drag moves the meat, shift-click the hide, take all empties it, right-click splits a stack, a patch kit is dragged onto toolbar slot 8; the empty body goes once closed. Lifting off on the jetpack never flashes IMPACT.
5. **The suit fabricator** (YOU tab) makes a survival constructor in 45 s of play while the castaway walks; dragged to toolbar slot 3 and set down on open ground.
6. **The base core.** E opens the constructor's hopper beside the bag; STORE ALL loads 745 SU of ore. Then its own in-world screen, at eye height: a cursor where you look, +1 on the base core clicked there; it plans 6 lines of sub-parts and the core comes out 53 s later.
7. **The base.** The constructor crafts the base's blocks as items (a container, a generator, two steel); level 4 by then, the small constructor unlocked on the TECH tab and crafted too; TAKE ALL; the core set down founds a base (large grid, base mode, fixed body). Stacks dragged from the bag to the toolbar, 5 large blocks built by looking and clicking, each using one from its stack, the steel stack used up and its slot empty. The tool's deconstruct mode takes a block down, its item back in the bag, and it goes on again.
8. **Level 5 and the small vessel's core.** STORE MY ORE AND MATERIALS; the base's small constructor makes the vessel's parts; **level 5 at 727 XP after 4.4 min of play** (crafting at the test time-scale); the small-vessel core, thruster and gyro unlocked with points (the command chair is free from level 1); the constructor crafts the core and the vessel's block items into the base's storage, and TAKE ALL brings them to the bag.
9. **The vessel.** The core set down starts a small vessel on its build stand; 17 of 17 blocks built from carried stacks on the toolbar (a command chair on top from a jetpack hop, lift jets and a retro turned with R/T, a down jet, three legs), lift margin +67 kN, never in the ground. F at the chair; the CORE tab shows its four numbers; a chair in the open gives no air; Space lifts it 108 m in 2 s; it flies 574 m at up to 70 m/s and lands on its gear, undamaged.
10. **The rest.** Every tab opens by its own button; L toggles the head torch (45 m, 32°). A running jump off the hovering vessel from 115 m kills the castaway: everything carried is in a backpack where they fell; R wakes them in the bunker; they walk to the marker, E, take all, everything back. The castaway is never rescued from the ground all run.

Game time at each step (min): out 0.8, gathered 2.2, hunted 2.3, looted 2.3, base core 3.3, base 4.0, level 5 4.4, vessel core 4.7, vessel built 5.8, flown 7.6, recovered 8.9. Level 5 came at 720 XP after 4.6 minutes of play; the vessel flew 560 m and landed undamaged. Dying takes more than it did: a suited body falls no faster than about 41 m/s and players land at 15.5 m/s unhurt, and a landing on a slope glances, so the vessel hovers 220 m over the flattest ground along a walkable way out of the bunker, the castaway steps slowly off it, lands on a sliver of health and a hop on the jetpack finishes them; R wakes them in the bunker and they climb out to their backpack.

### The systems test (`tests/SliceTest.tscn`)

Fresh seeds, determinism, the bunker, gathering, cutting a tree (its collider gone the frame it falls), the Skiff blueprint projected from the menu and built block by block, one item each, the same in Creative for nothing, a 2 km flight and landing, a hop flown twice to the tick, creatures warned before they bite, and nothing stuck in the ground. It starts with the cryopod already open (the real-player test wakes in it shut) and, as a systems test, hands over the Skiff's block items and opens the tech tree: printed when it does.

Latest run: **ALL PASSED, 44 checks** in 54 s. Two fresh games roll different seeds, and `--seed`/`--planet` pin both; the planet, its fauna and the starter blueprint generate identically twice; the weather opens calm for 12 minutes. The cryo bunker is dug in at the start site, 6.5 m under the ground and above the sea, 91 m from the generator's landing site, 19 steps up; the castaway wakes in their cryopod with 3 dead sleepers beside it; the start site is survivable with the planet's own wildlife around it. The locker emptied by TAKE ALL and up the stairwell on foot, never rescued. Ore dug as real terrain from the nearest shallow deposits the bunker wasn't dug through; a tree cut (its collider gone the frame it falls). The Skiff blueprint built block by block (174 of 174, each one item, never pushed out of the ground), the same in Creative for nothing; flown over 2 km and landed on its gear undamaged; a short hop flown twice identically to the tick; a creature's attack warned (THREAT) before it bites; nothing stuck in the ground.

### The build test (`tests/BuildTest.tscn`)

Blocks as items and the storage model round them. Pure checks: the HUD at six window sizes and four UI scales, nothing overlapping or leaving the window and the longest item line fitting its box; text and category filters, every sort, moves under a filter, and a storage of thousands of stacks reconciling, filtering and redrawing inside a frame; every device in all 24 orientations on both grids, a thruster changing only its axis; a chair in a sealed shell, and the same shell holed. Then in a world, handing items over and teleporting (printed when it does): no block in the open, each core founding its class, small blocks refused on a base, a base-only block refused on a small vessel; your toolbar drawing only on your stack, the structure toolbar only on the structure's storage, neither falling back; deconstruct into the bag, a full bag into the structure, both full into a container on the ground; the area for all four classes, 0.25 m inside and outside; a vessel parked by a base; constructors and the suit fabricator each keeping to their own storage; the filter box taking typing without moving you, Escape's three steps; tooltips for a block, an ore, a part and something locked; Creative free under the same rules.

Latest run: **ALL PASSED, 96 checks** in 17 s.

### The place test (`tests/PlaceTest.tscn`)

With `--fixed-fps 60`. `-- --walk-only` runs just the visit, `-- --town-only` the settlement, `-- --survey` lists a world's places.

- **Systems** (focus moved by hand, printed): a place stands up within the frame budget (47 chunks, worst frame about 4 ms of building, about 5 ms for its creature views), the far drawing gives way in the same frame; door blocks open as you come up, shut behind you, and E works them; creature capsules from the rig (a giant solid at chest height, a flyer shot, a head worth half as much again, a vessel knocking a small creature aside, worst tick 1.2 ms); the salvage hook; a cave found and filled, on a worker.
- **The real-player visit** on the run's world (no teleports, largest move in a tick 0.2 to 0.6 m, never rescued): out of the bunker with the kit, 900 m over the land round cave mouths to the deep bunker in view, down its stairs, a cache looted, a locked door refused, its keycard taken from its dead owner, the door opened, the Bloom cut through, the hidden panel broken, its stash looted, a dressing piece and a wall block salvaged, a hunter's tell met and the hunter shot, out again. Creatures are held still while the place is walked (printed), as PlayerTest does, and wake for the meeting: a nest on the lowest level would otherwise decide the run.
- **A settlement** on seed 19: its people going about their day, walked through in peace, indoors at night, a shot turning it hostile, guards taking aim before they fire. The castaway is set down 55 m from its way in (printed): the 2 km land route there crosses a river and wasn't walked.

### The start test (`tests/StartTest.tscn`)

The owner's first concern: that you can always get out. Two fresh games must roll different seeds; then, on each world, the castaway wakes empty-handed in the cryopod, makes the survival tool in the suit fabricator, opens the pod, takes the locker's kit, climbs the stairwell to the surface, walks 50 m away and back down into the chamber, climbs out again (the game's inputs only; a watch on every tick proves no teleports and no rescues), and from the way out has a place in clear view 300 to 1,200 m off. The walk out goes along a way the site was chosen for, then step by step over the terrain's real collision, never stopping at the lip of a drop, and comes back by every breadcrumb.

- **The gate** rolls 3 random worlds every run, any planet type, each printed as `world: seed N, type` to replay.
- **The soak** (`-- --seeds N`, not in `test-all`) runs N random worlds: a fresh seed and a random planet type each. Latest, on this branch, tool made first: **20 of 20 passed** (281 checks), none needing a jump or the jetpack on the stairs or at the way out. Earlier soaks found worlds with no start site in reach of the landing site (the search now goes round each place on the planet), a hop on the stairs landing the walker on the kerb, and a lip at the top step or a half-metre riser the walker caught on (the ramps and the aligned top tread).
- `-- --seed N --planet type` runs one world.

## Pacing

The design target is a small vessel within one to two hours for an experienced player. shared/Items' pacing reference (45 ore a minute with the survival tool, 12 ore a cell, 1.2 s to place a block) and the real-player test's own route give:

| Step | What it takes | An experienced player |
| --- | --- | --- |
| Out of the cryopod, locker emptied, up the stairs | | 1 min |
| Ore for the whole loop (about 800 raw: iron, copper, carbon, silicon, a little ice and stone) | 45 ore/min | 18 min of digging, spread over the run |
| Survival tool, then the survival constructor (suit fabricator, 3 s and 20 s) | 28 ore | at about 4 min |
| Base core and the foothold's parts (survival constructor, half speed) | about 280 runs | 12 to 15 min, while gathering |
| Base blocks crafted and built (core, container, generator, small constructor, two steel) | 6 large block items, about 1 min on the survival constructor | 2 min |
| The vessel's parts (small constructor) and core | about 120 runs | 6 to 8 min |
| The vessel's block items crafted, built (13 blocks) and flown | about 1 min of crafting on the small constructor | 4 min |

So **about 47 to 57 minutes** from the crash to flying, inside the target with room for food, air, weather and creatures.

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
| `shared/Items` | Blocks as items: `ItemCategory.Block`, `ItemDef.BlockId/SmallGrid/Family/Places/Group`, `BlockBill.ItemFor`, `ItemCatalogue.BlockItem/BlockItems/NameBlockItems`, `StructureClass`, `BlockPlacer` by item, `Constructor.Lock/CanMake/Blockers`, free recipes | Blocks are craftable items, as in Empyrion |
| `building` | The cockpit becomes the command chair (`IsCommandChair`, `"chair"`); `IsCockpit` and the `cockpit` id stay as aliases for old blueprints | Every seat is a command chair |
| `shared/Physics/Core/WalkController.cs` | `Climb` (`ClimbMode` None, Ladder, Shaft), `ClimbSpeed`, `ShaftSpeed` | Ladders and lift shafts: held against gravity, moved by the walk |
| `planet-surface/src/Poi/PoiLayer.cs` | `PlaceLayer` (default its own `Layers.Poi`), `NodeOf(site)` | The slice puts places on the world layer, and hides a place's far blocks while it stands it up |
| `shared/Items/Core` | `ItemDef.StackSize` (data `stack`, a default per category), `ItemDef.Unlock` and `BlockBill.UnlockSmall/UnlockLarge` (data `unlock`), the `Unlock` record, `BlockBill.NodeId` | Empyrion-shaped inventories and a tech tree carried by the items and blocks themselves |
| `shared/Items/data` | `unlock` on every block (per grid) and on tools, consumables and ammo | The tree's nodes; materials and parts have none (always made) |
| `planet-surface/shaders/sky.gdshader` | The sunset term's base clamped (and two other `pow` bases guarded) | Straight away from the sun the dot product rounded past -1 and `pow` of a negative returned NaN: a dotted dark line down the sky on the anti-solar azimuth at dawn and dusk |
| `planet-surface/shaders/terrain.gdshader`, `src/Terrain/TerrainStreamer.cs` | The horizon mesh sinks inside the far rings' square (`hole_sink`) and is cut only well inside it | Where the coarse horizon stood above the far rings' outer edge, hard-edged notches of sky showed in hills about 1 km off |
| `shared/Physics/Core/PhysicsWorld.cs` | `Release(body)`, `Adopt(bodies)` | A zone handover: bodies leave one world and join another as they are, riders aboard |
| `ship-handling/src/Handling/FlightModel.cs` | `FlightState.Seat`, `Control`; turn, cruise and thrust in the seat's frame | A vessel's heading is set by the pilot's seat |
| `planet-surface/shaders/sky.gdshader`, `src/Sky/Atmosphere.cs`, `SkyRig.cs`, new `CloudCoverage.cs`, `clouddeck.gdshader` | The cloud layer lowered to 600 to 1,000 m, its march scaled to it, `space_fade` into space's backdrop, a cloud deck from above, the cloud at the eye | Orbit at 1,060 m; the atmosphere fading away as the mask for the crossing |
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

- **The escape pod and its descent are gone**; the bunker start lives in `Game.Start.cs` and `src/Start/Bunker.cs`, apart from the rest.
- **Places:** a faction's core can only be shot out so far (hijacking waits for factions, and start worlds have no cores); doors don't stop creatures, which move through their spaces; townsfolk are placeholders with no trade (trading comes last); a frame shift across the planet's seam doesn't move a cave sim's rock with it; salvaged blocks stay gone but aren't saved (phase 3).
- **Saves:** none yet beyond the zone ledger, which records where everything is at each crossing (phase 3 loads). Progress lives in one `ProgressState` ready for it.
- **In orbit** nothing builds yet but a core into a wreck: block building waits for the block-items work. A claimed wreck is a station: it holds still and can't be flown. A wreck's blocks taken away don't split it into pieces.
- **Machines stop in a parked zone:** a base's constructors wait while you are in orbit.
- **No creatures in derelicts yet**, and shooting in space hurts nothing.
- **The crossing's handling** is the physics' one altitude switch: weight goes and the top speed changes at 1,060 m; thrust doesn't yet thin on the way up.
- **Lateral flight in the overlap band** is flat: you arrive over what you see under you, which drifts from the globe's point if you fly sideways through the band. A straight descent lands exactly on it.
- **Loose containers** aren't carried with a vessel across a crossing (they never ride one).
- **Lava and ice worlds** start like any other (the test climbs out and walks 50 m on each), but the slice hasn't tuned survival on them beyond that.
- **Deco items** craft and unlock but can't be placed by hand yet: the slice's build tool places building's blocks, and deco pieces aren't among them.
- **Capital vessels** found from a capital core and hold on their stand; flying one isn't tuned. Warp and hyperdrive blocks craft and place and do nothing yet.
- **Blueprints:** the menu's one entry projects the Skiff; there is no save or library here yet.
- **Equipment is read-only**: the paperdoll's armour and booster slots show the survival suit's; there are no armour items to drag in yet.
- **Power is a switch**: a generator on a structure powers its machines; fuel isn't burned yet, and the lights toggle doesn't switch light blocks yet.
- **Capital mode** can be set on a base once the capital vessel tech is unlocked, but the base stays anchored: lift-off belongs to capital-base and isn't wired here.
- **A new vessel holds on its build stand** until first flown: it doesn't drop onto its legs by itself.
- **Hover vessels** have their tab, core and pads, but the slice's handling of hover pads isn't tuned or tested.
- **Deposits under deep rock** don't show on the compass: the survival tool can't cut deep rock.
- **Creatures** are solid to the castaway through blockers on the props layer; they don't collide with vessels or each other's blockers, and they read the natural ground, not dug holes (wildlife's limit).
- **Plants are cut whole** and don't regrow; **the open Skiff** has no cabin.
