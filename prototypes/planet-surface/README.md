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
| Space | Jump; hold in the air for the jetpack |
| Ctrl (hold) | Crouch |
| Left mouse | Dig (1.4 m ball; bedrock and anchored ground refuse) |
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

Runs headless in about 70 s and exits 0 or 1. Add `-- --only=Heights,Flight` to run just the named checks. It checks that:

- weather is deterministic from its seed, and each of the 12 states sets its own fog, cloud and valley-fog values
- every archetype variant's surface has its relief: range in 2 km, slopes over 30° and 45°, canyon depth
- POIs on five worlds are seated: no corner off its floor, no missing ground under or around their rooms, nothing buried, no blocked way in
- streamed voxel heights match the generator exactly (300 columns), and the walkable mesh is within 0.08 m of it
- neighbouring chunks share their border vertices bit for bit
- an edit survives unloading and reloading, a save round trip, and leaving the planet and coming back
- stuck safety frees a buried player and leaves one standing in a fresh pit alone
- flying 2 km at 100 m/s keeps streaming's main-thread cost under 8 ms a frame (about 4 ms) and catches up after landing
- chunks beyond the north edge match their twins at the south edge (heights to 0.24 mm, normals to 0.005°), as does the corner beyond both seams
- an edit straddling the north-south seam is removed on both sides and kept through save and load
- walking north across the edge keeps you on the ground, at speed, with no frame over 8 ms

## Screenshots

```powershell
. .\tools\env.ps1
dotnet build prototypes/planet-surface/PlanetSurface.csproj
& $GodotEditor --path prototypes/planet-surface res://tests/Screenshot.tscn -- --view=mountains --out="$PWD/shot.png"
```

Views are `mountains`, `canyon`, `foggy_valley`, `thunderstorm`, `blizzard`, `poi_hillside`, `cave` and `volcanic_dusk`. Each finds its own spot from the terrain (a peak, the deepest canyon, a ridge over low ground, a POI on a slope, a rock face to tunnel into), so none depend on hand-picked coordinates. `--debug=1` shows cloud cover, `--debug=2` cloud colour.

## How it fits together

- `shared/Terrain`: `PlanetTerrainSource` (the planet as cells), `PlanetTerrain` (the ghost-band torus store, edits on every copy, save and load), `TerrainStamp` (POI pads), `PeriodicNoise` (3D noise whose lattice wraps with the planet).
- `src/Terrain`: `TerrainStreamer` (voxel, far and horizon layers), `VoxelMeshing`, `FarBuilder`, `GroundPalette` (soil and rock colours by biome and world).
- `src/Sky`: `SkySystem` (the sky as data, from the planet spec), `Atmosphere` (weather to fog and cloud), `SkyRig` (sun, moons, giant, environment).
- `src/WeatherView`: the weather prototype's model and forces as particles, lightning, tornadoes, sound and ground cover.
- `src/Poi`: placement, siting and meshes for the POI generator's places.
- `src/Player`: the walker, survival tool, free-fly and stuck guard.

## Limits

- **GC pauses.** Streaming itself costs about 4 ms of main-thread time at 100 m/s, but .NET collections still pause the frame for up to 6–9 ms now and then, so the whole frame occasionally reaches 11–14 ms while flying fast. Walking stays under 4 ms.
- **POIs only near the landing site.** Places are generated on a 3.2 km map around it, not across the whole planet.
- **Water is static,** as in terrain-digging: seas and rivers don't flow into dug holes, and a pit dug below sea level stays dry.
- **Weather is global** for the planet. There are no local storm cells yet, apart from tornadoes.
- **Far rings are coarser than voxels.** On sheer cliffs the 2–8 m heightfields can show a slight comb where a wall is steeper than their spacing.
- **Ore outcrops show their 1 m cells** where they break the surface.
- **Planets are 16 x 8 km** by default.
- Particles are hidden under rock only where the chunks overhead are already loaded.
