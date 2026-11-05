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
- **Streaming that keeps up with a ship.** Near the player, voxel chunks within 5 chunk columns (80 m) are meshed on worker threads. Collision covers 2 columns, built only where needed. Beyond that come generator heightfield rings out to about 900 m, then a horizon mesh out to 7 km. The rings' 128 m chunks tile the planet both ways: where its size isn't a multiple of 128 m (8 km is 62.5 chunks), the last chunk before the seam is cut short, ground and flora alike. Each layer cuts a hole where the finer one lies, so they never overlap. The main thread only uploads meshes, within a 3 ms budget shared between near and far.
- **Dramatic ground.** Ranges of mountains hundreds of metres high, escarpments and mesas with jagged edges, canyons and gorges, rolling lowland between, all varying by archetype (see the planet generator's [relief numbers](../planet-generator/README.md#relief)).
- **The sky follows the place.** `SkySystem` derives each sky from the world:
  - The star's class sets the light's colour.
  - Rayleigh scattering of that light gives the clear-sky colour (blue under a yellow star, paler under a white one, warmer under an orange one).
  - The air and archetype tint it further: dusty, humid, thin, ash-laden or exotic, with exotic air allowed to be pastel or neon.
  - Neighbouring bodies appear where the system has them: moons with phases, a gas giant (ringed if the planet has the RingedSky modifier), stars and a nebula at night.
  - Clouds are a slab, raymarched against the sun at half resolution, with lit tops and dark bases.
  - **No seams anywhere on it.** The night backdrop is looked up along the 3D direction itself: the nebula from 3D noise inside one texture tile (a longitude-latitude map pinched it into a starburst at its pole and tore it along a meridian), the stars as points through a thin shell of a 3D lattice, each kept or dropped by itself (cutting the lattice with the sky's sphere left a star-free hole ringed by bands at each axis). Stars fade out below the horizon instead of stopping at a line. The cloud march starts each pixel at a well-spread depth (interleaved gradient noise) and takes more steps where its path through the slab is long, so cloud edges no longer slice into combs and streaks, and the reflection map's cloud cover eases in over the horizon instead of switching on there.
  - **A low sun never drenches the picture.** Near the horizon its light comes through more air: under half from about 17° down (`SkyRig.AirMass`, the same curve in the shader's `sun_air`), for the sunlight, the sun's disc (warmer too) and its wide halo. Haze and fog are lit as the ground is, by a sun clear of the horizon's air, so under a low sun they are dim and warm, not a white wall; the glow toward the sun through fog (sun scatter) is modest. Glow only comes from what is really bright (HDR threshold 2). Morning valley fog forms overnight and burns off: after noon only the weather's own ground fog lies in the valleys, so a setting sun brings none. And once the camera is down in a bank of valley fog it becomes distance fog instead of height fog, so the ground at your feet stays readable.
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
| Left mouse | Dig (1.4 m ball, anywhere but bedrock); at a tree, fell it for wood |
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
- the walker steps up onto anything a step tall by the shared physics module's step-up (`shared/Physics/Engine/Steps.cs`) and step height (0.6 m): walked into a sheer 0.5 m slab it is up on top without a jump, and the same slab 1 m tall stops it
- water is a volume: from a shore found near the landing site the player walks into the sea until they swim, swims out over deep water floating with their head out, dives until their head is under (the vitals flag set, the view tinted and fogged) and comes back up, never inside or below the ground and never rescued
- solid flora, in a pine forest: colliders only in the near rings (25 in 10 bodies, none past the collision reach, of about 300 solids loaded); walking into a trunk stops the player at the trunk's radius, and a navigation grid's query marks it blocked; a big boulder stops them, a rock under 0.6 m is walked over; a hull the size of a hover rover, swept the way the shared physics module moves vessels, is stopped by a grove and by a boulder; the survival tool fells a conifer for wood, its collider gone the same frame, and it stays felled after unloading its ground and after a save and load; a player put inside a trunk is moved out, and the landing site is clear
- flying 2 km at 100 m/s keeps streaming's main-thread cost under 8 ms a frame (about 4 ms) in the best of three windows of the flight, and catches up after landing; GC pauses that land inside streaming are left out of that figure and reported beside it, along with the flight's collections
- chunks beyond the north edge match their twins at the south edge (heights to 0.24 mm, normals to 0.005°), as does the corner beyond both seams
- the far grid tiles the planet across the north-south seam: the last 64 m strip before it has its far ground and its flora
- an edit straddling the north-south seam is removed on both sides and kept through save and load
- walking north across the edge keeps you on the ground and at speed in every one of three walks (simulation ticks, so load can't shorten one), with no frame over 8 ms of game code (about 3 ms; the crossing frame about 1.6 ms) and no engine stall between frames in the best of the three

`tests/SkyTest.tscn` renders, so a headless run starts itself again in a window placed off-screen (`--position 20000,20000`) and passes on what it prints (`shared/Sky/SkySeams.cs`). In about 2 minutes it checks that:

- the sky alone, from the slice's world (`SkyRig` with no ground), looked at straight along a cube's corners and edges, the zenith, the axes and the azimuth's wrap, by day under broken cloud with the sun well up and in the afternoon west, at dusk and under heavy cloud, and a nebula world's sky at midnight (and its backdrop alone, brightened six times): shows no line where the colour jumps, no crowd of jumps, no swirl round a pole, and stars as dense along the axes as anywhere else
- a low sun never drenches the picture: on a temperate coast and in arid hills, at dawn and at dusk with the sun 5–7° up, looking toward it, across it and away from it, in clear air and in fog: under 2% near-white pixels, never brighter than noon in the same place and weather, and the ground never a bright, flat wall of lit haze (it keeps contrast of its own)

The frame checks judge the best of three windows (or walks), and print all three: a machine busy with other work stalls some frames, but a real slowdown shows in every one. Everything else waits for a state (streamed in, colliders built, standing, at rest), or counts simulation ticks where the limit is simulation time, never wall-clock seconds.

Frame costs are measured per frame by `tests/FrameProbe.cs`. Godot's own monitors give only the slowest process and physics passes of a whole second, possibly from different frames, and up to a second stale.

## Screenshots

```powershell
. .\tools\env.ps1
dotnet build prototypes/planet-surface/PlanetSurface.csproj
& $GodotEditor --path prototypes/planet-surface res://tests/Screenshot.tscn -- --view=mountains --out="$PWD/shot.png"
```

Add `--position 20000,20000` before `--path` to keep the window off-screen. Views are `mountains`, `canyon`, `foggy_valley`, `thunderstorm`, `blizzard`, `poi_hillside`, `cave` and `volcanic_dusk`. Each finds its own spot from the terrain (a peak, the deepest canyon, a ridge over low ground, a POI on a slope, a rock face to tunnel into), so none depend on hand-picked coordinates. `--debug=1` shows cloud cover, `--debug=2` cloud colour, `--debug=3` the night backdrop alone, six times as bright.

### Sky settings the slice inherits

The slice uses this prototype's `SkyRig` and `shaders/sky.gdshader` as they are, so these changes reach it:

| Where | Setting | Was | Now |
| --- | --- | --- | --- |
| `SkyRig` environment | glow intensity, bloom, HDR threshold | 0.6, 0.03, 1.2 | 0.5, 0.02, 2.0 |
| `SkyRig.Apply` | sun light energy | star's energy × weather | also × `AirMass(sun height)`: 0.45 at the horizon, 1 from about 17° up |
| `SkyRig.Apply` | fog light | haze × daylight (full from 7° up) | haze × light that rises from 9° below the horizon until about 14° up (about 0.8 at 7°), so a low sun lights haze and fog a little dimmer and warmer |
| `SkyRig.Apply` | fog sun scatter | 0.15 + 0.25 × air density | (0.08 + 0.12 × air density) × (1 − overcast) |
| `SkyRig.SetValleyFog` | valley fog | morning fog whenever the sun was low | morning fog only before noon; down in the bank, distance fog instead of height fog |
| `Atmosphere` | new: `GroundFog`, `ValleyFogDepthOf` | | the weather's own ground fog, for the evening |
| `sky.gdshader` | the sun's disc and wide halo, the sunset glow | full strength at any height | dimmed and warmed by `sun_air` near the horizon; sunset glow 0.45 + 0.35 × density |
| `sky.gdshader` | cloud march | 40 steps, a third of a step of jitter by direction | 24–64 steps by path length, a whole step of jitter by pixel |
| `sky.gdshader` | stars, nebula, horizon | lattice cut by the sphere; longitude-latitude nebula; stars cut off below the horizon | shell stars; 3D nebula (`nebula_noise`, `nebula_detail`, set by `SkyRig`); stars fade out below it |

## How it fits together

- `shared/Terrain`: `PlanetTerrainSource` (the planet as cells), `PlanetTerrain` (the ghost-band torus store, edits on every copy, save and load), `TerrainStamp` (POI pads), `PeriodicNoise` (3D noise whose lattice wraps with the planet).
- `src/Terrain`: `TerrainStreamer` (voxel, far and horizon layers; `TerrainStreamer.Solids.cs`, the near colliders of solid flora), `VoxelMeshing`, `FarBuilder`, `FloraSolids` (which flora is solid, from `data/flora-solids.json`), `GroundPalette` (soil and rock colours by biome and world).
- `shared/Physics/Core` and `shared/Physics/Engine/Steps.cs` (compiled in): the shared physics module's water rules, which the walker swims by, and its step-up and step height, which it walks by.
- `src/Sky`: `SkySystem` (the sky as data, from the planet spec), `Atmosphere` (weather to fog and cloud), `SkyRig` (sun, moons, giant, environment).
- `src/WeatherView`: the weather prototype's model and forces as particles, lightning, tornadoes, sound and ground cover.
- `src/Poi`: placement, siting and meshes for the POI generator's places.
- `src/Player`: the walker, survival tool, free-fly and stuck guard.

## Limits

- **GC pauses.** Streaming itself costs about 4 ms of main-thread time at 100 m/s, but .NET collections (about 16 MB allocated a second while flying fast) still pause for up to 6–9 ms now and then, so the slowest whole frame reaches about 10 ms. Walking stays under 3 ms.
- **POIs only near the landing site.** Places are generated on a 3.2 km map around it, not across the whole planet.
- **Water is static,** as in terrain-digging: you can go into seas and rivers, but they don't flow into dug holes, a pit dug below sea level stays dry, and there are no currents or waves. The sea's edge follows the world map's cells, so on a shallow shore its line can sit a metre or two off the voxel ground's.
- **Solid flora has one shape each:** trunks without canopies, boulders as boxes (a rock's knobbly outline is a little larger than its box). There are no vessels or creatures on this planet yet, so the checks sweep a rover-sized hull in their place, and nothing here breaks a tree by ramming it; the layer (`Layers.Flora`, the shared module's World) and the blocked-cell query (`TerrainStreamer.BlockedAt`) are there for them. A rock over the 0.6 m step height is jumped over or walked round.
- **Weather is global** for the planet. There are no local storm cells yet, apart from tornadoes.
- **Far rings are coarser than voxels.** On sheer cliffs the 2–8 m heightfields can show a slight comb where a wall is steeper than their spacing.
- **Ore outcrops show their 1 m cells** where they break the surface.
- **Planets are 16 x 8 km** by default.
- Particles are hidden under rock only where the chunks overhead are already loaded.
