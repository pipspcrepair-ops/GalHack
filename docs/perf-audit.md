# Performance audit (13 Nov)

Where the frame and the loading time go, measured on proto/perf (from proto/batch2 9b324a8), windowed, 1080p,
on a machine shared with up to four other Godot runs. Wall-clock frame times are noisy; counts (draws,
primitives, nodes, bodies, allocations), the renderer's own CPU and GPU timers and the main thread's own CPU
time per system are not. Numbers to re-measure on a quiet machine are marked (q).

## How it was measured

- `PlacePerf --audit=1` (tests/PlacePerf.Audit.cs): a fresh world with a city (CityPlan.Force); the castaway
  jumps to open country (the spot furthest from any place, looking at the horizon), a town (from 30 m off its
  edge), the city's middle at eye height looking along the street with the most tower along it, and over the
  city's edge from 120 m up. Per scene: how long each kind took to come in after the jump, then 240 frames.
  `--scenes=`, `--toggle=occlusion,loadbudget` (a fix off, then on, in the same scene), `--bodies=pieces`.
- `SystemSplit` (tests/SystemSplit.cs): the main thread's CPU time and allocations by system with nothing
  switched off (marker nodes between the zone's, the game's, the wildlife patch's and each place's children).
- `AllocCensus`: what is allocated, by type, on every thread (the runtime's allocation sample).
- `PopTest --report=1`: the walk, bike and fly routes (now also primitives, objects, shadow draws,
  allocations, the split, and the nearest and median distance each kind of change arrived at).
- City planning differs between runs of one seed (noted in the playtest list), so the city scenes are only
  compared within one run (`--toggle`) or by counts; the street picked differs run to run.

## The frame, by scene (before)

| scene | frame mean | render CPU | GPU | draws (shadow) | primitives (shadow) | nodes | script |
|---|---|---|---|---|---|---|---|
| open country | 4.6 ms | 1.2 | 0.8 | 416 (332) | 1.2 M (0.9 M) | 15.5 k | 2.1 |
| town (2 places) | 3.9 | 0.8 | 1.0 | 537 (31) | 1.4 M (0.5 M) | 19.7 k | 1.9 |
| city street | 198-286 | 18-29 | 8-10 | 2.5-4.2 k (9-12 k) | 5.5-6.5 M (29-36 M) | 85-103 k | 165-236 |
| city from the air | 144-154 | 12-15 | 9-10 | 6.1-7.3 k (0.7-9 k) | 7.8-8.1 M | 62-76 k | 120-127 |
| walk route | 12.7 (p99 34) | 3.1 (p99 7) | 3.5 | 2.6 k | | | |
| bike route | 20.3 (p99 75) | 4.3 (p99 12) | 3.9 | 3.3 k | | | |
| fly route (over a city) | 27.8 (p99 109) | 7.3 (p99 63) | 3.4 | 2.5 k | | | |

GPU is never the limit (under 10 ms everywhere, (q)). The city is CPU-bound, and almost all of it is script.

**The city street, by system** (main-thread CPU ms a frame, 51-84 places standing, 180-230 people, 60-160 creatures):

| system | ms | what it is |
|---|---|---|
| towns' people (Place/Townsfolk ticker) | 45-70 | Town.Tick for every standing place, every physics tick |
| wildlife patch | 50-67 | WildSim.Step: Locomotion.Move 44-52 (KeepOff loops over all 200 people per creature) |
| places' creatures (Place/DeepSite) | 22 | each place's own WildSim stepped every tick |
| places (other) | 25-30 | Place._Process, lights, near meshes, dressing (each with its own 1.5-2.5 ms budget) |
| render CPU | 18-29 | 2.5-4 k draws, 9-12 k shadow draws (sun shadows to 1500 m, 4 splits) |
| everything else | under 10 | streaming 0.4, POI layer 1.5, infra 0.2, physics host 2.9, GC pacer 1.4-2.9 |

**Why it is 200 ms and not 25: the physics tick spiral.** The town, wildlife and place sims run in
`_PhysicsProcess`. At 60 ticks a second a slow frame owes several ticks; the engine runs up to 8 a frame, so
everything that runs per tick costs 8 times over. Measured: 7.98 ticks a frame in the city street and the air.
Per tick the sims cost about 17-20 ms (people 5.7, wildlife 6-7, place creatures 2.7, the rest about 3), more
than a tick's 16.7 ms, so the frame runs away to the cap. Anything that brings the per-tick cost under about
8 ms brings the frame back from 200 ms to the 20s; nothing on the render side matters until then.

Also seen in the city: 42-59 k collision shapes (blocks, 6-9 k dressing pieces with bodies, people), 1356 omni
lights in the POI layer (3 a city building, 20 a lone place) with 0 shadowed, 1.0-1.4 MB a frame allocated on
the main thread (4.9 MB on all threads), 80-90 gen0 and 1 gen2 collections in 240 frames.

**Open country and towns** are inside the budget: 4-9 ms. 133-150 KB a frame allocated on the main thread
even standing still: TerrainStreamer 85 KB (a closure made for every ring column and far chunk every frame),
InfraLayer 22-26 KB (a closure for every tile round the camera and a LINQ sort every frame), the machines'
`Constructor.Current` (LINQ over three job lists, asked every tick); 3.5-7 MB a frame on the workers.

## Loading: from "should be there" to "is there"

After a jump (scene settled = streamer settled, no place waiting for merged blocks, places idle for a second):

| scene | ground | places' merged blocks | places stood up |
|---|---|---|---|
| open country | 3.1-8.5 s | 1.0 s | - |
| town | 5.6-9.4 s | 1.0-1.9 s | 2.1-4.9 s (0.6-1.7 s a place) |
| city street | 15-30 s | 1.0 s | not idle after 90 s (51-84 standing, 1.1-1.5 s a place, worst 3.6-8.6 s) |
| city air | 0 s | 39-77 s (one place at a time) | 22-31 s |

What is queued behind what:
- Places' merged blocks: one place at a time on one worker, the next picked every 12 frames (0.2 s at 60 fps,
  2.4 s at 5 fps), and a city tile's far mesh waits for every one of its buildings: a city from the air took
  39-77 s to get its merged blocks.
- Places stood up: one place at a time (meshing on a worker, then 2.5 ms a frame of collision, fixtures,
  caches, dressing, creatures, people); 60+ city buildings within 320 m at about 1.5 s each is 90 s or more.
  The slowest single steps: dressing 27-33 ms, caches 43 ms, blocks 7-12 ms (hitches over 33 ms).
- Each standing place then loads its own full-detail cubes (2.5 ms a frame) and its rooms' dressing (1.5 ms a
  frame), and each place's creatures their bodies (1.2 ms a frame): budgets per place, so 60 places at once
  spend up to 300 ms a frame between them.
- Ground: the streamer's 3 ms a frame upload budget is per frame, so at 5 fps the ground takes 15-30 s.
- Routes (PopTest, busy machine): every change arrived as a fade, 0 pops; on the fly route full detail came
  in 45 times and went 45 times; far chunks built on the way 164 (walk), 775 (bike), 568 (fly).

## Not measured again here

- The crossing hitch (57-288 ms, sky tracking 72 ms) and a moon's streamer set-up (18-24 ms) are OrbitTest's
  and MoonTest's numbers from the playtest list; not re-run in this pass (q).
- Far plants drawn with their near meshes (7-10 ms on bike and fly) and the long shadow distance (render CPU
  about 10 ms): not separated out here; the shadow pass is the shadows agent's.
- Monsoon rain: not run.

## Ranked fixes

| # | fix | expected gain | risk | files |
|---|---|---|---|---|
| 1 | Places' world origin kept as they are moved: a person's or creature's look at a place (`Person.Position`, `SolidAt`, `ClimbAt`) no longer calls the engine; 200 people x 160 creatures a tick | wildlife 6-7 ms a tick to about 1-2 | low | slice Places/Place.cs, PoiPlaces.cs |
| 2 | One loading budget a frame shared by every place's stand-up, cubes, dressing and creature bodies | 20-100 ms a frame in a city while it loads | low | planet-surface Fx/LoadBudget.cs, slice Places, Surface/DeepSite.cs |
| 3 | Far towns tick less often (as far people already pose less often) | people 5.7 ms a tick to about 1-2 | medium (people's timing) | slice Places/Town.cs |
| 4 | Several places' merged blocks built at once (3 workers) | city merged blocks 39-77 s to under 10 s | low | planet-surface Poi/PoiLayer.Tiers.cs |
| 5 | Occluders from places' merged blocks (whole opaque faces only) | draws behind buildings (q) | medium (anything wrongly hidden) | planet-surface Poi/PoiLayer.Occluders.cs |
| 6 | Creatures drawn as one skinned body (as people are), not a node a piece | 27 meshes to 1-3 a creature; 4-6 k fewer nodes in a city | medium (look: the pattern rides on CUSTOM0) | shared/Bestiary View |
| 7 | No per-frame closures in the streamer and infra loops; no LINQ in Constructor.Current | 110 KB a frame less garbage, fewer gen0 | low | TerrainStreamer.cs, InfraLayer.cs, Constructor.cs |
| 8 | City buildings stand up in two stages (drawn and dressed far, colliders, creatures and people near) | the 60-place stand-up queue | high | slice Places (design) |
| 9 | A cap on physics ticks a frame (8 now) | stops the spiral at any load (the game slows instead) | owner's call | project.godot |
| 10 | Far plants' low mesh simplified from each plant's own mesh (mesh LODs) | 7-10 ms on bike and fly (q) | medium (look) | TerrainStreamer, shared/Flora |
| 11 | City lights: 1356 omni lights kept in the scene at every distance | render CPU (q) | low | PoiLayer.Tiers.cs |

Trades waiting on the owner: 9 (slow motion under extreme load rather than a frozen frame).
