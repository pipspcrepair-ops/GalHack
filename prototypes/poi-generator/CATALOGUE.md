# POI catalogue

Every kind of place asked for so far, and how far along it is.

- **Built**: a family in `data/*families.txt`. It generates, validates and passes every test.
- **Procgen-able**: the pieces already exist (a program or site plan, a style kit, rooms). A new family entry in data makes it, with no new code.
- **Data only**: sited from data, but not built yet (space places: orbit play is on hold).
- **Idea**: needs a new site part, program or system first. The note says what.

Programs are recipes (`programs.txt`): room demands, footprint shapes, circulation patterns, storeys and basements. Sites (`sites.txt`) compose parts: buildings, runways, hangars, towers, craters, rockets, fences, camps. Style kits (`kits/*.kit`) re-skin either one. A family can name its own `style`, and that is how a flavour works. **Alien** is a flavour: the same plans built in an alien kit (`alien_hive`, `alien_cathedral`, `alien_library`, `alien_temple`). Families opt in. Alien places are common on alien worlds (the alien theme's set) and rare outliers elsewhere. Alien is not Precursor: Precursor ruins are their own kit-built family.

## Built (28 families)

| Family | Tier | How it's made | Notes |
| --- | --- | --- | --- |
| Vharn Garrison | outpost | vharn kit | faction-held, core, defenders; ridge siting |
| Freeholder Farm | outpost | freeholder kit | flat ground, keeps away from Vharn |
| Abandoned Mine | site | freeholder + precursor deep kit | the mine turns into a Precursor dungeon further down |
| Research Station | outpost | meridian lab kit | buried, hatch only |
| Crashed Freighter | wreck | meridian ship kit | no core, salvage |
| Kessari Relay Spire | outpost | kessari kit | a landmark |
| Precursor Vault | site | precursor kit | no core |
| Deep Bunker | site | program bunker, concrete | worse with depth: dark, red emergency light, fog, Bloom at the bottom |
| Research Base | outpost | program research_base | sublevels, lifts |
| Ice Outpost | outpost | program ice_outpost | |
| Ominous Tower | town | program tower_block | 8 to 12 storeys, loops and atriums, corner setbacks |
| Abandoned Hospital | town | program hospital | wards, theatre, quarantine level under the mortuary |
| Cryo Vault | site | program cryo_vault | rows of pods, level on level |
| Biolab | outpost | program biolab | containment below |
| Command Bunker | base | program command_bunker | |
| Prison Block | base | program prison | cross or H plans, cell blocks round atriums |
| Meridian Garrison | base | program garrison_hq | faction-held, core, armoured foundations |
| Old Airfield | base | site old_airfield | runway, hangars, control tower, fence gate |
| Abandoned Spaceport | spaceport | site spaceport | a rocket on its pad (restorable), terminal, gantry |
| Abandoned Military Base | base | site military_base | walled, watchtowers, trench, command bunker |
| Blast Crater | site | site blast_crater | fallen world: radiation warned by signs, ramped paths down |
| Scrap Fort | camp | site scrap_fort | roving gangs: spawn and patrol markers only, no core |
| Old Mine | site | site old_mine | headframe and lattice tower over the shaft, drifts and stopes |
| Alien Hive Ruin | site | mine_works in alien_hive | chewed warrens; Bloom-thick infested state |
| Alien Library | site | research_base in alien_library | crystal stacks underground |
| Alien Cathedral | site | tower_block in alien_cathedral | a pale spire on a peak |
| Abandoned Factory | site | sites factory_low, factory_mid, factory_high | a production hall frozen mid-run (conveyor, robot arms, a load on the crane), warehouse and loading bays, works offices over a boiler house, smokestacks and a water tower; tech level by layout; gangs hold some |
| Old Farm | site | sites farm_low, farm_mid, agri_complex | farmhouse with a cellar, barn, silos, windmill or water tower, fields gone wild, greenhouses and hydroponic sheds, a vertical farm tower at high tech |

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

## Settings and flavours

- **Themes** (`themes.txt`): a themed set per kind of world plus 4 to 14% outliers, rarer and more ominous. A **start world** keeps its theme with no faction holding ground. Faction places turn up only as ruins (no core, no defenders).
- **Fallen** theme: craters with radiation, dead cities, bunkers, scrap forts.
- **Tech level** (`tech` on a theme and on a layout): factories and farms have low, mid and high tech layouts, and a world picks its own.
- **A century or two after the fall**: places carry an age (new, old-world, preserved) and each room an exposure (indoors, outdoors, breached). Old-world places hold no food outside sealed, frozen or airless rooms. Unsheltered timber has rotted and fallen in, while concrete, stone and steel stand as shells. Trees grow through courtyards and breaches.
- **Two layers**: old grandeur built in the program's own kit, with the recovery patch-up on top (lights, barricades, gang camps). Faction framing comes from roster and state: Vharn seize ruins, Meridian runs recovery, the Freeholders farm the ruins, the Carrion Reavers and the factionless gangs scavenge.
