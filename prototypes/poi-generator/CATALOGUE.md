# POI catalogue

Every kind of place asked for so far, and how far along it is.

- **Built**: a family in `data/*families.txt`. It generates, validates and passes every test.
- **Procgen-able**: the pieces already exist (a program or site plan, a style kit, rooms). A new family entry in data makes it, with no new code.
- **Data only**: sited from data, but not built yet (space places: orbit play is on hold).
- **Idea**: needs a new site part, program or system first. The note says what.

Programs are recipes (`programs.txt`): room demands, footprint shapes, circulation patterns, storeys and basements. Sites (`sites.txt`) compose parts: buildings, runways, hangars, towers, craters, rockets, fences, camps. Style kits (`kits/*.kit`) re-skin either one. A family can name its own `style`, and that is how a flavour works. **Alien** is a flavour: the same plans built in an alien kit (`alien_hive`, `alien_cathedral`, `alien_library`, `alien_temple`). Families opt in. Alien places are common on alien worlds (the alien theme's set) and rare outliers elsewhere. Alien is not Precursor: Precursor ruins are their own kit-built family.

## Built (38 families)

| Family | Tier | How it's made | Notes |
| --- | --- | --- | --- |
| Vharn Garrison | outpost | vharn kit | faction-held, core, defenders; ridge siting |
| Freeholder Farm | outpost | freeholder kit | flat ground, keeps away from Vharn |
| Abandoned Mine | site | freeholder + precursor deep kit | the mine turns into a Precursor dungeon further down |
| Research Station | outpost | meridian lab kit | buried, hatch only |
| Crashed Freighter | wreck | meridian ship kit | no core, salvage |
| Kessari Relay Spire | outpost | kessari kit | a landmark |
| Precursor Vault | site | precursor kit | no core |
| Deep Bunker | site | below: shelter | a long stair or a ladder down; hubs, grids, rings and alcove galleries by level, blast vestibules between sections; worse with depth |
| Research Base | outpost | site research_site: labs, below: research | sublevels through airlocks, a round test chamber with a gallery ring, lifts in a stacked core |
| Ice Outpost | outpost | program ice_outpost | |
| Ominous Tower | town | program tower_block | 8 to 12 storeys, loops and atriums, corner setbacks |
| Abandoned Hospital | town | program hospital | wards, theatre, quarantine level under the mortuary |
| Cryo Vault | site | below: cryo | in through decontamination, rings of pod halls level on level |
| Biolab | outpost | site biolab_site: labs, below: biolab | clean, dirty and isolation levels through airlocks, containment cells round a hub |
| Command Bunker | base | below: command | a bermed portal, an operations hall with a gallery over it, blast doors |
| Prison Block | base | site prison_site: program prison, below: prison | cross or H plans, cell blocks round atriums; the hold under it: cell blocks round a guarded hub |
| Meridian Garrison | base | program garrison_hq | faction-held, core, armoured foundations |
| Old Airfield | base | site old_airfield | runway, hangars, control tower, fence gate |
| Abandoned Spaceport | spaceport | site spaceport | a rocket on its pad (restorable), terminal, gantry |
| Abandoned Military Base | base | site military_base | walled, watchtowers, trench, command bunker |
| Blast Crater | site | site blast_crater | fallen world: radiation warned by signs, ramped paths down |
| Scrap Fort | camp | site scrap_fort | roving gangs: spawn and patrol markers only, no core |
| Old Mine | site | site old_mine, below: mine | headframe and shaft house, a cage and ladders down the shaft, winding drifts, stopes, ore chutes |
| Alien Hive Ruin | site | below: hive, in alien_hive | chewed round chambers on curved ways, ramps down; Bloom-thick infested state |
| Missile Silo | base | below: silo | a hatch on a pad, the lid flush with the ground; rings round the shaft level by level, the missile standing in it |
| Alien Library | site | below: library, in alien_library | galleries of crystal stacks, round reading chambers with galleries round them |
| Alien Cathedral | site | program cathedral in alien_cathedral | a cruciform nave with a tapering spire and leaning fins |
| Abandoned Factory | site | sites factory_low, factory_mid, factory_high | a production hall frozen mid-run (conveyor, robot arms, a load on the crane), warehouse and loading bays, works offices over a boiler house, smokestacks and a water tower; tech level by layout; gangs hold some |
| Old Farm | site | sites farm_low, farm_mid, agri_complex | farmhouse with a cellar, barn, silos, windmill or water tower, fields gone wild, greenhouses and hydroponic sheds, a vertical farm tower at high tech |
| Air-Defence Battery (Vharn) | base | site sam_site | launchers on emplacements, radar, a bunkered control room; ridge siting |
| Sky Railgun (Meridian) | base | site sky_railgun | a barrel 44 m up the sky on columns, capacitor halls, cooling towers, control bunker; rare landmark |
| Comms Relay (Meridian) | outpost | site comms_relay | masts and dishes on a peak, a cab tower |
| Supply Depot (Ledger) | outpost | site supply_depot | warehouses, landing pad, fuel tanks |
| Kessari Works | base | factory_mid in industrial | an old factory the machines restarted |
| Meridian Research Station | outpost | research_base in lab | labs back in use |
| Ledger Market | town | program market | stalls, shops, canteen; citizens, not hostile until provoked |
| Meridian Flats | town | tower_block in urban | apartment blocks lived in again |
| Survivor Hamlet | camp | site survivor_hamlet | shacks on a street, market corner, gardens, a water tank, a watch post; mood per settlement |
| Survivor Town | town | site survivor_town | old shells patched with scrap, two rows of shacks, a market, walls of salvage, watch posts |

## Procgen-able (a family entry away)

| Place | Recipe |
| --- | --- |
| City ruins, megacity blocks | tower_block in `urban`, several per site (a site with three building parts) |
| Underground city, bunker city under a nuked waste | bunker / command_bunker in `urban`, theme `fallen`, beside a blast crater site |
| Toxic surface prison colony | prison in `industrial`, toxic hazard rooms |
| Hospitals with quarantine | built (hospital); `lab` style for a clinic variant |
| Cryo vaults, seed vaults | cryo_vault in `lab` or `overgrown` |
| Terraforming station, climate plant, refinery (volcanic) | pump_station in `industrial` |
| Weather control station | control_tower (cab) in `lab`, peak siting |
| Observatory on a peak (human) | control_tower in `spaceport`, peak siting |
| Monster biolab | biolab in `lab`, infested weight up |
| Desert mining town | barracks + garrison_hq + pump_station parts in `miningtown` |
| Resort hotel (ocean or desert) | hospital or terminal program in `resort` (wards read as rooms) |
| Colony dome (moons) | tower_block court shape in `colonydome` |
| Jungle temple | mine_works or bunker in `alien_temple` or `overgrown` |
| Ship graveyard | a site of rocket and aircraft parts, wrecked |
| Alien temple, alien observatory | tower_block or control_tower in `alien_temple` / `alien_cathedral` |
| Alien research station, hospital, prison, seed vault, weather station, spaceport | the human program in an alien kit (research_base, hospital, prison, cryo_vault, control_tower, site spaceport) |
| Alien shipyard, mining rig, terraforming engine | hangar / old_mine / pump_station in an alien kit |

## Data only: space (orbit.txt, sited by OrbitPlacement)

Derelict freighter, wreck field, escape pod cluster, drifting hulk, broken station, warship wreck, generation ship hulk, old orbital shipyard, asteroid mining outpost, Reaver pirate hideout, Vharn picket, Meridian relay, Ledger waystation, Kessari foundry. A start system gets plenty of these wrecks close to home, danger 2 at most, and no live faction.

## Ideas (need something new first)

| Place | Needs |
| --- | --- |
| Space stations, orbital habitats, orbital prisons, orbital shipyards (built) | space layouts: a hull program and zero-g traversal (the layout already knows `InSpace` and station anchors) |
| Colony ship crashed into a mountain / on an ice moon | a large hull part that cuts into terrain (a stamp kind for a crash furrow) |
| Space elevator | a tether site part and a far-ring silhouette |
| Sunken city, underwater lab | flooding (`LayoutExtras.WaterY` is reserved) and swimming |
| Theme park, alien zoo | ride and enclosure site parts; an `alien_zoo` kit |
| Alien megastructures, alien cities, alien warships | site parts at a much larger scale, streamed |
| Alien colony domes, alien orbital habitats | the colony dome and orbital habitat above, in an alien kit |
| Gas giant moon research post | an `InSpace` variant of research_base on a moon |

## Ideas from the owner's brainstorm (design doc rev 425)

Recorded with a tech band each (L low, M mid, H high; the pre-collapse level they belong to). Starred
ones are built next, after the infrastructure, underground and utilities job, in this order:
evacuation camps, cisterns and missile silos first, then quarantine zones, stadiums used as camps,
collapsed highway interchanges, open-pit mines, offshore drilling platforms and arcologies.

| Group | Place | Band | Notes |
| --- | --- | --- | --- |
| Underground | Cisterns ★ | L M H | Vast half-flooded vaulted halls under towns; a water table and columns in the dark |
| Underground | Missile silos ★ | M H | A blast-door hatch in a fenced pad, a launch-control capsule, the silo shaft storeys down |
| Underground | Civil-defence shelters, fallout bunkers under towns | M H | Entrances in the street grid; bunk halls, decontamination, air plant |
| Underground | Catacombs and ossuaries | L | Niches and passages; bones as ornament, never gore |
| Underground | Underground car parks | M H | Ramps and levels of rusted cars, flooded lowest level |
| Underground | Bank and records vaults | M H | A vault door and deposit boxes; the archive stacks behind |
| Underground | Pneumatic freight tubes | H | Tube network with capsules stuck in it; a hub station |
| Underground | Funicular and cable-car tunnels | M | An inclined tunnel with a stranded car; anchored cables only |
| Collapse story | Evacuation camps ★ | M H | Tent rows, fences, registration tents, abandoned vehicle queues on the road in |
| Collapse story | Quarantine zones ★ | M H | A walled district with checkpoints, decontamination tents, the wall breached |
| Collapse story | Emergency broadcast towers | M H | A mast (anchored guy wires) and a bunkered transmitter hut |
| Collapse story | Battlefields | M H | Trench lines, tank graveyards, burnt-out convoys along a road |
| Civic and city | Stadiums and arenas ★ | M H | Bowl of terraces, the pitch turned into a camp, tunnels under the stands |
| Civic and city | Malls, hotels, cinemas | M H | Atriums, escalators, a dark screen hall |
| Civic and city | Museums with pre-fall artifacts | M H | Galleries round a hall; a few artifacts as loot |
| Civic and city | Universities and schools | M H | Lecture halls, labs, a library |
| Civic and city | Churches and cathedrals | L M | Nave, crypt and tower; stone survives |
| Civic and city | Cemeteries and mausoleums | L M | Grave rows (no gore), a mausoleum with a sealed crypt |
| Civic and city | Botanical domes gone wild | M H | Broken glass domes, jungle inside |
| Transport | Highway interchanges ★ | M H | Part of the network: stacked ramps on piers, a span or two down |
| Transport | Toll plazas, service stations, truck stops | M H | Network stops on highways, wrecked queues at the booths |
| Transport | Grand rail termini | L M H | A train shed over platforms; the network's rail ends there |
| Transport | Ferry terminals, harbours with sunken ships | L M H | Needs water work (sunken hulls, flooded docks) |
| Transport | Canals and locks, aqueducts, lighthouses | L M | Network pieces: lock gates, arched aqueducts, a lighthouse landmark |
| Industry | Open-pit mines ★ | L M H | Terraced pit stamp, haul roads spiralling down, slag heaps |
| Industry | Toxic tailings ponds | M H | A dam of tailings round a poisoned pond (warned hazard) |
| Industry | Offshore drilling platforms ★ | M H | A rig on legs in the sea, decks storeys up; needs sea siting |
| Industry | Container ports | M H | Stacks, gantry cranes, quays |
| Industry | Shipbreaking yards | M H | Hulls beached and half cut up |
| Industry | Chemical plants | M H | Columns, pipe racks, tank farms; warned leaks |
| Industry | Sawmills, paper mills, logging camps | L M | Log ponds, saw halls, stacked timber |
| Military and signals | Radar stations | M H | Domes and dishes on a ridge |
| Military and signals | Artillery emplacements, coastal batteries, fortress lines | L M | Casemates in a line along a coast or a ridge |
| Military and signals | Satellite dish arrays | M H | A field of dishes, a control block |
| High tech | Arcologies ★ | H | A town-sized structure, storeys on storeys; far-ring landmark |
| High tech | Space-elevator anchor | H | Tether site part, far-ring silhouette (see Ideas above) |
| High tech | Mass-driver track | H | A long ramp of coils up a mountainside |
| High tech | Cloning facility | H | Vat halls, sealed labs |
| High tech | Robot assembly works | H | Assembly lines, dormant frames |
| Low tech | Windmills and watermills | L | Small landmarks along rivers and ridges |
| Low tech | Frontier keeps and stockades | L | Timber walls and a stone keep |
| Low tech | Lumber camps, stone quarries | L | A quarry face stamp, sheds, a cart track |
| Low tech | Village chapels | L | A small stone chapel and graveyard |

## Settings and flavours

- **Themes** (`themes.txt`): a themed set per kind of world plus 4 to 14% outliers, rarer and more ominous. A **start world** keeps its theme with no faction holding ground. Faction places turn up only as ruins (no core, no defenders).
- **Fallen** theme: craters with radiation, dead cities, bunkers, scrap forts.
- **Tech level** (`tech` on a theme and on a layout): factories and farms have low, mid and high tech layouts, and a world picks its own.
- **A century or two after the fall**: places carry an age (new, old-world, preserved) and each room an exposure (indoors, outdoors, breached). Old-world places hold no food outside sealed, frozen or airless rooms. Unsheltered timber has rotted and fallen in, while concrete, stone and steel stand as shells. Trees grow through courtyards and breaches.
- **Two layers**: old grandeur built in the program's own kit, with the recovery patch-up on top (lights, barricades, gang camps). Faction framing comes from roster and state: Vharn seize ruins, Meridian runs recovery, the Freeholders farm the ruins, the Carrion Reavers and the factionless gangs scavenge.
