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

*To follow in the next commit.*

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

**Secret caches.** 7 Days to Die puts 94 safes behind paintings across its 887 encounter POIs, and 4,717 of 72,000 containers (6.5%) have no air next to them: buried in walls, floors or rubble, found by breaking in. Empyrion walls in 11% of containers and 72% of cores.

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
