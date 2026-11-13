# Prototype: planet generator

Proves that GalHack can generate planets that are varied, distinct and good-looking from one seed: a globe you can spin from orbit, and the same world under your feet when you land, streamed without hitches.

Worlds are divided into regions of real landforms: plains, steppe, mountain ranges, dune seas, deltas, mesas, fjords and 17 more (see [LANDFORMS.md](LANDFORMS.md)).

![A mixed world from orbit, tinted by landform (L)](screenshots/landforms_orbit.png)
![The same world in its own colours, with relief](screenshots/globe_mixed.png)

| | |
| --- | --- |
| ![Plains to the horizon](screenshots/landform_plains.png) | ![A mountain range over its foothills](screenshots/landform_mountains.png) |
| ![A dune sea](screenshots/landform_dune_sea.png) | ![A river delta from the air](screenshots/landform_delta.png) |
| ![Mesa country](screenshots/landform_mesas.png) | ![A fjord](screenshots/landform_fjords.png) |
| ![Steppe](screenshots/landform_steppe.png) | ![A dune world: one landform throughout](screenshots/globe_dune_world.png) |
| ![A mountain world from orbit](screenshots/globe_mountains.png) | ![A range on the terminator, at the limb](screenshots/globe_limb.png) |
| ![A volcanic field: cones over a glowing lava field](screenshots/landform_volcanic_field.png) | ![Basalt plains beside a lava sea](screenshots/landform_basalt_plains.png) |
| ![Crystal shard fields from the air](screenshots/landform_shard_fields.png) | ![Dome fields under giant fungi](screenshots/landform_dome_fields.png) |
| ![A ring structure in the ringlands](screenshots/landform_ringlands.png) | ![A volcanic world from orbit](screenshots/globe_volcanic.png) |
| ![An alien crystal world from orbit](screenshots/globe_alien.png) | |

[atlas/landforms.png](atlas/landforms.png) shows one world per variant: its landform map beside its colour map.

Before landforms, every world was the same blend of hills and cliffs:

![Five archetypes from orbit, before landforms](screenshots/orbits.png)
![Fungal world, before landforms](screenshots/surface_fungal.png)
![Volcanic world, before landforms](screenshots/surface_lava.png)
![Dune world, before landforms](screenshots/surface_dunes.png)
![Temperate meadow, before landforms](screenshots/surface_temperate.png)

## How it works

Generation is pure C# in `src/Gen`, with no engine types, and deterministic from a `PlanetSpec` (seed, archetype, variant, modifiers, size). Presentation lives in `src/Look` (palettes, colour maps), `src/Orbit` and `src/Surface`.

1. **One height function for every scale, on a torus.** `TerrainModel` gives the elevation at any point and sample spacing. The map wraps both ways: walk off the north edge and you come in at the south edge at the same x, as you do east-west. Every noise field is 4D simplex noise sampled on a flat torus (x round one circle whose circumference is the map width, y round another whose circumference is the map height), so both seams and the corner where they meet are invisible and a metre is a metre in both directions. Octaves finer than the spacing are dropped, so the 32 m world map and the 2 m terrain near the player come from the same function and always agree.
2. **Continents and oceans first.**
   - Sea level is the quantile that leaves each planet's rolled land target (inside its variant's band) above water.
   - Lakes, pools, delta channels and fjords cut below sea level afterwards. A sparse first pass measures what they cost, and sea level is chosen again to make up for it.
3. **Landforms.** The land is divided into regions, each with its own landform, and every height sample runs one relief engine on the blend of the landforms there (see **Landforms** below). Volcanoes, cinder cones, ring structures and craters sit on jittered grids that wrap on both axes.
4. **Rivers.** Priority-flood fills pits, flow follows the steepest descent, catchments add up, and cells draining enough area become rivers. Neighbours wrap on both axes, so rivers flow across either seam. River surfaces never climb downstream: where a river leaves a basin it cuts a gorge rather than floating over the rim. The rivers become smoothed polylines, and the fine terrain cuts channels along them, meandering a few tens of metres off the polyline so the coarse grid never shows.
5. **Climate and biomes.** Temperature comes from latitude and altitude; humidity from distance to water, dry belts and rain shadows, and the landform's own air. The equator is the middle row and both map edges are the pole, so the polar band runs straight across the north-south seam: cold country, never a wall. Each archetype variant has a short rule list for its biomes. Each landform hints at its own: a dune sea is sand on any world, wetlands are swamp, sea cliffs and fjord walls are bare rock. Riverbanks get their own biome, and colours blend across wandering borders.
6. **Starter materials.** The planet is cut into 200 m cells, and every cell with usable land gets a surface or shallow (3 m deep at most) deposit of each common material for a small vessel: iron, copper, silicon, carbon and water. From any walkable spot on any planet type, all five are within about 230 m. That keeps the early game short (a small ship within 1–2 hours). Each archetype also scatters its signature resource.

**From orbit**, the globe's texture is the generator's own colour map, wrapped as an equirectangular projection. The map's top and bottom rows each pinch to a pole on the globe, which shows no seam. Enter lands you on a riverside or coastal spot near whatever faces the camera. L tints the globe and the flat map by landform, with a key.

**The globe has real relief** (`src/Orbit/GlobeRelief.cs`). It is a 720 x 360 sphere displaced by the height map, built on the worker with the colour map (about 60 ms, 518 k triangles):
- **Heights.** Oceans stay flat at sea level. Land rises by h / (h + 450 m) of up to 1.25% of the radius. These planets are only a few kilometres round, so true relief would make a spiky ball. This scale keeps plains low, lifts tablelands a little and ranges most, and stays under the clouds.
- **Shading.** Normals come from the same surface with its relief steepened three times, so the terminator shows ranges and escarpments in light and shade, and ranges break the limb a little.
- **No seams.** The wrap's columns are the same points and normals. Heights relax to each pole's average over the last 11% of latitude, so the pinched poles have no spike.
- **Same mapping.** The layout matches Godot's sphere, so the textures, `FacingPoint` and landing line up as before.
- **Cost.** Orbit frame time went from 0.42 ms to 0.40-0.48 ms (vsync off, 63 k to 544 k primitives).

**On the surface**, terrain streams in 160 m chunks on worker threads:
- **Near chunks:** 2 m grid with full scatter within 2 chunks of the camera, 4 m with trees within 4, and 8 m out to 7 chunks (about 1.1 km).
- **Far ring:** drawn straight from the world map, it reaches the horizon and cuts a hole wherever detailed chunks are loaded. It sits a little low so the detailed chunks win, but low ground (deltas, wetlands) sinks less, or it would show under water.
- **Wrapping:** chunk coordinates wrap on both axes, and every chunk is drawn at whichever copy of it lies nearest the camera.
- **Upload budget:** finished chunks reach the scene within 3 ms of main-thread time per frame.
- **Scatter:** low-poly flora and deposits use one MultiMesh per kind per chunk.

## Landforms

The doc's "Landforms" paragraph, built (rev 422 and 423):
- A few worlds are one landform throughout; most combine two or more.
- Regions are placed where they make sense and blended at the borders.
- Flat, open ground is as common as rugged ground, so the dramatic places stand out.

The catalogue of 24 landforms is in [LANDFORMS.md](LANDFORMS.md). It gives what each looks like, where it occurs, its relief recipe and which archetypes use it.

**The region layer** (`src/Gen/LandformLayer.cs`) is built once per world from its spec, before any height exists, on a 256 m grid:
1. **Context.** The continents' large shape gives land, sea and a rough sea level. From them come:
   - distance to the coast
   - temperature
   - dryness: subtropical dry belts, continental interiors, and rain shadows downwind of ranges on each latitude band's prevailing wind
   - a rough drainage map that finds where the big rivers reach the sea
2. **Mountain ranges** are traced as spines along the strongest uplift lines (zero contours of a warped field), each several kilometres long. A range holds a band round its spine, with foothills in a wider band outside. The range sags at intervals along its spine for passes and tapers at its ends. Range heights grow with the square of how much the range holds, so their edges grade down into foothills.
3. **Regions.** The world is cut into irregular regions (a jittered, warped Voronoi about 2 km across on a 16 km world, up to 4 km on big ones).
   - **Inland landforms** claim the regions that suit them best, each up to its share of the land. Picky landforms claim first: dune seas take the driest country, ice plains the coldest, wetlands the lowest and wettest, while plains and hills do well almost anywhere. A pull towards neighbours of the same landform makes whole countries.
   - **Coastal landforms** claim coastal regions: fjords only on cold coasts, cliffs on high ones, archipelagos on shallow shelves.
   - **Deltas** fan out from the mouths of the biggest rivers on unfrozen coasts.
4. **Blending.** Neighbouring regions share weight over a band, and a sample is a quadratic B-spline over the 3 x 3 grid nodes round it, looked up through a gentle warp so borders wander. It has no creases along grid lines and never overshoots. A blend keeps the strongest four landforms, lowered by the fifth's weight, so it stays continuous.

**Recipes and one engine.** Each landform has a relief recipe (`src/Gen/Landforms.cs`) of amplitudes, covers and multipliers:
- **Base.** Flatten (keep only the continent's broad shape), compress towards the country's own level, squash towards the waterline, lift and swell.
- **Hills and ranges.** Hills with flat valley floors, and range massifs and ridges.
- **Structural.** Tablelands, terraces, canyons (V gorges, U valleys or ledged canyons).
- **Shaped fields.** Dunes, gullies, towers, spires.
- **Point features.** Cinder cones and rings.
- **Water and coast.** Lakes, fjord troughs, sea cliffs, delta distributaries, islands.
- **Rivers and air.** River, rain and meander numbers, and dryness.

Recipes are written for a temperate world and scaled by each archetype's own numbers, so a fungal world's hills are domes. Every sample blends the recipes of the landforms there, a weighted sum, and runs one shared relief engine (`TerrainModel.Shape`). A border is a gradient, never a seam. Plains, wetlands and the like skip the expensive fields, so fine terrain got faster: a 160 m chunk went from 20 ms to about 12.5 ms on this machine.

Signature details:
- **Dune seas.** Transverse dunes are a wave with a whole number of crests round the planet both ways, so they wrap. They lie square to the world's prevailing wind, with a long windward slope and a steep slip face; crests wander and link, and the dunes swell and shrink across the field.
- **Deltas.** A tree of distributary channels from the apex out to the sea, splitting as it goes, with pools between and new land built out over the shallows.
- **Fjords.** U-shaped troughs whose floors lie below the sea near the coast and climb inland, between walls that rise straight from the shore.
- **Plains.** The continent's shape low-passed (nothing under about 2 km survives) and compressed round the country's own level, with a few metres of swell. Rivers there meander widely with low banks.

**World mixes** (`ArchetypeDef.Landforms`, `LandformMix`):
- **Palette.** Each variant has a landform palette, the landforms that may make up a whole world alone, and how often a world is one landform throughout.
- **Single-landform worlds.** About 21% of worlds are one landform throughout: a dune world, an ice plain, an endless steppe, an archipelago world, a basalt or wetland or karst world.
- **Mixed worlds.** The rest roll 2 to 5 landforms:
  - The dominant one takes 38-55% of the land and is more often an open landform.
  - Ranges always come with their foothills.
  - Open ground (rolling counting a quarter) takes at least half the inland share.
  - Terrain modifiers push their landform in: archipelago, mesas, canyons, highlands (mountains).

| Variant | Landforms it can roll (likeliest first) | One landform throughout |
| --- | --- | --- |
| Temperate meadow | plains, mountains, hills, steppe, river valleys, lake district, delta, coastal cliffs, plateau, fjords, wetlands, archipelago, karst, dune sea, rocky desert, badlands | plains, steppe, archipelago |
| Temperate rainforest | hills, plains, wetlands, river valleys, mountains, delta, karst, plateau, lake district, archipelago, coastal cliffs | wetlands, karst, archipelago |
| Arid dunes | dune sea, rocky desert, steppe, mesas, badlands, plateau, mountains, canyonlands, coastal cliffs, delta, plains | dune sea, rocky desert |
| Arid canyon | canyonlands, mesas, plateau, badlands, rocky desert, steppe, dune sea, mountains, coastal cliffs, river valleys, delta | mesas, canyonlands |
| Ice glacier | ice plain, mountains, fjords, coastal cliffs, hills, plateau, lake district, steppe, plains, archipelago | ice plain |
| Ice hot spring | ice plain, hills, volcanic field, mountains, lake district, plains, fjords, plateau, wetlands, coastal cliffs | ice plain, lake district |
| Lava volcanic | volcanic field, basalt plains, mountains, badlands, plateau, canyonlands, rocky desert, mesas, coastal cliffs | basalt plains, volcanic field |
| Lava ocean | archipelago, volcanic field, basalt plains, coastal cliffs, plateau, badlands | archipelago |
| Alien crystal | shard fields, plains, ringlands, mesas, canyonlands, mountains, plateau, rocky desert, dune sea, coastal cliffs | shard fields, ringlands |
| Alien fungal | dome fields, wetlands, plains, karst, ringlands, hills, lake district, delta, archipelago, river valleys | dome fields, wetlands |

**Queries.** All are cheap, deterministic and seamless:
- `WorldMap.LandformsAt(x, y)` and `SurfaceSampler.LandformsAt(x, y)` give the landforms at a point and how much each holds (a `LandformBlend`, strongest first).
- `WorldMap.Landform[]` gives each cell's dominant landform.
- `TerrainModel.Mix` gives the world's mix, and `LandformLayer.Present()` the shares really on the ground.
- `LandformLayer.Deltas` and `LandformLayer.Ranges` give the deltas and range spines.

`src/Gen/Landform.cs` (the enum, the blend and the names) has no other dependencies, so wildlife compiles it to build species for their home landforms.

**How Empyrion does it.** Empyrion (the local install's `Content/Playfields/*/playfield_dynamic.yaml` and `playfield_static.yaml`, read only, nothing copied) has one heightmap per planet type. It picks biomes per point by height, slope, humidity, temperature and latitude criteria, some forced by stamps, and its creature spawns list the biomes they spawn in. GalHack's doc asks for more: landform regions with their own relief, placed by climate and drainage and blended at their borders. Species' home landforms play the role of Empyrion's per-spawn biome lists.

## Planet sizes

Worlds come in four sizes, on Empyrion's planet classes 2-5 and their odds (the map widths are community figures, and the design doc's rev 456):

| Size | Planets | Moons |
| --- | --- | --- |
| 8x4 km | 5% | 1 in 9 |
| 16x8 km | 40% | 8 in 9 |
| 32x16 km | 50% | never |
| 64x32 km | 5% | never |

Start worlds are always 16x8 km. `PlanetSpec.RollWidthKm(seed, kind)` rolls the width from the seed, and `PlanetSpec.RollSized(seed, archetype, variant, kind)` makes the whole spec, with `kind` one of `WorldKind.Planet`, `Moon` or `Start`. `PlanetSpec.Roll` and its explicit width are unchanged, so nothing downstream moves until it asks for a roll. The moon odds are the planet odds of the two small classes, renormalised: the brief named the classes, not the odds.

A bigger world has bigger or more regions, never stretched ones, because a region's size is set in metres and clamped to 1.6-4 km:

| Size | Regions | Each | Map | Colour map | Globe |
| --- | --- | --- | --- | --- | --- |
| 8x4 km | 10 | 1.6-2 km | 52 ms | 167 ms | 46 ms |
| 16x8 km | 32 | 2 km | 141 ms | 131 ms | 49 ms |
| 32x16 km | 32 | 4 km | 490 ms | 429 ms | 55 ms |
| 64x32 km | 128 | 4 km | 2.5 s | 1.1 s | 0.1 s |

Timings are one run on the development machine, off the main thread.

## Heights

Orbit starts at 1,060 m and the cloud layer lies at about 600-1,000 m, so land stops short of them:

| What | Height |
| --- | --- |
| Hard limit (`ArchetypeDef.TerrainCeiling`) | 900 m on every world |
| Most ranges top out at | about 500 m |
| The highest peaks of the biggest ranges | 700-900 m, up into the cloud |
| Every other landform | up to about 500 m (fjords, whose walls are mountains, reach 584 m) |

Three things keep it that way:

- **`MountainHeight`** is 200-425 m by variant, on a short wavelength (1,150-1,700 m), so low ranges stay as steep as tall ones. Mountain land measures 91% over 30° and 620 m of relief in 1 km.
- **`SummitHeight`** lifts crests only in the core of the biggest ranges, where a slow swell picks out a few peaks. Narrow ranges have no such core. Ground over 600 m is 1.1% of mountain land.
- **The ceiling** is a soft roll-off over the last fifth of the height, so the rare peak that gets there stays a peak and never reaches the limit. The highest ground over 140 test worlds is 875 m.

Canyons and fjords still cut below sea level. Temperature falls at the old rate up to 200 m, so lowland climate didn't move, and faster above it, so the tops of 500 m ranges carry ice. All ground over 460 m on temperate and ice worlds is snow or ice, and biome altitude rules are scaled to match.

## Relief

`Relief.Measure` puts numbers on how dramatic the land is, on dry ground:
- **Range in 2 km:** highest minus lowest ground (sea at its surface) in 2 x 2 km windows. Median over the windows.
- **Slopes:** the share of ground steeper than 30° and 45° across 8 m (a hillside or a cliff face, not a bump underfoot). 45° is about where walking stops and the jetpack starts.
- **Canyon depth:** how far ground sits below walls on both sides within 150 m, at the 95th percentile.

`GenTest` measures every variant on two fixed seeds. Landforms deliberately took relief out of most of the land, since the doc wants flat, open ground as common as rugged ground. So each variant is now held to having drama somewhere (its biggest range in a 2 km window, and a share of cliffs) rather than everywhere, and each landform is held to its own character (see Test it). Before landforms (the relief work's numbers) and after, on the same seeds:

| Variant | Range in 2 km (median / biggest) | Slopes > 30° | Slopes > 45° | Canyon depth |
| --- | --- | --- | --- | --- |
| Temperate meadow | 947 → 299 / 1114 m | 49 → 12% | 32 → 7.5% | 61 → 19 m |
| Temperate rainforest | 494 → 207 / 556 m | 52 → 14% | 33 → 8.1% | 57 → 49 m |
| Arid dunes | 245 → 111 / 465 m | 26 → 6% | 11 → 3.0% | 31 → 17 m |
| Arid canyon | 324 → 181 / 541 m | 29 → 13% | 22 → 8.7% | 108 → 44 m |
| Ice glacier | 620 → 821 / 1121 m | 53 → 16% | 36 → 9.2% | 69 → 12 m |
| Ice hot spring | 412 → 174 / 553 m | 38 → 10% | 19 → 5.6% | 44 → 22 m |
| Lava volcanic | 498 → 195 / 645 m | 36 → 6% | 18 → 1.9% | 41 → 10 m |
| Lava ocean | 404 → 136 / 259 m | 42 → 13% | 23 → 9.0% | 35 → 24 m |
| Alien crystal | 482 → 134 / 475 m | 46 → 6% | 29 → 2.3% | 64 → 12 m |
| Alien fungal | 343 → 103 / 198 m | 39 → 5% | 18 → 2.4% | 46 → 14 m |

Each landform's own numbers, measured where it holds on a world that has plenty of it (`LandformCharacter`):

| Landform | Gentle (under 5°) | Over 45° | Relief in 1 km | What is checked |
| --- | --- | --- | --- | --- |
| plains | 87% | 0% | 38 m | 80% gentle, 40 m or less |
| steppe | 97% | 1% | 13 m | 80% gentle, 40 m or less |
| ice plain | 94% | 1% | 18 m | 80% gentle, 40 m or less |
| basalt plains | 73% | 1% | 19 m | 65% gentle, 40 m or less |
| rocky desert | 84% | 3% | 100 m | 65% gentle, rock domes allowed |
| wetlands | 88% | 1% | 12 m | 85% gentle, 15 m, 5-60% water |
| delta | 93% | 0% | 3 m | 6 channels crossed per arc across the fan |
| lake district | 78% | 3% | 18 m | 19% under water (8-60%) |
| hills | 39% | 0% | 63 m | 25-400 m, not flat |
| river valleys | 46% | 7% | 98 m | flat floors between bluffs |
| mountains | 0% | 91% | 620 m | 220 m and a quarter over 30° |
| fjords | 2% | 61% | 338 m | steep walls, 24% water in the troughs |
| plateau | 42% | 23% | 170 m | flat tops and cliffs |
| mesas | 29% | 10% | 119 m | flat tops and cliffs |
| canyonlands | 36% | 22% | 156 m | flat tops and cliffs |
| badlands | 3% | 33% | 62 m | 33% over 30°: dense gullies |
| dune sea | 20% | 4% | 41 m | crests 38 m, correlation 0.41 one wavelength along the wind, -0.60 at half |
| karst | 82% | 10% | 108 m | towers out of a flat plain |
| coastal cliffs | 22% | 3% | 159 m | 85% of its shore rises 15 m within 60 m |
| archipelago | 36% | 4% | 46 m | 54% water |
| volcanic field | 31% | 4% | 131 m | cones |
| shard fields | 23% | 22% | 143 m | spires |
| dome fields | 20% | 1% | 136 m | rolling domes |
| ringlands | 79% | 3% | 40 m | ring ridges on open ground |

The relief work before landforms (still in the engine): plateaus and escarpments with jagged, wandering edges (sheer in places, a walkable ramp in others); canyons whose width and depth vary along them; mesa steps that fade out up the flanks of real peaks; river channels that meander off their coarse polylines, with ragged gorge walls where they cut through high ground.

## Archetypes

Every archetype differs from every other on terrain shape, palette, sky, flora, hazard and resources.

| Archetype | Variants | Terrain (landforms; see the table above) | Palette and sky | Flora | Hazard | Signature |
| --- | --- | --- | --- | --- | --- | --- |
| Temperate | meadow, rainforest | plains, hills, ranges, steppe, river valleys, deltas, lakes, fjords on cold coasts; wetlands and karst in the rainforest | greens and teal seas under a blue sky | broadleaf, conifer, jungle trees | storms, floods | titanium, resin |
| Arid | dunes, canyon | dune seas and rocky desert; mesas, tablelands and stepped canyonlands, badlands, steppe | orange and red rock, turquoise water, dusty peach horizon, big blue moon | palms on oases, cacti, scrub | heat, sandstorms | titanium, gemstones |
| Ice | glacier, hot spring | ice plains, high ranges, fjords and ice cliffs; volcanic fields and lake districts at the hot springs | white and pale blue under an indigo and pink sky | ice shards, frost rock | cold, geysers | cobalt, sulfur |
| Lava | volcanic, lava ocean | volcanic fields of cinder cones under tall calderas, basalt plains, ash badlands; volcanic archipelagos | black basalt, glowing lava seas and rivers, red sky | lava rocks, obsidian | heat, eruptions | thorium, titanium |
| Alien | crystal, fungal | shard fields, ringlands, crystal plains and mesas; bulbous dome fields, spore marshes, karst | violet and teal under a ringed sky; orange and cream under lime | giant crystals; giant mushrooms | radiation, spores | crystal, resin |

Each planet also rolls 1–2 modifiers:
- **Terrain modifiers:** archipelago, highlands, cratered, mesas and canyons change the terrain. All but cratered push their landform into the world's mix (archipelago, mountains, mesas, canyonlands). Cratered scatters craters everywhere, and only on bodies that keep them (arid, ice and lava worlds, which it makes airless: `PlanetSpec.Craters`, `PlanetSpec.Airless`); a living world never rolls it. A war's blast craters are not terrain modifiers: they go on the war's targets (the POI generator's strikes).
- **Look-only modifiers:** ringed sky, perpetual storm and glowing nights change only the sky, clouds and night-side glow.

## Play it

From the repo root, in PowerShell:

```powershell
.\tools\godot.ps1 -Project prototypes/planet-generator -Play
```

| Key | Action |
| --- | --- |
| 1–5 | Archetype: temperate, arid, ice, lava, alien |
| V | Next variant |
| N | New seed |
| M | Reroll modifiers |
| S | Planet size: 8x4, 16x8, 32x16, 64x32 km |
| Tab | Flat map |
| L | Landform map (globe and flat map), with a key |
| Enter | Land near the part of the globe facing you |
| WASD, mouse | Fly (on the surface) |
| Shift | Fly fast |
| Space / Ctrl | Up / down |
| Esc | Back to orbit |

On the surface, the HUD shows the distance to the nearest deposit of each starter material.

## Test it

```powershell
.\tools\test.ps1 -Project prototypes/planet-generator -Scene res://tests/GenTest.tscn
.\tools\test.ps1 -Project prototypes/planet-generator -Scene res://tests/ViewTest.tscn
```

`tests/GenTest.tscn` runs headless in about 150 s and checks (`-- --only=Relief,Distinctness` runs just the named checks). `-- --only=Survey`, `-- --list=a:v:first:count` and `-- --world=a:v:seed` are tuning aids that print worlds' landforms. The checks:
- **Determinism:** identical map hash and identical fine terrain from the same spec.
- **Terrain ceiling:** over 140 worlds of every variant, Highlands included: nothing stands over 900 m, most ranges top out about 500 m, ground over 600 m is rare and the greatest peaks still reach 700 m, other landforms stay on the 500 m scale, and high ground carries ice.
- **Size classes:** the roll keeps to its odds over 200,000 seeds, moons stay small, start worlds are 16x8 km, and `Roll` without a width is still 16 km.
- **Sized worlds:** at all four sizes regions stay square and 1.6-4 km across, and about a fifth of worlds are one landform. At 8x4 and 64x32 km, landform patches run as far across as down, and plains, mountains and dune seas keep their character. The colour map and globe build at every size with no seam, and a whole 64x32 km world costs under 16 s.
- **Seamless wrap, both ways:** the step across each seam falls within the range of every other column (or row) pair. The fine terrain is continuous across both seams and at all four corners at once: height and normal differences shrink tenfold when the gap does.
- **Poles:** no wall. The polar band's steepest ground (95th percentile) is no steeper than the steepest other latitude band's, by 25%. Landform regions make some latitudes more rugged than others. The polar band is colder than the equator, and its temperature carries across the north-south seam.
- **Land fraction:** inside each variant's band over 20 seeds.
- **Rivers:** every river cell drains to the sea, and riverbank cells exist, on every variant. Some rivers cross the north-south seam, never climbing downstream, with their channel carved on both sides.
- **Starter materials:** all five within 300 m of any walkable spot and of landing sites (worst about 205 m and 180 m). Over 98% of deposits are on dry ground, none deeper than 3 m.
- **Performance:** a 16x8 km map well inside 2 s (114-142 ms on the development machine today, which ran main's code at 266-685 ms under the same load); a 2 m chunk of fine terrain in about 12.5 ms on one thread (main: 20 ms).
- **Relief:** see above; every variant must have its drama somewhere.
- **Landforms:**
  - **Same seed, same landforms:** layer hash, mix and 400 point queries.
  - **Seamless:** the blend changes by at most 0.001 across 0.1 m at either seam or a corner, as anywhere inland.
  - **World variety over 200 seeds:** 21% single-landform worlds, all 158 mixed worlds with 2-5 landforms, and every one of the 24 landforms on some world.
  - **Placement:**
    - Deltas: 14 deltas on 7 worlds, none inland, 12 with a real river ending in them.
    - Fjords only on cold coasts: 95th percentile temperature 0.36, against about 0.6 at a temperate equator.
    - Deserts in the dry places: drier than the rest of the land on 14 of 14 mixed-climate worlds, and the dune sea is the driest country on 10 of 10 arid worlds.
  - **Character:** each landform measurably what it claims (the table above).
  - **Flat land:** temperate worlds have 49% (meadow) and 57% (rainforest) of their land under 5° on average over six worlds each, the least 39% and 36%.
- **Globe:**
  - Same seed, same globe.
  - The wrap's columns match and normals turn no more across the seam than anywhere.
  - Each pole is one point with level rings round it.
  - The highest ground is under the clouds.
  - It builds in about 60 ms.
- **Distinctness:** a nearest-centroid classifier on colour and terrain features, trained on some seeds and scored on 100 unseen planets, names the archetype 100% of the time (it must stay at 95% or above).

`tests/ViewTest.tscn` runs headless and is the views' stability check. It lands, flies at boost speed north-east across the corner where both seams meet, returns to orbit, switches planet and lands again. It checks that streaming settles and that the main thread never stalls (worst frame about 9 ms). The seam-corner flight is flown three times and the stall check judges the best flight, printing all three: a machine busy with other work stalls some frames, but a real stall shows in every flight.

`tests/LandformAtlas.tscn` writes [atlas/landforms.png](atlas/landforms.png): one world per variant, its landform map beside its colour map, with the mixes in [atlas/landforms.txt](atlas/landforms.txt) (`-- --worlds=a:v:seed,...` for others).

`tests/Atlas.tscn` writes [atlas/atlas.png](atlas/atlas.png): 20 unlabelled flat maps (5 archetypes x 4 seeds, shuffled) for a human blind test. The answers are in [atlas/atlas_key.txt](atlas/atlas_key.txt); don't peek.

`tests/Screenshot.tscn` must run in a window:

```
-- --view=orbit|flat|surface|pole --archetype=0-4 --variant=0|1 --seed=N --out=file.png [--mods=Mesas,Cratered] [--hud=0]
   [--landforms=1] [--landform=Plains --face=Mountains --from=3000] [--limb=1]
```

`--landforms=1` tints the globe and flat map by landform. `--landform=X` stands the surface camera deep in a landform, and `--face=Y --from=metres` turns it towards another. `--limb=1` stands the orbit camera on the terminator with the longest range on the planet's edge. It also prints the frame time with vsync off, plus primitives and draw calls. On the development machine, the surface draws 0.65–0.95 M primitives (shadow passes included) in about 200 draw calls.

## Known limits

- **Lakes:** at sea level only. Lake districts, wetland pools and delta channels are basins cut below sea level, which fill with sea (or lava) at sea level. Elsewhere, pits drain through gorges.
- **Landform sizes:** regions are 1.6-4 km across, by world size (see [Planet sizes](#planet-sizes)). Small worlds have small countries.
- **Downstream sizes:** the size roll is tested here, in the generator. planet-surface, the slice and capital-base still build 16x8 km worlds and have not been run at the other sizes.
- **Volcanoes don't smoke:** this viewer has no particles, so the volcanic field shot shows cones and glowing lava but no plume.
- **Fine detail from orbit:** the globe and colour map smooth relief over a few 32 m cells, so very steep worlds (shard fields) look soft from orbit, not stepped.
- **Collision:** none. The camera only clamps to the ground. The heightfield is on a regular grid (1 m cells conceptually) and ready for a HeightMapShape3D and A*.
- **Time of day:** the surface is always day; glowing nights only show on the globe's night side.
- **Gameplay:** hazards and resources are labels and deposits.
- **Build settings:** the csproj sets `Optimize` even in Debug. The generator is tight numeric loops that run 3–5x slower unoptimised, which made streaming stutter in editor play.
- **Determinism across machines:** checked on one machine and runtime only. Seeds that must match across players need a cross-platform check of the trig in the cylinder mapping.
- **Low-end hardware:** not yet tested on real low-end GPUs. A low preset (fewer detail rings, no scatter shadows, merged MultiMeshes) is the obvious next step.
- **Engine noise:** none. All noise is our own, so FastNoiseLite's determinism doesn't matter.
- **Assets:** none. Everything, meshes included, is built in code, so there's nothing to credit.

## Integration

The generator's public API only grew. Nothing was renamed or removed, and downstream builds unchanged: planet-surface, slice and capital-base all compile.

**Already on this branch**, each in its own commit, to reconcile at merge:

| File | Change | Why |
| --- | --- | --- |
| `shared/Terrain/PlanetTerrainSource.cs` | `GeneratorId` is `planet-surface/2` | The same seed gives different ground, so old saves must refuse to load |
| planet-surface `tests/SurfaceTest.cs`, relief | The surface must keep the generator's own relief, and worlds with rugged country must show it | The old floors assumed every world was dramatic everywhere |
| planet-surface `src/Player/Walker.cs` | Horizontal acceleration is one vector, not per axis | Setting off at any heading but an axis ran at 45 degrees until one axis saturated: a 5.5 cm sidestep at 30 degrees, enough to glance off a trunk and slide round it. The collider was fine |
| planet-surface `tests/SurfaceTest.cs`, solids | The big boulder is sought over 150 m, then on the nearest rocky ground | Big rocks are a third of a pine forest's few rocks: none within 60 m of this forest stood a metre tall |
| planet-surface `src/Poi/PoiSiting.cs` | A half-buried ruin's tilt hinges on its way in | It hinged on the footprint's front edge, 4-6 m ahead of the door, so the ground stood 0.7 m over the sill. Both blocked ways in were this |
| planet-surface `src/Terrain/GroundPalette.cs` | Soil colours soften as the generator's do | The same square biome edges, on the voxel ground and the far rings |
| planet-surface `src/World/PlanetWorld.cs` | Snow lines lowered: temperate 620 to 380 m and 900 to 500 m, alien 800 to 500 m, and arid gains one at 640 m | They were set for ranges over a kilometre high |
| slice `tests/Driver.cs` | Walking to a seat never digs | The stuck rescue dug beside the hull, the dig woke the landed vessel, and Space on an awake vessel raises no `LiftedOff` |
| slice `src/Game/Game.cs` | The flight stick snaps to zero under 1e-4 | Its decay never reached zero, so two hops started on different inputs; the difference showed as 1 ULP of height. The check stays exact |
| slice `tests/SliceTest.cs` | The hunter spawns on dry ground | On a low shore it could spawn in the water creatures keep out of, and stand there |
| slice `tests/SliceTest.cs` | The pilot lets go once its feet are down; the landing check counts only damage from the flight; a plant over the sleeper's head counts as out of reach | The new ground is steeper at the start site. The sleeper can bump the parked Skiff, and the nearest plant can be up a bank |
| slice `tests/Playthrough.cs` | `PlaceBlock` clicks only with the ghost on the wanted face | On a slope the walker drifts, and a stray click spent a thruster's parts |

**Still to do:**

1. **Landforms for POI siting** (`planet-surface/src/Poi`). `map.LandformsAt(x, y)` (blend) and `map.Landform[cell]` (dominant) are cheap. Towns, bases and landing pads could prefer open landforms (plains, steppe, ice and basalt plains) and keep big pads off deltas, wetlands and fjord walls.
2. **Wildlife in the slice** (`slice/src/Surface/WildlifePatch.cs`). Roll the planet's wildlife for its landforms and spawn by home ground:
   ```csharp
   // SetWorld: species built for the planet's landforms, coloured like their ground.
   WorldMap map = planet.Map;
   var sums = new Dictionary<Landform, (Godot.Color c, int n)>();
   for (int k = 0; k < map.Count; k++)
   {
       if (map.Has(k, CellFlags.Water)) continue;
       var l = (Landform)map.Landform[k];
       Rgb c = BiomeLook.Color((Biome)map.Biome[k]);
       var (s, n) = sums.GetValueOrDefault(l);
       sums[l] = (s + new Godot.Color(c.R, c.G, c.B), n + 1);
   }
   var ground = sums.ToDictionary(e => e.Key, e => e.Value.c / e.Value.n);
   Fauna = new FaunaGenerator(Zone.Data.Wildlife.Fauna).Generate(planet.Spec.Seed, rules.Alienness, bloom: false,
       map.Model.Mix.Landforms, ground);

   // Populate: a group goes down only on its species' home ground. Deep in a region only that
   // landform's species; near a border either side's, as much as each holds there.
   LandformBlend here = map.LandformsAt(Mathx.WrapX(Origin.X + at.X, planet.Width), Mathx.Wrap(Origin.Z + at.Z, planet.Height));
   float w = s.HomeWeight(here);
   if (w <= LandformFauna.MinHome || (w < 0.98f && rng.Float() >= w)) continue; // try another spot
   // (or pick by the spot: Species s = LandformFauna.PickFor(Fauna, here, rng);)
   ```
   - **Spawner.** A `Spawner` built for the patch takes `LandformAt = p => map.LandformsAt(...)` and does the same.
   - **Species counts.** The slice's checks on species counts will move: a planet now has two or three species per landform.
   - **Kept.** The discovery card, base and town no-spawn zones, day and night, cave and POI fauna and domestic animals are untouched. No new popups.
3. **Far rings.** planet-surface's far rings use the source's heights directly, which is right. Any far mesh lowered to sit under detailed chunks must sink low ground less: deltas, wetlands and coastal plains sit at 1.7-3 m and would show under the sea, as the generator's own far ring did (fixed here).
4. **Biomes.** Landform hints use existing biomes only, so `PoiSiting` and `BiomeLook` need nothing.
5. **Cloud base.** The greatest peaks rise to 875 m, through the cloud layer's base at about 600 m. Phase 2 owns the clouds, and this is on purpose: they stand out of it.
6. **Size classes.** To use them, call `PlanetSpec.RollSized(seed, archetype, variant, kind)` where a world is rolled. The slice's start worlds pass `WorldKind.Start`. Worlds of 8x4, 32x16 and 64x32 km have not been run through planet-surface, the slice or capital-base: check streaming, POI placement (`PoiSiting.MaxSites` is fixed at 14 whatever the size) and landing first.
7. **The shared walker** (`shared/Physics/Core/WalkController.cs`, line 115). It accelerates per axis, as planet-surface's walker did. The fix is the same if the sidestep matters there. Not changed here: it is shared with every prototype.
8. **Lift-off after a wake** (`shared/Physics/Core/VesselController.cs`). A landed vessel woken by a dig or a ram, then flown, raises no `LiftedOff` and stays `Parked` in flight, because `TryWake` only runs while it is frozen or settling. Harmless today: `Parked` only matters with nobody at the controls.
9. **Fjord walls in planet-surface.** The streak fix here is in this viewer's terrain shader. planet-surface has its own, with strata already. Worth a look at its walls in the same light.
