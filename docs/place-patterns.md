# Place patterns: rules and ranges for generated places

GalHack's own rules for its place generator (`prototypes/poi-generator`), drawn from measurements of two reference games' hand-made places: Empyrion (blueprints, playfield placement rules) and 7 Days to Die (prefabs, sleeper volumes, world placement). We took their **numbers and habits**, never their layouts. Nothing from either install is copied here: no files, names of their content, tables or plans. File names are cited so a reader can check a figure.

**Who this is for**
- The places agent (`proto/places`), for generator ranges and checks.
- The spawns agent, for encounter placement (section 2).
- Later agents building low-tech, high-tech and space places.

**Units.** GalHack is one 1 m cell; a storey is 4 m (slab plus 3 m headroom); the standard door is 2 x 2 m. 7 Days to Die is 1 block = 1 m (checked in the floor-plan notes). Empyrion's large blocks are 2 m and small blocks 0.5 m; every Empyrion figure below is already in metres. **Their sizes are the floor, never the cap:** ours may be bigger.

**Method, in one paragraph.** Scripts (kept outside the repo, in the floor-plan reference folder's tools and a scratch folder) read every 7 Days to Die POI prefab's settings (`Data/Prefabs/POIs/*.xml`, 1,105 POIs, 887 with sleeper volumes) and block grid (`*.tts`, `*.blocks.nim`), every Empyrion blueprint that is a place, not a player ship (`Content/Prefabs/*.epb`, 2,091 read: 1,097 planet bases, 358 space bases, 103 ruins and junk, 55 wrecks, 406 capital vessels), and every Empyrion playfield's placement rules (`Content/Playfields/*/playfield_static.yaml`, `space_dynamic.yaml`; 108 planets and moons parsed). Pockets of air were found by flood fill, so a "closet" is a sealed air pocket of 12 m³ or less and a "ceiling cavity" is a pocket at most 2 m tall with a room directly under its floor.

Sections: [1 Per place category](#1-per-place-category) · [2 Encounters](#2-encounters) · [3 Tech level](#3-tech-level) · [4 Map layout](#4-map-layout) · [5 Space](#5-space) · [6 Generator checklist](#6-generator-checklist)

---

## 1. Per place category

**How rooms were measured.** A walkable cell is open space with at least 2 m of headroom over a structural floor, and a roof somewhere within 10 m above it. Each storey's walkable cells are split into rooms by walls and closed doors. Closets (4 m² or less), ceiling cavities (2 m or less tall with a room under them) and every encounter pocket from section 2.8 are **left out** of these figures. A room counts from 6 m² (7 Days to Die) or 8 m² (Empyrion, 2 m cells). A corridor is a space whose clear width is one cell (1 to 2 m in 7 Days to Die; one 2 m large block in Empyrion) and at least 4 m long. 7 Days to Die figures come from 1,105 POIs (`Data/Prefabs/POIs`) and Empyrion figures from 1,837 place blueprints on the large grid (`Content/Prefabs`). A room's kind was guessed from its furniture, so the room-kind shares are rough. Lot size in 7 Days to Die is the whole plot (yard, drive, street edge), not the building.

### 1.1 What every category shares

| Measure | 7 Days to Die (low tech) | Empyrion (high tech) | GalHack now |
|---|---|---|---|
| Room floor area | p10 12, median 23, p90 110 to 410 m² | p10 36, median 88, p90 320 to 440 m² | rooms by recursive splits |
| Room plan | median 7 x 5 m (p90 about 20 x 13) | narrowest span median 6 m (p90 10 m) | |
| Clear headroom | median 3 to 4 m (p10 2, p90 5 to 8) | median 4 m, from 2 m in wrecks and huts to 8 m in halls | 3 m |
| Floor to floor | **5 m in most buildings** (45 to 69% of columns), 6 m in offices, industry and military (25 to 33%), 4 m in 10 to 20% | 6 m (2 m slab plus 4 m) | 4 m |
| Corridor clear width | 1 to 2 m | 2 to 4 m (one or two large blocks; ship and base interiors median 4 m) | 2 m |
| Corridor run between turns | median 6 m (p90 10 to 16 m) | | |
| Corridor share of floor | 10% (hospital) to 28% (settlement); 21% in houses | 23 to 33% in buildings, 55 to 71% in satellites and asteroid bases | |
| Rooms per place | house 14, shop 13, office 39, hospital 42, police 78 (all levels) | 2 to 7 large rooms per blueprint | |

**Rule P1 (headroom and storeys).** Their sizes are the floor, so GalHack's 4 m storey is the **minimum**, not the default. Use 4 m for houses, huts, cabins, wrecks and service decks; 5 m (4 m headroom) for shops, offices, schools, clinics and stations; 6 m or more for industry, hangars, military and high-tech halls; double height (8 to 10 m) for lobbies, workshops and atriums, about one room in eight in civic and high-tech places.

**Rule P2 (rooms).** Low-tech rooms: 9 to 40 m² (median about 24 m², about 5 x 5 to 5 x 8 m), with one hall over 100 m² per place in shops, schools, offices and military sites. High-tech rooms are about three to four times larger: 36 to 150 m² (median about 90 m²), at least 6 m across, with 300 m²+ halls. Bathrooms and closets 4 to 15 m². No room under 6 m² except closets and stores.

**Rule P3 (corridors).** Low tech: 2 m clear (never below our 2 m minimum), turning or opening into a room about every 6 m, taking 10 to 25% of the floor (less in open-plan shops and hospitals with wide wards; more in apartments and settlements). High tech: 2 to 4 m clear, 25 to 35% of the floor; tight service places (satellites, asteroid bases, wrecks) up to 60%.

### 1.2 Low-tech categories (7 Days to Die)

Lot = plot area; floor = summed room floor over all levels; levels = distinct floor heights with rooms (split levels and mezzanines included); below = share of floor under ground.

| Category | n | Lot (median) | Floor (p10 / median / p90 m²) | Levels | Below | Halls >100 m² | Main materials |
|---|---|---|---|---|---|---|---|
| House | 164 | 42 x 42 m | 141 / 386 / 837 | 2 to 9, median 5 | 24% | 1 | wood 59%, concrete 26%, brick 7% |
| Shop, gas station, diner, bar | 65 | 56 x 50 m | 133 / 562 / 2,024 | median 3 | 10% | 1.6 | concrete 53%, metal sheet 17%, wood 15%, brick 13% |
| Office, apartments, tower | 40 | 42 x 42 m (up to 60 x 108 m tall) | 87 / 1,196 / 5,547 | 3 to 28, median 7 | 9% | 4 | concrete 67%, wood 12%, metal sheet 11% |
| Industry, warehouse, utility | 166 | 42 x 42 m (up to 100) | 0 / 129 / 1,191 | median 2 | 15% | 0.8 | metal sheet 39%, concrete 38%, brick 12% |
| Police, jail | 4 | | 103 / 3,344 / 3,988 | median 11 | 6% | 3 | concrete 75%, metal sheet 16% |
| Fire station | 4 | | 332 / 544 / 552 | 5 | 7% | 1.5 | concrete 53%, brick 16% |
| School | 8 | 51 x 51 m | 0 / 1,046 / 3,304 | median 5 | 1% | 2 | concrete 48%, brick 20%, wood 19% |
| Hospital | 4 | 80 x 80 m | 247 / 1,522 / 6,123 | median 9 | 17% | 3.8 | concrete 78%, metal sheet 10% |
| Military, bunker | 13 | up to 100 x 100 m | 279 / 488 / 3,710 | median 7 | **63%** | 3.1 | concrete 69%, metal sheet 21% |
| Mine, cave | 20 | up to 100 x 100 m | 238 / 605 / 1,983 | median 14 | **91%** | 1.1 | concrete 40%, metal sheet 33%, wood 24% |
| Settlement, farm, cabin, camp | 81 | 60 x 60 m (farms) | 10 / 373 / 863 | median 5 | 16% | 0.7 | wood 55%, concrete 21%, metal sheet 15% |

**Room programs (shares of rooms of 6 m² and up, by furniture; "bare" is about 40 to 60% everywhere: halls, stairs, empty or stripped rooms).**
- **House**: living and dining together about 20%, storage 12%, bathroom 7%, bedroom 5%, kitchen and office the rest; a basement in a quarter of the floor. Program to use: entry or hall, living, kitchen with dining, 1 to 4 bedrooms, 1 to 2 bathrooms, a utility or garage, an optional cellar and attic. Bedrooms on the upper storey; the kitchen at the back.
- **Shop**: one sales floor (the biggest room, 100 to 300 m²), storage behind it (12%), a back office, a staff toilet; storage and toilets reached from the sales floor through a staff door.
- **Office and tower**: office cells and open-plan floors round a core of stairs, lift and toilets (9% bathrooms, 9% offices); the ground floor a lobby; stacked identical floors (we vary them: the architect's plan-signature rule already does).
- **Industry**: one big work hall (often the whole building), with storage (18%), a small office (6%) and a toilet; 60% of rooms read bare because machine halls are open floor.
- **Police**: offices (10%), a holding block (bed-and-bar cells read as bedrooms here, 23%), storage, a lobby and locker room.
- **School**: classrooms (22% office-like: desks), a hall or gym over 100 m², toilets (9%), lockers lining the corridors.
- **Hospital**: wards (beds, 14% read as living), treatment and offices, waiting areas (13% dining-like seating), toilets 9%; the widest rooms of any category (median 34 m²) and the least corridor (10%).
- **Military and bunker**: two thirds below ground; barracks, armoury (7%), storage, mess, offices.
- **Mine and cave**: 91% below ground, 81% bare (tunnels and chambers), storage niches, a tool room near the top.
- **Settlement**: shacks, stores (18%), a mess, sleeping rooms; the most corridor-like space (28%) because outdoor walkways between shacks are roofed.

**Entrances.** Doors that open onto open sky, per POI (median, p90): house 2 (5), shop 3 (11), office 4 (13), industry 1 (7), school 3 (15), fire station 10, hospital 11 (12), police 13 (28), military 2 (7), settlement 1 (8), mine and cave 0 (1). About half are on the ground floor; the rest open onto balconies, roofs and fire escapes. Kinds: a one-wide house or office door, glass shop doors (single and double), roll-up and garage doors 3 to 5 m wide in shops, fire stations and industry, chain-link gates in yards. Mines and caves are entered through an opening in the ground, not a door. Fake cabinet doors that hide passages (523 counted on outer walls alone) and trap doors are encounter and secret features (section 2.8).

**Rule P4 (ways in).** Low-tech places have 2 to 4 ways in for houses and small shops, 3 to 6 for offices and schools, 8 to 12 for hospitals, fire stations and police (staff, public, vehicle, emergency). At least one is at ground level on the street side and one at the back. Upper-storey ways in (fire escape, balcony, roof hatch) are one in two. Vehicle doors 3 to 5 m wide go on industry, fire stations, garages and shops' loading bays. Mines and bunkers get one main way in plus an optional hidden or collapsed second.

### 1.3 High-tech categories (Empyrion, planet places)

Sizes in metres (large blocks x 2); counts per blueprint, median (p90).

| Category | n | Plan (median x by z, p90) | Height (median, p90) | Spawners | Interior guns | Turrets | Containers | Core held |
|---|---|---|---|---|---|---|---|---|
| Outpost, base, military, bunker | 192 | 46 x 44 m (108 x 102) | 24 m (64) | 3 (41) | 3 (25) | 4 (10) | 5 (86) | 84% |
| Lab, research | 26 | 54 x 58 m (142 x 138) | 42 m (72) | 9 (22) | 1 (17) | 0 (8) | 20 (65) | 83% |
| Factory, refinery | 40 | 60 x 52 m (90 x 90) | 32 m (50) | 5 (30) | 4 (17) | 1 (8) | 17 (57) | 90% |
| Mine, drilling site | 70 | 58 x 54 m (116 x 110) | 38 m (90) | 3 (18) | 0 (7) | 0 | 10 (48) | 74% |
| Trade post, port, depot | 134 | 48 x 46 m (100 x 104) | 26 m (54) | 8 (37) | 4 (15) | 2 (7) | 21 (57) | 80% |
| Settlement, colony, village | 184 | 56 x 62 m (146 x 140) | 18 m (46) | 4 (38) | 0 (2) | 0 (4) | 7 (40) | 75% |
| Alien site, temple, tomb | 117 | 30 x 32 m (112 x 138) | 36 m (82) | 5 (36) | 0 (7) | 0 (8) | 4 (29) | 84% |
| Cave, nest, lair | 9 | 64 x 60 m | 52 m | 4 (122) | 5 (58) | 0 (10) | 13 (160) | 50% |
| Ruins and junk | 103 | 16 x 20 m (36 x 44) | 8 m (30) | 0 (5) | 0 | 0 | 1 (3) | 0% |

Room figures across these: room median 84 to 96 m² (p90 320 to 416 m²), narrowest span 6 m, corridors 23 to 31% of floor, headroom 4 m (2 m in settlements' huts and alien crawlways, up to 8 m in halls), 2 to 7 large rooms per blueprint, levels median 2 to 6 (factories 6, p90 22).

**Programs (from Empyrion's device mixes and our own design; Empyrion names no rooms).**
- **Outpost** (small, 20 to 40 m): a guard post, a store, a power room, a landing pad; 1 to 3 rooms.
- **Base and garrison** (40 to 110 m): command, barracks, armoury, workshop and fabrication, power and fuel, a hangar or vehicle bay, a medical bay; the core room deep inside; turrets on roofs and corners.
- **Lab** (50 to 140 m, the tallest after mines): labs in a row on a clean corridor, a containment or test chamber, offices, a store, sublevels through airlocks. The most containers per place (20).
- **Factory and refinery**: one production hall 300 m²+ and 8 m+ tall, stores, a control room above the hall, loading bays; lots of round shapes (tanks, pipes: 7% round against 2 to 4% elsewhere).
- **Mine**: a headframe or drill rig over a shaft, a few storeys of plant, ore stores; the tallest places (38 m median, 90 m p90).
- **Trade post and port**: a hall with counters, stores, a landing pad, living quarters; the most containers with labs.
- **Alien site**: compact (30 m) but tall (36 m), few doors (median 0), trap doors and hatches into the levels below, 44% alien materials.

**Entrances (high tech).** Doors per blueprint median 6 (bases), 13 (labs), 19 to 20 (trade, factories); 0 to 2 in ruins and alien sites. Kinds, by count: one-block (2 m) interior doors, armoured vertical doors, shutter doors 2 x 2 to 5 x 5 blocks (4 to 10 m) for vehicles and hangars, trap doors over ladders. Windows carry armoured shutters (by far the commonest "door" block in every category): a high-tech place closes its eyes when attacked.

**Rule P5 (high-tech ways in).** One main way in (an airlock or armoured door pair), one vehicle or hangar door 4 to 10 m wide, one or two service ways (roof hatch, ladder shaft, pad lift). Alien sites: one way in at ground level, then hatches down. Every high-tech window can close.

### 1.4 Space categories (Empyrion, space places)

| Category | n | Longest side (p10 / median / p90) | Height (median) | Room median | Corridor share | Headroom | Spawners | Turrets | Containers | Doors |
|---|---|---|---|---|---|---|---|---|---|---|
| Station, trade, repair, supply | 79 | 62 / 122 / 186 m | 86 m | 88 m² | 33% | 4 m | 8 (37) | 6 (19) | 29 (91) | 24 (145) |
| Space base, production, yard | 201 | 48 / 108 / 202 m | 84 m | 80 m² | 38% | 4 m | 6 (30) | 2 (16) | 18 (65) | 22 (80) |
| Asteroid base, mining | 18 | 86 / 134 / 242 m | 118 m | 68 m² | 55% | 4 m | 4 (22) | 0 (6) | 21 (67) | 15 (91) |
| Defence platform, satellite, beacon | 80 | 18 / 50 / 78 m | 24 m | 36 m² | 71% | 2 m | 0 | 0 (8) | 0 (4) | 0 (6) |
| Space wreck, derelict | 11 | 26 / 138 / 276 m | 50 m | 88 m² | 59% | 2 m | 0 (97) | 0 (1) | 4 (54) | 6 (104) |

Capital-ship wrecks used as places (406 capital vessels): 50 x 26 x 102 m median (p90 98 x 48 x 174 m), 17 containers, 19 doors, 8 turrets.

**Programs.** Station: docking bays and hangars, a trade hall, stores, quarters, a command deck, power; the most doors and containers of any place. Asteroid base: rooms cut into rock (rock is the commonest block by far in a sample of 50 Empyrion base blueprints), joined by long tunnels (55% corridor), with ore stores and a refinery. Satellite and defence platform: no rooms worth the name, only crawlways (71% corridor, 2 m headroom) and guns. Derelict: a ship's own plan, damaged; corridors dominate (59%), headroom low (2 m) where decks crushed.

### 1.5 Block shapes by category

Share of shaped structural blocks (devices, terrain and rock left out).

7 Days to Die (`*.blocks.nim` shape names):

| Category | Full cube | Part cube (half, quarter) | Plate (thin wall, floor) | Slope (ramp, wedge, gable) | Corner | Pole, beam | Stair | Trim | Rail, fence | Window |
|---|---|---|---|---|---|---|---|---|---|---|
| House | 49% | 8% | 6% | **18%** | 2% | 4% | 1% | 3% | 2% | 4% |
| Settlement | 37% | 10% | 9% | **19%** | 3% | 8% | 1% | 3% | 4% | 4% |
| Shop | 47% | 21% | 9% | 6% | 2% | 5% | 1% | 3% | 1% | 2% |
| Office | 53% | 15% | 7% | 4% | 3% | 5% | 1% | 3% | 1% | 6% |
| Industry | 39% | 15% | 11% | 8% | 4% | 8% | 1% | 2% | 3% | 4% |
| Hospital | 54% | 14% | 8% | 5% | 3% | 3% | 1% | 3% | 1% | 5% |
| Police | 53% | 20% | 5% | 5% | 3% | 2% | 1% | 3% | 2% | 4% |
| School | 55% | 16% | 8% | 5% | 3% | 3% | 1% | 3% | 1% | 4% |
| Military | 55% | 11% | 7% | 9% | 3% | 5% | 1% | 2% | 2% | 2% |
| Mine, cave | 34% | 12% | 12% | 6% | 5% | **17%** | 1% | 3% | 6% | 1% |

Empyrion (`BlocksConfig.ecf` shape lists, read through each block's variant):

| Category | Full cube | Half, part | Slope (ramp, wedge) | Corner | Round (cylinder, edge) | Thin wall | Beam, truss |
|---|---|---|---|---|---|---|---|
| Base, military | 63% | 7% | 19% | 5% | 3% | 3% | 1% |
| Trade, station | 54% | 9% | 22% | 5% | 4% | 4% | 1% |
| Settlement | 51% | 9% | 22% | 4% | 3% | 7% | 2% |
| Factory | 54% | 7% | 23% | 5% | **7%** | 2% | 0% |
| Lab | 65% | 6% | 17% | 5% | 3% | 4% | 1% |
| Mine | 67% | 6% | 15% | 4% | 3% | 3% | 0% |
| Alien | 64% | 4% | 20% | 6% | 2% | 3% | 1% |
| Wreck | 65% | 5% | 19% | 4% | 2% | 2% | 0% |
| Capital vessel | 48% | 4% | 20% | **10%** (cut and sliced corners) | n/a | 1% | 0% |

**Rule P6 (shapes).** About half of every place is plain cube. Low tech: slopes 15 to 20% where roofs are pitched (houses, settlements, farms) and 4 to 8% where they are flat (shops, offices, civic, industry); part cubes and plates 20 to 30% (counters, shelves, thin partitions, kerbs); poles and beams 3 to 8%, up to 17% in mines (props and timbering); windows 2 to 6%. High tech: slopes 15 to 23% everywhere (chamfered edges, sloped armour, roofs), corners 4 to 7%, round 2 to 4% (7% in industry), almost no plates or poles. A check: a generated place's shape shares should fall within these bands for its tech, give or take 5 points.

### 1.6 Dressing density and where it sits, per room kind

7 Days to Die, per m² of room floor (median for dressing, mean for containers and lights). Dressing = furniture and clutter cells standing on the floor. Wall share = the share of floor dressing touching a wall.

| Room kind | n | Area (median) | Dressing per m² (median, p90) | Containers per m² | Lights per m² | Against a wall |
|---|---|---|---|---|---|---|
| Bathroom | 417 | 13 m² | 0.38 (0.64) | 0.25 | 0.08 | **64%** |
| Office | 823 | 24 m² | 0.32 (0.59) | 0.17 | 0.09 | 45% |
| Cell block | 10 | 79 m² | 0.32 (0.60) | 0.07 | 0.02 | 53% |
| Dining, bar | 1,251 | 30 m² | 0.26 (0.50) | 0.11 | 0.08 | 49% |
| Living | 423 | 40 m² | 0.26 (0.47) | 0.09 | 0.07 | 52% |
| Bedroom | 292 | 18 m² | 0.26 (0.51) | 0.10 | 0.07 | 44% |
| Storage | 1,201 | 36 m² | 0.25 (0.53) | 0.09 | 0.04 | 42% |
| Kitchen | 183 | 24 m² | 0.24 (0.46) | **0.28** | 0.07 | 49% |
| Locker room | 80 | 25 m² | 0.22 (0.60) | **0.28** | 0.07 | 58% |
| Study, library | 139 | 29 m² | 0.21 (0.40) | 0.27 | 0.07 | 40% |
| Workshop | 97 | 25 m² | 0.18 (0.56) | 0.11 | 0.04 | 52% |
| Armoury | 53 | 20 m² | 0.17 (0.58) | 0.14 | 0.04 | 26% |
| Shop floor | 379 | 36 m² | 0.16 (0.42) | **0.33** | 0.06 | 39% |
| Clinic | 16 | 20 m² | 0.14 (0.57) | 0.21 | 0.04 | 46% |
| Bare (halls, stairs, stripped) | 1,918 | 15 m² | 0.06 (0.40) | 0.03 | 0.03 | 39% |

Empyrion places are sparser on the floor: about 0.05 dressing blocks per m² of room floor (2 m cells, devices counted once at their origin), containers about 2.4 per 100 m² of roofed floor on planets and 1.8 in space (section 2.9), lights 6 to 26 per place (more in trade and factories, fewest in wrecks and caves).

**Rule P7 (dressing).** Per m² of room floor: wet rooms 0.3 to 0.6, offices, bedrooms, living and dining 0.2 to 0.5, stores and shop floors 0.15 to 0.4 but mostly containers, workshops and halls 0.1 to 0.3, corridors and stairs under 0.1. About half the dressing stands against walls (60% in bathrooms and locker rooms, under 30% in armouries, where racks stand free in rows); the rest in islands (tables, beds, counters, shelving rows). One light per 11 to 15 m² in lived rooms, one per 25 m² in stores and workshops. High-tech rooms: about a fifth of that floor dressing per m² but bigger machines, so judge by covered floor (15 to 30%) rather than count.

**Rule P8 (loot by room).** Containers per m²: shop floors, kitchens, lockers and studies 0.25 to 0.33; bathrooms 0.25 (medicine and small caches); offices, clinics and armouries 0.14 to 0.2; bedrooms, living, dining, storage, workshops 0.09 to 0.11; halls 0.03. So a place's loot follows its room program: shops, clinics and offices are rich, industry and caves poor. The reward cache rule is in 2.9.

---

## 2. Encounters

How the two games put creatures and people in a place, measured, then turned into GalHack rules. The spawns agent should read 2.1 for the model, 2.2 to 2.8 for the rules, and 2.9 for the checks.

### 2.1 The model both games share: a place is a chain of rooms, each with its own small encounter

7 Days to Die hangs its whole encounter on **sleeper volumes**: boxes drawn over the plan, each with a group, a min and max count, and flags (`SleeperVolumeSize`, `SleeperVolumeStart`, `SleeperVolumeGroup`, `SleeperVolumeFlags`, `SleeperVolumeTriggeredBy`, `SleeperIsLootVolume`, `SleeperIsBossVolume` in each POI's `.xml`). Inside a volume, marker blocks set the exact spot and pose (standing, sitting, lying on the back, front or side, crawler-only, animal). Empyrion uses **spawner blocks** set into the build (`EntitySpawner*` in `BlocksConfig.ecf`, default two NPCs each), fixed **sentry guns** inside and **turrets** outside, and a **core** that marks who owns the place.

Measured volume sizes (7 Days to Die, 8,377 volumes):

| | p10 | median | p90 |
|---|---|---|---|
| Volume floor area | 6 m² | 42 to 99 m² by category | 140 to 390 m² |
| Volume height | 2 m | 3 to 4 m | 6 to 10 m |

So **one volume is one room** (or one hall, or one stretch of corridor), almost never a whole storey. 99.8% of the 39,710 markers sit inside a volume.

**Rule E1.** Encounters are planned per room, not per place. Each room the architect makes gets an encounter slot: empty, or a group with a min and max. The place's total comes from summing rooms, not from a place-wide count scattered at random.

### 2.2 How many, and how big a group

Group size per volume (largest count the volume can roll), 8,377 volumes:

| Max in volume | 0 | 1 | 2 | 3 | 4 | 5 | 6 to 9 | 10 |
|---|---|---|---|---|---|---|---|---|
| Share | 18% | 42% | 23% | 10% | 2% | 3% | 1% | 0.5% |

Most volumes are a fixed count (min = max: 1 and 1, 2 and 2, 3 and 3 are the three commonest). Group size follows room volume: under 150 m³ the median is 1; 150 to 3,000 m³ it is 2; over 3,000 m³ (a hall) it is 3.

By difficulty tier (7 Days to Die `DifficultyTier`, 0 to 5):

| Tier | POIs | Volumes per POI (median) | Creatures per POI (median of mid-counts) | Lot area (median) | Empty volumes | Tough groups |
|---|---|---|---|---|---|---|
| 0 | 409 | 2 | 3 | 625 m² | 9% | 2% |
| 1 | 146 | 6 | 7 | 1,764 m² | 10% | 0% |
| 2 | 136 | 10 | 15 | 1,764 m² | 13% | 3% |
| 3 | 106 | 15 | 22 | 1,764 m² | 16% | 8% |
| 4 | 65 | 24 | 40 | 3,600 m² | 18% | 20% |
| 5 | 25 | 57 | 106 | 10,000 m² | 38% | 22% |

Two things grow with tier: the count per area (about 0.2 creatures per 100 m² of lot at tier 0, 1 to 1.3 at tiers 1 to 3, 2.3 at tier 4, 3 at tier 5) and the toughness mix. **The share of empty rooms grows too**: big hard places have more quiet rooms, not fewer.

Empyrion, per place blueprint (median, p90), counting the large-grid floor area that is roofed:

| Kind | Spawners | Interior sentries | Outside turrets | Spawners per 100 m² floor | Spawner to nearest spawner |
|---|---|---|---|---|---|
| Planet base | 5 (35) | 0 (15) | 1 (8) | 1.2 (p10 0.2, p90 4.2) | |
| Space base | 4 (27) | 0 (18) | 1 (14) | 0.5 (p90 2.2) | |
| Ruins and junk | 0 (5) | 0 | 0 | | |
| Wrecks | 0 (13) | 0 (5) | 0 (2) | 0 (p90 2.0) | |
| All of the above | | | | | 8 m (p10 4 m, p90 16 m) |

**Rule E2.** Roll each room's group from its volume: 0 to 1 below 150 m³, 1 to 2 up to 3,000 m³, 2 to 4 above that, rare 5 to 10 only in the biggest halls. Then scale the place by its danger tier with these targets, per 100 m² of walkable floor: about 0.3 for the easiest places, 1 to 1.5 for middling, 2 to 3 for the hardest. Space and derelict places run at about half the planet density. Keep spawn points at least 4 m apart and usually about 8 m.

### 2.3 Quiet stretches

Both games leave a lot of a place empty on purpose.
- 18% of 7 Days to Die volumes roll zero (rising from 9% at tier 0 to 38% at tier 5), and many rooms have no volume at all (median volume floor area is a few tens of m² on lots of 600 to 10,000 m²).
- Empyrion ruins and wrecks have a median of **no** spawners; their danger is outside (creature spawn zones round wrecks and abandoned places in the playfield files, radius 500 m, densest at the centre).

**Rule E3.** At least one room in five holds nothing, and never three occupied rooms in a row along the route without a quiet one, except in a garrison's guard zone. Big, hard places get more empty rooms, not fewer: up to a third. A corridor run is quiet by default; corridors get passers (a patrol, 2.6), not residents.

### 2.4 Residents by room purpose

Who is waiting matches what the room was for. In 7 Days to Die about 60% of volumes use the generic group, and the rest use the place's own occupation: workers in industry and utilities, office staff in offices, soldiers on military sites, patients and staff in hospitals, inmates in police stations and jails, bar crowds in bars, lab staff in labs, janitors in schools and service rooms. Poses follow the furniture: standing (41% of markers), sitting (23%, at desks, counters, toilets, pews), lying on back, front or side (30%, in beds, on floors, in wrecked cars), crawlers (1%, in low gaps), animals (5%: dogs, carrion birds, snakes).

**Rule E4.** Every room purpose in `rooms.txt` names who it holds when occupied, as a share: about 60% the place's general roster and 40% the room's own (ward: patients; cells: inmates; workshop: workers; barracks: off-duty guards lying down; mess: seated). The pose follows the room's props: seated at a desk, bench or console; lying on a bed, bunk or the floor; standing at a machine or window. A resident starts *in* the room doing its thing, asleep or idle, and wakes on noise, light or sight. Wildlife in an abandoned place keeps to its own kind of spot: flyers high (roof, atrium galleries), runners low (ground floor, yards), crawlers in low gaps.

### 2.5 Guards, cores and turrets (faction-held places)

Empyrion, over 1,600 place blueprints:
- **Outside turrets**: 98% face open air. They sit toward the edge (median 0.32 of the way from edge to centre) and high (median at 48% of the place's height, p90 88%): roofs, corners, towers.
- **Interior sentries**: 43% in rooms, 44% in spaces open to the outside (hangars, entry halls), the rest tucked into pockets. They cover entrances and halls.
- **Spawners**: spread evenly (median depth 0.54, centred) and low (median at 27% of height, so mostly on the lower decks), 8 m apart.
- **The core**: deep and low. Median 0.67 of the way to the centre, 17% up the height, and 72% of cores are walled in by solid blocks, so the player has to dig or find the way. Containers sit a median 31 m from the core (p10 11 m, p90 66 m), as do spawners (27 m): the core is not where the loot is.
- Outside the walls, the playfield sends drones: a median **DroneProb** of 1.0 per faction place (p10 0.2), **1 to 2 drones** (p90 3 to 5), and half the faction places can call a troop transport when attacked (`TroopTransport`, 401 of 795). Patrol troops spawn in zones round faction bases: dense within 150 m, thinner out to 500 m, a wide ring to 1,200 m (`SpawnZones` in `Arid/playfield_static.yaml`).

**Rule E5.** A held place has three rings:
1. **Outside** (out to 150 m, thinning to 500 m): patrols on foot and 1 to 2 hovering drones circling at 20 to 60 m.
2. **The shell**: turrets on roofs, corners and over the way in, in the upper half of the building's height and within the outer third of its plan; interior guns covering the entry hall, the hangar and any long corridor end.
3. **The inside**: guards spread through rooms at the E2 density, slightly heavier on the lower storeys.

The core goes deep (in the inner third of the plan, in the lower half of the height), sealed behind a locked door or solid wall, at least 10 m from the nearest loot cache. The existing `Dressing.cs` guards-at-the-entrance, turret-on-top and hover-at-the-door spawns fit rings 1 and 2. Add the spread through rooms and the core rule.

### 2.6 Patrols

7 Days to Die has no patrol routes inside places: everyone is a sleeper, and "patrols" are wandering hordes outside. Empyrion's are outside too (patrol groups in spawn zones, drones on circles round a base). **Rule E6 (GalHack's own).** Patrols belong to held places only: one walker per 2 to 3 guard rooms, on a loop through corridors (the architect's loop, ring and ward-loop circulation patterns make ready routes), never through a quiet room's interior. Abandoned and infested places have no patrols, only residents and wanderers.

### 2.7 Pacing with depth and height

Measured on 7 Days to Die volumes by height against the ground:

| Where | Volumes | Creatures per volume | Woken by a trigger | Tough groups |
|---|---|---|---|---|
| Below 3 m under ground | 1,199 | 1.58 | 22% | 16% |
| Ground (-3 to +3 m) | 4,143 | 1.41 | 11% | 7% |
| Upper (4 to 12 m) | 2,266 | 1.38 | 12% | 10% |
| High (13 m and up) | 769 | 1.87 | 20% | 24% |

By distance from the place's reward chest (quarters of each POI's volumes, nearest first): 1.61, 1.31, 1.42, 1.36 creatures per volume, and groups of four or more in 7% of the nearest quarter against 4% elsewhere. The reward chest itself sits in the inner half of the plan (median 0.57 of the way from edge to centre) and at about ground or first-floor level (median +2 m), but the hardest fights are at the extremes: the basement and the roof.

**Rule E7.** Danger rises with distance from the way in, either down or up: roughly double the share of tough or alert groups in the deepest or highest third of a place, and a modest bump (about 20% more per room) in the rooms next to the vault. This matches the generator's `Decay` wrongness, which already rises with depth and height. Tie the encounter tier to the same `TierAt(cell)`.

### 2.8 Ambushes, triggers, and hidden spawns (encounter features, not architecture)

These are tricks, not rooms. They are **excluded from every room-size figure in section 1**.

**Triggered rooms.** 1,132 of 8,377 volumes (14%) in 222 of 887 POIs (25%) stay asleep until the player crosses a separate trigger box (`TriggerVolume*`, `SleeperVolumeTriggeredBy`; volume flag value 3 goes with a trigger in 1,132 of 1,133 cases). Each triggered volume answers exactly one trigger. Triggers sit where the player's attention is held elsewhere: picking up loot, opening a door, stepping off a ladder, dropping through a floor.

**Closets.** 3.1% of 7 Days to Die markers stand in a sealed pocket of 12 m³ or less behind a door (a wardrobe, a cupboard, a storeroom), nearly all standing or sitting. **Ceiling cavities.** 1.7% lie or crouch in a 1 to 2 m void above a room's ceiling and drop through it. Small pockets (13 to 60 m³, 1.3%) and slots inside walls (rare) make up the rest: about **6% hidden in all**. Empyrion does the same at a larger share: 6.4% of spawner blocks sit in a pocket of one to three large blocks and 6.8% in a closet-sized pocket, about **13%**, plus 5.5% buried with no air next to them (they spawn into the room through the wall).

**Secret caches.** 7 Days to Die puts 94 safes behind paintings across its 887 encounter POIs, and 4,717 of 73,000 containers (6.5%) have no air next to them: buried in walls, floors or rubble, found by breaking in. Empyrion walls in 11% of containers and 72% of cores.

**Rule E8 (optional, use sparingly).** GalHack may use these, but as rules with tells, not as surprises:
- **Ambush rooms**: at most one room in four in a place is triggered, and only in places of middling danger and up. The trigger is the room's own bait (the cache, the lift call, the ladder top). The waking group comes from a room or pocket next to it, not from thin air.
- **Hidden spawns**: at most 5% of a place's spawns start in a closet, a ceiling void or a wall slot, and every one has a tell the player can learn (a door ajar, a stained or sagging ceiling tile, scratching, a smell cue). The generator already makes slivers into hidden rooms "with their tells"; reuse those tells.
- **Secret caches**: 1 in 10 places gets one, behind a breakable panel, a picture or a loose wall, using the existing hidden-room rule.

### 2.9 Loot placement (encounter-side; the per-category figures are in section 1)

- 7 Days to Die: containers per 100 m² of lot, p10 1.0, median 3.2, p90 8.3. Hospitals (median 7.8), offices (5.6), shops and fire stations (4 to 4.7) are richest; industry, caves, settlements (2.1) poorest. 47% of POIs have one big reward chest, and only 19% of those chests sit inside a volume flagged for loot: the reward is near the hardest fights, not inside them.
- Empyrion: containers per 100 m² of floor, median 2.4 on planet bases, 1.8 on space bases, 2.7 in wrecks. Most are in rooms or open halls; 11% walled in.

**Rule E9.** One reward cache per place of middling danger and up: in the inner half of the plan, one room past the hardest encounter on the route (so the fight guards it, but the cache is not in the fight's room). Ordinary containers at 2 to 4 per 100 m² of floor, 5 to 8 in stores, clinics and offices, 1 to 2 in industry and caves.

### 2.10 Checks for the spawns agent

A place passes when:
1. Every spawn is inside a room or corridor cell that the player can reach on foot (hidden spawns: their pocket opens onto a reachable room).
2. The quiet share is at least 20% of rooms (E3), and no run of three occupied rooms has no quiet one between, outside a guard zone.
3. Density per 100 m² of floor falls in the band for the place's danger tier (E2).
4. Spawns are at least 4 m apart.
5. In a held place: every turret is outdoors in the upper half and outer third; the core is in the inner third, lower half, sealed, and 10 m or more from the reward cache (E5).
6. The tough share in the deepest or highest third is at least 1.5 times that of the ground floor near the way in (E7).
7. At most one room in four is triggered and at most 5% of spawns are hidden, each with a tell (E8).
8. Each occupied room's group and pose come from its purpose (E4).

---

## 3. Tech level

*To follow.*

## 4. Map layout

*To follow.*

## 5. Space

*To follow.*

## 6. Generator checklist

*To follow.*
