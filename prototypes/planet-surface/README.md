# Prototype: planet surface

The first whole planet you can stand on. The planet generator's worlds, as diggable 1 m voxel terrain with caves, ore and bedrock, streamed around you out to a 7 km horizon. Each has its own sky and its own weather, places from the POI generator seated into the slopes, and edits that are never lost. The map is a torus: walk far enough north or east and you come back from the south or west, with no edge and no polar wall.

![A ringed giant over a mountain range](screenshots/mountains.png)
![Canyon country under a dusty sky](screenshots/canyon.png)
![Fog pooled in the valleys at dawn](screenshots/foggy_valley.png)
![A thunderstorm over the mesas](screenshots/thunderstorm.png)
![A blizzard on an ice world](screenshots/blizzard.png)
![A research lab cut into a crystal world's hillside, under an exotic sky](screenshots/poi_hillside.png)
![A tunnel dug into the foot of a rock face](screenshots/cave.png)
![A volcanic world at dusk under ash](screenshots/volcanic_dusk.png)

## What it proves

- **The generator's planets are diggable.** `shared/Terrain` turns a `WorldMap` into terrain-digging's `ITerrainSource`. Every column's surface is the generator's own height at 1 m. Below it lies soil (thinner on slopes), rock, deep rock, and ore bodies under the deposit map's deposits. Caves are winding tubes 3–36 m down that sometimes break the surface. Bedrock lies 30 m below the nearby valley floors, so a mountain can be tunnelled through but the world has a floor. Terrain-digging's chunk store, Surface Nets mesher, edit log, saves and stuck safety are reused as they are.
- **A torus without seams.** The generator samples 4D noise on a flat torus, so terrain, climate and rivers all carry across both edges, and the equator runs through the middle of the map with a pole at each edge. The terrain store keeps the planet plus 8 ghost chunks beyond each seam. Every edit lands on every copy, and crossing a seam moves the frame by one planet width, re-keying the loaded chunks instead of rebuilding them.
- **Streaming that keeps up with a ship.** Near the player, voxel chunks within 5 chunk columns (80 m) are meshed on worker threads. Collision covers 2 columns, built only where needed. Beyond that come generator heightfield rings out to about 900 m, then a horizon mesh out to 7 km. Each layer cuts a hole where the finer one lies, so they never overlap. The main thread only uploads meshes, within a 3 ms budget shared between near and far.
- **Dramatic ground.** Ranges of mountains hundreds of metres high, escarpments and mesas with jagged edges, canyons and gorges, rolling lowland between, all varying by archetype (see the planet generator's [relief numbers](../planet-generator/README.md#relief)).
- **The sky follows the place.** `SkySystem` derives each sky from the world:
  - The star's class sets the light's colour.
  - Rayleigh scattering of that light gives the clear-sky colour (blue under a yellow star, paler under a white one, warmer under an orange one).
  - The air and archetype tint it further: dusty, humid, thin, ash-laden or exotic, with exotic air allowed to be pastel or neon.
  - Neighbouring bodies appear where the system has them: moons with phases, a gas giant (ringed if the planet has the RingedSky modifier), stars and a nebula at night.
  - Clouds are a slab, raymarched against the sun at half resolution, with lit tops and dark bases.
- **Weather from the weather prototype.** Its model and forces are compiled in unchanged. Clouds, fog, valley fog at dawn, rain, snow, dust, embers, thunderstorms with visible lightning, and tornadoes are driven by its deterministic conditions; wind pushes the walker by mass. Rain and snow stop under rock.
- **Places seated into the land.** POIs are placed by the POI generator's own rules on a 3.2 km map around the landing site. Each one is then fitted to the ground its kind prefers (ridge, valley, flat or any) and stamped with a pad: cut and fill blended into the slope, its rooms carved out below, and caves kept out from under it.
- **Nothing is lost.** Edits persist through unloading, reloading, leaving the planet and coming back, and save/load.
- **Seas and rivers you can go into.** Water is a volume, not a surface: under the sea's level where the world map lies below it, and under a river's surface in its channel, exactly where the water is drawn (`PlanetTerrainSource.WaterSurfaceAt`). The walker wades in the shallows and swims once deep enough, by the shared physics module's water rules and numbers (`shared/Physics/physics.json`, water): buoyancy and drag on the share of it under water, floating with its head out, Space and Ctrl to swim up and down. Under water the view turns to the liquid's own murk (its colours from the world's look, fogging thicker with depth) behind a tint, and the HUD shows the vitals flag: the suit is on its own oxygen. A lava world's seas are lava, and aren't entered.
- **Solid trees and boulders.** Trees, cacti, crystals and rocks bigger than a pebble get one simple collider each, a trunk cylinder or a box sized from the instance's own scale (`data/flora-solids.json`), in the near rings only, where the ground has collision: the finest far rings list them on the workers, and each near 16 m column gets one static body of shared shapes. The survival tool fells a tree for wood (its collider goes the same frame), and felled trees stay felled through streaming and saves. A player inside a trunk or a boulder is moved out beside it.

Everything is generated in code or shaders. Sounds are synthesised from filtered noise. There are no third-party assets.

## Play it

```powershell
.\tools\godot.ps1 -Project prototypes/planet-surface -Play
```

You land near the landing site of a temperate world. The help in the bottom right lists the keys.

| Key | Action |
| --- | --- |
| WASD, mouse | Walk and look |
| Shift | Sprint |
| Space | Jump; hold in the air for the jetpack; swim up in deep water |
| Ctrl (hold) | Crouch; swim down in deep water |
| Left mouse | Dig (1.4 m ball; bedrock and anchored ground refuse); at a tree, fell it for wood |
| Right mouse | Place a block |
| F | Free-fly (Shift fast, Space and Ctrl up and down) |
| 1 to 5 | Planet: temperate, arid, ice, lava, alien |
| V | Next variant of this archetype |
| N | New seed |
| K | Next weather |
| L | Lightning strike |
| O | Tornado |
| T | Time ×20 |
| Y | Jump to dawn, noon, dusk, night |
| F5, F9 | Save, load |
| H | Hide the help |
| Esc | Free the mouse |

## Test it

```powershell
.\tools\test.ps1 -Project prototypes/planet-surface -Scene res://tests/SurfaceTest.tscn
```

Runs headless in about 100 s and exits 0 or 1. Add `-- --only=Heights,Flight` to run just the named checks. It checks that:

- weather is deterministic from its seed, and each of the 12 states sets its own fog, cloud and valley-fog values
- every archetype variant's surface has its relief: range in 2 km, slopes over 30° and 45°, canyon depth
- POIs on five worlds are seated: no corner off its floor, no missing ground under or around their rooms, nothing buried, no blocked way in
- streamed voxel heights match the generator exactly (300 columns), and the walkable mesh is within 0.08 m of it
- neighbouring chunks share their border vertices bit for bit
- an edit survives unloading and reloading, a save round trip, and leaving the planet and coming back
- stuck safety frees a buried player and leaves one standing in a fresh pit alone
- water is a volume: from a shore found near the landing site the player walks into the sea until they swim, swims out over deep water floating with their head out, dives until their head is under (the vitals flag set, the view tinted and fogged) and comes back up, never inside or below the ground and never rescued
- solid flora, in a pine forest: colliders only in the near rings (25 in 10 bodies, none past the collision reach, of about 300 solids loaded); walking into a trunk stops the player at the trunk's radius, and a navigation grid's query marks it blocked; a big boulder stops them, a rock under 0.6 m is walked over; a hull the size of a hover rover, swept the way the shared physics module moves vessels, is stopped by a grove and by a boulder; the survival tool fells a conifer for wood, its collider gone the same frame, and it stays felled after unloading its ground and after a save and load; a player put inside a trunk is moved out, and the landing site is clear
- flying 2 km at 100 m/s keeps streaming's main-thread cost under 8 ms a frame (about 4 ms) in the best of three windows of the flight, and catches up after landing; GC pauses that land inside streaming are left out of that figure and reported beside it, along with the flight's collections
- chunks beyond the north edge match their twins at the south edge (heights to 0.24 mm, normals to 0.005°), as does the corner beyond both seams
- an edit straddling the north-south seam is removed on both sides and kept through save and load
- walking north across the edge keeps you on the ground and at speed in every one of three walks (simulation ticks, so load can't shorten one), with no frame over 8 ms of game code (about 3 ms; the crossing frame about 1.6 ms) and no engine stall between frames in the best of the three

The frame checks judge the best of three windows (or walks), and print all three: a machine busy with other work stalls some frames, but a real slowdown shows in every one. Everything else waits for a state (streamed in, colliders built, standing, at rest), or counts simulation ticks where the limit is simulation time, never wall-clock seconds.

Frame costs are measured per frame by `tests/FrameProbe.cs`. Godot's own monitors give only the slowest process and physics passes of a whole second, possibly from different frames, and up to a second stale.

## Screenshots

```powershell
. .\tools\env.ps1
dotnet build prototypes/planet-surface/PlanetSurface.csproj
& $GodotEditor --path prototypes/planet-surface res://tests/Screenshot.tscn -- --view=mountains --out="$PWD/shot.png"
```

Views are `mountains`, `canyon`, `foggy_valley`, `thunderstorm`, `blizzard`, `poi_hillside`, `cave` and `volcanic_dusk`. Each finds its own spot from the terrain (a peak, the deepest canyon, a ridge over low ground, a POI on a slope, a rock face to tunnel into), so none depend on hand-picked coordinates. `--debug=1` shows cloud cover, `--debug=2` cloud colour.

## How it fits together

- `shared/Terrain`: `PlanetTerrainSource` (the planet as cells), `PlanetTerrain` (the ghost-band torus store, edits on every copy, save and load), `TerrainStamp` (POI pads), `PeriodicNoise` (3D noise whose lattice wraps with the planet).
- `src/Terrain`: `TerrainStreamer` (voxel, far and horizon layers; `TerrainStreamer.Solids.cs`, the near colliders of solid flora), `VoxelMeshing`, `FarBuilder`, `FloraSolids` (which flora is solid, from `data/flora-solids.json`), `GroundPalette` (soil and rock colours by biome and world).
- `shared/Physics/Core` (compiled in): the shared physics module's water rules, which the walker swims by.
- `src/Sky`: `SkySystem` (the sky as data, from the planet spec), `Atmosphere` (weather to fog and cloud), `SkyRig` (sun, moons, giant, environment).
- `src/WeatherView`: the weather prototype's model and forces as particles, lightning, tornadoes, sound and ground cover.
- `src/Poi`: placement, siting and meshes for the POI generator's places.
- `src/Player`: the walker, survival tool, free-fly and stuck guard.

## Limits

- **GC pauses.** Streaming itself costs about 4 ms of main-thread time at 100 m/s, but .NET collections (about 16 MB allocated a second while flying fast) still pause for up to 6–9 ms now and then, so the slowest whole frame reaches about 10 ms. Walking stays under 3 ms.
- **POIs only near the landing site.** Places are generated on a 3.2 km map around it, not across the whole planet.
- **Water is static,** as in terrain-digging: you can go into seas and rivers, but they don't flow into dug holes, a pit dug below sea level stays dry, and there are no currents or waves. The sea's edge follows the world map's cells, so on a shallow shore its line can sit a metre or two off the voxel ground's.
- **Solid flora has one shape each:** trunks without canopies, boulders as boxes (a rock's knobbly outline is a little larger than its box). There are no vessels or creatures on this planet yet, so the checks sweep a rover-sized hull in their place, and nothing here breaks a tree by ramming it; the layer (`Layers.Flora`, the shared module's World) and the blocked-cell query (`TerrainStreamer.BlockedAt`) are there for them. The walker has no step-up of its own, so a rock between 0.6 m and its jump height is jumped over or walked round.
- **Weather is global** for the planet. There are no local storm cells yet, apart from tornadoes.
- **Far rings are coarser than voxels.** On sheer cliffs the 2–8 m heightfields can show a slight comb where a wall is steeper than their spacing.
- **The far grid stops 64 m short of the north-south seam.** Far chunks are whole 128 m squares, and 8 km is 62.5 of them, so the last strip before the seam has no far chunk: no flora is scattered there (so nothing solid stands in it), and the far rings have a 64 m gap there (near you, voxel ground covers it). Found while making the tests wait for colliders; not fixed here.
- **Ore outcrops show their 1 m cells** where they break the surface.
- **Planets are 16 x 8 km** by default.
- Particles are hidden under rock only where the chunks overhead are already loaded.
