# Prototype: terrain digging

Proves that GalHack's deliberately limited digging is rock-solid, fast on modest PCs and fun. The genre's usual voxel troubles are terrain growing into things, holes that differ between players, saves that lose edits, and cracks at chunk seams. Here each one is ruled out by design and checked by a headless test.

![A fresh landscape](screenshots/landscape.png)
![A dug pit exposing an ore vein](screenshots/pit.png)
![A test crater, mid-blast](screenshots/crater.png)
![Two blocks set into a slope, each in the notch it cleared](screenshots/block.png)

## The rules, and how they hold

| Rule | How |
| --- | --- |
| 1. Remove only, 1 m cells | Edits are a per-chunk set of removed cells (a 4096-bit mask) over the seed-generated base. Nothing can add to the base, and the set only grows. |
| 2. Shallow | The generator puts bedrock 30 m below the original surface. Bedrock refuses every kind of edit. Ore sits 1.2–10 m down. |
| 3. Structures and POIs anchor the ground | Each block or POI registers an anchor box: its footprint plus 3 m around, 6 m below and 3 m above. Tools and craters are refused inside it. |
| 4. Building clears terrain | Placing a block removes every cell inside it, then anchors the ground around it. A block can't cut bedrock or a POI's ground. |
| 5. No terrain physics | There is none. A carved-free island floats forever. |
| 6. Static water | One flat plane, masked to the columns whose *natural* surface is below water level. Digging never changes the mask, so a pit beside a lake stays dry. |
| 7. Stuck-safe | A stuck guard samples the density field at each body's core every physics tick. Anything inside the ground for two ticks moves to the nearest spot with standing room, preferring up. |
| 8. Edits as differences | `EditLog` is a canonical, checksummed diff (about 1.2 bytes per dug cell). Removal sets are grow-only, so diffs can arrive in any order or twice and every player still converges on the same holes. |
| 9. Caves only from the generator | Two winding generator caves; players have no carving tool. |
| 10. Crashes and liftoffs | `ChunkStore.Crater`: a sphere centred above the ground cuts a capped bowl (≤ 6 m, ≤ 900 cells). It's remove-only and respects bedrock and anchors. |
| 11. Atomic, audited saves | Write `.tmp`, flush, read back and verify, then an atomic replace that keeps `.bak`. Loading rebuilds on a separate store and audits every cell, the counts, the structures and a state fingerprint before swapping in. |
| 12. A grid world | `ChunkStore.IsSolid(cell)` is the whole query plain A* needs. |

## How it works

**Terrain core** (`src/Terrain`, pure C#, no engine types, deterministic):
- `ITerrainSource` / `SimpleTerrainSource` is the seeded generator, behind the interface the planet-generator will implement. It is built only from integer hashing and exact float ops, so every machine generates identical cells. `GeneratorId` is saved, so edits never load onto different terrain.
- `ChunkStore`: 16³ chunks of 1 m cells. All the rules live here and return clear verdicts: `Removable`, `AlreadyAir`, `Bedrock`, `Anchored` or `OutsideWorld`. Masks are copy-on-write, so mesh workers hold an exact snapshot without locks.
- `SurfaceNetsMesher`: Surface Nets over cell-centre densities, with one-cell padding from the 26 neighbours. I chose it over Marching Cubes because it has no ambiguous cases or tables and never produces cracks or slivers. A dug cell becomes a softly chamfered hole that blends into natural slopes. Vertices are in world space, so the two sides of a seam compute bit-identical floats.
- `EditLog`, `TerrainSave` and `StuckResolver`, as described above.

**Presentation** (`src`):
- `TerrainView` snapshots each dirty chunk's neighbourhood (just references), meshes it on a worker, and swaps finished meshes in under a 2 ms frame budget. Collision is split into eight hashed 8³ blocks per chunk, each on its own body, so a dig only makes Jolt rebuild the blocks it changed. That was the one expensive main-thread step.
- `GatherTool` aims by walking the cell grid, not the collision mesh, so it always targets the real terrain even mid-rebuild.
- The look is a 1970s paperback after Chris Foss: toon light, lavender shadows, orange soil, banded rust rock, ore in a glowing aqua nothing else uses, bedrock with a faint cell grid, and a ringed giant over the rim mountains.
- Sound is synthesised in code at startup (`Sfx.cs`): filtered noise and decaying sines. There are no audio files, so nothing to license.

## Play it

From the repo root, in PowerShell:

```powershell
.\tools\godot.ps1 -Project prototypes/terrain-digging -Play
```

| Key | Action |
| --- | --- |
| WASD, mouse | Walk and look |
| Shift | Sprint |
| Space | Jump; hold in the air for the jetpack (climb out of pits) |
| Ctrl | Crouch: smaller capsule, lower eye, slower. You stay down until there is room to stand. |
| Left mouse (hold) | Dig the cells under the crosshair. Ore goes to the counter. |
| Right mouse | Place a 2 m block: clears its notch, anchors the ground around it |
| C | Test crater (crash or liftoff) at the crosshair |
| Q | Ore detector ping: marks nearby deposits through the ground |
| F5 / F9 | Save / load (atomic, audited). Saves go to `saves/`, which is git-ignored. |
| F8 | Wipe every chunk mesh and collider, regenerate from seed + edits, and confirm every mesh hash is identical |
| B | Chunk borders; chunks flash yellow as they're remeshed |
| H | Hide the controls |
| Esc | Free the mouse |

The crosshair turns red on bedrock and orange on anchored ground.

## Test it

```powershell
.\tools\test.ps1 -Project prototypes/terrain-digging -Scene res://tests/DigTest.tscn
```

`tests/DigTest.tscn` runs 55 checks headless in about 17 s and exits 0 or 1:
- **Rules 1–6, 9 and 10:** remove-only under 1000 random edits of every kind (with exact accounting); bedrock depth and refusals; anchors under, beside and below blocks and the POI; blocks clearing their volume; capped craters; dry pits beside the lake; generator caves; a floating island.
- **Rules 8 and 11:**
  - 400 edits replicated to two separate instances, one serialised and one shuffled with duplicates, give identical cell and mesh hashes.
  - Logs and saves round-trip byte for byte.
  - A simulated crash after the temp write but before the swap leaves the old save intact, and it loads.
  - The audit refuses a flipped bit, a truncated file, a save that silently lost a chunk of edits, an edit on bedrock, and a buried structure. With a corrupt save and a good backup, loading falls back to the backup and says so.
- **Mesher:** shared seam vertices around an 8-chunk corner match bit for bit; no open edges across seams; winding agrees with normals.
- **Scene:**
  - Stuck rescue for a player and a crate (2 physics frames), and no false rescue while standing.
  - Crouch: the capsule shrinks, you can't stand under a 1 m overhang, a crouched player in a crawlspace is left alone, and a buried crouched player is rescued to standing room.
  - The keys end to end: dig, place, F5, dig into a fresh hole, F9, rescued. Then crater and detector.
  - F8: all 2048 chunks identical, and nobody falls while the colliders are rebuilt.
  - Procedural sound levels.
- **Performance:** 1000 random digs through the real pipeline, 10 per frame.

Typical numbers on a Ryzen 7 5800X3D:

| | |
| --- | --- |
| Surface Nets remesh, one thread | avg 0.22 ms, max 0.4–0.6 ms per chunk (~550 triangles) |
| Remesh job on the workers | avg 0.75 ms. A handful of 1000s go over 5 ms when the OS descheduled a worker or the GC paused it. |
| Mesh swap-in on the main thread | avg 0.5 ms (render 0.08, collision 0.45) |
| Worst frame of terrain work | 3.4–5.4 ms at 10 digs per frame, which is far more than a player can do |
| Startup | 462 chunk meshes, 246k triangles in ~2 s headless, ~4.5 s windowed (behind a loading message) |

`tests/Screenshot.tscn -- --view=landscape|pit|crater|block --out=file.png` renders a view (windowed). `--view=map` writes a top-down layout map headless; `--debug=1|2` shows normals or unlit albedo.

## Known limits

- The world is a fixed 256 × 256 × 128 m box with rim mountains; streaming chunks in and out is future work.
- Collision rebuilds still cost about 0.45 ms per chunk on the main thread. Fine for a player digging; a heavy multiplayer dig-fest would want collision near bodies prioritised, or Jolt shapes built off-thread.
- Occasional worker and frame spikes are GC pauses from per-job array allocation; pooling those arrays would remove most of them.
- Blocks only clear and anchor terrain. There's no removing or stacking them, and placement checks the player's body but not crates or creatures.
- A verified `.tmp` left by a crash is ignored on load rather than recovered. Recovering it is a safe follow-up.
- Water is visual and static: you walk on the lake bed, and there's no swimming.
- The generator's ore and caves are tuned for this map; the planet-generator replaces `SimpleTerrainSource` wholesale.
- The HUD is a debug overlay, not the diegetic UI. Single player only, though edits are already diffs, ready for networking.
