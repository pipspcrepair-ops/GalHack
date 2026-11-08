# World layers

How GalHack's worlds come from combinations of parts instead of bespoke entries. This splits every theme in `docs/world-themes.md` into combinable layers. That file stays as it is, as the source and the record of the owner's ideas; this one is what the generator should be built from. It's a plan: nothing here is in the game yet.

The owner's rule: "nothing should be bespoke, it should all be because a combination of things shaping unique situations and environments." And: "Every world feels different", "We're building on variables".

## 1. Summary

- **The result.** The world stack is now 32 layers, in six groups, with 800-odd values that each stand alone. Every table is in this file in a form a script reads, so the tables and the checks can't drift apart.
- **The 112 themes.** 47 dissolved completely into parts, which is the best result a theme can have. 31 stayed under their own name as a small mood, and 34 merged into one of those or into a new one. Twelve new themes and a `plain` make 44. Nothing the owner liked has gone: section 6.1 lists where each one ended up, and every one can still really be rolled.
- **The best discoveries.** Most old themes were a landmark, a fall and a living. `feature`, `fall` and `trade` carry the most themes, 60, 50 and 48 of the 112, and widening **trade** from six words to 36 did more than any other single change: Foundry Belt, Cannery World and Casino Dunes are mostly a trade. Two small new layers explain what nothing else could. Parade Ground and School Holidays are mostly **the moment it fell**, and Clockwork Hours and Drone Orchard are mostly **what's still running**.
- **Compatibility is rules, not lists.** Nonsense is excluded by a few dozen general rules about air, water, life, tech and place. With the rules off, about half of all worlds hold something impossible; with them on, none do. Rare breaks stay allowed on purpose.
- **Combinations.** Section 9 has the exact counts: about 1.5 × 10^15 over the core layers alone, and about 5 × 10^40 over all of them. In 200 rolled worlds no layer dominates, and two settled worlds share under a fifth of their single-value layers on average.
- **Compatibility with what exists.** The format is the overhaul's `world-layers.txt` flattened to one line per record, with the same keywords. The overhaul's region planner reads the same tables, and `docs/space-pois.md` gets one stable list of names to read (section 8.5).

## 2. The stack at a glance

| Group | Layer | Values | What it decides | Replaces |
| --- | --- | --- | --- | --- |
| Body | `zone` | 4 | Rim to core; gates factions, Hollow growth and the Husks' warping | Galaxy region |
| Body | `type` | 39 | The place to stand | The 40 types, ten of which moved |
| Body | `air` | 4 | What can live and be built; `air0` then, `air` now | "Toxic air" as a hazard only |
| Body | `climate` | 19 | Temperature and water | A line in each theme |
| Body | `land` | 32 | Landform bias, two per world | A line in each theme |
| Body | `mod` | 17 | Gravity, spin, rings, seasons, terraforming | Seven old types, and modifiers |
| People | `pre`, `post` | 3 each | Tech bands, old world and living | Profile, unchanged |
| People | `port` | 6 | The old main starport | Profile, unchanged |
| People | `people` | 4 | How many are alive now | Profile, unchanged |
| People | `trade` | 36 | What it lived on, and most of the region plan | Profile's six trades, widened |
| People | `ran` | 14 | Who ran the world | New |
| People | `build` | 25 | Architecture and building culture | New |
| People | `infra` | 20 | The networks, and the planner's style words | The `style` line of a theme |
| Fall | `phase` | 6 | Spared, fallen, mending, recovered, falling, untouched | Part of the apocalypse rule |
| Fall | `age` | 4 | Time since the fall, and so wear | Three fixed POI ages |
| Fall | `fall` | 82 | The apocalypse, primary and scar | The 70, plus 12 |
| Fall | `moment` | 17 | The moment it fell | New |
| Identity | `theme` | 44 | A small mood | The 112 |
| Present | `running` | 20 | What's still running | New |
| Present | `presence` | 35 | Factions, groups, what's working the place | Overhaul's presences, widened |
| Present | `creed` | 19 | What survivors believe | New |
| Present | `occ` | 17 | Per place: who's in it | 13 codes, plus 4 |
| Surface | `feature` | 94 | Landmarks and big works | Overhaul's features, widened |
| Surface | `hazard` | 54 | What hurts | A line in each theme |
| Surface | `flora`, `fauna` | 27, 38 | What kind of living is winning | A line in each theme |
| Surface | `sky`, `weather` | 25, 27 | What's up there, and the mood of the air | A line in each theme |
| Surface | `palette` | 24 | The world's colour mood | A line in each theme |
| Surface | `sound` | 22 | Music temperament and signature sound | Nothing: new |
| Surface | `marks` | 25 | Signs and marks | Nothing: new |
| Support | `dress` | 189 | Room dressing sets, keyed by `tags` | The `Inside` lines |

All the owner's extra world variables are in: time since the fall (`age`), who ran the world (`ran`), architecture (`build`), the moment it fell (`moment`), what's still running (`running`), native life (`flora`, `fauna`, and `life` on the type), terraforming state (`mod`), who's reclaiming it (`presence`), and day length and seasons (`mod`).

POI family weights have no layer of their own, on purpose. Any value can carry `poi`, `hub`, `industry` or `farm` weights, and they add up. So a family's odds on a world are the sum of what's true of that world, which is what the owner asked for.

## 3. Rules kept

- **No written logs or dialogue for now.** Where an old entry had a log, letter, ledger or voice, it's now an object, a place or a wordless sound. Marks are signs and tallies, not text. The only script is script nobody can read.
- **The warping stays subtle.** What the Husks do to a place (the tag `warped`, the occupant `WRP`, "something wrong here") is written only as what the player can notice. No `name` calls it anything, and `name` isn't player-facing anyway. The Hollow's growth (the tag `hollow`, which was `bloom`) is a separate, loud thing, kept to its own values and rare patches, and a whole Hollow world is rarer still (`flora.hollow_overrun`, owner's decision 8 in section 11).
- **No Earth, no real brands, no copying.** New names are ours, and animals are described by what they do. Two old names that echo other works are flagged in section 11.
- **Tech bands are low, mid and high.** The incomprehensible isn't a band. It appears only as a rare glimpse in the sky near the core.
- **No invading drones.** Machine presences hold their ground. A sentry stays on post and a harvester stays in its field, and neither comes for a base in waves.
- **The setting holds.** The collapse was fast, galaxy-wide and a century or two ago. A fall is how it played out here, never the one true cause. Mid-collapse is a phase of the same layers, not a list of its own.

## 4. The layer tables

Every table below is live data: the script reads these fenced blocks, so what you see here is what was rolled. Section 8 explains the line format. In short, one line is one record, fields are split by ` | `, and each field is the same keyword the overhaul's `LayerParser` already reads, or one of a few new ones. `name` is a phrase for debugging and sample worlds, not player-facing text.

### 4.1 The body: what the world is

These roll first and owe nothing to people. Position in the system comes from `docs/solar-systems.md`, which reads a type off its orbit zone. The `zones` field keeps that link; the roller here ignores it and uses `weight`, because it has no system to place a world in.

**Galaxy zone.** Where the world sits. It gates factions, the Hollow's growth, the Husks' warping and glimpses of the incomprehensible.

```layers
zone rim | name on the quiet rim | weight 38 | occ EMP 8
zone arm | name in the outer arms | weight 34 | is rough | occ SCV 3 HSK 3 | like fall.husks_fall*2 presence.husk_nights*2 | bias alien 0.05
zone inner | name in the inner arms | weight 20 | is rough | occ HSK 6 WRP 3 | like fall.husks_fall*4 presence.husk_nights*4 feature.precursor_plates*1.5 feature.vault_field*1.5 hazard.night_swarms*1.5 | bias alien 0.1
zone core | name near the core | weight 8 | is rough | occ WRP 8 HSK 10 | like theme.wrong_stars*2 fall.husks_fall*8 presence.husk_nights*8 feature.precursor_plates*2.5 feature.vault_field*2.5 hazard.night_swarms*2.5 sky.seam*2 flora.hollow_overrun*2 | bias alien 0.2
```

**World type.** A place to stand, not a story. Thirty of the old 40 types stay here, and ten moved to other layers (section 6.2). Nine are new, from the design doc and the owner's lists: places those named that had no type, such as the plateau, oasis, irradiated ice, dead and city-covered worlds. `air`, `temp`, `water`, `life` and `land` list what the type allows, and `settle` is the chance people ever lived there. Weights follow the old rarity: common 8, uncommon 4, rare 2.

```layers
type meadow | name a meadow world | was 1 | arch temperate.meadow | zones Z4 | weight 8 | air open | temp mild | water some wet | life mid high | land plains hills river_valleys lake_district coastal_cliffs terraces | settle 0.96 | start yes
type rainforest | name a rainforest world | was 2 | arch temperate.rainforest | zones Z3 Z4 | weight 7 | air open | temp mild hot | water wet | life high | land hills wetlands karst delta | settle 0.85 | start yes | bias overgrown 0.3
type swamp | name a swamp world | was 3 | arch new | zones Z3 Z4 | weight 4 | air open thin | temp mild hot | water wet | life mid high | land wetlands delta archipelago karst | settle 0.8 | start yes
type steppe | name a steppe world | was 4 | arch temperate.steppe | zones Z4 Z5 | weight 8 | air open thin | temp mild cold | water dry some | life mid | land steppe plains river_valleys plateau | settle 0.92 | start yes | bias stock 0.2 dry 0.2
type archipelago | name an archipelago world | was 5 | arch temperate.islands | zones Z4 | weight 4 | air open | temp mild hot | water sea | life mid high | land archipelago coastal_cliffs reef | settle 0.9 | start yes
type highland | name a high plateau world | was new | arch temperate.plateau | zones Z4 | weight 3 | air open thin | temp mild cold | water some dry | life mid | land plateau mountains hills karst terraces | settle 0.9 | start yes
type oasis | name an oasis world | was new | arch arid.oasis | zones Z3 | weight 3 | air open thin | temp hot mild | water dry some | life low mid | land rocky_desert lake_district mesas | settle 0.8 | start yes
type dune | name a dune world | was 7 | arch arid.dunes | zones Z2 Z3 | weight 8 | air open thin | temp hot | water dry | life low | land dune_sea rocky_desert salt_pan | settle 0.75 | start yes | bias dry 0.7
type canyon | name a canyon world | was 8 | arch arid.canyon | zones Z2 Z3 | weight 8 | air open thin | temp hot mild | water dry | life low | land canyonlands mesas badlands plateau | settle 0.78 | start yes
type salt_flat | name a salt flat world | was 9 | arch new | zones Z2 Z3 | weight 4 | air open thin | temp hot mild | water dry | life low | land salt_pan plateau rocky_desert | settle 0.7 | start yes | bias dry 0.7
type scorched | name a scorched world | was 10 | arch new | zones Z1 Z2 | weight 4 | air thin foul none | temp scorch | water none dry | life none low | land basalt_plains badlands crater_field | settle 0.5 | bias dry 0.7 sparse 0.4
type glacier | name a glacier world | was 11 | arch ice.glacier | zones Z5 Z6 | weight 8 | air open thin | temp frozen cold | water some | life low | land ice_plain mountains fjords | settle 0.7 | start yes
type tundra | name a tundra world | was 12 | arch new | zones Z5 | weight 8 | air open thin | temp cold | water dry some | life mid | land steppe hills lake_district ice_plain | settle 0.85 | start yes
type hot_spring_ice | name a hot-spring ice world | was 13 | arch ice.hot_spring | zones Z5 Z6 | weight 4 | air open thin | temp cold frozen | water some | life mid low | land ice_plain volcanic_field fjords | settle 0.72 | start yes
type deep_freeze | name a deep-freeze world | was 14 | arch new | zones Z7 Z8 | weight 4 | air thin none | temp frozen | water none | life none | land ice_plain crater_field | settle 0.3
type ocean | name an ocean world | was 15 | arch new | zones Z4 Z5 | weight 4 | air open | temp mild hot cold | water sea | life high | land archipelago reef coastal_cliffs | settle 0.8
type tide | name a tide world | was 17 | arch new | zones Z4 Z5 | weight 2 | air open | temp mild cold | water wet sea | life mid | land tidal_flats delta coastal_cliffs | settle 0.8 | start yes
type volcanic | name a volcanic world | was 18 | arch lava.volcanic | zones Z1 Z2 Z3 | weight 8 | air thin foul open | temp hot scorch | water dry none | life low | land volcanic_field basalt_plains mountains | settle 0.6
type lava_ocean | name a lava ocean world | was 19 | arch lava.lava_ocean | zones Z1 | weight 4 | air foul none | temp scorch | water none | life none low | land basalt_plains volcanic_field | settle 0.3
type ash_fall | name an ash-fall world | was 20 | arch new | zones Z2 Z3 Z4 Z5 | weight 4 | air thin foul open | temp mild cold | water dry some | life low | land basalt_plains plains badlands | settle 0.7 | start yes | bias ash 0.4 dead 0.3
type cratered_moon | name a cratered moon | was 21 | arch new | zones any | weight 7 | air none | temp hot mild cold frozen | water none | life none | land crater_field rilles | settle 0.5
type large_barren | name a large barren world | was 22 | arch new | zones Z1 Z2 Z6 Z7 Z8 | weight 7 | air none | temp hot cold frozen | water none | life none | land crater_field basalt_plains rift_valleys | settle 0.5
type barren_metal | name a barren metal world | was 25 | arch new | zones Z1 | weight 2 | air none | temp scorch hot | water none | life none | land basalt_plains shard_fields | settle 0.4
type ice_moon | name an ice moon | was 26 | arch new | zones Z6 Z7 Z8 | weight 7 | air none thin | temp frozen | water some | life none | land ice_plain rift_valleys | settle 0.5
type nascent | name a nascent world | was 28 | arch new | zones any | weight 2 | air foul none | temp hot scorch | water none dry | life none low | land basalt_plains rift_valleys volcanic_field | settle 0.15 | bias sparse 0.4
type toxic | name a toxic world | was 32 | arch new | zones Z2 Z3 | weight 4 | air foul | temp hot mild | water wet some | life low | land badlands wetlands plains | settle 0.6 | bias mutagen 0.3
type crystal | name a crystal world | was 33 | arch alien.crystal | zones Z5 Z6 Z7 | weight 4 | air open thin foul | temp cold mild | water dry some | life low | land shard_fields ringlands plains | settle 0.55 | bias alien 0.2
type fungal | name a fungal world | was 34 | arch alien.fungal | zones Z3 Z4 Z5 | weight 4 | air open thin foul | temp mild hot | water wet some | life mid | land dome_fields wetlands karst | settle 0.65 | bias fungal 0.6
type bioluminescent | name a glowing world | was 35 | arch new | zones Z4 Z5 | weight 2 | air open thin | temp mild cold | water wet some | life high | land hills karst wetlands | settle 0.6 | bias alien 0.2
type shell | name a hollow shell world | was 36 | arch new | zones Z2 Z3 Z4 Z5 Z6 | weight 2 | air thin open | temp mild cold | water dry some | life mid | land sinkhole_country plains | settle 0.6 | bias alien 0.1
type shattered | name a shattered world | was 37 | arch new | zones any | weight 2 | air thin none | temp cold mild | water none dry | life low none | land rift_valleys crater_field | settle 0.3
type storm | name a storm world | was 40 | arch new | zones Z3 Z4 Z5 | weight 4 | air open thin | temp mild hot | water some wet | life low | land mountains badlands plateau | settle 0.6 | bias storm 0.8
type irradiated_ice | name an irradiated ice world | was new | arch new | zones Z6 Z7 | weight 2 | air thin none | temp frozen | water some | life low none | land ice_plain shard_fields | settle 0.3
type cryo_fire | name a cryo-fire world | was new | arch new | zones Z7 Z8 | weight 2 | air foul | temp frozen | water some | life none low | land ice_plain lake_district | settle 0.25
type petrified | name a petrified forest world | was new | arch new | zones Z4 Z5 | weight 2 | air open thin | temp mild cold | water dry | life low | land plains hills canyonlands | settle 0.6 | bias dead 0.5
type rogue | name a rogue world with no sun | was new | arch new | zones deep | weight 1 | air thin none | temp frozen | water none some | life none low | land ice_plain basalt_plains | settle 0.3
type dead_world | name a dead world | was new | arch new | zones any | weight 3 | air thin foul | temp cold mild | water dry | life none low | land plains basalt_plains badlands | settle 0.9 | bias barren 0.6 dead 0.6 desolate 0.5
type city_covered | name a city-covered world | was new | arch new | zones Z4 | weight 2 | air open thin | temp mild hot cold | water some wet | life mid high | land plains river_valleys | settle 1 | only pre.hightech | bias sparse 0.5 barren 0.3
type arc | name a Precursor Arc | was new | arch new | zones deep | weight 1 | is deep | air open thin | temp mild | water some | life mid low | land plains ringlands | settle 0.05 | like feature.precursor_plates*20
```

**Air.** The most useful filter in the game. `air0` is the air people built under, `air` is the air now. A fall can change the second and not the first, which is how a gabled town ends up on a world you can't breathe on. `settle` scales the type's chance of people.

```layers
air open | name open breathable air | sets air0=open air=open | weight 8
air thin | name thin air | sets air0=thin air=thin | weight 5 | like hazard.thin_air*6
air foul | name foul air | sets air0=foul air=foul | weight 3 | is rough | settle 0.9 | like hazard.bad_air*12
air none | name no air | sets air0=none air=none | weight 3 | is rough | like hazard.vacuum*20
```

**Climate.** Sets the world's `temp` and `water` facts inside what the type allows.

```layers
climate scorch_bare | name scorching and bone dry | sets temp=scorch water=none | like hazard.heat*8 | bias dry 0.7
climate scorch_dry | name scorching with dry seas of dust | sets temp=scorch water=dry | like hazard.heat*8 | bias dry 0.5
climate hot_bare | name hot and waterless | sets temp=hot water=none | bias dry 0.7
climate hot_dry | name hot and dry | sets temp=hot water=dry | weight 3 | bias dry 0.5
climate hot_some | name hot with slow rivers | sets temp=hot water=some | bias dry 0.2
climate hot_wet | name hot and steaming | sets temp=hot water=wet | weight 2 | bias storm 0.2
climate hot_sea | name hot with warm seas | sets temp=hot water=sea | bias storm 0.2
climate mild_bare | name mild by day, waterless | sets temp=mild water=none | bias dry 0.7
climate mild_dry | name mild and dry | sets temp=mild water=dry | weight 2 | bias dry 0.5
climate mild_some | name mild with rivers and lakes | sets temp=mild water=some | weight 3 | bias dry 0.2
climate mild_wet | name mild and rainy | sets temp=mild water=wet | weight 2 | bias storm 0.2
climate mild_sea | name mild with open seas | sets temp=mild water=sea | weight 2 | bias storm 0.2
climate cold_bare | name cold and waterless | sets temp=cold water=none | bias dry 0.7
climate cold_dry | name cold and dry | sets temp=cold water=dry | weight 2 | bias dry 0.5
climate cold_some | name cold with frozen lakes | sets temp=cold water=some | weight 2 | bias dry 0.2
climate cold_wet | name cold and sodden | sets temp=cold water=wet | bias storm 0.2
climate cold_sea | name cold with grey seas | sets temp=cold water=sea | bias storm 0.2
climate frozen_bare | name deep frozen | sets temp=frozen water=none | like hazard.cold*8 | bias dry 0.7
climate frozen_some | name frozen, water locked in ice | sets temp=frozen water=some | like hazard.cold*8 | bias dry 0.2
```

**Land.** Landform bias. A world rolls two for now, and each region of the planner takes one. The first 24 are the code's landforms; the last eight are new.

```layers
land plains | name open plains | weight 3
land hills | name rolling hills | weight 2
land mountains | name mountain ranges | weight 2
land steppe | name open steppe | weight 2
land river_valleys | name river valleys
land lake_district | name a lake district
land delta | name river deltas
land plateau | name high plateaus
land coastal_cliffs | name coastal cliffs
land fjords | name fjords
land wetlands | name wetlands
land archipelago | name island chains
land karst | name karst towers and caves
land dune_sea | name dune seas
land rocky_desert | name rocky desert
land badlands | name striped badlands
land canyonlands | name canyon country
land mesas | name mesas
land basalt_plains | name basalt plains
land volcanic_field | name volcanic fields
land ice_plain | name ice plains
land shard_fields | name shard fields
land ringlands | name ringlands
land dome_fields | name dome fields
land crater_field | name crater fields
land rilles | name rilles and low ridges
land salt_pan | name salt pans
land tidal_flats | name vast tidal flats
land reef | name reefs and shoals
land rift_valleys | name rift valleys and slabs
land sinkhole_country | name sinkhole country
land terraces | name hillsides carved into terraces and canals | was 39
```

**Modifiers.** The owner's day length, seasons and terraforming variables live here, and so do seven old world types, because each is true of many bodies rather than being a body of its own. Up to two. Gravity, spin and rings really come from the system generator; here they roll after the profile, because the terraforming values need a people to have done it.

```layers
mod low_gravity | name low gravity | was 23 | weight 3
mod high_gravity | name high gravity, squat and heavy | was 27 | weight 3
mod tidally_locked | name one face to the sun, life in the twilight ring | was 29 | weight 2 | req type!=arc,rogue | like sky.stuck_sun*30
mod ringed | name rings overhead | was 30 | weight 2 | req type!=arc | like sky.ring_arc*30
mod giant_moon | name a moon of a giant | was 31 | weight 3 | req type!=arc,rogue | like sky.giant_overhead*30 hazard.radiation*3
mod tilted | name strong seasons, cool amber light | was 6 | weight 2 | req air!=none | like palette.amber_leaf*5
mod meteor_pelted | name pelted by meteors | was 24 | weight 2 | req air=none,thin | like hazard.meteors*20
mod year_thaw | name in the thaw of a centuries-long year | weight 1 | req air!=none water!=none temp=cold,mild
mod year_summer | name in the high summer of a centuries-long year | weight 1 | req air!=none temp=mild,hot
mod year_leaf | name in the leaf-fall of a centuries-long year | weight 1 | req life=mid,high air!=none
mod year_winter | name in the deep winter of a centuries-long year | weight 1 | req air!=none temp=mild,cold | like weather.blizzards*5 | bias sparse 0.2
mod short_days | name days of a few hours | weight 1
mod long_days | name days that last for weeks | weight 1
mod terraformed | name terraformed, and it took | weight 2 | is high settled | req air=open,thin life!=none | bias alien -0.1 overgrown 0.1
mod half_terraformed | name half terraformed, grid by grid | weight 2 | is high settled | req air=open,thin,foul | bias alien 0.05 sparse 0.2
mod fire_and_ice | name fire at the equator and ice at the poles | weight 2 | req air!=none | like feature.active_volcano*6
mod drained_sea | name a sea that went away | weight 2 | req water=dry,none temp!=frozen | like feature.stranded_ships*10 | bias dry 0.4
```

### 4.2 The people: who lived here and how

**Tech.** Bands only: low, mid, high. Beyond those is the incomprehensible, which is never a band; it's a rare `sky` value near the core. The two bands are independent, as the design says. Survivors usually live below the ruins round them, so `post` leans low, but a world can climb past its old self, rarely, and `docs/space-pois.md` relies on those rows. `drop` is how far it fell, with `up` meaning it rose.

```layers
pre L | name low tech | weight 3 | is low | req air0=open,thin | needs water_works | plant coal_plant 2 wind_farm 1 | hub survivor_hamlet 1 | like post.M*0.12 post.H*0.02 port.E*3 trade.subsistence*4 trade.farming*2 trade.herding*2 trade.mining*2 ran.elders*3 ran.great_houses*2 build.gabled_timber*2
pre M | name mid tech | weight 5 | needs sewage_works water_works substation | plant coal_plant 2 nuclear_plant 1 wind_farm 1 solar_farm 1 | hub meridian_apartments 1 | like post.H*0.08
pre H | name high tech | weight 4 | is hightech | needs sewage_works water_works substation telecom_exchange | plant fusion_plant 2 solar_farm 1 nuclear_plant 1 | hub meridian_apartments 1 ominous_tower 1 | like trade.subsistence*0.2 trade.herding*0.3 infra.tracks*0.2
post L | name low tech | weight 6 | req air=open,thin | bias working 0.4 stock 0.2
post M | name mid tech | weight 3
post H | name high tech | weight 1 | bias stock -0.1
```

**Port.** The old main starport. A and B leave things in orbit, and the orbit layer reads them.

```layers
port A | name a great port | weight 1 | req pre=H | region port_hub 1 | orbit highport yards stations traffic
port B | name a large port | weight 2 | req pre=H | region port_hub 1 | orbit yards stations traffic
port C | name a working port | weight 3 | req pre=M,H | region port_hub 1 | orbit stations
port D | name a poor port | weight 3 | req pre=M,H | region scrappy_pad 1
port E | name a frontier field | weight 3 | region scrappy_pad 1 | poi landing_clearing 1
port 0 | name no port at all | weight 2
```

**People now.** How many live here. Town counts come from the old world; this says who's left.

```layers
people none | name with nobody left | weight 2 | occ EMP 20 NST 6
people survivors | name with a few survivors | weight 5 | hub survivor_hamlet 2 | occ EMP 16 SCV 10 SQT 6
people outposts | name with scattered outposts | weight 2 | hub survivor_hamlet 1 | poi frontier_base 2 | occ SQT 8 SCV 8 EMP 10
people settled | name with settled country | weight 4 | hub survivor_town 2 survivor_hamlet 1 | occ SQT 18 SCV 6
```

**Trade: what it lived on.** The biggest dissolver. The profile's six trades stay in `base`, so `WorldProfile.Weight` still works; a region plan's towns, farmland, industry, hubs and most new POI families now hang here instead of on a theme. Many old themes were just a trade with a name.

```layers
trade subsistence | name small farms and local trade | base Agricultural | weight 7 | is crops | region hamlet 4 farmland 3 town 1 | hub survivor_hamlet 2 | farm old_farm 3 freeholder_farm 2 | poi water_mill 1 | streets 2 | bias stock 0.3 working 0.4
trade farming | name grain and field farming | base Agricultural | weight 6 | is crops | region town 2 farmland 4 hamlet 2 | hub meridian_apartments 1 | farm old_farm 3 grain_elevator 2 | poi ration_depot 1 seed_vault 1 | tags field_stores | streets 3 | bias stock 0.5 working 0.2
trade orchards | name orchards and pressing | base Agricultural | weight 3 | is crops | region town 1 farmland 4 hamlet 3 | farm orchard 3 old_farm 2 walled_orchard 1 | poi drone_hive 1 harvester_field 1 | tags press_cellars | streets 2 | bias stock 0.2
trade herding | name herding on open grass | base Agricultural | weight 3 | is outdoors rich_life | region hamlet 4 farmland 1 | farm yurt_camp 2 old_farm 1 | hub survivor_hamlet 1 | tags felt_camps | streets 1 | bias stock 0.8 working 0.3
trade greenhouses | name sealed greenhouse farming | base Agricultural | weight 2 | is mid | region town 1 farmland 3 | farm greenhouse_dome 3 | poi seed_vault 1 | tags grow_racks | streets 2
trade vat_stock | name meat grown in vats | base Agricultural | weight 2 | is high | region industry 2 town 1 | industry vat_hall 3 rendering_plant 2 | poi biolab 1 | tags vat_shapes | streets 2 | bias stock 0.2
trade fishing | name fishing and canning | base Industrial | weight 3 | is shore | region town 2 industry 2 | industry cannery 3 docks 2 rendering_plant 1 | hub docks 1 | tags tins labels | streets 2
trade mining | name mining | base Mining | weight 7 | region town 2 industry 3 | industry old_mine 3 abandoned_mine 2 ore_sorting_hall 2 | hub company_town 2 | depot rail_yard | tags pay_tickets | streets 2 | bias working 0.1
trade heavy_industry | name furnaces and heavy industry | base Industrial | weight 6 | is mid | region town 2 industry 4 | industry works_complex 4 steelworks 3 refinery 2 old_mine 1 coal_plant 2 | hub survivor_town 1 | depot rail_yard | tags shift_boards last_pour | streets 2 | orbit traffic
trade light_industry | name factories and assembly | base Industrial | weight 5 | is mid | region town 3 industry 3 | industry abandoned_factory 4 works_complex 1 | hub meridian_apartments 2 | depot rail_yard | tags assembly_stop | streets 4
trade fuel | name pumping and refining fuel | base Industrial | weight 4 | is mid | region town 1 industry 3 | industry refinery 3 pump_station 3 offshore_rig 1 | tags frozen_gauges | streets 1 | orbit traffic
trade power_export | name power for export | base Industrial | weight 3 | is mid | region town 1 industry 3 | industry dam_hydro 2 geothermal_plant 2 nuclear_plant 1 fusion_plant 1 wind_farm 2 solar_farm 2 substation 2 | tags switch_halls | streets 1
trade shipbuilding | name building ships | base Industrial | weight 3 | is high port | region town 2 industry 3 | industry abandoned_factory 2 works_complex 3 | poi crashed_freighter 2 | depot rail_yard | tags slip_cranes | streets 3 | orbit yards hull_frames
trade breaking | name breaking ships and scrap | base Industrial | weight 3 | is mid | region town 1 industry 3 | industry breaker_yard 3 steelworks 1 | poi scrap_fort 1 | tags cutting_rigs | streets 1
trade salt | name salt and minerals from dry seas | base Mining | weight 2 | is dry | region town 1 industry 2 | industry salt_works 3 refinery 1 | tags crystal_cargo | streets 1
trade ice_cutting | name cutting and shipping ice | base Mining | weight 2 | is cold | req water!=none | region town 1 industry 2 | industry ice_cutters 3 ice_outpost 2 | tags stove_rooms | streets 1 | orbit traffic
trade timber | name timber | base Industrial | weight 2 | is rich_life outdoors plants | region hamlet 3 town 1 industry 1 | industry abandoned_factory 1 | poi treehouse_village 1 | hub survivor_hamlet 1 | tags tool_lockers | streets 1 | bias working 0.3
trade digs | name digging up the old and huge | base Research | weight 2 | region hamlet 2 camp 3 | hub survivor_hamlet 1 | industry bone_dig_site 3 old_mine 1 | poi bone_dig_site 2 | tags casts | streets 1
trade research | name research | base Research | weight 5 | is mid | region town 2 industry 1 | hub research_base 2 | industry research_lab 3 meridian_research 1 biolab 1 | poi research_base 2 | tags bench_work | streets 2
trade star_survey | name watching the sky | base Research | weight 3 | is mid | region hamlet 2 camp 2 | hub research_base 1 | industry observatory 3 | poi telecom_exchange 1 volcano_observatory 1 | tags star_charts | streets 1
trade archives | name keeping the galaxy's records | base Research | weight 3 | is mid | region town 1 industry 2 | industry data_centre 3 archive_vault 3 telecom_exchange 1 | tags cold_racks | streets 1
trade learning | name schools and colleges | base Research | weight 3 | region town 2 hamlet 2 | hub school_campus 3 public_library 2 | industry school_campus 2 | tags lockers_notes | streets 3
trade medicine | name hospitals and care | base Commerce | weight 3 | region town 3 | hub hospital 3 | industry hospital 2 aid_station 2 hospice_ward 2 mortuary_deep 1 | tags wards | streets 3
trade administration | name running other worlds on paper | base Commerce | weight 3 | is mid | region town 4 | hub ministry_hall 3 ominous_tower 2 | industry archive_vault 2 telecom_exchange 1 | tags stamp_rooms | streets 5
trade military | name garrisons and arsenals | base Military | weight 5 | region town 2 industry 2 | hub barracks_town 3 military_base 2 | industry motor_pool 2 military_base 2 old_airfield 1 command_bunker 1 | tags mess_halls | streets 2 | orbit patrol fleet_parked
trade prison | name holding prisoners | base Military | weight 2 | region town 1 industry 2 | hub prison_block 2 | industry prison_block 3 alien_cellblock 1 | tags cells | streets 1
trade resort | name resorts and holidays | base Commerce | weight 3 | is outdoors | region town 3 | hub hotel_tower 3 casino_resort 1 | industry spa_springs 2 ski_lodge 1 ferry_terminal 1 | poi old_airfield 1 | tags pool_bars | streets 3
trade roadside | name roadside wonders and coach tours | base Commerce | weight 3 | is outdoors | region town 2 | hub theme_park 2 fairground 2 museum 1 | industry stage_hall 1 | tags gift_shops | streets 2
trade gaming | name gambling | base Commerce | weight 2 | region town 2 | hub casino_resort 3 hotel_tower 2 | industry exchange_market 1 | tags vault_rooms | streets 3
trade sport | name leagues and stadiums | base Commerce | weight 2 | is mid | region town 3 | hub stadium_camp 3 | industry gang_arena 1 | tags scoreboards | streets 3
trade art | name art and studios | base Commerce | weight 2 | is outdoors | region hamlet 3 town 1 | hub artist_colony 3 museum 1 | tags studios | streets 1
trade pilgrimage | name pilgrims and shrines | base Commerce | weight 3 | is outdoors | region hamlet 4 town 1 | hub pilgrim_hostel 3 church_chapel 2 monastery 2 | tags hostels | streets 1
trade parks | name parks and menageries | base Commerce | weight 2 | is rich_life | region town 1 hamlet 2 | hub zoo_menagerie 3 | industry research_lab 1 | tags keeper_rounds | streets 1
trade transit | name carrying people across the world | base Commerce | weight 4 | is mid | region town 3 industry 1 | hub terminus_station 2 metro_station 2 monorail_station 1 | industry rail_yard 3 maglev_depot 2 | depot rail_yard | tags ticket_halls | streets 3 | orbit traffic
trade signals | name relays and broadcasting | base Commerce | weight 2 | is mid | region hamlet 2 town 1 | hub telecom_exchange 2 | industry radio_mast_site 3 meridian_relay 1 | tags headsets | streets 1
trade markets | name trade and markets | base Commerce | weight 5 | region town 3 | hub exchange_market 3 shopping_mall 2 | industry exchange_depot 2 | tags market_stalls | streets 3 | orbit traffic
```

**Who ran the world.** An owner's variable. It sets the marks, the mood of the buildings and how the end was handled.

```layers
ran company | name run by a company | weight 6 | hub company_town 1 | like marks.brand_ghosts*4 build.corporate_block*3 | tags company_store
ran ministry | name run by a civil service | weight 5 | is mid | like marks.stamped_plates*5 build.concrete_slab*3 | poi ministry_hall 1 | tags stamp_rooms
ran generals | name run by the army | weight 3 | like marks.warning_stripes*3 build.army_prefab*4 | poi military_base 1 | tags mess_halls
ran founders | name run under the founders' charter | weight 4 | like build.ship_hull_town*3 build.pressure_dome*2 | tags founders_plaques
ran faith | name run by a faith | weight 3 | like creed.lamp_keepers*3 marks.prayer_flags*3 sound.bells*3 | poi church_chapel 1 | tags altars
ran guilds | name run by its guilds | weight 3 | like marks.strike_banners*3 | tags union_halls
ran great_houses | name run by a few great houses | weight 3 | like build.stone_keep*3 build.tiled_courtyard*2 | poi estate_house 1 | tags trophy_halls
ran machines | name run by mind-machines | weight 2 | is mid | like running.clerks*3 running.cleaners*3 | tags quiet_terminals
ran syndicate | name run by a syndicate | weight 2 | like presence.gangs*2 | tags protection_boxes
ran scholars | name run by its scholars | weight 2 | like trade.learning*4 | tags reading_rooms
ran elected | name run by an elected council | weight 4 | tags council_rooms
ran elders | name run by nobody much, village by village | weight 4 | is open | req pre=L,M | like build.gabled_timber*2 | tags village_stores
ran absentees | name owned from off-world | weight 3 | is port | like running.long_life_lamps*2 | tags company_store
ran alien_regime | name run by an alien regime | weight 1 | is rough faction | like trade.prison*6 build.vharn_grown*12 presence.vharn_garrison*3 | tags alien_orders
```

**Building culture.** The owner's architecture variable and the biggest single multiplier: the same hospital plan in a different skin. `kit` names the palette file, existing where one exists. `is open` means built under open sky, so it needs breathable original air; `is sealed` is the pressurised family.

```layers
build gabled_timber | name gabled timber and shingle | kit freeholder | weight 5 | is open | req pre=L,M
build stone_keep | name heavy stone and towers | kit concrete | weight 3 | is open | req pre=L,M | tags stone_halls
build ribbed_adobe | name ribbed adobe | kit new | weight 4 | is open dry | req temp=hot,mild
build stilt_timber | name timber on stilts | kit new | weight 3 | is open shore
build tiled_courtyard | name tiled courtyards | kit resort | weight 3 | is open | req temp=hot,mild
build earth_mound | name earth-sheltered mounds | kit new | weight 3 | is open
build felt_canvas | name felt and canvas | kit new | weight 2 | is open | req pre=L
build brick_terrace | name brick terraces | kit miningtown | weight 5 | is open | req pre=L,M
build pastel_estate | name pastel estates of identical houses | kit urban | weight 4 | is open mid
build concrete_slab | name brutalist concrete slab | kit concrete | weight 5 | is mid
build corporate_block | name clean corporate blocks | kit meridian | weight 4 | is mid
build glass_tower | name glass towers and skyways | kit urban | weight 3 | is high open
build arcology | name arcologies the size of towns | kit meridian | weight 2 | is high
build white_clinical | name white clinical halls | kit clinic | weight 2 | is mid
build resort_curve | name sweeping resort curves | kit resort | weight 2 | is open mid
build container_stack | name stacked containers | kit industrial | weight 4 | is mid
build army_prefab | name army prefab | kit military | weight 3 | is mid
build pressure_dome | name pressure domes and tubes | kit colonydome | weight 6 | is sealed mid | poi habitat_dome 2 greenhouse_dome 1 | tags dead_gardens
build sealed_module | name sealed modules and airlocks | kit colonydome | weight 6 | is sealed mid | poi habitat_dome 1 | tags airlock_rooms
build buried_warren | name buried tunnels and shop streets | kit mine | weight 4 | is sealed | poi undercity_district 2 fan_tower 1 | tags shop_streets
build ice_block | name cut ice and insulated block | kit ice | weight 3 | is cold
build undersea_domes | name domes on the sea bed | kit underwater | weight 2 | is sea mid | tags airlock_rooms
build ship_hull_town | name landed hulls turned into towns | kit shiphull | weight 2 | is port | poi colony_ship_wreck 1 | tags crew_decks
build scrap_weld | name welded scrap | kit scrap | weight 2 | is mid
build vharn_grown | name grown Vharn carapace | kit vharn | weight 1 | is rough | req ran=alien_regime
```

**Infrastructure.** What joins the places. Each value sets the `style` the planner already reads (`road`, `rail`, `power`), so nothing new is needed to use the first three words, and `net` names the extra network kind for the routes still to build.

```layers
infra road_web | name a web of highways | net none | weight 7 | style road haul rail none power pylons | is mid open overland
infra quiet_roads | name quiet country roads | net none | weight 7 | is open overland | style road quiet rail none power intact
infra tracks | name cart tracks and timber bridges | net none | weight 6 | style road track rail none power none | is open
infra port_funnel | name every road running to one port | net none | weight 3 | style road jammed rail derelict power port_only | is port open overland | region camp 3 scrappy_pad 4 | tags jam
infra rail_web | name a dense rail web | net rail | weight 5 | is open overland | style road quiet rail trunk power pylons | poi rail_yard 1 rail_wreck 1 signal_box_keep 1 terminus_station 1 | tags carriages
infra diesel_rail | name steam and diesel rail with water towers | net rail | weight 3 | is open overland | style road track rail trunk power none | req pre=L,M | poi water_tower_keep 1 | tags stage_stops
infra one_spine | name one great maglev line | net maglev | weight 2 | style road track rail trunk power pylons | is high open overland | poi maglev_depot 2 monorail_station 1 | tags ticket_halls
infra skyways | name skyways and ring roads | net skyway | weight 3 | style road haul rail trunk power intact | is high open overland | poi highway_interchange 2 metro_station 2 | tags tunnels_metro
infra canals | name canals, locks and aqueducts | net canal | weight 2 | style road track rail none power none | is flowing open | tags towpath_inns
infra ferries | name ferries and causeways | net ferry | weight 3 | style road track rail none power intact | is shore open | poi ferry_terminal 2 pier 1 | tags harbour_steps
infra boardwalks | name boardwalks over bog and water | net boardwalk | weight 2 | style road track rail none power none | is shore open | poi stilt_village 2 | tags boardwalk_homes
infra pipelines | name pipelines across the land | net pipeline | weight 3 | style road haul rail none power port_only | is mid | poi pump_station 2 | tags frozen_gauges
infra conveyors | name long ore conveyors and haul roads | net conveyor | weight 2 | style road haul rail derelict power port_only | is mid
infra airstrips | name a landing strip in every valley | net air | weight 2 | style road quiet rail none power intact | is mid open overland | poi old_airfield 2 aero_club 2 | tags hangar_bar
infra toll_roads | name toll roads between walled places | net none | weight 3 | style road quiet rail none power none | is open overland | poi toll_gate 2 | tags toll_boxes
infra pilgrim_ways | name walked ways with a well-house each day's march | net none | weight 2 | style road track rail none power none | is open | poi pilgrim_hostel 1 | tags hostels
infra hill_signals | name arm-towers signalling hill to hill | net signal | weight 2 | style road track rail none power none | is low open | tags signal_towers
infra ice_roads | name flagged routes and ice roads | net none | weight 3 | style road track rail none power none | is cold open
infra tube_links | name sealed tubes and rover routes | net tube | weight 6 | style road track rail none power port_only | is sealed mid | tags airlock_rooms
infra deep_tunnels | name tunnels with few ways up | net tunnel | weight 3 | style road track rail derelict power port_only | is mid | poi metro_station 1 fan_tower 1
```

### 4.3 The fall: what happened

The setting is fixed: the collapse was fast, galaxy-wide and a century or two ago, and each fall is how it played out here. None is ever explained as the one true cause.

**Phase.** How far along the fall is. Mid-collapse isn't a separate list; it's this value on any fall tagged `now`, which gives each one its own version of a world falling. `state` is the planner's table (active, abandoned, infested, ruined). `spared` is the frontier world that barely noticed: Horse and Diesel's "did not need a collapse", which the old stack had no way to say.

```layers
phase fallen | name still in ruins | weight 62 | blank yes | state active 1 abandoned 6 infested 1 ruined 3 | break 0.1 | occ EMP 26 SCV 8 NST 8 | bias severity 0.6 desolate 0.3 ferals 0.3
phase mending | name mending, slowly | weight 14 | req people=survivors,outposts,settled | state active 3 abandoned 5 infested 1 ruined 2 | break 0.12 | occ EMP 24 SCV 10 SQT 12 | tags patched | bias severity 0.45 living 0.3
phase recovered | name recovered, the fall showing only in spots | weight 60 | is mended | req post=M,H people=settled drop=0,1,up | state active 7 abandoned 2 infested 0 ruined 1 | break 0.15 | occ EMP 12 SQT 20 FAC 8 MCH 8 | tags memorials | bias severity 0.15 living 0.6
phase spared | name barely touched | weight 20 | is spared | req pre=L,M post=L,M people=survivors,settled,outposts drop=0,1,up | only fall.mild | state active 5 abandoned 3 infested 0 ruined 1 | break 0.1 | occ SQT 24 EMP 22 SCV 8 | bias severity 0.1 living 0.5
phase untouched | name never settled | weight 0 | req settled=no | occ EMP 70 NST 30
phase falling | name falling right now | weight 12 | is falling rough | req drop=0,1,up people=settled | only fall.now | state active 6 abandoned 3 infested 1 ruined 1 | break 0.08 | occ SQT 14 GNG 12 FAC 8 EMP 8 | tags front_barricades | bias severity 0.9 strife 0.7
```

**Time since the fall.** Wear on any world comes from this. Today POIs have three fixed ages, so `wear` is what replaces them.

```layers
age now | name in the last few months | wear 0 | req phase=falling | weight 1 | bias age 0
age recent | name within a generation, after holding on | wear 1 | req phase!=falling | weight 2 | bias age 0.3
age century | name about 110 years ago | wear 2 | req phase!=falling | weight 5 | bias age 0.65 overgrown 0.25
age long | name about 170 years ago | wear 3 | req phase!=falling | weight 5 | bias age 1 overgrown 0.45
```

**The fall.** The apocalypse layer: all 70 keep their ids and their entries in `docs/world-themes.md`, which stay the reference for what each leaves behind. Three groups of changes were needed. Requirements moved from `needs pre` to the general tags. `poi`, `occ`, `like` and `tags` hold the data form of each entry. `now` marks falls that can still be happening, `mild` marks those a spared world can have, and `stamp` says what it writes into the terrain. Z71 to Z82 at the end are new, from the gaps in section 10.

```layers
fall nuclear_war | name a nuclear exchange | group war | speed instant | weight 5 | is mid now | poi blast_crater 3 deep_bunker 3 command_bunker 2 missile_silo 2 nuclear_plant 1 fallout_shelter 3 shadow_wall 2 | occ SQT 10 GNG 10 MUT 10 | like hazard.radiation*10 weather.ash_fall*3 flora.dead_forest*4 fauna.mutants*3 | tags shadows sealed_doors tally_doors | stamp crater_ring | orbit debris | bias severity 0.35 strife 0.4 burnt 0.7 dead 0.4 barren 0.35 ash 1 mutagen 0.3 predators 0.2 fade 0.2
fall orbital_bombardment | name bombardment from orbit | group war | speed quick | weight 4 | is now | poi crashed_freighter 2 abandoned_spaceport 2 blast_crater 2 debris_camp 2 orbital_wreck_site 1 | occ SCV 10 NST 5 | like hazard.unstable_wrecks*5 feature.debris_line*5 sky.broken_stations*5 | tags vacuum_crews | stamp crater_line | orbit wrecks debris patrol | bias severity 0.25 strife 0.4 burnt 0.6 barren 0.2
fall invasion | name invasion and occupation | group war | speed quick | weight 4 | is now | poi military_base 2 vharn_outpost 2 vharn_sam_site 1 prison_block 2 scrap_fort 1 | occ FAC 10 GNG 6 | like hazard.mines*5 presence.resistance*5 marks.foreign_script*5 | tags ransacked_offices | orbit patrol | bias severity 0.25 strife 0.4 burnt 0.2
fall civil_war | name civil war | group war | speed season | weight 5 | is now | poi military_base 2 command_bunker 2 hospital 2 evacuation_camp 2 trench_line 2 | occ GNG 8 SCV 6 | like hazard.mines*4 presence.militias*5 marks.two_flags*6 | tags barricades field_hospitals | bias severity 0.25 strife 0.4 burnt 0.2
fall machine_uprising | name a machine uprising | group war | speed quick | weight 3 | is mid now | poi kessari_works 3 kessari_spire 2 abandoned_factory 2 sentry_line 2 | occ MCH 18 BOT 8 | like hazard.armed_hardware*5 presence.kessari*4 running.sentries*4 | tags welded_doors spotless_zones | bias severity 0.25 strife 0.4 burnt 0.2
fall chemical_war | name chemical war | group war | speed quick | weight 3 | is mid now rough | poi mask_depot 3 deep_bunker 2 refinery 1 hospital 2 | occ EMP 8 SCV 6 | sets air=foul | like hazard.bad_air*6 hazard.corrosion*3 | tags masks stained_streets | bias severity 0.35 strife 0.4 burnt 0.2 dead 0.5 barren 0.3 mutagen 0.2
fall kinetic_strikes | name rocks dropped from space | group war | speed instant | weight 3 | is now | poi blast_crater 3 old_mine 1 precursor_vault 1 | occ SQT 6 NST 4 | like hazard.unstable_ground*6 | tags tilted_towers | stamp crater_deep | orbit debris | bias severity 0.35 strife 0.4 burnt 0.5
fall blockade | name siege and blockade | group war | speed season | weight 3 | is port now | poi ration_depot 3 abandoned_spaceport 2 scrappy_port 2 command_bunker 1 | occ GNG 8 SCV 8 | like hazard.mines*3 presence.smugglers*5 | tags ration_shelves tally_doors | orbit fleet_parked patrol | bias severity 0.25 strife 0.4 burnt 0.2
fall drone_wars | name autonomous weapons fighting themselves out | group war | speed quick | weight 3 | is mid now | poi drone_factory 3 sentry_line 2 military_base 2 motor_pool 1 | occ MCH 14 BOT 10 | like hazard.armed_hardware*6 feature.machine_drifts*8 | tags spare_airframes | orbit fleet_parked | bias severity 0.25 strife 0.6 burnt 0.2
fall mercenary_seizure | name hired guns taking the cities | group war | speed quick | weight 3 | is now | poi scrap_fort 2 military_base 2 casino_resort 1 exchange_depot 2 | occ GNG 14 FAC 4 | like presence.warlords*5 infra.toll_roads*3 | tags gilded_forts | bias severity 0.25 strife 0.4 burnt 0.2
fall plague | name plague | group disease | speed quick | weight 6 | is now | poi hospital 3 evacuation_camp 2 burial_pits 3 biolab 1 | occ EMP 10 SQT 6 HSK 6 | like marks.plague_marks*8 fauna.ferals*2 | tags sealed_houses cot_halls | bias severity 0.15 desolate 0.3 ferals 0.3
fall biohazard | name a lab agent let loose | group disease | speed quick | weight 4 | is mid now | poi biolab 3 research_base 2 cryo_vault 1 hospital 2 bloom_garden 1 vat_hall 1 | occ MUT 12 NST 8 | like hazard.spores*5 hazard.mutagen*5 flora.swollen*5 fauna.mutants*5 | tags grown_through taped_corridors | bias severity 0.15 desolate 0.3 swollen 0.6 mutagen 0.6 predators 0.8
fall sleep_sickness | name a sickness of sleep | group disease | speed season | weight 3 | is now | poi hospital 2 hospice_ward 3 cryo_vault 1 sleeper_ward 2 routine_loop_street 1 | occ SLP 18 EMP 8 KPR 6 | like presence.routine_walkers*6 | tags sleep_wards | bias severity 0.15 desolate 0.3
fall frenzy | name a contagion of rage | group disease | speed quick | weight 3 | is rough now | poi hospital 2 prison_block 2 evacuation_camp 2 | occ GNG 6 MUT 8 | like presence.frenzied*6 | tags gouged_doors | bias severity 0.25 desolate 0.3 predators 0.3
fall husks_fall | name bodies that kept walking | group disease | speed quick | weight 3 | is mid rough now | poi cemetery_vault 3 church_chapel 2 hospital 2 mortuary_deep 1 | occ HSK 26 SQT 4 | like presence.husk_nights*8 hazard.night_swarms*6 marks.tally_marks*5 | tags churchyards | bias severity 0.25 desolate 0.6
fall fungal_takeover | name mould through the cities | group disease | speed season | weight 3 | is now | poi fruiting_hall 3 sewage_works 2 hospital 1 old_mine 1 | occ NST 12 MUT 6 | like hazard.spores*6 flora.fungal_giants*4 | tags mat_streets | bias severity 0.15 desolate 0.3 fungal 0.8 overgrown 0.2
fall sterility | name the births stopping | group disease | speed slow | weight 3 | is mild | poi school_campus 2 hospice_ward 2 survivor_town 1 | occ EMP 16 SQT 6 | like sound.silence*2 | tags empty_nurseries | bias desolate 0.3
fall parasite_swarm | name a swarm in the walls | group disease | speed season | weight 3 | is beasts | poi sewage_works 2 hospital 1 meridian_apartments 2 | occ NST 18 | like hazard.swarm_nests*6 | tags webbed_lifts | bias severity 0.15 desolate 0.3 predators 0.4 swollen 0.2
fall famine | name famine | group env | speed season | weight 5 | is now mild | poi grain_elevator 3 ration_depot 3 old_farm 2 evacuation_camp 2 | occ GNG 8 SCV 8 | like presence.gangs*2 | tags ration_shelves soup_kitchens | bias sparse 0.3 dead 0.2 stock -0.3
fall drought | name the rivers failing | group env | speed slow | weight 4 | is flowing air_weather mild | sets water=dry | poi water_works 3 water_tower_keep 2 drowned_cistern 2 caravan_stop 2 | occ GNG 6 SQT 6 | like hazard.thirst*6 weather.dust_storms*3 presence.water_lords*5 | tags tally_rods stranded_boats | bias dry 0.8 dead 0.3 sparse 0.4 barren 0.2
fall rising_seas | name the sea rising | group env | speed slow | weight 3 | is shore air_weather flood | sets water=sea | poi docks 2 sewage_works 2 barge_town 3 hospital 1 | occ SQT 8 NST 4 | like hazard.surges*6 feature.drowned_city*12 | tags waterlines | stamp flood | bias severity 0.15 storm 0.3
fall long_winter | name a winter that stayed | group env | speed season | weight 4 | is air_weather | req temp=mild,cold | sets temp=cold | poi ice_outpost 2 coal_plant 2 old_mine 1 ski_lodge 1 | occ EMP 6 SQT 6 | like hazard.cold*6 weather.blizzards*5 presence.fur_folk*3 | tags stove_rooms buried_cars | bias severity 0.15 sparse 0.3 storm 0.2
fall runaway_heat | name the air baking | group env | speed slow | weight 3 | is air_weather | req temp=mild,hot | sets temp=hot | poi water_works 2 deep_bunker 2 solar_farm 2 | occ EMP 8 | like hazard.heat*6 weather.fire_storms*3 | tags cellar_towns | bias severity 0.15 dry 0.5 dead 0.3 sparse 0.3
fall ash_year | name an eruption closing the sky | group env | speed instant | weight 3 | is air_weather now | poi coal_plant 1 old_mine 2 deep_bunker 2 | occ SQT 4 EMP 6 | like weather.ash_fall*8 flora.dead_forest*3 | tags ash_roofs | stamp ash | bias severity 0.25 dead 0.4 sparse 0.3 ash 0.6 barren 0.2
fall great_quake | name the ground moving | group env | speed instant | weight 3 | is now mild | poi highway_interchange 2 rail_yard 2 hospital 2 | occ SCV 6 | like hazard.unstable_ground*6 hazard.sinkholes*3 | tags tilted_towers | stamp fault | bias severity 0.1
fall air_loss | name the air thinning | group env | speed slow | weight 2 | req air0=open | sets air=thin | poi habitat_dome 3 greenhouse_dome 2 research_lab 1 | occ EMP 10 SQT 4 | like hazard.thin_air*6 | tags masks dead_gardens | bias severity 0.15 sparse 0.4 barren 0.3
fall poison_air | name a toxic haze from a thousand chimneys | group env | speed slow | weight 3 | is mid rough | req air0=open,thin | sets air=foul | poi refinery 2 steelworks 2 abandoned_factory 2 dust_mill 1 mask_depot 1 | occ SCV 4 | like hazard.bad_air*6 weather.acid_rain*4 | tags masks stained_streets | bias severity 0.25 dead 0.4 barren 0.35 mutagen 0.1
fall stellar_flare | name the star flaring | group env | speed instant | weight 3 | poi solar_farm 2 telecom_exchange 2 observatory 2 | occ EMP 10 | like hazard.radiation*5 | tags sunward_scorch | bias severity 0.25 burnt 0.3 dead 0.2
fall meteor_swarm | name years of meteors | group env | speed quick | weight 3 | is now | poi blast_crater 2 observatory 1 habitat_dome 1 | occ EMP 6 | like hazard.meteors*8 | tags holed_domes | stamp crater_scatter | bias severity 0.15 burnt 0.2
fall industrial_collapse | name factories eating the land | group env | speed slow | weight 3 | is mid mild | poi steelworks 2 refinery 2 coal_plant 2 acid_pool_works 1 | occ SCV 8 | like hazard.toxic_runoff*6 | tags slag_rivers | bias dead 0.2 dirt 0.3
fall pollinator_loss | name the pollinators stopping | group env | speed slow | weight 2 | is beasts crops mild | poi orchard 2 old_farm 2 greenhouse_dome 2 | occ SQT 4 | like flora.pollen_heavy*4 | tags bare_orchards | bias sparse 0.3 dead 0.2
fall grey_goo | name small machines eating everything | group tech | speed quick | weight 2 | is high rough | poi research_lab 2 kessari_works 1 data_centre 1 abandoned_factory 2 | occ EMP 10 | like hazard.grey_front*12 fauna.metal_eaters*5 | tags lace_steel | stamp grey_scar | bias severity 0.25 barren 0.5 dead 0.5
fall reactor_failures | name every plant failing at once | group tech | speed instant | weight 3 | is mid | poi nuclear_plant 3 fusion_plant 2 substation 2 evacuation_camp 2 | occ EMP 8 MUT 6 | like hazard.radiation*6 | tags alarm_rooms | bias severity 0.25 mutagen 0.4 predators 0.3 swollen 0.3
fall grid_cascade | name the lights going out together | group tech | speed instant | weight 4 | is mid now | poi substation 3 coal_plant 1 nuclear_plant 1 rail_yard 2 hospital 2 dam_hydro 1 | occ SCV 8 | like hazard.arc_flash*5 feature.pylon_forest*5 | tags candle_stubs | bias severity 0.25
fall ai_silence | name every mind-machine going quiet | group tech | speed instant | weight 3 | is mid now | poi data_centre 3 ministry_hall 2 abandoned_factory 2 telecom_exchange 2 | occ MCH 8 | like presence.kessari*2 | tags quiet_terminals | orbit sats | bias severity 0.25
fall terraform_failure | name the world-fixer breaking | group tech | speed quick | weight 2 | is high | like mod.half_terraformed*12 | poi weather_engine 2 research_lab 2 solar_farm 1 observatory 1 | occ SQT 4 | like hazard.strange_chemistry*5 marks.survey_stakes*6 | tags climate_towers | stamp terraform_grid | bias severity 0.15 alien 0.3 swollen 0.2 storm 0.3
fall weather_failure | name the weather control breaking | group tech | speed instant | weight 3 | is high air_weather | poi weather_engine 3 telecom_exchange 1 wind_farm 2 | occ EMP 8 | like weather.perpetual_storm*8 weather.dead_calm*3 hazard.lightning*5 | tags shelter_halls | bias severity 0.25 storm 0.8
fall teleport_accident | name the gates misfiring | group tech | speed instant | weight 2 | is high | poi gate_ruin 3 abandoned_spaceport 1 research_lab 1 | occ EMP 8 | like hazard.misplaced_things*5 | tags lost_luggage | bias severity 0.25 alien 0.2
fall drive_disaster | name a drive failing on its pad | group tech | speed instant | weight 2 | is high port | poi abandoned_spaceport 3 crashed_freighter 2 blast_crater 2 | occ SCV 8 | like hazard.radiation*3 hazard.unstable_wrecks*4 | tags melted_pads | stamp crater_ring | bias severity 0.25
fall cloning_mishap | name too many of everyone | group tech | speed quick | weight 2 | is high rough | poi clone_farm 3 hospital 2 cryo_vault 1 | occ MIM 10 GNG 4 | like presence.duplicates*6 | tags name_doubles | bias severity 0.25
fall memory_rot | name the records rotting away | group tech | speed slow | weight 2 | is mid mild | poi data_centre 2 public_library 2 archive_vault 2 ministry_hall 1 | occ EMP 6 | like marks.blanked_signs*6 | tags cold_racks
fall debris_cascade | name orbit filling with wreckage | group tech | speed quick | weight 3 | is high | poi crashed_freighter 2 debris_camp 3 observatory 1 abandoned_spaceport 2 | occ SCV 10 | like hazard.falling_debris*8 sky.drifting_hulks*5 feature.debris_line*4 | tags vacuum_crews | stamp wreck_furrow | orbit debris wrecks | bias severity 0.15
fall monoculture_failure | name one crop and one blight | group tech | speed quick | weight 2 | is mid crops | poi grain_elevator 3 old_farm 2 ration_depot 2 | occ SCV 6 | like flora.dead_fields*8 | tags field_stores | bias severity 0.15 dead 0.5 stock 0.2
fall exodus | name everyone trying to leave | group social | speed season | weight 6 | is port now | poi abandoned_spaceport 3 scrappy_port 3 evacuation_camp 3 processing_hall 2 luggage_field 2 | occ EMP 8 SCV 8 | like infra.port_funnel*5 hazard.unstable_wrecks*3 feature.jam_highways*6 | tags luggage queue_slips | orbit wrecks | bias severity 0.05 desolate 0.4 ferals 0.4 stock 0.2
fall lockdown | name walls going up | group social | speed quick | weight 3 | is now | poi quarantine_wall 3 sorting_gate 2 front_checkpoint 2 prison_block 2 | occ MCH 6 EMP 6 | like feature.quarantine_walls*6 marks.colour_zones*6 | tags newspapers_stand | bias severity 0.05
fall economic_collapse | name money ceasing to mean anything | group social | speed quick | weight 4 | is now mild | poi exchange_market 2 casino_resort 1 shopping_mall 2 exchange_depot 2 | occ GNG 6 SQT 6 | like presence.gangs*2 | tags dead_banks | bias severity -0.1 ferals 0.2
fall faith_takeover | name prophets filling the gap | group social | speed season | weight 3 | is now mild | poi church_chapel 3 monastery 2 hollow_shrine 1 alien_cathedral 1 | occ CLT 16 | like creed.lamp_keepers*2 marks.prayer_flags*3 | tags altars | bias severity -0.1
fall decreed_exodus | name an order for everyone to leave | group social | speed quick | weight 3 | is mid now | poi evacuation_camp 2 ministry_hall 2 abandoned_spaceport 2 | occ EMP 14 MCH 6 | like running.sentries*3 | tags door_notices | bias severity 0.05 desolate 0.4 ferals 0.3
fall general_stoppage | name everyone putting tools down | group social | speed quick | weight 3 | is now mild | poi steelworks 2 rail_yard 2 abandoned_factory 2 company_town 2 | occ SQT 6 | like marks.strike_banners*8 | tags picket_chairs | bias severity -0.1
fall enclave_wars | name gated estates fighting | group social | speed season | weight 3 | is now | poi wall_city 3 scrap_fort 2 meridian_apartments 2 estate_house 2 | occ GNG 8 | like hazard.crossfire*4 | tags trophy_halls | bias severity 0.05 strife 0.4
fall mutiny | name the workers rising | group social | speed quick | weight 3 | is now | poi company_town 3 ministry_hall 1 abandoned_factory 2 old_mine 2 | occ GNG 6 SQT 6 | like marks.strike_banners*6 ran.company*2 | tags meeting_halls | bias severity 0.05 strife 0.3
fall great_migration | name a whole people walking away | group social | speed season | weight 3 | is outdoors mild | poi evacuation_camp 2 caravan_stop 3 pilgrim_hostel 1 | occ EMP 14 | like marks.route_paint*6 | tags road_camps | bias severity -0.1 desolate 0.3
fall dark_patch | name the light dimming | group cosmic | speed slow | weight 3 | is mild | poi observatory 2 solar_farm 2 greenhouse_dome 2 | occ EMP 6 | like fauna.night_hunters*4 flora.glow_moss*5 | tags lamp_arrays | bias severity 0.05 alien 0.1
fall gravity_shift | name the pull changing in patches | group cosmic | speed instant | weight 2 | poi research_lab 2 crashed_freighter 1 space_elevator_base 1 | occ EMP 8 | like hazard.gravity_pockets*10 | tags marker_posts | bias severity 0.3 alien 0.1
fall rogue_body | name a dark world passing close | group cosmic | speed quick | weight 2 | is shore air_weather flood | sets water=sea | poi docks 2 observatory 1 lighthouse 1 | occ SQT 4 | like hazard.tides*6 feature.stranded_ships*3 | tags stranded_boats | stamp flood | orbit debris | bias severity 0.2 storm 0.3
fall time_stutter | name hours repeating in patches | group cosmic | speed quick | weight 1 | is warped | poi time_pocket 3 wrong_house 2 observatory 1 | occ EMP 14 WRP 8 | like hazard.time_slips*10 | tags repeat_rooms | bias severity 0.2 alien 0.15
fall dimensional_tear | name a seam opening overhead | group cosmic | speed instant | weight 1 | is warped rough | poi bloom_scar 1 alien_cathedral 1 observatory 2 | occ EMP 10 WRP 8 | like sky.seam*20 | tags bent_rooms | bias severity 0.4 alien 0.15
fall wormhole_tides | name passages opening at random | group cosmic | speed quick | weight 2 | is rough | poi crashed_freighter 2 gate_ruin 2 abandoned_spaceport 1 | occ SCV 8 | like hazard.misplaced_things*4 | tags lost_luggage | bias severity 0.3 alien 0.1
fall slow_fade | name a slow fade | group slow | speed slow | weight 5 | is mild | poi survivor_town 2 hospital 1 school_campus 1 | occ SQT 10 EMP 10 | tags one_open_street | bias severity -0.25 desolate 0.2 overgrown 0.2
fall emigration | name the young leaving | group slow | speed slow | weight 4 | is mild | poi school_campus 2 hospice_ward 1 survivor_town 2 | occ EMP 12 SQT 6 | tags one_open_street | bias severity -0.25 desolate 0.2 overgrown 0.2 ferals 0.2 stock 0.2
fall neglect | name nobody maintaining it | group slow | speed slow | weight 4 | is mild | poi water_works 2 rail_yard 2 dam_hydro 2 | occ KPR 6 SCV 6 | like hazard.collapsing_floors*5 | tags propped_stairs | bias severity -0.25 desolate 0.2 overgrown 0.3 dirt 0.3
fall supply_cutoff | name the ships ceasing to come | group slow | speed quick | weight 5 | is mild | poi exchange_depot 2 scrappy_port 2 ration_depot 2 | occ SQT 10 | tags ration_shelves | bias severity -0.25 desolate 0.2
fall ageing_colony | name machines wearing out first | group slow | speed slow | weight 3 | is mid mild | poi kessari_works 1 old_farm 2 habitat_dome 2 | occ SQT 6 KPR 6 | like hazard.failing_seals*6 | tags hand_parts | bias severity -0.25 desolate 0.2 dirt 0.2
fall vanishing | name everyone vanishing | group mystery | speed instant | weight 3 | poi survivor_hamlet 1 meridian_apartments 2 hospital 1 | occ EMP 28 WRP 6 | like running.long_life_lamps*3 sound.silence*3 | tags set_tables | bias severity 0.2 desolate 0.4 ferals 0.5 stock 0.3 overgrown 0.2
fall walked_out_apoc | name everyone leaving calmly at once | group mystery | speed quick | weight 3 | poi military_base 1 meridian_apartments 2 survivor_town 1 | occ EMP 24 | tags door_notices packed_bags | bias severity 0.1 desolate 0.4 ferals 0.4 stock 0.3 overgrown 0.2
fall silence | name sound failing | group mystery | speed instant | weight 1 | is warped | poi radio_mast_site 2 telecom_exchange 1 hospital 1 | occ EMP 10 | like sound.silence*12 marks.hand_signs*8 | tags clapperless_bells | bias severity 0.2 desolate 0.5
fall hollow_front | name the Hollow growth arriving | group mystery | speed season | weight 2 | is hollow rough now | poi bloom_scar 3 alien_hive_ruin 2 hollow_shrine 1 military_base 2 front_checkpoint 2 bloom_garden 1 | occ WRP 8 MUT 12 FAC 6 | like hazard.mutagen*6 feature.hollow_basin*8 flora.hollow_overrun*8 | tags grown_through | stamp hollow_region | orbit hollow | bias severity 0.2 desolate 0.4 fungal 0.8 overgrown 0.5 alien 0.2
fall folding | name a place folding onto itself | group mystery | speed instant | weight 1 | is warped rough | poi wrong_house 3 mirror_hall 2 time_pocket 1 | occ EMP 16 WRP 10 | like hazard.disorientation*10 feature.folded_district*10 | tags repeat_rooms | bias severity 0.3 desolate 0.4
fall replacement | name the people being swapped for copies | group mystery | speed quick | weight 1 | is warped rough | poi wrong_house 2 hospital 1 meridian_apartments 1 | occ MIM 20 EMP 10 | like presence.duplicates*4 | tags one_wrong_object | bias severity 0.2 desolate 0.4
fall long_sleep | name everyone entering cryo, and the vaults failing | group mystery | speed slow | weight 2 | is mid | poi cryo_vault 3 sleeper_barracks 3 hospice_ward 1 | occ SLP 18 EMP 14 KPR 4 | like running.cold_storage*6 | tags pod_rows | bias severity 0.1 desolate 0.4 ferals 0.3 overgrown 0.2
fall slide | name a slide into barbarism | group social | speed slow | weight 5 | is now mild | new Z71 | poi scrap_fort 2 survivor_town 2 wall_city 1 | occ GNG 12 SQT 8 | like presence.warlords*3 presence.gangs*3 | tags barricades | bias severity -0.1 strife 0.3 stock 0.2 working 0.3
fall liquidation | name the owners selling the world for parts | group social | speed season | weight 2 | is now | new Z72 | poi exchange_depot 3 breaker_yard 2 abandoned_factory 2 | occ SCV 8 FAC 4 MCH 4 | like ran.absentees*6 ran.company*4 marks.repossession_tags*10 | tags stripped_rooms | orbit traffic | bias severity 0.05 dirt 0.2
fall seam_fire | name fires under the ground | group env | speed slow | weight 2 | is now air_weather mild | new Z73 | poi burn_town 3 coal_plant 2 old_mine 2 | occ EMP 8 NST 4 | like hazard.burning_ground*10 hazard.bad_air*3 feature.burning_seams*10 | tags slumped_towns | bias burnt 0.6 dry 0.2 sparse 0.3
fall magnetic_flip | name the magnetic field failing | group cosmic | speed season | weight 2 | new Z74 | poi telecom_exchange 2 observatory 1 substation 1 | occ EMP 8 | like hazard.radiation*3 sky.aurora*8 fauna.lost_migrants*8 | tags dead_compasses | bias severity 0.2 storm 0.3 mutagen 0.1
fall mirror_burn | name an orbital mirror turning on its own cities | group tech | speed instant | weight 1 | is high | new Z75 | poi solar_farm 2 blast_crater 1 | occ EMP 10 | like sky.second_noon*10 hazard.glare*4 | tags sunward_scorch | stamp burn | orbit mirrors | bias severity 0.25 burnt 0.7 dead 0.3 dry 0.3
fall gene_drift | name children being born slightly changed | group disease | speed slow | weight 2 | is mid mild | new Z76 | poi biolab 1 school_campus 1 hospital 1 | occ MUT 6 SQT 8 | like creed.body_keepers*8 | tags height_marks | bias desolate 0.3 swollen 0.3 mutagen 0.3
fall broadcast | name a signal that changed how people behaved | group tech | speed quick | weight 1 | is mid warped | new Z77 | poi telecom_exchange 3 radio_mast_site 2 | occ WRP 6 CLT 6 EMP 8 | like running.broadcast*8 sound.radio_murmur*8 | tags cut_cables | bias severity 0.15
fall sinkage | name the ground under the cities giving way | group env | speed slow | weight 2 | is mild | new Z78 | poi sunk_town 3 water_works 1 metro_station 1 | occ NST 6 EMP 8 | like hazard.sinkholes*8 | tags tilted_towers | stamp pit
fall overreach | name a megaproject that broke the economy | group social | speed slow | weight 2 | is mid mild | new Z79 | poi works_complex 2 abandoned_factory 1 | occ EMP 10 SCV 4 | like feature.unfinished_works*14 | tags scaffolds | bias severity -0.1
fall last_ark | name the rich leaving in the only ship | group social | speed quick | weight 2 | is high port | new Z80 | poi abandoned_spaceport 2 estate_house 2 scrap_fort 1 | occ GNG 8 SQT 6 | like ran.great_houses*5 ran.company*3 | tags gilded_forts | bias severity 0.05 desolate 0.3
fall first_contact | name something landing and then leaving | group cosmic | speed quick | weight 1 | is rough | new Z81 | poi alien_cathedral 1 blast_crater 1 research_lab 1 | occ EMP 8 CLT 6 | like feature.long_furrow*5 | tags cordon_tents | bias severity 0.3 alien 0.2
fall tide_of_refugees | name more people arriving than it could feed | group social | speed season | weight 3 | is now port | new Z82 | poi evacuation_camp 3 aid_station 2 ration_depot 2 scrappy_port 1 | occ SQT 8 GNG 6 SCV 6 | like presence.refugees*8 | tags road_camps soup_kitchens | bias severity 0.05 strife 0.2 dead 0.1 sparse 0.2
```

**The moment it fell.** An owner's variable, and one of the best new layers. Parade Ground, School Holidays, Closed for the Season and half of the frozen-moment ideas turned out to be one value here. Slow falls have no moment, so they read `over decades`.

```layers
moment ordinary_evening | name on an ordinary evening, dinner on the table | weight 6 | is struck | occ EMP 6 | tags set_tables
moment rush_hour | name at rush hour | weight 4 | is struck | req pre=M,H | like feature.stopped_trains*2 | tags ticket_halls
moment shift_change | name at shift change | weight 4 | is struck | tags lockers_notes
moment deep_night | name in the dead of night | weight 4 | is struck | tags beds_made
moment festival | name in the middle of a great festival | weight 3 | is struck outdoors | poi fairground 2 stage_hall 2 | like marks.faded_bunting*10 feature.stalled_floats*6 | tags costumes stalls
moment match_day | name on the day of a final | weight 2 | is struck mid | like trade.sport*2 | tags scoreboards
moment term_time | name in the last week of term | weight 3 | is struck | poi school_campus 1 | tags lockers_notes
moment holiday | name in the off-season, everything closed for winter | weight 3 | is struck | tags dust_sheets
moment harvest | name at harvest | weight 3 | is struck | req settled=yes | tags field_stores
moment election | name during an election | weight 2 | is struck mid | tags council_rooms
moment wedding_season | name in the wedding season | weight 2 | is struck | tags set_tables
moment opening_day | name on the day before everything opened | weight 2 | is struck mid | tags dust_sheets
moment signing_day | name on the day a treaty was signed | weight 2 | is struck | poi border_checkpoint 1 | tags banquet
moment launch_day | name on launch day | weight 2 | is struck port | tags luggage
moment drill | name in the middle of a drill nobody knew was real | weight 2 | is struck mid | like marks.warning_stripes*2 | tags alarm_rooms
moment over_decades | name over months and decades | weight 6 | is gradual | tags one_open_street | blank yes
moment still_going | name and it's still going | weight 1 | req phase=falling
```

### 4.4 The present: who and what is here now

**What's still running.** An owner's variable. Where long-life power survives, places keep their schedule, so this also sets what changes by the hour.

```layers
running nothing | name nothing | weight 12 | blank yes | occ EMP 4 | like sound.silence*1.5 | bias desolate 0.3
running long_life_lamps | name long-life lamps in empty rooms | weight 5 | is mid | tags lamps_on
running timers | name timers: barriers, sprinklers and shift bells for no one | weight 4 | is mid | occ MCH 4 | tags afterhours
running transit_loops | name trams and trains on empty loops | weight 3 | is mid | occ MCH 6 | poi monorail_station 1 | like sound.rail_hum*5 | tags ticket_halls
running farm_machines | name farm machines still sowing and shipping | weight 3 | is mid crops | occ MCH 12 | poi harvester_field 2 drone_hive 2 | tags full_silos | bias stock 0.2
running factory_lines | name factory lines making stock for nobody | weight 3 | is mid | occ MCH 12 | tags stock_mountains
running vats | name vats still growing | weight 2 | is high | occ MCH 6 NST 4 | poi vat_hall 2 | tags vat_shapes
running sentries | name sentries still guarding | weight 5 | is rough mid | occ MCH 16 | poi sentry_line 2 | like hazard.armed_hardware*5 | tags dormant_racks
running clerks | name clerk machines still processing forms | weight 3 | is mid | occ MCH 12 | poi sorting_gate 1 | tags stamp_rooms
running cleaners | name cleaners and maintenance machines | weight 4 | is mid | occ MCH 10 | tags spotless_zones
running carers | name care machines tending wards | weight 2 | is mid | occ MCH 6 SLP 4 | tags sleep_wards
running broadcast | name a station still on the air | weight 3 | is mid | occ KPR 4 | poi radio_mast_site 2 | like sound.radio_murmur*5 | tags headsets
running banked_furnaces | name furnaces banked and still hot | weight 2 | is mid | like hazard.molten_pits*6 | tags last_pour
running automata | name brass automata keeping shop | weight 2 | is high | occ MCH 12 | poi clock_tower_town 2 puppet_theatre 2 | tags automata
running weather_plant | name a weather plant stuck on one setting | weight 2 | is high air_weather | like weather.perpetual_storm*3 | poi weather_engine 1
running life_support | name life support, barely | weight 5 | is sealed mid | occ SQT 6 KPR 4 | like hazard.failing_seals*3 | tags airlock_rooms
running cold_storage | name cold stores and sleeper vaults | weight 3 | is mid | occ SLP 8 | tags pod_rows
running beacons | name beacons and lighthouses | weight 3 | occ KPR 6 | poi lighthouse 2 lone_beacon 1 | tags lamp_rooms
running billboards | name powered billboards still selling | weight 3 | is high | tags still_selling
running hour_horn | name a great horn sounding once a day | weight 2 | is mid | like sound.hour_horn*20
```

**Presences and factions.** Who holds ground now, and who's working the place. Faction rules from the design hold: the rim has none, start worlds have none, and machine presences hold ground, they never come at a base in waves. The Husks' warping is written only as what the player can notice, and `name` never calls it anything.

```layers
presence vharn_garrison | name a Vharn garrison | weight 4 | is faction | occ FAC 18 | poi vharn_outpost 4 vharn_sam_site 2 | like marks.faction_sigils*3 | orbit patrol | bias strife 0.3
presence vharn_patrols | name Vharn crews in orbit | weight 2 | is faction | orbit patrol | bias strife 0.2
presence meridian_remnant | name a Meridian remnant | weight 4 | is faction settled | occ FAC 14 | poi meridian_garrison 2 meridian_relay 2 meridian_research 1 | orbit stations
presence exchange_traders | name Exchange traders | weight 4 | is faction living | occ SQT 6 | poi exchange_depot 3 exchange_market 2 | orbit traffic stations
presence kessari | name Kessari clans | weight 4 | is faction | occ MCH 12 | poi kessari_works 3 kessari_spire 3 | orbit relays
presence reaver_capital | name a Reaver capital ship overhead | weight 2 | is faction | occ GNG 8 | poi crashed_freighter 2 bog_wreck_field 1 | orbit pirates
presence reaver_salvage | name Reaver salvage crews | weight 3 | is faction settled | occ GNG 10 SCV 6 | poi scrap_fort 2 | orbit pirates
presence freeholders | name Freeholder settlers | weight 6 | is living | occ SQT 16 | poi freeholder_farm 3 frontier_base 1 | bias living 0.4 stock 0.2
presence gangs | name roving gangs | weight 6 | is settled rough outdoors | occ GNG 22 | poi scrap_fort 3 fuel_fort 1 gang_arena 1 | tags trophy_walls | bias strife 0.2
presence warlords | name warlords | weight 3 | is settled rough | occ GNG 14 SQT 6 | poi hilltop_keep 2 toll_gate 2 wall_city 1 | tags trophy_halls | bias strife 0.5
presence militias | name town militias | weight 3 | is living | occ SQT 8 GNG 6 | poi scrap_fort 1 | tags barricades | bias strife 0.3
presence resistance | name resistance cells | weight 0.6 | is living rough | occ SQT 6 FAC 4 | tags hidden_cellars | bias strife 0.4
presence smugglers | name smugglers | weight 2 | is living | occ SCV 8 | tags hidden_cellars
presence water_lords | name water lords | weight 2 | is living dry rough | occ GNG 12 | poi water_tower_keep 3 | tags tally_rods
presence caravaners | name caravaners | weight 3 | is living outdoors | occ SCV 10 | poi caravan_stop 2 | tags caravan_gear | bias working 0.5
presence fisher_folk | name fisher folk | weight 3 | is living shore | occ SQT 10 | poi barge_town 1 pier 1 | tags net_lofts | bias living 0.3
presence fur_folk | name fur-clad hamlets | weight 2 | is living cold outdoors | occ SQT 10 | poi ice_cutters 1 | tags stove_rooms | bias living 0.2 working 0.2
presence pale_folk | name pale people from below | weight 2 | is living | occ SQT 8 NST 4 | poi undercity_district 2 | tags shop_streets
presence hermits | name lone hermits | weight 5 | is settled | occ SQT 3 SCV 3 | poi hermit_cabin 2 | tags hermit_camps
presence keepers | name keepers who never left | weight 4 | is living | occ KPR 12 | tags duty_rosters | bias living 0.2
presence pilgrims | name pilgrims | weight 3 | is living outdoors | occ CLT 10 | poi pilgrim_hostel 1 | tags hostels
presence cultists | name a sect | weight 2 | is living | occ CLT 14 | poi hollow_shrine 1 church_chapel 1 | tags altars
presence refugees | name refugees | weight 2 | is living | occ SQT 10 SCV 6 | poi evacuation_camp 2 aid_station 1 | tags road_camps | bias strife 0.1 living 0.2
presence debris_diggers | name debris diggers | weight 2 | is living | occ SCV 12 | poi debris_camp 2 | tags hermit_camps
presence bandit_riders | name riders on the old roads | weight 2 | is living rough low outdoors | occ GNG 10 | tags trophy_walls | bias strife 0.3 working 0.3
presence hunters | name hunters and guides | weight 2 | is living beasts | occ SCV 6 SQT 4 | tags trophy_halls | bias living 0.2
presence routine_walkers | name sleepers walking their old routines | weight 0.2 | is settled | occ HSK 6 | poi routine_loop_street 2 | tags routine_scenes
presence husk_nights | name husks abroad after dark | weight 0.4 | is settled mid rough | occ HSK 22 | poi cemetery_vault 1 | tags churchyards | bias desolate 0.2
presence frenzied | name the frenzied, still wandering | weight 0.2 | is settled rough | occ MUT 6 GNG 6 | tags gouged_doors | bias strife 0.3
presence duplicates | name people who are all the same person | weight 0.2 | is settled rough | occ MIM 12 | tags name_doubles
presence reclaimers | name crews come back to reclaim it | weight 3 | is mended | occ SCV 6 FAC 4 | tags patched | bias living 0.3
presence precursor_guardians | name silent guardians | weight 0.4 | is rough | occ GRD 20 | poi precursor_vault 2 | orbit precursor
presence machine_lords | name a lone machine that thinks it owns the place | weight 1 | is mid rough | occ MCH 10 | tags spotless_zones
presence hollow_shore | name shore towns at the edge of the Hollow growth | weight 1 | is hollow living rough | occ WRP 6 SQT 6 MUT 8 | poi bloom_scar 1 | orbit hollow | like flora.hollow_overrun*3 | bias fungal 0.3
presence listening | name something listening | weight 2 | is warped | occ WRP 16 | poi hollow_shrine 1 | tags patterned_objects
```

**Creeds.** Survivors now have beliefs as well as places, which the variety scout found to be the biggest gap on the people side. A creed changes dressing, dress, stock and rules, and shows only in behaviour and objects, never in words.

```layers
creed no_creed | name nothing in common | weight 16 | blank yes
creed the_numbered | name holding inherited places in a queue that hasn't moved | weight 2 | is living | like feature.jam_highways*3 | tags queue_slips
creed the_unmoving | name keeping the last table exactly as it was | weight 3 | is living | tags set_tables
creed stampers | name trading by forms they can't read | weight 2 | is living | tags stamp_rooms
creed knockers | name talking through the pipes | weight 2 | is living | tags pipe_taps
creed festival_keepers | name still holding a smaller festival each year | weight 2 | is living outdoors | tags costumes
creed horn_pausers | name stopping to look up at the hour horn | weight 1 | is living | like running.hour_horn*6
creed lamp_keepers | name keeping one lamp lit in every town | weight 3 | is living | tags lamp_rooms
creed cargo_builders | name building timber ships and waiting | weight 2 | is living port outdoors | poi timber_fleet 2 | tags timber_ships
creed sealed_believers | name sure the outside air is death | weight 2 | is living | poi deep_bunker 1 | tags trade_hatch
creed coreward_leaners | name building everything to face one way | weight 2 | is living warped | poi hollow_shrine 2 | tags pilgrim_cots
creed sky_counters | name counting nights and lights in the sky | weight 2 | is living | poi observatory 1 | tags star_charts
creed seed_keepers | name guarding seed they may never plant | weight 3 | is living crops | poi seed_vault 2 | tags seed_rooms
creed road_menders | name keeping up one perfect stretch of road | weight 2 | is living outdoors | tags road_crews
creed ancestor_tenders | name tending the old dead before the living | weight 3 | is living | poi cemetery_vault 1 tomb_mound 1 | tags altars
creed machine_kin | name treating old machines as cousins | weight 2 | is living mid | occ MCH 4 | tags hand_parts
creed body_keepers | name marking children's heights on every door | weight 1 | is living | tags height_marks
creed mender_faith | name holding that everything must be repaired, never replaced | weight 2 | is living | tags hand_parts
creed tally_keepers | name counting days on every wall | weight 2 | is living | tags tally_doors
```

**Occupancy.** Rolled once per place, last of all, from the world's added-up mix. Empty stays a real result. Each layer adds or takes shares, so a world's mix is what its parts add up to, not a hand-written row. The old per-theme table remains in `docs/world-themes.md` as the record of intent. Four codes are new, because the old table was bending existing ones to fit: sleepwalkers aren't husks, guardians aren't machines. Code note: `Occupancy.cs` still reads `SHB` for what this doc calls `HSK`, and has only nine codes.

```layers
occ EMP | name empty
occ NST | name nesters | is beasts
occ HSK | name husks | is mid
occ SCV | name scavengers passing | is living
occ SQT | name squatters and settled survivors | is living
occ GNG | name gang-held | is living
occ FAC | name faction-held | is faction
occ MCH | name machine-held | is mid
occ CYB | name cyborgs | is high
occ BOT | name feral robots | is mid
occ WRP | name Husk-warped: something wrong here | is warped
occ CLT | name cultists and pilgrims | is living
occ MUT | name mutated predators | is beasts
occ KPR | name keepers, on a duty that outlived its reason | is living
occ SLP | name sleepers, in pods or beds or on their feet | is settled
occ GRD | name guardians of a far older place
occ MIM | name mimics and copies | is settled
```

### 4.5 The surface: what you meet

**Features and landmarks.** One to three per world. Natural ones need no people; the rest are works anyone could have built, each usable on any world that passes its rules. Everything big enough to be seen from orbit or a vessel lives here. `stamp` writes into terrain, as the world-types section already planned.

```layers
feature buttes | name buttes on skirts of scree | weight 4 | is dry air_weather | stamp rock
feature rock_spires | name jagged spires and volcanic necks | weight 4 | stamp rock
feature columnar_towers | name fluted columnar towers | weight 3 | stamp rock
feature hoodoos | name hoodoos with balanced cap stones | weight 3 | is dry air_weather | stamp rock
feature beehive_domes | name clusters of banded beehive domes | weight 3 | is air_weather | stamp rock
feature lone_rock | name one huge smooth lone rock | weight 3 | stamp rock
feature cliff_pulpits | name cliff-top pulpits over water | weight 2 | is flowing | stamp rock
feature natural_arches | name natural arches | weight 3 | is air_weather | stamp rock
feature rainbow_strata | name rock striped in vivid colours | weight 3 | is air_weather | stamp rock
feature sea_stacks | name sea stacks | weight 2 | is shore | stamp rock
feature crystal_ridges | name sharp faceted crystal ridges | weight 2 | req type=crystal,irradiated_ice,barren_metal,dead_world | poi crystal_garden 2 | tags crystal_grown
feature geyser_basin | name a geyser basin | weight 3 | is flowing | poi spa_springs 1 geothermal_plant 1 | like hazard.scalding*6 | tags steam_halls
feature active_volcano | name an active volcano on the horizon | weight 2 | req temp!=frozen | poi volcano_observatory 1 | like hazard.eruptions*6
feature sinkhole_chain | name a chain of sinkholes | weight 3 | poi old_mine 1 | stamp pit
feature great_falls | name great falls off an escarpment | weight 2 | is flowing air_weather | poi dam_hydro 1 water_mill 1
feature crater_lake | name a crater lake | weight 2 | is flowing air_weather | stamp crater_deep
feature tar_flats | name tar flats | weight 2 | is hot air_weather
feature mirror_lakes | name still mirror lakes | weight 3 | is flowing air_weather | poi pier 1
feature methane_lakes | name methane lakes and ice burning blue at the vents | weight 1 | req type=cryo_fire
feature glowing_caves | name glowing cave mouths | weight 3 | is plants | tags crystal_grown
feature fungal_deep | name glowing fungus in collapsed caves | weight 3 | is plants | poi fruiting_hall 1 crystal_garden 1 | like fauna.blind_crawlers*5
feature crystal_fields | name crystal flowers across the plains | weight 2 | poi crystal_garden 3 | like weather.glass_sleet*3 hazard.sharp_ground*6 | tags crystal_grown
feature petrified_trunks | name stone trees a kilometre tall | weight 1 | req type=petrified
feature giant_bones | name giant fossils in the rock | weight 3 | req life!=none | poi bone_dig_site 2 | tags casts
feature kelp_mats | name floating mats that thicken the sea | weight 2 | is sea plants | poi offshore_rig 1 barge_town 1 | like hazard.entanglement*6
feature coral_land | name reefs walking up the shore | weight 2 | is shore beasts | poi pier 1 | like fauna.reef_stalkers*10
feature hive_mounds | name mounds to the horizon | weight 3 | is beasts dry | poi alien_hive_ruin 2 mound_city 2 | like fauna.hive_castes*12
feature thorn_maze | name hedges grown into a maze of thorn | weight 2 | is plants settled | poi maze_manor 2 estate_house 1 | like hazard.getting_lost*6 flora.thorn_lanes*10
feature herd_giants | name herds so large they wear their own roads | weight 2 | is beasts rich_life | like fauna.giants*10 hazard.stampedes*6
feature green_valley | name one valley still green | weight 0.3 | is plants settled | req water=some,wet | poi walled_orchard 2 freeholder_farm 2 | tags seed_rooms
feature colony_hull | name a whole colony ship standing on the ground | weight 2 | is settled high | poi colony_ship_wreck 3 ark_dome 2 seed_vault 1 | tags crew_decks | orbit stations
feature fallen_elevator | name a fallen space elevator across a continent | weight 1 | is settled high bigport | poi space_elevator_base 3 | like hazard.unstable_wrecks*3 | tags climber_cabins | orbit elevator_stump
feature big_gun | name a single gun the size of a mountain range | weight 1 | is settled high rough | poi breech_hall 3 ammunition_hall 2 meridian_railgun 2 | stamp furrow | tags gun_halls
feature launch_fields | name makeshift launch rails along the shore | weight 2 | is settled port | poi launch_rail 3 scrappy_port 1 | tags hull_cabins
feature launch_ramp | name a mass-launch ramp ending in open air | weight 1 | is settled high | tags slip_cranes
feature mega_dam | name a dam holding back a dead reservoir | weight 2 | is settled mid flowing | poi dam_hydro 3 | tags switch_halls
feature open_pits | name stepped open pits and spoil heaps | weight 3 | is settled | poi old_mine 2 | stamp pit | tags pay_tickets
feature missile_grid | name silos in rows, hatches ajar | weight 2 | is settled mid | poi missile_silo 3 command_bunker 1 | tags launch_consoles
feature sentry_lines | name turret lines across every pass | weight 2 | is settled mid rough | poi sentry_line 3 | like running.sentries*4
feature quarantine_walls | name walls slicing the land into zones | weight 2 | is settled open | poi quarantine_wall 3 sorting_gate 1 | tags newspapers_stand
feature inward_fences | name gates and fences facing inward | weight 2 | is settled | poi company_town 1 prison_block 1 | tags pay_tickets
feature trench_lines | name line after line of trenches | weight 3 | is settled open | poi trench_line 3 battlefield_memorial 2 motor_pool 1 | tags dugouts
feature border_line | name identical checkpoints along two roads that never join | weight 2 | is settled open | poi border_checkpoint 3 | tags banquet
feature debris_line | name wreckage in long lines along an old orbit | weight 3 | is settled mid | poi orbital_wreck_site 2 debris_camp 1 | stamp wreck_furrow | tags vacuum_crews | orbit debris
feature wreck_graveyard | name wreck fields from old wars | weight 2 | region wreck_field 3 | poi crashed_freighter 3 | stamp wreck_furrow | tags vacuum_crews | orbit wrecks | like presence.reaver_capital*4 presence.debris_diggers*3
feature crash_sites | name ships that came down here | weight 3 | region wreck_field 2 | poi crashed_freighter 4 | tags flooded_holds | like presence.reaver_salvage*3
feature sunk_ships | name ships sunk nose-first in the mire | weight 2 | is shore | poi bog_wreck_field 3 crashed_freighter 2 | region wreck_field 1 | tags flooded_holds | like presence.reaver_capital*4 presence.debris_diggers*3
feature stranded_ships | name ships left high and dry on a plain | weight 2 | is dry air_weather settled | poi docks 2 beached_liner 2 | tags ballrooms
feature wreck_beach | name a beach of fallen hulls | weight 2 | is shore settled | poi wreck_beach 3 | tags hull_cabins
feature beached_liners | name liners run aground in a last rush | weight 2 | is shore settled mid | poi beached_liner 3 | tags ballrooms
feature jam_highways | name highways jammed nose to tail for hours of walking | weight 3 | is settled mid open overland | poi highway_interchange 1 | tags jam
feature stopped_trains | name a stopped train on every line | weight 3 | is settled open overland | poi rail_wreck 2 | tags carriages
feature stalled_floats | name parade floats stalled on the highways | weight 1 | is settled mid open overland | tags costumes
feature giant_statues | name giant roadside statues and shaped buildings | weight 2 | is settled outdoors | poi theme_park 1 | tags gift_shops
feature mast_forest | name a forest of radio masts | weight 3 | is settled mid | poi radio_mast_site 3 | tags headsets
feature turbine_ranks | name wind turbines tuned to sing | weight 2 | is settled mid air_weather | poi wind_farm 2 sound_garden 2 | like sound.wind_chimes*10
feature pylon_forest | name pylon lines thick as hedges | weight 3 | is settled mid open | poi substation 2 | tags switch_halls
feature bell_towers | name a bell tower on every height | weight 2 | is settled air_weather open | poi monastery 1 | like sound.bells*10
feature graveyard_hills | name a graveyard on every hill | weight 2 | is settled outdoors | poi cemetery_vault 2 | tags churchyards
feature standing_stones | name downs of standing stones and mounds | weight 2 | is outdoors | poi tomb_mound 3 megalith_ring 1 | tags grave_goods
feature pipe_racks | name pipes crossing everything | weight 2 | is settled mid | poi pump_station 2 | tags frozen_gauges
feature burning_seams | name ground burning from below for fifty years | weight 2 | is settled air_weather | poi burn_town 2 | tags slumped_towns
feature drowned_city | name a drowned city with rooftops for islands | weight 2 | is settled shore | poi barge_town 2 drowned_cistern 1 | stamp flood | tags waterlines
feature sunk_town | name a town half sunk in sand | weight 2 | is settled dry air_weather | poi sunk_town 3 | tags sand_halls
feature glass_plains | name plains fused to glass | weight 2 | is mid | poi shadow_wall 1 | like hazard.glare*5 hazard.radiation*3 | stamp glass | tags shadows
feature dead_city_ash | name endless city ruins under ash | weight 2 | is settled open | poi ominous_tower 2 | tags ash_roofs
feature overgrown_city | name a city to the horizon, in leaf | weight 3 | is settled plants rich_life mid | poi ominous_tower 2 shopping_mall 2 metro_station 1 highway_interchange 1 | like flora.reclaiming_forest*8 | tags trees_indoors
feature machine_drifts | name dead war machines lying in drifts | weight 2 | is settled mid | poi motor_pool 1 | tags cockpits
feature dismantled_arsenal | name yards of neatly dismantled weapons | weight 2 | is settled mid | poi breaker_yard 2 | tags last_gun
feature high_marks | name huge shapes scraped into the plain | weight 2 | is settled open | stamp furrow
feature long_furrow | name a dead-straight furrow ending at nothing | weight 1 | stamp furrow | tags study_camp
feature warning_country | name land made unpleasant on purpose, one sealed door at the centre | weight 2 | is settled mid | tags warning_spikes
feature dummy_town | name a mannequin town built to be destroyed | weight 2 | is settled mid open | poi flavour_house 2 | tags mannequins
feature timber_ships | name a field of full-size timber starships | weight 1 | is settled port outdoors | poi timber_fleet 3 | tags timber_ships
feature good_kilometre | name one perfect stretch of road | weight 1 | is settled living outdoors | tags road_crews
feature waiting_town | name a town finished and never moved into | weight 2 | is settled mid | tags dust_sheets
feature unfinished_works | name a vast thing never finished | weight 2 | is settled mid | poi works_complex 1 | tags scaffolds
feature visitor_centre | name a ticket booth round something far older | weight 1 | is settled mid | poi alien_library 1 precursor_vault 1 | tags gift_shops
feature simulated_voyage | name a buried hull that never left the ground | weight 1 | is settled mid | poi deep_bunker 1 | tags star_screens
feature failed_base | name a failed first base in your own blocks | weight 2 | tags first_camp
feature survey_beacon | name a lone survey beacon | weight 2 | poi lone_beacon 2 | tags beacon_hut
feature precursor_plates | name smooth plates and pillars on the plain | weight 2 | is rough | poi precursor_vault 3 alien_library 2 ring_lock 1 vault_gate 2 megalith_ring 2 | tags glyph_floors | orbit precursor | like presence.precursor_guardians*12
feature vault_field | name sealed domes on an empty plain | weight 2 | is rough | poi precursor_vault 2 vault_gate 2 alien_hive_ruin 1 | tags packed_cases | orbit precursor | like presence.precursor_guardians*12
feature vharn_yards | name half-built hulls on alien cradles | weight 1 | is faction | poi alien_slipway 3 vharn_outpost 2 | tags hull_jigs | orbit yards | like presence.vharn_garrison*12
feature alien_prison | name an alien prison of gantries and wall lines | weight 1 | is faction | poi alien_cellblock 3 prison_block 1 | tags cells | like presence.vharn_garrison*12
feature hollow_basin | name a lowland full of shimmering growth | weight 2 | is hollow rough | poi bloom_scar 2 bloom_garden 2 | stamp hollow_region | tags grown_through | orbit hollow | like presence.hollow_shore*6 flora.hollow_overrun*4
feature hollow_patch | name one rare splotch of overgrown land | weight 2 | is hollow | poi bloom_garden 1 | tags grown_through
feature leaning_world | name every trunk leaning one way | weight 1 | is warped plants | poi hollow_shrine 1 | like flora.bent_coreward*12 | tags pilgrim_cots
feature singing_rock | name fluted stone that seems to repeat itself | weight 1 | is warped | poi sound_garden 2 | like sound.stone_flutes*12
feature folded_district | name a district that turns back on itself | weight 1 | is warped settled | poi wrong_house 2 time_pocket 1 | tags repeat_rooms
feature mirror_district | name districts with twins, each with one small edit | weight 1 | is warped settled mid | poi mirror_hall 3 wrong_house 1 | tags repeat_rooms
feature still_wave | name a breaking wave stopped and turned to stone | weight 1 | is warped shore
feature facing_suits | name empty suits in rows, all facing one way | weight 1 | is warped settled | tags patterned_objects
feature domed_observatories | name a dome on every high point | weight 2 | is settled mid | poi observatory 3 | tags star_charts
```

**Hazards.** One to three per world, weighted by what's already rolled. Every one is warned, as the design requires, and none gives ailments.

```layers
hazard vacuum | name vacuum | weight 1 | req air=none
hazard thin_air | name thin air | weight 1 | req air=thin
hazard bad_air | name bad air | weight 1 | req air=foul
hazard heat | name heat | weight 2 | is hot
hazard cold | name cold | weight 2 | is cold
hazard radiation | name radiation zones | weight 2 | bias mutagen 0.3 predators 0.1
hazard mutagen | name mutagen patches | weight 1 | is plants | bias mutagen 0.7 swollen 0.4 predators 0.3
hazard spores | name spore bursts | weight 1 | is plants | bias fungal 0.4
hazard toxic_runoff | name toxic runoff | weight 1 | is settled mid air_weather | bias dead 0.2
hazard strange_chemistry | name odd chemistry in the air and water | weight 1 | is air_weather
hazard corrosion | name corrosion | weight 1 | is air_weather
hazard thirst | name thirst | weight 2 | is dry air_weather | bias dry 0.4
hazard glare | name glare | weight 2 | is dry
hazard sharp_ground | name sharp ground | weight 1
hazard unstable_ground | name unstable ground | weight 2
hazard sinkholes | name sinkholes | weight 2 | is air_weather
hazard crevasses | name crevasses | weight 2 | is cold
hazard boggy_footing | name boggy footing | weight 2 | is shore
hazard deep_water | name deep water | weight 2 | is sea
hazard tides | name fast tides | weight 2 | is shore
hazard surges | name storm surges | weight 1 | is shore air_weather
hazard entanglement | name entangling growth | weight 1 | is sea
hazard scalding | name scalding vents | weight 1 | req water=some,wet,sea temp!=scorch
hazard lava | name lava | weight 2 | req temp=scorch,hot air0!=open
hazard eruptions | name eruptions | weight 1 | is hot
hazard quakes | name quakes | weight 2
hazard burning_ground | name burning ground | weight 1 | is air_weather | bias burnt 0.3
hazard lightning | name lightning | weight 2 | is air_weather
hazard gusts | name gusts and tornadoes | weight 2 | is air_weather
hazard meteors | name meteor showers | weight 1
hazard falling_debris | name falling debris | weight 1 | is mid
hazard unstable_wrecks | name unstable wrecks | weight 2 | is settled
hazard collapsing_floors | name collapsing floors and rusted rigging | weight 3 | is settled
hazard falling_facades | name falling facades | weight 1 | is settled mid
hazard failing_seals | name failing seals | weight 2 | is settled mid
hazard armed_hardware | name armed machines still on post | weight 2 | is settled mid rough | bias strife 0.2
hazard mines | name mines and unexploded ordnance | weight 2 | is settled rough | bias strife 0.3
hazard crossfire | name crossfire | weight 1 | is settled rough living | bias strife 0.5
hazard arc_flash | name arc flash at live switchgear | weight 1 | is settled mid
hazard molten_pits | name pits still molten | weight 1 | is settled mid
hazard rot_pockets | name rot pockets and gas | weight 1 | is settled | bias fungal 0.2
hazard gang_ambush | name ambushes on the roads | weight 2 | is settled rough living outdoors | bias strife 0.3
hazard night_swarms | name night swarms | weight 1 | is settled rough
hazard swarm_nests | name nest rooms | weight 2 | is beasts | bias predators 0.2
hazard ambush_plants | name ambush plants | weight 1 | is plants rich_life rough | bias alien 0.1
hazard stampedes | name stampedes | weight 1 | is beasts rich_life outdoors | bias stock 0.1
hazard night_predators | name night predators | weight 2 | is beasts | bias predators 0.4
hazard getting_lost | name getting lost | weight 1
hazard grey_front | name a grey front that will not bear weight | weight 1 | is high
hazard gravity_pockets | name gravity pockets | weight 1
hazard misplaced_things | name things that reappear elsewhere | weight 1 | is high
hazard disorientation | name disorientation | weight 1 | is warped
hazard time_slips | name lost minutes | weight 1 | is warped
hazard sound_lures | name wordless sounds that lead you on | weight 1 | is warped
```

**Flora.** One bias per world, on top of the one flora module, which still rolls the actual species.

```layers
flora bare | name no plants | weight 8 | blank yes | req life=none | bias sparse 1 barren 0.4
flora bare_rock_lichen | name lichen in the shade only | weight 6 | is plants | req life=low | bias sparse 0.7 barren 0.3
flora dead_forest | name dead grey forests | weight 0.3 | is plants settled | req temp!=frozen,scorch | bias dead 0.8
flora dead_fields | name vast fields of one dead crop | weight 0.3 | is plants settled crops | bias dead 0.6 stock 0.2
flora meadow_grass | name grass and broadleaf | weight 6 | is plants rich_life | req temp=mild,hot,cold | bias stock 0.1
flora deep_forest | name deep canopy | weight 5 | is plants rich_life | req water=some,wet,sea temp=mild,hot | bias overgrown 0.4
flora mangrove_reed | name mangrove, reed and moss | weight 4 | is plants shore
flora scrub | name scrub and few trees | weight 4 | is plants | req water=dry,some | bias sparse 0.3 dry 0.2
flora cactus_oasis | name succulents and oasis palms | weight 3 | is plants dry | req temp=hot,mild | bias dry 0.4
flora salt_scrub | name salt scrub | weight 2 | is plants dry | bias sparse 0.4 dry 0.4
flora ice_moss | name moss, dwarf shrub and lichen | weight 4 | is plants cold | bias sparse 0.3
flora fungal_giants | name giant mushrooms | weight 2 | is plants flowing | bias fungal 0.8
flora glow_moss | name glowing moss on everything | weight 2 | is plants flowing | bias alien 0.1 fungal 0.2
flora crystal_growth | name crystal growing like plants | weight 1 | bias alien 0.3
flora kelp_forest | name kelp forests | weight 3 | is plants sea
flora swollen | name swollen, fused and wrongly coloured growth | weight 1 | is plants | bias swollen 0.8 mutagen 0.2
flora carnivorous | name fanged vines and pitcher traps | weight 2 | is plants rich_life rough | bias alien 0.15 predators 0.2
flora thorn_lanes | name walls of thorn | weight 1 | is plants | bias overgrown 0.3
flora reclaiming_forest | name forest through every ruin | weight 2 | is plants rich_life settled | bias overgrown 0.8
flora crop_gone_wild | name old crops gone wild | weight 3 | is plants crops settled | bias overgrown 0.3 stock 0.3
flora orchard_rows | name orchards in rows nobody planted | weight 1 | is plants crops | bias stock 0.2
flora pollen_heavy | name flowers shedding yellow cloud | weight 1 | is plants rich_life | bias overgrown 0.2
flora stone_trees | name stone trees and little else | weight 1 | is plants | req type=petrified | bias dead 0.6
flora tethered_drifters | name gas bladders on long stalks | weight 1 | is plants shore | bias alien 0.25
flora tell_plants | name plants that grow only over ore | weight 2 | is plants | bias alien 0.1
flora giant_flora | name giant plants | weight 2 | is plants rich_life | bias overgrown 0.6
flora bent_coreward | name everything bending one way | weight 1 | is plants warped | bias alien 0.15
flora hollow_overrun | name the Hollow's growth over everything | weight 0.5 | is plants hollow rough | sets hollow=world | bias fungal 1 overgrown 0.7 alien 0.2 desolate 0.2
```

**Fauna.** One bias per world. The bestiary still builds the bodies; this says what kind of living is winning here. Behaviour ideas from the variety list are values, not creatures, so any world can use them.

```layers
fauna none | name no animals | weight 6 | blank yes | req life=none | bias barren 0.9
fauna sparse | name almost nothing alive | weight 5 | req life=low,none | bias barren 0.6
fauna quiet_grazers | name wary grazers | weight 6 | is beasts rich_life | bias barren -0.1
fauna herds_hunters | name herds and their hunters | weight 5 | is beasts rich_life | bias barren -0.2
fauna giants | name giants | weight 1 | is beasts rich_life
fauna pack_hunters | name pack hunters that flank | weight 3 | is beasts rich_life | bias predators 0.2
fauna swarms | name swarms | weight 3 | is beasts
fauna hive_castes | name hive castes | weight 1 | is beasts
fauna burrowers | name burrowers under everything | weight 4 | is beasts dry
fauna sand_swimmers | name things that swim in sand | weight 2 | is beasts dry
fauna reef_stalkers | name reef stalkers | weight 1 | is beasts shore
fauna sea_life | name sea life in full variety | weight 4 | is beasts sea
fauna waders | name waders and ambushers | weight 3 | is beasts shore
fauna ice_beasts | name shaggy ice beasts | weight 4 | is beasts cold
fauna heat_lovers | name heat-adapted life | weight 3 | is beasts hot
fauna cliff_fliers | name cliff-nesting fliers | weight 3 | is beasts
fauna carrion_fliers | name carrion fliers in the ruins | weight 3 | is beasts settled | bias desolate 0.1
fauna ferals | name old stock and pets gone feral | weight 3 | is beasts settled | bias ferals 1
fauna mixed_exotics | name animals from many worlds breeding together | weight 1 | is beasts settled rich_life | bias alien 0.1 ferals 0.3
fauna mutants | name mutants | weight 1 | is beasts | bias mutagen 0.5 predators 0.6
fauna blind_crawlers | name blind cave crawlers | weight 3 | is beasts
fauna night_hunters | name night-adapted hunters | weight 3 | is beasts | bias predators 0.3
fauna lamp_shy | name hunters that won't cross light | weight 2 | is beasts
fauna metal_eaters | name metal-eating crawlers | weight 1 | is beasts
fauna wreck_hermits | name things that wear wrecks as shells | weight 2 | is beasts settled
fauna bower_builders | name creatures sorting ruin litter into displays | weight 2 | is beasts settled
fauna dusk_climbers | name herds that climb to high ground at dusk | weight 2 | is beasts rich_life
fauna slowstones | name boulders that move a few metres a day | weight 1 | is beasts dry
fauna burden_giants | name giants with old huts lashed to their backs | weight 1 | is beasts rich_life settled | bias working 0.4
fauna web_liners | name something using web lines as roads | weight 2 | is beasts settled
fauna echo_birds | name birds repeating sounds of generations ago | weight 2 | is beasts settled
fauna old_rounds | name harnessed beasts still walking a dead delivery route | weight 1 | is beasts settled low | bias working 0.8
fauna lode_grazers | name grazers that lick at ore seams | weight 2 | is beasts
fauna static_nesters | name static-charged nesters | weight 1 | is beasts air_weather
fauna light_floaters | name slow floating fliers | weight 2 | is beasts
fauna lost_migrants | name migrating flocks that no longer know the way | weight 1 | is beasts
fauna pollinators | name great moths and pollinator swarms | weight 2 | is beasts rich_life
fauna vacuum_rarities | name rare things adapted to vacuum | weight 1 | req air=none
```

**Sky and weather.** Two small layers. `sky` is what's up there, and because celestial bodies are real, values that claim a body say so to the system generator rather than painting it. `weather` is the mood of the air.

```layers
sky plain_sky | name an ordinary sky | weight 9 | blank yes | is air_weather
sky one_big_moon | name one big moon | weight 5 | tags moon_lakes_set
sky many_moons | name several moons | weight 4
sky ring_arc | name a ring arc across the sky | weight 1
sky giant_overhead | name a giant filling the sky | weight 1
sky stuck_sun | name a sun fixed on the horizon | weight 1
sky double_sun | name double sunsets | weight 3 | req type!=rogue
sky swollen_sun | name a swollen dying sun | weight 2 | req type!=rogue
sky pinprick_sun | name a tiny savage point of sun | weight 2 | req type!=rogue
sky aurora | name aurora | weight 3 | is cold air_weather
sky black_sky | name stars at noon | weight 14 | blank yes | req air=none
sky hard_stars | name extreme clear stars | weight 2 | is dry
sky dust_lane | name a dim sun behind a dust lane | weight 2
sky day_eclipse | name a timetabled noon eclipse | weight 2 | req type!=rogue
sky falling_night | name harmless bright meteor nights | weight 2 | is air_weather
sky dead_train | name dead satellites crossing at dusk in single file | weight 2 | is settled high
sky second_noon | name a point of light from an old orbital mirror | weight 2 | is settled high | orbit mirrors
sky broken_stations | name a ring of broken stations | weight 2 | is settled high | orbit wrecks stations
sky drifting_hulks | name hulks drifting across the night | weight 2 | is settled mid | orbit wrecks debris
sky light_on_moon | name a light blinking on the moon's dark side | weight 2 | is settled | orbit beacon
sky visitor_moon | name a body slightly bigger each night | weight 1 | is warped
sky wrong_stars | name more lights than the star map says | weight 1 | is warped
sky stutter_sun | name a sun that stutters at noon | weight 1 | is warped
sky seam | name a seam in the air | weight 1 | is warped rough
sky glimpse_beyond | name something impossible, seen once and gone | weight 1 | is nearcore
```

```layers
weather no_weather | name no weather | weight 10 | blank yes | req air=none | bias storm -1
weather calm | name calm settled weather | weight 6 | is air_weather | bias storm -0.3
weather gold_evenings | name long gold evenings | weight 4 | is air_weather
weather morning_mist | name mist at dawn | weight 4 | is air_weather flowing
weather fog_banks | name fog that hides the horizon | weight 3 | is air_weather shore
weather sea_wind | name salt wind and fast weather | weight 3 | is air_weather shore | bias storm 0.3
weather trade_winds | name steady trade winds | weight 3 | is air_weather | bias storm 0.2
weather dry_gales | name dry gales | weight 3 | is air_weather dry | bias storm 0.4 dry 0.4
weather dust_storms | name dust storms | weight 3 | is air_weather dry | bias storm 0.5 dry 0.5
weather heat_shimmer | name heat shimmer and mirage | weight 3 | is air_weather hot | bias dry 0.4
weather monsoon | name monsoons | weight 3 | is air_weather | req water=wet,sea temp=mild,hot | bias storm 0.5
weather thunderheads | name big angry storm towers | weight 3 | is air_weather | req water!=none | bias storm 0.6
weather rain_seasons | name long grey rains | weight 3 | is air_weather | req water=some,wet,sea temp=mild,cold,hot | bias storm 0.3
weather snowfall | name snow on every road | weight 3 | is air_weather snowy
weather blizzards | name blizzards | weight 3 | is air_weather snowy | bias storm 0.7
weather pollen_clouds | name yellow pollen clouds | weight 1 | is air_weather rich_life
weather ash_fall | name ash fall | weight 1 | is air_weather | bias ash 0.4 dead 0.2
weather acid_rain | name rain that bites | weight 1 | is air_weather | bias storm 0.3 dead 0.2
weather spore_haze | name spore haze | weight 1 | is air_weather plants | bias fungal 0.3
weather perpetual_storm | name a perpetual storm | weight 1 | is air_weather | bias storm 1
weather fire_storms | name fire storms | weight 1 | is air_weather hot | bias storm 0.7 burnt 0.3
weather glass_sleet | name sideways glass sleet | weight 1 | is air_weather hot | bias storm 0.3
weather iron_drizzle | name a thin iron drizzle | weight 1 | is air_weather
weather soot_snow | name soot snow | weight 1 | is air_weather snowy settled | bias ash 0.3 dirt 0.2
weather oil_rain | name oily black rain | weight 1 | is air_weather hot | bias dirt 0.3
weather seed_fall | name a season of slow drifting seeds | weight 1 | is air_weather rich_life
weather dead_calm | name a dead calm that never lifts | weight 1 | is air_weather | bias storm -0.5
```

**Palette.** A world's colour mood. Old entries each carried one, so they're gathered as values and any world can take any that suit it.

```layers
palette gold_dust | name gold and faded paint | weight 3 | is air_weather | paint c9a24a 8a6a3a a86a3a b89a6a ffd28a
palette pastel_moss | name pastels under moss | weight 3 | is plants rich_life settled | paint c8a8b0 9ec4b0 7a8a6a d8c8a0 ffe6c8
palette grey_sea | name grey sea and rust orange | weight 3 | is shore | paint 6c7478 c4622a 5a5e60 8a8070 ffd0a0
palette white_turquoise | name white and turquoise | weight 2 | is shore | paint e0e0d8 3aa8a0 d8d8d0 5ab0b8 e8f8ff
palette soot_signal | name soot grey and signal red | weight 3 | is settled | paint 4a4a4c c42a20 3a3a3c 8a2a24 ffb080
palette slag_ochre | name ochre and slag | weight 3 | paint b8862e 5a4e44 7a5a34 a0782c ffc070
palette rust_chrome | name chrome and rust | weight 2 | is settled | paint a85a32 b8bcc0 7a4a2c 5a5a5e fff0e0
palette stone_brass | name grey stone and brass | weight 2 | is settled | paint 8a8a84 b08a3a 5c5c5a 6a6a5c ffe0a0
palette beige_slate | name beige and slate | weight 2 | is settled | paint c8b89a 4c5866 4a5260 a89878 fff0d8
palette green_gold | name green-gold haze | weight 3 | is plants rich_life | paint 6a8a3a c8a83a 4a6a34 9aa860 ffe8a0
palette violet_cold | name cold violet light | weight 2 | paint 6a5a8a a8a0d0 4a4466 7a6a9a d0d8ff
palette ash_brown | name ash grey and brown | weight 3 | paint 7a6e64 5a4a3c 4a423c 6a5e52 ffd8b0
palette bone_white | name bone white and glare | weight 3 | paint e4dccc a89070 cac0ae d8cdb8 fff8ec
palette steel_blue | name steel blue | weight 2 | paint 5a7088 c8ccd0 3e4c5c 4a5c74 d8e8ff
palette red_black | name red and black | weight 2 | paint 8a2420 1e1c1c 3a1e1c 6a1a18 ffa080
palette amber_leaf | name amber and leaf-red | weight 2 | is plants rich_life | paint c07a2a 8a3a24 7a4a2a a86a30 ffc890
palette lilac_yellow | name lilac skies and yellow cloud | weight 1 | is air_weather | paint b8a0c8 e0c860 8a7a9a c8b0d0 fff0c0
palette pink_white | name steam and pink-white | weight 1 | is settled | paint e8c8c8 f0f0ec c8a0a0 e0b8b8 ffe8ec
palette neon_dead | name dead neon | weight 1 | is settled high | paint 2a2a34 e040a0 30303a 3ac0c8 c0f0ff
palette prismatic | name prismatic edges | weight 1 | paint c0c8d0 7ad0c0 8a90a8 b0a0d0 f0f8ff
palette indigo_pink | name indigo and pink | weight 2 | is cold | paint 3a3a6a d8809a 2e2e4a 6a5a8a e0d0ff
palette lime_cream | name lime and cream | weight 1 | is plants rich_life | paint b8d060 e8e0c0 8a9a4a d8d0a8 fff8d0
palette moss_black | name wet green and black | weight 2 | is plants rich_life flowing | paint 3a5a34 1e221e 2a3a28 4a5a3a e8ffd0
palette ink_starlight | name black and hard white | weight 4 | req air=none | paint 1c1c20 e8e8f0 2a2a30 4a4a54 f0f4ff
```

**Music and sound.** All music is generated drones by world type, so this picks the drone's temperament and one signature sound, heard in the world or sat under it. No recorded tracks, and no reactive scores.

```layers
sound soft_drone | name a soft spacious drone | weight 7
sound haunting | name an eerie thin drone | weight 5 | bias desolate 0.5
sound pastoral | name a warm slow drone | weight 4 | is plants | bias living 0.6
sound grinding | name a low industrial drone | weight 4 | is settled | bias dirt 0.1
sound wind_low | name wind at every opening | weight 4 | is air_weather
sound surf | name surf and shore birds | weight 3 | is shore air_weather
sound insect_chorus | name an insect chorus | weight 3 | is beasts rich_life | bias living 0.3
sound creaking_ice | name creaking ice | weight 3 | is cold air_weather
sound crystal_hum | name a hum from the crystal | weight 1 | req type=crystal,irradiated_ice,barren_metal
sound rail_hum | name distant rail hum | weight 1 | is settled mid
sound generators | name far-off generators | weight 2 | is settled mid
sound radio_murmur | name radio static that almost sounds like a voice | weight 1 | is settled mid | bias desolate 0.2
sound bells | name bells at set hours | weight 1 | is settled air_weather | bias living 0.2
sound wind_chimes | name wind chimes and tuned turbines | weight 1 | is settled air_weather
sound stone_flutes | name wind in fluted stone | weight 1 | is air_weather
sound hour_horn | name a horn across the region once a day | weight 1 | is settled
sound distant_guns | name far guns or something like them | weight 1 | is settled rough | bias strife 0.7
sound wordless_chant | name wordless chanting | weight 1 | is living | bias living 0.1
sound festival_echo | name a band that plays once at dusk | weight 1 | is settled mid | bias living 0.4
sound the_knock | name three slow thuds from far below | weight 1 | is warped | bias desolate 0.3
sound still_minute | name a minute when all sound stops | weight 1 | is warped | bias desolate 0.3
sound silence | name near silence | weight 4 | bias desolate 0.7
```

**Signs and marks.** Signs carry meaning without language, and a world's culture and tech vary their frames and colours. These are the world's habit of marking, placed from real room data so they tell the truth. Nothing here is text to read, apart from foreign script nobody can read.

```layers
marks none_left | name no marks at all | weight 6 | blank yes | req settled=no
marks plain_pictograms | name plain pictogram plates | weight 8 | is settled
marks traveller_chalk | name traveller chalk that means real things | weight 5 | is settled
marks tally_marks | name tally marks on doors | weight 2 | is settled
marks plague_marks | name painted house marks | weight 1 | is settled
marks colour_zones | name colour-coded zone signs | weight 2 | is settled mid
marks faction_sigils | name faction sigils | weight 2 | is settled
marks gang_sign | name gang trophies and spikes | weight 2 | is settled rough
marks route_paint | name hand-painted route arrows | weight 3 | is settled
marks strike_banners | name banners and painted fists | weight 2 | is settled
marks prayer_flags | name prayer flags | weight 2 | is settled outdoors
marks faded_bunting | name faded bunting | weight 1 | is settled outdoors
marks brand_ghosts | name the ghosts of dead brands | weight 3 | is settled mid
marks stamped_plates | name stamped numbered plates | weight 3 | is settled
marks warning_stripes | name warning stripes and chevrons | weight 3 | is settled mid
marks hand_signs | name hand-sign boards | weight 1 | is settled
marks mural_walls | name murals on every wall | weight 2 | is settled
marks survey_stakes | name survey stakes to the horizon | weight 1 | is settled
marks waterline_marks | name flood lines and height marks | weight 2 | is settled flowing
marks memorial_names | name walls of names as shapes, too worn to read | weight 2 | is settled
marks foreign_script | name orders in a foreign script | weight 1 | is settled
marks two_flags | name two sets of flags on the same ruins | weight 1 | is settled
marks blanked_signs | name signs gone blank | weight 1 | is settled mid
marks glyph_marks | name glyphs nobody can read | weight 2
marks repossession_tags | name numbered tags on everything | weight 1 | is settled
```

### 4.6 Room dressing sets

Every old `Inside` line and every apocalypse's `Leaves` line is gathered here. A dressing set is one thing a room can show, keyed by `tags` on any layer value, which is the same word the overhaul already uses for what decorations and loot key on. Where an old entry had a log, ledger, letter or roll, the idea survives as an object you can read without reading: a stack, a tally, a stopped board. No text, no voices.

```layers
dress luggage | shows halls of unclaimed suitcases, half-packed lives, a family's picnic still laid out on a bonnet
dress queue_slips | shows ticket booths, numbered slips, a lottery drum with the last draw still in it, queue rails
dress jam | shows vehicles nose to tail, doors open, belongings along the verge
dress packed_bags | shows packed bags left behind, keys in a box, a locked shop
dress door_notices | shows tidy shut-up houses, a plain stamped plate on every door, stripped shelves
dress set_tables | shows dinner on tables, an unfinished meal, a bed still warm, one house locked from inside
dress beds_made | shows beds slept in, lamps by them, shoes set out for the morning
dress dust_sheets | shows dust sheets, plastic on chairs, price stickers, a banner for an opening day
dress lamps_on | shows a light left on over an empty room, moths, a warm switch
dress afterhours | shows a barrier lowering for no train, sprinklers on dead lawns, a board flipping, a shift bell
dress still_selling | shows powered billboards for dead companies, a screen that turns to face the passer-by
dress costumes | shows costumes on racks, a half-set band, floats in procession
dress stalls | shows food stalls with fossilised cakes, a rigged prize wheel, instruments on a stand
dress scoreboards | shows a scoreboard frozen mid-final, trophy halls, locker rooms, bunk tents on the pitch
dress lockers_notes | shows lockers, passed notes as folded shapes, a kettle still plugged in, an unfinished experiment
dress reading_rooms | shows reading lamps, card drawers, margins crowded with marks, stacks behind a second door
dress cold_racks | shows racks in cold aisles, frost flowers, a terminal offering to restore something, rows gone dark
dress quiet_terminals | shows cars stopped at crossings, auto-doors locked, the same error shape on every screen
dress stamp_rooms | shows stamp rooms, in-trays that never empty, a counter machine asking for a form, permits in vaults
dress council_rooms | shows an empty chamber with a vote half taken, rival stacks of paper, two stamps
dress banquet | shows banquet tables set for two delegations, an empty vault of destroyed arms, two flags
dress company_store | shows a company store, pay windows, a manager's office with a safe
dress pay_tickets | shows pay tickets on spikes, barricaded shafts with scratch marks, a union flyer as a printed fist
dress shift_boards | shows shift boards, name tags on lockers, a foreman's safe
dress last_pour | shows moulds with the last pour cooled in them, a furnace that still holds heat
dress assembly_stop | shows a line stopped mid-unit, half-built things on hooks
dress stock_mountains | shows warehouses of finished stock to the roof, a line still adding to it
dress full_silos | shows silos full and sealed, stalled harvesters, hives where machines dock in rows
dress field_stores | shows empty silos, seed sacks, sprayers rusted in place
dress seed_rooms | shows seed cabinets, jars relabelled in a different hand, a looted vault
dress press_cellars | shows presses, jars, cellars, ladders against bare rows
dress bare_orchards | shows bare orchards, empty hives, hand-pollination ladders
dress felt_camps | shows felts, rails, cheese racks, a fire ring
dress grow_racks | shows grow racks, dead lamps, a last green shelf
dress dead_gardens | shows dead gardens under panels, bunk rows, breathing masks on hooks
dress vat_shapes | shows vats with half-grown shapes, packing lines, a menu card
dress tins | shows tins to the ceiling, unlabelled stock, one tin still warm from the line
dress labels | shows conveyor belts with the last tins half-labelled, a label contest pinned up as pictures
dress frozen_gauges | shows valves, frozen gauges, a tank yard with one tank still full
dress switch_halls | shows switch halls, a cascade stopped on a frozen board, a warm panel
dress slip_cranes | shows slipways, cranes over half-built frames, cargo sheds at the foot of a ramp
dress cutting_rigs | shows cutting rigs, half-gutted liners, a board of salvage claims in numbered tags
dress crystal_cargo | shows cargo rooms furred with crystal, harbour offices on stilts
dress stove_rooms | shows stove rooms, wood stacks, one tally of winters on a wall
dress tool_lockers | shows tool lockers still locked, unfinished buildings
dress hand_parts | shows patched machines, hand-forged replacements, mismatched pipes, a spare-parts wall
dress casts | shows casts, a cradle holding a skull, dig tents, spoil heaps
dress bench_work | shows benches left mid-task, sample cases, a bench grown through with crystal
dress star_charts | shows charts with entries struck out, a telescope pointing at the ground, a lunch left on a console
dress wards | shows bedside charts, supply counts, one bed with fresh sheets, triage tents
dress sleep_wards | shows beds with straps, charts of sleep depth, carers' cots beside them
dress cot_halls | shows rows of cots in halls, discharge slips for the dead, burial pit markers
dress sealed_houses | shows sealed houses, stuffed door gaps, painted marks
dress field_hospitals | shows field hospitals, shattered bridges, boots in rows
dress mess_halls | shows mess halls set for hundreds, weapon racks locked, a roll board with every peg moved
dress cells | shows cells, chains, a warden's desk, tiers of gantries
dress dugouts | shows dugouts, boots in rows, a gas shelter
dress cockpits | shows stalled walkers with cracked cockpits, ammo dumps, a tent with a map and two flags
dress last_gun | shows gutted tanks in rows, tool lockers, the last gun in a display case
dress gun_halls | shows control rooms with unspent checklists as lit lamps, ammunition halls
dress launch_consoles | shows launch consoles with blinking keys, crews' bunks, a silo with a live rocket
dress climber_cabins | shows climber cabins with seats still belted, cable drums, a queued command on a panel
dress hull_cabins | shows hand-welded cabins, names scratched on bulkheads, a neat arc of fallen rockets
dress vacuum_crews | shows tilted decks, vacuum-sealed crews, cargo of ammo
dress flooded_holds | shows flooded holds, crew bunks on the high decks
dress ballrooms | shows ballrooms, cabins with luggage, a casino deck with the tables set
dress crew_decks | shows a crew deck frozen mid-handover, founders' cabins, a corridor nobody walks
dress founders_plaques | shows founders' plaques, a seed store, tidy grid streets
dress pool_bars | shows pool bars with glasses set out, staff quarters hastily emptied, locked suites
dress vault_rooms | shows vaults, chips under glass, a wheel still turning slowly, a host waiting in a penthouse
dress gift_shops | shows gift shops of keepsakes, a ride that turns once if you pull a lever, a coach with a microphone on the seat
dress studios | shows canvases of the last days, an unfinished portrait of someone who looks like you, price tags still on
dress hostels | shows bunks, a soup pot, a donation box, prayer boards
dress altars | shows shrines in offices, banners, offerings of fruit
dress pilgrim_cots | shows pilgrim cots, a pit lined with roots that all point down
dress keeper_rounds | shows feeding schedules as pictograms, a night house with sealed doors, a nursery kept warm
dress ticket_halls | shows ticket gates, departure boards, a cab with a half-eaten meal
dress carriages | shows carriages full of luggage and bunks, a dining car with soup in bowls, a lever left mid-throw
dress stage_stops | shows saloon rooms, a telegraph key, a sheriff's cell, a livery with tack on the pegs
dress tunnels_metro | shows metro platforms, service tunnels, apartments with trees in the lounge
dress towpath_inns | shows lock gates, barge wrecks, towpath inns
dress harbour_steps | shows harbour steps, ferries tied up, bell ropes, refectories
dress boardwalk_homes | shows homes on stilts, ladders down to water, flooded lower floors
dress hangar_bar | shows planes under tarps, a bar with a propeller on the wall
dress toll_boxes | shows toll boxes, chalked safe routes, a gatehouse
dress signal_towers | shows arm-towers, a mirror on a far ridge, hamlets beneath them
dress market_stalls | shows stalls bartering in car parks, shuttered shops, a covered arcade
dress dead_banks | shows banks with drifts of worthless notes, forced vault doors
dress headsets | shows operator desks with warm headsets, a dial that only goes one way, a request box full of paper, unlabelled tapes
dress cut_cables | shows cut cables, stuffed speakers, a door with its intercom smashed
dress stone_halls | shows great halls, dungeons, a chapel with a lord's tomb
dress trophy_halls | shows feasting halls, trophy rooms, stripped lawns, a hostage cell
dress trophy_walls | shows welded barricades, spikes, loot piles, a boss room
dress engine_shrines | shows shrines to engines, gladiator pits, jerry cans, a tally board of races
dress caravan_gear | shows barrels, pack frames, a well-house with its rope still wet
dress tally_rods | shows tally rods, barrels, a deep cistern kept as a shrine
dress stranded_boats | shows dry reservoirs, stranded boats, empty canals, broken cliffs
dress net_lofts | shows net lofts, smoke racks, boats hung from ceilings
dress waterlines | shows waterlines on walls, upper floors lived in, boats tied to balconies
dress buried_cars | shows snow-buried cars, boarded windows, tunnels between doors
dress cellar_towns | shows melted tarmac, cooked orchards, cellars lived in by night
dress ash_roofs | shows ash drifts, fallen-in roofs, grey rivers
dress shadows | shows silhouettes burnt onto walls, a hospital where nobody turned up
dress sealed_doors | shows cracked shelters, sealed doors, stacks of tinned food
dress tally_doors | shows doors with counted days, chalk hunger tallies
dress masks | shows masks in rows and stacked bins, filters on hooks
dress stained_streets | shows stained walls, yellowed streets, dead trees in patches
dress tilted_towers | shows tilted towers, split roads, offset rails
dress ration_shelves | shows ration cards, last-tin shelves, boarded shops, a larder still locked
dress soup_kitchens | shows soup kitchens, long tables, ladles
dress spare_airframes | shows hangars of spare airframes, scorched pylons
dress gilded_forts | shows gilded forts with scarred gates, stolen goods rooms
dress ransacked_offices | shows ransacked offices, shot-up gates, marked houses
dress barricades | shows barricades across streets, burnt district lines, broken tables
dress front_barricades | shows fresh sandbags, lights still on, cars abandoned mid-road, wet paint
dress patched | shows patched roads, new doors in old frames, a working pump
dress memorials | shows a plaque, a roped-off ring, a kept room behind glass
dress welded_doors | shows human quarters welded shut from outside, stacked tool racks
dress spotless_zones | shows spotless floors beside rot, a cleaner still on its round
dress dormant_racks | shows dormant sentries in racks, maintenance bays
dress automata | shows waiters serving nothing, a theatre rehearsing for no one, a register signed by machines
dress routine_scenes | shows a diner set at the same time each cycle, clocks stopped, crops half-tended
dress gouged_doors | shows gouged doors, locked-in rooms, bloodied shelters
dress churchyards | shows stacked coffins, barricaded vestries, nailed doors, dug-out graves
dress mat_streets | shows mat-covered streets, fruiting bodies in halls, fused furniture
dress grown_through | shows grown-through corridors, sample fridges under pulsing mats, a desk fused into a tree
dress taped_corridors | shows taped corridors, specimen jars, scorch marks, a cure in a fridge nobody announced
dress webbed_lifts | shows webbed lifts, egg cases in vents, hollowed furniture
dress empty_nurseries | shows empty nurseries, painted-over playgrounds, cribs under tarps
dress height_marks | shows a child's height marks on a door frame, small shoes, a new door frame
dress holed_domes | shows pitted roofs, holed domes, splintered forests
dress sunward_scorch | shows scorched sunward faces, melted antennas, shadow-side towns
dress slag_rivers | shows slag fields, black rivers, dead orchards
dress lace_steel | shows steel frames eaten to lace, clean-cut stumps, a road that ends at the grey
dress alarm_rooms | shows control rooms with alarms still lit, cooling towers leaning, roads packed one way
dress candle_stubs | shows dead lifts with doors forced, candle stubs on every desk, hand-marked charts as ticks
dress climate_towers | shows dead climate towers, half-made soil, seas the wrong colour
dress shelter_halls | shows shelter halls dug into hills, ruined rods, sandblasted towers
dress lost_luggage | shows arches in squares, scorch rings, luggage in rows, odd cargo from elsewhere
dress melted_pads | shows melted ships on pads, shock lines in concrete, a town flattened on one side
dress name_doubles | shows clone vats, double graves, two sets of handprints
dress newspapers_stand | shows last-day papers still on stands, queue barriers, colour-coded gates
dress road_camps | shows long road camps, carts at fords, doors left open
dress picket_chairs | shows picket lines of chairs, unlit furnaces, banners
dress meeting_halls | shows mass meeting halls, burnt towers, wrecked company stores
dress one_open_street | shows one open street, shuttered upper floors, faded signs, a bus stop with no bus
dress propped_stairs | shows propped-up stairs, failed pumps, cracked dams
dress lamp_arrays | shows huge lamp arrays, crops under lights, glowing moss on walls
dress marker_posts | shows floating debris pockets inside marker posts, stretched bridges
dress repeat_rooms | shows duplicate rooms with one small edit, the same meal on two tables, a lift button for the same floor
dress bent_rooms | shows impossible objects, light that bends, a quiet ring around them
dress one_wrong_object | shows perfect houses, one object slightly wrong in each, meals with no crumbs
dress pod_rows | shows pods in rows, some empty, some fogged, a door that opens on a room you remember
dress clapperless_bells | shows dead speakers, bells with clappers removed, soft-soled boots
dress patterned_objects | shows objects set out in patterns, every door opened from inside, shadows that arrive late
dress stripped_rooms | shows rooms stripped to the frame, numbered tags on what's left, a crate half nailed
dress slumped_towns | shows slumped ground, fire crews' trucks, roads sinking into smoke
dress dead_compasses | shows compasses in a bin, flocks' droppings on a wrong-way tower
dress scaffolds | shows scaffolding, stacked materials, rebar waiting, floors with no walls
dress cordon_tents | shows a cordon of tents, instruments aimed at an empty pit
dress study_camp | shows a camp at the far end, gear a generation apart at each end
dress warning_spikes | shows leaning concrete spikes, jagged earthworks, faces carved in pain, picture-signs
dress mannequins | shows mannequins at every table, all one face, half the town scorched
dress star_screens | shows ports that are screens of passing stars, an airlock opening on grass
dress first_camp | shows a failed first base, a half-finished vessel, a skeleton in your suit
dress timber_ships | shows full-size wooden starships, repainted yearly, paint pots at the keel
dress road_crews | shows fresh paint, clean verges, a road crew's cart
dress trade_hatch | shows a sealed door, warning lights, a hatch and a trade slot
dress pipe_taps | shows pipes worn bright where people tap them
dress hidden_cellars | shows a false wall, a stash, a rope ladder
dress hermit_camps | shows a tripod over ash, a bedroll, a stash they'll come back for
dress duty_rosters | shows peg boards of duties, a lamp room swept clean, a spare cot
dress lamp_rooms | shows spiral stairs, a lens still turning, one bed, an unfinished meal
dress sand_halls | shows sand-filled halls, shells of old ships, a half-buried door
dress crystal_grown | shows lab benches and glowing halls grown through with crystal
dress steam_halls | shows bathhouses, folded towels, locked lockers, turbine floors
dress trees_indoors | shows malls with a canopy, trees in the lounge, roots through roads
dress grave_goods | shows grave goods, stone doors, bones in patterns, a chamber that is warm
dress glyph_floors | shows glyphs, floors that tilt, chambers with no door, control spires
dress packed_cases | shows packed cases left behind, a sealed dome, a neat door
dress hull_jigs | shows hulls on jigs, alien gantries, a Vharn crew's mess
dress alien_orders | shows orders in a foreign script, alien stairs too tall for you
dress airlock_rooms | shows airlocks, pressure doors, rooms that hold air until something fails
dress shop_streets | shows shop streets, shared kitchens, a deepest hall nobody enters
dress union_halls | shows union halls, a banner in the foyer
dress village_stores | shows a village store, a shared well, a list of chores as pegs
dress beacon_hut | shows a single survey beacon, a stool, one mug
dress moon_lakes_set | shows fishing gear, boat jetties, a guest book of signatures
dress protection_boxes | shows a toll box in every shop, a marked door, a burnt one
```

## 5. Themes: what's left

A theme is now a small mood: a named bias over the layers, with the one idea that's left once the parts are out. The field that matters is `identity`. If a theme's identity can't be said in one sentence that isn't just its parts, it doesn't deserve a row. `like` is the whole of the theme: each word multiplies the odds of a value, and at the moment a theme is chosen it also counts in the theme's favour for whatever has already been rolled, so a theme fits its world by how well it suits it, and no allow-lists are needed.

Most old themes turned out to be combinations, so most are gone, and that's the point. The four ids the overhaul already uses (`long_queue`, `hush_suburbs`, `moon_lakes`, `foundry_belt`) stay, so its data and tests keep working. Twelve at the end are new. Themes marked `wild` can also sit on a world nobody ever settled, because those worlds deserve moods too; three of the new ones exist for them.

```layers
theme plain | name no strong mood | weight 16 | blank yes | is wild | identity Nothing pulls this world one way, and what it is comes from its parts alone
theme long_queue | name The Long Queue | like fall.exodus*6 fall.decreed_exodus*3 fall.tide_of_refugees*2 infra.port_funnel*8 feature.jam_highways*6 palette.gold_dust*3 weather.gold_evenings*3 creed.the_numbered*4 | region camp 2 scrappy_pad 2 | occ EMP 8 SCV 6 | break 0.12 | tags queue_slips | identity Everyone here wanted the same one door out | bias desolate 0.3
theme hush_suburbs | name Hush | like fall.vanishing*8 fall.walked_out_apoc*4 moment.ordinary_evening*4 running.long_life_lamps*5 build.pastel_estate*5 sound.silence*4 palette.pastel_moss*4 | occ EMP 22 WRP 3 | break 0.1 | tags set_tables | identity Nobody home and nothing broken, and the quiet is the unease | bias desolate 0.5 ferals 0.3
theme parade_ground | name The Parade Ground | like moment.festival*10 moment.match_day*8 trade.sport*3 trade.roadside*2 marks.faded_bunting*5 sound.festival_echo*6 creed.festival_keepers*6 | occ EMP 6 SQT 6 SCV 6 | identity The happiest day a world ever had, stopped | bias strife 0.2
theme closed_season | name Closed for the Season | like trade.resort*8 trade.gaming*8 moment.holiday*6 fall.decreed_exodus*3 fall.walked_out_apoc*3 build.resort_curve*6 palette.white_turquoise*4 palette.neon_dead*4 running.cleaners*3 | occ EMP 8 MCH 8 | identity The rich left first and best, and it still looks ready for them | bias desolate 0.3
theme deserted_capital | name The Standing Order | like trade.administration*10 ran.ministry*5 running.clerks*8 build.concrete_slab*4 fall.ai_silence*3 fall.decreed_exodus*4 marks.stamped_plates*5 palette.beige_slate*4 palette.stone_brass*3 creed.stampers*6 | occ MCH 10 | identity An order is still being carried out, and nobody remembers giving it | bias desolate 0.3
theme clockwork_hours | name Clockwork Hours | like running.automata*10 running.farm_machines*8 running.timers*6 running.transit_loops*5 running.factory_lines*4 creed.machine_kin*6 sound.generators*3 palette.stone_brass*3 | is mid | occ MCH 18 | identity Everything runs, slightly off, for no one | bias desolate 0.2
theme sentry_world | name Sentry World | like running.sentries*10 feature.sentry_lines*8 trade.military*3 fall.drone_wars*5 fall.machine_uprising*4 hazard.armed_hardware*8 palette.steel_blue*4 | is rough mid | occ MCH 20 | identity Everything is guarded and nobody is home | bias strife 0.1
theme mercy | name The Mercy Hospitals | like trade.medicine*10 running.carers*6 presence.keepers*5 build.white_clinical*5 fall.plague*3 fall.sleep_sickness*3 fall.tide_of_refugees*4 weather.morning_mist*3 | occ SQT 10 KPR 8 | tags wards | identity People stayed to look after other people, long past any sense in it | bias living 0.2
theme wall_cities | name Wall Cities | like build.stone_keep*6 infra.toll_roads*8 presence.warlords*6 presence.militias*4 fall.enclave_wars*6 fall.slide*5 fall.mercenary_seizure*3 ran.great_houses*4 | is living | occ SQT 8 GNG 10 | poi wall_city 2 hilltop_keep 2 toll_gate 1 | identity Every settlement is a fortress against its neighbours | bias strife 0.3
theme road_warrens | name Road Warrens | like presence.gangs*8 infra.road_web*5 trade.fuel*5 fall.supply_cutoff*3 fall.slide*3 palette.rust_chrome*6 marks.gang_sign*6 hazard.gang_ambush*10 | is rough outdoors living | occ GNG 20 | poi fuel_fort 3 gang_arena 2 caravan_stop 1 | tags engine_shrines | identity Order failed and the survivors became convoys, and fuel is the only law | bias strife 0.3
theme water_tax | name The Water Tax | like fall.drought*8 fall.famine*6 fall.monoculture_failure*4 presence.water_lords*6 hazard.thirst*5 creed.seed_keepers*5 feature.green_valley*10 flora.dead_fields*4 | is living | occ GNG 8 SQT 8 | identity One scarce thing rules, and whoever holds it holds the world | bias dry 0.4 strife 0.2
theme foundry_belt | name Foundry Belt | like trade.heavy_industry*8 trade.breaking*6 running.banked_furnaces*6 fall.general_stoppage*4 fall.poison_air*4 fall.industrial_collapse*6 palette.soot_signal*5 sound.grinding*5 feature.pylon_forest*3 | is mid | occ SCV 8 GNG 6 | tags shift_boards | identity Work without end, stopped mid-shift, and the furnaces never quite cold | bias dirt 0.5
theme moon_lakes | name Moon Lakes | like phase.spared*6 fall.slow_fade*4 fall.emigration*4 trade.subsistence*5 trade.orchards*5 sky.one_big_moon*5 feature.mirror_lakes*6 sound.pastoral*4 weather.calm*3 | is outdoors | occ EMP 14 SQT 8 | tags moon_lakes_set | identity Beautiful, lightly lived in and in no hurry
theme overgrown_metropolis | name Overgrown Metropolis | like type.city_covered*20 feature.overgrown_city*10 flora.reclaiming_forest*6 fauna.cliff_fliers*3 infra.skyways*4 | is plants settled mid | occ NST 8 MUT 6 | identity A city to the horizon, in leaf | bias overgrown 0.7 ferals 0.5
theme pilgrim_roads | name Pilgrim Roads | like trade.pilgrimage*10 infra.pilgrim_ways*10 fall.faith_takeover*4 ran.faith*5 marks.prayer_flags*5 sound.bells*4 feature.bell_towers*5 presence.pilgrims*5 | is outdoors | occ CLT 8 SQT 6 | identity A world organised around walking to something | bias living 0.2 working 0.3
theme library_towns | name Library Towns | like trade.learning*8 trade.archives*8 ran.scholars*8 fall.memory_rot*6 infra.quiet_roads*2 | occ EMP 8 KPR 6 | tags reading_rooms | identity What was known is being kept, whether or not anyone can still use it
theme lighthouses | name The Last Keepers | like running.beacons*10 running.broadcast*10 presence.keepers*6 feature.mast_forest*3 sound.radio_murmur*4 | occ KPR 10 | tags lamp_rooms | identity One keeper, one light, still going, for nobody in particular | bias desolate 0.3
theme wrong_stars | name Wrong Stars | like sky.wrong_stars*12 sky.stutter_sun*8 sky.visitor_moon*6 trade.star_survey*5 feature.domed_observatories*6 creed.sky_counters*8 fall.time_stutter*6 feature.mast_forest*3 sound.radio_murmur*3 hazard.sound_lures*6 | is warped | occ EMP 8 WRP 6 | identity The numbers have stopped adding up, and the careful people noticed first | bias alien 0.1
theme folded_town | name The Folded Town | like fall.folding*10 feature.folded_district*8 feature.mirror_district*8 weather.calm*3 sound.silence*4 | is warped settled | occ EMP 20 WRP 8 | identity A place that doesn't quite close | bias desolate 0.2
theme hollow_orchards | name The Lean | like feature.leaning_world*10 feature.singing_rock*10 flora.bent_coreward*8 creed.coreward_leaners*8 presence.pilgrims*3 | is warped | occ CLT 10 WRP 8 | tags pilgrim_cots | identity Land that has started pointing at something, and people who have started listening | bias alien 0.2
theme lamplight | name Lamplight | like creed.lamp_keepers*8 running.long_life_lamps*4 weather.morning_mist*3 sound.soft_drone*2 | occ EMP 12 SQT 8 | tags lamp_rooms | identity One lit window in each town, and nobody says who keeps it | bias living 0.2
theme souvenir | name Souvenir World | like trade.roadside*10 trade.art*8 feature.giant_statues*8 marks.mural_walls*6 palette.gold_dust*3 | occ EMP 6 SQT 6 SCV 6 | identity A world made to be looked at, still smiling
theme trench_moon | name Trench Moon | like feature.trench_lines*10 fall.civil_war*5 fall.invasion*3 feature.machine_drifts*6 hazard.mines*6 palette.ash_brown*4 sound.distant_guns*6 weather.rain_seasons*3 | is rough | occ EMP 6 GNG 6 HSK 3 | identity A stalemate cut so deep into the land that nobody can leave it | bias strife 0.6 dead 0.3 burnt 0.3
theme eye_bloom | name The Last Days | like phase.falling*20 fall.hollow_front*5 marks.warning_stripes*2 sound.distant_guns*3 | is rough | req phase=falling | occ FAC 8 SQT 6 | identity A world with days left, and everyone in it knows | bias strife 0.6 severity 0.2
theme diesel_frontier | name Hoof and Diesel | like phase.spared*8 infra.diesel_rail*8 trade.mining*2 trade.herding*3 build.gabled_timber*5 presence.bandit_riders*6 presence.freeholders*5 | is outdoors living | req pre=L,M | occ SQT 10 GNG 4 | identity A frontier that never reached the modern day and didn't need a collapse | bias working 0.6 stock 0.5 living 0.4
theme pit_worlds | name Pit Worlds | like build.buried_warren*10 infra.deep_tunnels*8 presence.pale_folk*8 feature.sinkhole_chain*5 feature.fungal_deep*6 type.shell*6 | occ NST 6 SQT 6 | identity The surface is the lid, and everything that matters is below | bias dirt 0.4
theme teeth_trees | name Teeth in the Trees | like flora.carnivorous*10 flora.thorn_lanes*6 fauna.hive_castes*6 fauna.sand_swimmers*6 fauna.reef_stalkers*6 feature.hive_mounds*6 feature.thorn_maze*6 feature.coral_land*6 hazard.ambush_plants*8 | is beasts rough wild | occ NST 14 MUT 10 | identity The land itself hunts | bias overgrown 0.4 predators 0.3
theme titans_meadows | name Titans' Meadows | like fauna.giants*10 feature.herd_giants*10 feature.giant_bones*8 trade.digs*8 fauna.burden_giants*6 | is beasts wild | occ EMP 8 SQT 4 | identity Everything here is bigger than you, and mostly doesn't care | bias stock 0.2 overgrown 0.3
theme glass_garden | name The Glass Garden | is wild | like feature.crystal_fields*10 sky.aurora*5 sky.hard_stars*5 trade.star_survey*5 trade.research*2 sound.crystal_hum*5 palette.prismatic*6 | occ EMP 18 | identity Quiet wonder, with hardly anyone left to see it | bias alien 0.2
theme barrow_downs | name The Old Ground | is wild | like feature.standing_stones*10 feature.precursor_plates*10 feature.vault_field*10 marks.glyph_marks*6 presence.precursor_guardians*4 | occ EMP 8 GRD 6 | identity Far older than anyone here, and not finished | bias alien 0.1
theme empty_ark | name Empty Ark | like feature.colony_hull*12 ran.founders*8 fall.walked_out_apoc*6 build.ship_hull_town*4 | occ EMP 16 KPR 3 | identity The founders' order is intact, and the people walked out of it by plan | bias desolate 0.4
theme waiting_world | name The Waiting World | like feature.waiting_town*12 moment.opening_day*8 running.cleaners*4 running.timers*4 fall.supply_cutoff*3 | is mid | occ EMP 18 MCH 8 | tags dust_sheets | identity Everything was built and made ready, and the settlers never came | bias fade -0.4 dirt -0.2 desolate 0.3
theme test_range | name The Test Range | like feature.dummy_town*10 feature.warning_country*8 feature.missile_grid*4 ran.generals*5 trade.military*3 hazard.mines*3 | is rough mid | occ EMP 16 MCH 6 | identity This place was built to be destroyed, and parts of it still are | bias burnt 0.4 dead 0.2
theme memorial_world | name The Kept Grave | like phase.recovered*6 phase.mending*3 marks.memorial_names*8 creed.ancestor_tenders*8 feature.graveyard_hills*5 | is living | occ KPR 8 SQT 6 | tags memorials | identity The whole world has been made into its own grave marker, and it's tended daily | bias living 0.3 fade -0.2
theme liquidation | name The Sale | like fall.liquidation*12 ran.absentees*5 marks.repossession_tags*8 trade.mining*2 | occ SCV 6 FAC 2 | tags stripped_rooms | identity Someone is selling the place out from under itself, room by room | bias dirt 0.2
theme false_spring | name False Spring | like phase.mending*6 phase.recovered*3 presence.reclaimers*6 weather.gold_evenings*2 | is living | occ SQT 8 | tags patched | identity Fresh paint on old wounds, and everyone very cheerful about it | bias living 0.3 fade -0.3 dirt -0.3
theme imitation | name The Timber Fleet | like feature.timber_ships*12 creed.cargo_builders*10 creed.stampers*4 | is living | occ SQT 8 CLT 6 | identity People copy what they can no longer understand and wait for it to work | bias living 0.2
theme duty | name The Good Kilometre | like creed.road_menders*8 feature.good_kilometre*10 presence.keepers*5 running.hour_horn*4 creed.horn_pausers*5 | is living | occ KPR 10 | identity Work handed down so long that nobody remembers what it was for | bias living 0.2
theme nature_reserve | name The Reserve | like trade.parks*8 fauna.mixed_exotics*6 fauna.herds_hunters*3 flora.meadow_grass*2 running.nothing*2 | is beasts settled | occ NST 8 KPR 6 | identity The old world set this aside on purpose, and the fences outlived the reason | bias stock 0.1 barren -0.3
theme second_wind | name The Second Wind | like phase.falling*2 fall.slide*2 presence.reclaimers*2 phase.recovered*2 presence.refugees*3 | is living | occ SQT 6 GNG 4 | identity It survived the collapse once and is finding out what that cost | bias living 0.5
theme wild_country | name Wild Country | weight 7 | like fauna.herds_hunters*4 fauna.pack_hunters*4 flora.deep_forest*3 flora.giant_flora*4 hazard.night_predators*4 | is wild beasts | req settled=no | occ NST 10 | identity Nobody has ever stood here, and everything alive is busy | bias barren -0.2
theme grand_scenery | name The Grand Scenery | weight 7 | like feature.buttes*5 feature.rock_spires*5 feature.columnar_towers*5 feature.natural_arches*5 feature.great_falls*5 feature.rainbow_strata*5 feature.lone_rock*5 sound.wind_low*3 | is wild | req settled=no | identity The land has no audience and puts on the show anyway
theme stillness | name The Stillness | weight 7 | like sound.silence*6 weather.dead_calm*6 fauna.sparse*4 sky.hard_stars*3 sky.black_sky*2 | is wild | req settled=no | occ EMP 16 | identity Nothing here moves, and a footprint would last a million years | bias desolate 0.5 barren 0.3
```

## 6. Where the old entries ended up

### 6.1 The 112 themes

Each line is one old theme and every hard-coded choice pulled out of it, by layer. `to` says what's left: `dissolved` means no theme remains and the parts alone make it, which is the good result; anything else names the theme that now holds its identity. `keeps` records a specific idea that isn't a value of its own, so it stays findable. The script forces each line's parts onto a roll, and a line that can't hold together under the rules fails the check, so every one of these worlds can still really come up, and the debug start screen can force any of them by name.

Words the rules forbid changed in passing: logs, letters and speech became objects and wordless sound, and a "voice" is a sound that isn't quite one.

```layers
old long_queue | n 1 | to long_queue | parts type.meadow fall.exodus infra.port_funnel feature.jam_highways palette.gold_dust weather.gold_evenings pre.M post.L port.C presence.hermits | keeps hermits living in the queue itself; a silence over the last kilometre
old lifeboat | n 2 | to long_queue | parts type.meadow land.coastal_cliffs fall.exodus feature.launch_fields feature.wreck_beach presence.reaver_salvage palette.grey_sea pre.M post.L port.D | keeps a field where all the rockets fell short in a neat arc
old last_train | n 3 | to dissolved | parts type.meadow land.river_valleys trade.transit infra.rail_web feature.stopped_trains fall.great_migration palette.soot_signal pre.M post.L presence.hermits | keeps a train stopped by something at the end of the line; lone drivers who never left
old empty_ark | n 4 | to empty_ark | parts type.meadow feature.colony_hull ran.founders fall.walked_out_apoc pre.H post.M | keeps a quiet corridor no one will walk
old hush_suburbs | n 5 | to hush_suburbs | parts type.meadow build.pastel_estate fall.vanishing moment.ordinary_evening running.long_life_lamps palette.pastel_moss sound.silence pre.M post.L | keeps a single survivor family, unsure why
old parade_ground | n 6 | to parade_ground | parts type.meadow land.lake_district moment.festival fall.plague marks.faded_bunting creed.festival_keepers pre.M post.L
old closed_season | n 7 | to closed_season | parts type.archipelago trade.resort moment.holiday fall.decreed_exodus build.resort_curve palette.white_turquoise running.sentries pre.H post.L | keeps caretakers; guests' secrets in locked suites
old deserted_capital | n 8 | to deserted_capital | parts type.meadow land.plains trade.administration ran.ministry running.clerks fall.decreed_exodus build.concrete_slab palette.stone_brass presence.meridian_remnant pre.H post.M | keeps rival stacks saying leave and stay
old lockin | n 9 | to dissolved | parts type.ash_fall trade.mining ran.company fall.lockdown feature.inward_fences infra.conveyors palette.slag_ochre hazard.unstable_ground pre.M post.L | keeps towns sealed to save the ore; Exchange buyers who ask no questions
old walked_out | n 10 | to dissolved | parts type.steppe land.steppe trade.military ran.generals fall.walked_out_apoc running.sentries build.army_prefab presence.gangs pre.M post.L | keeps vehicles parked in lines, fuelled and waiting
old quarantine | n 11 | to dissolved | parts type.steppe fall.plague scar.lockdown feature.quarantine_walls marks.colour_zones pre.M post.L | keeps walls built from outside; a lab that cured it and didn't say
old fever_jungle | n 12 | to dissolved | parts type.rainforest fall.biohazard flora.swollen fauna.mutants weather.spore_haze palette.green_gold presence.hermits pre.M post.L | keeps the forest as a single organism
old sleepwalkers | n 13 | to dissolved | parts type.meadow fall.sleep_sickness presence.routine_walkers weather.morning_mist pre.M post.L | keeps carers who watch; crowds that panic when disturbed
old pollen_wars | n 14 | to dissolved | parts type.meadow trade.orchards fall.pollinator_loss flora.pollen_heavy weather.pollen_clouds palette.lilac_yellow running.farm_machines fauna.pollinators pre.M post.L | keeps a fridge of next year's seed
old blightland | n 15 | to water_tax | parts type.steppe trade.farming fall.famine flora.dead_fields palette.ash_brown pre.M post.L | keeps black rivers; rare clean springs
old bloom_drowned | n 16 | to dissolved | parts type.meadow zone.inner fall.hollow_front feature.hollow_basin presence.hollow_shore palette.violet_cold pre.M post.L | keeps paths of safe stone; roads that dive in and come out the far side
old coughing_hills | n 17 | to foundry_belt | parts type.ash_fall trade.heavy_industry fall.poison_air palette.slag_ochre hazard.bad_air pre.M post.L | keeps a mill fire as the local cause; masks stacked at checkpoints
old mercy | n 18 | to mercy | parts type.meadow trade.medicine running.carers presence.keepers weather.morning_mist pre.M post.L | keeps one bed with fresh sheets
old flesh_gardens | n 19 | to dissolved | parts type.meadow land.plains trade.vat_stock ran.company fall.biohazard running.vats palette.pink_white pre.H post.L
old clinic_edge | n 20 | to mercy | parts type.meadow land.coastal_cliffs trade.medicine fall.tide_of_refugees presence.freeholders pre.M post.L | keeps a shelf of unsent letters as tied bundles
old glass_plains | n 21 | to dissolved | parts type.dune fall.nuclear_war feature.glass_plains presence.gangs fauna.sand_swimmers weather.heat_shimmer pre.M post.L | keeps highways on stilts above the glass; voidglass
old rust_armies | n 22 | to trench_moon | parts type.steppe fall.drone_wars feature.machine_drifts feature.trench_lines presence.kessari palette.red_black pre.H post.L | keeps two fleets shut down mid-charge
old wall_cities | n 23 | to wall_cities | parts type.meadow land.hills build.stone_keep infra.toll_roads presence.warlords fall.slide pre.M post.L | keeps fine riding beasts
old missile_fields | n 24 | to dissolved | parts type.steppe fall.nuclear_war feature.missile_grid running.sentries pre.M post.L | keeps survivors using hatches as storm cellars
old trench_moon | n 25 | to trench_moon | parts type.meadow mod.giant_moon feature.trench_lines fall.civil_war palette.ash_brown weather.rain_seasons pre.M post.L
old peace_treaty | n 26 | to dissolved | parts type.highland moment.signing_day feature.border_line fall.walked_out_apoc presence.meridian_remnant pre.M post.L | keeps weapons destroyed on schedule; a handover that never came
old siege_orbitals | n 27 | to dissolved | parts type.canyon fall.orbital_bombardment sky.broken_stations feature.debris_line presence.reaver_salvage pre.H post.L
old decommissioned | n 28 | to dissolved | parts type.steppe feature.dismantled_arsenal trade.breaking fall.supply_cutoff pre.M post.L
old sentry_world | n 29 | to sentry_world | parts type.canyon running.sentries feature.sentry_lines presence.kessari palette.steel_blue pre.H post.H | keeps sentries on post, never in waves
old big_gun | n 30 | to dissolved | parts type.steppe feature.big_gun trade.military fall.kinetic_strikes palette.red_black hazard.radiation pre.H post.L | keeps fired once and never recovered
old road_warrens | n 31 | to road_warrens | parts type.salt_flat presence.gangs infra.road_web trade.fuel pre.M post.L
old water_tax | n 32 | to water_tax | parts type.oasis fall.drought presence.water_lords pre.M post.L
old salt_and_bone | n 33 | to dissolved | parts type.salt_flat mod.drained_sea feature.stranded_ships trade.salt palette.bone_white presence.caravaners pre.M post.L | keeps the dried sea dial from the world-types section
old long_winter | n 34 | to dissolved | parts type.tundra fall.long_winter weather.snowfall pre.M post.L | keeps tech frozen intact
old ember_world | n 35 | to dissolved | parts type.ash_fall trade.mining fall.seam_fire feature.burning_seams weather.ash_fall pre.M post.L
old bandit_kingdoms | n 36 | to wall_cities | parts type.meadow land.hills presence.warlords ran.great_houses infra.toll_roads pre.L post.L | keeps rings of fortified farms; Freeholders pressed
old pit_worlds | n 37 | to pit_worlds | parts type.dune air.thin build.buried_warren infra.deep_tunnels presence.pale_folk pre.M post.M | keeps a deepest hall nobody enters
old floodlines | n 38 | to dissolved | parts type.meadow fall.rising_seas feature.drowned_city presence.fisher_folk pre.M post.L
old last_garden | n 39 | to water_tax | parts type.steppe feature.green_valley presence.freeholders fall.famine pre.M post.L | keeps besieging neighbours; politics
old hunger_roads | n 40 | to water_tax | parts type.steppe trade.farming fall.famine presence.gangs pre.M post.L | keeps the rumour of a larder still locked
old teeth_trees | n 41 | to teeth_trees | parts type.rainforest flora.carnivorous fauna.pack_hunters hazard.ambush_plants pre.M post.L
old stalker_reefs | n 42 | to teeth_trees | parts type.archipelago feature.coral_land fauna.reef_stalkers pre.M post.L | keeps pearl-like crystals
old hive_plains | n 43 | to teeth_trees | parts type.steppe feature.hive_mounds fauna.hive_castes pre.M post.L | keeps towns eaten; queen chambers
old maw_dunes | n 44 | to teeth_trees | parts type.dune fauna.sand_swimmers feature.sunk_town pre.M post.L | keeps vibration sensing
old thorn_wall | n 45 | to teeth_trees | parts type.meadow land.hills feature.thorn_maze flora.thorn_lanes pre.L post.L | keeps hunting lodges
old storm_spires | n 46 | to dissolved | parts type.storm feature.rock_spires fall.weather_failure fauna.static_nesters weather.perpetual_storm hazard.lightning pre.H post.L | keeps stormcore
old titans_meadows | n 47 | to titans_meadows | parts type.meadow feature.herd_giants fauna.giants feature.giant_bones pre.L post.L | keeps guides
old spore_caverns | n 48 | to dissolved | parts type.fungal land.karst feature.fungal_deep fauna.blind_crawlers hazard.spores pre.M post.L
old acid_reaches | n 49 | to dissolved | parts type.canyon fall.poison_air weather.acid_rain hazard.corrosion pre.M post.M
old kelp_sea | n 50 | to dissolved | parts type.ocean feature.kelp_mats flora.kelp_forest fauna.sea_life infra.ferries pre.M post.L
old wrong_stars | n 51 | to wrong_stars | parts type.meadow zone.core sky.wrong_stars trade.star_survey feature.domed_observatories creed.sky_counters hazard.time_slips pre.M post.L | keeps signposts that disagree; pilgrims who sleep outside to watch
old folded_town | n 52 | to folded_town | parts type.meadow zone.core fall.folding feature.folded_district pre.M post.L | keeps unique odd items that fit no recipe
old hollow_orchards | n 53 | to hollow_orchards | parts type.meadow zone.inner feature.leaning_world flora.bent_coreward creed.coreward_leaners pre.L post.L | keeps herd animals that walk one way at dusk
old whisper_flats | n 54 | to wrong_stars | parts type.salt_flat zone.inner feature.mast_forest sound.radio_murmur hazard.sound_lures pre.M post.L | keeps birds perched on masts in rows; sound in the shape of people who left, never words
old mirror_cities | n 55 | to folded_town | parts type.meadow zone.core fall.folding feature.mirror_district pre.H post.L
old choir_canyon | n 56 | to hollow_orchards | parts type.canyon zone.inner feature.singing_rock sound.stone_flutes pre.L post.L | keeps resonant stone
old clockwork_hours | n 57 | to clockwork_hours | parts type.meadow running.automata pre.H post.H | keeps a handful of repair folk
old husks | n 58 | to dissolved | parts type.meadow fall.husks_fall presence.husk_nights feature.graveyard_hills weather.morning_mist pre.M post.L
old eye_bloom | n 59 | to eye_bloom | parts type.meadow zone.inner phase.falling age.now fall.hollow_front moment.still_going pre.M post.M people.settled | keeps survey pay
old dream_salt | n 60 | to dissolved | parts type.glacier fall.long_sleep sky.aurora pre.H post.L
old black_library | n 61 | to library_towns | parts type.highland trade.archives ran.scholars pre.M post.L | keeps dangerous knowledge walled in; rename, see section 11
old lamplight | n 62 | to lamplight | parts type.meadow land.hills creed.lamp_keepers pre.L post.L
old glass_garden | n 63 | to glass_garden | parts type.crystal feature.crystal_fields pre.L post.L
old moon_lakes | n 64 | to moon_lakes | parts type.meadow land.lake_district sky.one_big_moon feature.mirror_lakes pre.L post.L | keeps guest books as signatures you can't read
old terraced | n 65 | to moon_lakes | parts type.highland land.terraces trade.subsistence feature.great_falls presence.freeholders pre.L post.L
old wildflower | n 66 | to moon_lakes | parts type.meadow infra.airstrips flora.meadow_grass pre.M post.L | keeps hobby pilots
old aurora_shelf | n 67 | to glass_garden | parts type.glacier sky.aurora trade.research pre.M post.L | keeps a kettle on a cold stove
old bells | n 68 | to pilgrim_roads | parts type.archipelago feature.bell_towers sound.bells ran.faith pre.L post.L
old orchard_moon | n 69 | to moon_lakes | parts type.meadow mod.low_gravity mod.giant_moon trade.orchards fauna.light_floaters pre.M post.L
old amber_steppe | n 70 | to dissolved | parts type.steppe trade.herding build.felt_canvas palette.gold_dust pre.L post.L
old hot_springs | n 71 | to dissolved | parts type.hot_spring_ice feature.geyser_basin trade.resort pre.M post.L
old starfield | n 72 | to glass_garden | parts type.canyon land.plateau sky.hard_stars trade.star_survey feature.domed_observatories pre.M post.L | keeps a lunch left on a console
old foundry_belt | n 73 | to foundry_belt | parts type.ash_fall trade.heavy_industry running.banked_furnaces palette.soot_signal pre.M post.L
old drone_orchard | n 74 | to clockwork_hours | parts type.meadow running.farm_machines trade.farming presence.kessari pre.H post.H | keeps harvesters that treat walkers as weeds, inside their fields only
old power_down | n 75 | to dissolved | parts type.meadow fall.grid_cascade trade.power_export feature.pylon_forest pre.M post.L
old elevator_shadow | n 76 | to dissolved | parts type.meadow feature.fallen_elevator pre.H post.L port.A | keeps a patrol keeping others from the base
old cannery_world | n 77 | to dissolved | parts type.archipelago trade.fishing palette.grey_sea fall.supply_cutoff pre.M post.L
old data_tombs | n 78 | to dissolved | parts type.glacier trade.archives running.cleaners pre.H post.L | keeps heat vents steaming from halls
old shipbreakers | n 79 | to foundry_belt | parts type.meadow land.coastal_cliffs trade.breaking feature.wreck_beach palette.grey_sea pre.M post.L | keeps crews who still cut
old maglev_spine | n 80 | to dissolved | parts type.meadow land.plains infra.one_spine trade.transit pre.H post.L | keeps whole carriages as bases
old pipeline_world | n 81 | to dissolved | parts type.dune infra.pipelines trade.fuel weather.heat_shimmer presence.gangs pre.M post.L
old geothermal | n 82 | to dissolved | parts type.hot_spring_ice trade.power_export feature.geyser_basin pre.H post.L
old souvenir | n 83 | to souvenir | parts type.meadow trade.roadside feature.giant_statues pre.M post.L | keeps a small town living inside a giant teapot
old bureaucracy | n 84 | to deserted_capital | parts type.steppe trade.administration running.clerks creed.stampers pre.H post.H | keeps travel documents that open doors, as stamped tokens
old pirate_radio | n 85 | to lighthouses | parts type.meadow land.hills running.broadcast feature.mast_forest sound.radio_murmur presence.hermits pre.M post.L
old zoo_world | n 86 | to dissolved | parts type.meadow trade.parks fauna.mixed_exotics pre.M post.L | keeps a locked house never opened
old artist_colony | n 87 | to dissolved | parts type.meadow land.coastal_cliffs trade.art marks.mural_walls pre.M post.L
old sporting | n 88 | to parade_ground | parts type.meadow trade.sport moment.match_day fall.plague presence.gangs pre.M post.L | keeps gangs who still hold matches; stewards who check tickets
old casino_dunes | n 89 | to closed_season | parts type.dune trade.gaming build.resort_curve palette.neon_dead pre.H post.L
old school_holidays | n 90 | to dissolved | parts type.meadow trade.learning moment.term_time fall.walked_out_apoc pre.M post.L
old ghost_harbour | n 91 | to dissolved | parts type.meadow land.coastal_cliffs feature.beached_liners fall.exodus presence.reaver_salvage pre.M post.L port.C
old library_towns | n 92 | to library_towns | parts type.meadow land.hills trade.learning pre.M post.L | keeps margins that answer each other, as marks
old diesel_frontier | n 93 | to diesel_frontier | parts type.steppe infra.diesel_rail build.gabled_timber phase.spared fall.supply_cutoff presence.freeholders pre.L post.L port.E
old castle_worlds | n 94 | to wall_cities | parts type.meadow land.hills build.stone_keep trade.subsistence ran.great_houses fall.slide pre.L post.L port.0
old barrow_downs | n 95 | to barrow_downs | parts type.meadow land.hills feature.standing_stones pre.L post.L | keeps wights at dusk as HSK or HOL; rename, see section 11
old pilgrim_roads | n 96 | to pilgrim_roads | parts type.highland trade.pilgrimage infra.pilgrim_ways pre.L post.L
old precursor_shelf | n 97 | to barrow_downs | parts unsettled.yes type.meadow feature.precursor_plates presence.precursor_guardians
old lighthouses | n 98 | to lighthouses | parts type.meadow land.coastal_cliffs running.beacons pre.L post.L
old bone_dig | n 99 | to titans_meadows | parts type.canyon land.badlands trade.digs feature.giant_bones pre.L post.L
old chime_coast | n 100 | to dissolved | parts type.meadow land.coastal_cliffs feature.turbine_ranks sound.wind_chimes pre.M post.L
old sunken_raiders | n 101 | to dissolved | parts type.swamp feature.sunk_ships presence.reaver_capital pre.M post.L | the design doc's own example of a combination
old overgrown_metropolis | n 102 | to overgrown_metropolis | parts type.city_covered feature.overgrown_city flora.reclaiming_forest pre.H post.L
old waste_world | n 103 | to dissolved | parts type.dead_world fall.nuclear_war weather.ash_fall flora.dead_forest hazard.radiation pre.M post.M
old mutant_world | n 104 | to dissolved | parts type.meadow fall.biohazard flora.swollen fauna.mutants pre.M post.L
old never_settled | n 105 | to wild_country | parts unsettled.yes type.meadow feature.survey_beacon | keeps the rule that a never-settled world has no roads, ruins or wrecks
old ocean_world | n 106 | to dissolved | parts type.ocean fauna.sea_life infra.ferries presence.fisher_folk pre.M post.L | keeps fold crystal in deep water
old ring_remnant | n 107 | to barrow_downs | parts unsettled.yes type.arc feature.precursor_plates presence.precursor_guardians
old dome_colonies | n 108 | to dissolved | parts type.large_barren build.pressure_dome infra.tube_links pre.H post.M | keeps sealed stores
old fire_ice | n 109 | to dissolved | parts type.tundra mod.fire_and_ice feature.active_volcano trade.research pre.M post.L
old vacated_vaults | n 110 | to barrow_downs | parts unsettled.yes type.steppe feature.vault_field
old prison_worlds | n 111 | to dissolved | parts type.canyon trade.prison ran.alien_regime feature.alien_prison build.vharn_grown presence.vharn_garrison pre.H post.M | keeps captive tech
old alien_shipyards | n 112 | to dissolved | parts type.volcanic air.thin trade.shipbuilding ran.alien_regime feature.vharn_yards presence.vharn_garrison pre.H post.H | keeps Vharn crews at work
```

### 6.2 The old world types that moved

Ten of the 40 old types turned out to be true of many bodies, or to be caused by history. They're values in other layers now, so they combine with any base. A low-gravity swamp moon of a giant is now possible, and needs no row.

```layers
oldtype autumn_tilt | n 6 | to mod.tilted
oldtype drowned | n 16 | to fall.rising_seas feature.drowned_city
oldtype low_gravity_moon | n 23 | to mod.low_gravity
oldtype meteor_pelted | n 24 | to mod.meteor_pelted
oldtype high_gravity | n 27 | to mod.high_gravity
oldtype tidally_locked | n 29 | to mod.tidally_locked
oldtype ringed | n 30 | to mod.ringed
oldtype gas_giant_moon | n 31 | to mod.giant_moon
oldtype graveyard | n 38 | to feature.wreck_graveyard
oldtype terraced_farm | n 39 | to land.terraces
```

### 6.3 The rest

- **The 70 apocalypses** all stay, under their old ids, in section 4.3. They were already their own layer. Their prose in `docs/world-themes.md` stays the reference for what each leaves.
- **"Moving apocalypses out of the themes"** is superseded by section 6.1, which goes further and pulls out every layer, not only the fall. The old table's "identity theme that remains" column paired falls with themes as examples. Those pairs are simply combinations now, and any of them can roll.
- **Occupancy mix by theme.** The 112 rows stay as the record of intent. A world's mix is now the sum of its layers' `occ` shifts, so a raider presence on The Long Queue takes gangs up, exactly as the overhaul's test expects.
- **Every new POI family** from the old list has a home in at least one layer's tables, and the script fails if one doesn't.
- **The design doc's themed planets:** storm world, shattered world and fungal deep are types or features; glass desert is `feature.glass_plains`; cryo-fire and petrified forest gained types; bone fields is `feature.giant_bones`; ruin world is `feature.dead_city_ash`.

## 7. Compatibility rules

Nothing here lists which values go with which. The rolled world has a few plain facts, each value says what it's made of, and general rules decide. Add a value and it works with everything that's already there, which is the whole point.

**Facts.** `zone`, `type`, `air0` (the air people built under), `air` (the air now), `temp`, `water`, `life`, `settled`, `pre`, `post`, `drop` (how far tech fell, or `up`), `port`, `people`, `phase`, `age` and `speed` (of the fall). A fact not rolled yet doesn't block anything, which is why order matters, and why section 8.2 sets the chain.

**Three tools, all on the record.**

- `is` lists what a value is made of, such as `is open crops`. Each word has one general rule below. `open` means built under open sky, so it needs breathable original air; every open building style, road and street market gets that one rule by saying one word.
- `req` is a direct condition, for the few values no general word fits, such as `req temp=mild,hot`.
- `only` narrows a later layer by tag. A spared world takes `only fall.mild`, and a mid-collapse world takes `only fall.now`, so `falling` works with any fall that can still be happening without listing them.

**Hard and soft.** A hard rule is physics or tech: no weather without air, no reactor failures on a world of carts, no animals roaming a vacuum. It never breaks. A soft rule is habit or rarity: domes on a good world, the Husks' warping or Hollow growth near the rim. One world in 25 may break one soft rule. That's the "rare break", and it's recorded on the world so the generator can give it a reason and a survey can show it.

**How the air rule plays out.** `air0` and `air` differ only when a fall changes the air. So a world whose air went foul keeps its gabled streets, and now has people in masks walking them, or nobody at all. Living things and today's outdoor life read `air`, buildings read `air0`, and the design's "ruins fit their world's air" needs no special case. The same rule covers moons: what matters is the air, not the kind of body.

**How themes fit.** A theme is never forbidden by a fall, and there are no allow-lists. When a theme is chosen, its weight is multiplied by every `like` that matches what's already rolled, so a theme lands where it suits and rarely where it doesn't. That rarity is the old theme's "break". POIs keep their own `break` share on top, as the overhaul already has it.

```layers
rule open | req air0=open,thin | why built under open sky: streets, gables, markets and farms need the air people built under to be breathable
rule sealed | soft yes | req air0=thin,foul,none | why pressurised building is for air you can't trust; a dome on a good world is a rare break
rule outdoors | req air=open,thin | why people and animals working out of doors need air now
rule crops | req air0=open,thin water!=none temp=hot,mild,cold life!=none | why open farming needs air, water, soil life and a survivable season
rule plants | req air!=none life!=none | why nothing grows in the open in vacuum or on lifeless ground
rule beasts | req air!=none life!=none | why nothing that breathes roams an airless surface, and animals need a living world
rule rich_life | req life=mid,high | why big food chains need rich life
rule sea | req water=sea | why open water
rule shore | req water=wet,sea | why standing water or a coast
rule flowing | req water=some,wet,sea | why rivers and lakes
rule dry | req water=none,dry | why dry country
rule snowy | req temp=cold,frozen water!=none air!=none | why snow needs cold, water and air
rule cold | req temp=cold,frozen | why cold country
rule hot | req temp=scorch,hot | why hot country
rule air_weather | req air!=none | why weather and erosion need air
rule overland | req water!=sea | why long roads and rail need continents
rule low | req pre=L | why only a low-tech people
rule mid | req pre=M,H | why modern at least: highways, computers, nuclear power
rule high | req pre=H | why stellar tech only
rule port | req port=A,B,C,D,E | why needs a port to happen at
rule bigport | req port=A,B,C | why needs a big port
rule living | req people!=none | why needs somebody alive now
rule settled | req settled=yes | why needs someone to have lived here
rule faction | req zone!=rim start=no | why the rim has no factions, and start worlds never have a faction holding ground
rule rough | req sheltered=no | why too hostile for a start world on the rim; a game begun deeper in takes its chances
rule warped | soft yes | req zone=inner,core | why the Husks' wrongness rises toward the core; a trace of it further out is a rare break
rule hollow | soft yes | req zone=inner,core | why Hollow growth is rare near the rim
rule deep | soft yes | req zone=inner,core | why the greatest old works lie deep in the galaxy
rule nearcore | soft yes | req zone=core | why the incomprehensible is seen near the core
rule mended | req phase=mending,recovered | why reclaimers need something to reclaim from
rule struck | req speed=instant,quick phase!=falling | why a moment needs a fall fast enough to have one
rule gradual | req speed=season,slow phase!=falling | why slow falls have no moment
clash sealed outdoors | why a world of domes doesn't have open pilgrim roads
clash sealed open | why one world doesn't mix open streets and sealed tubes as its main network
clash spared rough | why a barely touched world isn't a hostile one
fam existing | list abandoned_factory abandoned_mine abandoned_spaceport alien_cathedral alien_hive_ruin alien_library biolab blast_crater coal_plant command_bunker crashed_freighter cryo_vault deep_bunker docks evacuation_camp exchange_depot exchange_market flavour_farmstead flavour_house flavour_shed flavour_shop flavour_wayside freeholder_farm frontier_base fusion_plant grain_elevator hospital ice_outpost kessari_spire kessari_works landing_clearing meridian_apartments meridian_garrison meridian_railgun meridian_relay meridian_research military_base nuclear_plant old_airfield old_farm old_mine ominous_tower precursor_vault prison_block rail_yard refinery research_base research_lab scrap_fort scrappy_port sewage_works solar_farm steelworks substation survivor_hamlet survivor_town telecom_exchange vharn_outpost vharn_sam_site water_works wind_farm works_complex
fam wanted | list clone_farm gate_ruin timber_fleet
```

`fam` lists exist only so the script can catch a mistyped family: `existing` is what's on `proto/overhaul`, `wanted` is what's new here or named by an apocalypse without being in the old count. Everything else comes from the old new-families table.

## 8. Data format and how the roll chains

### 8.1 The format

The tables are the overhaul's `data/world-layers.txt` format, flattened so a record fits on a line: a block's lines are joined with ` | `. To turn one into the other, replace ` | ` with a newline and two spaces.

```text
fall nuclear_war | name a nuclear exchange | group war | speed instant | is mid now | poi blast_crater 3 deep_bunker 3 | occ SQT 10 GNG 10 | like hazard.radiation*10 | tags shadows | stamp crater_ring | orbit debris
```

is the same record as:

```text
fall nuclear_war
  name a nuclear exchange
  group war
  speed instant
  is mid now
  poi blast_crater 3 deep_bunker 3
  occ SQT 10 GNG 10
  like hazard.radiation*10
  tags shadows
  stamp crater_ring
  orbit debris
```

So these tables can become `.txt` files by a mechanical pass, and they're compact enough to read in a doc. Blocks are read at startup, and unknown family names are skipped with a warning, as now.

**Keywords that keep their meaning:** `name weight chance region style hub needs industry plant depot farm poi break state occ streets tags`. These go straight into `WorldRoll.Combine`, which doesn't need to change: every table adds up, and `style`, `state` and `break` take the last value, so roll order matters only there.

**New keywords:**

| Keyword | Meaning |
| --- | --- |
| `is` | What a value is made of; each word has a rule |
| `req` | A direct condition on facts, as `fact=a,b` or `fact!=a,b` |
| `only` | `layer.tag`: later picks in that layer must have the tag |
| `like` | `layer.id*N`: multiplies that value's odds on later rolls. Under 1 shuns it |
| `sets` | `fact=value`: changes a fact, as a fall changing the air |
| `orbit` | Words the orbit layer reads, section 8.5 |
| `stamp` | The terrain stamp it writes, from the world-types section |
| `blank` | A default, ignored by the dominance check |
| `base`, `arch` | Link to the existing `Trade` enum and to archetype and variant |
| `kit`, `net` | The kit file; a network kind beyond road and rail |
| `air temp water life land settle zones start was` | On types: what the type allows |
| `group speed new` | On falls |
| `identity`, `why`, `shows` | Plain words for people reading, never shown to players |
| `bias` | `key N` pairs: how a value leans the rest of the world. They add up over a roll and the game reads the sums (`WorldReading`): flora `alien overgrown dead swollen fungal burnt sparse`, creatures `stock working ferals predators mutagen barren`, time and fall `age severity`, weather inputs `storm dry`, sound `desolate strife living ash`, paint `fade dirt`. Negative leans the other way |
| `paint` | On palettes: five colours, `main accent roof cloth light`, that made things take. Natural colours never do |

**Code changes the format needs**, for whoever builds it: `LayerParser` throws on an unknown keyword, so it needs the new ones, or a rule to keep unknown ones as data. `LayerKind` needs the new kinds. `WorldLayer.Pre`, `Post`, `Ports` and `Worlds` move into `req` and `is`. `Occupancy.Code` needs `HSK` (it reads `SHB`) and the codes it lacks: `FAC CYB BOT CLT` from the old table and the new `KPR SLP GRD MIM`.

### 8.2 The chain

Each step sees everything before it, and the seed forks by layer name, so adding a layer later doesn't shift the rest of a world.

1. **Body:** `zone`, `type`, `air`, `climate`, `life`, then `land` twice. The system generator supplies type and zone in the real game, from orbit position and region.
2. **Was it settled?** The type's `settle`, scaled by air. If not, skip to step 7 with a `wild` theme.
3. **Profile:** `pre`, `post`, `port`, `people`, `trade`, `ran`.
4. **Fall:** `phase`, `age`, `fall`, then `mod`, `moment`, and a scar one time in three.
5. **Theme:** chosen by fit, then a second theme for one region one time in four. This keeps the order the apocalypse section set: the apocalypse first, then the theme.
6. **The old world's look:** `build`, `infra`, `running`.
7. **The present:** `feature` (one to three), `presence` (up to two), `creed`, `hazard` (one to three).
8. **The surface:** `flora`, `fauna`, `sky`, `weather`, `palette`, `sound`, `marks`.
9. **Add up:** `WorldRoll.Combine` gives the region, hub, industry, plant, farm, POI and occupancy tables.
10. **Terrain stamps:** each `stamp` is applied after base terrain and before rivers and sea level, as the world-types section sets out.
11. **Region plan:** `RegionPlan.Make`, unchanged. It reads the tables and knows nothing of any theme, which is already the overhaul's rule.
12. **Per place:** occupancy, rolled from the world's mix with the place's own seed.

### 8.3 How this meets the overhaul

The overhaul's data keeps working while this is built. Its four themes keep their ids. Its world types (`green`, `dry`, `cold`, `hard`, `strange`) are stand-ins for `type`, one per archetype, and go when real types exist; `arch` says which is which. Its features and presences are values here (`crash_sites`, and `gangs`, `listening` for `raiders`, `hollow`), or can stay as they are.

- `WorldProfile.Trade` is `trade.base`. `Before`, `Now` and `Starport` are `pre`, `post` and `port`. `Current` is `people`.
- `WorldProfile.Exodus` stops being a flag and a hard-coded multiplier in `WorldRoll.Roll`. It's now true when `fall` is `exodus` or `decreed_exodus`, and `ScrappyPorts` and `EvacuationCamps` come from `region` tables.
- A theme's old `region`, `hub`, `needs`, `industry`, `plant`, `depot` and `farm` lines move onto `trade`, `pre` and `port`. A theme may still carry them, and they add.
- Family tech levels and `WorldProfile.Fits` still apply on top, so a family with no low-tech layout won't appear on a low-tech world even if a table asks for it.

### 8.4 How this meets the apocalypse layer

The Z records keep their shape, with these changes:

| Old field | Now |
| --- | --- |
| `needs pre M H` | `is mid`, and the general rules |
| `land`, `hazard`, `life` | `like`, onto the layers that own those things, plus `stamp` |
| `place` | `tags`, into the `dress` table |
| `occ_shift` | `occ`, which adds like every other layer |
| `recovered` | `phase.recovered`: each entry's recovered line is what shows, as spots, and its `place` list mostly doesn't |
| `allow_theme` | Gone. Theme fit replaces it |
| `scar_odds` | One world in three rolls a scar from the same table and the same rules |

Mid-collapse needs nothing new: `phase.falling`, `age.now`, any fall tagged `now`, and `moment.still_going`. Buildings are near intact, because wear comes from `age`. `state` puts defenders and attackers in, and `tags front_barricades` dresses it. Moving on while the player is away, later, is a change to a place's state and needs no change here.

### 8.5 How orbit POIs read a world

Orbit reads, it never rolls. An orbit takes the finished roll of its parent as read-only data, so the sky and the ground can't disagree. These names are stable. `docs/space-pois.md` was written alongside this, and uses longer names for the same things:

| Read this | In `space-pois.md` | Notes |
| --- | --- | --- |
| `type` | `world_type` | 39 values here. Ten old ones are now `mod`, `feature`, `land` or `fall` values, so read those too |
| `theme`, and `echo` below | `world_theme` | See below |
| `fall`, `scar` | `apocalypse` | Same ids, plus Z71 to Z82 |
| `phase` | `world_phase` | `fallen`, `mending` (its recovering), `recovered`, `falling` (its mid-collapse), plus `spared` and `untouched` |
| `pre`, `post` | `pre_tech`, `post_tech` | `drop` is the difference. Rises are allowed, and its L > M and M > H rows need them |
| `port` | `port_class` | A to E, 0 |
| `people` | `people_below` | `none`, `survivors`, `outposts`, `settled` for its empty, scattered, towns and cities |
| `build` | `culture` | |
| `air`, `mod` | `lift` inputs | Air, and `low_gravity`, `high_gravity` |
| `zone` | `warped` | How far the Husks' warping reaches a world comes from its zone |
| `feature`, `fall` | `hollow_region` | True with `hollow_basin`, `hollow_patch` or `fall.hollow_front` |
| `trade`, `ran`, `running`, `presence`, `age` | Its budget, owners and fate tables | Available if useful |

**The `orbit` words.** Any value can add words to a world's list, and the orbit layer treats them as hints: `highport`, `yards`, `hull_frames`, `stations`, `traffic`, `patrol`, `pirates`, `fleet_parked`, `wrecks`, `debris`, `sats`, `mirrors`, `relays`, `beacon`, `elevator_stump`, `precursor`, `hollow` (Hollow growth; it was `bloom`). A world with `feature.fallen_elevator` says `elevator_stump`, and the orbit knows a cable belongs there, which is the echo that document wants.

**The echo: reading a dissolved theme.** Its group tables key on the 112 old ids, and most of those no longer exist as themes. They don't need to. Section 6.1 keeps each old theme's parts, so a world can say which old themes it echoes: any whose parts it holds at least two of, or all if there are fewer, leaving out type, tech and landform. Best match first. `world_theme` in that document then means "the world's theme if it has one, and otherwise its best echo". The sample worlds in section 9 show these as `echo:` words.

## 9. Quality check by rolling

The script is `docs/world-layers-tools/roll_worlds.py`. It reads the fenced tables out of this file, rolls 200 worlds and 60 start worlds with a fixed seed, and writes the block below back in, so these numbers are always from the tables above. It follows the chain in section 8.2 and adds tables the way `WorldRoll.Combine` does.

It checks more than the rolls. It fails if any of the 112 themes, 70 apocalypses or old new-POI families is missing or has no home, if a value references something that doesn't exist, or if a dressing set is unused. Each line of section 6.1 is forced onto a roll, and it fails if that line's parts can't hold together. The nonsense check is written from the rolled facts, separately from the filter, and it's run with the filter on and off so the comparison means something.

<!-- roll results begin: written by world-layers-tools/roll_worlds.py -->

```text
Values by layer: zone 4, type 39, air 4, climate 19, land 32, mod 17, pre 3, post 3, port 6, people 4, trade 36, ran 14, phase 6, age 4, fall 82, moment 17, theme 44, build 25, infra 20, running 20, presence 35, creed 19, feature 94, hazard 54, flora 27, fauna 38, sky 25, weather 27, palette 24, sound 22, marks 25, occ 17
Total layer values: 806, plus 189 dressing sets, 32 rules and 3 clashes.
Old lists found and homed: 112 themes, 70 apocalypses, 132 new POI families; 193 families referenced in all.
Rolled 200 worlds in 201 tries with seed 20261107: 137 settled, 63 never settled, plus 60 start worlds.

Most common value in each layer, as a share of the worlds that rolled it:
  zone      rim                      34%   4 of 4 values seen
  type      tundra                    8%   36 of 39 values seen
  air       open                     42%   4 of 4 values seen
  climate   mild_some                14%   18 of 19 values seen
  land      plains                   22%   32 of 32 values seen
  mod       giant_moon                8%   15 of 17 values seen
  pre       H                        42%   3 of 3 values seen
  post      L                        66%   3 of 3 values seen
  port      E                        30%   6 of 6 values seen
  people    survivors                40%   4 of 4 values seen
  trade     mining                   13%   32 of 36 values seen
  ran       company                  12%   14 of 14 values seen
  phase     fallen                   55%   6 of 6 values seen
  age       long                     45%   4 of 4 values seen
  fall      civil_war                 6%   54 of 82 values seen
  moment    over_decades             52%   16 of 17 values seen
  theme     plain                    33%   38 of 44 values seen
  build     concrete_slab             9%   22 of 25 values seen
  infra     quiet_roads              14%   20 of 20 values seen
  running   nothing                  32%   18 of 20 values seen
  presence  vharn_garrison            7%   28 of 35 values seen
  creed     no_creed                 40%   18 of 19 values seen
  feature   sinkhole_chain           11%   77 of 94 values seen
  hazard    sinkholes                10%   51 of 54 values seen
  flora     bare                     22%   26 of 27 values seen
  fauna     sparse                   22%   32 of 38 values seen
  sky       plain_sky                18%   25 of 25 values seen
  weather   calm                     18%   25 of 27 values seen
  palette   slag_ochre               10%   24 of 24 values seen
  sound     soft_drone               20%   20 of 22 values seen
  marks     none_left                26%   24 of 25 values seen
  scar      nuclear_war               3%   31 of 82 values seen
Dominance flags: none

Of 23 single-value layers, two settled worlds share 3.9 on average; the closest pair shares 12.
Settled worlds with the same type, fall and trade as another: 0 of 137.
Places left empty: average 44%, range 8 to 86.
Biggest occupant by world: EMP 122, SQT 14, MCH 1
Themes seen: 34 of 44; plain on 41 worlds.

Worst nonsense: with the filters off, 100 of 200 worlds fail at least one check; with them on, 0 of 260.
  filters off:  35 a recovered world at low tech
  filters off:  30 plants in the open on an airless world
  filters off:  27 weather without air
  filters off:  26 breathing animals on the surface of an airless world
  filters off:  21 a creed with no one to hold it
  filters off:  16 a continental road or rail web on an ocean world
  filters off:  15 open roads across vacuum
  filters off:  13 a modern or stellar fall on a low-tech world
Rare breaks kept on purpose: 4 of 200 worlds.
Settled worlds that echo at least one old theme, for the orbit tables: 101 of 137.
Start worlds: 14 types seen; most common [('dune', 8), ('steppe', 8), ('glacier', 6)].
Settled worlds with an empty region plan table: 0.

Ledger: 47 old themes dissolve into parts; 65 live on in 32 themes; every recipe rolled legal: yes.
Distinct legal combinations: core layers 1.5e+15; all layers 4.98e+40.
```

Twenty sample worlds, the first twenty of the roll, unedited:

**1.** a toxic world, foul air, mild with rivers and lakes, in the outer arms. Land: striped badlands and open plains. Once mid tech, now mid tech, with a few survivors, with a frontier field. It lived on keeping the galaxy's records, owned from off-world, built in sealed modules and airlocks, joined by long ore conveyors and haul roads. It fell by the ships ceasing to come, on the day before everything opened, about 110 years ago, and is still in ruins. Mood: no strong mood. Still running: life support, barely. Belief: nothing in common. Now: nobody new. Features: an alien prison of gantries and wall lines. Danger: gusts and tornadoes, getting lost, falling facades. Life: glowing moss on everything, slow floating fliers. Sky: a sun fixed on the horizon, a dead calm that never lifts, steel blue. Sound: wind at every opening. Marks: traveller chalk that means real things. Places: EMP 43, SQT 23, SCV 22, NST 8.

**2.** a swamp world, open breathable air, hot and steaming, in the outer arms. Land: river deltas and island chains. Once low tech, now low tech, with scattered outposts, with a frontier field. It lived on small farms and local trade, run by nobody much, village by village, built in landed hulls turned into towns, joined by every road running to one port. It fell by more people arriving than it could feed, over months and decades, within a generation, after holding on, and is still in ruins, with a scar of bombardment from orbit in one region. Mood: The Long Queue. Still running: nothing. Belief: counting nights and lights in the sky. Now: nobody new. Features: still mirror lakes. Danger: sinkholes, gusts and tornadoes, night predators. Life: giant plants, blind cave crawlers. Sky: a sun fixed on the horizon, long grey rains, gold and faded paint. Sound: bells at set hours. Marks: banners and painted fists. Places: EMP 39, SCV 33, SQT 13, NST 10. Orbit reads: wrecks debris patrol echo:long_queue.

**3.** a canyon world, thin air, mild and dry, on the quiet rim. Land: striped badlands and mesas. Never settled. Mood: The Grand Scenery. Now: nobody new. Features: natural arches. Danger: sinkholes, spore bursts. Life: scrub and few trees, hive castes. Sky: one big moon, dust storms, prismatic edges. Sound: wind at every opening. Marks: glyphs nobody can read. Places: EMP 72, NST 28.

**4.** a steppe world, thin air, mild with rivers and lakes, in the outer arms. Land: open plains and open steppe; pelted by meteors. Once mid tech, now mid tech, with settled country, with a working port. It lived on research, run by nobody much, village by village, built in earth-sheltered mounds, joined by cart tracks and timber bridges. It fell by the air baking, over months and decades, about 110 years ago, and is mending, slowly. Mood: no strong mood. Still running: beacons and lighthouses. Belief: holding inherited places in a queue that hasn't moved. Now: pale people from below, a Vharn garrison. Features: natural arches, a geyser basin. Danger: spore bursts, meteor showers. Life: walls of thorn, night-adapted hunters. Sky: one big moon, long gold evenings, amber and leaf-red. Sound: wind in fluted stone. Marks: stamped numbered plates. Places: SQT 32, EMP 27, SCV 16, FAC 15. Orbit reads: stations patrol echo:lighthouses.

**5.** a canyon world, thin air, hot and dry, in the inner arms. Land: high plateaus and canyon country. Once low tech, now low tech, with settled country, with a frontier field. It lived on salt and minerals from dry seas, run by a company, built in brick terraces, joined by a dense rail web. It fell by something landing and then leaving, in the wedding season, within a generation, after holding on, and is still in ruins, with a scar of an eruption closing the sky in one region. Mood: no strong mood. Still running: nothing. Belief: sure the outside air is death. Now: Freeholder settlers, roving gangs. Features: downs of standing stones and mounds, endless city ruins under ash, stepped open pits and spoil heaps. Danger: mines and unexploded ordnance. Life: scrub and few trees, burrowers under everything. Sky: a timetabled noon eclipse, rain that bites, ash grey and brown. Sound: a low industrial drone. Marks: murals on every wall. Places: EMP 33, SQT 29, GNG 17, SCV 11. Orbit reads: echo:barrow_downs.

**6.** a city-covered world, thin air, mild with rivers and lakes, on the quiet rim. Land: open plains and river valleys. Once high tech, now mid tech, with a few survivors, with a large port. It lived on power for export, run by an elected council, built in stacked containers, joined by a web of highways. It fell by money ceasing to mean anything, at shift change, about 110 years ago, and is still in ruins, with a scar of hired guns taking the cities in one region. Mood: The Test Range. Still running: a station still on the air. Belief: guarding seed they may never plant. Now: nobody new. Features: wreckage in long lines along an old orbit, glowing cave mouths. Danger: sinkholes. Life: forest through every ruin, metal-eating crawlers. Sky: an ordinary sky, spore haze, cold violet light. Sound: an insect chorus. Marks: the ghosts of dead brands. Places: EMP 49, GNG 15, SCV 13, SQT 9. Orbit reads: yards stations traffic debris.

**7.** a tundra world, open breathable air, cold with frozen lakes, in the inner arms. Land: a lake district and rolling hills. Once mid tech, now low tech, with a few survivors, with a poor port. It lived on gambling, owned from off-world, built in gabled timber and shingle, joined by long ore conveyors and haul roads. It fell by one crop and one blight, on the day a treaty was signed, about 170 years ago, and is still in ruins, with a scar of a nuclear exchange in one region. Mood: Titans' Meadows; in one region, The Lean. Still running: cold stores and sleeper vaults. Belief: building everything to face one way. Now: nobody new. Features: ships that came down here. Danger: sinkholes, ambushes on the roads, disorientation. Life: scrub and few trees, shaggy ice beasts. Sky: an ordinary sky, big angry storm towers, pastels under moss. Sound: a low industrial drone. Marks: hand-painted route arrows. Places: EMP 34, SCV 16, SQT 14, GNG 7. Orbit reads: debris.

**8.** a dune world, open breathable air, hot and dry, on the quiet rim. Land: salt pans and dune seas. Once high tech, now low tech, with a few survivors, with a poor port. It lived on mining, run by a company, built in stacked containers, joined by long ore conveyors and haul roads. It fell by the records rotting away, over months and decades, about 110 years ago, and is mending, slowly. Mood: Moon Lakes; in one region, False Spring. Still running: furnaces banked and still hot. Belief: nothing in common. Now: keepers who never left. Features: buttes on skirts of scree, a stopped train on every line, huge shapes scraped into the plain. Danger: collapsing floors and rusted rigging, mines and unexploded ordnance, failing seals. Life: swollen, fused and wrongly coloured growth, static-charged nesters. Sky: one big moon, fire storms, ochre and slag. Sound: a low industrial drone. Marks: the ghosts of dead brands. Places: EMP 51, SQT 25, SCV 15, KPR 9. Orbit reads: echo:lockin.

**9.** an ice moon, thin air, frozen, water locked in ice, near the core. Land: rift valleys and slabs and ice plains; a moon of a giant. Never settled. Mood: no strong mood. Now: something listening, Kessari clans. Features: a chain of sinkholes. Danger: scalding vents. Life: no plants, almost nothing alive. Sky: a giant filling the sky, big angry storm towers, ochre and slag. Sound: wind at every opening. Marks: no marks at all. Places: EMP 74, WRP 26. Orbit reads: relays.

**10.** a dead world, thin air, mild and dry, on the quiet rim. Land: open plains and basalt plains. Once high tech, now low tech, with nobody left, with a poor port. It lived on carrying people across the world, run by the army, built in earth-sheltered mounds, joined by every road running to one port. It fell by famine, over months and decades, about 170 years ago, and is still in ruins, with a scar of walls going up in one region. Mood: The Test Range; in one region, The Long Queue. Still running: trams and trains on empty loops. Belief: nothing in common. Now: roving gangs. Features: an active volcano on the horizon, a vast thing never finished, a mannequin town built to be destroyed. Danger: unstable wrecks, mines and unexploded ordnance. Life: no plants, almost nothing alive. Sky: a light blinking on the moon's dark side, calm settled weather, soot grey and signal red. Sound: radio static that almost sounds like a voice. Marks: painted house marks. Places: EMP 82, MCH 18. Orbit reads: traffic beacon echo:hunger_roads echo:walked_out echo:last_train.

**11.** a scorched world, no air, scorching and bone dry, on the quiet rim. Land: basalt plains and striped badlands; a moon of a giant. Once mid tech, now mid tech, with settled country, with a working port. It lived on furnaces and heavy industry, run by mind-machines, built in clean corporate blocks, joined by pipelines across the land. It fell by the young leaving, over months and decades, within a generation, after holding on, and is mending, slowly, with a scar of a sickness of sleep in one region. Mood: Lamplight. Still running: clerk machines still processing forms. Belief: counting nights and lights in the sky. Now: nobody new. Features: makeshift launch rails along the shore. Danger: radiation zones. Life: crystal growing like plants, almost nothing alive. Sky: a light blinking on the moon's dark side, no weather, chrome and rust. Sound: a soft spacious drone. Marks: gang trophies and spikes. Places: EMP 40, SQT 28, SLP 11, SCV 10. Orbit reads: stations traffic beacon.

**12.** a city-covered world, thin air, mild with rivers and lakes, on the quiet rim. Land: open plains and river valleys. Once high tech, now low tech, with a few survivors, with no port at all. It lived on carrying people across the world, run by a company, built in sealed modules and airlocks, joined by long ore conveyors and haul roads. It fell by famine, over months and decades, within a generation, after holding on, and is still in ruins, with a scar of chemical war in one region. Mood: no strong mood. Still running: nothing. Belief: keeping one lamp lit in every town. Now: lone hermits. Features: wind turbines tuned to sing, wreck fields from old wars. Danger: mines and unexploded ordnance. Life: deep canopy, migrating flocks that no longer know the way. Sky: a light blinking on the moon's dark side, ash fall, beige and slate. Sound: an eerie thin drone. Marks: traveller chalk that means real things. Places: EMP 51, SCV 29, SQT 7, NST 7. Orbit reads: traffic wrecks beacon echo:lamplight echo:last_train echo:lockin.

**13.** a canyon world, thin air, mild and dry, in the outer arms. Land: mesas and striped badlands. Once mid tech, now low tech, with nobody left, with no port at all. It lived on orchards and pressing, run by mind-machines, built in stacked containers, joined by toll roads between walled places. It fell by factories eating the land, over months and decades, about 110 years ago, and is still in ruins. Mood: no strong mood. Still running: nothing. Belief: nothing in common. Now: Reaver salvage crews, Kessari clans. Features: a stopped train on every line. Danger: night predators. Life: succulents and oasis palms, almost nothing alive. Sky: a light blinking on the moon's dark side, calm settled weather, soot grey and signal red. Sound: wind at every opening. Marks: plain pictogram plates. Places: EMP 66, NST 18, MCH 16. Orbit reads: pirates relays beacon echo:last_train.

**14.** an oasis world, open breathable air, hot and dry, in the outer arms. Land: a lake district and rocky desert. Once mid tech, now low tech, with a few survivors, with a working port. It lived on pumping and refining fuel, run by a few great houses, built in earth-sheltered mounds, joined by cart tracks and timber bridges. It fell by money ceasing to mean anything, on an ordinary evening, dinner on the table, about 110 years ago, and is barely touched. Mood: no strong mood. Still running: long-life lamps in empty rooms. Belief: nothing in common. Now: a Meridian remnant. Features: huge shapes scraped into the plain. Danger: sinkholes. Life: succulents and oasis palms, something using web lines as roads. Sky: several moons, ash fall, steam and pink-white. Sound: a low industrial drone. Marks: faction sigils. Places: EMP 36, SQT 30, SCV 17, FAC 12. Orbit reads: stations traffic echo:hush_suburbs. Rare break: phase.spared.

**15.** a tundra world, thin air, cold with frozen lakes, on the quiet rim. Land: rolling hills and open steppe. Once high tech, now low tech, with scattered outposts, with no port at all. It lived on breaking ships and scrap, run by its guilds, built in sealed modules and airlocks, joined by pipelines across the land. It fell by civil war, over months and decades, within a generation, after holding on, and is still in ruins. Mood: Trench Moon. Still running: sentries still guarding. Belief: holding inherited places in a queue that hasn't moved. Now: nobody new. Features: crystal flowers across the plains, dead war machines lying in drifts, a whole colony ship standing on the ground. Danger: spore bursts, mines and unexploded ordnance, ambush plants. Life: crystal growing like plants, cliff-nesting fliers. Sky: an ordinary sky, a season of slow drifting seeds, dead neon. Sound: far guns or something like them. Marks: banners and painted fists. Places: EMP 41, SCV 18, MCH 13, GNG 12. Orbit reads: stations echo:glass_garden.

**16.** a steppe world, open breathable air, mild and dry, in the outer arms. Land: open plains and river valleys. Once high tech, now high tech, with nobody left, with a working port. It lived on schools and colleges, run by a company, built in clean corporate blocks, joined by skyways and ring roads. It fell by everyone leaving calmly at once, on the day a treaty was signed, about 110 years ago, and is still in ruins. Mood: Clockwork Hours. Still running: timers: barriers, sprinklers and shift bells for no one. Belief: nothing in common. Now: nobody new. Features: giant roadside statues and shaped buildings. Danger: armed machines still on post, spore bursts. Life: scrub and few trees, herds and their hunters. Sky: several moons, dust storms, ash grey and brown. Sound: far guns or something like them. Marks: gang trophies and spikes. Places: EMP 66, MCH 21, NST 13. Orbit reads: stations echo:library_towns echo:school_holidays echo:peace_treaty.

**17.** a storm world, open breathable air, mild with rivers and lakes, on the quiet rim. Land: striped badlands and mountain ranges. Never settled. Mood: no strong mood. Now: nobody new. Features: a chain of sinkholes. Danger: radiation zones, quakes, burning ground. Life: swollen, fused and wrongly coloured growth, cliff-nesting fliers. Sky: one big moon, mist at dawn, cold violet light. Sound: an eerie thin drone. Marks: glyphs nobody can read. Places: EMP 72, NST 28.

**18.** a rainforest world, open breathable air, hot and steaming, in the inner arms. Land: river deltas and wetlands. Once high tech, now low tech, with nobody left, with a large port. It lived on garrisons and arsenals, run by a civil service, built in pastel estates of identical houses, joined by a web of highways. It fell by a toxic haze from a thousand chimneys, over months and decades, about 170 years ago, and is still in ruins, with a scar of a sickness of sleep in one region. Mood: Foundry Belt; in one region, The Long Queue. Still running: a great horn sounding once a day. Belief: nothing in common. Now: nobody new. Features: pylon lines thick as hedges. Danger: night predators. Life: mangrove, reed and moss, blind cave crawlers. Sky: a tiny savage point of sun, long gold evenings, cold violet light. Sound: a horn across the region once a day. Marks: stamped numbered plates. Places: EMP 66, SLP 19, NST 15. Orbit reads: yards stations traffic patrol fleet_parked.

**19.** a cratered moon, no air, mild by day, waterless, in the outer arms. Land: rilles and low ridges and crater fields; low gravity, days of a few hours. Never settled. Mood: no strong mood. Now: a Reaver capital ship overhead. Features: jagged spires and volcanic necks, wreck fields from old wars, ships that came down here. Danger: vacuum. Life: no plants, rare things adapted to vacuum. Sky: double sunsets, no weather, black and hard white. Sound: near silence. Marks: glyphs nobody can read. Places: EMP 100. Orbit reads: wrecks pirates.

**20.** a tundra world, open breathable air, cold with frozen lakes, in the outer arms. Land: open steppe and rolling hills; strong seasons, cool amber light. Once mid tech, now mid tech, with a few survivors, with a working port. It lived on sealed greenhouse farming, run by an alien regime, built in brick terraces, joined by every road running to one port. It fell by plague, on an ordinary evening, dinner on the table, about 170 years ago, and is still in ruins. Mood: no strong mood. Still running: nothing. Belief: sure the outside air is death. Now: keepers who never left, pale people from below. Features: a forest of radio masts. Danger: quakes, gravity pockets, mines and unexploded ordnance. Life: grass and broadleaf, hunters that won't cross light. Sky: harmless bright meteor nights, mist at dawn, amber and leaf-red. Sound: a soft spacious drone. Marks: two sets of flags on the same ruins. Places: EMP 47, SCV 16, SQT 15, NST 9. Orbit reads: stations echo:mercy.

<!-- roll results end -->

### What the rolls show

**They read as different places.** Most of the samples above would get a different first sentence from a player. A gambling town in gabled timber on a tundra, owned from off-world (7), an airless world of furnaces run by mind-machines (11), arsenals and pastel estates in a rainforest (18), and a canyon nobody ever saw (3) don't overlap. Type, fall and trade together barely repeat: no settled world in this roll shares all three with another.

**No layer dominates.** Every layer with more than six values has a top value under a quarter of worlds, apart from defaults marked `blank`, such as `running.nothing`, which are honest absences. A slow fall has no moment, so `over_decades` is common, and that's correct.

**The worst nonsense is gone, and the odd stays.** Rules catch what's impossible, not what's strange. The samples still hold pairs that look wrong for a moment: a world "run by nobody much, village by village" doing research (4), an alien regime running sealed greenhouses in brick terraces (20). These are left in on purpose. Each one is a question a player will want answered, which is what the owner asked for.

**Weak spots, honestly.** Two things are thinner than the rest, and neither is a filter problem:

- Odd pairs inside one world's surface layers, like gusts and a dead calm, because surface layers don't read each other. A dozen `like` links would fix most of it, and it's safe to do later.
- The script can't test what the generators do with the values. A roll saying "ribbed adobe" is only as good as the kit that draws it, and four `build` values have no kit yet.

**Empty stays real.** Places are empty about 45% of the time across settled worlds, and the biggest single occupant on most worlds is nobody, which matches the old table's spirit. It ranges from busy recovered worlds to near-silent ones.

## 10. Gaps found and filled

With the parts laid out, these were missing. All of them are in the tables above.

**Whole layers the old list had no place for:** `trade` as more than six words, `moment`, `running`, `ran`, `build`, `creed`, `phase`, `palette`, `sound` and `marks`. The last three were only ever phrases inside entries.

**A world that wasn't hurt much.** Every inhabited world had to roll an apocalypse, yet Horse and Diesel "did not need a collapse". `phase.spared` allows it, with only mild falls.

**People who rise.** The old fit lines always went down. Tech rising past its old self is now possible, rarely, as the design's independent bands imply.

**Occupancy that bent to fit.** Four new codes: `KPR` keepers on a duty that outlived its reason, `SLP` sleepers, `GRD` guardians and `MIM` mimics. The old table was using husks for sleepwalkers and machines for Precursor guardians. `docs/space-pois.md` reached `SLP` independently, with the same name and meaning.

**Falls that were missing (Z71 to Z82).** A slide into barbarism, which the design names as a mid-collapse cause but the list didn't have; the owners selling the world for parts; underground fires, which Ember World needed; a failing magnetic field; an orbital mirror turned on its own cities; children born changed, using the design's mutation rule with no horror; a broadcast that changed behaviour; cities sinking; a megaproject that broke the economy; the rich leaving in the only ship; something landing and leaving; and more refugees than a world could feed, which is how many rim worlds would really have died.

**World types.** Nine with no row: plateau, oasis, irradiated ice, cryo-fire, petrified forest, a rogue world, dead world, city-covered world, and the Precursor Arc. All were already named in the design or in the old themes.

**Building for the sea.** `undersea_domes`, since a kit already exists and nothing used it.

**Trades.** Thirty more than the profile's six, such as salt, ice cutting, timber, digs, vat stock, sport, parks, and shipbreaking.

**Themes truly unlike the 112:**

- **The Waiting World.** Everything built and made ready, and the settlers never came.
- **The Test Range.** Built to be destroyed.
- **The Kept Grave.** A whole world made into its own grave marker.
- **The Sale.** The place is being sold out from under itself.
- **False Spring.** Fresh paint on old wounds.
- **The Timber Fleet.** People copying what they no longer understand.
- **The Good Kilometre.** Duty that outlived its reason.
- **The Reserve.** Set aside on purpose, and the fences outlived the reason.
- **The Second Wind.** It survived once, and is learning what that cost.
- **Wild Country, The Grand Scenery and The Stillness.** Moods for worlds nobody ever settled, which had nothing at all to set them apart.

**Ideas from the variety scout that now have a home.** Creeds, building cultures, afterhours timers, still-selling billboards, geoglyphs, the long furrow, warning country, dummy towns, the good kilometre, the timber fleet, the visitor centre, the simulated voyage, the launch ramp, the still wave, facing suits, the failed first base, long-year seasons, strange rains, and most of the behaviour ideas as `fauna` values. They're values, so they combine with everything.

**Gaps left open.** Three, because they aren't layers: nothing threads between worlds (that belongs to the system story in `docs/space-pois.md`), nothing runs on a clock beyond `running` (it needs the saved world clock), and `sound` values need the positioned sound work before most can be heard.

## 11. Owner's decisions

Decided. These stand.

1. Barrow Downs is renamed The Old Ground, and Black Library stays merged into Library Towns.
2. Horse and Diesel is renamed Hoof and Diesel. `yurt_camp` and `church_chapel` become `felt_camp` and `shrine_hall` when built.
3. Players never see a theme's name, only its effects. Names are for us and the debug screen.
4. About three settled worlds in ten have no mood at all.
5. Mid-collapse worlds stay at a few in every hundred.
6. Husks need mid or high tech.
7. The 112-row occupancy table is a record, not data: mixes come from adding layers.
8. The Husks are the threat, and the names move with them (8 November). `bloom` now means the Hollow: fungal growth, and Hollow worlds, which remain as before, overrun by the growth, but rare (`flora.hollow_overrun` sets `hollow=world`, behind the soft `hollow` rule). What was `hollow` or `HOL`, "something wrong here", is the Husks' warping: the tag `warped` and the occupant `WRP` (the code `HOL` still reads as it). Value ids moved with them: `fall.hollow_front`, `feature.hollow_basin`, `feature.hollow_patch`, `presence.hollow_shore`, `orbit hollow`. Theme ids (`eye_bloom`, `hollow_orchards`) and POI family ids stay as they were.
