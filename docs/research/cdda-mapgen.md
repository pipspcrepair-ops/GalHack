# How Cataclysm: DDA generates its maps, and what GalHack should take

Research note, November 2026. It's based on CDDA's master branch (docs, C++ and data), its pull requests and its forums. CDDA is CC BY-SA 3.0, so everything here is described in my own words. Nothing has been copied; identifiers are named only so you can find them.

## Summary: the five most useful ideas

1. **Plan coarse, build fine, treat the plan as law.** CDDA decides what goes where on a coarse grid (the overmap) long before any wall exists. Local maps are built from that plan plus a seed. GalHack needs the same: a planet plan of cells, routes and sites that the voxel builder obeys.
2. **Grow complexes from typed sockets in phases, then cap every open end.** Mutable specials join pieces only where face sockets match, then grow in ordered phases. A final phase closes every loose end, and a test places each special thousands of times. That's the route to zero broken corridors.
3. **Networks are a spanning tree plus a few loops over a terrain cost field, with a discount for reusing routes.** Bridges fall out of the costs. Trunk highways come from a separate jittered world grid, reserved before towns are placed.
4. **Shells with standard door positions, plus interchangeable room chunks.** Room chunks are named by size and door side. Style comes from palettes and parameters rolled once per scope (site, stack, room), so a building stays consistent.
5. **Decay is independent state axes rolled once per site, plus post-passes that read what a tile is, not its ID.** A burn covers the whole building, and stairs always survive.

## 1. The overmap

**How CDDA does it.** The world is an endless grid of overmaps, each 180 × 180 cells ("OMTs") and 21 levels deep. Each cell becomes a 24 × 24-tile local map when the player gets close. Overmaps generate on demand and read their neighbours so roads and rivers continue across edges.

The pass order in `overmap::generate` is:

1. rivers, lakes, oceans, forests, swamps and ravines
2. reserved highway corridors
3. city seeds
4. interchanges
5. city streets and buildings
6. trails
7. roads and rail
8. specials
9. highway finishing
10. underground and upper levels (sewers, subways, bridge decks)
11. monsters

Forests and lakes come from global noise thresholds, so they line up across borders; small lakes are dropped. Rivers are jittered Bézier curves. Tunables live in `region_settings`, and a `region_layout` paints regions across the world (uniform, random, wedges or Voronoi). Rules point at `overmap_location`s (named sets of terrain types), never raw IDs.

**What GalHack should take.**
- **A saved planet plan on a coarse cell grid** (one cell per 32–64 m column, for example) that the voxel builder reads.
- **A fixed pass order:** terrain classes, reserved trunk corridors, settlement seeds, streets and lots, secondary network, POIs and flavour ruins, trunk finishing (bridges, tunnels, interchanges), underground layers, spawns.
- **Global passes on the torus.** It's finite, so run each pass over the whole planet. Every distance, tree and noise lookup must wrap.
- **Planet themes are region settings:** one data file per planet type. Outlier zones are a second region painted in by Voronoi.
- **Rules refer to named terrain-class sets** ("dry flat", "shore", "rock").

## 2. Cities

**How CDDA does it** (`overmap_city.cpp`).

- **Count and size.** City count comes from spacing and size settings. Each city's size is rolled, then bucketed into tiny, small, large or huge.
- **Streets.** Every city starts as one crossroads and grows streets recursively in four directions. Side streets branch off at intervals, alternating narrow and wide blocks, and shrink as they go. Ends may turn to form neighbourhoods. Streets stop at rivers, ravines and highways, and where they'd run right beside another road.
- **Buildings.** Each street cell places a building on each side three times in four, rotated to face the street. Type depends on distance from the centre, scaled by city size: shops inside a jittered radius, then parks, then houses. Each comes from a weighted list with optional once-per-city flags.
- **Pockets and megacities.** Enclosed pockets are flood-filled into the city. Megacity mode puts five big cities on every overmap, so towns merge into one sprawl.

**What GalHack should take.**
- **Settlements:** grow a street tree, then fill lots along the frontage. Lots face their street. Zone by scaled distance plus jitter: core, commerce, industry, housing, outskirts.
- **Add loops.** CDDA's streets are trees, so add cross-links between parallel streets or seed some districts from block grids.
- **City worlds** use a megacity mode with many overlapping seeds. Pocket fill turns the gaps into plazas.
- **Start worlds:** a small settlement with the cryo bunker as a forced POI just outside its outer lots, linked by a spur.

## 3. Roads, highways and underground networks

**How CDDA does it.**
- **The network.** Roads link every city centre plus at least three edge exits that avoid rivers and match the neighbours' exits. The links form a minimum spanning tree (shortest pairs first), and one redundant edge in ten is kept to make loops.
- **Pathing.** Each link is a greedy best-first search. An `overmap_connection` prices each terrain class: field is cheap, forest dearer, swamp dearer still, and water dearest (the road becomes a bridge). The distance term weighs five times more on new ground than on existing road, so roads merge.
- **Pieces.** Bridges can't turn, and crossings can be forced square. A four-bit mask of connected sides picks the end, straight, curve, tee or crossroads piece. Bridges get a raised deck, supports and ramps.
- **Specials** are pathed from the nearest city centre. Their rotation is chosen to satisfy the most connections.
- **Highways** (2025):
  - A jittered world grid of intersections, kept off lakes. Neighbours are joined by orthogonal lines with at most two bends plus slants.
  - Highway cells are reserved before cities, which avoid them; an earlier attempt ploughed through towns.
  - Segments are multi-level specials: flat, river bridge, overpass and road crossing.
  - Interchanges are spaced roughly every quarter overmap.
- **Underground.** Manholes at crossroads and subway stations become nodes at depth, joined by the same tree-plus-loops routine.

**What GalHack should take.**
- **One path engine with a cost table per tier**, read from voxel classes: slope, water depth, cliff, soft ground, existing route. Turn rules differ by tier:
  - highway: few bends
  - rail: gentle grades
  - pipeline: ignores slope and runs straight
  - power line: straight between pylons, may parallel a road
- **Structures are cost decisions** stored on the route: water gives a bridge, a steep enough ridge a tunnel, a ravine a viaduct.
- **Trunks** come from a jittered grid that divides evenly into the torus, reserved first. The planet's tech level picks which tiers and piece sets exist. Secondary roads use tree-plus-loops between settlements and major POIs, and every POI gets a spur with a reuse discount.
- **Underground roads, sewers and service tunnels** are depth layers built from node lists: manholes, stations, POI hatches.
- **Each segment stores a connection mask, a tier and an age**, so decay can be worst on rural spurs.

## 4. Overmap specials

**How CDDA does it.** An overmap is cut into 144 shuffled sectors of 15 × 15 cells. Each sector gets up to ten tries of a random point, special and rotation, and is used once something fits. Mandatory minimums go first, and any that can't fit spill into a nearby ungenerated overmap.

Constraints:
- terrain sets per cell (a dock needs one shore cell and one water cell)
- a distance band from the nearest city's edge
- a city-size range
- a minimum and maximum count per overmap

Unique specials use an "x in y" chance. A rate bug made rare ones about ten times too common. Since 2026 it's a deck of y cards holding x hits, drawn once per overmap, which makes the rate exact.

*Fixed* specials list cells at relative x, y, z offsets, plus road, sewer and subway connection points.

*Mutable* specials (2021) are grown instead:
- Each piece declares a join type on each of its six faces.
- Growth starts from a root and runs ordered phases of weighted or capped rules. Multi-cell chunks move as rigid bodies.
- Prechecks confirm the space is free before growth starts.
- Optional, asymmetric and alternative joins handle the awkward cases.
- The last phase caps every open join with heavily weighted dead ends. Anything left open is an error, and a test grows each special thousands of times to catch it.

Labs are modular fixed specials (2021) stacked down the levels: a surface security block, stair-carrying cores, residential, cargo and research tiers, and a finale at the bottom. The older JSON labs gave each cell only its south and east walls, centred the doors, and sealed edges that faced rock. A mutable ruins special grows ruin cores along a road spine from a gate.

**What GalHack should take.**
- **POI families state their constraints as data:**
  - terrain sets per footprint cell
  - distance bands to settlements and to each network tier
  - a minimum and maximum per planet
  - uniqueness per planet or per galaxy, drawn from a deck
- **Placement:** a shuffled jittered-sector scatter, mandatory families first, and empty sectors allowed.
- **Complexes** (bases, labs, mines) use the joins grammar:
  - typed sockets: corridor, door, shaft, airlock, exterior
  - phases: core, spine, wings, services, caps
  - a soak test that fails on any open socket
- **Multi-storey sites:** fix the vertical cores (stairs, lifts, shafts) for the whole stack, then lay out each storey independently. Every storey can differ and the cores still line up.
- **Cell-based interiors:** one owner per wall, so walls never double or gap.
- **Flavour ruins:** farmsteads and ruin clusters are small mutable specials grown along a spur. Lone houses are frontage lots on rural roads.

## 5. Local map generation

**How CDDA does it.**
- **Palettes.** Each cell is a 24 × 24 grid of symbols, and *palettes* map those symbols to terrain, furniture, items and monsters. Palettes include other palettes, and later definitions override earlier ones.
- **Variants.** Several weighted layouts can share a cell type.
- **Parameters** (2021) roll a choice once per scope (cell, vertical stack, special or chunk), such as a wall material or carpet colour. A switch keeps matching pieces in step, so a chosen fence type picks its matching gate.
- **Nested chunks** run from 1 × 1 to 24 × 24 and come from weighted lists that can include "nothing". With `place_nested` a chunk can depend on neighbour types, the joins used, the level or the terrain underneath. That's how sidewalks and the outer walls of complexes appear.
- **Chunk rules** from the guides: doors go mid-wall, corners stay clear, and names encode the size and door side.
- **Predecessor mapgen** generates the natural terrain first and builds on top.
- **Later edits.** `update_mapgen` changes a map after generation (missions, traps). *Map extras* add rare scenes per terrain type.

**What GalHack should take, mapped onto the pipeline.**
- **Programs.** CDDA has none, and its random mansions show the cost (section 8). A program lists the required rooms, their counts and their adjacencies, and layouts are solved against it.
- **Shared rooms** are CDDA's nests: modules typed by footprint and socket positions, in walled and open kinds, placed only into matching slots.
- **Style kits** are palettes plus scoped parameters, one per planet theme. They roll per site, per stack or per room, so a building reads as one place.
- **Site plans** hold the footprint, connections and rotation scoring. They build in order: voxel terrain, then foundations and cuts, then structure.
- **Ominous outliers:** map extras make cheap ones, such as a crash site or a lone survey beacon.

## 6. Decay, wrecks and aftermath

**How CDDA does it.**
- **State axes.** House palettes nest independent axes, each rolled once per special:
  - under construction (rare)
  - normal, or a rare outlier: abandoned, hoarder or survivor holdout
  - tidy, untidy or messy
  - no, light or heavy damage
  - alarmed or simply locked
  - flooded basement
- **Post-process passes** (`pp_generator`, data-driven since 2026) attach to terrain types:
  - bash damage and scattered items
  - fires, which fade with days since the event
  - blood
  - a full burn that chars walls and floors and keeps stairs, decided once for every floor of the building
- **Aftershock ruins.** The sci-fi Aftershock mod ruins a level fully only if open sky or a ruined floor lies above it.
- **Vehicles.** Road chunks place vehicles per lane and facing: out-of-fuel cars still in their lane on country roads; wrecks, parked cars and pile-ups in cities. Vehicles spawn lightly damaged with nearly empty tanks.
- **Overgrowth** barely exists, because CDDA is set days after the event.

**What GalHack should take.**
- **Decay is a per-site state vector:** age, violence, fire, flood, overgrowth, looting and occupation. It's rolled from planet history, site tier and distance from the network.
- **Decay passes read voxel material tags** (structural, flammable, organic, glass, interior). They never touch protected circulation: cores, doors the program needs, corridor sockets.
- **Collapse runs top-down.** A storey can lose its roof only if everything above it is gone.
- **City-world overgrowth:** soil drifts into roofless rooms, vines spread by exposure, trees grow where roofs are gone, and roads crack by tier.
- **Vehicles tell a story:** evacuation queues facing away from danger, pile-ups at bridges, strays out of fuel on rural spurs.

## 7. Furniture and items

**How CDDA does it.**
- **Item groups** are *collections* (each entry rolls independently) or *distributions* (pick one by weight). They nest and can fill containers.
- **"Specific use storage" groups** describe what one kind of storage furniture holds, and are reused everywhere that furniture appears.
- **Palettes put the item group on the furniture's own symbol,** so placing a dresser also fills it. Messy palettes scatter extras.
- **A debug command** samples a group 100 times.

**What GalHack should take.**
- **Loot tables keyed by container prop and room function,** modified by planet theme and decay (looted sites lose the best rolls).
- **Style kits own the props; tables own the contents.**
- **Build a table-sampling tool early.**

## 8. Known weaknesses and fixes

- **Repetition.** From 2013 on, players called houses samey: few layouts, oversized rooms, identical kitchens that sometimes blocked doorways. Variants, nests and state palettes (2021–22) helped.
- **Nonsense rooms.** Random mansions could fill with dining halls; hand-made layouts plus nests replaced them.
- **Crowding.** The sector grid fits most specials onto every overmap, so "rare" feels common. The unique-rate bug made it worse until the 2026 deck fix.
- **Seams.** Lab walls and doors misaligned across cells. Spawns and vehicles can't cross cell borders.
- **Pass order.** Buildings faked the forest around them until predecessor mapgen (2021–22).
- **Gradients.** A forest gradient left players driving hundreds of kilometres without a town (issue #70062).
- **Testing.** Determinism and terrain-coverage tests now exist; region borders still aren't blended.

## Pitfalls to avoid

- **Room fill with no program behind it.**
- **Independent rolls for rare things.** Use per-planet quotas and decks.
- **Every POI family on every planet.** Emptiness is part of "ominous".
- **Decay decided per storey or per cell** instead of per site.
- **Nests or decay touching circulation.** CDDA never checks that a map can be walked. GalHack should flood-fill every site after decay and repair or reject it.
- **Two modules owning one wall or socket.**
- **Global random state.** Seed per site and per pass, and test for identical output.
- **Untested gradients** that empty whole regions.
- **Copying CDDA content.** Ideas only.

## Sources

Docs (CleverRaven/Cataclysm-DDA, master):
- https://github.com/CleverRaven/Cataclysm-DDA/blob/master/doc/JSON/OVERMAP.md
- https://github.com/CleverRaven/Cataclysm-DDA/blob/master/doc/JSON/MAPGEN.md
- https://github.com/CleverRaven/Cataclysm-DDA/blob/master/doc/JSON/REGION_SETTINGS.md
- https://github.com/CleverRaven/Cataclysm-DDA/blob/master/doc/JSON/REGION_LAYOUT.md
- https://github.com/CleverRaven/Cataclysm-DDA/blob/master/doc/JSON/ITEM_SPAWN.md
- https://github.com/CleverRaven/Cataclysm-DDA/blob/master/doc/JSON/JSON_Mapping_Guides/Guide_for_beginning_mapgen.md
- https://github.com/CleverRaven/Cataclysm-DDA/blob/master/doc/JSON/JSON_Mapping_Guides/Guide_for_intermediate_mapgen.md
- https://github.com/CleverRaven/Cataclysm-DDA/blob/master/doc/JSON/JSON_Mapping_Guides/JSON_ROOF_MAPGEN.md

Source and data:
- https://github.com/CleverRaven/Cataclysm-DDA/blob/master/src/overmap.cpp (pass order, road tree, pathing, bridges, specials, sewers, subways)
- https://github.com/CleverRaven/Cataclysm-DDA/blob/master/src/overmap_city.cpp
- https://github.com/CleverRaven/Cataclysm-DDA/blob/master/src/overmap_highway.cpp
- https://github.com/CleverRaven/Cataclysm-DDA/blob/master/src/overmap_water.cpp
- https://github.com/CleverRaven/Cataclysm-DDA/blob/master/src/overmap_special.cpp
- https://github.com/CleverRaven/Cataclysm-DDA/blob/master/src/overmap_special_fixed.cpp
- https://github.com/CleverRaven/Cataclysm-DDA/blob/master/src/mapgen_post_process.cpp
- https://github.com/CleverRaven/Cataclysm-DDA/blob/master/src/vehicle_group.cpp
- https://github.com/CleverRaven/Cataclysm-DDA/blob/master/data/json/post_process_generators.json
- https://github.com/CleverRaven/Cataclysm-DDA/blob/master/data/json/mapgen_palettes/house_general_palette.json
- https://github.com/CleverRaven/Cataclysm-DDA/blob/master/data/json/mapgen/nested/road_vehicles_nested.json
- https://github.com/CleverRaven/Cataclysm-DDA/tree/master/data/json/overmap/overmap_special/lab_modular
- https://github.com/CleverRaven/Cataclysm-DDA/blob/master/data/json/overmap/overmap_mutable/ancient_city_ruins.json
- https://github.com/CleverRaven/Cataclysm-DDA/tree/master/data/json/itemgroups/SUS
- https://github.com/CleverRaven/Cataclysm-DDA/blob/master/tests/overmap_test.cpp

Pull requests and issues:
- Mutable specials: https://github.com/CleverRaven/Cataclysm-DDA/pull/51317, https://github.com/CleverRaven/Cataclysm-DDA/pull/51657, https://github.com/CleverRaven/Cataclysm-DDA/pull/51723, https://github.com/CleverRaven/Cataclysm-DDA/pull/52103
- Parametric mapgen: https://github.com/CleverRaven/Cataclysm-DDA/pull/48529, https://github.com/CleverRaven/Cataclysm-DDA/pull/49980, https://github.com/CleverRaven/Cataclysm-DDA/pull/50681, https://github.com/CleverRaven/Cataclysm-DDA/pull/51028
- Predecessor mapgen: https://github.com/CleverRaven/Cataclysm-DDA/pull/52208, https://github.com/CleverRaven/Cataclysm-DDA/pull/55735
- Modular labs: https://github.com/CleverRaven/Cataclysm-DDA/pull/50241
- Highways: https://github.com/CleverRaven/Cataclysm-DDA/pull/81461, https://github.com/CleverRaven/Cataclysm-DDA/pull/81602, https://github.com/CleverRaven/Cataclysm-DDA/pull/82439
- Unique-special deck: https://github.com/CleverRaven/Cataclysm-DDA/pull/85849
- Post-process generators: https://github.com/CleverRaven/Cataclysm-DDA/pull/86558
- Forest gradient with no towns: https://github.com/CleverRaven/Cataclysm-DDA/issues/70062

Forums and other:
- https://discourse.cataclysmdda.org/t/houses-in-dire-need-of-an-update/4124
- https://discourse.cataclysmdda.org/t/better-mansion-layout-proposal/6910
- https://discourse.cataclysmdda.org/t/worldgen-rewrite/8934
- https://discourse.cataclysmdda.org/t/towns-feel-less-worthwhile-past-the-early-game/19507
- https://discourse.cataclysmdda.org/t/laboratories-with-unaligned-walls-and-doors/19204
- https://cataclysm-bn.pages.dev/mod/json/guides/map/lab (Bright Nights fork guide describing the older floorplan-based JSON labs)
