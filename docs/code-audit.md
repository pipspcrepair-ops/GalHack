# Code audit: bloat, duplicates and dead ends

Read-only audit of `proto/batch2` (branch `proto/audit`), 13 Nov 2026. No code was changed. Line numbers are as of this branch; other agents are editing, so re-check a reference before acting on it.

## What the repo is, in numbers

- 1,211 C# files, about 329,000 lines. About 80,000 of them are tests (`slice/tests` alone is 37,000 in 213 files).
- Comments are not the problem: 11% of lines, and a scan for doc comments that only repeat the member name found one.
- The slice (`prototypes/slice/Slice.csproj`) compiles its own 79,000 lines plus, by link, nearly all of `shared/*` (not `shared/Sky`, `Music/Render`, `Items/Deposits`), and pieces of 12 prototypes. **Prototypes the slice does not link at all:** boarding, coop, deco, encounters, invasions, manufacturing, walkable-ship, wildlife, plus `shared/Sky`. Prototypes it links in part: building, capital-base, diegetic-ui, physics-sandbox, planet-generator, planet-surface, poi-generator, saves, ship-handling, survival-vitals, terrain-digging, weather. The part it does not link is that prototype's own demo app (`Main`, `Player`, `Hud`, `Fx`, `World`, ...), roughly 108,000 lines including tests.
- `tools/test-all.ps1` walks every prototype's `tests/*Test.tscn`, so every dormant prototype also costs gate time.
- Last commit per prototype: boarding, coop, encounters, saves, walkable-ship 4-5 Nov; invasions, terrain-digging 7 Nov; everything else 8-9 Nov. The first group has been idle for a week while the slice moved on.

How findings are sized: **Lines** is what a tightening pass could remove net. **Risk** is low (compiler finds every break), medium (behaviour or seeded output could shift; run the gate), high (visible or save-affecting). **Area** is the owner list from the brief; "none" means no current area touches it, so it can go any time.

## Ranked findings

| # | Finding | Lines | Risk | Area |
|---|---|---|---|---|
| 1 | Dormant prototypes nothing builds on | ~34,500 | low | none |
| 2 | Second-tier demo apps (the unlinked half of linked prototypes) | ~25,000 | low-medium | none (some grid, places, sound) |
| 3 | Test scaffolding copied between test files | ~3,000 | low | each area's tests |
| 4 | Three `Int3` types and 43 alias headers | ~250 net, 500 touched | medium | grid, places, world building |
| 5 | Two collision-layer tables whose bits disagree | ~30, but a correctness hazard | medium | grid |
| 6 | Hover, gear and contact rules live twice, and a dead flight engine | ~1,300 | low-medium | grid |
| 7 | Splitmix, RNG, hash and noise helpers copied ten-plus times | ~600 | medium (seeded output) | spawns, places, trees and flora, world building |
| 8 | Two flora systems, forked at every call | ~900 | medium-high (visual) | trees and flora |
| 9 | Eleven hand-rolled sample-to-WAV encoders | ~350 | low | sound |
| 10 | Wreck spawning copied three times; four `StructureView`s | ~250 | medium | grid |
| 11 | Three weather renderers | ~1,500 | low-medium | none for the old two; the slice's is spaceflight and world building |
| 12 | Three block-grid models, three mesh buffers | design call | high | grid, places |
| 13 | Tile-streaming bookkeeping written three times | ~300 | medium | world building, places |
| 14 | Three nav-grid A*s, one unused | ~120 | low | grid, places, combat |
| 15 | Binary and stray weight in git | ~190 MB of PNG | none to code | none |
| 16 | Small dead members | ~300 | low | various |
| 17 | Very long units | n/a | medium | various |

Total estimated safely removable: **about 63,000 lines** (see the end), of which only about 8,000 sit in code the slice actually compiles.

---

## 1. Dormant prototypes nothing builds on (~34,500 lines)

No `.csproj` links into these, no other code references their types, and the slice has moved past them. They are dead ends rather than duplicates, but they are also the source of many duplicates below.

| Prototype | Lines (src + tests) | Last commit | Note |
|---|---|---|---|
| `invasions` | 10,661 | 7 Nov | Its own `Block`, `BlockKind`, `Cell`, `Palette`, `Hud`, `GameData`, `Json`. Superseded by the slice's invasion plan once built. |
| `coop` | 6,811 | 5 Nov | Steam test, `Player`/`Ship`/`ShipLayout` are copies of walkable-ship's. `steam_appid.txt`, `tools/steam-coop-test.ps1`. |
| `boarding` | 6,774 | 5 Nov | Own `Block`, `Palette`, `Inventory`, `Tunables`, `PlayerHealth`, `WeaponDef`. |
| `encounters` | 5,823 | 5 Nov | Own `Rng`, `MathX`, `Json`, `GameData`. |
| `walkable-ship` | 1,649 | 4 Nov | Original of the triplicated `Player`/`Ship`/`ShipLayout` below. Also the default target of `tools/test.ps1:6`. |
| `saves/src/Game`, `src/Records`, `src/Zones` | ~3,760 | 5 Nov | The `Game` folder is a third copy of the walkable-ship game (87 identical 8-line blocks with coop `Player.cs`, 77 with walkable-ship `ShipLayout.cs`). Only `saves/src/Persistence` is linked by the slice. `capital-base/src/Records/SaveGame.cs` has its own `TerrainData` beside `saves/src/Records/Payloads.cs`. |

Triplication evidence: `ShipLayout.Build` (83 lines) is byte-identical in `coop/src/Game/ShipLayout.cs:71`, `saves/src/Game/ShipLayout.cs:67` and `walkable-ship/src/ShipLayout.cs:67`; `Player.Walk` (41 lines) in coop `Player.cs:169` and walkable-ship `Player.cs:129`; `Ship.BuildVisuals` and `FlyPiloted` likewise.

**Before deleting:** some tests here guard shared modules and should move, not die: `manufacturing/tests/ItemsTest.cs` and `ProductionTest.cs` test `shared/Items` (`ItemsTest.cs:53` is the only caller of `Validation.Validate`, 95 lines in `shared/Items/Core/Validation.cs`); `wildlife/tests` (8,093 lines) test `shared/Bestiary`; `deco/tests` test `shared/Deco`. Manufacturing (3,866) and wildlife and deco are therefore **not** counted in the 34,500; they are in finding 2. Suggest tagging the repo (`archive/prototypes-2026-11`) then removing, so the history stays one command away.

## 2. Second-tier demo apps (~25,000 lines)

Standalone windows that duplicate what the slice does better. They also act as screenshot and test hosts, so removing one means deciding where its tests live. Lines are what the slice does not compile:

- `wildlife/src` 3,359 (`Showroom` 755 is a creature viewer; the creatures are `shared/Bestiary`); tests stay.
- `capital-base` 4,050 (`World` 2,062 incl. its own `Ground`/`GroundField`/`Debris`/`Sites`/`Pilot`; `Main`, `Hud`, `Scenery`, `Records`).
- `physics-sandbox` 5,211 (`World` 2,484, tests 2,376). Its `Ship.cs`/`StructureView.cs` are the prototype of the slice's `Vessel`/`StructureMesh`.
- `survival-vitals` app 1,800 (`Main`, `Props`, `VisorHud`, `Menus`, `Terrain`, `Player`, `Palette`; the `Model` is linked).
- `terrain-digging` app 1,700 (`TerrainView`, `GatherTool`, `Debris`, `Player`, `Scenery`, `Sfx`, `StuckGuard`, `Palette`, `Shaders`).
- `weather` app 2,700 (`World`, `Fx`, `Hud`, `Main`, `WeatherDirector`).
- `diegetic-ui` `World`/`Pages` 2,742 (the `Theme` and `Screens` are linked). `Pages/ConsolePage.cs` and `ConstructorPage.cs` have same-named twins in `slice/src/Ui/Screens.cs` and `manufacturing`.
- `building/src/Game` 1,921 (`BuildController` 900 vs the slice's `BuildTool` 882; see 12).
- `ship-handling` `Presentation` + `Physics` 2,255 (see 6).
- `planet-generator` `Surface` + `Orbit` + `Main` 1,654 (`ChunkStreamer`/`ChunkBuilder` are the older terrain streamer that `planet-surface/Terrain` replaced).
- `poi-generator/src/View` 2,365 (`PoiView*`, `Player`, `Terrain`, `SkyRig`: a second viewer; the slice links only `View/MeshBuild.cs`).
- `planet-surface` `Player`, `Main`, `WeatherView/WeatherRig`, `Hud` 1,766.
- `manufacturing` 3,866 (screens and panels; shared `Items` stays).

Not all of this should go: the poi-generator and planet-generator viewers are how screenshots for the gallery are made. Recommended order is to cut the demos with a slice equivalent first (physics-sandbox, capital-base, building/Game, weather app, terrain-digging app, ship-handling Physics), keep the two generator viewers until the slice has its own place gallery.

## 3. Test scaffolding copied between files (~3,000 lines)

- `Screenshot.cs` exists in 21 prototypes, 5,702 lines in all (planet-surface 828, poi-generator 775, wildlife 617, terrain-digging 405...). The `_Ready` (10 lines) and `Capture` (7 lines) are identical in five to six of them (e.g. `building`/`encounters`/`survival-vitals`/`walkable-ship`/`weather` `Screenshot.cs:_Ready`; `Capture` in building, physics-sandbox, planet-generator, ship-handling, terrain-digging). A `GalHack.Testing.ShotRun` base in `shared/Testing` (which already holds `Backstop`) would take the shared part, leaving each file only its scene list. Save about 1,500.
- `slice/tests`: `_PhysicsProcess` (13 lines) is word-for-word the same in 21 tests (`ClimateTest.cs:47`, `CrouchTest.cs:49`, `DamageTest.cs:53`, `DarkTest.cs:47`, `GunFeelTest.cs:41`, `BuildTest.cs:68`, `ClimbTest.cs:58`, `CoreTest.cs:45`, `HorizonTest.cs:59`, `HulkTest.cs:49`, ...); `_Ready` (11 lines) in 10 more. The same `Check`/`Until`/`Tally`/`Click` helpers are re-declared in `coop/tests/UnitTests.cs:267`, `planet-generator/tests/GenTest.cs:653`, `walkable-ship/tests/WorldProbe.cs:147`, `weather/tests/WeatherTest.cs:863`, `wildlife/tests/CreaturesTest.cs:1869`, `ShowroomProbe.cs:84`, `planet-surface/tests/SteepGroundTest.cs:107` / `SurfaceTest.cs:147`, `slice/tests/Staging.Escape.cs:27,67`. One `SliceTestBase` plus `TestKit.Check/Until` saves about 800.
- `SkySeamTest.cs` is copied into 6 prototypes (building, encounters, physics-sandbox, planet-generator, weather, wildlife; 143 lines) all over `shared/Sky/SkySeams.cs`; one shared scene would do. `deco/tests/TestBase.cs` and `manufacturing/tests/TestBase.cs` are the same file.
- Several single test files are very long: `slice/tests/PlaceTest.cs` 1,479 (`RealPlayer` is a 340-line method, `Settlement` 320), `poi-generator/tests/PoiTest.cs` 2,212, `physics-sandbox/tests/PhysicsTest.cs` 2,071, `wildlife/tests/CreaturesTest.cs` 1,887. Splitting is optional; the gain is reading, not lines.

Risk: low; the gate runs every test, so a break shows at once. Fix per area as its tests change, since other agents have these files open.

## 4. Three `Int3` types and 43 alias headers (~250 net)

Same idea (integer cell), three structs, compiled into the same assembly under different namespaces:

- `GalHack.Structures.Int3`: `building/src/Structures/Int3.cs` (111 lines, class struct, axis indexer, `Dir` helpers).
- `PoiGenerator.Gen.Int3`: `poi-generator/src/Gen/Core.cs:11` (record struct, plus its own `Dir`/`Dirs` at `Core.cs:27`, duplicating `building/.../Int3.cs`'s `Dir`/`Dirs`).
- `TerrainDigging.Terrain.Int3`: `terrain-digging/src/Terrain/Int3.cs` (84 lines, comparable, `Floor`, `Center`).

The cost is visible in the code: 29 source files and 14 test files open with `using PInt3 = ...`, `TInt3 = ...`, `SInt3 = ...` or `Int3 = ...`, and 106 `new PInt3/TInt3/SInt3` conversions at the seams (`slice/src/Places/*`, `Surface/Light/SkyLight.cs:10-11`, `Vessels/Vessel.Hulk.cs:14`, `planet-surface/Poi/PoiSiting.cs:11-12`, `World/Infra/CityPlan.cs:10`, `Start/Bunker.cs:14`). Keep **`GalHack.Structures.Int3`** (the one the build tool and vessels use), move it to `shared/Geometry`, give it the union of members, and delete the other two plus the aliases. This also removes the three-way `Dir` split. It is the single most confusing thing in the place/vessel seam. Risk medium: wide mechanical edit, hashes (`GetHashCode` constants differ per copy; check no output is keyed off them), and every area is touching these files now, so do it in one pass right after the grid and places merges.

## 5. Two collision-layer tables whose bits disagree (hazard)

`shared/Physics/Core/Layers.cs:17-35` is documented as "the one collision-layer table" (World 0, Hull 1, Interior 2, Water 3, Player 4, Props 5). `planet-surface/src/World/Layers.cs:6-17` is a second table (Terrain 0, Structure 1, **Player 2, Props 3**, Poi 4, **Flora 5**) and the slice compiles both. The bits mean different things: planet-surface's Player (bit 2) is the shared Interior; its Props (bit 3) is the shared Water; its Flora (bit 5) **is** the shared Props bit. Three slice files OR them together: `slice/src/Arms/ArmsHost.cs:79`, `Player/SurvivalTool.cs:553`, `Vehicles/HoverBike.cs:586` (`Layers.World | Layers.Hull | Layers.Props | PlanetSurface.World.Layers.Flora`). It works by accident because Flora and Props share a bit, and the shared header's claim "no bit means two things" is false in the build. Fix: add `Flora` to the shared table as its own bit (or fold flora into World/Props) and delete `planet-surface/.../Layers.cs`; the `TerrainStreamer` sets the layer on its bodies, so check every `CollisionLayer =` in `planet-surface/src/Terrain`. About ten more prototypes carry their own `Layers` class and go with finding 1 and 2. Lines small, risk medium, area grid.

## 6. Hover, gear and contact rules twice, and a dead flight engine (~1,300 lines)

- `ship-handling/src/Handling/Tunables.cs` (150 lines) defines `HoverData`, `GearData`, `ContactData` (lines 77, 95, 106); `shared/Physics/Core/PhysicsRules.cs` (245) defines the same three classes (38, 111, 139). Two data files carry the numbers: `prototypes/ship-handling/data/handling.json` and `shared/Physics/physics.json`. The slice reads both (`GameData.cs:73` for physics; the handling JSON through `Tunables`). One source of truth is the point of the shared module; move the handling-only numbers across.
- `ship-handling/src/Physics/` (851 lines: `Vessel`, `HoverController`, `LandingGear`, `Events`, `Layers`, `FlightPhysics`) is the pre-shared engine. Its `HoverController` (213), `LandingGear` (119), `Events` and `Layers` have same-named successors in `shared/Physics/Core` (294, 186, 66, 66). The slice does not link it. Remove with the ship-handling `Presentation` folder (1,404) and keep `Handling/` (linked) and its `HandlingTest`, repointed.
- `MathX` exists in `shared/Physics/Core/MathX.cs` and `encounters/src/Director/MathX.cs` (the latter goes with encounters).
- `StuckGuard` is three copies: `shared/Physics/Core/StuckGuard.cs` (233), `planet-surface/src/Player/StuckGuard.cs` (85), `terrain-digging/src/StuckGuard.cs` (138); `Walker`/`WalkController` and `SetCrouch`/`CanStand` repeat the same way (`planet-surface/Player/Walker.cs:227,236` vs `terrain-digging/src/Player.cs:122,131`). Only the shared one is on the slice path.

Risk low-medium (the slice does not use the old copies; the tests do). Area grid (shared/Physics).

## 7. Splitmix, RNG, hash and noise helpers (~600 lines)

The SplitMix64 finaliser (`0xBF58476D1CE4E5B9` / `0x94D049BB133111EB`) is hand-written in 19 files. Generators, all "SplitMix64 with a golden-ratio step", each with its own API:

| Where | Line | Notes |
|---|---|---|
| `poi-generator/src/Gen/Core.cs:103` (`Hash.Mix`), `:158` (`Rng`) | 158 | `Range`, `Between`, `Pick`, `PickWeighted`, `Shuffle`, `WeightedOrder`. The richest. Seeds raw. |
| `shared/Bestiary/Core/Rng.cs:12` | 95 | `Float`, `Range`, `Int`, `For(seed, purpose)`. Seeds `seed ^ golden`, so streams differ from the POI one. |
| `planet-generator/src/Gen/Rng.cs:7,32` | 45 | struct, `NextFloat`, `Next(count)`, `Hash.Mix`/`Hash.Of`. |
| `shared/Deco/Core/DecoMath.cs:12,75` | | `DecoRng`, `Mix`. |
| `shared/Flora/Core/PlantTypes.cs:32` | | `Mix`, `Dice`. |
| `shared/Items/Deposits/DepositBody.cs:7,9` | | static `Rng`, `Mix`. |
| `shared/Galaxy/Core/Galaxy.cs:448,461` | | `Hash`, `GalaxyRng`. |
| `shared/Terrain/CaveMouths.cs:165` | | private `struct Rng`. |
| `shared/Music/Core/Dsp.cs:7` | | `DroneRng`. |
| `weather/src/Model/Rng.cs:10` (54), `encounters/.../Rng.cs` (78), `invasions/.../DeterministicRng.cs` (74) | | `NextULong` bodies are the same 6 lines in all three. |

Also `Fract` x5 (`WeatherSim.cs:795`, `Front.cs:173`, `TornadoFx.cs:128`, `LightSettings.cs:97`...), `SmoothStep` x7 (`terrain-digging/Terrain/Noise.cs:45`, `weather/World/Terrain.cs:205`, `Hazards.cs:205`, `ForceModule.cs:261`, `GroundPalette.cs:493`, `encounters/MathX.cs:37`...), `Lerp` x5, string `Hash` x4 (`GunVoice.cs:204`, `GunLook.cs:322`, `Bestiary/Rng.cs:85`, `DeterministicRng.cs:64`), and five different value-noise implementations (`planet-generator/Gen/Noise.cs`, `terrain-digging/Terrain/Noise.cs`, `Galaxy.cs:518,532`, `wildlife/SpaceScene.cs:156`, `weather/Terrain.cs:196`).

Keep: one `shared/Geometry/Rng.cs` (or `shared/Core`) with the POI `Rng` API and a `Hash` class, as the **keeper**; the Bestiary `Rng.For` becomes an extension. Warning: seeds are part of the design (a seed gives a world), so every kept copy must stay bit-identical to the one it replaces: only dedupe where the constants and seeding match, and leave the Bestiary `seed ^ golden` stream as a named variant. Tests roll fresh worlds, so a changed stream passes tests and silently changes worlds: run a before/after hash of one planet, one town and one creature set. Risk medium. Areas: spawns (Bestiary), places (poi), trees and flora (Flora), world building (planet-generator/surface).

## 8. Two flora systems, forked at every call (~900 lines)

The planet carries both the older low-poly `Flora` kinds (`planet-generator/src/Look/BiomeLook.cs`, meshes in `planet-generator/src/Surface/FloraMeshes.cs`, 258 lines, built by `TerrainStreamer.cs:224` for every kind) and the newer `Plant` species (`shared/Flora`: `Herbarium` 535, `PlantForms` 937, `Sculptor` 483). `planet-surface/Terrain/FarBuilder.cs` keeps both live: `plant != null ? ... : ...` at lines 836-864 (scale, tint, variant, distant), `TerrainStreamer.Pop.cs:35` (`PlantMesh(...) ?? _floraMeshes[b.Kind]`), `TerrainStreamer.cs:1068`. Still used for what has no `Plant`: crystals, mushrooms, ore, fallen logs, `RuinGrowth.cs:43` for tree tests. `FloraMeshes.IsLarge/IsTree/MinUp/BaseScale/Reach` is the kind table that `Plant` already has as data (`Plant.Large`, `CrownRadius`, `Mineral`). The finish is one `Plant` list where every kind (including crystal, mushroom, ore) is a species; then `FloraMeshes`, the `Flora` enum, `FloraRule` and the fork branches go. `TerrainStreamer.KindMesh` (`TerrainStreamer.cs:299`) is already unreferenced. The old `planet-generator/Surface/ChunkStreamer.cs` + `ChunkBuilder.cs` (614) are a third copy of the scatter pass, in the unlinked viewer. Risk medium-high (forest looks and densities change at the seam), area trees and flora, with the `FarBuilder` part also world building.

## 9. Eleven hand-rolled sample-to-WAV encoders (~350 lines)

Each does float samples to 16-bit PCM to `AudioStreamWav { Format = Format16Bits, MixRate = ..., Data = ... }` with its own clamp, rate constant and noise source: `building/src/Game/Sfx.cs:145`, `diegetic-ui/src/World/Sfx.cs:52`, `planet-surface/src/Fx/Sfx.cs:99`, `terrain-digging/src/Sfx.cs:77` (`Sfx.cs` in planet-surface and terrain-digging share 33 8-line blocks, and `White` at `Sfx.cs:180`/`:140`), `slice/src/Arms/GunSounds.cs:85`, `slice/src/Game/Game.Wrongness.cs:83,104`, `slice/src/Game/SliceAudio.cs:305,320,340`, `slice/src/Surface/WindHowl.cs:133,160`, `shared/Bestiary/Audio/ProceduralAudio.cs:188,225,247`. Also `Noise` classes inside the two Sfx files, `Clock(double)` x5 (`Alerts.cs:101`, `Paint.cs:176`, `invasions/Hud.cs:290`, `ScreenKit.cs:174`, `WeatherSim.cs:1276`). The slice compiles `building/Sfx.cs`, `diegetic-ui/World/Sfx.cs`, `planet-surface/Fx/**` and `GunSounds` as separate sound banks: a `SampleWav.From(float[] samples, int rate, bool loop)` in `shared/Music/Core` (engine-free arrays) or a small `shared/Audio` takes the encoder out of all of them. Keep `ProceduralAudio` as the model for creature voices. Area sound; risk low.

## 10. Wreck spawning copied three times; four `StructureView`s (~250 lines)

- `Vessel.SpawnDerelict` (`slice/src/Orbit/Vessel.Orbit.cs:27`), `Vessel.SpawnGroundWreck` (`Vessels/Vessel.Hulk.cs:44`), `Vessel.SpawnWreck` (`Vessels/Vessel.cs:289`) each: compute mass, register a `Body` (only `Kind`, `Motion`, `DragArea`, `Integrity`, node type differ), `new Vessel`, create a `CharacterBody3D`/`StaticBody3D`/`RigidBody3D` with the same name-stripping `Replace(" ", "")...`, `AddChild`, `Bind`, `StructureShapes`, `Furnish`, `Teleport`, `Register`. One `Spawn(zone, s, SpawnKind, at, rot)` with a switch for the three differences is about 60 lines instead of about 110. Area grid (Vessels) and spaceflight (Orbit): do after both merge.
- `StructureView` x4: `building/Presentation/StructureView.cs` 154, `physics-sandbox/World/StructureView.cs` 233, `terrain-digging/src/StructureView.cs` 105, `planet-surface/Player/StructureView.cs` 56; the slice's own is `Vessels/StructureMesh.cs` (205). Only `ChunkMesher` is shared; the other four are prototype shells that go with finding 2.
- `Vessel.DistanceFromHull` (16 lines) and `RegionOf` (13 lines) are identical in `physics-sandbox/src/World/Ship.cs:368,239` and `slice/src/Vessels/Vessel.cs:529,474`.

## 11. Three weather renderers (~1,500 lines)

`weather/src/Fx/` (1,223: `LightningFx`, `TornadoFx`, `DebrisFx`, `WeatherFx`, `WeatherAudio`, `Looks`), `planet-surface/src/WeatherView/` (`LightningFx` 348, `TornadoFx` 292, `WeatherRig` 405, `IWeatherScene`), and `slice/src/Surface/SurfaceWeather.cs` (920). `planet-surface/WeatherView/LightningFx` and `weather/Fx/LightningFx` share 47 8-line blocks; `TornadoFx` 44; `Stroke`, `BuildCrackles`, `Particles`, `ActiveParticles` are identical. `WeatherRig.ArchetypeFor`/`WeatherRig` ctor/`Drive` match `SurfaceWeather.ArchetypeFor` (`:330`)/ctor (`:222`)/`Drive`. The slice links planet-surface's `LightningFx`, `TornadoFx`, `IWeatherScene` and replaces the rig with `SurfaceWeather`. Keep planet-surface `LightningFx`/`TornadoFx`/`SurfaceWeather`. Drop `weather/src/Fx` (1,223) and `planet-surface/WeatherView/WeatherRig.cs` (405) with their demos. `GlobeWeather.cs` (337, orbit) is a separate, third renderer for the map and not a duplicate. `SurfaceWeather.cs` at 920 lines is the file to break up (emitters, sounds, fronts) after the world-building and spaceflight work lands. Risk low (the unlinked copies); area world building.

## 12. Parallel block-grid models and mesh buffers (design call)

Not a quick fix; flagged because it is the largest remaining duplication of **concept**:

- **Block models.** `building/Structures` (`Structure`, `Block`, `BlockKind`, `Catalogue` 558+ lines, `Paint`, `Rooms`, `Damage`) is the player's grid, used by `Vessels`/`Construction`/`Ui`. `poi-generator/Gen/Blocks.cs` (`BlockType`, `Block`, own `Role`, `Dir`) is the generated places' grid; `slice/Places/Place` turns it into walkable rooms with its own `Place.Door`/`Fixtures`. `ship-handling/VesselStructure.HullBlock` is a third, now only a designs source (`VesselDesigns.cs` 238 lines of hand-built ships). `invasions/BaseGrid` and `boarding/Block` are dead ends (finding 1). The one-metre-grid plan (`docs/one-metre-grid.md`) is the owner's route; the audit's advice is that any new cell type be added **there**, and that `HullBlock`/`VesselDesigns` become Structure blueprints (the slice already has `BlueprintLibrary.cs` and 18 `slice/data/blueprints`).
- **Mesh buffers.** `building/Presentation/ChunkMesher.cs` (789, `MeshBuffers`), `poi-generator/Gen/Mesher.cs` (1,778, `MeshBuffer` + `ChunkMesh` + coplanar face merge), `poi-generator/View/MeshBuild.cs` (1,283, buffers to Godot meshes), `shared/Deco/Core/DecoMesh.cs` (`DecoMeshData`/`DecoEmit`). Same job (axis-aligned boxes and faces to vertex arrays, with per-face finish codes), three formats. `shared/Geometry/Coplanar.cs` (245) already shows the way: a common face type. A shared `FaceBuffer` would be the root of any later merge. Also a stray double doc comment on the same enum at `Mesher.cs:9-10`.
- Two `BuildController` (900) vs `BuildTool` (882+264+313) and two `Placement`/`Mirrors` paths: the first is dead with `building/src/Game`.

Risk high, because it changes how every place and vessel draws. Do not attempt in a tightening pass; keep it as the design item it is.

## 13. Tile-streaming bookkeeping written three times (~300 lines)

`planet-surface/src/Infra/InfraLayer.cs` (`TileSize = 256f` at `:26`, `Tile` at `:41`, worker `Task.Run` at `:105,165,279`), `Poi/PoiLayer.Tiers.cs` (`TileSize = 256f` at `:60`, `Tile` at `:43`, `Task.Run` at `:79`) and `TerrainStreamer` all keep a per-tile state, build on workers, hand to the scene one per frame at the planet copy nearest the camera. Also two directories named `Infra` in different namespaces (`PlanetSurface.Infra`, 1,033 lines of drawing, and `PlanetSurface.World.Infra`, 6,500 of plans: `InfraNetwork` 2,147, `PowerLines` 1,055, `CityPlan` 859, `FlavourRuins` 662, `WorldDress` 546); the plan/draw split is sound, the names invite mistakes. A `TileStream<T>` in `planet-surface/Terrain` with the near-set, wrap-aware key and per-frame budget would hold the pattern once. Risk medium (frame budget is 16.7 ms), area world building and places.

## 14. Three nav-grid A*s, one unused (~120 lines)

`building/Structures/NavGrid.cs` (122, with `NavLink`/`NavKind`), `poi-generator/Gen/Nav.cs` (257), `shared/Bestiary/Core/NavGrid.cs` (239). The building one has no caller outside its own `BuildTest.cs:687-706` ("for crew, boarders, invaders to run A* on": speculative for a feature that has not been built); the slice compiles it, never uses it. The POI nav validates places; the Bestiary one steers creatures around rocks and trunks; they solve different problems and need not merge, but the building one can go (or be the POI one when crew exists). Area grid (delete), combat (when a crew feature arrives, reuse `Gen/Nav`).

## 15. Binary and stray weight in git

- **Screenshots:** 318 tracked PNGs under `prototypes/*/screenshots` (poi-generator 73 files / 64 MB, slice 87 / 51 MB, planet-generator 23 MB, wildlife 19 MB, deco 8 MB, invasions 6 MB...), about 190 MB, plus `planet-generator/atlas/*.png` (7 MB), `slice/screenshot.png`, `docs/gallery` (14). Git's pack is 423 MB. Regenerable by the tests, so they could be ignored, but history already holds them; a rewrite is out of scope. Stop growing it: keep only `docs/gallery`.
- **`docs/signs`:** 578 tracked files: `svg/` and `png/` are the same 91 art pieces twice (png is a render), plus `svg-stencil`, `sigils` (87), `restyle` (66), `zones` (42), `attempts` (25 and the nested `sigils/attempts-*`), the build scripts in `docs/signs/tools`. The README says "Nothing here is in the game yet"; the game reads `slice/data/signs` (18 files) and `poi-generator/data/sigils.txt`. Keep the generator scripts and the final sheets; the attempt folders and the per-file PNG renders can be dropped (about 350 files) since the scripts remake them.
- **Repo-root strays (untracked, in the main checkout `G:\GalHack`, from the status snapshot; I did not touch them):** `CapitalWorld.cs`, `Craft.cs`, `Homes.cs`, `Library.cs`, `OccupantTest.Dark.cs`, `TerrainStreamer.cs`, `prototypes/planet-surface/tests/SurfaceTest.City.cs`, `prototypes/poi-generator/src/Gen/MeshStandoff.cs`, `WorldLayers.cs`, `prototypes/slice/tests/Staging.PlaceSurf.cs`. The six root-level `.cs` files have the names of files that live in `prototypes/*`; they look like an agent's copies written to the wrong directory (the ones under `prototypes` are real new work in progress). Not in the worktree, so not in git; the owner should check them before they are added. Also untracked: `docs/research/Book_1_Characters_and_Combat.pdf`, `Book_2_Starships.pdf`, `Book_3_Worlds_and_Adventures.pdf`, `docs/music-samples/`, `docs/font/`, `docs/mockups/`, `asset-notes/`. The PDFs look like third-party books; check the licence and keep them out of git (`CREDITS.md` and the no-external-assets rule).
- Data files that the name-scan did not find in any `.cs`: `poi-generator/data/{architect,utility,city,flavour}-families.txt` (44 KB), six `kits/*.kit` (`clinic`, `miningtown`, `themepark`, `alien_temple`, `colonydome`, `shiphull`), `weather/data/weather.schema.json`. The POI data is loaded by directory scan (`poi-generator/src/View/GameData.cs:27`, and the slice's own loader), so these may be live; **verify by deleting one in a scratch run** before removing. The schema is a documentation file.
- `.tools/` is gitignored and lives inside each worktree. Do not count it: `.tools/tmp/coresky-old/` holds a stale copy of `shared/Galaxy`, which is why a naive duplicate scan lights up on `Galaxy`.

## 16. Small dead members (~300 lines)

Names that appear only at their definition across all `.cs`, `.tscn` and data (so not called by name, reflection or scene). Ordered by size; check each with a build, since a `[Signal]` or string-dispatched method can hide.

- Tests and helpers: `slice/tests/Staging.PlaceSurf.cs:309 SurfClose` (32), `wildlife/tests/CreaturesTest.cs:475 TunnelSection` (31), `:929 ProwlerCase` (18), `slice/tests/Driver.cs:392,401 EnterThroughHatch`, `WalkOutOfHatch`, `poi-generator/tests/PoiTest.cs:753 RoomMix`.
- Slice: `Places/PoiPlaces.cs:398 StandAll` (11), `Places/Place.Deco.cs:553 CentresOf` (9), `Ui/UiText.cs:42 Sentences` (7; companion of `Problem`, check for a caller in tests before removing), `Items/PlayerInventory.cs:155 AddRemote` and `:134 RemoteCount`, `Surface/SurfaceZone.cs:316 KeptDry`, `:175 WaitForPlanet`, `Game/Game.cs:788 WeatherWarnings`, `Arms/GunModel.cs:111 SetGlow`, `Orbit/GlobeMap.cs:45,118,121 LatitudeDegrees, VelocityToOrbit, VelocityToSurface`, `Orbit/OrbitZone.cs:125 WaitUntilPrepared`, `Items/Progress.cs:247 HasFound`, `Game/SliceAudio.cs:192 CraftDone`, `Construction/StructureArea.cs:32 CoreItem`.
- Planet and places: `planet-surface/Terrain/TerrainStreamer.Cover.cs:56 CoverNear` (9), `FarBuilder.cs:1019 Overhangs` (9), `:963 DistantBasis` (6), `TerrainStreamer.cs:299 KindMesh`, `Poi/PoiSiting.cs:193 WithRules`, `Poi/PoiLayer.Tiers.cs:469 MidReady`, `poi-generator/Gen/Occupancy.cs:427 Webs` (9), `Gen/Regions.cs:75 ByRole`, `Gen/Features.cs:38 StopCells`, `Gen/Blocks.cs:169,300 IsEmissive, Unders`.
- Shared: `shared/Terrain/PlanetTerrainSource.cs:456 StampAt`, `shared/Items/Core/BlockBills.cs:70 ItemsFor`, `Machines.cs:71 FirstConstructorFor`, `DecoCatalogue.cs:170 WithRole`, `shared/Deco/Core/DecoForge.cs:51 ClearMeshes`, `Arrange/DecoArrangements.cs:151 SetFor`, `shared/Bestiary/Sim/Crawl.cs:472 BodyClear`, `Rigs/Shapes.cs:261 YawPitch`, `shared/Items/Deposits/DepositBody.cs:294 YieldAt`, `shared/Physics/Core/Units.cs:27,28 Newtons, Kilonewtons`.
- `building/Structures/NavGrid.cs` (see 14), and `shared/Items/Core/Validation.cs` (95 lines, called only by `manufacturing/tests/ItemsTest.cs`; keep, but move the test to a shared host before removing manufacturing).
- Data/shader/scene check found no unreferenced shader. All 144 `.tscn` are test scenes plus each prototype's `Main.tscn`; none of the dormant prototypes' scenes is referenced from outside.

## 17. Very long units

Longest source methods (not tests, not data tables): `World/Infra/CityPlan.cs:107 Plan` 428, `poi-generator/Gen/Arch/HomeBuild.cs:487 Draw` 318, `Gen/Mesher.cs:1461` 315 (the `For` switch), `planet-surface/Sky/StarSystem.cs:360 For` 270, `planet-generator/Gen/LandformLayer.cs:67` 270 (a constructor), `Gen/Arch/BuildingAlien.cs:16 AlienForm` 263, `World/Infra/FlavourRuins.cs:192 Plan` 247, `World/Infra/PowerLines.cs:192 Grid` 223, `Poi/PoiSiting.cs:406 Seat` 221, `slice/Surface/Light/SkyLit.cs:99 Inject` 198, `Gen/Arch/Building.cs:382 TryPlan` 206, `shared/Bestiary/Rigs/HumanRig.cs:156 Drive` 208, `PeopleGenerator.cs:309 Person` 180. These each want a split by stage; low payoff (no lines saved) and conflicts with open work.

Longest files: `World/Infra/InfraNetwork.cs` 2,148, `Gen/Mesher.cs` 1,778, `Gen/Arch/CapitalHull.cs` 1,763, `Gen/Arch/Sites.cs` 1,607, `Terrain/TerrainStreamer.cs` 1,414 (already split into five partials, still the largest of them), `slice/Places/Town.cs` 1,273 (already in 9 partials), `weather/WeatherSim.cs` 1,282, `slice/Ui/MapLevels.cs` 1,044. `shared/Deco/Core/Models/*` has 800-line methods (`Dressing.Festival.Festival` 787, `Stories` 529, `Vehicles` 504, `Aviation` 386): these are content as code, by design, and fit the "nothing bespoke" rule only if they stay combinations of parts; leave alone.

---

## What the audit did not find

- Comment bloat (11%, one echo).
- Dead scenes or shaders.
- Speculative options: only a handful of unset flags (`GameSettings` toggles were not audited here; no obvious unreferenced setting turned up in the dead-member scan, which would have caught a never-read field only if it were also never written).
- Wrapper layers: `Game.*` partials, `Place.*`, `Vessel.*` partials split big classes but do not wrap; no pass-through layer worth removing was found.

## Tightening passes

Order is by payoff over risk, and by which areas are free.

1. **Pass A, archive the dormant (now, no area conflict).** Tag, then remove `boarding`, `coop`, `encounters`, `invasions`, `walkable-ship` and `saves/src/Game`+`Records`+`Zones`. Repoint `tools/test.ps1:6`. About 34,500 lines, gate time down. Owner confirms first.
2. **Pass B, demos with a slice equivalent (now).** `physics-sandbox` app, `capital-base` app, `building/src/Game`, `weather` app and `Fx`, `terrain-digging` app, `ship-handling` `Physics`/`Presentation`, `survival-vitals` app, `diegetic-ui` `World`/`Pages`, `planet-surface` `WeatherView/WeatherRig` and `Player`. Move tests of shared code (`manufacturing`, `wildlife`, `deco`) into the shared module or keep their prototype as test host. About 22,000 lines.
3. **Pass C, shared tiny helpers (low conflict).** One `Rng`/`Hash`/`SmoothStep`/`Fract`/`Lerp` in `shared/Geometry` (keeping bit-identical streams, before/after world hash); one `SampleWav`; one `Clock`. About 1,000 lines. Do `SampleWav` with the sound area, `Rng` with spawns, places and trees.
4. **Pass D, test scaffolding (per area, as each area's tests settle).** `ShotRun`, `SliceTestBase`, shared `Check/Until/Tally`, one `SkySeamTest`. About 3,000 lines.
5. **Pass E, after grid and places merge.** One `Int3` in `shared/Geometry` and delete aliases (finding 4); one layer table (5); single `Tunables` source (6); one `Vessel.Spawn` (10); remove building `NavGrid` (14); drop `StuckGuard`/`Walker` copies. About 2,000 lines plus the removal of a recurring source of confusion.
6. **Pass F, after trees and flora and world building merge.** Collapse `Flora` kinds into `Plant` (8), `TileStream` (13), rename or merge the two `Infra` namespaces.
7. **Pass G, dead members (any time, by area).** Section 16 list as one build-and-delete sweep per area; about 300 lines.
8. **Pass H, design items to the owner, not tightening:** block-model and mesh-buffer unification (12), the screenshot/`docs/signs` weight (15), `SurfaceWeather` and long-method splits (17).

**Estimated safely removable: about 63,000 lines** (Pass A 34,500; B 22,000; C 1,000; D 3,000; E 2,000; G 300), of which Passes C to G (about 6,300 lines) touch code the slice compiles and need the gate; A and B only remove code no `.csproj` links, and their risk is the lost reference and the test hosting noted above.
