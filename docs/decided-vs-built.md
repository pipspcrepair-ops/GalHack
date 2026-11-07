# Decided versus built

An audit of what the design says against what the game does today. Read-only: no code was changed.

**What was compared**
- The design: `docs/DESIGN.md` on main (revision 561), with newer decisions from `docs/playtest-list.md`. Line numbers like "DESIGN 759" refer to `docs/DESIGN.md`.
- The code: worktree `G:\GalHack-wt\batch`, branch `proto/batch2` at 7611301, plus the other branches in section 5. File paths below are relative to that worktree. `slice/` means `prototypes/slice/`, and `items/` means `shared/Items/data/`.
- Skipped on purpose: anything the playtest list already marks as an agent running, unless the code disagrees. Items that are on the list but not running are included, and say so.

Counts: contradictions 11, decided but missing 25, partly built 14, not started by design 10, on unmerged branches 9, doc problems 13.

---

## 1. Contradictions: the game does what the design forbids, or uses a retired name or structure

Worst first.

1. **Four cores instead of Big Core and Small Core.** DESIGN 759 and 768 say one Big Core (base and capital) and one Small Core (vessel and hover), each with a mode switch. The game has four core items: Base core and Small-vessel core (`items/blocks.json:67-70`), plus Hover core and Capital core (`slice/data/items.json:32-35`, recipes at `:66-67`). All four are separate nodes in the tech tree and separate crafts, and the code lists them as `CoreItems` (`slice/src/Items/PlayerInventory.cs:54`). The player picks BASE core, CAPITAL core, HV core or SV core up front (`slice/src/Construction/BuildTool.cs:385-388`). Hover versus small is fixed when the core is set down; there is no Vessel Mode or Hover Mode switch. `proto/cores` has no commits and a clean worktree, so nothing is written yet.

2. **A base cannot hold the ship parts the design says it can.** DESIGN 762: thrusters, command chair and so on "can sit on a base beforehand and do nothing until the core is switched". In the data the large command chair, thruster, gyro, hover plate, landing gear, warp drive, hyperdrive and fold tank are all `places: capital` only (`items/blocks.json:75, 78, 108, 111, 114, 144-160`). On a base, the player is told "Thruster: capital vessel only" (`shared/Items/Bridge/BlockPlacer.cs:58-63`). A base cannot even have a seat. On the small side the hover plate is `hover` only and landing gear is `small` only (`blocks.json:111, 114`), which is why the two small cores are separate.

3. **The Base/Capital mode switch is refused, and the "mode" signals are missing.** DESIGN 770 says the switch is in the menu at any time and "nothing refuses it", with warning stripes around it and a seated indicator. The CORE tab disables the switch to Capital until the large thruster is unlocked (`slice/src/Ui/CoreSection.cs:254-260`). There are no warning stripes and no seated mode indicator. Small cores have no mode at all (`prototypes/building/src/Structures/Building.cs:310` returns false for the small grid).

4. **CPU tier 3 and 4 are not gated by exploration.** DESIGN 480, 606 and 883: T3 and T4 extenders need lattice cores and arrays found in POIs. The game has one craftable CPU extender (circuit boards and logic modules, `items/blocks.json:100`) and tiers come from the count alone (`prototypes/building/data/grids.json`, extendersPerTier 0, 1, 2, 4). Lattice items exist (`items/items.json:130-131`, `items/recipes.json:63-64`) but nothing in the building or slice code uses them.

5. **Plain armour costs CPU.** DESIGN 874-876: "Plain armour and structural blocks cost none." Steel costs 20 CPU per block, hardened 45, combat 90, glass 15, concrete 10 (`prototypes/building/data/materials.json`). This is on the playtest list ("CPU: only devices count") but not running. The CORE tab CPU row shows the result.

6. **The uncommon ores cannot all be found, and some cannot be found at all.** DESIGN 626: "Every planet has deposits of all of them" (iron, copper, silicon, stone, burnstone, cobalt, magnesium, neodymium). The world generator guarantees only iron, copper, silicon, carbon and water near any start (`prototypes/planet-generator/src/Gen/Deposits.cs:8-14`). Burnstone, cobalt, magnesium and gold are only an archetype's single "signature" deposit, in about one cell in four, with no promise (`Deposits.cs:46-48`; signatures at `Archetypes.cs:122-250`). Neodymium and gravium have no deposit source at all (`items/items.json:51, 55` have no `deposit`; the generator has no matching kind). Magnets need neodymium (`items/recipes.json:48`), so large thrusters (`:56`) and large gyros cannot be crafted. Hardened and combat steel need gravium (`blocks.json:48, 51`). Raw fold crystal turns up on canyon and crystal worlds (`Archetypes.cs:170, 250`), where DESIGN 598 puts it on irradiated ice and dead-star asteroids.

7. **Creatures spawn right beside your base.** DESIGN 310: no spawns within a radius of any base. The rule exists in the wildlife prototype (`prototypes/wildlife/src/Core/Structures.cs:42`, `WildSim.cs:157`), but the slice never registers its bases with the wildlife sim (no reference anywhere in `slice/src`). Only town zones are respected.

8. **The core can be removed.** DESIGN 693: "The core can't be removed, only destroyed or hijacked." The player takes the last block down and gets the core item back, and the structure and its storage disappear (`slice/src/Construction/BuildTool.cs:757-770`).

9. **The survival tool drops on death.** DESIGN 525: "The survival tool is never dropped." Death empties the whole pack into the backpack with no exemption (`slice/src/Game/Game.Items.cs:226-233`). The item data comment says "a lost tool can always be replaced" (`slice/data/items.json`, survival-tool recipe).

10. **Blueprint factory can go on small and hover vessels.** DESIGN 822: bases and capital vessels only. The block has no `places` limit, so it exists on the small grid at level 12 (`items/blocks.json:134-136`).

11. **Names that differ from the design.** DESIGN 539 and 538: Hand drill T1/T2, medkits, O2 bottles small +75 and large +250, ration paste. The game has Mining drill and Plasma drill (`slice/data/items.json:22-25`), Patch kit and Trail ration (`:26-29`), one Oxygen bottle that refills the whole tank plus an Air cell that refills half (`slice/data/items.json:30, 80-81`, `items/items.json:135`). The start screen's "Barren" is the Arid desert archetype (`slice/src/Game/WorldChoice.cs:20`), not the design's cratered, airless Barren world.

---

## 2. Decided but missing

Grouped by area, most noticeable first.

**Player and survival**
1. **Crouch** (hold Ctrl, lower, quieter, fits under overhangs; DESIGN 918). The word does not appear anywhere in the code.
2. **Respawn choices** (DESIGN 526): home base, nearest clone chamber or healing booth, nearest survival tent, or a fresh start. The game respawns you at the bunker only (`slice/src/Game/Game.Items.cs:245-250`). No healing booth, clone chamber or survival tent exists as an item, and the death-drop difficulty setting has data but no way to change it.
3. **Food spoils** (DESIGN 520), and **replicator tiers T2 and T3** (DESIGN 512-516): no age on any item in the slice's inventory, and no galley or advanced replicator in the block list. Only the T1 survival kit works (B to deploy).
4. **Discovery card, codex, hold-Q scan, PDA tutorials** (DESIGN 307, 423, 1328). `slice/src/Surface/WildlifePatch.cs:23` says the discovery card "is not wired in". The Tab menu has You, Tech, Stats, Core and Map only; Q switches tool mode. The owner's note that the card is back on is not in the playtest list.
5. **XP for first visits to places and for looting** (DESIGN 473). XP comes from ore, plants, crafting, building, kills, salvage and first deposit or species only (`slice/src/Game/Game.Progress.cs:21-23`, `Game.Items.cs:275-396`).
6. **Survival kit items**: tent, portable heater and cooler, emergency O2 and water generator (DESIGN 544). Only the head torch exists.

**Tools and weapons**
7. **Every handheld weapon** (DESIGN 548-562): pistols, SMG, shotgun, rifles, sniper, launchers, flamethrower, melee, faction weapons. The survival tool's shot is the only gun. The weapons tab holds only the gatling, turret and rocket pod (`items/blocks.json:96-165`). On the playtest list, not running.
8. **Multi-tool, chainsaw, handheld detector, paint sprayer, Hijack Tool** (DESIGN 538-546). The paint cartridge exists as an item with no tool to use it. Deconstruct is a mode of the survival tool.
9. **No way to repair a damaged block.** `VesselDamage.Repair` has no caller outside tests (`slice/src/Vessels/VesselDamage.cs:192`). The multi-tool, repair bay and field repair station are all missing, as is upgrading armour in place.

**Structures and components**
10. **Missing devices** (DESIGN 814-827): solar panels, capacitors, shield generators and extenders, healing booth, clone chamber, repair bay, field repair station, teleporter, auto-miner, switches and sensors with device groups, displays, shutters, ramps, lifts, remote-control console, force-field emitters, spotlights, docking pads, jet thrusters, mining drills, harvester blades and salvagers, minigun, flak, plasma and artillery turrets. There is also one fuel tank size (not T1-T3), one container size and one thruster size. `items/blocks.json` lists every block there is.
11. **Fuel is never burned and power is not a budget.** Generators and tanks only add to the stats (`prototypes/building/src/Structures/StructureStats.cs:61`); nothing in the slice consumes fuel. DESIGN 856-858. On the playtest list ("power budget, thrusters and drives using CPU and fuel").
12. **Station roles other than pilot** (Navigation, Gunnery, Engineering, Scanning; DESIGN 796-808). `slice/src/Vessels/VesselRoles.cs` only has the pilot.
13. **Docking** (DESIGN 787-792) and **build-mode visor aids**: no live power, CPU and mass readouts on the visor, no warning before crossing a CPU tier, no airtight overlay (DESIGN 1362-1364). The numbers are on the CORE tab only. No visor mini-map (DESIGN 1346).
14. **Breaches and force fields** (DESIGN 833-838). Sealed-room detection exists and decides breathing (`slice/src/Player/Suit.cs:99-110`), but there is no venting, alarm, power-loss countdown or force field.
15. **Flight assist toggle hotkey** (DESIGN 941). Assist is always on. On the playtest list ("thrust hold toggle"), not running.
16. **Cyber modules and charged combat armour** (DESIGN 475, 724): no charged material in `prototypes/building/data/materials.json`, and no unique-equipment path outside the tree.

**Gathering and economy**
17. **Auto-miners, vehicle drills, plant and ore regrowth, resource meteorites, ore richness options** (DESIGN 615-616, 641-642). None exist in `slice/src`.

**Travel and space**
18. **Warp drive, hyperdrive, fold crystal use, route planner, short-range warp, shields, teleporters, wormholes** (DESIGN 963-973). The blocks exist as data only; `items/blocks.json:138` says "warp and jumps aren't built yet". The map text promises "travel between planets: warp drive" and "hyperdrive" (`slice/src/Ui/MapLevels.cs:242, 286`).
19. **Anywhere to go but home.** One world, its orbit and a few wrecks (`slice/data/orbit.json`). No moons, other planets, asteroid fields, stations, or the start-system guarantee of an ice neighbour, asteroid field and trading station (DESIGN 203).
20. **Scanning ladder and discovery rewards**: orbital scan, survey probe, system scan, signal leads, survey data, first-discovery records and danger grades on maps (DESIGN 412-435, 452). Only eyes, the built-in ore scanner and own map marks exist.

**World**
21. **Planet and star variety.** The generator has five archetypes (`prototypes/planet-generator/src/Gen/PlanetSpec.cs:6`) and five star classes (`prototypes/planet-surface/src/Sky/SkySystem.cs:9`), against about fifteen archetypes and ten star classes in DESIGN 133-195. The eight exotic themed planets (DESIGN 216-225) are absent. The 40 world types are written in `docs/world-themes.md` and none are built (playtest list).
22. **Flocks, schools, aquatic and space creatures** (DESIGN 299-300, 312). The wildlife prototype has swim and space rigs, but the slice shows none. Aquatic species are on the playtest list, not running.

**Modes and persistence**
23. **Relaxed survival, creative workshop and every difficulty dial** (DESIGN 1290-1322). The start screen offers only Survival or Creative, a planet type and a seed (`slice/src/Ui/StartScreen.cs`).
24. **Saves** in the slice (playtest list; the slice only keeps a zone ledger across orbit crossings, `slice/src/Orbit/ZoneLedger.cs`).
25. **Co-op** is not in the slice at all.

---

## 3. Partly built

1. **Core modes**: Base/Capital toggle exists (`CoreSection.cs:254`); Vessel/Hover mode, indicator and stripes do not (see 1.1 and 1.3).
2. **Tech tree**: Base & Capital and Small & Hover are one tab each, with hover plate before thruster, as designed. There is an extra DECO tab, and the large hover plate sits on the SMALL & HOVER tab (`items/blocks.json:112-113`). No node exists for most of section 2.
3. **Shape picker**: 8 shapes (`prototypes/building/data/shapes.json`) against 100+ in DESIGN 695.
4. **Symmetry**: one mirror axis (`slice/src/Construction/BuildTool.cs:65`) against three in DESIGN 694.
5. **Blueprints**: only the starter Skiff projector, from the CORE tab (correctly not on the toolbar). No library, sharing, repair template or save-as-blueprint.
6. **Armour and boosters**: the model has four suits and eight boosters (`prototypes/survival-vitals/data/armor.json`) but nothing to craft, find or equip. The start inventory is empty of both (`slice/data/survival.json`), so the player only ever has the light suit. No interior wear.
7. **Building in space**: claiming a wreck with a core and salvaging work. Building in orbit is refused ("Building in orbit comes later", `slice/src/Orbit/Spaceflight.Hands.cs:64`).
8. **Maps**: three levels, own markers, shared marks, discovered POIs. The star map is the home star plus 4-7 invented neighbours with no jump (`slice/src/Orbit/Charts.cs`).
9. **Sky**: moons, one gas giant, nebula backdrop (`SkySystem.cs`), not the real system. On the playtest list and running on `proto/space`.
10. **CPU**: tiers, extender counts and the efficiency penalty work. Drive and shield spikes do not exist, since there are no drives or shields.
11. **Station storage**: shared storage per structure, size caps, remote toolbar (Y), Home and auto-transmit all work. Dragging between containers and multi-select are on the playtest list.
12. **Levelling**: cap 25 in `slice/data/progress.json`. The planned 343-node, levels 1-25 tree is written (`docs/tech-tree-pacing.md`) and not built.
13. **Wildlife**: activity cycles, telegraphed lunges, rests and dens work. No flocks or schools; cave and POI creatures work but their size limits are on the playtest list.
14. **Salvage**: wrecks return 33% as designed (`slice/data/orbit.json:62`); ruins return 50% (`slice/data/places.json:22`).

---

## 4. Not started, by design

- Factions, standing and territory
- Base invasions and sirens
- Trading, credits, traders and survey-data sales
- Boarding, hijacking and the alarm ladder (prototyped, not in the slice)
- NPC ships, crewed patrols and the encounter director (the playtest list wants one roving ship, later)
- Hostile NPC fire and warning shots
- Steam co-op and always-on servers
- Blueprint library sharing
- Expeditions, unique set pieces and precursor arcs
- Wormholes and space anomalies (nebulae, pulsars, fold scars and the rest)

---

## 5. On unmerged branches

Compared with `proto/batch2`. Work in a worktree that is not committed is not on any branch.

- **`proto/overhaul`** (17 commits): region plan before placement, worlds from layers, occupancy as its own layer, road network with highways and grid streets, large places on the 2 m grid, farm, brutalist complex and sewage works, scavengers and squatters as people, start worlds keeping a landmark and a large place within reach. Covers DESIGN 255-270 (world profile, infrastructure, region planning, themes) in part.
- **`proto/weather2`** (8 commits): setting suns, rain everywhere, storm cells (rain, strikes and tornadoes from visible towers), stratus and cirrus, wind howl at openings, ice water, lava contact and the **climate control node** (DESIGN 501), blizzards and fire storms draining health with a deep hole as shelter, tornadoes tearing up trees and throwing creatures, and **swimming and underwater**. Covers most of DESIGN 227-239, 272-275 and 959.
- **`proto/flyers`** (2 commits, detached worktree): gliders never stop, swooping attacks, hoverers are their own body. Covers the flying rule; not flocks.
- **`proto/space`**: one commit (climb capped at 1,060 m, the world's own air as a shroud, one ground sheet to the globe). Uncommitted edits in its worktree to the globe and orbit rules.
- **`proto/galaxy`**: the same single commit as `proto/space`, with a clean worktree. The seeded star catalogue (DESIGN 98) is not written anywhere yet.
- **`proto/cores`**: no commits and a clean worktree. The Big Core / Small Core change (section 1.1-1.3) is not started in code.
- **`proto/facades`** and **`proto/natural`**: no commits beyond `proto/batch2`; both worktrees have uncommitted edits (POI layer and kit files; POI generator, states and a new Growth.cs).
- **`proto/ruins`**: already contained in `proto/batch2` (no commits ahead).
- **`proto/gallery`** (README banner and gallery shots), **`proto/lift-test`** (a test for a half-buried structure boring out; relates to DESIGN 763) and **`proto/mockups`** (a corridor-width mock): no game features.

---

## 6. Where the design contradicts itself or looks out of date

1. **Level cap**: DESIGN 474 says 25; DESIGN 475 says "Player levels run to about 50". The playtest list and the data say 25.
2. **Small vessel speed**: DESIGN 922 says 50 m/s level, 90 vertical, 115 in space; DESIGN 939 says 70 in air, 130 in space. The code uses the second (`prototypes/ship-handling/data/handling.json:13`).
3. **Detector range**: DESIGN 412-414 says about 1 km handheld and 2.5-10 km vehicle; DESIGN 542 says about 1,250 m and 10 km; DESIGN 613 says 2.5 km.
4. **Armour and CPU**: DESIGN 722 says combat steel "costs more CPU"; DESIGN 876 says armour costs none.
5. **Hover vessel level**: DESIGN 476 and 753 gate it at level 1; DESIGN 1329 says it is offered at level 3.
6. **Names**: DESIGN 464 says "suit constructor"; DESIGN 1341 and the code say suit fabricator.
7. **Misplaced text**: the weather forces and forecast bullets (DESIGN 272-275) sit under the POI themes paragraph, after the weather table. DESIGN 298 joins wildlife placement and POI corridor widths in one bullet.
8. **Respawn**: DESIGN 88 says healing booth, clone chamber or fallback; DESIGN 526 lists four options including tent and fresh start.
9. **Pacing stages**: DESIGN 466 lists the hover vessel for stage 3, but the hover bike (DESIGN 545) comes from the suit fabricator at the start and appears in no stage.
10. **Out of date**: the header says revision 561; the playtest list cites revisions 564 (Hollow mystery) and 580 (one bestiary module), so later edits are not in this copy. DESIGN 1398 still lists the slice as "in progress" and the 5 Nov status.
11. **Decisions on the playtest list that DESIGN does not hold**: personal shields as a bar parallel to health, no weapon wear and no companions, level cap 25 with a slower early curve, the Hollow as unexplained wrongness only (rev 564), debug options on the start screen, music as drones plus CC0 tracks (DESIGN names Kevin MacLeod and other sources), and the 349-weapon and 40-world-type lists.
12. **Tech tabs**: DESIGN 475 lists Base & Capital, Small & Hover, Tools, Weapons and Misc; the game also has a DECO tab, which DESIGN 268 only implies.
13. **Blueprint wording**: DESIGN 740 says "no blueprint tool on the toolbar"; the code keeps a dead toolbar shortcut for it (`slice/src/Items/PlayerInventory.cs:48`) alongside the CORE tab button.
