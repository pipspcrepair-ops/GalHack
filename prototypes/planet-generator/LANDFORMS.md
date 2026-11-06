# Landform catalogue

GalHack's worlds are divided into regions, and each region has a landform. This is the generator's own list. It draws on real geomorphology:
- tectonic and structural
- fluvial and lacustrine
- aeolian
- glacial
- coastal
- volcanic
- karst

It adds three alien landforms. Each was chosen because it looks different from the others, plays differently (on foot, driving, flying low, building), and can be made well by a height function on a torus.

There are 24 landforms. Code names are in `src/Gen/Landform.cs`, and recipes are in `src/Gen/Landforms.cs`.

**How a world uses them.** Each archetype variant has a landform palette: which landforms it can roll, how likely each is, and which few may make up a whole world on their own.
- **Single-landform worlds.** About one world in five is one landform throughout, such as a dune world, an endless ice plain, an endless steppe or an archipelago world.
- **Mixed worlds.** The rest roll 2 to 5 landforms, with a dominant one.
- **Open ground.** At least one is an open landform, so flat ground is as common as rugged ground.

**How regions are placed.**
- **Context first.** The generator first works out, on a coarse 256 m grid:
  - land and sea
  - distance to the coast
  - long uplift belts
  - temperature
  - dryness: dry belts, distance from the sea, and rain shadow behind ranges on the prevailing wind
  - a rough drainage map showing where the big rivers reach the sea
- **Mountains follow the uplift belts.** Ranges are traced as spines along the strongest uplift lines, several kilometres long, with foothills grading out from them.
- **Coastal landforms take coastal regions.** Fjords, cliffs and archipelagos claim coastal regions, and deltas fan out from the mouths of the biggest rivers.
- **Inland landforms fill the rest.** They go to the regions that suit them best, the pickiest claiming first. For example, dune seas take the driest regions and wetlands the lowest, wettest ones.
- **Smooth borders.** Neighbouring regions blend over several hundred metres. Each sample blends the landforms' relief parameters, so a border is a gradient, never a seam.

**Where a landform gets its heights.**
- **Sea level.** It is set before any landform shapes the land.
- **Shaping.** Landforms work in metres above that sea level, so a delta sits at the waterline and a fjord's floor is below it, wherever the coast falls.
- **Seamless.** Every field is sampled on the 4D torus or on grids that wrap, so all of it is seamless across both seams.

| Group | Landform | One line | Where it occurs | Relief recipe | Archetypes |
| --- | --- | --- | --- | --- | --- |
| Tectonic | **Mountains** (alpine ranges) | Long ranges of sharp peaks with passes and deep valleys between spurs. | Along uplift belts: a spine traced along each of the strongest uplift lines, never blobs. Snow on the tops of all but the hottest worlds. | A broad massif rising towards the spine, carved by ridged noise into crests, spurs and valleys. Heights grow with the square of how much the range holds, so the edges grade into foothills. The range sags at intervals along its spine for passes, and tapers at its ends. | Temperate, arid canyon, ice, lava volcanic (low basalt ranges), alien crystal |
| Tectonic | **Hills** (rolling hills and foothills) | Rounded hills with flat-floored valleys between them. | As foothills grading out from every range, and as hill country regions of their own in wetter interiors. | Rolling hills at the archetype's hill height, a gentle swell, and valley floors flattened below a level. | Temperate, ice, alien fungal |
| Structural | **Plateau** (tablelands and escarpments) | Wide, flat raised tables ending in wandering escarpment cliffs, some sheer and some walkable ramps. | Dry to mid interiors at moderate height. | Flattened base lifted onto one or two tiers with jagged, wandering edges. | Temperate, arid, ice, lava, alien crystal |
| Structural | **Mesas** (mesa and butte country) | Isolated flat-topped mesas and buttes with stepped cliff sides above open plain. | Dry regions, next to canyons and plateaus. | A flat floor with closed plateau contours (mesas and buttes) and terraced risers, like rock strata weathering back. | Arid, lava (basalt mesas), alien crystal |
| Structural | **Canyonlands** | A raised plateau cut by long, winding, stepped canyons hundreds of metres deep. | Dry uplands, often beside mesas. | Plateau lift with a network of flat-floored, ledged canyons along the zero lines of a warped noise field. | Arid, lava, alien crystal |
| Lowland | **Plains** | Genuinely flat to gently rolling open ground, for kilometres. | Interior lowlands and basins, and coastal lowlands that end in beaches. | The continent's broad shape, low-passed so nothing under about 2 km survives and compressed round the country's own level, plus a few metres of swell. Wide, meandering rivers with low banks. | Every archetype (on alien crystal worlds, crystal plains) |
| Lowland | **Steppe** | High, open, dry grassland: very broad swells under a big sky. | Semi-dry interiors at middle latitudes, and in rain shadows. | Flattened like plains, lifted a few tens of metres, with long low swells. Few rivers. | Temperate, arid, ice (tundra steppe) |
| Fluvial | **River valleys** (floodplains between bluffs) | Broad, flat valley floors with wide rivers, between low bluffs and terraces. | Interior lowlands where a lot of water collects. | Raised ground cut by broad, flat-floored valleys with gentle walls. Rivers are more frequent, wider and meander more. | Temperate, arid canyon, alien fungal |
| Aeolian | **Dune sea** (erg) | A sea of sand dunes in long, regular crests under a steady wind. | The driest regions: dry belts, rain shadows and continental interiors. | Transverse dunes as a wave at a whole number of periods round the planet, square to the planet's prevailing wind. Each is a gentle climb (about 18 degrees at its steepest) to a sharp brink, then a slip face falling at the sand's angle of rest (about 35 degrees). The crests stand up to about 36 m on a 240 m wave, wander and link, and rise and dip along their length, and the dune height swells and fades across the field. | Arid, sometimes temperate, alien crystal (crystal sand), ice (snow dunes) |
| Aeolian | **Rocky desert** (reg, hamada and salt pans) | Hard, flat stony plain and dead-flat salt pans, with lone rock domes (inselbergs) standing out of it. | Dry, raised ground near the ranges, leaving the lowest, driest basins to the dune seas. | Very flat base, sparse tall rounded rock domes, and a little grit. | Arid, temperate (rare), lava, alien crystal |
| Erosional | **Badlands** | Bare, steep, densely gullied soft rock in banded layers. | Dry regions at the edge of plateaus and mesas. | Dense small-wavelength ridges and gullies on a flattened base, with thin terraced bands. | Arid, lava (ash badlands), temperate (rare) |
| Wet | **Wetlands** (marsh and swamp) | Low, flat, waterlogged ground with pools and channels everywhere. | The lowest, wettest regions near the coast and rivers. | Squashed down to a metre or two above the water, with shallow pools cut below it. Many small rivers. | Temperate rainforest, ice hot spring (thermal marsh), alien fungal (spore marsh) |
| Wet | **Lake district** | Low hummocky ground holding dozens of lakes. | Low to middle ground, cooler regions first (glacial lakes). | Squashed to tens of metres, with hummocks and lake basins cut below the water. | Temperate, ice, alien fungal |
| Wet | **Delta** | A fan of branching channels and marshy islands where a big river meets the sea. | At the mouths of the planet's biggest rivers, on unfrozen coasts. | A fan from an apex inland out past the shore. The land is squashed to the waterline. The river runs out across it in channels that bend more the further they go, shedding side channels off either bank. Some die out in the marsh, and the rest draw apart, so the fan widens towards the sea, with pools between them. | Temperate, arid (a desert's one green river), alien fungal |
| Glacial | **Ice plain** | An endless, smooth, near-flat plain of ice and snow. | Cold regions and cold worlds. | Very flat, with a faint swell and almost no fine roughness. | Ice |
| Glacial | **Fjords** | Steep mountain walls dropping into long, narrow, drowned valleys. | Cold, high coasts only. | Mountain relief rising steeply from the shore, cut by U-shaped troughs whose floors lie below sea level near the coast and rise inland into glacial valleys. | Ice, temperate (cold coasts) |
| Coastal | **Coastal cliffs** (cliffs, headlands and coves) | Sheer cliffs standing straight out of the sea, with flat tops and the odd cove beach. | Coasts on higher ground. | A cliff step at the shoreline with a jagged edge, broken here and there into coves. | Temperate, arid, ice (ice cliffs), lava |
| Coastal | **Archipelago** | Chains of islands across a shallow sea. | Shallow shelves offshore, or the whole world. | Island noise raised on the shelf before sea level is chosen, so islands rise out of the shallows. | Temperate, ice, lava ocean, alien fungal |
| Volcanic | **Volcanic field** | Cinder cones and craters scattered over lava flows. | Hot regions, and the equator on hot worlds. | A cone field on a wrapping grid, each cone with a summit crater, lobed lava flow steps and heat. | Lava, ice hot spring, temperate (rare) |
| Volcanic | **Basalt plains** (flood basalt and ash plains) | Vast, dark, flat plains of old lava in low steps, with pressure ridges. | Low regions of volcanic worlds. | Flattened, with lava-flow steps about 10 m high and small ridges. | Lava |
| Karst | **Karst** (tower karst) | Steep, round-topped limestone towers rising straight out of a flat plain. | Warm, wet regions. | Very flat base with dense steep towers, a threshold on a short-wavelength field. | Temperate rainforest, temperate meadow (rare), alien fungal |
| Alien | **Shard fields** | Forests of knife-edged crystal spires and blades. | Anywhere on crystal worlds. | Sharp ridged spires rising from a flattened floor. | Alien crystal |
| Alien | **Dome fields** | Swollen, bulbous domes packed together, with creases between them. | Anywhere on fungal worlds. | Billowed hills (absolute-value noise) that give round domes and sharp creases. | Alien fungal |
| Alien | **Ringlands** | Concentric ring ridges round shallow centres, like ripples frozen in rock. | Open interiors on alien worlds. | Ring structures on a wrapping grid: rings of decaying height round each centre, on flattened ground. | Alien crystal, alien fungal |

## Open, rolling and rugged

The world mix and the flat-land checks group the landforms by how they play:

| Kind | Landforms |
| --- | --- |
| **Open** (build, drive, fly low) | plains, steppe, rocky desert, ice plain, basalt plains, wetlands, delta |
| **Rolling** | hills, river valleys, lake district, dune sea, dome fields, ringlands, archipelago |
| **Rugged** (the dramatic places) | mountains, fjords, plateau, mesas, canyonlands, badlands, coastal cliffs, volcanic field, karst, shard fields |

## Wildlife

Each landform shapes the wildlife too (design doc rev 423). The wildlife generator groups landforms into habitats and builds each landform's own species for its ground:

| Habitat | Landforms | Creatures |
| --- | --- | --- |
| **Open** | plains, steppe, ice plain, basalt plains, rocky desert, ringlands | herds of long-legged runners and the pack hunters that chase them |
| **Steep** | mountains, fjords, coastal cliffs, mesas, canyonlands, plateau, karst, shard fields | climbers with grip, and cliff gliders |
| **Sand** | dune sea, badlands | burrowers, sand-swimmers and ambushers |
| **Wet** | delta, wetlands, lake district, river valleys, archipelago | long-legged waders and lurking ambushers |
| **Broken** | hills, dome fields, volcanic field | grazers, lone predators and armoured crawlers |

## Reference

**Empyrion: Galactic Survival** (local install, read only). Each planet type has one heightmap, and biomes are chosen by height, slope, humidity, temperature and latitude criteria, some forced by stamps:
- `Content/Playfields/*/playfield_dynamic.yaml` (`Biomes`, `SelectionCriteria`, `BiomeStamp*`)
- `playfield_static.yaml`: creature spawns list the biomes they spawn in (`Entities: ... Biomes: [...]`)

GalHack's design doc goes further: landform regions, blended at borders, with their own relief, which Empyrion's single heightmap per planet type doesn't have. Our species' home landforms play the role of Empyrion's per-spawn biome lists. Nothing was copied.
