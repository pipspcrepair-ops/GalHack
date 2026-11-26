# Test audit: what the GalHack tests prove

**26 Nov check, against test-build 5b4ded38: still current.** No game code has changed since this audit's base, f20ac912: the 48 commits since then on test-build touch only docs, the readme images and a few doc tools. So every finding stands as written, line numbers included. Its findings are the input to `root-fix-tasks.md`; that list, not this doc, tracks the fixes.

> Read-only audit of proto/batch2 at f20ac912 (25 Nov 2026), by an audit agent. The director spot-checked MoonTest.cs:101-103 (calms weather and freezes wildlife before leaving) and SleeperToolTest.cs:286-297 (asserts the coreless-block exploit). The rule it proposes is in [ARCHITECTURE.md](ARCHITECTURE.md) section 9.

- **Branch:** proto/batch2 at f20ac912 (G:\GalHack-wt\batch). Read only, nothing run.
- **Paths:** relative to `prototypes/slice/tests` unless they say otherwise.
- **Sure:** C = confirmed by reading, L = likely (header or part read), U = unsure.
- **Context correction:** it isn't only MoonTest that leaves the starter. MoonTest, RoundTripTest, WarpTest, FoldTest, OrbitTest, OrbitReliefTest and GalaxyTest all fly off it. But each one gets there by flying the Skiff from the starter, and checks travel and presentation, not the game's systems (see 3 and 4).

---

## 1. Coverage map

**Key for bodies:**
- S = the starter (whatever type was rolled)
- S* = the starter forced to a type, size or roll (still `Game.Surface`)
- O = home orbit
- M = a moon
- W = another world in the home system
- X = another star
- V = a vacuum body reached by travel

**Key for Real:**
- Game = a `new Game.Game`, the real game
- Model = pure code, or a world built without a Game

### Off-world and travel (the only tests that leave the starter)
| Test | System checked | Bodies | Real | Sure |
|---|---|---|---|---|
| MoonTest | Skiff build, climb, frame hand-over to the moon, crossing mapping, landing, moon gravity/air/sky/clock, a stride, visor marks on the moon (`MarkedHere` :646-655), a second moon | S, O, M (V if airless) | Game; wildlife frozen and weather calmed (:101-103) | C |
| RoundTripTest | 3 loops S→O→M→O→S: clouds, parking, one sky/light/sound per zone, camera far, suit `Beyond`, one clock, keys released, globes, frame-time and level drift (:458-560, :707-708) | S, O, M | Game; weather calmed (:276) | C |
| WarpTest | warp drive and fuel, warps to another world, a moon, a giant and the star, landing on worlds with no zone yet, `Leftovers` (sky/light/sound/globe/camera/gravity, :557-599), levels after 2 loops (:203-213) | S, O, W, M | Game; wildlife frozen, weather calmed (:124-126) | C |
| FoldTest | fold home→A→B→home→A: catalogue system rebuilt, sky/sun, crystal spent, ledger holds a crate at A (:232-233, :255-258), home unchanged (:240-249), a landing on a world of A (`AbroadWorld` :275-334: gravity, air vs suit `Beyond`, flora differs, creature count), freed or parked zones, levels | S, O, X, a world at X | Game; weather calmed (:149) | C |
| OrbitTest (+Sky) | to orbit, derelict, float across, forces its door by the orbit path, hold loot, orbit salvage, claim with a core, back down at a chosen point; one zone per tick; sky one system | S, O | Game | C |
| OrbitReliefTest | globe relief vs band sheet in low orbit | S, O | Game | C |
| GalaxyTest | galaxy from seed, sky directions, parallax, ground-to-space sky | S, O; galaxy model | Game + Model | C |
| SystemsTest (+Barren, +Drives) | system rules: gravity table, vacuum, moons, fuels, barren look | every body, as model only | Model | C (header) |
| ClimateTest.Airless | starting *on* an airless world reads as vacuum | S* (airless start) | Game, **only with `--airless`, which the gate never passes** (see 4) | C |
| GravityTest | g from size: jump, fall, jetpack, bag, Skiff lift | S* forced 8 km and 64 km | Game (a stand-in for a moon) | C (header :21-27) |
| ZoneTest | zone ledger crash at each commit point, reload | none (ledger) | Model (saves' fault injector) | C (header) |

### Starter-only, real game
Every one of these plays on S or S* only.

| Test | System |
|---|---|
| SliceTest, PlayerTest, StartTest, WildStartTest, MatrixTest | the loop, the start, the bunker, start options across depth and eras |
| AftermathTest | death, bag, respawn (:267-271) |
| CombatTest, WeaponTest, ShotFeelTest, GunFeelTest | arms |
| HazardTest | place hazard blasts |
| DamageTest | block damage, lightning |
| SleeperToolTest | the tool: gather, salvage, repair |
| PlatingTest | plating |
| BuildTest, BlueprintTest, BarTest | build and items |
| CoreTest | core modes |
| PowerTest | power |
| ForceFieldTest | fields |
| ClimateTest (+Rooms) | swim, lava, rooms' air, doors |
| CrouchTest, ClimbTest, FallTest | movement |
| VehicleGroundTest | bike and vessels against the ground |
| WindTest, LooseTest, SoundTest, FogLayerTest, HorizonTest, DarkTest | weather and look |
| ForecastTest | warnings |
| CoreScanTest, PlaceScanTest | scanners |
| PlaceTest | places, doors at :1262-1287 |
| HulkTest | hulks |
| InsideTest, IncidentalTest, ScavTest, StartleTest, CrowdTest, StrideTest, WoodsTest, BonesTest, WildlifeTest | life and people |
| SkyshipTest | NPC ship |
| EscapeMenuTest | menu, SAVE/LOAD inert |
| SeatedTest, PilotCamTest, ShakeTest | camera |
| PopTest, PopRoute, Pacing, PlacePerf, MarkerSweep | perf and pop-in; the last three are windowed only |
| OwnSweep | a print-only sweep, not run by the gate |

All C, from the headers and the `new Game.Game` lines.

### Starter-free models (no Game)
| Tests | System | Sure |
|---|---|---|
| ArmTest, TownWalkTest, FurnitureTest, OccupantTest, LayersTest, DressTest, DressSweep | sims and generators on built worlds | L |
| RegionTest, DustStormTest | weather model alone | C |
| VortexTest | weather, on an arid start (it does build a Game) | C |
| ChartTest, CacheTest, GlobeTest, SeatTest, ThrusterSoundTest, MusicTest, HeldMergeTest | pure logic | C |

### Libraries
| Library | What its tests cover |
|---|---|
| planet-generator (GenTest.*, incl. Barren, Sizes) | generator over random specs |
| planet-surface (SurfaceTest.*, FloraTest, SeaTest, SkyTest, PaletteTest) | `Main` scene on one planet |
| poi-generator (PoiTest.*) | generator only |
| survival-vitals (VitalsTest) | its own scene |
| wildlife (BestiaryTest etc.) | pure sims |
| weather (WeatherTest) | model and scene |
| building, physics-sandbox, ship-handling, terrain-digging, deco, manufacturing | their own sandboxes |
| **saves** (`prototypes/saves/tests/CoreTests.cs`, SaveTest, ZoneTests) | real save/reload and the zone manager, but in the saves prototype, not the slice Game |
| **coop** (`prototypes/coop/tests/NetTest.cs`, TestAgent.cs) | multi-process host/client with "change zone, leave and rejoin" commands, in the coop prototype only |

`shared/Testing` holds only AllocMeter, Backstop and CoplanarScene. There's no shared check harness (see 3).

---

## 2. Audit findings and the test that would catch them

### Off-world audit (docs/offworld-audit.md)
| # | Catching test | Sure |
|---|---|---|
| 1 Respawn wrong world | **none**. AftermathTest:267-271 checks a respawn on the starter only. | C |
| 2a Suit reads the starter on an air world | **none**. FoldTest:325, RoundTripTest:536 and MoonTest:578 check only `Suit.Beyond`/vacuum. On an air world `Beyond == null` is the expected value, so the bug passes. | C |
| 2b Starter storm hurts off-world | **none, and masked.** Every travel test calms and clears the starter's weather first: MoonTest:101-102, WarpTest:124-125, FoldTest:149, RoundTripTest:276, OrbitTest:182. | C |
| 3 No places off-world | **none**. MoonTest:654 prints `Planet.Pois.Count` in the message but checks no place stands. | C |
| 4 Doors off-world | **none**. BlockDoors is checked on the starter only (PlaceTest:1262-1287, ClimateTest.Rooms:133-142, ForceFieldTest:235-246). OrbitTest:448ff forces a derelict door through the *orbit* "force the door" path, the second door system. | C |
| 5 Bikes off-world | **none**. VehicleGroundTest is on the starter. | C |
| 6 Wildlife not wired | **none, and masked.** The starter's wildlife is frozen (MoonTest:103, WarpTest:126). FoldTest:327-330 only counts creatures. | C |
| 7 State at another star lost | **partial**: FoldTest:232-233 and 255-258 cover a *container* only. Digs, felled trees, deposits, corpses and place memory: none. | C |
| 8 Devices never return; wrecks duplicate | **none**. FoldTest leaves only a crate. | C |
| 9 Weather/wind follow the starter | **none**. WarpTest `Leftovers` :580-585 and RoundTripTest:529 ("nothing plays from a zone you left") might catch the starter's WindHowl if it plays. | U |
| 10 Streaming and settings only on the starter | **none**. WarpTest:211 checks the shape count level only. | C |
| 11 Map shows the starter | **none**. ChartTest is pure, one map. | C |
| 12 Dust, chips, gibs in the starter | **none** | C |
| 13 Hulls keep their first zone | **weak partial**: Moon/Warp/Fold land a starter-built Skiff elsewhere, which fails only if the landing itself fails. | L |
| 14 Death bag size off-world | **none** | C |
| 15 Ledger zone names | **none**. FoldTest:232 builds on `ZoneLedger.StarZoneOf`, so it follows whatever scheme is there. | C |
| 16 Bunker marker / home checks | **partial**: MoonTest:646-654 fails if a "bunker" marker shows on the moon (visor side only). | C |
| 17 Music reads the starter | **none**. MusicTest is starter and pure. | C |
| 18 Lights near places | **none** | C |
| 19 Replicator markers | **none** (no test mentions replicators) | C |
| 20 Cave/bunker rules | **none** | C |
| 21 Deposit discovery IDs | **none** | C |
| 22 One "moon" discovery for every body | **none** (discoveries only in PlayerTest, ChartTest) | L |
| 23 Wreck charting by bare ID | **none** | L |
| 24 Species IDs | **none** | L |
| 25 Compass in orbit | **none** (compass only in BuildTest, starter) | L |
| 26 Two salvage systems | **none, and both locked in**: OrbitTest salvages by the orbit hands, SleeperToolTest by the tool. Nothing compares them. | C |
| Wider 6: unwire on leave | **coarse**: levels in WarpTest:203-213, RoundTripTest:707 and FoldTest `Level` (nodes, objects, bodies, memory with 15 % + 40 MB slack) catch big leaks only. | C |

### Structure audit (docs/structure-audit.md)
**Wrong in play today**

| Finding | Catching test | Sure |
|---|---|---|
| C-A1 StartWorld static | **none, and worked around**: LayersTest.Load:26-27 and LayersTest.Strikes:63-64, 186-187 set `PoiSiting.StartWorld = false` by hand. DressSweep:177 sets it true. | C |
| C-A2 Forced roll on every world | **none** (MatrixTest checks the start only) | L |
| C-B1 Vessel weather leak | **none** | L |
| C-B2 Shots and frame shifts | **none**. CombatTest.World:65 only compensates for starter shifts. | C |
| B-B1 Three blasts | **none** (HazardTest and WeaponTest each test their own) | L |
| B-B5 Two people sims | **none** | L |
| B-B8 Coreless hulk taken whole | **the test asserts the bug**: SleeperToolTest:286-297 ("no core" → back in the bag whole) | C |
| B-B14 Shelter nine ways | **none** (no hole or POI-doorway case) | L |
| B-B25 Tornado felling bypass | **passes with the bug**: ClimateTest:466 checks only that the tree is in `Planet.Felled`. | C |
| B-B26 Overflow lost or mislabelled | **none** | L |
| A-5.1 Loot seeded by cell | **none** | L |
| A-4.4 Creature ID across a patch move | **none** | L |
| B-B10 Wreck-site progress resets | **none** | L |
| B-B16 Gravity constants | **partial**: GravityTest:21-27 covers the sleeper, bag and Skiff, but not the bike, creature hops, spit, the tornado throw or particles. | C |
| B-B3 POI vs structure damage | **none** comparing them | L |
| C-D3 Guards two ways | **none** found | U |
| C-D2 Storm harm by label | **none** | L |
| B-B2 Two health units | **none** | L |
| B-B24 Three marker lists | **partial**: MoonTest:646-654 (visor only) | C |
| C-C1/C2 Creative copies | **none** | L |

**The rest**

| Group | Catching test | Sure |
|---|---|---|
| Co-op (A-R1, A-R2, A-2.7, A-R3, A-6.3) | **none**: the slice can't make a second sleeper (see 3) | C |
| A-6.4 (several clocks) | **partial**: MoonTest:584 and RoundTripTest:539 check that every sky in the home system tells one day | C |
| Saves (A-R4, A-R5) | **none** for PoiSite IDs, place memory or seeds. ZoneTest reloads the ledger only. | C |
| A-6.1/6.2 (galaxy from the start world) | **partial**: FoldTest:533 checks that other stars rebuild the same from their seed, and GalaxyTest gives "same seed, same galaxy". Neither compares the home system with what the galaxy would make. | C |
| Content growth (C-D1, D4, D5, G1, G2, G3, H1) | **none** | L |

### Uncaught, grouped by system
| System | Uncaught findings |
|---|---|
| **Player systems off-world** | #1, #2a, #2b, #5, #9, #10, #11, #12, #14, #17, #25, C-B2, B-B16 (bike) |
| **World-owned content off-world** | #3, #4, #6, #18, #20, C-A1, C-A2, C-G1 |
| **Persistence and identity** | #7 (except containers), #8, #15, #19, #21, #22, #23, #24, A-R4, A-R5, A-4.4, A-5.1, B-B10 |
| **Co-op** | all of S1, S2 and S7 |
| **Unification bugs** | B-B1, B-B2, B-B3, B-B5, B-B14, B-B26, #26/B-B8 (asserted), B-B25 (asserted), C-D2, C-D3, C-C1/C2 |

---

## 3. Harness gaps

**Start on any body or another star: not possible.**
- `Game.Begin` always makes one zone, "Surface", from `World.Spec()` (src/Game/Game.cs:237). C
- `WorldChoice` holds only seed, archetype, variant, width and force (src/Game/WorldChoice.cs:17), with no star or body. C
- The sleeper spawns at the starter bunker (src/Game/Game.Start.cs:63). C
- `--type barren` (WorldChoice.cs:78-82) and `--size` give an airless or small *starter*. That is still `Game.Surface`, so every "home" special case takes its home path and the audit's off-world bugs can't show. C
- Other bodies are reached only by flying (Pilot.cs:53-238).
  - MoonTest has a 26-minute watchdog (MoonTest.cs:44).
  - Each test hand-rolls its own "skip the cruise" teleport: MoonTest.cs:713-718, Staging.Moon.cs:226. C

**Two players: not possible.**
- `Game.Survivor` is one property with a private setter (src/Game/Game.cs:70), and no test makes a second one. C
- Only `prototypes/coop/tests/TestAgent.cs` runs host plus client, and it does so outside the slice. C

**Save and reload: not possible in the slice.**
- The escape menu draws "NO SAVES YET" (src/Ui/EscapeMenu.cs:317), and EscapeMenuTest:317 asserts that SAVE and LOAD do nothing. C
- The nearest things are ZoneTest (a ledger reload under fault injection) and FoldTest's ledger restore of a crate at another star. C
- Full save tests exist only in `prototypes/saves/tests`. C

**Move between worlds and check that systems follow: presentation only.**
- Three hand-written copies check that nothing of a zone you left still shows:
  - WarpTest `Leftovers` :557-599
  - RoundTripTest `Settle` :458-560
  - FoldTest `OnlySky`/`OnlyHere` :696-703
- Together they cover sky, lights, sounds, globes, gravity, the camera and suit `Beyond`. C
- None asks a behavioural system which world it acts in: suit climate source, wildlife target and events, weather observer, places stack, doors, bike, debris, map, music, discoveries. C

**No shared test base.**
- `Check`/`Fail` and the pass/fail tally are copied into 81 files in slice/tests, and Driver.cs:18 only drives input. C
- The library tests each have their own as well: deco/tests/TestBase.cs, manufacturing/tests/TestBase.cs, saves/tests/TestLog.cs. C

**Where the ability should go (one feature, not per-test hacks):**
1. **Game side (it falls out of S6/R6):**
   - `WorldChoice` carries a start world key (star, body), and `Game.Begin` (Game.cs:237) makes the start zone from it.
   - The galaxy comes from a game seed, and the starter is just the body picked in it.
   - Tests need nothing more than `--on=moon|airless|world|abroad` for a random body of that kind (never a pinned one).
2. **Test side: one `Harness` beside Driver.cs**, owning the tally and a few calls:
   - `Arrive(body)`: through the real crossing or `Handover`, not a node teleport.
   - `AddSleeper()`: once S1 gives a Sleeper list.
   - `SaveAndReload()`: through the saves core the ledger already uses.
   - `WorldProbe(sleeper)`: after every arrival, asserts that each player-owned system reads that sleeper's world and each world-owned system belongs to it. The audit's ownership list becomes a checklist.
3. **test-all:**
   - Read `--on` from a second `.args` variant per scene, so a share of each suite runs off the starter on every gate.
   - Fix the existing hole: ClimateTest.args is only `--fixed-fps 60`, so `--airless` (ClimateTest.cs:74) never runs.

---

## 4. Weak checks

**Always pass**
- WarpTest:681 `Check(true, "... had no zone of its own ...")` adds a pass for nothing. C
- PopTest:116 and PopTest.Reach:114 log a skip as a pass. C
- BuildTest:190 and :325, and GalaxyTest:171, are `Check(true, …)` summary lines. Their inner failures are probably counted before them. L

**Silent skip: the branch is left with a print and no failure.** No test fails when every branch skipped; none counts that anything ran. C
| File | Lines |
|---|---|
| AftermathTest | 126 |
| BonesTest | 189, 216 |
| ClimbTest | 121 |
| CombatTest | 228, 251, 312 |
| CrowdTest | 175, 191, 343, 367 |
| FallTest | 143, 464, 511, 518, 607 |
| ClimateTest | 275, 300, 448, 488 |
| FogLayerTest | 121 |
| MoonTest | 703-706 (the second moon) |
| PlaceTest | 824 |
| ScavTest | 151 |
| SliceTest | 437 |
| StartleTest | 142, 358 |
| StrideTest | 425 |
| VehicleGroundTest | 482 |
| WarpTest | 349 |

**Vacuous ORs:** IncidentalTest:77 (`_withTown == 0 ||`), :79 (`|| worlds.Count == 0`), :141 (`|| air.Count == 0`) and :208 (`|| made == 0`). C

**Masking setup:** every travel test freezes wildlife and calms the starter's weather before leaving (lines in 2). These are the two systems the audit says follow you wrongly. C

**Counts instead of behaviour**
- FoldTest:327-330: number of creatures on the foreign world.
- WildlifeTest:122 and :146: kinds and animals in view.
- MoonTest:654: places only printed.
- WarpTest:203-213 and RoundTripTest:707: node and object levels.
- `Leftovers`/`Settle`: whether a zone's nodes show, not whether systems act in the right world.

All C.

**Loose tolerances**
- MoonTest:555: the crossing lands within 80 m of the globe point.
- MoonTest:606: a stride on the moon may slide 2× the world's, or 1 m.
- RoundTripTest:708: p99 up to 1.3× plus 3 ms.
- WarpTest:203: nodes +2 % or 40, objects +2 % or 200.
- WarpTest:207: managed memory +15 % plus 40 MB.

All C.

**Tests that assert the bug**
- SleeperToolTest:286-297 (B-B8).
- ClimateTest:466 (B-B25 outcome).
- OrbitTest plus SleeperToolTest together lock in the two salvage paths (#26).

All C.

**Gate gap:** ClimateTest.Airless runs only with `--airless`. ClimateTest.args doesn't pass it, and tools/test-all.ps1:47-50 reads only that file. C

**Pinned RNGs (owner rule: never pin a seed)**
- `new Random(11)` ChartTest:64; `Random(7)` DamageTest:306 and GlobeChecks:39; `Random(11)`/`Random(3)` ThrusterSoundTest:284 and :293.
- boarding/tests/SoakTest.cs:34 seeds `7000 + run`.
- PlaceTest:1363 `FindTown` walks seeds 1..60 to pick one to pin.

All C. These are test-input RNGs, not worlds, except the last two.
