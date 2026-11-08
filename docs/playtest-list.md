# Playtest list, 6 and 7 November 2026

Every issue the owner raised while playing the batch build. Tick an item only when it works in the real game, not just in a test.

**Status key and counts** (157 open items): main 14, batch 39, branch 22, running 7, partly 26, plan only 7, not started 38, unknown 4.

**Not started and unassigned** (most noticeable first):
1. Hollow growth blocks look bad
2. Hollow mystery
3. Text encoding bug
4. Quieter UI
5. Creatures fit their environment
6. Survivors with personality by role
7. Flavour animals as effects, not AI
8. Personal shields
9. Thrust hold toggle, power budget, thrusters and drives using CPU and fuel, spike
10. Puffs of dust kicked up by wind
11. Tumbling debris in the wind, if cheap
12. Aquatic and amphibious species don't exist yet
13. Killed flying creatures fall along an arc
14. Debug options on the start screen
15. Distant planets and moons show their real surface detail from afar, matching wha
16. Vacuum worlds, small or large, with no atmosphere
17. Music
18. Water features with it
19. Rings up close resolve into fields of little asteroids you can fly through and m
20. Camera vibration while moving near wrecks
21. Road markings stop before structures
22. Gravity anomalies
23. Geography less like a noise map
24. Ruined cityscapes
25. Rare worlds in the midst of collapse
26. More scattered rock and bits of ruin
27. Ruins fit the world's air, not the kind of body
28. High-tech survivors and factions use vehicles and weapons the player could const
29. Husks
30. One bestiary module that all critters, beings and robots come from
31. Ground and flight builds
32. Spool-up lift-off, then rerun the massive POI test
33. Worst-case stress test after the merge
34. One roving non-aggressive ship on start worlds
35. Test against the bar
36. Unexplained cones when shooting
37. Review PlaceWalk and OrbitTest changes made to fit random worlds
38. Extra world variables if simple and impactful

## Playtest 8 November 2026 (owner, overnight)

Screenshots: `reference-photos/playtest-1108/` (local only). Owner's order: world themes, POIs and bestiary first, then the UI.

World and places:
- [ ] Floating rubble with z-fighting in ruins [running: sweep]; a bug stuck in the floor, walking about trapped in a wall [batch: creatures freed from walls]
- [ ] Z-fighting on the inside faces of doors [not started: bug sweep]
- [ ] Dead animals still show the green halo; corpse should stay without it; loot from an animal shot on a ledge got caught in the ceiling [batch]
- [ ] Rooms don't match their place: office desks and filing cabinets inside planes, hangars and a mine [not started: bug sweep]
- [ ] Planes read as boxy buildings shaped like a plane; aircraft should be generated as aircraft [queued: wrecks job]
- [ ] Mine passage blocked by tool racks; furniture must never block the way through [not started: bug sweep]
- [ ] Blasting crates don't explode when shot [not started: bug sweep]
- [ ] Shrubs and ground cover stop in a hard line around a POI [not started: bug sweep]
- [ ] Caches are finicky to open from some angles [not started: bug sweep]
- [ ] Tube flora has incomplete collision [not started: bug sweep]
- [ ] Water edge hovers above the shore on desert worlds; crouching in water won't go all the way and the camera vibrates [not started: bug sweep]
- [ ] Ore deposits too plentiful and too small: fewer, bigger deposits (count to a third or half, each several times larger), count and size as data per ore and world type, first ores still near the start [running: sweep]
- [ ] Tool yields too much ore (tons in the pocket fast): survival tool to about 3 ore per cell, better tools faster not richer [running: sweep]
- [ ] Carry limits modelled on Empyrion (owner approved): Empyrion limits by volume (suit armour holds about 100 L light, 150 medium, 200 heavy; a transport booster adds 400 L, 800 L epic, with a small speed cost; iron ore is about 16.5 kg and 3.2 L a lump); our ore gets real mass and volume, carrying past capacity slows, then stops sprint and jetpack, then stops pickups; numbers as data [queued: next small gameplay job]
- [ ] Hover bike cargo (owner, 8 Nov): storage three times what the player can carry in the starting suit, reached by walking up to the bike or from the seat; set as a data value tied to the carry limit [queued: with the carry limits job]
- [ ] Haven't met a real dungeon yet in this build [batch: 6 to 10 a start world, nearest 0.4 to 1.5 km; owner to check]
- [x] Owner likes: world geography and biomes ("very realistic"), flora, fewer fauna, skyboxes and the nebula in the night sky matching the star map, weather coming along

- [ ] Layers everywhere: flora strangeness, creature mix, decay and wear, weather, sky palette and sound all read the world's rolled type, theme, fall and tech, not just the planet type; make the rarer occupants show something distinct (keepers, sleepers, guardians, mimics, "something wrong here"); palette rule (owner, 8 Nov): natural colours (sky, ground, water, rock, plants) come only from the world's star, air, soil, minerals and life, never the theme; made things (paint, roofs, signs, cloth, vehicles, lights, faction trim) take the rolled palette, which the theme tilts but doesn't fix; the fall and age fade and dirty it; at most a faint tint in haze or grade [queued: next, after cities, sweep and Husks]

Creatures:
- [ ] Husks are the galactic threat: people hijacked by worm parasites out of every opening, puppet-like, grab the head and force the worms in, maggot vomit, arm strikes, worm masses in dens, Husk ships that crash into settlements; open helmet in a den means a quick horrible death (doc rev 657) [batch: seize by reach, any shot breaks it, dens with worm masses and no eggs; slice gives rim rates until worlds set how coreward they are; live struggle not played yet]
- [ ] Towering biomechanical walkers (owner's art refs, mood only): tall machines on long jointed legs striding through fog, half machine and half creature with something living visible inside; and an invasive red growth that smothers a world's own plants and water, leaving dead trees standing in it; ideas from the public-domain novel are fine, the album art's designs are not; and heavy armoured war walkers (squat two-legged mechs with domed shoulder guns and wide splayed feet) advancing through a burning city, as faction or machine-held war machines built from blocks like other vehicles [idea: for the incomprehensible, faction and late-game threats]
- [ ] Hollows become a lesser, rarer fungal infection of wildlife, spread on travellers' boots; names stay Husks and Hollows for now, owner to think on better ones; Hollow worlds stay as before but rare (doc rev 661) [batch, but the world layers have no Hollow flag yet, so no Hollow world rolls: layers pass]

Owner ideas, 8 November (design doc rev 664):
- [ ] Civic places: police stations, fire stations, schools, universities [batch: in cities; room contents listed for the dressing rules]
- [ ] Law enforcement and guard people (roles, gear, patrols, behaviour) [queued: bestiary round 2]
- [ ] Husk grab cooldown: after a grab breaks, no new grab for a short time, so surrounding Husks can't chain grabs [queued first: Husks round 2]
- [ ] Husk dens are never obvious from outside: an ordinary-looking place with Husks buried inside, deeper in and below ground; severity scales the signs, from a few hidden Husks and a faint vein (light) to rooms stuffed with worm masses and veins (severe) (owner, 8 Nov; doc rev 687) [queued: Husks round 2]
- [ ] Husk veins on walls, floors and ceilings around infestations [queued: Husks round 2]
- [ ] Husk worlds (owner idea, 8 Nov; doc rev 687): where the infection won; normal-looking at first from orbit and on the ground, until nobody acts right; cities stuffed with worm masses, strong psionic warping, Husk ships launching; rare on the rim, commoner coreward; a mid-collapse version with survivors among silent carriers; plus a second type, the completely Husked world, where nothing pretends any more: cities, ruins and ground grown through with worm flesh, all life Husk, the wrongness at full strength; the rarest, deep coreward (doc rev 689) [to develop: world layers, after Husks round 2]
- [ ] Body parts (hands, feet, part of a face) sticking out of worm masses to show they were people [queued: Husks round 2]
- [ ] Silent carriers: Husks that look, move, gesture and sound normal, worms emerging only when alone with someone; past a share of a settlement they behave like normal Husks; simple routines [queued: Husks round 2]
- [ ] Husk screening scanners at facility entrances on mid and high tech worlds; radio rooms, two-way sets, broadcast masts and dishes as dressing and decals [queued: decay and dressing pass]
- [ ] How people, animals and Husks behave around each other: who flees, hunts, guards, ignores; simple reactive routines [queued: bestiary round 2]
- [ ] Weapons and explosions: bombs, nuclear and antimatter blasts, disintegration rays, and how each damages blocks (shapes, craters, melted, vaporised) [queued: with the weapons and the BLAST and ORDNANCE upgrades]
- [ ] Dust storms on nuclear-blasted worlds carry radiation [queued: layers everywhere pass]
- [ ] Husks are mildly psionic, more so as they cluster and infest; that is where the reality warping comes from (compass drift, wrong shadows, rooms that don't fit, sound dying) (doc rev 679) [queued: Husks round 2]
- [ ] Design doc made consistent: Husks are the legacy of the collapse, Hollows the fungal infection, Hollow worlds rare (doc rev 685) [done]
- [ ] In the world layers data, `bloom` now means Hollow worlds and `hollow`/HOL ("something wrong here") means Husk-warped places; rename or remap in the layers pass [queued: layers everywhere pass]
- [ ] New iconography for Husks and Hollows, final picks (owner, 8 Nov): no Husk sigil; Something wrong here = the scribble plus room, needle and two shadows; Husks here = six regional marks; Hollow traveller marks = all four cap-bearing beasts; Hollow hazard G (beast gone to mould); spread-on-boots A; Husk hazard C and D; Husk group C; all five screening signs [finalising: docs/signs; then into the game's sign placement]

- [ ] Polar ice reads as a straight band from high, not a cap: needs a lobed, meandering edge with glaciers down valleys, sea ice breaking into floes, a gradual tundra-to-ice change, rounded off by the horizon sink (owner, 8 Nov) [queued: farland2 follow-on]
- [ ] Distant land doesn't draw: toward the horizon from the air the land turns to a flat smear and grey void (owner's annotated shot, 8 Nov) [running: proto/farland2]
- [ ] Cities: big buildings must not stand on sandy soil; towers and dense blocks need firm ground (rock, clay, firm soil), so sand, beaches and dunes stay open or get only light buildings on piles [queued: cities follow-up]
- [ ] Cities: each lot sits on a bare brown pad that reads as squares from the air; low-tech river piling reads as a fence; a thin bright column rises into the sky in two frames [queued: cities follow-up]

- [ ] Places don't read as connected and their ground is wrong (owner, 8 Nov): grass where there should be cracked concrete, eroded asphalt and concrete rubble. Sites need made ground between and around their buildings (yards, aprons, car parks, forecourts, service roads, paths) that wears with age into cracked slabs, broken asphalt with weeds in the seams, and rubble, never plain grass; buildings in one site joined by internal roads and paths, and sites joined to each other and to the road network, so a cluster reads as one place [queued: places ground job, with the cities follow-up]

- [ ] Galactic depth and start options (owner, 8 Nov; doc rev 691): every system gets a depth, 0 at an arm tip on the rim to 1 at the core, from its real galaxy position; depth feeds the world roll and drives factions, Husks and Husk worlds, the wrongness, incomprehensible glimpses, danger and finds; a new-game screen with presets and custom options (depth, type, theme, fall, mid-collapse, old and current eras, size, mode, seed); the same options as command-line flags for tests, and a test suite that sweeps depths, eras, types and falls with fresh random worlds in each [queued: second, after the blueprints tab]

- [ ] Houses (owner, 8 Nov): garage doors are tiny and every house looks the same. Garage doors at real size (single about 2.5 m wide, double about 5 m, about 2.1 to 2.4 m tall) and varied (panelled, ribbed, glazed, timber, roll-up); house design generated from combinable parts by culture, era, tech, climate and wealth: storeys, footprint, roof form (gable, hip, cross-gable, flat, mansard, A-frame, tiled, slate, metal), wall material (brick, render, weatherboard, stone, timber, half-timber), windows, porch, chimney, garage placement; regional families and cultures (stilt houses, courtyard houses, tiled-roof and pagoda roofs, adobe, chalets, cottages, terraces, modern boxes, earth-sheltered, domes); suburbs mix styles within a culture rather than repeat one model; plus rare odd one-offs that still come from the same parts (wave or shell roofs, a house on a water tower, homes sunk into hillsides, organic mushroom forms, saucer and pod houses on legs, faceted glass shells), canal-side timber houses on stone piers, and walled courtyard compounds (owner's photos) [queued: housing variety job, with places ground]

Galaxy and map:
- [ ] Starting worlds at the tip of a spiral arm [not started]
- [ ] Galaxy map: keep every star, but declutter; labels off except the selected one [UI round]

UI (after world, POIs and bestiary):
- [ ] Our sign set isn't in the game yet (checked 8 Nov): places show a generic box-built warning plate (a stripe or a crude radiation mark), and the HUD still uses text toasts for hazards ("Tornado on the ground", "STRUCK BY LIGHTNING..."). Needs: the approved signs, traveller marks, sigils and the Husk and Hollow set drawn as decals or textured plates in places, chosen from real room data and the place's occupants, worn and tilted by age, survivors' marks chalked near what they warn of; and the HUD hazard alerts as the matching icon with a timer and direction [queued: signs in the world and HUD]
- [ ] Blueprints tab in the Tab menu (owner, 8 Nov): the library of blueprints, Large and Small, with a 3D preview, its four numbers, cost and unlocks; project one to build (in Creative it places at once); save any owned structure as a blueprint; a starter set: Small (the skiff, a hover rover, a light fighter, a shuttle) and Large (a starter base, a hover tank, a hauler, a small capital ship), all built from blocks that exist, as data files. Design rule kept: blueprints live in the menu, never on the toolbar, and nothing in the early game steers toward them [running: proto/blueprints]
- [ ] Escape menu soon: save, load, quit, options for graphics, sound, keys and mouse sensitivity (even before save and load work) [batch: resume, save and load marked not yet, options, quit; graphics, sound, rebindable keys and mouse saved between sessions; real window and input not yet checked by hand]
- [ ] Handheld scanner, so the compass isn't cluttered with every ore [UI round; ore halving now]
- [ ] Hazard warnings as a symbol with a timer and direction, not a sentence [UI round]
- [ ] Menus waste space; more slots, more lists, more information [UI round]
- [ ] Broken characters (Â·, Â°C) in the suit terminal [batch: fixed], and labels overlapping (remote toolbar line, set-down hint) [UI round]
- [ ] Tech tree too spread out and too empty: each category's unlocks visible on one screen [UI round]
- [ ] Too few weapons and tech options overall [running: first 12 weapons, paused by the weekly limit]
- [ ] Two classes, Large and Small (doc rev 658): Large (Big Core) has Base, Hover and Flight modes; Small (Small Core) has Hover and Flight; tech tabs become Large and Small, with large hover tech a branch parallel to flight; large hover plates then work on a Big Core in Hover Mode (hover tanks, land carriers, mobile bases) [queued: with the UI round]
- [ ] Owner hasn't built much because the menus feel bad and present data poorly [UI round]

Sound:
- [ ] Wind and drone are sometimes perfect, sometimes grating or too droney: narrow their ranges [not started]

Design clarified: small vessels never auto-hover; only Hover Mode holds a craft above the ground (doc rev 657).

## Done in the test build (needs a check in play)

- [ ] E opens the full panel on every fabricator and constructor, with hopper, recipes and queue [main]
- [ ] Fabricators list only what they can make and you've unlocked; no deco on survival ones [main]
- [ ] M toggles the map [main]
- [ ] Fabrication twice as fast [main]
- [ ] Tornadoes wander, never chase the player, and wear out [main]
- [ ] Ore rocks solid and gatherable [partly: mined rock's collider fix is only in batch]
- [ ] JET gauge on the HUD [main]
- [ ] TEMP shows the air around you; body temperature on the You tab [main]
- [ ] Weather warnings below the compass labels [main]
- [ ] CPU extender can be unlocked, crafted and placed [main]
- [ ] Mouse recaptured when the menu closes [main]
- [ ] Red bolt in Shoot mode; blue beam in Gather and Deconstruct; compass distance on POIs [main]
- [ ] Comfortable -10 to 60 C, grace period, chip damage scaled by how extreme it is; bunker stays comfortable [main]
- [ ] Barren starts cost no health, even at midday (re-measure) [main]
- [ ] Slowdown near POIs (creatures, re-rigging) [main]

## In progress

- [ ] Rebuilt roadside ruins at real scale from the player's own blocks, about 25 a world (was 150), placed with reasons [partly: in batch; gable ends stepped, wayside stops not shown, block trees on big POIs, claiming ruins]

## World bugs

- [ ] Space transition: one continuous climb with a hard 1,060 m cap and the air shroud [batch]
- [ ] Rings hide the planet's surface behind them from space [branch: space, uncommitted]
- [ ] Real solar systems: planets in true orbits and positions, seen correctly from the flat world and from space; real goldilocks zones, with a planet's type following from its star and orbit [partly: system model in batch; planet type doesn't follow star and orbit yet]
- [ ] Rings up close resolve into fields of little asteroids you can fly through and mine, sharing the rock field system with asteroid belts [not started]
- [ ] Distant planets and moons show their real surface detail from afar, matching what you'd land on [batch; five surface types, not 40; blocky close up]
- [ ] Hitch at the crossing: hand-over frames 57 to 111 ms, one 288, up from 12 to 15; sky tracking took 72 ms of the worst; measure on a quiet machine after the gate [not started]
- [ ] Real starscape: today's stars are random shader dots and no galaxy exists; build the seeded catalogue the doc describes (20,000 to 35,000 stars) as one source of truth for the sky and the star map, so every star shown is a place you can travel to; nebulae in the sky are real regions with systems inside them [running: galaxy]
- [ ] Celestial bodies are real, not decoration: same direction, size and phase on the ground, climbing and in space; the moon must not vanish on the climb [partly: one system in every sky is in batch; moon on the climb is uncommitted in space]
- [ ] Owner's direction for that climb: the high atmosphere's own colour shrouds the crossing, then fades into space [batch]
- [ ] TOP PRIORITY: seeing through blocks must never happen; ground curving in around structures [batch]
- [ ] Hollow growth blocks look bad: pink cubes need a real redesign, organic and non-blocky [not started]
- [ ] Missing wall faces: see-through walls, hollow from outside, no undersides, pillar tops, on ruins and full POIs; NOT fully fixed: the owner spotted it again in the derelict lab shot taken from the world fix build [partly: fixes in batch; lab-shot recurrence unchecked]
- [ ] Ground curving in beside basement walls; terrain flush against walls [batch]
- [ ] Gaps or tearing on the sides of buttes, mesas and spires [branch: proto/terrain-gaps; ray test 0 of 58,346 through, was 92 of 31,002; held back until the landing failure is checked against the gate]
- [ ] Caves and sealed interiors are pitch dark without a light, not lit by the outside; daylight falls off from real openings [running: proto/darkness]
- [ ] Invisible floors and walls: drawn terrain and collision must match [batch]
- [ ] Nothing floats: ruin chunks, overgrowth, deco, ore rocks, plants, scattered rocks, road slabs over potholes [batch]
- [ ] Ore slabs and road slabs bend to the ground [batch]
- [ ] No block trees on ruins; the planet's real flora grows there [partly: in batch; flavour ruins get no trees yet]
- [ ] Rocks, ore tells and debris use each world's own colours, muted, fewer tells in open fields [batch]
- [ ] Ruined buildings look better: blocky collapse needs real damage shapes; broken pillars and walls capped with rebar and jagged concrete [batch]
- [ ] Decay detail from the owner's ruin photos: pancaked floors and exposed rooms in cross-section, bent rebar at every broken concrete edge, slabs and rubble heaps, hanging ceiling tiles and cables, mould and water stains on walls, standing water in basements and stairwells with reflections, plants growing indoors under skylights and broken roofs, dead leaves drifted inside, offices and malls with their furniture and clutter left behind; amount from the world's age and fall [queued: decay pass after cities]
- [ ] Tunnels and caves don't breach the surface; designed cave entrances in sensible spots [batch]
- [ ] Buildings, details and caves loaded before the player arrives; no pop-in; Tab menu and alt-tab clue [batch]
- [ ] Cave critters and enemies never spawn beside or behind the player; repeat spawns capped [batch]
- [ ] Camera vibration while moving near wrecks [not started]
- [ ] Missing deco parts: all luggage has no bag, wheels have no caps; audit every piece [partly: luggage and wheels fixed in batch; full audit not done]
- [ ] Unexplained cones when shooting (not reproduced) [not started]
- [ ] Real hitches from garbage collection near creatures; monsoon frame drops [partly: GC fixes on overhaul; monsoon drops untouched]
- [ ] Random-world failures from the merge run: PlaceTest seed 'no route' (walker can't reach a cache; 50 of 52 scenes passed), SliceTest seed 7937604306141837110 (cutting a plant gives +0) [partly: PlaceTest walker fixes in batch; plant +0 case unchecked]
- [ ] Review PlaceWalk and OrbitTest changes made to fit random worlds [not started]

## Roads, rail, power and wrecks

- [ ] More roads, highways and bridges; every POI and ruin cluster connected [batch]
- [ ] Road layout: straight, no wobble; roads form grids with small roads off bigger ones; only tracks spread out from a hub [batch]
- [ ] Roads look like smudged dark paint with jagged blurry edges, from the air and up close; need real surfaces and crisp edges by tech and age [running: proto/roads, round 4; crack grid gone, but on the S-bend ("a mess") the cracks scribble, seams cross the road, grass and sand cover too heavy, potholes read as splats]
- [ ] Only dirt tracks switchback; paved roads curve gently with a minimum radius by class; power poles never stand in roads; no sharp or 90-degree bends anywhere, and town corners are rounded too (owner: "Rounded, even in town") [running: proto/roads; poles and junction kerbs in batch, elbows still showing]
- [ ] Railways (owner's photos): no rail lines generate at all right now (the railway check finds 0 lines on 8 worlds); lines with ballast, sleepers and rails, gentle curves with a wide minimum radius, single and double track; cuttings with stone or grassy walls, tunnels with stone and brick portals, embankments and viaducts; level crossings by tech (ungated with a cross sign, barriers and lights, rubber crossing decks); stations, yards fanning into many tracks with points, depots and freight sidings with wagons; stations with platforms, canopies on posts, water towers and plazas; industrial yards beside works and silos; on fallen worlds: weeds and saplings between rusted rails, rotten sleepers, buckled and warped track, rusted locomotives and carriages left on the line, some toppled, burnt out or graffitied, scrap lines of dead engines, all generated from parts like the vessels [queued: after the roads agent, since both use the road and rail network]
- [ ] Bridges of every kind, chosen by span, load, ground and the world's tech and age (owner's photos): stone single and multi-arch bridges and culverts through embankments; steel truss through-bridges, pony trusses and arches; suspension bridges with towers, main cables and hangers, and cable-stayed; rope and plank footbridges that sag and sway; tall multi-arch rail viaducts over valleys, straight or curved; all with real piers and abutments on ground, nothing floating, broken spans on fallen worlds [queued: roads agent, after highways]
- [ ] Road markings stop before structures [not started]
- [ ] Rail: long straights, gentle curves, ending at a station or depot [batch]
- [ ] Pylons on their own straight legs, 30 to 50 m tall, with collision; poles too [batch]
- [ ] High-tech power, all three (owner: "All three"): transmission dish relays hop between hilltops to receiver dishes at towns and bases; receiver fields under a beam from an orbital power station; buried conduit as lines of hatches and vent posts; mixed by age with old pylons; collapsed worlds have dishes knocked out of line; live beams shimmer in dust or rain, are deadly to fly through and are always signed (doc rev 655) [queued: roads agent, after bridges]
- [ ] Vehicle wrecks at real scale, more common along roads, salvageable; consider building them from small-grid blocks instead of bespoke models; look from the owner's photos: cars, trucks, buses and coaches generated from parts (body shell, cab, grille, lamps, wheels or bare hubs, glass), sunk and settled into the ground, moss carpeting roofs and bonnets, saplings and brambles growing through and around them, doors and panels missing or sagging, flat or missing tyres, peeling paint in layers over rust, cracked and missing glass, gutted interiors with wiring hanging; amount from the world's age, climate and fall; pile-ups where a highway jammed: vehicles crumpled nose to tail and jack-knifed trucks across lanes, burnt-out clusters with blackened shells and melted debris, loose panels, tyres and glass strewn across lanes, survivors' spray marks on checked wrecks (ties into traveller marks); crashed aircraft: broken fuselage sections and wings in fields and forests, half-swallowed by ferns and trees; fresh small-plane crashes on mid-collapse worlds: nosed in with a crushed cockpit, flipped on its back with wheels up, a wing torn off, propeller bent, scraped furrow behind it [partly: in batch; not built from blocks; decay look queued]

## Creatures and people

- [ ] Half the wildlife or fewer, then scaled by how livable the world is: lush worlds busy, barren or lava worlds sparse [batch]
- [ ] Far fewer infested POIs, especially on start worlds; caps per room and per POI [batch]
- [ ] POI creatures fit the spaces; large ones only where they can turn [batch]
- [ ] Line of sight, vision cone, memory, hearing as a separate sense, awareness meter [batch]
- [ ] Sight depends on darkness, so shadow hides you and a torch gives you away; some creatures and peoples see better in the dark; echolocation and scent carried on the wind as senses; for example a blind stalker with big ears in a dark cave [not started]
- [ ] Weight: size-scaled acceleration, braking and turning [batch]
- [ ] No clipping through the player [batch]
- [ ] Combat feedback when hitting and when being hit [batch]
- [ ] Blood splatter; swarm critters pop in a burst [batch]
- [ ] Corpses: no ring, looted ones darker, carrion eaters feed on them [partly: carrion eaters exist; no-ring and darker looted corpses missing]
- [ ] Killed flying creatures fall along an arc [not started]
- [ ] Flying animals never hold still in the air: always moving forward or gliding; attacks are swooping passes; only small hoverers may hold still, with rapid hummingbird wings to match [batch]
- [ ] Land animals float at most, though drinking at the water's edge is fine; aquatic ones stay in water; amphibious ones around shores [partly: land animals float at most (weather2); aquatic and amphibious not built]
- [ ] One bestiary module that all critters, beings and robots come from (doc rev 580) [batch]
- [ ] Groups of people run slowly [batch: one body per person, built off the main thread; 20 people about 0.3 ms; owner to recheck]
- [ ] People react to outsiders differently, leaning by tech level and the world's state; nobody reacts on the same instant (young later, pack leader first, robots in sync); young play; gestures broad and held, readable at 30 to 70 m [batch]
- [ ] Creatures fit their environment: no reptiles on ice worlds, woolly and fat-layered bodies there, scales and thin skin on hot worlds; survivors' outfits match climate and hazards [batch]
- [ ] Easy feel wins from the creature research, including townsfolk [partly: townsfolk and combat feel in batch; rest of list unchecked]
- [ ] Flavour animals as effects, not AI: butterfly-like creatures, gnats and fireflies near the player, flocks of birds high in the sky; set by biome, time of day, weather and how livable the world is, different on every world [not started]
- [ ] Survivors with personality by role: trader, guard, farmer, medic, scavenger, elder, child, drunk; each with their own routine, look and gear, still simple, and no dialogue; raiders with spikes, bandoleers and mohawks, by look alone you can tell who's who; reuse the creature procgen so humans and human-likes get different accoutrements with every generation [partly: builds, founders, contrast and decorations in batch; routines per role unchecked]
- [ ] People make generated wordless speech: murmurs, calls and shouts, distinct per person, with a hint of dialect per world [batch]
- [ ] Mutation as a bestiary layer on anything alive, including people: mild to monstrous, with occupancy setting whether they're friendly, raiders or feral; mutated throats can give a vomit attack from the mouth, sharing the creature spit attack, coloured from the body's own chemistry, such as red or green-yellow [batch; raiders in towns don't vomit yet]
- [ ] Alien peoples from the bestiary, not only humans: the old civilisation was mixed-species, so their survivors live everywhere, often side by side; varied body plans with the same roles, gestures, voices and outfits [later] Plan in docs/alien-peoples.md; owner decisions 8 Nov: Vharn are insect-like (carapace, mandibles, antennae), and every people is height-capped to fit doorways (doc rev 662).
- [ ] Wasteland people: towns reachable from starts, roaming survivors and scavengers [batch]
- [ ] Survivor settlements from the owner's photos: shanty towns packed up hillsides and riverbanks, built from corrugated iron, timber, crates and salvage; rusted roofs held down with bricks and tyres; stovepipes, washing lines, tarps and awnings; muddy lanes between; scrap yards and tyre piles at the edges; poles with tangled wires; on higher-tech worlds, indoor farms in old buildings (stacked hydroponic racks under purple and white grow lights); built from the player's blocks and salvage pieces, density and materials from the world's tech and fall; markets (souk lanes under awnings and cloth, covered alleys lit by lamps at night, roadside stalls of crates and poles hung with produce, umbrellas along a dirt road, flags and wires strung overhead); refugee and evacuation camps (tent grids on a main avenue seen from the air, tents inside sports halls and stadiums, tents pitched in city rubble); walled compounds with watchtowers, razor wire and floodlights [queued: with cities]
- [ ] High-tech survivors and factions use vehicles and weapons the player could construct with the right unlocks; low and mid-tech peoples use the gear of their own era; enemy craft built from the same blocks and blueprints, enemy guns the same weapons [not started]
- [ ] Outfits from the owner's clothing photos, built in layers like real cold-weather kit (base, insulation, wind, shell) so warmth comes from what is worn: fur-ruffed hooded parkas, balaclavas, buffs and goggles, mittens and mukluks with gaiters, belts with pouches and knives; desert robes and long wraps with the face covered against sun and sand; work coveralls and hi-vis with reflective bands, hard hats, masks and safety glasses for industrial workers; protective gear on hazard worlds (hooded suits with rubber gloves and overboots, full-face masks with one or two filter canisters, masks fed by a hose from a pack, helmets over masks, load vests with pouches); traders' kit and wares (bales, sacks, rugs, chests, scales and goods laid out on cloth, felt-walled tents and yurts with rugs and stacked bedding inside); worn, stained, patched and faded by age; colour and cut from the world's culture and tech, no brand marks or text [queued: bestiary round 2]
- [ ] Bestiary from the owner's animal photos: cold-world bodies (thick fur, blubber, tusks and whiskers, white or mottled coats, furred feet, woolly hump-shouldered grazers); hot-world bodies (long legs and humps for storing fat, pale sandy coats, huge ears for shedding heat, big curled horns on rock climbers, lizards and coiled snakes patterned to match the sand); sentries that stand upright on a mound to watch while the group feeds; herds that stream across plains in their thousands and pour down river banks in dust; packs that move and bicker as a group; flocks in V-lines and murmurations whose swirling cloud makes shapes; hatchlings racing to the sea; and real oddities as body-part ideas: frilled necks, feathery external gills, armour of overlapping scales, huge forward eyes, stalked compound eyes with raptorial arms, star-shaped tentacle noses, see-through skin, finned ears, glass-like bodies [queued: bestiary round 2]
- [ ] Husks: worm-like protuberances coming from their orifices, to make them creepier [batch]

## Weather and sky

- [ ] Setting suns not blinding (reported twice) [main]
- [ ] Rain falls everywhere, stops on contact, splashes, ripples on water, none indoors or underwater [main; softer distant rain shaft and ring ripples in batch, owner likes them]
- [ ] Distant rain veil must not clip through terrain in front of it [running: rain agent]
- [ ] Distant rain is a soft curtain, densest at the cloud base and fading toward the ground; fingers, backlit glow, spray at the foot (owner's photos) [running: rain agent]
- [ ] Falling rain: fine, faint, layered drops, never pure white; slant follows the world's wind, nothing camera-relative [running: rain agent]
- [ ] Storm cloud hidden among other clouds, no lone marshmallow; tornadoes twisty and varied (rope, column, cone, wedge) with a lifecycle (owner's photos) [partly: storm grows from the deck in batch; tornado shapes running: clouds agent]
- [ ] Fog that follows the land: pooling in valleys and low ground with hilltops and treetops rising out of it, thin layers lying on slopes, banks rolling in from the sea, dense fog where structures fade to silhouettes, light shafts through forest fog; set by humidity, temperature, time of day and the ground (owner's photos) [queued: rain agent, after the rain]
- [ ] Space look from the owner's photos (NASA, ESA and Webb imagery as reference): worlds from orbit with shallow seas glowing turquoise, cloud cover and sun glint; a soft terminator where day fades through dusk colours into night; a thin glowing atmosphere limb (blue by day, a green or orange airglow band at night) with city lights on inhabited night sides and lightning flashing in clouds; rings close up as fine grooves, ripples and bright and dark bands with scattered particles; comet and asteroid surfaces of cracked dusty rock, scattered boulders, sharp shadows and a hard horizon; nebulae as layered glowing gas with dark dust cliffs and pillars, filaments, rings and shells around dying stars, and bright young clusters with diffraction spikes [queued: space look pass, with the space POIs]
- [ ] Aurora at night: curtains and arcs of light with vertical rays, folding and rippling slowly, brightest low and fading upward; colours from the world's air (green, red, pink, violet, blue, or stranger on alien air) and its star's activity; seen toward the poles of the map, more often on worlds with strong fields or active stars, none on airless worlds; reflected in still water and lighting snow faintly (owner's photos) [queued: small sky-shader job]
- [ ] After rain: wet surfaces darken and shine, with real reflections of sky, lights and signs on roads and pavements; puddles that form in dips and ruts and dry out over time; drips from leaves and eaves; mist rising off warm wet ground; breaking cloud with the low sun lighting cloud edges; rainbows opposite a low sun when rain is still falling there (correct position, always opposite the sun, double bow sometimes); ground dries from the edges with temperature and wind (owner's photos) [queued: rain agent, after fog]
- [ ] Heat shimmer and mirages: on hot days the far horizon wavers; at low grazing angles over hot asphalt, sand and salt flats the ground shows a mirage (a shimmering band that mirrors the sky and whatever stands on it, receding as you approach); set by heat, sun and surface, strongest at midday on hot worlds, none in rain or at night (owner's photos) [queued: small screen-space job]
- [ ] Volcanic eruptions and ashfall: billowing ash columns from active vents, spreading into a flat umbrella cloud downwind; ash falling like grey snow downwind, heaviest near the vent; ash settling on everything (roofs, wrecks, trees, ground) as a grey blanket with drifts, using the same settling system as snow; lightning in the plume; on lava and volcanic worlds, rolled from the world (owner's photos) [queued: with snow, sharing the settling system]
- [ ] Snow and blizzards: flakes that drift and tumble (big soft flakes in calm, fine driven snow in wind), whiteout where the road and horizon vanish, snow streaming along the ground, lamps and windows glowing with halos through falling snow; snow that settles: caps on roofs, branches and parked wrecks, drifts banked against walls and in the lee of things, ploughed or trodden tracks; build-up and melt over time from temperature; frost: hoarfrost furring grass, reeds, branches and fences on cold still mornings; snow streaming in thin snakes across roads in wind; packed snow and ice on roads with wheel ruts; frost and snow on people and animals (frosted beards, fur and hoods, iced scarves, breath steam) (owner's photos) [queued: next free slot]
- [ ] Dust storms as an approaching wall of billowing dust coloured by the ground, then a dense haze inside (owner's photos) [queued: clouds agent]
- [ ] Fire storms with real fire: ground fire fronts spreading through dry flora with the wind, burning trees and embers, smoke lit orange from below, fire whirls as thin twisting ropes of flame (tornado shapes with a flame look), everything tinted orange inside; built as the FIRE upgrade, first in the owner's engine order, which also gives the Pressure Thrower ground fire (owner's photos) [queued: after the first 12 weapons]
- [ ] Lightning that hits weird: long sideways bolts that crawl kilometres under the cloud base before striking, strikes at odd angles on hillsides, cloud-to-cloud crawlers, forks and flickering re-strikes; a strike can start a ground fire on dry land; at night the flash lights the whole cloud from inside in violet and blue, throws the land and trees into silhouette for an instant, and shows the storm's shape; spider lightning spreading across the base in webs; distant flashes glowing inside clouds over the horizon; seen from the air, strikes over lit towns (owner's photos) [queued: with the fire job]
- [ ] Storm cells: rain, lightning and tornadoes come from clouds [main; funnel top must always meet the tower base: running, clouds agent]
- [ ] Mixed cloud types; nicer lightning [main, but every sky shows the same streaky clouds: running, clouds agent; mix to come from the world's air and weather]
- [ ] Rain and storm clouds a shade darker by severity [running: clouds agent]
- [ ] Clouds stay flat until high up [running: clouds agent]
- [ ] Howling wind sound at openings when it's windy: cave mouths, doorways, broken walls [main]
- [ ] Tumbling debris in the wind, if cheap: leaves, litter, tumbleweed by biome, near the player only [not started]
- [ ] Puffs of dust kicked up by wind; dust devils: harmless small vortices on arid worlds, in clear sunny weather with no cloud above, wandering across dry ground, roads and towns (owner's photos) [queued: clouds agent, with the dust walls]
- [ ] Caves from the owner's photos, generated by rock type and world: limestone caves (chambers with stalactites, straws, flowstone curtains, columns, shelf-like rimstone and helictites, still clear lakes reflecting the ceiling), lava tubes on volcanic worlds (long round tunnels with ledged walls, roots hanging through the roof, puddles, a skylight where the roof fell in and, on active worlds, lava still flowing below), crystal caverns (giant pale beams of crystal criss-crossing a chamber, climbable, and fields of fine crystal straws over turquoise water), and sea caves cut into cliffs (an arched mouth framing the sea, a roof hole open to the sky, boulders and moss on the floor, waterfalls pouring past the mouth, tunnels through a headland to the next beach); darkness inside from the sky-exposure system, lit by the player's lamp and whatever light reaches in [queued: landform job]
- [ ] Ground surfaces from the owner's photos, in the terrain shader by soil and world: wind ripples on sand (wavy, forking, small ripples riding on bigger dune waves, aligned with the world's wind, sharp in low sun); lava (active: glowing rivers and braided flows with a dark crust, fountains in craters, steam where it meets water or rain; cooled: ropy pahoehoe folds and swirls, rough clinker rubble, plants in the cracks on old flows); tundra (autumn red and gold scrub over rock, frost needles in wet ground, polygon patterns of ice wedges under snow, permafrost banks where sod overhangs exposed ice, slump craters where it thaws); alpine scree (fields of angular blocks below cliffs, fan-shaped cones spilling into valleys and feeding braided gravel rivers, shale slopes of thin plates, lingering snow patches); badlands (rounded mounds and fluted ridges striped with layers, hoodoo pinnacles, grey gullied clay, banded hills rising out of grassland, the stripes coloured by the world's minerals) [queued: landform job]
- [ ] Alien world mood from the owner's classic sci-fi art (mood only, never copy any artist's designs): skies coloured by alien air and odd stars (purple, green, orange, yellow haze) with huge moons and ringed planets close overhead; still water mirroring them; alien rock (swept organic arches with holes worn through, brain-coral textured plateaus, clusters of dark needle spires, frost peaks lit gold); alien flora (tall twisting stalks ringed with shelf fungi, mushroom caps on bending stems, drooping umbrella trees, trumpet-mouthed plants, pod towers draped with tendrils, giant crystal growths); a vast crashed hull half-buried in dust; from the Roger Dean set (ideas only, his style is distinctive and must not be imitated): great natural rock arches spanning valleys, stacked eroded pillars, mushroom-shaped table rocks standing in still water, paths spiralling up rock stacks, wind-twisted pines, rock ribs looping in and out of still water, huge veined domes like living hills, misty gorges of pines and stacked boulders; floating islands only inside a clearly signalled gravity anomaly, never as plain terrain; strangeness rolled per world [queued: alien flora job and landform job]
- [ ] Strange real landforms as terrain features, rolled per world (owner's photos): salt flats (white crust in hexagonal polygons, a mirror sheet of water after rain), travertine terraces (stepped white mineral pools of turquoise water down a slope), stone forests (dense fluted limestone pillars with grass and trees between), glacier ice caves (scalloped blue walls lit through the ice, icicles, a stream on the floor, openings to daylight), sulphur springs (acid-yellow and green pools fringed with orange and white crusts, knobbly salt-and-sulphur mounds and stepped crust terraces, hazardous), banded hills (ridged hills striped in red, orange, yellow, green and blue layers, the bands following the slopes and showing in plan from the air), and slot canyons (narrow winding sandstone passages with smooth flowing walls, glowing orange where light bounces down, a single beam of sun reaching the floor at midday) [queued: landform job after the sky and rain work]
- [ ] More hazards: fire tornadoes on hot worlds, water spouts over water, earthquakes (shaking, dust, rockfall), and chasms as a landform [partly: fire storms in main; fire tornadoes, spouts, earthquakes, chasms missing]
- [ ] Exposed in a blizzard or fire storm drains health fast; a dug hole deep enough counts as shelter [main]
- [ ] Climate control node: placeable, built early, fuelled, steadies the temperature in a small radius [main]
- [ ] Wind and tornadoes can't touch anything in shelter [main]
- [ ] Trees and wildlife are thrown and torn up, leaving a trail of destruction [main]
- [ ] Ice water, beside lava and in lava drain health fast [main]
- [ ] Proper underwater state [main]
- [ ] Underwater look from the owner's photos: kelp forests (tall swaying stalks rising to a golden canopy, sun shafts through it), reefs on warm worlds (soft and fan corals in pinks, purples and orange, schools of small fish), light that fades with depth to deep blue and then black, god rays and caustics from the rippling surface overhead, the surface seen from below as a bright wavy mirror; rivers and lakes clear over stones or murky green-brown with drifting particles and short visibility, set by sediment and life; sunken wrecks (aircraft on the seabed, ship hulls listing on sand, everything encrusted with coral and sponges, fish around them, rusted hulks breaking the surface in shallows) [queued: water job]
- [ ] Ore tell rocks too bright and too many in open fields (seen in storm shots): tone down colour and count so they read as subtle hints [batch]
- [ ] Aquatic and amphibious species don't exist yet: add a few [not started]
- [ ] Tree trunks must reach their foliage: a weeping tree's trunk stopped short of its canopy [batch: every trunk, limb and crown joined for every species; owner: foliage looks great]
- [ ] Plants respond to rain, if cheap: a small bow and a wet sheen [not started]
- [ ] Procgen flora per world: every world rolls its own tree and plant designs (shape, height, canopy, colour), even on two temperate worlds, and different regions of one world have different trees [batch]
- [ ] Flora repeats: meadow worlds show the same plants over and over, and other world types likely do too (owner, 8 Nov). Fix in the flora round: more species per habitat, every plant varied per instance (size, lean, branching, crown shape, colour and age), clustering and mixing so stands aren't uniform, regions of one world differing, and two worlds of the same type looking different; measure it with a check on how many distinct shapes are in view [queued: flora round, with alien flora]
- [ ] Weird alien trees: strangeness rolled per world from the world layers (now fixed per world type), and more strange body plans from real oddities in the owner's photos: dragon's blood umbrella crowns on radiating branches with red sap, gill-ribbed fungal canopies, swollen bottle trees flowering pink, baobab giants with tiny crowns, welwitschia's two endless strap leaves in a shredded heap with cone stalks, boojum spikes that arc and loop, grass trees with a fibrous skirt on a black trunk; plus bulb clusters, tendrils, coral fans, balloon pods, spirals [queued: alien flora job, next free slot]
- [ ] Denser flora, cheaply: grass, bushes and scrub by biome, drawn as instanced patches near the player and thinning with distance [batch]
- [ ] Water features with it: neighbouring swamps, water run-off, pond biomes, wider riparian zones, underwater flora, waterside flora such as reeds [not started]
- [ ] Rivers and coasts from the owner's photos: waterfalls where rivers drop over a step (single plunges, wide curtains, terraced cascades, many spouts down a canyon wall), with mist plumes, spray and rainbows in sun; rapids over rock ledges; deltas and estuaries at river mouths (braided silt channels, branching tidal creeks, sandbars, wetland islands); sea cliffs with layered rock, arches, stacks and caves cut by waves; rock platforms and tidepools with clear water, barnacles, weed and algae at the shore; mangrove forests on warm muddy coasts and river mouths (dense low canopy on stilt roots, brown or milky channels winding between, sand flats at low tide); rivers winding through jungle canopy; frozen lakes on cold worlds (clear black ice with white bubbles trapped in stacks, pressure cracks filled with snow, frost feathers in the ice, walkable); flooded towns on worlds where the sea rose or a river broke its banks (brown water to the eaves of the lower streets, roofs and treetops sticking out, cars half-sunk, debris floating against fences), set by the world's fall and terrain height [queued: landform job]

## UI

- [ ] Tech menu rebuilt: branches in rows, tidy lines, path highlight, names that fit [batch]
- [ ] Tech tree tabs unified: Base & Capital, Small & Hover [batch]
- [ ] One hierarchy that works both ways: galaxy, system, high orbit, world, with the map zooming down and back up through each level, and every level reading the same real data [running: galaxy]
- [ ] Map: terrain at every level, discovered POI markers, spaced labels, delete your own markers [batch]
- [ ] Compass marks discovered POIs, nearest few [batch]
- [ ] Remote toolbar with origins, greyed out of range, ammo from containers [batch]
- [ ] Storage menu: drag between containers, Ctrl and Shift select, partial stacks [batch]
- [ ] CORE tab: inside any core's radius, its management screen is available from the Tab menu; pick between cores in range [batch]
- [ ] Inventory icons show each item's actual model [not started]
- [ ] 3D item icons with short names [batch]
- [ ] Universal deco block with a gallery; theme picker in multiple rows, not one long row [partly: in batch; placed deco has no collision]
- [ ] Light colour and animation per light block and deco (steady, pulse, flicker, strobe, colour fade), settable by the player and used by POIs for mood and lights painted onto block faces with the paint tool, using the same settings [batch: player panel, modes, glowing paint and the places call; places still to use it (running: rollout); deco lights not done; painted faces cast no real light]; later: in the universal deco block
- [ ] More interior lights in deco: ceiling lights and strip lights [batch]
- [ ] Iconographic signs and labels, inspired by the look of Alien's Semiotic Standard: a colour-coded pictogram set placed from real room data, as block stencils and deco signs, varying by culture and tech level plus terrestrial hazards such as rockfall, lava and thin ice, and biohazard; a separate family of hand-made traveller marks in the spirit of hobo code, including a warding mark for the Hollow; draft 1 approved by the owner, 56 signs and 17 marks in `docs/signs/`; draft 3 approved: 81 signs and 27 traveller marks, six faction sigils final (Wing-cases, Kessari original, Taken corner, Split coin, Kill tally, Lantern), deck markers awaiting review (restyle rejected); not in the game yet; signs also hang as plates on chain link, brick, wire and barbed wire fences, properly fixed [running: signs]
- [ ] Gather beam stays on the barrel while firing and moving [batch]
- [ ] Text encoding bug: 'Â·' shows in game text on the YOU tab and elsewhere [not started]
- [ ] World map should default with the poles at top and bottom, not scrolled so the polar ice runs through the middle; sea ice has a blocky edge [not started]
- [ ] Faction takeover of old signs: don't stencil a sigil straight over a sign, it reads as mud; paint a rough block over the sign first, then stencil on that, or hang the sigil beside it [not started]
- [ ] POIs reveal in proximity and stay marked; a handheld scanner, free from the suit fabricator from the start, pings distant POIs and ore deposits, marked on the compass for ten seconds then fading; the compass shows only the bunker until then, and things stay marked on the HUD and map only once you've been near; a stronger scanner block for small and hover vessels, and the strongest for bases and capital vessels [not started]
- [ ] Distant land not drawing at high altitude, only water; speed up objects loading in [running: proto/farland]
- [ ] Compass crowded by ore labels: solved by the scanner rule, nothing shows until pinged or visited [not started]
- [ ] Alerts as symbols: weather warnings are a sign silhouette with a timer on its left and no description; attacks flash an animal or people silhouette at the left with no alert sound, only the damage vignette and the attack's own sounds; no explainers, including the cryo emergence; attack and weather icons still show over the Tab menu and the Esc options menu; the game never pauses, so every menu stays see-through [not started]
- [ ] Tab menu background more see-through, especially the grey parts, for awareness of what's beyond it, while staying dark enough for text and icons to read [not started]
- [ ] Quieter UI: it labels everything as it happens; cut pop-up messages to what the player truly needs, and never label mood or mystery [not started]
- [ ] Debug options on the start screen: planet type, pre and post-collapse tech level, starport class, theme, occupancy and creature density, so everything can be checked by hand; plus a way to jump to any POI [not started]

## Gathering and building

- [ ] Hover bike from the suit fabricator; get on and off like any vehicle; treats water as ground, like hover vehicles (essential) [batch]
- [ ] Trees give materials with each hit, fall over once depleted, leave a stump, and the trunk fades [batch]
- [ ] Gather mode slowly chips at everything it points at, never instant deletion, including building blocks found in the world [batch]
- [ ] Sustained drilling, resources trickling in, blocks shrinking into their neighbours; faster tools by level; own blocks stay instant [batch]

## POI and world overhaul

- [ ] Hollow mystery: ordinary POIs lose the loud pink cubes and neon monster stuffing; the Hollow is subtle, unexplained wrongness; neon creatures and Bloom growth stay as their own Bloom-world biome; growth on buildings only on Hollow worlds or inside consumed patches of terrain, like a splotch on a temperate world (doc rev 564) [not started]
- [ ] Extra world variables if simple and impactful: time since the fall, who ran the world, architecture, the moment it fell, what's still running, native life (no Earth in the setting), terraforming state, who's reclaiming it, day length and seasons [not started]
- [ ] Dramatic rock formations as landmarks, ideas from the owner's reference photos, not copies: buttes on scree skirts, mesas, stepped spires and volcanic necks, columnar towers, hoodoos with cap stones, banded beehive domes, inselbergs, cliff-top pulpits, natural arches, and rainbow-striped strata; crystal worlds with sharp angles, faceted ridges and glass-like surfaces [not started]
- [ ] Geography less like a noise map: continents, ranges and rivers with real logic, towns placed where people would settle, drawing on how Dwarf Fortress builds worlds, without its history simulation [not started]
- [ ] Region planning before placement [batch]
- [ ] Expanded base world types: list finished, 40 types in `docs/world-themes.md` with the first 8 to build chosen; none built yet [plan only]
- [ ] Apocalypses change the terrain itself: nuclear craters baked into geography, and similar [plan only]
- [ ] Rare worlds in the midst of collapse: hurt by the galactic collapse and falling now because of it, through causes as ordinary as barbarism or civil war, not only the Hollow or invasion; first as a snapshot, later moving on while the player is away; needs a per-world time since the fall (today POIs have three fixed ages), the apocalypse layer, occupancy and people [not started]
- [ ] Apocalypse as its own layer: how each world fell (nuclear war, plague, famine, grey goo, invasion and many more), separate from themes, with visible evidence even on recovered high-tech worlds [plan only]
- [ ] More scattered rock and bits of ruin; clearings where civilisation once was: old foundations, a lone wall or chimney in a gap in the trees [not started]
- [ ] Scatter debris that bridges areas: rubble, fences, signs, litter and old foundations fading out between places, so nothing just stops at a pad's edge [partly: litter settles past pads in batch; nothing bridges places]
- [ ] Worlds come from combinations of layers (world type, theme, features, presences), not bespoke entries; split the themes list into combinable parts where it hard-codes one combination [batch]; no cap on themes: add more whenever they're truly different
- [ ] Occupancy separate from layout: the same hospital can be full of nesting bugs, zombies or violent scavengers, completely empty, or mostly empty with a few scavengers passing through, chosen by the world's theme; mixed occupancy too, such as bugs below and scavengers above; robot and cyborg occupancy [batch]
- [ ] Every world feels different: a theme from the big list (`docs/world-themes.md`) guides its plan, POIs and contents, with some POIs breaking from the theme [plan only]
- [ ] Ruins fit the world's air, not the kind of body: moons with breathable air can be fully inhabited; airless worlds get domes, sealed modules and tunnels, no gabled neighbourhoods; atmosphere filters POI families and building styles; on vacuum worlds survivors live in sealed interiors and wear suits outside; robots can work outside; no animals on the surface except super rare space animals [not started]
- [ ] POIs on the 2 m grid, using the player's blocks and shapes [batch]
- [ ] Exchange market buildings still have a colour mosaic after the wall fix [not started]
- [ ] Scale: houses 2x, facilities 4x, some 5x, well beyond double outside too [batch]
- [ ] Buildings must read from a distance: no per-cell colour mosaic; clear materials, floors, doors and windows [partly: in batch; ruined buildings still blocky, freeholder door frames loud]
- [ ] Nothing templated: the 7 faction families move off hand-made kit modules onto the architect, right after world themes and POIs are playable [running: rollout; market, hospital, water works and bunker converted, six kit families left]
- [ ] Interiors specific to each place type, not the same basement [partly: new large places in batch; room library not widened yet]
- [ ] Presence: huge brutalist industrial complexes in the spirit of 1970s and 80s sci-fi, with towers, pipework, gantries and hazard markings (our own designs) [batch]
- [ ] Airports at real scale (owner: the test airport was very small with a far too short strip): even small fields get a long runway with threshold and centre markings, taxiways with holding points and high-speed exits, an apron with stands; bigger ones add parallel or crossed runways, a terminal (one or two storeys, piers, jet bridges, concourse with shops and departure boards as symbols), control tower, hangars and maintenance, freight sheds, fuel depot, fire station, car parks, a rail station and road loops in front; the lowest tier is just an airstrip (a long graded or paved strip, maybe a shelter or hangar and a few fuel tanks, a windsock); a small regional field has one long main runway with a crossing secondary strip, a parallel taxiway, a single terminal pier with a few stands, a GA apron with hangars, a car park and road loop, and a perimeter fence with a service road; sized by the old tech and starport class, and at high tech the same place becomes a spaceport (pads, launch towers, landing fields beside or instead of runways); derelict on fallen worlds with wrecked aircraft on the aprons [queued: with industrial and military sites]
- [ ] Military and launch sites from the owner's photos: airbases (crossed runways with numbered thresholds, taxiways looping to dispersal pads and hardened shelters, aprons of parking spots, hangar rows, base housing beside them); launch complexes on flat coasts (gantry towers beside a pad on a raised ramp, flame trenches, water towers, spherical tanks, a crawlerway, perimeter roads); radar domes (white spheres on drums or towers, single on hills or in fenced rows); bunkers (domed pillboxes, slab-roofed coastal bunkers sunk in dunes, arched doors in earth mounds, store tunnels inside); perimeters (chain-link with razor coils, gates with barriers and stop signs in symbols, concrete walls with round watchtowers); air bases (flight lines of parked aircraft in rows, sunshades over stands, hangars, fuel tank farms, crossed runways, base town of blocks and tents beside them); forward operating bases and field camps (blast-wall compounds of filled barrier bags, rows of tents or half-dome shelters, containers, vehicle parks, guard towers at corners, a helipad or short strip, sprawling across the land as they grow); naval and coastal commands (piers and breakwaters, berthed ships, a great warship moored for good beside a city waterfront with aircraft still on its deck, explorable as a vertical place, walled compounds on the shore, fuel and water tank fields, radar domes, a runway on an island); derelict on fallen worlds: armoured vehicles left rusting in fields and in tunnels and shelters, trucks and fuel drums dumped in yards, arched hardened shelters with graffiti, curved corrugated huts and torn tent frames, rusted stair bridges, faded painted boards, lattice watchtowers over trailers [queued: with industrial sites]
- [ ] Industrial sites from the owner's photos, generated from parts and sited where the land and resources call for them: refineries (distillation columns, flare stacks, spherical and domed tanks, pipe racks on blue steel frames, lit up at night), power stations (clusters of hyperboloid cooling towers with steam, a turbine hall, tall chimneys), open-pit mines and quarries (stepped benches, haul roads, conveyors, ore piles, rail sidings, excavators and haul trucks left in place), container ports (ship-to-shore gantry cranes on the quay, container stacks, berthed ships), and big halls inside (arched concrete roofs, galleries, gantries, pipework, vessels, shafts of light); derelict on fallen worlds [queued: after cities and decay]
- [ ] Three test places for the owner to judge: playable on `proto/overhaul` via `Play Overhaul Test.cmd` (sewage works, farm, brutalist complex); roads now grids and straights; frame times 4 to 15 ms on an RTX 5070 Ti; owner's verdict needed before rollout [batch]
- [ ] Ice world seed 8462825922810013499 throws you out of the bunker [unknown: seed-specific; bunker exit fix is on main, not rerun]
- [ ] Start blocker: SliceTest seed 5884139302385556268 sticks in the bunker; present before the flyer work; must be fixed before the batch merges [unknown: seed-specific; start fixes in batch, not rerun]
- [ ] River water edges 1.6 m proud (old bug, SurfaceTest) [unknown: river fixes on main since, not rechecked]
- [ ] Ruined towns with streets, a main street and shops; suburbs thinned [batch]
- [ ] Ruined cityscapes: real city districts with tall broken buildings, on suitable worlds [batch: first pass. Districts, 8 to 30 storey towers, terraces, civic places, waterfront walls, lot frontage, collapse; missing: skewed grid, flooded streets, towers fallen across streets, bridges and station (roads side), sea quay frame]
- [ ] Colossal derelicts in the landscape (owner's Simon Stalenhag references, mood only; his style and designs are never copied, and no brand or text from them): giant dead machines and robots lying in fields and beside roads, half-buried, with cables trailing; walking machines stalled on highways; huge towers and spheres with a few lights on, looming over ordinary farms and towns; ships hanging low in fog; the feeling of everyday life carrying on beside vast tech nobody understands; giant coiled ring structures rusting by a pond; spheres and pods abandoned in long grass beside a junk-hung shack; small walker machines prowling woods; machines frozen into a snowfield; a car swallowed by fleshy tendrils at a lake edge (a fit for Husk-touched places); a tentacled machine stalking a rainy road past wrecked trucks; convoys hauling strange cargo; a colossal head-like ruin grown into a hillside; muted overcast palette, fog, snow and dusk. Generated from parts as world features and landmarks, scaled to be seen from far off, tied to the world's era and fall [queued: landmarks, with wrecks and the incomprehensible]
- [ ] Landmark buildings from the owner's 'weird buildings' set (not saved as files; described): looped towers bridged at the top, ring-shaped towers, stacked offset boxes cantilevering out, a tower broken into cube clusters, a building lifted on a sloping leg over water, twin towers joined by sky-bridges, a leaning watch tower with a cantilevered lookout, and starburst concrete monuments with porthole windows. All generated from the city architect's parts as rare landmarks in high-tech cities, never copies of the real buildings, and on fallen worlds they make striking ruins [queued: cities follow-up]
- [ ] Test cluster of broken vehicles built from blocks; massive highway pile-ups [partly: pile-ups and wrecks exist; not built from blocks]
- [ ] Test against the bar: Fallout 3 style ruin roaming [not started]
- [ ] Variety ideas from science fiction: 71 in `docs/sci-fi-inspirations.md`, top 20 all small jobs; five decisions for the owner at the end of that file; none built yet [plan only]

## Systems and design decisions

- [ ] Big Core and Small Core only: base and capital are one core, small and hover are one core; mode switch in the menu with warning stripes, seated indicator [partly: in batch; capital fuel burn and clean lift-off missing]
- [ ] CPU: only devices count, rebalance from real builds [running: rules]
- [ ] Block and component costs based on Empyrion's [unknown: costs not compared with Empyrion's]
- [ ] Level cap 25, matching Empyrion and Reforged Eden 2, with slower early progress [partly: cap is 25; slower early curve not built]
- [ ] No empty level bands: plan finished in `docs/tech-tree-pacing.md`, a full map of levels 1 to 25 with 343 nodes and a new points curve; not built yet [plan only]
- [ ] More and more exotic weapons than Empyrion or its mods: lightning guns, living weapons that fire worms, short-lived attack hornets, homing bullets, every sci-fi trope, plus every staple such as railguns, rocket and grenade launchers and mines (list finished in `docs/weapon-ideas.md`: 349 weapons in 42 families, first 12 chosen; none built yet, no fire system exists yet); owner decided: effects on the player last seconds, living weapons use plain biomass ammo [running: proto/weapons, the first 12 with generated models, icons from the model and generated sounds]
- [ ] Thrust hold toggle, power budget, thrusters and drives using CPU and fuel, spikes from drives and shields [not started]
- [ ] Ground and flight builds; heavy weapons, guns through the tech tree; cyber modules [not started]
- [ ] Better-looking survival tool and guns from a generated gun model builder: receiver, barrel, magazine, emitter and tank chosen from each weapon's parts, tech band and faction [not started]
- [ ] A lot of weapons can't be researched and need a cyber module: move exotic families out of the tree, then recut the tree and points curve in `docs/tech-tree-pacing.md` [plan only]
- [ ] Personal shields: a bar parallel to health, takes damage first, regenerates after a short break, as in Halo or Borderlands; no weapon wear; no companions [not started]
- [ ] Spool-up lift-off, then rerun the massive POI test [not started]
- [ ] At least 8 worlds per system, gas giants with many moons, moon starts [partly: moons and a giant in the system model; 8 worlds per system unconfirmed]
- [ ] Gravity anomalies: the one place things may float; must be unmistakable, with a clear edge, moving objects, sound and a pull the player feels, or it reads as a bug [not started]
- [ ] Start worlds always have a moon reachable under thrusters, with warp fuel and warp drive materials, as the next step after first flight; then the rest of the system holds hyperdrive materials, hyperdrive fuel and capital ship materials, guaranteed in each start system; needs landing on other bodies, which isn't built [partly: fuel moon in the system model; landing on other bodies not built]
- [ ] Space POIs: 100 orbital themes reflecting each world's tech level and theme, 100 deep space themes, and layers that feed the procgen, in `docs/space-pois.md` [running: research]
- [ ] Vacuum worlds, small or large, with no atmosphere: craters everywhere and meteor shower events [not started]
- [ ] Worst-case stress test after the merge: a dense ruined city, a large capital ship, many creatures and a storm at once, to measure whether the engine holds up [not started]
- [ ] Saves [partly: saves prototype exists; not in the slice]
- [ ] Lights: the core's lights toggle actually switches light blocks, and single lights can be switched [batch]
- [ ] Music: generated drones only, by world type and different for each world; no recorded tracks; no reactive music; master, music, effects and ambience volume, master default 50%; the owner approved the sound [batch]
- [ ] One roving non-aggressive ship on start worlds; NPC traffic later [not started]
- [ ] Fuel stations as POIs (with the ruins work), with low, mid, high and unique tech variants; every POI family gets tech variants, not cars and petrol everywhere [partly: roadside fuel stops in batch; other POI families unchecked]
