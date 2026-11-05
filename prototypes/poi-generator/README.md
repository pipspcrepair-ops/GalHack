# Prototype: POI generator

Proves that GalHack can fill its planets with places worth finding. Some are hand-authored faction kits assembled into whole buildings from a seed. The rest are procgen places that an **architect** plans from a shared library: an airfield, a research base with sublevels, a tower that gets wrong as you climb, a bunker that gets worse with depth, an old mine, a hospital, a prison, a blast crater, alien ruins. Each layout becomes four sites (active, abandoned, infested by the Hollow Bloom, ruined). Each is dressed with loot from `shared/Items`, defenders and frozen moments, then scattered across a whole planet by themed placement.

Every result is checked: every room, storey and cache can be reached on foot, corridors are whole, nothing floats, and cores sit only where a faction holds the place.

The full list of places, built and still to come, is in [CATALOGUE.md](CATALOGUE.md).

![The ominous tower at dusk from 300 m](screenshots/far-ominous-tower-dusk.png)
![Six seeds each of a hospital, a prison block and a deep bunker](screenshots/contact-sheet.png)
![The ominous tower's floors side by side](screenshots/floors-ominous-tower.png)
![An active Vharn garrison](screenshots/garrison_active.png)

## Run it

From the repo root, in PowerShell:

```powershell
.\tools\godot.ps1 -Project prototypes/poi-generator -Play
```

You start in the air over a planet patch.

| Key | Aerial | Inspect | Walk |
| --- | --- | --- | --- |
| 1 / 2 / 3 | | switch to Aerial / Inspect / Walk | |
| WASD, mouse | fly, look | orbit (mouse or arrows), wheel zoom | walk, look; climb ladders |
| Space / Ctrl | up / down | | jump / hold to crouch |
| Shift | fast | | sprint |
| Enter | inspect the place you're looking at | | |
| F | | next family | |
| V | | next authored layout in the family | |
| N | | a new layout from a random seed | |
| S | | next state: active, abandoned, infested, ruined | |
| R | | next rarity: common to unique | |
| E | | | use: take a keycard, break a cracked wall, call or ride a lift, open a cache |
| L | | | head torch (on by default except when active) |
| Tab | | layout map | layout map |
| Esc | free the mouse | | |

## How it works

Everything under `src/Gen` is plain C# with no Godot types, and deterministic from a seed (its own SplitMix64 and FNV hashing). `src/View` is the Godot side. The grid is one metre a cell. A storey is 4 m: a slab and 3 m of headroom. The ground floor walks at y 0, and each basement level is 4 m further down.

### Two generators, one format

- **Kits** (`Assembler.cs`): hand-authored faction modules joined socket to socket. The Vharn, Meridian, Freeholder, Kessari and Precursor families use them.
- **The architect** (`src/Gen/Arch`): plans a building from a **program** (`programs.txt`), or composes a **site** from parts (`sites.txt`). A **style kit** (`kits/*.kit`) re-skins either one.

Both produce the same `Layout` (blocks, modules, doorways, slots), so fixtures, states, dressing, the validator and the mesher are shared.

### The architect

1. **Massing.** A footprint by type and seed, beyond the rectangle: L, T, U, H, cross and courtyard, built from bars with wings. Basements have their own shape: the same, the main bar only, or a box. Towers shed corners as they climb, leaving terraces. A building has a stair core, an optional lift, and a second stair on long plans.
2. **Circulation per storey.** Each storey picks its own pattern from the program's weighted list:
   - a spine, or a jogged spine
   - a plaza (rotunda)
   - a loop: a full ring, a horseshoe or a figure of eight
   - an atrium, with galleries round a void
   - a courtyard ring
   - an open hall
   - an enfilade, with rooms opening through rooms
   - mine drifts with stopes

   A pattern already used on another storey is less likely again. A storey whose plan signature matches another storey's is planned again. Shape follows purpose: hospitals and prisons spread their wings, towers climb, bunkers and mines go down.
3. **Rooms.** The space left over is cut into rooms by recursive wall splits. Each room gets a purpose from the program's demands (`rooms ground lobby! nurses ...`) and from the room library (`rooms.txt`). The library gives each room its size, props, adjacency, and a deco set for the decorations library. Slivers become hidden rooms behind breakable walls, each with its tells.
4. **Doors and locks.** Every room reaches a corridor, either directly or through a room it belongs with. Some doors need a keycard, and the keycard always lies on the reachable side of its lock. The vault sits on the deepest level. A program's must-have rooms (`lobby!`) are guaranteed: a plan that leaves one out is planned again.
5. **Verticality.**
   - Stairs, with 3 cells of headroom over every step.
   - Ladders, and lifts with cars.
   - Holes where a floor fell through. They are warned.
   - Collapse breaches from the surface, down a rubble slope.
   - Caverns broken into from the end of a corridor.

   The terrain stamp opens every underground room in the planet's voxels.
6. **Ominous.** Wrongness rises with depth and height (`Decay.cs`):
   - Lights fail level by level: working, then flickering, then red emergency lamps, then dead.
   - Fog thickens, and the Bloom creeps in at the bottom.
   - Puddles appear, and walls bruise.
   - Sound hooks are placed: drips, hums, knocks, wind.

   From outside, one high window is still lit and a thin column of smoke rises. A place with no window keeps a work lamp burning over its door.
7. **Checked, then regenerated.** `LayoutCheck` checks every layout before it is used:
   - Corridor cells have a floor, walls, a ceiling and 3 m of headroom, and are two cells wide.
   - Every doorway sits in a wall, with floor on both sides.
   - Every stair has its headroom.
   - Every room and level can be reached on foot.
   - Hidden rooms open only when their wall breaks.
   - Breaches lead in.
   - Nothing floats.

   A layout that fails is thrown away and planned again from a new sub-seed, up to 24 times. `Architect.Stats` counts the generated, failed and regenerated layouts.

### Places, states and loot

- **Cores** appear only where a faction owns the place (`FamilyDef.HasCore(state)`). Wrecks, ruins, the old world's abandoned places and gang forts never have one.
- **States**:
  - *Active*: power and guards.
  - *Abandoned*: dead lights and wildlife.
  - *Infested*: the Hollow Bloom.
  - *Ruined*: holes and rubble.

  Cables and Bloom tendrils always hang from something. A ruin that lost its ceiling also loses what hung from it.
- **A century or two after the fall.** Each place has an age, and each room an exposure, for loot, dressing and the decorations library's disorder pass:
  - `PoiInstance.Age`: new (worked today), old-world (long abandoned) or preserved (frozen or airless).
  - `AgeOf(module)`: a sealed vault, cryo bay or hidden room keeps its own age.
  - `ExposureOf(module)`: indoors, outdoors, or breached (open to the sky).

  Old-world places hold no food outside sealed rooms. Unsheltered timber has rotted and fallen in, while concrete, stone and steel stand as shells. Trees, many of them mature, grow through courtyards and breaches.
- **Repair after decay.** Anything left floating comes down. Every spawn is then flood-filled from the way in, and rolled again from a new sub-seed if decay cut the way to the vault or the core.
- **Loot** comes from real `shared/Items` ids (`loot.txt`). The cache tier rises with rarity, depth and wrongness, and is higher for vaults, hidden stashes and infested places.
- **Frozen moments** replace text logs: an abandoned meal, welded doors scratched from the inside, drag marks leading deeper, a last stand by the core. Terminals are props.
- **Gangs** belong to no faction. Their camps and forts get spawn and patrol markers, and never a core.

### Placement

- **Themes** (`themes.txt`): each kind of world has a weighted themed set and a share of outliers from outside it. Outliers are rarer and more ominous. Every planet gets at least one outlier, and none gets more than its theme's maximum. `WorldPlacement` scatters places over the whole planet (a torus) by Poisson disc.
- **Start worlds** (`ThemeParser.For(..., start: true)` or `ThemeDef.AsStart()`): no faction holds ground. Faction places appear only as ruins, with no core and no defenders.
- **Space** (`orbit.txt`, `OrbitPlacement`): places in space, as data, sited per system. A start system gets plenty of wrecks close to home, with danger 2 at most and no live faction.
- **Siting by purpose**: each family names the ground it wants (`siting`: peak, ridge, hillside, valley, flat, open or any), how high its landmark stands (`rise`), and its importance tier (`tier`).
- **Tech level**: a theme names its world's tech level (`tech low|mid|high`). Families with layouts per tech level (factories, farms) pick that level's layout.

## Authoring a kit module as data

A kit is a palette plus modules. A palette line is `symbol type material colour[|colour...] role`; several colours make a patchwork, picked per cell. A module is a stack of ASCII layers, **bottom first**; in each layer rows run north to south and columns west to east, framed by `|` so trailing spaces survive editors.

```
kit vharn
faction vharn
light ffa040
palette
  #  cube   chitin  2a1328|301630   wall    plum-black carapace plates
  _  cube   resin   2a1824          floor
  =  cube   chitin  381a34          roof
  *  cube   lumen   ffa040          light   amber resin lamps
  o  rib    bone    d9c8a0          wall    bone ribs

module vh_gullet
  kind corridor          # entrance, corridor, room, hall, vault, tower or descent
  weight 2               # how often the assembler picks it
  layer fill _           # the floor: _ under every column any grid layer uses
  layer x3               # this grid three times: walls 3 m high
  |#DD#|
  |o..o|
  |#..#|
  |o..o|
  |#DD#|
  layer
  |====|
  |=**=|
  |====|
  |=**=|
  |====|
```

Reserved symbols: space is outside the module, `.` air inside it, `D` a socket (a doorway; a connected rectangle on one outside face, 2 wide and 3 high so all kits fit together), `X` the way in (entrances only), `$` loot, `T` terminal, `C` core, `!` enemy spawn, `B` boss, `?` story prop, and `^ v < >` ramps rising that way (made of the palette's `ramp` entry). Block types are the shapes the mesher knows (`cube`, `ramp`, `pillar`, `rib`, `pane`, `lattice`, `spike`, `pod`, `machine`, `crystal` and more); materials and roles are listed in `src/Gen/Blocks.cs`. Roles and materials matter: ruin never removes `floor` blocks, and losing power darkens every `lumen` block (lamps, lit windows, eggs, cryo cradles) while `glyph` and `crystal` keep glowing.

A family (`data/families.txt`) names its kits, rarity, biomes, territory, near and avoid rules, spacing, reveal distance and state weights, and 3 to 6 layouts, each a fixed seed and a recipe:

```
variant Brood Spine
  seed 1101
  path entrance corridor room corridor hall vault
  branches 1-2 room tower corridor>room
  grow corridor room
```

Steps can repeat (`corridor*1-2`) and come from a second kit (`deep:hall`). Change a module, run the tests: every layout of every family is generated in every state and rarity and validated.


A family names a `style` to re-skin its program or site. That is how the alien flavour works: `alien_cathedral` is the tower program built in the `alien_cathedral` kit.

## Integration notes (planet surface and the slice)

`prototypes/planet-surface/src/Poi/PoiSiting.cs` places POIs on a real planet and seats them in its terrain. The results are in `PlanetWorld.Pois`.

- **A read-only API for the infrastructure network.** Each `PoiSite` exposes:
  - `Center`, `Footprint`, `Stamp` and `Rotation`. Rotation is in quarter turns; it is always 0 today, because places sit as generated.
  - `Gates`: the main entrance, fence gates and collapse breaches. Each gives its world cell and its way out (`PoiGate.OutX`, `PoiGate.OutZ`).
  - `Tier`: city, spaceport, town, base, outpost, camp, site or wreck.
  - `FactionHeld`: its core is live or its faction is home, so its own infrastructure works.

  Approach stamps stay local to each place: the pad, its apron, and the blend back to natural ground.
- **Start worlds.** Set `PoiSiting.StartWorld = true` before building a start world, or pass `startWorld: true` to `PoiSiting.Place`. Then no faction-held place and no core is sited.
- **Composable stamps.** `TerrainStamp.Compose` is the one height rule. In list order, each stamp eases the height so far toward its own target, by its weight. Overlapping pads meet smoothly, and a road or rail stamp laid later wins wherever its weight is 1. Order the list by what should win. `TerrainStamp.Apply` is the step for a single stamp.
- **Dig anywhere.** Stamps are shape only. Faction places lay armoured foundations (`Material.Armour`), so tunnelling in is slow but a valid way in.
- **Far rings.** `FarBuilder` samples `SurfaceHeight`, which already includes the stamps, so pads show in the distance. Underground carves and crater bowls below the pad don't show in the far heightfield. To show a crater there, the far ring's height sampling needs its bowl (a stamp target below the pad), not only the voxel carve mask.
- **Screenshots and caps.** `PoiSiting.ForceFamily` seats one family near the landing site, for screenshots. `MaxSites` caps how many places are seated: the nearest half first, then an even spread to the far side.

## Test it

```powershell
.\tools\test.ps1 -Project prototypes/poi-generator -Scene res://tests/PoiTest.tscn
```

The tests run headless and exit 0 or 1. Add `-- --only=Name,Name` to run only the named checks. The suite covers:

- **Data**: kits, factions and families; the shared library (rooms, programs, sites, style kits, themes); the five-step rarity ladder; and loot ids checked against `shared/Items`.
- **Validation**: determinism; 500 fresh kit layouts; every authored kit layout; every named architect layout in three rarities and four states; and 1600 fresh architect layouts. The run reports how many failed the layout check and were regenerated. None is placed broken.
- **Corridors and sense**: the geometric corridor rule on every corridor cell and doorway, room adjacency, and stairwells that line up storey after storey.
- **Variety**:
  - Within a type, no two seeds give the same plan, and the commonest coarse topology covers at most 25% of a type's seeds.
  - No two storeys of a building share a plan signature.
  - Pairs of types that must never walk alike share under 50% of their footprint and circulation mix.
- **Places**:
  - Cores exactly where a faction holds the place.
  - State transforms.
  - Loot rises with rarity and with depth, and vaults and hidden stashes beat the side caches.
  - Underground levels are carved.
  - Hidden rooms open only when broken, and each has its tells.
  - Collapse breaches lead in.
  - Keycards lie before their locks.
  - Cables and tendrils always hang from an anchor.
  - Programs keep their must-have rooms.
  - Old-world places hold no food outside sealed rooms, and trees grow through them.
  - Factories and farms match their world's tech level.
- **Placement**: biome, territory, near and avoid rules; regrowth and reveal; themed sets with outliers in range; whole-planet cover; start worlds with no faction ground; and start systems full of wrecks.
- **Distance**: each family's landmark rises as high as its purpose wants, and something on it is lit.
- **Performance**: generate, validate and mesh 100 POIs (printed, with a ceiling).
- **Walks**: a physics body walks from outside to the deepest point and back. It does this in the airfield, the research base, the tower (top and bottom), the deep bunker, the old mine, and to a Vharn garrison's vault.

The planet surface's `SurfaceTest` checks the seated places:

- Corners sit at floor level.
- No ground is missing under rooms, and no block is buried.
- Every way in is open, and every gate can be walked up to over the stamp.
- Seating is deterministic.
- Places spread to the far side of the planet.

### Screenshots

Run screenshots windowed, and add `--position 20000,20000` to keep the window off-screen:

```
tests/Screenshot.tscn -- --view=<view> --out=file.png
```

The views are:

- `inspect:<family>:<variant>:<rarity>:<state>:<seed>:<yaw>:<pitch>:<distance>`
- `walk:...:<storey|deep|top>[:<along>]`
- `ground:...:<distance>:<yaw>`
- `tell:...`: a hidden room's cracked wall
- `breach:...`: a collapse breach from its lip
- `contact:<families>:<seeds>`
- `floors:<family>:<seed>`

The planet surface adds `far:<family>:<archetype>[:<variant>]:<day|dusk|night>`, which shows one family from 300 to 420 m away at eye height.

## Known limits

- Space places are data and siting only. They have no layouts and no orbit play.
- Hybrids, layering rules and repurposing between programs are not in data yet. The only combinations are flavour (a style kit over a program) and kit layering (a mine that turns Precursor further down).
- Power lines between poles are not drawn. Hanging cables follow the one-anchor rule.
- Sockets on kit modules are all 2 wide and 3 high.
- Figures are static stand-ins. Combat and AI belong to other prototypes.
- There is no audio. Sound hooks are markers for the slice to play.
- Data files are read with `FileAccess`, so an export must include `*.kit` and `*.txt` in its filter.
- The generator is built optimised even in debug (`<Optimize>true</Optimize>`).
