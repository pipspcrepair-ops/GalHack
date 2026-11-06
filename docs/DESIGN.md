# GalHack — Game Design Document

> This is a published copy of GalHack's living design document, last refreshed from revision 561. The design doc is the authority on how the game should work; the code may lag behind it.

## 1. Overview

GalHack is an open-world space survival sandbox: the player wakes from cryosleep in a buried bunker on a hostile planet with almost nothing and climbs, through mining, crafting and block-building, to captaining a self-built capital ship that warps between star systems. Its main draw is discovery: a procedurally generated galaxy whose worlds, landscapes, structures and points of interest keep surprising the player. There is no win state; the arc is the player's own escalation in scale, safety and reach.

| Attribute | Design |
| --- | --- |
| Genre | Survival crafting + voxel/block construction + vehicle combat + exploration |
| Perspective | First and third person, switchable at any time; seated vehicle view |
| World | Seeded procedural galaxy of star systems; every planet, orbit and space sector is its own instanced zone (a "playfield") |
| Modes | Survival and Creative; single player and co-op over Steam for a group of friends; all players share one faction |
| Session shape | Open-ended; an experienced player flies a small vessel within 1–2 hours and a capital ship within about 10, and exploring the galaxy never runs out |
| Player fantasy | "From castaway to captain": find what's out there, survive it, and build the ship that takes you further |
| Primary platform | PC, mouse and keyboard |
| Visual style | 1970s sci-fi paperback art inspired by Chris Foss: huge, chunky, boldly painted ships; alien designs get stranger the more alien their builders (section 5) |

**Scope.** This document covers design pillars, gameplay loops, progression and every major mechanic. Story and lore are out of scope, apart from the short info about aliens and creatures shown when the player discovers them (section 6); the PDA mission system is kept only as onboarding tutorials (section 20). Faction names, exotic material names and named systems are GalHack originals. Numbers marked as reference values are starting points for tuning, not commitments.

**Setting.** The galaxy is recovering from a collapse on a galactic scale. It happened a century or two ago. Whatever it was, it was fast and devastating: places were abandoned mid-moment, with tables still set, rides frozen mid-motion, evacuations left half done. The old civilisation's spaceports, shipyards, cities, stations, colony ships and research sites lie abandoned everywhere. The factions, the Freeholders and the players are the ones picking up the pieces, salvaging and rebuilding in the ruins of what came before. The core conflict: civilisation is returning to the stars, but whatever caused the collapse is returning too, spreading outward from the galactic core. Players start on the quiet rim and push coreward, toward the factions and, beyond them, toward it. It is the source of the Hollow Bloom, but what it is stays a mystery: the game never explains it outright, and players only ever see its effects and its spread. The tone is Lovecraftian: something interdimensional shredding through space and time, never shown, only felt. The rim is merely ominous; the closer to the core, the more reality frays: stars that are slightly wrong on the map, wormholes that shouldn't exist, gravity and time behaving strangely near infested sites, and, rarely and late, places folded in on themselves that are deliberately wrong.

**Who's who after the collapse:**

| Who | Place in the post-collapse galaxy |
| --- | --- |
| The old civilisation | What collapsed. Their spaceports, shipyards, cities, stations and colony ships are the ruins everywhere |
| Precursors | Far older aliens, gone long before the collapse. Their megastructures, vaults and Arcs survived where newer ruins crumbled |
| Hollow Bloom | The collapse itself, or its rot: something the old civilisation dug out of deep Precursor sites, which spread like a virus. It's why places were abandoned mid-meal, and it's thickest in the deepest, oldest places |
| Vharn Dominion | An alien empire that rode out the collapse on its fortress worlds, now expanding into the vacuum and seizing ruins by force. The main threat |
| Meridian Combine | The surviving corporate remnant of the old civilisation. Holds the inner stations and tries to restore order and recover old tech; friendly but self-interested, and pays for salvage and survey data |
| Ledger Exchange | The merchants who kept the trade lanes alive through the dark years. Neutral, rebuilding commerce |
| Kessari Clans | The old civilisation's machines: labour and war AIs left behind when their makers fell, who woke into independence, formed clans and resist the Vharn |
| Freeholders | Survivors rebuilding on the ground, with farms, mines and outposts built from the ruins. The recovery itself |
| Carrion Reavers | Scavengers turned pirates, picking the bones of the old world and raiding the haulers |
| Roving gangs (no faction) | Fallen-world survivors gone feral |
| The players | Sleepers who wake from cryo into the ruins and build up from salvage; part of the recovery |

## 2. Design pillars

Five pillars, led by discovery, carry one player fantasy, "from castaway to captain," and stand on a foundation of a persistent, shared universe. Every feature should serve at least one pillar and break none; when two pillars conflict, discovery wins. One rule sits above all of them: smooth play first (guiding rules below).

&#91;embedded content: design pillars · 1 fantasy, 5 pillars, 1 foundation\]

| Pillar | What it means | Expressed through | Design test for any feature |
| --- | --- | --- | --- |
| 1. Discover the unknown (primary) | The galaxy is the main attraction; the next system should hold something the player has never seen | Procedural star systems and planet archetypes, varied landscapes and biomes, generated and handcrafted structures, points of interest, space anomalies, rare one-off set pieces | Does it give the player something new to find, or a reason to go and look? |
| 2. Survive the frontier | The universe is indifferent and hostile; safety is earned and always local | Vitals, environmental hazards, scarce early resources, hostile factions, danger-graded planets | Does it create pressure the player must plan around? |
| 3. Build what you fly | Player-made structures are the main game objects, and a build's layout decides how it performs | Block building, functional devices, mass and thrust, power, CPU budget, block-level damage | Does the player's build choice change the outcome? |
| 4. Escalating scale | Each tier opens a bigger frame: on foot, base, hover vessel, small vessel, capital vessel, interstellar | Tech tree gating, structure classes, material tiers, warp travel | Does it open a bigger space or hand over a bigger tool? |
| 5. Freedom of approach | No forced path; several valid routes to every resource and goal | Mine, salvage, loot, trade or raid; board and hijack enemy bases and ships; build by hand or from blueprints | Can the player solve it at least two ways? |
| Foundation: smooth, persistent and shared | Play is smooth and bug-free; worlds and builds persist; players share servers and blueprints | Saves, Steam co-op, one shared player faction, blueprint sharing | Is it reliable and annoyance-free, does it persist, and can everyone share in it? |

**Anti-goals.** What GalHack deliberately is not:

- Not a narrative game: no system is gated behind story beats; PDA tutorials teach systems but never lock them.
- Not a flight simulator: flight is assisted (inertial dampening, auto-hover near ground), and difficulty comes from the ship's build, not the controls.
- Not a pure creative builder: in Survival, every block costs material and every device costs power and CPU.
- Not a competitive shooter: gunplay should feel satisfying, but there's no PvP and no competitive balance to chase.

**Guiding rules**

1. **Stable and smooth first.** Nothing a player builds, drops or stores ever disappears, and a bug-free, annoyance-free experience outranks every feature. If a feature can't be made reliable, it's simplified or cut.
2. **Depth where it's interesting.** Worlds, points of interest, ships and structures get the variety, detail and production effort.
3. **Simple where it's upkeep.** Survival, ship management, storage and logistics stay intuitive and quick. No chores, busywork or menus full of minutiae.

## 3. Core gameplay loop

The core loop is **explore → gather → craft → build → expand reach**, and every turn of it ends at a harder frontier with rarer resources, which starts the next turn. Survival pressure pushes the player through the loop; XP unlocks and better ores pull them forward; power, mass and CPU limits add friction that makes each build a decision.

&#91;embedded content: core loop · 5 steps, 3 forces, 1 loop back\]

| Loop | Timescale | Steps | Question the player asks | Reward |
| --- | --- | --- | --- | --- |
| Moment-to-moment | Seconds to minutes | Look, move, mine or fight, loot, check vitals | "Am I safe, and what is that?" | Ore, loot, small XP |
| Session (core) | 20–90 minutes | Set a goal, gather, process and craft, build or upgrade, try the new capability | "What do I need for the next device or ship?" | A new device, vessel or upgrade |
| Expansion | Under an hour per step early on, a few hours per step later | Reach a new playfield, land a foothold base, exploit its resources, unlock the next tier | "Where can I go now that I couldn't before?" | A new zone, ore or structure class |
| Endgame | Open-ended | Capital-ship fleets, faction standing, expeditions, large creative builds | "What do I want to build or take?" | Self-set goals, status, territory |

**Forces acting on the loop**

- **Push (survival):** food, oxygen, temperature and radiation drain constantly; raiders, patrols and wildlife attack the player and their structures. Standing still costs resources.
- **Pull (progression and discovery):** XP grants unlock points for the tech tree; harder planets and deep space hold the ores that higher tiers need, plus landscapes, structures and anomalies the player has never seen.
- **Friction (constraints):** every device draws power, adds mass and consumes CPU budget. A bigger ship needs better thrusters, generators and CPU extenders, which need rarer ores, which sit in more dangerous places.
- **Anchor (the base):** each expansion step repeats the arc in miniature: land, build a foothold, exploit, move on. Bases are the safe hub where loot turns into components.

**Fail states are soft.** Death drops the backpack at the corpse and respawns the player at a healing booth or clone chamber or a fallback spawn point. The cost is time and a risky corpse run, never the save. Being stranded works the same way: there's always a way back (section 14).

## 4. World generation

The galaxy is generated from a seed, one system at a time when it's first visited. Each level of the hierarchy decides what the next level can be: region picks star classes, a star picks its planets, a planet's type picks biomes, and biomes pick creatures, resources and points of interest. Where a place sits in the galaxy therefore predicts what kind of place it is, and players learn to read the map.

&#91;embedded content: world generation · 7 levels, galaxy to POI\]

**Galaxy**

- Reference scale: 20,000–35,000 stars in a disc about 500 light years in radius, plus nebulae, double stars and small satellite galaxies.
- Same seed, same galaxy. A system is generated only when someone first visits it, then saved; unvisited stars cost nothing.
- Faction territories are painted over the regions; the gaps between them are unclaimed space.

| Region | Character | What it's for |
| --- | --- | --- |
| Core | Dense old stars around a central black hole; strongest factions and precursor sites | Endgame expeditions |
| Inner arms | Faction space, trade routes, stations | Mid-game trade and missions |
| Outer arms | Frontier systems, pirates, Freeholder colonies | Early-to-mid exploration |
| Mid-galaxy voids | Few stars, long gaps between them | Tests hyperdrive range and fold crystal reserves |
| Periphery and satellite galaxies | Exotic, unclaimed, far from help | Late-game discovery |

**Zones**

Everything is zoned: each place below is its own self-contained zone with its own save records. Players feel one continuous galaxy because the seams are hidden, not because the world is one piece.

| Zone | What it covers | How you get there |
| --- | --- | --- |
| System space | The space around a star: the sun, interplanetary space, belts, stations and anything between the planets | Warping in from another system, or climbing out of a planet's orbit |
| Planet orbit | The space around one planet: its rings, nearby wrecks, stations and asteroid fields | In-system warp, or flying up from the surface |
| Moon orbit | The same for each moon, including every moon of a gas giant | Flying out from the parent's orbit, or in-system warp |
| Gas giant orbit | A gas giant's rings, its storms seen from above and its moons; gas giants have no surface | In-system warp |
| Surface | A landable planet or moon: a flat map that wraps both east-west and north-south, with no edges or walls, up to 64 × 32 km | Descending from orbit |
| Deep-space site | A wormhole mouth, nebula pocket, derelict field or other anomaly away from the planets | Warping to it once it's been found |
| Ship cabin | The inside of each occupied ship, in its own private world | Boarding |

- **Hidden seams:** the destination starts loading as soon as a transition is likely (nearing the atmosphere edge, or a warp spooling up). The re-entry or warp sequence lasts until it's ready, so a slower PC gets a longer tunnel, never a freeze.
- **Landing:** you come down where you crossed into the atmosphere; to land somewhere specific, fly over it in orbit first.
- **Wrapping both ways:** the map wraps both ways, like a torus. Crossing the north edge brings you in at the south edge, and crossing east brings you in at the west, so you can travel forever in any direction with no walls. Both the north and south edges are polar, so the cold band runs continuously across the seam.
- **Seeing other zones:** from orbit you see the real surface below, and from the surface the real moons and planets overhead, drawn from the same generator.
- **What crosses:** only players, their ships and their cargo move between zones. NPC ships break off at the edge, and shots stop at the boundary.
- **Gameplay zones are data:** each zone carries tags (region, star class, danger grade) and a list of circles (NPC territory, POIs, player bases, no-spawn areas). The encounter director, the map and content gating all read the same data.

**Star classes** decide which planets, resources and dangers a system can hold.

| Star | Typical bodies | Rare materials | Danger |
| --- | --- | --- | --- |
| Yellow and orange dwarfs | Temperate, arid and ice worlds; the start systems | Few | Low |
| Red dwarf | Small cold rocky worlds, many moons | Uncommon ores | Low to medium |
| Brown dwarf | Dim, cold, sparse; ice moons | Little; quiet systems | Low |
| Blue and white giants | Large hot worlds, barren metal worlds | Starsilver | High radiation |
| Protostar and young star | Lava and nascent worlds, molten moons | Emberite | High |
| Dying star (red giant) | Scorched and lava worlds | Emberite, gold | High |
| Dead star (white dwarf) | Dead worlds, irradiated ice | Fold crystal | Medium to high |
| Pulsar | Irradiated debris, crystal fields | Fold crystal | Very high |
| Black hole | Accretion debris, ancient derelicts, precursor sites | Unique materials | Extreme |
| Binary stars | A mix of both stars' bodies, double sunsets | Mixed | Mixed |

**Space geography and anomalies**

Real astronomy gives the galaxy its landmarks, and a few science-fiction anomalies add the strange. Each one changes how a place plays, not just how it looks.

| Feature | What it is | How it plays |
| --- | --- | --- |
| Emission nebula | Glowing gas where stars are born | Coloured skies and shorter sensor range; young stars and emberite inside |
| Dark nebula | Cold dust that blots out the stars | Near-blind navigation; pirate and Bloom hideouts |
| Reflection nebula | Dust lit blue by nearby stars | Calm and beautiful; rich survey data |
| Supernova remnant | The expanding shell of an exploded star | High radiation, rare and exotic materials, shattered planets |
| Pulsar | A spinning neutron star sweeping beams of radiation | Pulses arrive on a steady beat; move between the sweeps |
| Magnetar | A neutron star with a crushing magnetic field | Magnetic storms knock out shields and scanners for a few seconds |
| Black hole | A gravity well with a glowing accretion disc | A strong pull near the horizon, with warnings; ancient wrecks in the disc |
| Binary and trinary stars | Stars orbiting each other | Double shadows and sunsets, shifting temperatures, mixed planet types |
| Globular cluster | A dense ball of ancient stars near the core | Many short jumps, old civilisations, precursor sites |
| Rogue planet | A planet with no star | Pitch dark and frozen, lit only by its own geology; unique finds |
| Protoplanetary disc | A young star's ring of dust and debris | Asteroid-rich, meteor showers, emberite |
| Comets and asteroid belts | Ice and rock in orbit | Mining and salvage; debris tails to fly through |
| Ice and gas giants | Huge worlds with rings and many moons | Moon systems, ring mining, storms visible from orbit |
| Wormhole | A natural tunnel between two distant points | A shortcut across the galaxy: some stable, some shifting or collapsing, some one-way |
| Gravity anomaly | A pocket where gravity fades or points the wrong way | Floating rocks, debris you can walk up, impossible terrain |
| Fold scar | The wound left by an old warp accident | Short-range warp is cheaper here, but strange things leak through |
| Ion storm | Space weather drifting through a system | Static on scanners, draining shields, a light show from orbit |

- **Starting on the border:** every game begins on a border world out at the galaxy's rim. The galaxy map isn't known at the start: players see only the neighbouring stars they can travel to, and each jump reveals the stars around the new system, marked on the shared map for everyone. The rim has no factions; travelling coreward, players start encountering them.
- **Maps:** the Tab menu's Map section has three levels: a world map of the planet you're on, a system map of its star's bodies, stations and known wrecks, and the star map of the stars discovered so far. Each shows only what's been found, and everything marked is shared.
- **Wormholes** sit somewhere in a system's space, unmarked until someone finds them, but they always leave clues. The stars visibly warp around a mouth, so a sharp-eyed pilot can spot one from far off. A system scan picks up the gravity disturbance as a bearing and rough distance. A survey probe fired from the galaxy map flags an unknown anomaly before anyone jumps. Signal leads, Kessari relay spires and traders selling rumours point the way too. Once found, players short-range warp to the mouth, and it's marked on the shared map for everyone. A stable wormhole links two systems both ways for free; an unstable one collapses after a set time or number of passes and can strand a crew on the far side (section 14).
- Anomalies follow the warning rule: they're shown on the map once found, and their effects build up before they hurt.
- Real features follow real rules where that makes the game more interesting, and bend them where it would be dull or unfair (no instant spaghettification).

**Planet archetypes** (each with 2–4 variants; sizes vary in four classes as in Empyrion: 8 × 4 km, 16 × 8 km, 32 × 16 km and a rare 64 × 32 km, with moons in the two smaller classes and start worlds always 16 × 8 km; most ranges top out around 500 m, with the highest peaks reaching about 900 m through the cloud layer, still below orbit)

| Archetype | Signature landscape | Key hazard | Signature resources | Life |
| --- | --- | --- | --- | --- |
| Temperate (meadow, rainforest, plateau, dark-sky) | Rivers, forests, oceans, cliffs | Night predators | Common ores, wood | Grazers, big predators, insects |
| Arid (dunes, canyon, oasis) | Mesas, canyons, oasis lakes | Heat, thin air, sandstorms | Silicon, magnesium | Burrowing worms, nests |
| Ice (glacier, hot-spring, crystal desert) | Glaciers, steaming springs | Cold, no air | Ice, cobalt | Ice beasts |
| Swamp (mire, fungal forest) | Fog, giant fungi, bogs | Toxic air | Biomass, rare plants | Slimes, swarms |
| Alien | Strange-coloured flora, crystal growths | Toxic air, odd weather | Unusual plants and crystals | Bizarre fauna |
| Barren | Craters, dust plains | Vacuum, radiation | Iron, cobalt | None; Vharn outposts |
| Barren metal | Metallic plains, spires | High gravity | Gravium, starsilver | Guarded deposits |
| Lava (volcanic, lava ocean) | Lava lakes, ash fields | Extreme heat, meteor showers, fire rain | Emberite, gold | Aggressive heat-adapted fauna |
| Nascent | Broken young crust | 3.8–4.3 g; hover vessels fail | Rich mixed ores | Few |
| Ocean | Island chains, deep trenches | Little dry land | Fold crystal in deep water | Sea life |
| Irradiated ice | Glowing ice sheets | Radiation | Fold crystal | Mutated fauna |
| Dead world | Ash plains, ruined cities | Radiation, cold | Salvage | Hollow Bloom mutants |
| Graveyard world | Wreck fields from old wars | Scavengers, unstable wrecks | Salvage, unique materials | Scavengers |
| Gas giant | Not landable; rings and many moons | Storms, radiation belts | Ring ice, moon ores | None |
| Moons (rock, ice, desert, lava, forest) | Small, low gravity, often rich in one or two ores | Varies | One or two specialities | Few |

**Variety rules** (what stops 20 archetypes from looking like six)

1. **Archetype + variant + modifiers.** Every planet rolls one or two modifiers that change both the look and the play: tidally locked, ringed sky, perpetual storm, giant flora, crystal fields, low gravity, glowing nights, shattered moon overhead, ancient battlefield.
2. **Distinctness test.** Each archetype must differ from every other on at least three of: terrain shape, palette, sky, flora, fauna, hazard and resources.
3. **Terrain stamps.** Hand-sculpted features (canyons, arches, sinkholes, craters, cave mouths) are stamped into procedural voxel terrain, so landscapes have landmarks, not just noise. Biomes come from temperature and humidity; some archetypes have generated cave networks. Players can dig, but only within strict limits (section 10).
4. **Every system has something.** Each system rolls at least one notable feature: an unusual body, an anomaly or a set piece. Systems are full ones, with at least 8 worlds counting planets, gas giants and moons. Gas giants have many moons, and a starting world can be one of those moons, with the giant filling its sky.
5. **Quiet worlds on purpose.** About 10% of planets are deliberately empty and beautiful, so finds elsewhere feel earned.
6. **Start-system guarantee.** The start system always has a habitable world, an ice neighbour within reach for water and oxygen, an asteroid field and a trading station that stocks fold crystal. Everything needed up to the first capital ship can be gathered inside it (section 10).
7. **Orbits vary too.** Orbit types include empty space, asteroid fields, ice rings, debris fields, stations and nebula fog (about 1 orbit in 5), plus comets and broken moons.

**Continents, biomes and rivers**

- Planets form continents and oceans first, then biomes from temperature, humidity and altitude. Biomes meet and blend along their borders, so one continent can run from desert into forest into tundra.
- Rivers run from high ground to the sea, with riparian biomes along their banks: reeds, wetlands, denser life and easy water.
- Single-biome worlds still vary their terrain with mesas, canyons, dunes, craters, plateaus, cliffs, sinkholes and ridges, so an ice world is never one flat sheet.

**Themed planets and exotic locales**

A few planets carry a theme that overrides the usual rules. They're rare enough to feel special and hold the exotic materials (section 10).

| Theme | What it looks like | Known for |
| --- | --- | --- |
| Glass desert | Plains fused to glass by some ancient weapon, cracked and gleaming | Voidglass |
| Storm world | Endless lightning storms over charged rock spires | Stormcore |
| Cryo-fire world | Methane lakes and ice that burns blue at the vents | Rimefire |
| Shattered world | A planet broken into floating continents and drifting debris | Low-gravity island hopping, rare salvage |
| Bone fields | Giant fossils of something that once lived here | Dig sites, unique finds |
| Fungal deep | Glowing fungus forests in collapsed cave systems | Rare organics, Bloom sightings |
| Petrified forest | Stone trees a kilometre tall over a dead floor | Hidden ruins among the trunks |
| Ruin world | Endless city ruins under ash | Precursor vaults, unique materials |

**Weather**

| Weather | Where | What it does |
| --- | --- | --- |
| Clouds | Every world with air | Layered skies and shade; cloud cover is visible from orbit |
| Rain and monsoons | Temperate, swamp and ocean worlds | Cut visibility and solar output; monsoons bring days of downpour |
| Fog | Swamp, ocean and cold worlds | Hides the horizon; things appear close before they're seen |
| Snow and blizzards | Ice worlds | Faster cold drain, low visibility, slower movement |
| Dust storms | Arid worlds | Visibility near zero, strong wind, solar output cut |
| Lightning storms | Storm and temperate worlds | Strikes high points and exposed ships after a visible build-up |
| Fire storms | Lava and scorched worlds | Burning debris rains down; heat drain |
| Hurricanes | Ocean and temperate worlds | Sustained winds that push players and vehicles |
| Tornadoes | Temperate, arid and storm worlds | A moving vortex that lifts players and vehicles into the sky and flings them |

**Precursor Arcs (very rare).** A Precursor Arc is a vast artificial band left by the Precursors, spun for gravity: players land on its inner surface, gravity pulls outward from the band's centre, and the sky shows the band arching overhead into the distance. Arcs need their own generator path (a band that wraps along its length) and hold unique Precursor technology and exotic materials. How the rims work, how rare they are and what guards them are still to settle.

**Fallen worlds.** A post-apocalyptic world theme: nuclear blast craters with glassy floors and warned radiation, ruined cityscapes, bunker networks, wreck-strewn highways, scrap forts and artifacts from before the fall. It comes in two strengths. Nuclear waste worlds were hit everywhere: glassed plains and crater fields, dead grey forests, ash skies and radioactive dust storms, cities flattened outward from ground zero, radiation zones that need protection (always warned), and wildlife changed by it. Other worlds only show evidence of a blast or a few: a single crater with a ring of flattened ruins around it, shadows burnt onto surviving walls, a dead zone fading back into normal land with distance, and a radiation warning at its edge. **Roving gangs** belong to no faction and are always hostile. They drive scrap vehicles (buggies, trucks, gun bikes), patrol the roads between POIs, chase the player, and hold camps and forts with loot but no core.

**Mutagenic worlds.** Something got into these worlds' life (fallout, an escaped biolab agent, or the Hollow Bloom's touch) and twisted it. Plants grow swollen, tumorous and wrongly coloured, forests fuse into strange masses, and the wildlife carries growths, asymmetry and extra parts while still reading as real, dangerous animals. The mutagen is a warned environmental hazard, like radiation, that the suit protects against; it never gives the player ailments. Biolabs, quarantine zones and Bloom sites are the POIs that explain it, without ever saying it outright.

**Swamp worlds.** A world type of their own, temperate or alien: mostly low, flat and waterlogged, with shallow pools the player wades through, deeper channels, mud, dead and drowned trees, and mist in the mornings. Solid ground is scarce, so bases go on the few rises or stand on stilts. Crashed ships sink nose-first into the bog and ruins stand half-drowned.

**Overgrown city worlds.** Some worlds were built over almost entirely, and flying across them feels like passing over an overgrown urban wasteland: ruined city to the horizon, reclaimed by nature. Streets are canyons choked with trees and rusted traffic, towers stand with forests on their roofs and their sides torn open, overpasses and skyways have collapsed across the blocks, and rivers run through flooded districts. Most of it is flavour; the POIs are the places inside it worth going into.

**World identity.** Every world has a character, not just a terrain type. One seeded identity ties together whether it was ever inhabited (most were; some never were), how advanced its old settlers were (not every place was high tech: frontier colonies of dirt roads, timber and diesel trains, up to high-tech worlds of maglevs and skyways), which factions hold ground there (many worlds have none), its terrain, its dominant POIs and their states, the traffic in its sky, its weather mood and a signature landmark, so each world is remembered for something: a sunken swamp world with a Reaver capital ship patrolling overhead, a glassed fallen world crossed by gang convoys, an ice moon where one observatory light still burns. Most worlds follow their type; a few break it. A world can be one strong type or **mixed**: several regions of different types on one planet, arid, temperate, ice and volcanic side by side (ice caps at the poles, a desert belt, swamp lowlands, a jungle equator, a volcanic region of lava fields, an alien crystal region where something landed), each region with its own POI themes, wildlife and weather, blending at the borders. Regions sit where they make sense, never as a random patchwork: ice where it's cold (poles, high peaks), desert where it's dry (rain shadows, subtropical belts), swamp in wet lowlands and river deltas, and fire along the hot equator: lava fields and active volcanoes there and on rifts and hotspots elsewhere. Active volcanoes are landmarks you see from far off; they smoke, and they erupt with warning like every other hazard, sending out lava flows and ash fall.

**Landforms.** A few worlds are one landform throughout (a planet of dune seas, an endless ice plain), but most combine two or more. Their land is divided into regions, each with its own landform, placed where it makes sense and blended at the borders: mountain ranges and alpine peaks along old uplifts, rolling hills and foothills below them, wide flat plains and open steppe in the interiors, sprawling deserts and dune seas in dry basins, mesas, plateaus and canyon country, badlands, river valleys opening into deltas and marshes at the coast, lake districts, waterfalls where rivers meet escarpments and cliffs, cliffs and beaches, fjords on cold coasts, and island chains. Flat and open ground is as common as rugged ground, so a world has room to build, drive and fly low, and the dramatic places stand out because not everything is dramatic. The landform also shapes the wildlife: each region's creatures are adapted to its ground, such as herds and their hunters on plains and steppe, climbers and cliff-nesters in the mountains, burrowers and sand-swimmers in dune seas, and waders and ambushers in deltas and marshes, so crossing into new country means meeting new creatures.

**Tech level comes first.** World generation starts with a world profile, taking ideas from the Traveller tabletop RPG's world profiles (ideas only, with GalHack's own names): whether the world was ever inhabited, how many people lived there, the size of its old starport, its main trade (agricultural, industrial, mining, research and so on), and two tech levels, in rough bands rather than fine steps.

- **The pre-collapse tech level** is how advanced the place was before the fall. It decides the old world: which POI families, ruins, infrastructure, vehicles and decorations appear.
- **The post-collapse tech level** is how advanced the people living there now are: survivors, settlements and any faction holding ground. It decides their settlements, gear, vehicles and defences. The two are independent: survivors often live several levels below the ruins around them, while a high-tech place can stand on ruins from a lower level, such as a faction's base built over a town from our own modern-day level.

The bands are low (pre-industrial and industrial: timber, stone, carts and animals, rail, diesel, mines and mills; frontier or regressed colonies), mid (modern: highways, airliners, computers, nuclear power) and high (the game's own level, the tech players build: hyperspace, teleporting, cloning, grav vehicles, fusion, spaceports, maglevs, skyways, arcologies, space elevators). Beyond those is the incomprehensible: dimension-hopping ships and impossible things, glimpsed only rarely and never understood. It's not a band, so no world's ruins or people are built from it, and it's seen more often nearer the core. Neighbouring worlds can differ widely, because colonies developed unevenly; the old starport's class sets what its spaceport ruins are, on tiers inspired by Traveller's starport classes (our own wording): class A, a great port with terminals, yards that built starships, refined fuel and orbital stations; class B, a large port with yards for smaller craft and refined fuel; class C, a working port with basic repairs and raw fuel; class D, a poor one with little beyond a fuel depot and a few pads; class E, a frontier field that's just a marked landing area and a beacon; and none at all on some worlds. A world has one primary starport of its class; it may have more, even many, but always one main one. On exodus worlds, where it looks like as many people as possible tried to leave at the end, one major port is surrounded by many scrappy improvised ones: makeshift pads and launch fields, half-loaded ships, evacuation camps, and roads jammed with abandoned vehicles all leading toward them. Many of these worlds are nearly empty now, with that evidence of a once-huge population and little else: scattered survivors, outposts and frontier bases at most, or a scrappy town whose small starport is simply a flattened clearing where trading vessels set down.

**Infrastructure.** Worlds that were once inhabited are crossed by what's left of their infrastructure: highways, roads and tracks, railways (lines, stations, sidings, depots, viaducts and rail tunnels), bridges, tunnels and underground roads, pipelines, power lines and landing strips, in varying states of decay (cracked and buckled, buried by sand or snow, overgrown, flooded, bridges with fallen spans). Infrastructure isn't a POI, but it serves them. It's laid out logically between the old places (highways between cities and spaceports, roads branching off to towns, mines and bunkers, tracks out to remote outposts), it follows the land the way engineers would build it (gentle grades, switchbacks, bridges over rivers and ravines, tunnels through ridges), it matches the world's tech level (dirt tracks, timber bridges and diesel railways on frontier worlds; highways, maglevs and skyways on advanced ones), and it leads players toward POIs. Faction bases have their own infrastructure, kept in good repair (patrolled roads, landing pads, defence lines, supply routes), often old roads patched up and put back into service, so a route's condition tells you who uses it: maintained near faction bases, crumbling where nobody has driven since the collapse, and roving gangs drive the old roads. Below ground, old towns and cities keep their own networks, laid out under the streets above: sewers and storm drains, drainage pipes running out to rivers and the sea, utility and service tunnels, and metro and subway lines with their stations, all walkable, dark and often occupied. The old utilities are places too, matched to the world's old tech level: blown power plants (coal with its stacks and coal yards, nuclear with cooling towers and a breached reactor hall, fusion with its torus halls), dams and hydro stations, substations and switching yards, water treatment, sewage works and pumping stations, desalination plants, refineries and gas works, steelworks and cement works, quarries, landfills and incinerators, grain elevators, docks and ports, rail yards, telecom exchanges and data centres, radio masts, and wind and solar farms. Other old places that tell the collapse's story or stand out on the skyline come first among the rest: vast half-flooded cisterns, missile silos, evacuation camps abandoned mid-step, walled quarantine zones, stadiums used as camps, collapsed highway interchanges, open-pit mines, offshore drilling platforms and arcologies the size of towns. Power lines run only where they'd make sense (from power plants to towns, along main roads and railways, into sites that needed power), never strung everywhere. Wires and cables are always attached: power lines are strung between poles and pylons, sagging between two fixed points, and a line that's broken or attached at one end only hangs down from whatever still holds it, never floating in the air. Along with it come lots of abandoned houses, farmsteads, sheds, wayside stops and small ruins that aren't POIs at all, and abandoned vehicles (rusted cars, trucks and buses, often jammed nose to tail on highways where an evacuation stalled, rusted pile-ups on the highways, crashed flyers, airliners crashed in fields or abandoned on runways, trains standing derelict or derailed on their tracks), salvageable for scrap: flavour scattered along the roads and across the countryside. They're also an early source of raw and primary materials: the survival tool salvages scrap metal, wiring, glass, plastics and rubble from ruins and wrecked vehicles, a quicker start than mining alone, though each ruin runs out and mining soon yields more. Worlds that were never inhabited have none of this, and it's a big part of what makes a world feel abandoned and ruined.

**Region planning.** Before any POI is placed, the generator plans each region and what it was for: a town and the industry it lived on, farmland, wilderness. POIs, roads, rail, power and ruins are then placed to serve that plan, so a rail line ends at a depot, a plant feeds its town's power lines, and a sewage works serves somewhere, with empty land between places. The aim is a world that feels hand placed, with reasons behind what's where.

**World themes.** Every world rolls a strong theme from a large list (mass exodus, virus outbreak, nuclear war, raider wasteland, deadly flora and many more), which guides its region plan, its POIs and layouts, and what goes in them. Who occupies a place is a separate layer: the same hospital can be a bug nest, full of zombies, a scavenger den, or silent and empty, with the theme setting the odds. A share of POIs break from the theme, so nothing is certain. Memorable worlds come from combinations, not bespoke entries: a swamp world, plus a crash-site feature, plus a pirate presence overhead gives a swamp of crashed ships under a patrolling pirate capital ship, without anyone writing that world by hand.

**Decorations.** POIs are dressed with decorations that match their theme: tables and chairs, vending machines, desks and monitors, filing cabinets, beds and gurneys, weapon racks, bars and loungers, departure boards, ride cars, alien altars and much more, each set fitting its room (a canteen, a ward, an armoury, a resort bar, a terminal concourse). They're arranged as they were used (chairs round tables, desks facing screens, beds in rows), then left as the collapse left them: knocked over, broken, burnt, scattered or overgrown, by the POI's state. The collapse was a century or two ago, and time shows: forests have grown up through the ruins, timber buildings have fallen in unless sheltered while concrete, stone and steel stand as shells, vehicles are rusted hulks, and a few old machines still run on long-life power, eerily. Within that, things that rot fast (hay, food, crops, anything left out in the weather) are long gone or reduced to remnants, while sturdier things survive faded, dusty and worn (furniture, fabrics and paper kept indoors, treated wood). Metal left outside has rusted and plastics have turned brittle. Only sealed rooms, frozen worlds and airless places preserve things, and that's where the frozen moments are sharpest. As in Empyrion, decorations are deco blocks in the shared block catalogue, so players unlock and build with them too. Tables, toilets, bunks and the like are all built under one universal deco block rather than as separate build items: the player picks what it is from a gallery before placing it. Survival fabricators don't make it. The broken and knocked-over versions appear in POIs and in Creative.

**POI themes.** POIs are a call to adventure: from a distance they make you want to go there, and up close they pull you deeper. They are usually themed by planet and place (archetype, biome, territory), with the occasional unusual outlier that doesn't belong there. The set includes both hand-authored landmarks and procedurally generated places, with lots of verticality above and below ground. Examples: old airfields, abandoned spaceports with derelict ships on their pads, abandoned research bases with sublevels, ominous multi-floor towers, city ruins, and deep basements that get stranger and more dangerous the deeper you go. Only places a faction owns have a core.

- **Wind is a simple force** (direction and strength) applied to players and vehicles: light craft drift, heavy ones barely notice.
- **Tornadoes are moving vortex volumes.** They wander at random and eventually wear out, and never seek the player: weather is an act of god, not an antagonist. Wind and tornadoes can't touch anything in shelter, and trees and wildlife are as vulnerable to them as players and vehicles are, so a tornado leaves a trail of destruction behind it. Inside, a lift force pulls players and vehicles up and around, then lets them go. Getting caught is dramatic, never fatal: a fling always leaves the player alive, if badly hurt.
- **Forecasts:** weather shows on the visor before it arrives, and severe weather comes with a warning.
- Weather changes temperature, visibility, solar output and creature behaviour. There's no fluid or cloud simulation: only visual effects and simple forces. Rain, lightning and tornadoes always come from visible clouds above them. Skies mix cloud types: happy puffy cumulus, streaky cirrus, flat grey stratus layers, and big angry dark storm towers.

**Sky and sense of place**

- The sky shows the real system: neighbouring planets and moons hang overhead at their true positions and phases, gas giants fill horizons, rings cast shadows, and the star sets on schedule.
- From space, planets show their real surfaces: continents, oceans, ice caps, deserts, cloud cover and Bloom stains, drawn from the same generator that builds the ground.
- Every archetype has its own sky colour, light and soundscape (section 5).

**Wildlife**

Every planet generates its own wildlife, so the variety never runs out. Generated creatures should come out as cool, menacing monsters and believable animals, never goofy or cute.

| Body plan | How it varies |
| --- | --- |
| Walkers | Two to eight legs, long necks, armoured backs, stilt legs |
| Crawlers and slitherers | Segmented bodies, many legs, burrowing |
| Hoppers | Spring legs, inflating throat sacs |
| Fliers and floaters | Leathery wings; gas-bag jellies drifting on the wind |
| Swarms | Insect clouds, skittering packs |
| Giants | Slow and enormous, sometimes harmless, sometimes not |
| Oddities | Walking plants, living boulders, crystal-backed grazers |

- Each creature combines a body plan with swappable parts (heads, jaws, horns, shells, tails, extra limbs, tendrils), proportions, skin pattern, colours and glow, plus a behaviour: grazer, herd, pack hunter, ambusher, scavenger, swarm, territorial giant or harmless oddity. Combination rules keep the results looking like real, dangerous animals: strong silhouettes, believable anatomy, and natural palettes with warning colours only where they mean danger.
- Wildlife stays where it belongs: land animals never submerge and float at most, aquatic animals spawn and stay in water, and amphibious animals hang around riverbanks and shores, moving between the two. POI corridors and rooms suit the building's purpose: a house can be snug, with halls about 4 m wide, but plants, factories and other big facilities have main corridors about 8 m wide and 6 m high, some places about 10 m by 7 m, and rooms to match, with narrow passages as the exception. A POI's creatures fit its spaces: small things in narrow passages, large things only where rooms and corridors give them room, and bodies never clip through the player.
- **Flocks:** flying creatures move in flocks: murmurations wheeling over water and cliffs, long migrating lines crossing the sky, birds settling to roost at dusk, the whole flock scattering when startled or when an aerial hunter dives into it. They make the sky feel alive.
- **Schools:** lakes, rivers and seas have their own life: schools that swirl and part around a swimmer, bigger hunters cutting through them, and the occasional large shape passing below. Schools flash and scatter when something strikes, so the water is worth watching, and wading into it is a little uneasy.
- **Day and night:** each species has an activity cycle: diurnal, nocturnal, or active at dawn and dusk. Outside its hours it rests in dens, burrows, roosts or nests, so the same valley feels different after dark, with different hunters out.
- **Underground:** subterranean creatures live in caves, tunnels and mines. Many crawl on walls and ceilings, drop from above, and slip into cracks the player can't follow.
- **POI creatures:** where it makes sense (not every POI: some are empty and silent, which is ominous in its own way), POIs have their own creatures, with a look and behaviour that suit the place: lurkers in dark rooms, prowlers that roam corridors, ambushers waiting in vents, on ceilings and behind doors, and nests in the deepest levels that get worse with depth. Not everything there hunts the player: some creatures just subsist in the place, grazing on moss and fungus or picking at scraps, and the POI's predators hunt them, so each POI is a small ecosystem you can watch before you're noticed. Derelict ships, crashed on the ground or drifting in space, have their own nesters: creatures that build nests in holds, ducts and engine rooms and defend them. Infested POIs are overrun. Every ambush is telegraphed (a sound, a shadow, scratching inside the walls), so it's frightening but fair.
- Weirdness rises with how alien the world is. Temperate worlds get near-familiar animals; alien, swamp and themed worlds get bizarre ones; Bloom-infested worlds get corrupted versions of the local fauna.
- A creature's first attack is always a telegraphed lunge or charge that can be dodged (section 15).
- Creatures drop meat (organics) and sometimes rare parts that traders pay well for.
- **Discovery card:** the first time a player meets a species, a brief card shows its name, form, behaviour, size and planet without blocking play. The codex records it and who found it first, and scanning a creature (hold Q) shows full info at any time.
- The variety comes from parts and settings on a small set of reliable rigs, so animation stays smooth (guiding rule 1).

**No spawning near bases.** No creature spawns or respawns within a radius of any base (a structure whose core is in base mode), measured from the structure's edge so bigger bases get bigger zones. Creatures already outside may still wander in, and invasions still reach bases. The radius is a data value.

**Space creatures (rare).** Space has its own rare fauna, built the same way: drifting grazers in nebulae, hull-clinging leeches on derelicts, creatures in gas giants' upper atmospheres and, very rarely, a huge void leviathan. They reach the player as encounter-director spawns and signal leads, and drop rare, exotic and sometimes unique materials.

## 5. Points of interest, ships and visual style

Points of interest (POIs) are hand-built structures placed by procedural rules. Variety comes from three multipliers: a large library of families built from shared faction kits, several states per layout, and a rarity ladder from common sites to one-of-a-kind set pieces. Rarity matches the resource ladder, so a rare POI is where rare materials come from.

**Tone of places**

Structures are ominous and mysterious. Most places hint at a dark history the player pieces together without being told: scorched barricades, abandoned meals, sealed doors with something behind them, logs that stop mid-sentence, whole cities in ruins under ash. The game never explains it all.

**Rewards everywhere**

- Every dungeon, base, ruin and POI holds a reward worth the trip: a loot room or cache scaled to its rarity and guarded to match.
- Any base, NPC-held or abandoned, can be taken: hijack its core, or destroy the core and place your own (section 16).

**POI rarity ladder**

| Rarity | How often | Examples | What it's for |
| --- | --- | --- | --- |
| Common | Several per planet | Abandoned mines, crashed shuttles, supply caches, Vharn outposts, Freeholder farms, small wreck fields | Feeds the core loop; quick to clear |
| Uncommon | One or two per planet or orbit | Faction outposts, research stations, excavation digs, trading posts, Kessari villages, pirate hideouts in asteroids | Standing, trade, mid-tier loot |
| Rare | About one per few systems | Dungeons, fortresses, heavy capital ships, ghost ships, repairable derelict stations | Boss rooms, unique-material chances, capture targets |
| Unique | A handful per galaxy, placed by the seed | Expedition set pieces (below) | Long-range goals that a whole server can share |

**POI catalogue**

| Category | GalHack examples | Where | What players do there |
| --- | --- | --- | --- |
| Wrecks and derelicts | Crashed shuttles, broken freighters, silent hulks with no power, ghost ships with ultra-rare crates, derelict stations that can be repaired and claimed | Surface, orbit, deep space | Salvage, loot, claim |
| Abandoned sites | Mines, factories, farms, failed colonies, mines that turn into dungeons deeper down | Most planets | Early loot, shelter, salvage |
| Faction sites | Dominion garrisons, fortresses, relay towers and mining facilities; Meridian stations and refineries; Ledger trading stations; Carrion Reaver havens hollowed out of ice asteroids; Kessari machine villages and relay spires; Freeholder settlements | By territory | Trade, missions, raids, capture |
| Dungeons | Underground labs, buried temples, a pyramid whose defenses wake when entered, infested freighters, corrupted outposts | Hostile and alien worlds; orbits | Multi-room fights, traps, puzzle doors, a boss room |
| Ruins and digs | Excavations over bombarded cities, half-buried capital ships, graveyards, precursor shrines | Dead, graveyard and alien worlds | Unique materials, codex entries |
| Space features | Resource asteroid fields, ice rings, comets with debris tails, broken moons, nebula orbits, mining convoys with tug ships, distress beacons | Orbits and deep space | Mining, salvage, signal leads |
| Anomalies and spectacles | Crystal forests, floating rock fields, megastructure fragments, giant fossils, pulsar storms | Rare systems and archetypes | Discovery XP, survey data, screenshots |

**Expeditions** (unique, multi-stage set pieces with goals visible from far away)

| Expedition | Where | The draw | Gate |
| --- | --- | --- | --- |
| The Quiet Reach | A dead region of burnt-out stars | Precursor beacons that point to each other, then to a vault | Long hyperdrive range; radiation gear |
| The Hive Expanse | A Dominion-held cluster | A Dominion war-hive fortress whose core can be destroyed or hijacked | Capital-tier fleet |
| The Sealed Gate | The galaxy's edge | A one-way ancient gate to a sealed cluster with its own archetypes | Advanced drive and a full fold crystal tank; no way back except the long route |
| The Heart | The black hole at the core | A graveyard of ancient derelicts in the accretion disc | Shielding against tidal and radiation damage |

**How POIs are authored and placed**

- **Faction kits:** each faction has a shared block palette and module set, so designers build families of POIs quickly and every family reads as that faction at a glance.
- **Families, not one-offs:** each POI is a family of 3–6 layouts under one name; the generator picks one.
- **States:** one layout can spawn active, abandoned, infested by the Hollow Bloom, or ruined, with different loot, enemies and damage. One build becomes four sites.
- **Dressing pass:** loot tables, damage, enemy rosters and logic puzzles vary per spawn.
- **Placement rules:** POIs spawn by biome and by faction territory, with near and avoid rules (Vharn outposts near rich deposits; Freeholder farms away from Dominion garrisons) and counts scaled to planet size.
- **Faction POIs are mostly working sites, not forts.** A faction holds a variety of places: mines and refineries, research stations, comms relays and listening posts, supply depots, farms, shipyards and spaceports, and defensible sites that matter, such as SAM sites and air-defence batteries, or massive railguns aimed at the sky, and plain civilian places: a mall or market, apartment blocks, offices, a town square, with citizens going about their day and guards keeping order. Civilian places aren't hostile by default; shooting there turns the guards and the faction against you, and citizens flee rather than fight. Fortifications (garrisons, fortresses) exist but are uncommon. Every faction-held place has defenders and a core.
- **Factionless settlements:** survivors live in villages and towns of their own, built in and from the ruins, with no faction behind them. Some are peaceful and let players walk in and trade; some are wary, keep their guns ready and want you gone; some are angry and hostile. Each settlement's mood is its own, and shooting in a peaceful one turns it against you. Townsfolk behaviour is deliberately simple, just enough to give the impression of people going about their day: some rush between places, some man stalls, some wander about, others work at a bench or a garden, sit, or chat in pairs, and they head indoors at night. Outside town they can be preyed on by wildlife. Wild creatures avoid towns, and any that wander in are quickly killed, but towns keep their own animals: the odd stock animal, working animal or pet. Working animals belong to low-tech places, and stock animals to low-tech places and to farms of any tech level; elsewhere high-tech places have machines for that work, and at most a pet. They can appear on any once-inhabited world, start worlds included, alongside gang camps.
- **No repeats nearby:** the same layout can't appear twice in a system. Layouts the player hasn't seen yet are weighted higher, so the 20th planet still shows new places.
- **Map reveal:** each POI sets how far away it shows on the map. Some are landmarks visible from orbit; others are hidden until the player stumbles on them.
- **Regrowth:** common POIs regenerate on a timer when no player is near; rare and unique ones don't.

**Lessons from community-built content in the genre**

- What players praise: content tied to star class and region, so the map hints at what's out there; zones that get more dangerous and richer moving in toward or out from the star; multi-stage expeditions with goals visible from far off; salvage-first graveyard worlds; scanning and survey side loops; leaving the start system for the first time.
- What players complain about: meeting "the same dozen" POIs everywhere; many planet types that look alike; empty unclaimed space; a punishing early game with too few hints. The variety rules (section 4) and the rules above answer each of these.

**Visual style**

Ships and structures follow 1970s sci-fi paperback art, inspired by Chris Foss: huge, chunky, asymmetric hulls with heavy industrial detail and oversized engines, painted in bold livery (stripes, chevrons, checkerboards, hull numbers) in saturated reds, yellows, oranges and blues, and dwarfing the landscapes around them. Human and machine designs stay closest to that look; the more alien the builders, the weirder and wilder their ships and structures get. The style is evoked, never copied: no reproductions of specific paintings.

**Ships and structures by faction** (from most familiar to most alien)

| Faction | How alien | Shapes | Colour and markings | Signature ships and structures |
| --- | --- | --- | --- | --- |
| Freeholders | Human | Patched, ramshackle industrial; bolted-on modules | Faded livery, hand-painted numbers | Converted haulers, prefab farms and mines |
| Meridian Combine | Human | Clean, massive blocks; long spines and huge engine banks | Corporate stripes and chevrons in bright primaries | Frigates, refinery stations, colony arcologies |
| Ledger Exchange | Human | Bulbous freighters stacked with cargo pods | Colour-coded cargo, merchant banners | Container freighters, trading stations |
| Carrion Reavers | Human | Scrap-welded, mismatched plates, ram spikes, trophies | Rust, warning stripes, kill marks | Raider gunships, hollowed-asteroid havens |
| Kessari Clans | Machine | Modular repeating lattices, exposed machinery, perfect symmetry | Bare metal, glowing status lights | Walking work-machines, crawler rigs, relay spires, machine villages |
| Vharn Dominion | Alien | Segmented, insect-like, sweeping asymmetric curves | Dark carapace, glowing markings | Crewed war-hulls, hive fortresses, heavy capital ships |
| Precursor ruins | Ancient alien | Vast monoliths, rings and shells; cold geometric perfection | Stone-like surfaces, faint inner light | Megastructure fragments, vaults, shrines |
| Hollow Bloom | Utterly alien | Grown, not built: tendrils, pulsing growths, impossible geometry; corrupted hulls overgrown | Sickly colours, light that looks wrong | Infested derelicts, nest-ships, corrupted stations |

- **Rosters:** each faction fields a set of ship roles (fighter, gunship, hauler, miner, frigate, heavy capital ship). Each role comes in several hull variants built from the faction kit, and each hull can spawn pristine, damaged, derelict or infested.
- **Silhouette first:** every faction is recognisable by silhouette at long range, before colour or detail reads.
- **Players can build the look too:** large engine and greeble blocks, plus a cheap paint sprayer for livery, decals and detailing (section 11).

**Sound and music**

- **Space ambience:** slow, spacious synth and orchestral pads in the spirit of 1970s and 1980s science fiction, sparse enough to leave room for the world.
- **Haunting where it fits:** dead worlds, ruins, Bloom-infested sites, the deep void and precursor vaults get eerie, unsettling scores.
- **Every archetype sounds different:** wind, wildlife, distant thunder, creaking ice, the hum of crystal fields.
- **Music** doesn't react: it follows the game's themes, with atmospheric tracks by world type, and it doesn't change for discoveries, danger or combat.
- **Sirens, weather and ship sounds** are part of the soundscape, not just alerts.

## 6. Discovery mechanics and pacing

Discovery needs three things: tools that reach further as the player climbs, a record of what's been found, and pacing that keeps the next surprise close. The target is something new within about 10 minutes of travel on any world, and a new kind of thing at every stage of the ladder.

**Scanning ladder**

| Tool | Range (reference) | Reveals |
| --- | --- | --- |
| Eyes and HUD | Line of sight | Landmarks, creatures, nearby deposits (auto-revealed within about 500 m) |
| Handheld detector | About 1 km | Deposits, buried caches, hidden POIs |
| Vehicle detector | 2.5–10 km | Deposits and POIs across a region |
| Orbital scan | Whole planet, from orbit | Biome map, deposit types, POI count by rarity (not their locations) |
| Survey probe | One system, fired from the galaxy map | Bodies, archetypes, danger grade and any anomaly before the player jumps |
| Galaxy map | The galaxy | Star class, region, faction territory, danger grade |
| System scan | A whole star system, from system space | Anomalies, wrecks and signals as bearings with rough distances, ready to warp toward |

**Knowing what's been found**

- The maps start dark. Visited places, revealed POIs and the player's bookmarks fill them in.
- An in-game **codex** unlocks an entry the first time the player finds a planet archetype, creature, POI family, faction or anomaly, with its hazards and loot hints. Meeting a new alien species or creature also shows a brief on-screen discovery card.
- **First discovery:** on a server, the first player to reach a system or unique set piece is recorded on the map.

**Rewards for finding things**

- First-visit XP for each new system, planet, POI family, creature and anomaly, scaled by rarity.
- **Survey data:** scanning anomalies and new worlds produces data that the Ledger Exchange and Meridian Combine buy. Exploring alone is a viable career.
- Codex completion milestones unlock cosmetic rewards (paints, decals, suit colours).
- Unique materials and the best gear exist only out in the world (section 7).

**Signal leads**

Procedural breadcrumbs point players at content: distress beacons, buried-cache signals found by detector, faction transmissions, and precursor beacons that point to each other. A lead names a direction and distance, not an exact spot, so following one is still exploration.

**Pacing by stage**

| Stage | New things to discover | Main discovery tool |
| --- | --- | --- |
| 1. Wake | The local biome, the cryo bunker, first deposits, first creatures | Eyes, auto-reveal |
| 2. Foothold | Nearby abandoned sites, caches, the first Vharn outpost | Handheld detector |
| 3. Wheels | The whole planet: faction outposts, villages, dungeons | Vehicle detector |
| 4. Wings | Orbits, moons, asteroid fields, wrecks, stations, comets | Orbital scan |
| 5. Capital | New stars, new archetypes, anomalies, trade lines | Galaxy map, survey probes |
| 6. Frontier | Expeditions, unique set pieces, precursor ruins | Probes, signal leads |

**Pacing rules**

- **Ten-minute rule:** by hover vessel, a player on any non-quiet world reaches a notable POI or feature within about 10 minutes; on foot from a start, within about 3.
- **Landmarks pull:** big things (megastructures, broken moons, gas giants overhead, smoke from a crash) are visible from far away and draw players toward them.
- **Danger is shown before travel:** every planet and system shows a danger grade (easy, medium, hard, extreme) on the map, so risk is a choice, not an ambush.
- **Rare finds are guaranteed, not lucky:** each system rolls at least one notable feature, and each region holds rare POIs at a set density, so a player who keeps moving always finds something.
- **Unique set pieces are fixed by the seed,** so everyone on a server can talk about, race to and share the same legends.

## 7. Progression structure

Progression runs on three tracks. **Character level** buys recipes through the tech tree. **Structure class** opens new spaces. **Exploration finds** supply the parts and materials that no recipe can make. A player can't climb the ladder on any one track alone. Early stages are short: a player who knows what they're doing has a small spacefaring vessel within one to two hours.

&#91;embedded content: escalation ladder · 6 stages, crash to frontier\]

| Stage | Player goal | Key builds and unlocks | Main threats |
| --- | --- | --- | --- |
| 1. Wake | Stay alive | Survival tool (shoot and gather modes), suit constructor, survival replicator, O2 bottles, medkits | Exposure, hunger, wildlife |
| 2. Foothold | A safe, powered shelter | Base core, generator, fuel tank, small constructor, galley replicator | Night predators, first Vharn scouts |
| 3. Wheels | Cover the whole planet | Hover vessel, hand drill, detector, portable constructor | Vharn patrols, guarded POIs |
| 4. Wings | Reach orbit, moons and asteroids | Small vessel, landing gear, large constructor, CPU tier 2 | Space pirates, hostile fighters |
| 5. Capital | A mobile base that jumps between stars | Capital vessel, hyperdrive, fold crystal tank, shields, repair bay | Faction warships, higher-danger planets |
| 6. Frontier | Master the deep galaxy | CPU T3/T4, combat steel, advanced constructor, Hijack Tool T3, charged armor | Heavy capital ships, alien ruins, Hollow Bloom horrors |

**Character level**

- **XP sources:** exploring, discovering (first visits to planets, systems and POI types pay a bonus), looting, crafting, building, fighting, mining and harvesting. Automated turret kills give no XP, so the player must be present to grow.
- **Level cap:** 25 (reference). Unlock points keep accruing past the cap, so veterans can eventually open the whole tree, but a player at the cap has had to specialise.
- **Tech tree:** tabs for Base and Capital Vessel (one tab, since they share every block and component), Small and Hover Vessel (one tab, likewise, with hover plates a prerequisite for thrusters), Tools, Weapons and Misc. Each node has an unlock level, a point cost and a parent node. Everything is unlocked this way, guns included: skill points come from levelling up, and nodes sit behind their parent nodes and level bands. A player can unlock the whole tree by max level (this can be tuned down later if needed). Player levels run to about 50, with something to unlock at most steps. Unique alien equipment sits outside the tree: discovering and consuming a cyber module unlocks it for building, for the whole crew.
- **Reference gates:** hover vessel at level 1, portable constructor at 3, small vessel at 5, capital vessel at 7. Higher-tier devices sit deeper in each tab.

**Exploration-gated items** (no recipe; found or bought only)

- CPU T3/T4 components (lattice cores, lattice arrays), precursor alloy, echo shards and epic-tier gear.
- Unique weapons and boosters from faction rewards and rare containers.
- This keeps pillar 1 driving progression: the best gear comes from where the player has been, not from grinding one deposit.

**Design rules**

- Each stage should open a new *space*, not just a bigger number. If an unlock doesn't change where the player can go or what they can do there, it belongs inside a stage, not as a stage.
- Captured structures must not let a player skip stages (section 16).

## 8. Player character and survival

The player is a fragile body in a hostile place. Six vitals and planet-specific hazards create the pressure, and each new planet type tests a different vital. Survival pressure is strongest in the first hours and shifts from the body to the ship as the player climbs.

**Vitals** (reference values)

| Vital | Max | Drains from | Restored by | At zero or out of range |
| --- | --- | --- | --- | --- |
| Health | 500 | Damage, falls, starvation, exposure, radiation | Medkits in the field; a healing booth restores everything | Death |
| Food | 500 | Always; 1.5× running, 3× sprinting, faster while mining | Replicator feeding, replicator food, foraged fruit | Health damage over time; healing stops working |
| Stamina | 100 | Running, sprinting, melee, heat | Rest, fastest when standing still | Can't run; no damage |
| Suit oxygen | 500 | Breathing with the helmet on in thin or no atmosphere | O2 bottles (small +75, large +250); refills automatically in any breathable room | Suffocation damage |
| Body temperature | Comfort band | Climate, day and night, weather, water | Armor insulation, heaters, coolers, drinks, showers | In the survival suit, the air is comfortable from about -10 °C to 60 °C, and better suits widen that. Beyond it there's a grace period before any harm, shorter the more extreme the temperature, then health drains faster the further it is from comfort, until back in range. Direct contact is different: an ice-cold lake, standing beside lava, and worst of all being in lava, which is like walking on burning hot slag, drain health fast. So does being caught exposed in a blizzard or fire storm. A hole dug deep enough counts as shelter, and a placeable climate control node, built early and running on fuel, holds the temperature steady in a small radius around it. The HUD's temperature gauge shows the temperature of the area the player is in; body temperature is on the You tab |
| Radiation | 0 is safe | Irradiated worlds, stars, precursor alloy | Radiation booster, immunity shot | Health drains above 5 RAD |

**Environmental hazards** vary per planet: breathable oxygen percentage, temperature swing between day and night, background radiation, gravity (some worlds exceed 4 g and trap heavy ships), weather such as acid rain and storms, and night predators. Choosing where to land is a survival decision.

**No ailments.** GalHack has no injury or illness system: no fractures, wounds, burns, poison or parasites, and the Hollow Bloom can't infect players. Harm is health loss from damage or exposure (cold, heat, radiation, no oxygen), and it stops as soon as the cause stops. A healing booth takes care of every health need in one place, and medkits patch players up in the field; armor and boosters reduce damage and exposure.

**Food and replicators**

There's no farming. Replicators turn **organics** (plant matter, creature meat and other biomass the player harvests and keeps in storage) plus energy into food. Every replicator can feed the player straight to full on demand; its tier sets the nourishment and shelf life of the food items it makes for carrying.

| Tier | Device | Where | Food it makes | Nourishment | Shelf life |
| --- | --- | --- | --- | --- | --- |
| T1 | Survival replicator | A portable survival device, available from the start, that the player can build and set down anywhere except inside NPC faction territory | Ration paste | Low | Short; degrades |
| T2 | Galley replicator | Small vessels, bases and capital vessels | Meals | Medium | Long |
| T3 | Advanced replicator | Bases and capital vessels | Long-haul rations | High | Never spoils |

- Feeding to full costs energy and organics in proportion to how hungry the player is. Higher tiers get more nourishment from each unit of organics.
- The survival replicator runs on its own fuel and draws organics from the player's inventory; installed replicators use the structure's power and shared storage.
- Food items lose nourishment as they age, so players carry paste for short trips and higher-tier rations for long voyages.
- The cryo bunker holds a few ration packs, and foraged fruit can be eaten raw, to cover the minutes before the first survival replicator is built, and any time the player is inside faction territory where it can't be deployed.

**Death and respawn**

- On death the player drops a backpack at the corpse. It never despawns: it stays, marked on the map, until the player recovers it. Anything a player drops follows the same rule. What drops is a difficulty setting: everything, inventory only, or nothing. The survival tool is never dropped.
- Respawn options: the home base (a clone chamber or healing booth flagged as home), the nearest clone chamber or healing booth, the nearest survival tent, or a **fresh start** in a new cryo bunker on a start planet that keeps level, unlocks, structures and faction standing.
- The player respawns at full health; there is no death spiral. Worn armor loses durability on death.
- At night single players can sleep in a tent to skip ahead; servers can't skip night.

## 9. Personal equipment

The player's kit is small and role-based: a few tools that change the world, weapons with distinct jobs rather than numbered tiers, and armor whose booster slots let the player tailor the suit to each planet's hazards.

**Tools**

| Tool | What it does | Runs on | Notes |
| --- | --- | --- | --- |
| Survival tool | Two modes. Shoot: the only starting weapon, a rechargeable energy shot strong enough for early wildlife, fired as a red bolt. Gathering and deconstructing use a blue beam. A tree gives materials with each hit and falls over once it's depleted, leaving a stump, and the fallen trunk fades away. Gather mode slowly chips at everything it's pointed at, including building blocks found out in the world, and nothing is deleted instantly. Digging and salvage are gradual: sustained drilling wears a block or cell down instead of one hit removing it, with resources trickling in as it goes and the block slowly shrinking into its neighbours as it depletes, and advanced mining tools unlocked by level are faster. Taking down your own building blocks stays instant. Gather: mines ore, digs terrain, cuts plants and salvages wrecks (about 33% return) | Nothing | Every player starts with it; never dropped on death |
| Hand drill T1/T2 | Faster mining; digs terrain (remove only) | Biofuel (T1), drill charges (T2) | T2 has longer reach and collects ore automatically |
| Chainsaw | Fells trees for wood | Biofuel | Early building material |
| Multi-tool T1/T2 | Removes, repairs, upgrades and rotates blocks; salvages wrecks and pulls intact components out whole from structures whose core is destroyed | Small energy cells (T1), multi-charges (T2) | Salvage return about 33% (T1), about 65% (T2); vessel-mounted salvagers up to 80% |
| Detector | Shows ore deposits, POIs and buried caches | Battery | Handheld range about 1,250 m; vehicle detector about 10 km |
| Hijack Tool T1–T3 | Takes over enemy cores | Charges | Section 16 |
| Survival kit items | Survival tent, survival replicator, portable heater and cooler, emergency O2 and water generator, work light | Fuel or battery | Early-game lifelines |
| Hoverbike | Fast personal ground transport, a small vehicle made in the suit fabricator, so getting around is quick from early on | Fuel | Treats water as ground |
| Paint sprayer | Sprays colour, texture, patterns, stencils, decals and grime onto any block face | Cheap paint cartridges | Craftable from the start; the way to detail ships however players like |

**Weapons** (no tier numbers; each weapon has a job; at the start the survival tool's shoot mode is the only weapon)

| Weapon | Job | Ammo |
| --- | --- | --- |
| Projectile pistol | First crafted sidearm | Pistol rounds |
| Laser pistol | Efficient sidearm | Energy cells |
| SMG and laser SMG | Close-quarters spray, the boarding weapon | Rounds or cells |
| Auto shotgun | Close burst, clears corridors | Shells |
| Laser carbine and laser rifle | Mid-to-long range | Energy cells |
| Sniper rifle | Long range, picks off turrets and guards | Heavy rounds |
| Rocket launcher | Anti-armor; damages blocks and vehicles | Rockets |
| Flamethrower | Area denial, strong against wildlife | Fuel |
| Grenade launcher | Indirect fire, door breaching | Grenades |
| Melee (energy blade, combat knife) | Silent takedowns; costs stamina | None |
| Faction reward weapons | Unique effects, such as a crossbow that harms only living targets | Special; can't be crafted |

**Armor and boosters**

| Suit | Booster slots | Notes |
| --- | --- | --- |
| Interior wear | 0 | No protection; for ship and base interiors |
| Light | 2 | Mobile; the minimum for helmet, suit O2 and jetpack |
| Medium | 3 | Balanced |
| Heavy | 4 | Most protection; slower |
| Epic heavy | 0 | Top stats with fixed bonuses; found only, never crafted; can't be repaired at a station |

- **Boosters:** armor (damage reduction), jetpack (thrust), oxygen tank (capacity), insulation (temperature), radiation shielding, mobility (speed and stamina), EVA (vacuum work) and multi (several small bonuses, less hunger). Some boosters only fit some suits.
- **Loadout per planet:** an ice world calls for insulation, a dead moon for oxygen and EVA, an irradiated world for radiation shielding. Booster choice is how planet variety reaches the player's body.
- **Jetpack:** toggled on and off; short bursts with fast recharge; the player can't sprint while it's active.
- Armor has durability and is repaired at an armor locker or repair station.

## 10. Resources, mining and crafting

Resources climb a five-step rarity ladder (common, uncommon, rare, exotic, unique), and each step lives further out in more dangerous places. Names follow the ladder: plain words and real metals for common goods, crystal and star-metal names for rare ones, and names that hint at lost technology for unique finds that can't be mined at all. Whatever the player needs to advance is always obtainable: everything required to build a small vessel exists on every planet.

**Rarity ladder**

| Rarity | Material | Where it's found | Main uses |
| --- | --- | --- | --- |
| Common | Iron | Every planet | Steel, basic devices |
| Common | Copper | Every planet; richest on starter worlds and asteroids | Wiring, energy cells |
| Common | Silicon | Every planet; richest in deserts | Electronics, windows |
| Common | Stone and ice | Any terrain; ice worlds, moons, poles | Concrete; water and oxygen |
| Common | Wood, fibre, biomass | Vegetated worlds | Early building, replicator organics, biofuel |
| Common | Burnstone | Every planet; rich in asteroid fields | Fuel packs |
| Uncommon | Cobalt | Every planet in small deposits; rich on moons and asteroids | Alloys, constructors |
| Uncommon | Magnesium | Every planet in small deposits; rich on arid worlds and asteroids | Ammo, explosives |
| Uncommon | Neodymium | Every planet in small deposits; rich on moons and asteroids | Magnets for thrusters and RCS |
| Uncommon | Gold | Scattered; richer on volcanic worlds | Advanced electronics, trade value |
| Uncommon | Gravium | High-gravity worlds | Hardened and combat steel |
| Rare | Fold crystal | Irradiated ice worlds; asteroids near dead and irradiated stars | Hyperdrive fuel, shields |
| Rare | Emberite | Lava worlds around young and dying stars | Fusion cells, plasma weapons, high-output generators |
| Rare | Starsilver | Barren metal worlds and dense asteroids near giant stars | Top-tier devices, hyperdrive T2, advanced constructor |
| Exotic | Voidglass | Glass-desert worlds | Long-range scanners, sensor arrays, holographic displays |
| Exotic | Stormcore | Storm worlds | Capacitors, high-output shields |
| Exotic | Rimefire | Cryo-fire worlds | Hyperdrive range boosters |
| Unique | Precursor alloy | Alien ruins and ancient derelicts (salvage only) | Prize armor blocks; can't be crafted |
| Unique | Echo shards | Alien ruins | Charged combat armor, epic gear upgrades |
| Unique | Lattice cores and lattice arrays | Derelicts, faction strongholds, rare traders | CPU extenders T3 and T4 |

Rarity has one colour each in every inventory, map and loot pop-up: common grey, uncommon green, rare blue, exotic purple, unique gold.

**Where resources sit**

- Placement is driven by star and planet type: rare materials cluster around young, dying, giant and irradiated stars, so the galaxy map itself hints where to look.
- Surface deposits sit within about 10 m of the ground. Deposits within about 500 m reveal themselves; a detector shows them out to about 2.5 km.
- Rich deposits are often guarded by Vharn outposts or sit in faction territory, where mining costs standing.
- Asteroid fields, forests and plants regrow on a timer when no player is near. When a planet's deposits fall below about 10%, resource meteorites start to fall, a visible event players can race to.
- Server options set ore richness (rich, normal, poor) and deposit count (plenty, normal, few).

Deposit shapes (veins, layered beds, pockets and deep clusters) take ideas from the Vintage Story mod Interesting Ore Gen, as reference only and after checking its licence; no code or data is copied.

**Availability guarantees**

The generator checks every seed against these rules. If a guarantee fails, it adds the missing deposit or asteroid field instead of rerolling the galaxy.

| Progress stage | Materials it needs | Guarantee |
| --- | --- | --- |
| Wake to Wings (everything up to and including small vessels) | Iron, copper, silicon, stone, burnstone, cobalt, magnesium, neodymium | Every planet has deposits of all of them. Uncommon ores are smaller and fewer there, but never absent |
| Capital (first capital vessel and first hyperdrive jump) | The above, plus gravium, gold and fold crystal | All of them can be gathered inside the start system by small vessel, and the start system's trading station stocks fold crystal |
| Frontier | Emberite, starsilver | At least one system rich in each lies within one T1 jump (about 30 LY) of every start system |
| Unique items | Precursor alloy, echo shards, lattice cores and arrays | The rare POIs that hold them regenerate, and rare traders sell them at a premium, so a looted site never blocks anyone's progress on a shared server |
| Exotic upgrades | Voidglass, stormcore, rimefire | Optional upgrades only, never needed to advance; each lives on its themed planet type (section 4) |
| Getting home, from any system | Fold crystal and the small-vessel kit | Every system holds a fold crystal source (a deposit or a trader that stocks it), so no stranding is permanent (section 14) |

- Every material on the path to the next stage has at least two sources: mining plus trade or salvage.
- Depleted planets recover through resource meteorites, and asteroid fields regrow (above), so no world runs dry for good.

**Ways to gather**

| Method | Tool | Notes |
| --- | --- | --- |
| Hand mining | Survival tool, hand drill | Slow; also digs terrain |
| Vehicle mining | Drills on hover vessels (surface) and small vessels (asteroids) | The main mid-game income |
| Auto-miners T1–T3 | Placed on a deposit; need auto-miner cores | About 100, 200 and 400 ore per hour (reference); passive income that's worth defending |
| Salvage | Multi-tool, vessel salvagers | 33–80% return on blocks from wrecks and derelicts; intact components come out whole |
| Looting | Containers in points of interest | The only source of unique materials |
| Harvesting | Hands, chainsaw | Organics, fibre, wood, biofuel |
| Trading | Traders (section 18) | Converts surplus into what a world lacks |
| Vehicle harvesting | Harvester blades on the front of a hover vessel | Mow through alien forests and run down wildlife; wood, plant matter and meat pour straight into the vessel's storage |

**Digging terrain (kept simple on purpose)**

Players can dig into the ground to reach ore, but voxel terrain is the most bug-prone system in this kind of game. GalHack keeps digging small and predictable so it stays stable.

1. **Remove only.** Tools and drills only remove terrain, in fixed grid-aligned chunks (reference: 1 m). There's no fill, flatten or sculpt, so terrain never grows into players, vehicles or structures.
2. **Shallow.** Ground can be dug about 30 m down (reference); below that is undiggable bedrock. Deposits sit within about 10 m of the surface, so nothing worth having is deeper.
3. **Dig anywhere.** Terrain under and around structures and POIs can be dug like any other ground, so players can tunnel under an enemy base or into a POI to breach it, as in Empyrion. Bases and POIs are static: undermining one never makes it fall or disappear; it stays where it was built. Vessels, creatures and loose objects rest on whatever ground is left. Faction bases and POIs resist tunnelling with what they're built from (armoured floors and foundations) and with their defenders, not with protected ground. Only bedrock can't be dug.
4. **Building clears terrain.** Placing a block removes any terrain inside it, so bases sit on uneven ground without flattening.
5. **No terrain physics.** No cave-ins, falling sand or collapsing overhangs; leftover floating bits of terrain stay put.
6. **Water finds its level.** Lakes, ponds, rivers and seas are bodies with one even, flat surface level, not a full fluid simulation, but water always settles at the lowest point it can reach. Digging a path below a body's surface lets water spill into the new space over a few seconds: a pond drops by the volume it lost, while rivers and seas are effectively endless and fill what's dug to their level. Water never hangs in the air or stands above the ground around it.
7. **Stuck-safe.** Any player, creature or vehicle found inside terrain is moved to the nearest open space.
8. **Edits stored as differences.** The server keeps terrain changes as small per-chunk edit lists on top of the seed and sends those to clients, so every player sees the same holes and saves stay small.
9. **Caves come from the generator.** Cave networks and underground POIs are generated or hand-built, never carved by players.

Vehicle drills follow the same rules, and auto-miners extract ore without changing the terrain at all.

**Crafting**

- There's no separate smelter: constructors turn ore into ingots and parts as part of each recipe.
- **Constructor ladder:** survival constructor (carried, no power, basics) → portable → small → large → advanced. Each tier unlocks more recipes; vessel-mounted constructors carry a limited set.
- **Queues:** the player's orders sit in the top queue; missing sub-components are added to a second queue automatically.
- Constructors take their inputs from, and deliver their outputs to, the structure's shared storage (section 18).
- **Other stations:** replicators (food, section 8), deconstructor (turns items back into parts), blueprint factory (builds whole structures, section 11).
- Some items have no recipe at all, such as unique materials and faction reward weapons. Those must be found, bought or captured.

Building costs are based on Empyrion's, whose costs are well balanced: what a block or component takes, relative to the others, follows Empyrion, using GalHack's own items. Manufacturing in full is being prototyped: the whole item catalogue as data (ores, refined materials, parts, components), a recipe tree from ore to every block and component tied to the rarity ladder, constructor tiers, the deconstructor and blueprint factory, and survival building with blocks crafted as items and placed from the toolbar.

**Auto-restock ammo.** Any structure with a constructor and guns has an auto-restock toggle with a threshold per ammo type. When stock falls below the threshold, the onboard constructor queues that ammo from shared storage until stock is back up, behind any jobs the player queued themselves. If materials run short it pauses and says what it needs.

## 11. Building system

Every structure the player owns is built block by block on a grid. Blocks carry hit points and mass, components are the functional parts, and a structure's function comes from which blocks are attached to its core: devices work whichever way they face and wherever they sit, except thrusters, which push along their facing. A vessel's heading is set by the seat of whoever holds the pilot role: forward is whichever way that seat faces. Roles can be reassigned to any player in a command chair, so handing the pilot role to someone in a seat facing another way turns the ship's forward with it. There is no separate ship editor: the player builds in the world, in either game mode. In Survival, as in Empyrion, blocks are craftable items: constructors make them from materials, players carry them in stacks on the toolbar and in their bag, each placement uses one (from the player's own stack, or, when building on a structure, from that structure's shared storage: still one storage per core, kept separate from the player's own storage, and usable only while the player is within the structure's area radius, for bases, small vessels and capital ships alike; the player selects which source they're taking from, as in Empyrion), a crafted block of a family can be placed in any of that family's shapes, and taking a block down with the tool returns it to the inventory. A new base or vessel starts with its crafted core block placed from the inventory; everything else is built onto that structure, and only blocks compatible with its class can be placed (base and capital-vessel blocks, small-vessel blocks, and so on, as in Empyrion); building happens under threat; in Creative placing is free and unlimited, with every block available.

**Grids**

| Grid | Block edge | Used by | Notes |
| --- | --- | --- | --- |
| Large | 2 m | Bases, capital vessels | Big devices, turrets, constructors, blueprint factory |
| Small | 0.5 m | Hover vessels, small vessels | Fine detail; 1 large block = 64 small blocks of volume |

Reference cap: about 250 blocks per axis per structure, for performance.

**Placing and shaping**

- Each structure starts from a **starter block** that contains its **core**. The starter block fixes the structure's class and owner and provides CPU tier 1 and artificial gravity. Gravity works whenever the core has power and costs no energy; it's on by default and can be toggled in the control panel. There is no separate gravity generator. The core can't be removed, only destroyed or hijacked (section 16).
- Blocks snap to the grid with a ghost preview, mirror symmetry on up to three axes, and rotation on every axis.
- A **shape picker** turns any armor block into slopes, corners, wedges, rounds, cylinders, connectors and thin plates (100+ shapes). Shapes cost the same as cubes but have fewer hit points.
- A **paint sprayer** is a cheap handheld tool, craftable from the start, that sprays colour, texture, patterns, stencils and grime onto any face, so players can detail ships however they like. It has a copy-colour picker, plus livery decals (stripes, chevrons, checkerboards, hull numbers) for the bold cover-art look. Greeble and oversized engine blocks help builders hit the same style.
- The **multi-tool** removes blocks (returning most materials), repairs them, and upgrades armor in place (steel to hardened to combat) without losing shape or paint.

**Block stats**

Ships and structures are built from blocks and components: blocks form the hull and frame, and components (engines, generators, turrets, shields, command chairs and the other devices in section 12) do the work. Every block and component carries its own structural stats. A structure's toughness comes from how it's built: where the heavy plates sit, what shields the core, where the devices hide.

| Stat | What it does |
| --- | --- |
| Hit points | Damage the block or component absorbs before it breaks |
| Armor | Flat reduction per hit, so light guns barely scratch heavy plating |
| Resistances | Percentage reduction against kinetic, explosive and energy damage |
| Mass | Adds to the structure's mass, and so to how it handles (section 13) |
| CPU cost | Counts against the structure's CPU budget (section 13) |

- Shapes keep their material's armor and resistances; hit points and mass scale with the shape's volume.
- Each material leans toward a damage type: hardened steel shrugs off kinetic fire, combat steel resists explosives, charged armor resists energy weapons.
- Components have the same stats plus their function, and stop working when destroyed, so tucking a generator behind armor is a real defensive choice.
- There's no structural load or stress simulation, by design: it adds bug risk and serves no pillar. Only sections cut off from the core break away (below).

**Armor materials** (reference values, large grid)

| Material | Hit points | Mass | How it's obtained | Role |
| --- | --- | --- | --- | --- |
| Wood, plastic, concrete | Low (concrete higher) | Varies | Early recipes | Cheap shelters and bunkers |
| Steel | 500 | Light | Starting recipe | Default hull |
| Hardened steel | 1,000 | 2 t | Mid-tier unlock | Workhorse armor |
| Combat steel | 2,000 | 4 t | High-tier unlock; costs more CPU | Warship armor |
| Charged combat armor | 2,000 plus a shield bonus | 4 t | Upgrade using echo shards from alien ruins | Endgame armor that strengthens shields |
| Precursor alloy | 2,750 | Heavy | Salvage only, from alien structures; can't be crafted | Prize armor; mildly radioactive |
| Windows | Light and heavy variants | Light | Early to mid unlocks | Visibility; weak points |

Small-grid blocks have about a tenth of the hit points of their large-grid equivalent per cube. Precursor alloy and charged armor tie top-tier building to discovery: the best armor comes from exploring alien sites, not from a recipe.

**Damage model**

- Every block and device has its own hit points. Destroyed blocks are gone; destroyed devices stop working.
- Weapons deal kinetic, explosive or energy damage, with extra multipliers against shields or devices, so weapon choice matters against each target.
- No stress simulation: blocks don't need support. A section cut off from the core breaks away. **GalHack design choice:** severed sections become a separate unowned wreck that can be salvaged or claimed, rather than vanishing with their contents. This removes a common frustration and adds wrecks to discover.
- Interior rooms are **airtight** when fully enclosed, and the core keeps them breathable (section 12); a visor overlay shows any leaks.

**Templates and blueprints**

- **Repair template:** the player saves a structure's current layout as its template; repair devices restore it to that state (section 12).
- **A nice-to-have, not a core feature:** building by hand, block by block, is the game. Blueprints are reached from the menu (saving a structure, the library, the blueprint factory's panel) and are never forward-facing: no blueprint tool on the toolbar, and nothing in the early game steers the player toward assembling one.
- **Blueprints:** any owned structure can be saved as a blueprint. A blueprint factory (on bases and capital vessels) builds it for a resource cost paid in refined materials, not ore. Blueprints containing devices the player hasn't unlocked can't be produced.
- Blueprints can be shared through an in-game library on the same server and between players. This is content sharing, not modding.

Toggles and triggers (switches, sensors, device groups) are covered in section 18.

## 12. Structure classes and components

Four structure classes divide the world by where each can go, and two of them (bases and capital vessels) are really one structure with two core modes. Each one opens a new space to explore, so the class ladder is pillar 4 (scale) and pillar 1 (discovery) at once. Player structures never carry NPC crew: players fly and man them, and turret AI and autopilot cover any station nobody is manning.

| Class | Grid | Where it operates | What it's for | Hard limits | Unlock (reference level) |
| --- | --- | --- | --- | --- | --- |
| Base | Large, core in base mode | Fixed, on a planet or in space | Production hub, storage, defence, repair, respawn | Can't move until switched to capital mode | Start |
| Capital vessel | Large, core in capital mode | Space; can land on planets | Mobile base, warship, interstellar explorer | Needs thrusters, a command chair, and enough thrust to lift its mass | 7 |
| Hover vessel (HV) | Small | Planet surface only, hovering a few metres up | Rovers, mining crawlers, ground combat, cargo haulers | No flight or space; moves in 2D, so needs forward, reverse and strafe thrust | 1 |
| Small vessel (SV) | Small | Atmosphere and space within one star system | Fighters, shuttles, miners, scouts | Warp drive within a system; no hyperdrive, so no jumps between systems | 5 |

**Bases and capital vessels are one thing**

Bases and capital vessels share every block and component, and both are built on a Big Core (small-grid vessels use a Small Core); only the core's mode differs, and a core console switches it. Every material, block and recipe works the same in both modes, so nothing ever needs rebuilding when a core changes mode. A capital vessel is a floating base: it burns fuel to maintain lift and wind can buffet it. A base anchors to the ground instead.

- **Ship to base:** allowed at any time. The structure anchors where it comes to rest: on the ground, or holding still in space (it becomes a station). Switched in the air, it falls until it lands, then anchors and stops simulating physics.
- **Base to ship:** allowed at any time, but it only flies with thrusters, a command chair, and enough thrust to lift its mass. Those blocks can sit on a base beforehand and do nothing until the core is switched.
- **Clean liftoff:** terrain the hull is embedded in at liftoff crumbles away in a burst of rock and dust as the structure rises, so a half-buried building pulls free and leaves its crater behind. Rock overhead is cut too: the cut is planned once along the hull's straight climb and runs during a few seconds of engine spool-up, with the ground shaking and crumbling as the hull strains to break free, before it moves (reference limits until that's built and tested: 100 m of climb, 20,000 cells, never bedrock). Past those limits lift-off is refused, the console says why, and the player digs the hull out by hand. Only blocks connected to the core lift off.
- One CPU budget covers both modes, though flying costs more, because active thrusters and drives use CPU and fuel (section 13).

**Small and hover vessels are one thing**

Small vessels and hover vessels work the same way: one Small Core with Vessel Mode and Hover Mode, and every small block works in both. Hover plates give lift in Hover Mode and do nothing in Vessel Mode. In Hover Mode thrusters only push the craft along, and hover works only close to the ground, so a craft that goes over a cliff sinks until the ground is back in range, and it can't reach orbit.

**Switching modes.** Either core's mode can be changed from the menu at any time, even in flight, and nothing refuses it. It's never a keyboard command: the switch is only in the menu, with warning stripes around it. Switch a flying ship to a mode it isn't fitted for and it loses lift and falls. That's the player's own doing, and they can switch back. The console warns about missing parts. While seated, a visual indicator always shows the craft's current mode.

**Ground builds and flight builds.** One tech tree covers both, with room to specialise, and no block is limited to a mode: the differences come from power, recoil and mass.

- **Power.** Thrusters use up a craft's spare power in flight. Heavy weapons such as railguns charge from the generator's surplus, so they work best on a craft that isn't spending it on flight.
- **Recoil.** Heavy guns shove a flyer around. A hover craft braced against the ground is a stable platform for them.
- **Mass.** Heavy armour makes a flyer cumbersome. Hover plates carry it with no slowdown, up to their lift limit.
- **Roles.** A hover craft suits ground assault, combining heavy armour, heavy weapons, strafing and speed. A flyer can still carry those, just badly, and the player can choose that.
- **Big Core, same logic.** A base has no thrusters drawing power and no flight to disturb its aim, so the heaviest guns and armour work best there. A capital ship can land and switch to Base Mode to bring heavy guns to full power.
- **Relocating.** A heavy base or hover tank isn't stuck: with enough thrusters, CPU and power it can switch mode and move, slowly.

**The capital-to-base loop is the heart of building.** Landing a capital ship on a planet, switching it to base mode, running it as a base and lifting off clean is the loop players should do most. When it lifts off, terrain it was built into breaks free so it never snags, and vessels parked on it ride along.

**Wrecks can be restored**

Any wreck with no core (shot down, crashed, severed in combat or found as a POI) can be claimed by placing a core in it. Repair it, then add whatever is missing to fly it in capital mode, or keep it as a base. Small-grid wrecks work the same way with a small-grid core.

**Docking**

- Smaller or equal craft dock onto larger ones: HV onto SV or CV, SV onto CV, and like onto like. The docker becomes a passenger.
- Docking happens by landing gear contact (about 1 s) or a docking pad. A craft that already carries another can't be docked onto.
- Docked craft add their mass to the carrier, don't fire or shield, and aren't covered by the carrier's shield.
- A capital vessel carrying a hover rover and a small shuttle is the signature explorer loadout: land the shuttle, deploy the rover, scout the surface.

**Seats and stations**

Unlike Empyrion, there's no cockpit component: every seat aboard a ship is a command chair, and players build whatever enclosure they like around it. The player in a chair can reach all of the ship's systems. Only one player flies at a time, and the rest of the work is split into station roles that anyone can rearrange in seconds from the ship's systems console by dragging a role onto a player.

| Role | What the holder controls | When nobody holds it |
| --- | --- | --- |
| Pilot (one at a time) | Flight, fixed weapons, docking, landing | Autopilot holds course or follows the active route (section 14) |
| Navigation | Route planner, warp and short-range warp targets, bookmarks | The active route continues |
| Gunnery | Turrets, by turret group: manual aim or target priorities | Turret AI fires on hostiles |
| Engineering | Shields, power priorities, core gravity, repairs | Automatic: shields up in combat, gravity on |
| Scanning | Detector, orbital scans, survey probes | Passive detector only |

- One player can hold several roles, and gunnery can be split by turret group so several players can man turrets at once.
- A role's controls work from any seat. If its holder stands up, the AI covers the role until they sit down again; the assignment stays.
- Every role works solo too: a lone player flies while the AI handles the rest.

**Seats can kill.** A seat is a component with its own hit points, and if it's destroyed, whoever is sitting in it dies. Seats warn first: sparks, an alarm and a visor warning at half and at a quarter of the seat's hit points give the occupant time to get out. Armoring around the command chair is a real survival choice.

**Component catalogue**

| Category | Devices | Available on |
| --- | --- | --- |
| Control | Core, command chairs, remote-control console | All |
| Power | Generators sized per class, fuel tanks T1–T3, solar panels and capacitors | All; solar on bases and capital vessels |
| Life support | Healing booth, respawn (clone) chamber; air and gravity are built into the core | Mostly bases and capital vessels |
| Food | Replicators T1–T3 | Portable (T1); small vessels, bases and capital vessels |
| Propulsion | Thrusters (small, medium, large, jet), RCS gyros, hover engines, landing gear, docking pads | Per class |
| Travel | Warp drive and warp cell tank; hyperdrive T1/T2 and fold crystal tank | Warp drive on small and capital vessels; hyperdrive on capital vessels only |
| Production | Constructors (small, large, advanced), deconstructor, blueprint factory | Constructors on all; factory on bases and capital vessels |
| Storage | Cargo containers in several sizes, all adding to one shared storage | All, with volume caps per class |
| Defense | Shield generator and extenders, turrets, fixed weapons, detector, spotlights | Per class (section 15) |
| Repair | Repair bay T1/T2 with console, field repair station | Bases and capital vessels |
| Utility | Teleporter, auto-miner, switches and sensors, displays, doors, shutters, ramps, lifts | Per device |
| Harvesting | Mining drills; harvester blades (front-mounted mulchers that chip trees and plants and run down animals); salvagers | Hover vessels and small vessels |

**Air and life support**

Air is handled by the core, like gravity: there are no ventilators, oxygen tanks or refill stations to place, power or keep stocked.

- **Rooms are found automatically.** The game checks the block grid, and a room is sealed when every block on its boundary is airtight. Each block's tooltip says whether it's airtight; closed doors, hatches, ramps, glass doors and windows all are.
- **Only the boundary matters.** Ladders, lifts, furniture and components inside a room never break its seal, so a ladder up to a roof hatch is fine while the hatch is shut.
- **The core keeps sealed rooms breathable** whenever it has power, at no extra energy cost. If power fails, the air lasts a grace period (reference: 10 minutes) with a visor countdown before it thins.
- **Force fields hold the air at open doors.** A shimmering field spans any open bulkhead or outer hull door, so walking in and out never vents a room and it's plain to see why. Force field emitters can also span wide openings such as hangar bays: people, vessels and objects pass through, but the atmosphere stays in. Fields run on the structure's power: if the power goes out they fail, and any room relying on one decompresses at once, with the breach rules below.
- **Breaches:** a room opened to vacuum or toxic air vents over a few seconds with a visible rush and an alarm, then stays unbreathable until the gap is fixed. Air comes back within seconds of resealing; nothing needs refilling.
- **Finding leaks:** the visor's airtight overlay marks every leaking face in red, and the systems console lists each room as sealed, open or breached, with a marker to the problem.
- **Suits top themselves up** in any breathable room; oxygen bottles cover time outside.
- On breathable planets, open rooms are simply fine.

This keeps the good part of the genre's air systems (sealed rooms that matter in a breach) and drops the chores players complain about: hunting invisible leaks, tanks that silently run dry after a refit, ladder shafts that break seals, and power-hungry vents.

**Repair**

- **Repair bay T1** repairs damaged blocks on craft docked nearby; **T2** also replaces destroyed ones, from the craft's template. One per large-grid structure, in either core mode; it repairs docked craft of any class.
- **Field repair station** (bases and capital vessels): the player uses it in person to restore the structure. It won't run under attack, with shields up or with turrets firing, so repair is a between-fights activity.
- Repairs consume materials from the structure's shared storage.

## 13. Power, fuel, CPU and mass

Three budgets (power, CPU and mass) make every device a trade-off. Without them the best ship is simply "more of everything"; with them, a scout, a hauler and a gunship are different answers to the same limits.

**Power and fuel**

- Generators produce on demand, and fuel burn scales with the load. Idle devices cost nothing; a structure at full thrust with shields charging burns fuel fast.
- A structure needs a generator and a fuel tank with fuel in it. Tank capacity (reference): T1 1,200, T2 2,700, T3 6,000 fuel units.
- Solar panels and capacitors (bases and capital vessels) are used before fuel. Output depends on day and night, latitude and weather, which gives landing site choice a real cost.

| Fuel item | Fuel units (reference) | Source |
| --- | --- | --- |
| Biofuel | 10 | Plant matter in a constructor |
| Energy cell | 15 | Early recipe |
| Burnstone pack | 30 | Refined burnstone ore |
| Large burnstone pack | 150 | Mid-tier recipe |
| Fusion cell | 300 | High-tier recipe |

**Fold crystal** (a rare crystal that folds space for jumps and feeds shields) sits in one shared tank per structure:

- A hyperdrive jump between star systems costs about 1 refined fold crystal per light year. The warp drive, used between planets inside a system, burns a different fuel: warp cells, made in constructors from common materials, about 1 per AU, kept in their own tank.
- Shields burn fold crystal only while recharging (reference: about 1,000 shield HP per unit), never while full or off. Shields must be down to warp.
- Fold crystal deposits favour irradiated and dead stars, so fuelling long voyages pulls players toward dangerous systems.

**CPU budget**

Only blocks that do something cost CPU: devices such as thrusters, drives, shields, turrets and machines. Plain armour and structural blocks cost none, so size alone never uses up the budget. Faction and POI structures follow the same budget, so anything captured can actually be run. A structure's CPU ceiling rises in tiers, and each tier needs a set number of **CPU extenders** placed on the structure.

| Class | T1 (core) | T2 (1 extender) | T3 (2 extenders) | T4 (4 extenders) |
| --- | --- | --- | --- | --- |
| Small or hover vessel (small grid, either core mode) | 6,000 | 15,000 | 40,000 | 100,000 |
| Base or capital vessel (large grid, either core mode) | 200,000 | 500,000 | 1,500,000 | 10,000,000 |

- T3 and T4 extenders need **lattice core** and **lattice array** components, found only in points of interest or bought from traders. CPU growth is therefore gated by exploration, not just by the tech tree.
- Losing an extender in combat drops the structure a tier mid-fight.
- Going over budget is a soft limit: efficiency falls as the overshoot grows (reference: 100% over gives 50% efficiency), slowing thrust, RCS and constructor speed. Flight's cost comes from the blocks that do the flying, not from the mode: thrusters, warp drives and hyperdrives are what use CPU and fuel. Warp drives, hyperdrives and shields spike that usage: little at rest, then a sharp, brief peak while a drive engages or a shield recharges, and a peak that goes over budget dips efficiency as any overshoot does. They cost nothing while the core's mode doesn't use them, so thrusters waiting on a base are free until it switches to Capital Mode. Keep this modest, because powering a flyer should never feel exorbitant: a structure right at its limit in a ground mode still flies, just less efficiently, until it's trimmed or given an extender.

**Mass and volume**

- Every item has mass and volume. Volume is measured in storage units (SU): a small cargo block holds 125 SU, a large one 8,000 SU.
- A **structure's storage** is the total of all its containers, capped at 320,000 SU on the large grid and 32,000 SU on the small grid.
- Structure mass is blocks, devices, cargo and docked craft. Acceleration is thrust over mass; on a planet, vertical thrust must beat mass times local gravity, so a ship that flies on a moon may not lift off a heavy world.
- Heavy vessels reach a lower top speed first, then climb slowly toward the technical maximum.

**Remote storage and the remote toolbar**

Inside a core's area, a player can use everything in that core's storage without taking it out. Only cores they're allowed to use are available.

- **Two toolbars.** The personal toolbar holds what's on the player. The remote toolbar holds items left in core storage: blocks, food, guns or anything else. Dragging an item from a storage tab puts it there, and the toolbar switches to the remote one during the drag. A hotkey (Y) switches between the two, told apart by a label and a tint. An empty remote toolbar can't be opened (the key shows a short message), and it closes itself when its last item goes.
- **Slots point to the container.** A slot is a reference to an item in one core's storage. It shows the live count, falls to zero as anyone uses it, and refills if the storage does. The same item from two cores takes two slots, and nothing silently changes source.
- **Origin.** Each slot has a colour pip for its core, and the core's name shows on hover and while selected.
- **Out of range.** Outside the origin's area a slot greys out and can't be used until the player is back in range. A remote gun in the hands stays there, greyed out and unable to fire.
- **Ammo.** It comes from the player first, then the gun's origin, then the nearest container in range.
- **Death.** Remote items stay in their container and never go in the backpack.

**Moving items in the menu.** Storage tabs represent containers. Drag items onto a tab to send them there, or hover over it briefly to open it and place them. If a container fills, what fits moves, the rest stays, and a message says so. Selection works as in a productivity app and respects the text filter: Ctrl+click adds, Shift+click selects a range, Ctrl+A selects all. Double-click sends a stack across. Right-drag takes half a stack, Ctrl+right-drag takes one, and the mouse wheel changes the amount while holding it.

**Size class and block limits**

- Size class is an internal performance budget (devices, lights and polygons). GalHack shows it only as a server limit, not as a second design constraint.
- Per-category block limits (for example, maximum turrets per class) are server settings, on by default.

## 14. Movement, flight and travel

Movement scales with the ladder: feet and jetpack, then hover, then flight, then in-system warp, then hyperdrive jumps between stars. Each step multiplies the space the player can explore.

| Mode | Where | Speed (reference) | Limits |
| --- | --- | --- | --- |
| On foot | Anywhere | Walk, sprint (stamina), crouch (hold Ctrl: lower, quieter, fits under overhangs) | Fall damage; heavy gravity slows; swimming in water |
| Jetpack | Anywhere with light armor or better | Short bursts | Can't sprint while active; boosters extend it; EVA booster for vacuum |
| Hoverbike | Planet surface | Fast | Treats water as ground |
| Hover vessel | Planet surface, over land and water | Moderate | 2D movement; can't climb sheer cliffs or fly |
| Small vessel | Atmosphere and space in one system | 50 m/s level, 90 m/s vertical in atmosphere; 115 m/s in space | Thrust must beat mass × gravity to lift off |
| Capital vessel | Space; can land | About 100 m/s in space | Heavy; slow to turn; needs RCS |

**Flight model**

- Six degrees of freedom with assisted inertial dampening: release the controls and the ship stops while its thrusters are on. Hover assist holds altitude near the ground.
- Each axis needs its own thrusters; RCS gyros set turn rate, which falls as mass rises.
- A sizing rule of thumb: thrusters needed per axis = total mass ÷ thrust per thruster, scaled by local gravity. A ship that flies on a light moon may be grounded on a heavy world.

**Ship handling**

Ships handle like their build, but the rules are simple and the numbers readable. Every ship shows four numbers on its console and in build mode: acceleration per direction, turn rate, top speed and lift margin.

| Rule | How it works |
| --- | --- |
| Thrust | Each thruster pushes along its facing. Acceleration in a direction is that direction's total thrust divided by mass. Bigger thrusters give more thrust per tonne, so big engines are worth building, and they look the part |
| Turning | Gyros set the turn rate: total gyro torque divided by mass, with a floor so even the heaviest capital ship turns at a usable rate. Thruster placement never adds torque, so a lopsided build can't send a ship spinning |
| Top speed | Fixed per class and lower in atmosphere (reference: hover vessel 50 m/s; small vessel 70 m/s in air, 130 in space; capital vessel 60 in air, 100 in space). Mass changes acceleration and turning, never top speed |
| Lift margin | Upward thrust minus weight on the current planet. If it's negative the ship can't take off, and the console says so before anyone tries |
| Flight assist | On while thrusters are on, which is the default: let go of the controls and the ship slows to a smooth stop and holds its position and attitude, burning fuel the whole time, as far as its thrusters can support that attitude. Releasing a key fires the opposite thrusters to brake. A hotkey switches this holding off, shown by an on-screen indicator, and it stays off until switched back on. Thrusters then fire only when the pilot calls for them, including to adjust attitude, and nothing brakes or holds the ship: it falls on a planet, and in space it coasts on Newtonian physics, burning no fuel between inputs. Cruise holds a set speed; the autopilot holds course when the pilot stands up |
| Boost | A short burst of 1.5× thrust on a cooldown, for dodging and climbing; it never raises top speed |
| Bumps | Low-speed contacts slide and glance instead of stopping a ship dead; only real impacts deal damage (section 15) |

**Hover vessels**

- Hover engines hold a set ride height (reference: 1.5 m for small engines, 3 m for large) and read the ground ahead of the vessel as well as beneath it, so they rise over rocks early instead of snagging.
- They ride at full height over water and never sink.
- They level themselves within a second after a bump or a jump, and can't climb slopes steeper than a set angle.

**Landing gear** deploys automatically near the ground at low speed, and a key raises or lowers it by hand. A landed ship settles and stops simulating physics.

These rules keep what works in the genre (thrust against mass, big engines, heavy ships feeling heavy) and drop what players complain about: torque bugs that spin lopsided ships, top speeds that creep, ships stopped dead by a pebble, hovercraft that snag and sink, and landing gear with no manual control.

**No auto-levelling in flight.** Flying vessels hold whatever attitude the pilot leaves them in, in air and in space: no horizon assist and no self-levelling. Hover vessels still keep themselves level over the ground they ride on.

**Who the wind moves.** Wind pushes players, crates, wrecks, hover vessels, small vessels and capital ships. Bases are never moved by wind. A vessel parked on a base or on a capital ship, whether landed on it or docked, is sheltered from the wind and moves with its host.

**Water you can go into.** Water is a volume, not a hard surface. Players, creatures, crates, wrecks and vessels can all go below the surface; water slows movement, and the view tints and fogs underwater. Players swim and dive on their suit's oxygen.

**Trees and boulders are solid.** Every tree has a simple collision shape that blocks players, creatures, crates and vessels, and navigation routes around trunks. Cutting a tree removes it, and the cut stays cut through streaming and saves. Rocks and boulders above a size threshold are solid too; small pebbles stay decoration.

**Travel between places**

| Hop | How | Cost |
| --- | --- | --- |
| Surface to orbit | Fly straight up past the atmosphere ceiling; the game transitions to the orbit playfield | Fuel only |
| Within a playfield | Fly, or use **short-range warp**: reach 30 m/s, pick a bookmark, planet or radar contact, and jump | Free, on any warp drive |
| Planet to planet in a system | Small or capital vessel; in-system warp | About 1 warp cell per AU |
| Star to star | Capital vessel hyperdrive; T1 about 30 LY range, T2 60 LY | About 1 fold crystal per LY |
| Multi-jump routes | Route planner plots up to 500 LY and previews the fold crystal cost before committing | Sum of jumps |
| Between owned structures | Networked teleporters and portals in range | Power |
| Through a wormhole | Fly into its mouth (section 4) | Free; unstable ones can collapse behind you |

- Giant stars and the galaxy's outer edge need a better drive to reach, so distant, exotic systems are gated by gear, not by a wall.
- Shields must be down to warp, so jumping away from a fight costs time.
- **GalHack goal:** playfield transitions (surface to orbit, arrival after a jump) are masked by a flight or warp sequence, so the galaxy feels continuous even though it's built from separate zones.

**Walking aboard moving ships** (GalHack signature feature)

Players can leave the pilot seat and walk around inside any vessel while it flies, warps or fights. Each structure carries its own frame of reference, anchored to its core, so the ship is a place to live in, not just a seat to sit in.

| Situation | Gravity inside | How the player moves |
| --- | --- | --- |
| Core gravity on and core powered (the default) | 1 g toward the core's "down" axis, whatever the ship's attitude | Moves with the ship; walking, jumping and dropped items behave as if the ship were standing still |
| Core gravity off or core unpowered, in space | Zero g | Floats and steers with the jetpack, still carried along with the ship |
| Core gravity off or core unpowered, on or near a planet | The planet's gravity, toward the planet | Floors tilt with the ship |
| Stepping outside the hull | World gravity | Leaves the ship's frame, keeping the ship's velocity at that moment |

- Inside the hull, players, guards, loose items and docked craft move in the ship's frame. They inherit its velocity and rotation, and its acceleration doesn't throw them around.
- Hard hits, collisions and emergency stops cause a short jolt (camera shake and a stagger) but never fling players across the room.
- When the pilot stands up, the ship holds course: it keeps its velocity and heading, or follows the active route. A warp in progress completes.
- The ship's frame covers the sealed hull plus open hangar bays. Stepping out through a door's force field in space leaves the player drifting alongside; jumping out in atmosphere means a fall, so bring a jetpack.
- Boarding uses the same rules: once inside an enemy ship, the boarding party is in that ship's frame even while it's still flying.
- In multiplayer, positions inside a ship are sent relative to the ship, so everyone aboard moves smoothly even at full speed.

**Walking on the outer hull.** A player standing on a ship's outer hull belongs to that ship's cabin world just like one inside, so they can walk around the outside of a ship in flight or in space and step through an airlock without a seam. Jumping off, being knocked off or otherwise losing contact with the hull breaks the connection: the player drops back into the world with the ship's velocity plus their own, and in space they drift. Jumping inside the ship never breaks it. On the outer hull, down is whatever gravity the player is in: the ship's core gravity, or the planet's when core gravity is off, the same as inside. There are no mag boots, so the top of a hull can be walked on but its walls and underside can't. Low lips stay ordinary steps.

**Stranded, never stuck**

Getting stranded is part of the fun: GalHack is a high-tech Robinson Crusoe. Players can strand themselves with a crashed ship, an empty fold crystal tank or a one-way gate, but there's always a way back: find the materials, build what's needed, recover, and return to the adventure.

- **Build your way out:** every planet holds the materials for a small vessel and every system holds a fold crystal source (section 10), so a stranded crew can always rebuild, refuel and fly home.
- **Borrow a way out:** faction teleporters link their bases across systems. A friendly faction lets players use its teleporters; at a hostile base, players can sneak or fight their way in to use one.
- **Salvage a way out:** wrecks can be restored into working ships (section 12).
- **Last resort:** respawning at home always works, but the ship and gear stay where they were, waiting to be recovered.

## 15. Combat

Combat happens on foot, between vehicles, and between structures, and it should feel satisfying in all three. In every case damage goes through layers: shields first, then devices and hull. Combat is usually optional (pillar 5), but the richest discoveries are guarded, so a player who avoids all fighting sees less of the galaxy.

**Combat feel**

Fights should feel punchy and readable: tight, responsive shooting with heavy feedback, where every hit visibly does something.

- **Responsive shooting:** low input latency, crisp aim-down-sights, learnable recoil patterns rather than random spread, and hits that register the moment they land (predicted on the client, confirmed by the server).
- **Heavy feedback:** a distinct, punchy sound per weapon, muzzle flash, impact sparks and debris that match the surface hit, hit markers, a clear kill confirmation and a brief camera kick.
- **Enemies react:** guards flinch, stagger and take cover; damaged ships trail smoke and fire; creatures recoil.
- **Visible destruction:** blocks show scorching and cracks as they lose hit points, then break away in chunks; shields ripple where they're struck; hull breaches vent atmosphere; destroyed devices burst.
- **Every weapon feels different:** sound, kick, rate of fire and impact all match the weapon's job (section 9), so switching weapons changes the fight.
- **Ship weapons too:** turrets visibly track, heavy guns shake the command chair, and plasma bolts and rockets are easy to read in a busy fight.
- **Smooth under load:** debris and effects are capped and pooled so big fights keep a steady frame rate (guiding rule 1).

**Warned, never ambushed: inaccuracy and warning shots**

Hostile fire is deadly when it lands: a capital-class turret will vaporise a player on foot. What keeps that fair is that it never lands without warning, and that big guns struggle to hit small targets. Damage is never scaled down.

- **Warning shots:** when any hostile opens fire on a player, its first shots are deliberately wild. They're guaranteed to miss, but land close enough to see and hear, so the player always gets a moment to react.
- **Accuracy ramps up:** after the warning shots, accuracy climbs quickly (reference: over about 2 seconds) to the baseline for that shooter against that target. Breaking line of sight resets it.
- **Scale sets the baseline:** every weapon has an accuracy stat for its class and type, and the size gap between shooter and target decides how well it hits.

| Shooter's weapon class | Target on foot | Hover vessel | Small vessel | Capital ship or base |
| --- | --- | --- | --- | --- |
| Infantry small arms | Good | Fair | Poor | Very poor |
| Hover and small-vessel guns | Fair | Good | Good | Good |
| Capital and base turrets | Very poor | Poor | Fair | Very good |

- **Weapon type adjusts it:** miniguns spray (lower accuracy, high rate of fire), rail guns and lasers are precise, rockets track slowly, and artillery only hits big, slow targets well.
- **Slower projectiles:** shots travel at a visible speed, and capital-class guns are the slowest (reference: about 250–350 m/s, against about 600 m/s for small-craft guns), so a fast ship can jink and dodge heavy fire.
- **Creatures** follow the same idea: their first attack is a telegraphed lunge or charge that can be dodged.
- **Environmental dangers** (falls, lava, vacuum, a self-destruct countdown) follow the same spirit: always warned, never silent. The one hard exception is a destroyed seat, which kills whoever sits in it (section 12), and it warns before it goes.
- All accuracy, ramp and projectile-speed values live in data, so the deadliness can be tuned.

**On foot**

- Weapons have distinct jobs (section 9); armor and boosters reduce damage.
- Damage is plain health loss; there are no wounds or ailments to treat after a fight.
- Stealth is viable: melee takedowns are silent, and alarms (section 16) punish noise.

**Vehicle and structure weapons**

| Weapon | Mount | Classes | Range (reference) | Use |
| --- | --- | --- | --- | --- |
| Gatling and cannon | Fixed | Small and hover vessels | Short | Dogfighting, ground attack |
| Rocket launcher | Fixed or turret | All | 735–900 m | Anti-armor, anti-device |
| Minigun turret | Turret | All | 600–850 m | Anti-fighter, anti-infantry |
| Flak and point defense | Turret | Bases, capital vessels | Medium | Shoots down rockets and small craft |
| Plasma turret | Turret | Capital vessels | 900 m | Shield-stripping main battery |
| Artillery turret | Turret | Bases and capital vessels | 1,100 m | Long-range siege |
| Mining and salvage lasers | Fixed or turret | Small and capital vessels | Short | Tools that double as weak weapons |

- **Fixed weapons** fire where the vessel points; **turrets** aim on their own (AI) or under a gunner's control.
- **Ammo** is drawn from the structure's shared storage, by caliber. Running dry mid-fight is a real risk on long voyages.
- **Turret AI** picks targets by priority (players, vehicles, then devices), retargets immediately after a kill, and can be set to aim at hull or at devices.
- **Detection:** detectors and radar show contacts on the HUD and the map; range depends on the device and the target's size.

**Shields**

| Shield | Capacity (reference HP) | Classes |
| --- | --- | --- |
| Small vessel | 4,000 | Small vessels |
| Hover vessel | 6,000 | Hover vessels |
| Capital T1 / T2 | 18,000 / 36,000 | Capital vessels |
| Base T1 / T2 | 25,000 / 50,000 | Bases |

- Shields take about 15 s to come up, don't recharge while under fire (a short delay after each hit), and burn fold crystal while recharging.
- Every weapon has a shield-damage multiplier: plasma strips shields, rockets punish hull and devices.
- At zero shield HP, hits land on blocks and devices. Shield extenders and charged armor raise capacity.
- Shields must be down to warp, and the player can't add blocks while a shield is up, so retreating and repairing are both commitments.

**Disabling instead of destroying**

Targeted destruction sets up capture: kill thrusters to stop a ship, generators to drop its shields and turrets, and CPU extenders to cut its efficiency. A disabled ship is a boarding target (section 16); a ship whose core is destroyed goes derelict.

**Collisions and crashes**

- Impacts deal damage by speed and mass: when a ship hits another ship or the ground, blocks and components at the contact point take damage on both sides. A gentle bump does nothing.
- High-speed impacts with the ground blast a crater in the terrain, scaled to the impact and capped.
- A ship shot out of the sky falls, crashes and leaves a crater and a wreck. The wreck keeps whatever survived and can be salvaged, claimed and restored (section 12).
- Ramming works, but it hurts the rammer too.
- Landing, docking and nudging at low speed never cause damage.
- The rules behind this live in a separate, simple physics module (section 22).

**Where each class fights:** every class fights wherever it can go. Ground combat stays relevant to the endgame because invasions land troops on foot and bases are taken room by room.

## 16. Boarding and core hijacking

Any enemy structure with a core (an outpost, a fortress, a small vessel or a capital ship) can be captured intact by fighting to its core and hijacking it. Shooting it apart stays an option, but capture is the high-risk, high-reward path, and it turns enemy assets into the player's next rung on the scale ladder.

&#91;embedded content: boarding assault · 6 steps, 1 decision, 1 failure branch\]

**Three ways to deal with an enemy structure**

| Path | How | What the player keeps | Cost |
| --- | --- | --- | --- |
| Hijack the core | Board, reach the core, run the Hijack Tool to completion | The whole structure: hull, devices, cargo, docked craft | A fight through the interior, then a long channel under fire |
| Destroy and claim | Destroy the core; the structure goes derelict; place your own core | Whatever survived the fight; intact components can also be pulled out whole and reused | The core room is usually wrecked; other players can claim it first |
| Destroy | Shoot it apart from outside | Loot, salvage, and any components that survived, recovered whole from the wreck | Fastest and safest; lowest reward |

**Recovering components.** Once an enemy vehicle's or base's core is destroyed, its surviving components (thrusters, generators, turrets, shields, seats and the rest) can be removed whole with the multi-tool and fitted to the player's own builds. While the enemy core is alive, nothing can be removed. They can be fitted without unlocking them first; only crafting new ones needs the unlock (progression guardrails below).

**Targets** (reference values for tuning)

| Target | Class | Core security | Typical defenders | Breach routes | Hijack channel |
| --- | --- | --- | --- | --- | --- |
| Outpost | Small base | Level 1 | 2–6 guards, 2–4 turrets | Doors, walls, roof | 30 s |
| Fortress | Large base | Level 2 | 10–20 guards, internal turrets | Blast doors, breaching charges, vents | 60 s |
| Small vessel | Fighter or shuttle | Level 1 | Pilot only | Force a landing, then the command chair | 15 s |
| Capital ship | Frigate, freighter or patrol ship | Level 2 | 4–12 guards, internal turrets | Dock at an outer door, cut the hull, boarding pod | 60 s |
| Heavy capital ship | Warship or flagship | Level 3 | 15–30 guards, internal turrets, a vaulted core | Cut the hull, boarding pod, sabotage the shields from inside | 120 s in 3 stages |

**Hijack rules**

- **Hijack Tool.** A handheld device in three tiers, unlocked in the tech tree. Its tier must equal or beat the core's security level.
- **Channel.** The player holds interact at the core. Taking damage pauses progress; progress decays slowly while nobody is channeling. Up to 3 players can channel together for a shorter time, with diminishing returns.
- **Staged cores.** Level 3 cores hijack in three stages; each stage completion triggers a defensive response (guards, a sealed bulkhead, a power surge), so the squad must hold the room.
- **No hijack while powered shields cover the core room.** The squad must cut power, destroy the shield generator, or sabotage it from inside.

**Interior defenses**

- **Guards** patrol, respond to noise and converge on the core when the alarm rises. Officers carry keycards.
- **Security doors** lock to the owning faction. Players open them three ways: a short hack (quiet), a breaching charge (fast and loud, raises the alarm), or an officer's keycard (silent). This is pillar 5 in miniature: stealth, force or cunning.
- **Internal turrets** wake at alarm level 2 and fire on anyone not of the owning faction.
- **Decompression.** Cutting the hull vents connected rooms to vacuum. Suited players survive; unsuited guards suffocate; robotic defenders are immune.

**Alarm escalation**

| Alarm level | Trigger | Response |
| --- | --- | --- |
| 0 — Unaware | Undetected boarding | Normal patrol routes |
| 1 — Alert | A guard sees a player, or a loud breach | Guards converge on the last known position |
| 2 — Lockdown | Combat inside for 30 s, or a hijack starts | Bulkheads seal and internal turrets wake |
| 3 — Last stand (heavy capital ships only) | Hijack stage 2 completes, or hull below 25% | Self-destruct countdown (reference 180 s); finishing the hijack cancels it |

**After capture**

- Ownership moves to the player faction. Interior turrets switch sides; any remaining guards fight on until cleared.
- The structure reboots: warp drive and weapons stay offline for a cooldown (reference 120 s) while the core resynchronises, so a captured ship can't jump out of a fight at once.
- Damage persists. Hull breaches, wrecked devices and spent ammo must be repaired and restocked.
- The owning faction's standing drops sharply.

**Progression guardrails** (so capture doesn't skip pillar 4)

- Hacked components (captured with a structure or recovered from one) can be fitted and used at full output without unlocking them first. The player can't craft new ones until the recipe is unlocked, so the supply is limited to what they take.
- A captured structure still counts against the player's CPU and size-class limits. Over the limit, it flies, but sluggishly.
- Heavy capital ships appear only in high-danger sectors. A squad realistically needs capital-tier gear just to survive the boarding.

**Multiplayer**

- There's no PvP: only NPC-owned structures can be hijacked, never player structures.

**Kept simple**

- NPC capital ships carry no small craft. Players can still dock their own small and hover vessels on a captured ship.
- There is no crew system. Player ships never carry NPC crew, defenders never surrender or join, and a captured ship is flown by players.

## 17. NPCs, factions and threats

Seven factions give the galaxy its politics. One is the main enemy, one can never be reasoned with, and the rest can be traded with, helped or crossed. Standing with each is a number the player moves by what they do, and factions fight each other around the player, so the galaxy has battles to stumble on.

**Factions**

| Faction | Role | Default stance | Where they are | What they offer | What they throw at you |
| --- | --- | --- | --- | --- | --- |
| Vharn Dominion | Expansionist empire; the main enemy | Unfriendly, quick to turn hostile | Fortress worlds, garrisons and outposts on most planets | Little by trade; their best tech comes by capture | Infantry patrols, base invasions, fortresses, heavy capital ships |
| Kessari Clans | Self-aware robot clans resisting the Dominion | Neutral to friendly | Machine villages and relay spires on many worlds | Salvaged parts, repairs, reward weapons | Machine raiders if wronged |
| Meridian Combine | Human corporate fleet and colonies | Friendly | Stations and colonies in the inner arms | Missions, ship parts, repair and healing services | Patrols, if crossed |
| Ledger Exchange | Neutral merchant guild | Neutral; can't be made hostile by accident | Trading stations in most systems | Trade, repairs, fold crystal and fuel | Station guard ships if attacked |
| Carrion Reavers | Pirate clans | Unfriendly | Hidden asteroid bases at system edges | Black-market goods if friendly; buried treasure caches | Ambushes, raids on haulers |
| Freeholders | Independent settlers | Neutral | Farms, mines and small outposts | Odd jobs, local trade, a safe place to respawn | Nothing; they need protecting |
| The Hollow Bloom | A corruption that spreads like a virus: lovecraftian aliens, plus the mutants and twisted remnants of every race it has touched | Always hostile; can't change | Infested worlds, stations and derelicts; thickest near the core and in dead regions | Unique materials, taken by force | Mutant swarms, corrupted crews and ships, nameless horrors in infested POIs |

**Standing**

| Band | Range (reference) | Effect |
| --- | --- | --- |
| Hostile | Below 6,000 | Attacked on sight; no trade |
| Unfriendly | 6,000–11,999 | Attacked in their territory; no trade |
| Neutral | 12,000–17,999 | Trade allowed; faction missions offered |
| Friendly | 18,000–23,999 | Discounts; their teleporters and healing booths open |
| Honored | 24,000 and up (cap about 30,000) | Best prices; minor offences forgiven |

- **Gain** standing by completing faction missions, and by killing the faction's rivals and predators in its territory.
- **Lose** standing by attacking members, destroying or hijacking their cores, or mining and building in their territory (about 1 km around their bases).
- **Rivalries:** helping one faction moves standing with its rivals. Kessari and Meridian both oppose the Dominion; Meridian hunts the Carrion Reavers.
- **Repair:** envoy consoles at neutral stations accept reparations to restore lost standing.
- **Unclaimed space** has no faction territory at all: no penalties, no protection.

**AI threats**

- **No drones.** Every enemy is a soldier, a creature or a crewed vehicle.
- **Crewed vehicles:** every NPC vehicle is built from blocks and components exactly like the player's, and NPCs man its stations: a pilot flies it and gunners work its turrets. Kill a gunner and that turret goes quiet; kill the pilot and the ship drifts. Boarding (section 16) and picking off crews are real tactics.
- **Patrols:** foot patrols around garrisons, crewed patrol ships in faction space, and guard ships around trading stations.
- **Sky patrols:** as in Empyrion, crewed vessels fly circuits over planets: a faction's patrol ships over its territory, and Reaver gunships or a Reaver capital ship over worlds they raid. They're visible from far off, scan the ground as they pass, and answer what they find (a firefight, a landing party, a call for help) with warning, never out of nowhere.
- **Creatures:** every planet generates its own wildlife (section 4): grazers, herds, pack hunters, ambushers, scavengers, swarms and giants, getting weirder the more alien the world.
- Server options set enemy presence, invasion frequency and attack strength (section 19).

**Base invasions**

The Dominion invades player bases with troops sent from its nearby bases (outposts, garrisons and fortresses) or by ship from further away. So does every other faction the player is hostile with: the nearest hostile base of any faction sends the invasion, with that faction's own troops and tactics. The Ledger Exchange and Freeholders never invade, and in Bloom space the Hollow Bloom attacks from its infestations. Knocking out or hijacking a nearby enemy base stops the invasions it sends, so going on the offensive is the best defence. Invasions are announced well ahead, scaled to what the base is worth, and survivable.

**Invasions can be switched off.** The in-game options menu has an Invasions on/off toggle (the host's setting in co-op), changeable at any time. Switching it off cancels any warnings under way and sends an assault in progress home; switching it back on resumes normal scheduling, with the full 10, 5 and 1 minute warnings.

| Time | What happens |
| --- | --- |
| 10 minutes out | First warning on every player's visor and on the base's consoles, naming the attacker, the base it's coming from and its approach |
| 5 minutes out | Second warning; the enemy force appears on long-range scans |
| 1 minute out | Final warning; air-raid sirens start blaring across the base |
| Arrival | Troops land a few hundred metres out from ground vehicles, small vessels or capital ships and assault on foot, backed by their crewed vehicles |

- **Scaled to the base:** the force grows with the base's local resources (stored goods, size, defences) and the number of players present. A small outpost faces a squad; a fortress faces an army.
- **What invaders want:** they damage the base as they fight their way to the core, and try to destroy it. If the core falls, the base goes offline and the invaders withdraw; placing a new core brings it back with every block that survived.
  - **Loot is never at risk:** invaders never take items, and items are never destroyed. If containers are wrecked, their contents spill into a crate that stays until collected.
- **Sirens** keep sounding until the last invader is down or gone.
- **Only when someone is there:** a base with no player in its playfield is never invaded, so nobody logs in to find their home wrecked.

**A living galaxy, without simulating it**

The galaxy feels alive through incidental encounters around the players, not a simulation of everything at once.

- An encounter director spawns small events near players from a weighted deck per region and faction: freighters passing through, a patrol clashing with Carrion Reavers, a distress call, traders arriving in orbit, a crash smoking on the horizon, a Bloom outbreak at a station, Kessari miners at work, factions fighting where their territories meet.
- Events use the same crewed, block-built ships and soldiers as everything else, so players can join in, loot, rescue, board or ignore them.
- Nothing is simulated away from players except a few lasting facts: faction territories, POI states and what players changed. Events clean themselves up after players leave.
- Wrecks left by events can stay as new salvage, so the world remembers what players saw.

## 18. Economy, trading and logistics

The economy is a shortcut, not a second game: credits turn surplus into what a world lacks, and themed trade routes give explorers a reason to remember where things sell. Trading itself has no friction: one shared balance, one trade screen, no haggling. Logistics is kept simple: one shared storage per structure, plus simple toggles and triggers.

**Credits**

- **One shared balance.** Credits are a single number shared by the player faction and always shown on the HUD. There's no money item to carry or lose, and no bank to visit.
- Gold coins and other currency found as loot turn into credits the moment they're picked up.

**Traders**

- **Where:** trading stations, outposts and hubs, on planets and in orbit. Each trader has a themed stock and a list of what it buys.
- **One trade screen:** buy and sell straight from the player's inventory or the storage of any ship docked or landed nearby, all in one window. Once docked, players can trade from the ship's systems console; there's no walking to a counter.
- **No haggling, no caps:** traders buy any amount of what they list at a steady price and restock what they sell over time. Buying always costs more than selling pays, so trading can't print money.
- **Shortcuts:** a sell-all button for each item type the trader buys, and anything in storage the trader wants is highlighted.
- **Prices on the map:** once a trader is discovered, the map remembers what it buys and sells, so players can look up where to sell an item before flying there.
- **Standing:** friendly and honored factions give discounts; hostile ones won't trade.
- **Trade lines:** themed traders along fixed routes buy what other traders sell. Finding a profitable line is a discovery reward, and the map shows it once found.
- **Services:** repairs, shield and fold crystal refills, healing and respawn points, bought from the same screen and priced by standing.
- **Shared stock:** every player sells from and buys into the faction's shared storage and balance, so there's no trading between players.

**Storage and logistics**

- **One storage per structure:** every container attached to a base or ship adds its capacity to a single shared storage. There's one inventory per structure, not one per box, and any container on it opens that same inventory (capacities in section 13).
- **Everything uses it:** constructors take their inputs from it and put finished items back into it; replicators, repair devices and turrets (for ammo) draw from it too. There are no input or output assignments to set up.
- **Between structures:** when a craft is docked, its storage and the carrier's open side by side for drag-and-drop transfer.
- **Auto-miners** hold their ore until a player collects it (section 10).
- **Teleporters and portals** link the player's structures within range.

**Home and auto-transmit.** The player flags exactly one base or capital ship as Home; flagging a new one clears the old. Vessels carry an auto-transmit toggle (a vessel component): with it on, materials the vessel farms go straight into Home's storage while the vessel is within transmit range of Home and Home has space. Otherwise they stay in the vessel's own cargo and transfer automatically once it's back in range or Home frees space. When Home is part full, what fits transfers and the rest waits aboard; nothing is ever deleted. The vessel's console shows the link: in range, out of range, or Home full. The range is a data value, and a moving capital-ship Home works the same way.

**Toggles and triggers**

Builders control devices with simple toggles, not circuits: there are no logic gates, timers or wiring.

- **Device groups:** any set of devices (doors, lights, turrets, hangar doors, energy barriers) can be grouped and named.
- **Switches and buttons** toggle a group on or off. Groups also appear on the ship's systems console and can be bound to hotkeys.
- **Sensors:** a motion or proximity sensor can toggle one group when something enters its area, which covers automatic hangar doors or an alarm light. One sensor drives one group, with no chaining.
- **Built in:** doors open for the player faction on their own and their force fields hold the air, so the common cases need no setup.
- NPC points of interest use designer-authored scripting for traps and puzzle doors; players never need to learn it.

## 19. Multiplayer, game modes and difficulty

GalHack plays the same alone, or in co-op over Steam, and every player belongs to one shared faction that owns everything they build. Difficulty is a set of independent dials rather than one slider, so a group can make survival gentle and combat brutal, or the reverse. GalHack ships without modding support.

**Multiplayer**

- **Steam multiplayer:** co-op runs over Steam. Players join through Steam friend invites, and a group can run an always-on server of its own so the world is there whenever anyone logs in. There's no matchmaking or public server browser: GalHack is made for one group of friends.
- **One player faction:** every player is permanently a member of the single player faction and can't leave it or start another.
- **Everything is shared:** every player structure belongs to the faction. Any player can enter, fly, build on, repair or deconstruct any of them; there are no locks, per-device permissions or ownership transfers. On ships, players split the work through station roles (section 12).
- **Shared standing:** standing with NPC factions belongs to the player faction, so one player's raid changes everyone's prices and welcome.
- **No PvP.** The only player-versus-player setting is optional friendly fire, off by default.
- **Server settings:** host and admin controls, structure count and block limits, and the CPU, mass and volume rules (on by default).

**Game modes**

| Mode | What it is | Who it's for |
| --- | --- | --- |
| Survival | The full game | Everyone |
| Creative | Unlimited resources, no enemies, optional empty world | Designing ships and bases |
| Creative workshop | A creative bubble inside a survival save; designs come out as blueprints that still cost resources to build | Builders who want to design without wasting materials |
| Relaxed survival | No hunger, no oxygen limits, health regenerates | Explorers who want discovery without survival pressure |

**Starts**

- The player picks a seed and a start planet type: temperate (easy), ice, alien swamp, barren or lava. Same seed, same galaxy.
- **Every start is a cryo bunker.** Whatever the start planet, the player wakes in a cryopod chamber: a small buried shelter where they slept through the collapse. Inside, the climate stays stable and comfortable with breathable air, so even on a dangerous world the player has a chance and a place to shelter. The bunker has four cryopods: one works for each player, and the other sleepers are dead. In co-op (up to 4 players), a player who joins later wakes from one of those pods, which comes alive for them. They climb out through a hatch or a collapsed stairwell into the ruined world above. Start worlds were always inhabited. No faction holds ground on them, though factionless people may be there (lone survivors, scavengers, roving gangs). A start world's system has plenty of space wrecks in orbit and nearby (derelict ships, wreck fields, broken stations) to salvage once the player reaches space. The bunker lies at the edge of a ruined town or city, away from any living settlement, so the first view outside shows the setting at once: overgrown ruins and abandoned houses, a broken road with rusted cars, and a POI on the horizon worth heading for. The player wakes empty-handed: the first thing they do is make the survival tool, free of charge, in the suit's fabricator, then equip it. Each pod has its own locker on the wall holding the rest of that player's starting kit; a locker stays sealed until its player comes online. It's built from ordinary blocks, so players can pull it apart for materials or reuse it: build onto it, or make it part of their first base. There is no escape-pod start.
- **Veteran starts** drop the player on a harder world with hostile patrols and no protection around the bunker.

**Difficulty dials**

| Dial | Settings |
| --- | --- |
| Cryo bunker contents | Easy, medium, hard |
| Progression speed | Faster, normal, slower |
| Item degradation | Off, low, normal, high |
| Backpack drop on death | Nothing, inventory only, everything |
| Food use | Off, low, normal, high |
| Oxygen use | Off, low, normal, high |
| Radiation and temperature | Off, low, normal, high |
| Ore richness | Rich, normal, poor |
| Deposit count | Plenty, normal, few |
| Enemy attack strength | Easy, medium, hard |
| Enemy presence | Off, low, normal, high |
| Base invasion frequency | Off, low, normal, high |
| Crafting and blueprint build time | Faster, normal, slower |
| CPU, mass, volume and block limits | On or off |

## 20. Diegetic UI and onboarding

**PDA tutorials**

- The PDA watches the player's actions and shows a hint the first time they do something (first ore, first base, first vessel), with no fixed order.
- Short tutorial chapters cover each milestone: climb out of the cryo bunker, build a base, build a hover vessel (offered at level 3), reach orbit, first warp. Each chapter gives a small reward.
- Tutorials can be skipped; skipping grants the chapter's key reward (for example a portable constructor, a hoverbike or a detector) so experienced players lose nothing.
- Starts on harsher planet types skip the gentle chapters by default.

**Where everything lives**

GalHack's interface **looks like part of the world** without being built into it. Every panel is styled as the suit's or ship's own tech (screen glass, scanlines, bold paperback colours), but it's an ordinary on-screen interface, not a screen you walk up to. Everything the player manages lives in **one full-screen menu on Tab**. A minimal helmet-visor HUD covers what matters moment to moment, and the game never pauses for the menu.

| Need | Where | Notes |
| --- | --- | --- |
| Vitals, hazard warnings, ammo, compass, contacts, hotbar | Visor HUD, always on | Minimal and curved to the visor; fades when all is well; a vital flashes before it starts costing health |
| Inventory, equipment, your stats | Tab menu: **You** | Grid inventory with mass and volume; equipment slots arranged around a 3D paperdoll of your character model (armour, boosters, tools); your stats beside it; drag items onto the hotbar |
| Built-in manufacturing | Tab menu: **You** | The suit's own fabricator for the bare minimum: the survival tool, food, drink, heals, oxygen, a tent, a basic radar and a placeable survival constructor. That survival constructor, set down on the ground, makes your first core and early blocks |
| Containers, bodies and loot | Two-pane view from **You** (E on a container, body or creature opens it) | Your pack beside the container; take all and store all |
| Game stats | Tab menu: **Stats** | The game's stats |
| Any nearby core's systems | Tab menu: **Core** | The base or vessel you're in or next to: its manufacturing (constructors, replicators, factory, ammo restock), storage, core mode, gravity, Home flag, auto-transmit, device toggles, power, CPU and mass, station roles |
| Tech tree, codex, tutorials | Tab menu | Locked nodes show what they need; codex entries fill in on discovery |
| Map | Tab menu: Map, plus a visor mini-map | Discovered places marked; unknown space stays dark |
| Trade | Tab menu: Trade, when at a trader | Buy and sell lists beside your and nearby ship storage; sell-all buttons |
| Invasion and storm warnings | Visor HUD and sirens | Countdowns at 10, 5 and 1 minutes |

The Tab menu is the primary way to do everything. Machines and consoles in the world also keep their own screens, as flavour you can use: walk up to one and it works directly, with an on-screen cursor while you look at it, mounted at eye height, showing the same panel as the Tab menu.

**Style rules**

- No flat web-style menus, floating windows or dashboard pop-ups: every screen belongs to an object in the world.
- The look matches the ships: chunky type, bold colour blocks, scanlines and simple vector graphics, in each faction's palette.
- Readability comes first: large type, strong contrast and short labels, even on in-world screens.
- Using a screen brings the view in close to it; in multiplayer the world keeps running around the player.
- A cursor appears only while a screen is in use.

**Build-mode aids**

- The multi-tool projects ghost blocks as holograms, and the visor shows an airtight overlay of where rooms leak.
- Live power, CPU and mass readouts on the visor update with every block placed, with a warning before a placement crosses a CPU tier.
- Symmetry planes, shape picker and paint sprayer are one keypress away.

**Comfort**

- First and third person everywhere; seated view in vehicles.
- Hotkeyed device groups for quick control of doors, lights and turrets.
- Players respawn in the seat they logged out from.

## 21. Design risks and what to prototype first

The biggest risk is the primary pillar itself: if worlds and POIs blur together, nothing else saves the game. The second is the two signature features (walkable moving ships and boarding), which are technically the hardest parts to build and should be proven early.

| Risk | Why it matters | Mitigation |
| --- | --- | --- |
| Procedural sameness | Discovery fails if planets look alike and POIs repeat | Distinctness test, modifiers, terrain stamps, seen-weighting of POIs (sections 4–5) |
| POI authoring cost | Each hand-built POI takes dozens of hours, and library size drives variety | Faction kits, POI families, four states per layout, a procedural dressing pass |
| Walkable moving ships | Reference-frame physics and networking are hard; jitter breaks the fantasy | Ship-relative positions on the network; prototype in multiplayer from day one |
| Guards inside ships | NPC pathfinding on destructible block interiors is fragile | Every ship and base is a block grid, so guards path with plain A\* on the structure's own grid (section 22) |
| Punishing early game | Players quit before discovery pays off | Ten-minute rule, start-system guarantee, PDA tutorials, relaxed survival mode |
| Constraint overload | Power, CPU, mass and size class confuse builders | Live readouts while building; size class hidden; CPU is a soft limit |
| Capture skips progression | Hijacking a heavy capital ship early could end the ladder | Progression guardrails (section 16) |
| Shared-everything griefing | Any player can deconstruct any structure | Friendly fire off by default; automatic blueprint snapshots so the host can restore a lost build |
| Performance | Large ships, many zones and many NPCs strain servers | Lazy system generation, per-zone entity caps, baked structure lighting |
| Terrain digging | Voxel editing is the genre's most bug-prone system: floating terrain, stuck players and vehicles, holes that differ between players | Remove-only shallow digging, undiggable ground around structures and POIs, no terrain physics, server-side edit lists (section 10) |
| Physics creep | Crashes, collisions, wind and liftoff could tangle physics into every system | One self-contained physics module with simple rules, talking to the rest through a few events (section 22) |
| Lost player data | The genre's worst failure: bases, ships and items quietly disappearing over time | Persistence rules (section 22): one writer, atomic saves, no cleanup of player-owned things, audits on load, rolling backups, save soak tests |

**Prototype order**

1. A capital ship two players can walk around in during flight, warp and combat.
2. A planet generator with five archetypes and modifiers, tested by asking players to tell planets apart blind.
3. One POI family in all four states, placed by the generator.
4. A full boarding of an NPC capital ship: suppress, breach, fight through, hijack.

**Status, 5 Nov 2026:** all four are built and on main, along with eleven more prototypes: terrain digging, encounters, block building, invasions, survival vitals, ship handling, diegetic UI, weather, Steam co-op, saves and zones, and wildlife. Each has headless tests that run before every merge. Next: the playable slice of the core loop (in progress), then enemy bases and complete POIs (real structures with cores, live defenders, assault and hijack), ahead of any further invasion work.

Trading is built last of all systems.

**Open questions**

- [ ] How many POI families and planet archetypes are the launch target?
- [ ] Does the creative workshop ship at launch or later?
- [ ] Decided: a Steam co-op session has a cap of 4 players, which may rise later if performance allows.

## 22. Technology and production

GalHack is built in Godot 4 with C#, by a team of AI agents directed by a human lead. Every choice below favours work that is text-based, testable and automatable, because that's what agents do reliably, and it all serves guiding rule 1: stable and smooth first. GalHack is free, never sold, and made for one group of friends.

**Engine and stack**

| Area | Choice | Why |
| --- | --- | --- |
| Engine | Godot 4, .NET build, pinned to one version; C# for all game code | Scenes and resources are short text files that diff and merge cleanly; everything runs from the command line; open source; no licence activation; light on low-end PCs |
| Rendering | Mobile renderer by default, Compatibility renderer as the fallback for very old GPUs; mostly baked or simple lighting; no heavy post-processing | The Foss style (bold flat colour, decals, chunky silhouettes) is cheap to draw |
| Multiplayer | Host co-op over Steam for one group of friends, plus an optional always-on server the group runs itself. No matchmaking, anti-cheat or public server browser. The host owns the world; each player's own movement and shots are trusted | It's a game for friends, not a public service, so trust keeps play smooth and the code simple |
| Large worlds | Floating origin per playfield, plus ship-local frames for interiors | Keeps standard-precision maths accurate across a 64 km planet without a custom engine build |
| Engine changes | None; a custom engine build only if profiling proves it's needed | No engine to maintain |

**Performance targets** (suggested)

- Minimum spec: 4-core CPU, 8 GB RAM, GTX 1050-class or Iris Xe-class GPU.
- A steady 60 fps at 1080p on low settings in normal play, never below 30 fps in the biggest fights.
- A minimum-spec test machine runs the performance suite on every build; a frame-time regression blocks the merge.

**Keeping it fast**

- Ship and base blocks merge into one mesh per chunk (reference: 16 × 16 × 16 blocks) with compound box colliders; greebles are instanced; distant ships use simplified meshes.
- Terrain is a heightfield in the distance and chunked voxels only near players; edits are stored as small change lists (section 10).
- Debris and effects are pooled and capped.
- The host owns the world state; each player's own movement and shots are trusted, so play feels instant.

**Physics as a separate module**

Crashes, collisions, wind and liftoff live in one self-contained module with a few simple rules, so physics never tangles with the rest of the simulation.

- One rigid body per structure, never per block; colliders reuse the chunk boxes built for rendering.
- Impact damage is one simple formula of relative speed and mass, applied to blocks and components near the contact point. No deformation, stress or per-block physics.
- Craters are a capped sphere of terrain removal using the normal remove-only terrain edit.
- Landed ships and base-mode structures are frozen and cost nothing. Ship interiors run in the ship's own frame, and only the ship itself collides with the outside world.
- Wind and tornadoes are force volumes applied by the same module.
- The rest of the game hears about physics through a handful of events (impact, structure split, landed, lifted off), so combat, storage and crafting never touch physics code.

**Grid-based navigation**

- Terrain sits on a regular grid (1 m cells) and every ship and base is a block grid, so enemies find their way with plain A\* pathfinding.
- Walkable cells come straight from the grid; doors, ladders and ramps link cells together.
- Paths inside a moving ship are worked out in the ship's own frame, so guards can walk a ship's corridors while it flies.
- Long paths use two levels (chunk to chunk, then cell to cell) to stay cheap, and grids update only where blocks or terrain change.

**Code structure**

- Small modules with clear boundaries (world generation, terrain, structures, combat, physics, AI, UI, networking) that talk through a few well-defined events.
- Data over special cases: a new feature adds data before it adds code.
- When complexity is unavoidable, it's contained in one module behind a simple interface, and nothing lands without tests.

**Zones under the hood**

- **One isolated world per live zone:** the host runs each zone with a player in it as its own Godot world (its own physics, scene and navigation). Only the zone the host is standing in is drawn; the others keep simulating. A small prototype must confirm that physics runs in worlds that aren't drawn.
- **Limits:** up to 4 live zones on a player-hosted game, more on an always-on server. An empty zone stays live for about 2 minutes, so hopping between surface and orbit doesn't reload it every time.
- **Precision:** the host pins each zone's physics origin at its centre. With the map wrapping both ways, nothing is ever more than half a map away on either axis (32 km east-west and 16 km north-south on the largest surface, about 2 mm precision), so no custom engine build is needed.
- **Clients** load only their own zone, plus the destination during a transition. Every networked object carries a zone ID, so each player only receives what's in their zone.
- **Ship cabins:** each occupied ship gets its own small private world for its cabin, released when the ship is empty and parked. This replaces the prototype's single ghost slot: no slot limits, no ghosts colliding, and passengers ride through zone changes untouched.
- **Saving across zones:** every saved record has exactly one zone ID, plus a ship ID if it's aboard. Orbit starts low, on Empyrion's scale rather than a realistic one: about 1,060 m up (a data value), with most terrain under about 500 m, the cloud layer above that, and only the highest peaks (about 900 m) rising into it. To the player, crossing between a planet and space is seamless: no loading screen, fade to black or flashy effect, only the atmosphere fading away as the ship climbs (the sky darkens to space, the haze thins and the stars come out, and the reverse on the way down), the destination streamed in ahead, and the ship's position, orientation and velocity carried straight across. Underneath, crossing a zone is one atomic write, then the object is removed from the old zone and spawned in the new one. A crash mid-crossing leaves it in the old zone or the new one, never both and never nowhere. The load audit moves any record pointing at a missing zone to the last base.

**Persistence: nothing ever disappears**

The world save is the most important system in the game. Structures, items and progress must never vanish, whether the game crashes, a zone unloads or the save is months old. This is the genre's worst known failure, and GalHack treats it as a release blocker.

- **One writer:** only the host writes the save; clients never do.
- **Atomic writes:** every save goes to a new file, is checked, then swapped in, so a crash mid-save can't corrupt anything.
- **Small, separate records:** each structure, container, player and zone's terrain edits is its own record with a stable ID and a checksum, so damage to one can't spread.
- **No silent cleanup, ever:** nothing player-owned is despawned, garbage-collected or tidied up. POI regeneration and event cleanup only touch things that were never player-owned.
- **Zones save before they unload,** and loading checks that everything recorded came back.
- **Rolling backups:** automatic snapshots (reference: every 15 minutes for the last day, plus daily ones for a month), restorable by the host from a console.
- **Audit on load:** counts of player-owned structures, blocks and items are compared with the last save. If anything is missing, the load stops and offers a backup instead of carrying on without it.
- **Versioned saves:** every format change ships with a tested migration, so old saves always load.
- **Tested hard:** automated tests save and reload thousands of times, kill the game mid-save and soak multi-hour sessions, and the build fails if a single item goes missing.

**Content as data**

- Blocks, components, materials, recipes, ships, POIs, faction kits, planet archetypes, factions and loot tables all live in text files with schemas.
- Ships and POIs are stored as block lists that agents can write and edit directly. The in-game builder saves to the same format, so a human or an agent can author either way.
- Validators check every data change: schema, CPU and mass budgets, a reachable core, all four states for every POI family, loot that respects rarity.
- Tuning values (damage, speeds, drain rates, prices) live in data too, so adjusting feel is a one-line change.

**Automated testing**

| Test | What it proves |
| --- | --- |
| Unit tests | Core rules: damage, storage, crafting, standing, trading |
| World-generation validators | Sampled seeds meet the availability guarantees, the start-system guarantee and the distinctness test (sections 4 and 10) |
| Headless bots | They walk around a flying and warping ship, dig terrain, build a base, board and hijack a capital ship, die and recover their backpack |
| Multiplayer soak tests | Several bot clients play over Steam networking for hours with no desync or drift |
| Screenshot comparisons | Fixed scenes still look the same after a change |
| Performance suite | Frame-time budgets hold on the minimum-spec machine |

**Art pipeline**

- **Agents generate:** block and component meshes assembled from simple shapes in the Foss style, procedural planets and skies, and textures and livery decals made with AI image tools.
- **Licensed:** animated creatures, characters and their animation sets in standard formats (glTF or FBX), plus sound effect and music libraries.
- **Style guide as data:** faction palettes, livery rules and a silhouette checklist (section 5), so every agent produces consistent work.
  - **No AI-generated look:** nothing ships with the tell-tale signs (muddy over-detail, mushy shapes, inconsistent lighting, garbled lettering). The human lead reviews every new asset against the style guide, and in-game text is written to read as human.

**Asset sources** (licences checked 4 Nov 2026). GalHack is free and never sold, so non-commercial licences are fine too, and everything is credited.

| Source | Use it for | Licence | Watch out for |
| --- | --- | --- | --- |
| [Sonniss GDC bundles](https://sonniss.com/gdc-bundle-license/) | Pro-grade weapons, explosions, machinery, weather, sirens | Royalty-free, no credit needed | No redistributing raw files; no use for AI training |
| [Freesound](https://freesound.org/help/faq/) | Long-tail sounds: thunder, rain, wind | Per sound: CC0, CC-BY or CC-BY-NC | Non-commercial sounds are fine because GalHack is never sold; skip Sampling+ |
| [Pixabay](https://pixabay.com/service/license-summary/) | Sound effects and some music | Pixabay Content License, no credit needed | No redistributing raw files |
| [Kevin MacLeod (incompetech)](https://incompetech.com/music/royalty-free/faq.html) | Ambient and dark-ambient cues | CC BY 4.0 | Credit each track in the game's credits |
| [Soundimage.org](https://soundimage.org/attribution-info/) | Large sci-fi and ambient catalogue | Free with credit inside the game, or paid per track | A store-page credit doesn't count |
| [HoliznaCC0](https://freemusicarchive.org/music/holiznacc0/only-in-the-milky-way-part-2) | Vintage sci-fi ambient electronic | CC0 (check each track) | Mellow rather than haunting |
| [Poly Haven](https://polyhaven.com/license) | PBR textures, night-sky HDRIs for planet surfaces | CC0 | No site scraping; no deep-space HDRIs |
| [ambientCG](https://docs.ambientcg.com/license/) | Hull plating, rust, painted metal, concrete | CC0 | None found |
| [space-3d](https://github.com/wwwtyro/space-3d) | Procedural nebula and star-field skyboxes | Unlicense | Export steps not yet verified |
| [KayKit Space Base Bits](https://kaylousberg.itch.io/space-base-bits) | Low-poly modular base parts, glTF included | CC0 | Don't resell unmodified copies |
| [Quaternius](https://quaternius.com/license.html) | Animated aliens, robots, monsters, mechs, characters; sci-fi kits | Quaternius Asset License v1.0: commercial use, no credit | No redistributing the assets; check the licence file in each pack |
| [Mixamo](https://community.adobe.com/t5/mixamo-discussions/mixamo-faq-licensing-royalties-ownership-eula-and-tos/td-p/13234775) | Humanoid rigging and animations | Free in commercial games, no credit | Raw files stay private; no AI training; confirm Adobe's current terms |
| [Google Fonts (SIL OFL 1.1)](https://openfontlicense.org/open-font-license-official-text/): Orbitron, Michroma, Zen Dots, Audiowide, Monoton, Bungee, Oxanium, Space Mono | 1970s sci-fi display type, hull markings, panel readouts | SIL Open Font License 1.1 | Ship the licence text; don't rename modified fonts that have a reserved name |
| [Terrain3D](https://github.com/TokisanGames/Terrain3D) | Planet-surface terrain in Godot 4 | MIT | None found |
| [godot\_voxel](https://github.com/Zylann/godot_voxel) | Voxel terrain with LOD | MIT | C# support and install route still unclear |

**No Kenney assets of any kind:** no graphics, models, sounds or fonts from Kenney, anywhere in GalHack.

- Credit everything: every asset, CC0 included, gets a line in the in-game credits (author, source, licence), kept in a credits data file that also logs the URL.
- Raw files from Sonniss, Mixamo, Pixabay and Quaternius never go in a public repository; share builds, not source assets.
- Licensed assets are never used to train or tune AI tools.
- Licensed assets still pass the human style review; nothing ships with the AI-generated look.

**Team and workflow**

- The human lead sets priorities, plays every build and judges feel: combat, flight, moving interiors.
- Agents work in parallel on separate branches; every change must pass the build, the tests and the validators before it merges.
- A weekly playtest on the minimum-spec machine; anything annoying goes on the fix list before new features.

**Where agents are weak, and the mitigation**

| Weak spot | Mitigation |
| --- | --- |
| Game feel | Human playtests; tuning values in data files |
| Animation | Licensed animation sets; minimal custom animation |
| Multiplayer desync | One simulation path, server authority, soak tests |
| Mixing up engine versions | C# only, a pinned engine version, a project API reference for agents |

## 23. Glossary

| Term | Meaning |
| --- | --- |
| Alarm level | A boarded structure's response state, 0 (unaware) to 3 (last stand) |
| Archetype | A planet type (temperate, lava, ocean...), refined by a variant and modifiers |
| Burnstone | Common fuel ore, refined into fuel packs |
| Capital vessel (CV) | A large-grid structure with its core in capital mode: it flies, warps between stars and carries smaller craft. Switch the core to base mode once landed |
| Component | A functional part of a ship or structure (engine, generator, turret, shield, command chair and so on), with its own structural stats; also called a device |
| Core | The block that defines a structure's owner; destroy it to make a derelict, hijack it to capture |
| CPU tier and extender | A structure's complexity budget, raised in four tiers by placing extenders |
| Danger grade | Easy, medium, hard or extreme, shown on the map before travel |
| Echo shards | Unique crystals from alien ruins, used for charged armor and epic upgrades |
| Emberite | Rare ore from lava worlds near young and dying stars; fusion and plasma tech |
| Expedition | A unique, multi-stage set piece with a long-range goal |
| Fold crystal | Rare crystal that fuels warp jumps and recharges shields |
| Gravium | Uncommon dense ore from high-gravity worlds, used for heavy armor |
| Hijack Tool | Handheld device that takes over an enemy core, in three tiers |
| Hover vessel (HV) | Small-grid surface vehicle that hovers over land and water |
| Lattice core and lattice array | Unique components needed for CPU tiers 3 and 4 |
| Modifier | A trait rolled onto a planet that changes its look and play (ringed sky, perpetual storm...) |
| Organics | Plant matter, creature meat and other biomass, kept in storage to feed replicators |
| PDA | The tutorial and hint system |
| Playfield | One instanced zone: a planet surface, an orbit or a region of space |
| POI | Point of interest: a hand-built structure placed by the generator |
| POI family and state | A set of layouts under one name, each spawning active, abandoned, infested or ruined |
| Precursor alloy | Unique armor salvaged from alien ruins; can't be crafted |
| Replicator | Device that turns organics and energy into food; feeds the player to full on demand, and its tier (T1–T3) sets food quality and shelf life |
| Ship frame | A structure's own reference frame, so players can walk inside it while it moves |
| Short-range warp | A free jump to a target within the same playfield |
| Signal lead | A beacon or signal that points toward content by direction and distance |
| Small vessel (SV) | Small-grid ship for atmosphere and in-system space |
| Standing | The player faction's shared reputation with an NPC faction, from hostile to honored |
| Starsilver | Rare metal from worlds near giant stars, for top-tier devices |
| Storage unit (SU) | The volume measure for items and containers |
| Survey data | Scan results that traders buy; the explorer's income |
