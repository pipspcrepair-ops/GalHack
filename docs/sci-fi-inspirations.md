# Sci-fi inspirations

A variety scout's list for the queue, not for building now. The owner's bug list outranks everything here. Every idea is drawn from a trope or a real-world oddity, with all names our own. Sources are credited in the notes and in section 7, and nothing in this file should appear in the game under a borrowed name.

Effort, as used below:

- **S**: data, models and sound. One agent in one run, no new system.
- **M**: S plus a small piece of code, such as one new site part, one behaviour or one sky term. One agent run with its own tests.
- **L**: a new system, or content big enough for a batch of its own.

The S and M ratings assume the POI overhaul lands, since site and room data may change shape when POIs move to the 2 m grid.

## 1. Summary

### What we already cover well

The docs are strong on how worlds fell and what they became: 112 themes, 70 apocalypses, 13 occupancy types, 40 world types. Exodus, plague, war, famine, eerie frozen moments, resort and souvenir humour, infrastructure, utilities, and the Hollow's slow wrongness are all well served. Wildlife is broad (19 forms, 8 behaviours, mutation, cave and POI ecology), weather is broad, and the space features table already holds most classic astronomy. Weapons belong to `docs/weapon-ideas.md` and are left out here.

### The biggest gaps

1. **The sky has nothing built in it, and little happens up there.** The sky shows a sun, up to three moons, a giant and rings. There's nothing left by the old civilisation, and almost no event you'd look up for.
2. **Nothing threads between worlds.** Each world is its own surprise. No mystery, object or mark recurs across a playthrough, so the player has nothing to notice building up over time.
3. **Survivors have places but no beliefs.** Themes say where people live and what they trade. Few say what they think happened, what they do because of it, or what they've forgotten.
4. **One look per tech band.** Kits differ by faction and by building type. Two high-tech worlds that aren't faction-held read alike, however different their stories.
5. **Creatures mostly threaten.** Few interact with the ruins, and few tell the player anything about the land.
6. **Nothing reads at planetary scale from a vessel.** Roads and stamps exist, but there's little to see only from the air, and the old civilisation's biggest works barely show on the ground.
7. **Sound carries almost no content.** Many eerie ideas in the docs depend on sound, and the slice has very few positioned sounds.
8. **Little runs on a clock.** A place looks the same at every hour.
9. **Few tools for noticing.** The player has a detector and eyes. There's little that rewards looking carefully, and the quiet UI wants exactly that.

### A note on how real these estimates are

From reading the code in `G:\GalHack-wt\batch\prototypes`:

- The world stack is not in code yet. `poi-generator/data/themes.txt` is keyed by archetype, and there's no theme, apocalypse, feature or occupancy record anywhere. Where an idea plugs into a layer, it's a line in a future record. Most top-20 ideas can go in today as a family with a weight in `themes.txt`, or as data, and move to the layer later.
- Written logs don't exist in code. `Dressing.cs` says so outright, and terminals only report door status or "NO POWER". That's owner decision 1 in section 8. Nearly every idea here is written to work without text.
- `world-themes.md` gained "a world in the midst of collapse" while this was being written. Nothing here depends on it.

## 2. The ideas

Grouped by where they plug in. Reuse names are real files or data in the batch worktree.

### World features and land

**1. Warning Country (S).** A stretch of land was deliberately made unpleasant by people who wanted you to stay out: fields of leaning concrete spikes, jagged earthworks, black slabs that swallow the heat, faces carved in pain, picture-signs nobody can quite read. At the centre, one sealed door.
- Trope: long-term hazard markers, the real design work on warning people ten thousand years away (spike fields, menacing earthworks).
- Plugs in: POI family and world feature; pairs with reactor, biolab, Precursor and grey goo apocalypses.
- Reuses: `sites.txt` parts `obelisks`, `heaps`, `fence`, `crater`; `Buried` stamp; radiation warnings.
- New: three site plans, a few sign decals, one concrete kit.
- Why: a landmark whose entire message is "don't", and every player goes straight to it.

**2. High Marks (M).** Huge shapes scraped into a plain, kilometres across, meaningless from the ground. From a vessel they're a line of arrows, a figure, or a ring that points at something.
- Trope: geoglyphs seen properly only from the air.
- Plugs in: world feature on steppe, desert, salt flat and ice.
- Reuses: `TerrainStamp` shapes `Furrow`, `Cut` and `Road`; biome patch; map reveal.
- New: a stamp pattern generator that draws polylines, and a hidden cache at the point of the marks.
- Why: the first reason to look down from a vessel, and it rewards a player for climbing.

**3. The Long Furrow (S).** A dead-straight ploughed valley, tens of kilometres long, ending at nothing. At the far end is a camp of the people who walked it to find out, and their small cache.
- Trope: the Big Dumb Object, here known only by its absence.
- Plugs in: rare world feature, any solid world.
- Reuses: `Furrow` stamp at large scale, `camp` site part.
- New: one dressing set for the study camp, whose gear is a generation apart at each end.
- Why: a mystery made of terrain, cheap and visible from orbit.

**4. Canal Age (M).** On some worlds the old civilisation's main network was water: straight canals between lakes, lock gates, aqueducts, barge wrecks, towpath inns.
- Trope: planetary romance's canal worlds.
- Plugs in: an infrastructure style, alongside road and rail.
- Reuses: `InfraRoute`, `Cut` stamps, bridges, `docks`, rivers.
- New: a route style that fills with water to a held level, lock and aqueduct parts.
- Why: changes how a whole world is laid out, multiplying with every theme.

**5. The Launch Ramp (M).** A mass-launch track running for kilometres up a long mountainside and ending in open air, its last span broken off. The town at the foot still has its cargo sheds.
- Trope: launch loops and mass drivers.
- Plugs in: POI family for high-tech worlds, class A and B ports. Differs from `launch_rail`, which is improvised and low-tech.
- Reuses: maglev guideway deco, `road`, `conveyor` and `tower` site parts, ridge siting.
- New: a site part that lays a straight rising track.
- Why: a skyline silhouette nothing else gives, and a rail line that points at the sky.

**6. Long Year Ages (S).** A world's year lasts centuries, and you've arrived in one age of it: the Thaw, with half-melted towns and meltwater roads; High Summer; Leaf-fall, with all flora turned; or Deep Winter. The climate won't change in your lifetime, and survivors plan for it anyway.
- Trope: very long seasons from an eccentric or double-star orbit.
- Plugs in: the owner's "day length and seasons" world variable.
- Reuses: palette sets, temperature field in `Suit.cs`, POI age and exposure.
- New: four palette shifts, a temperature offset, one half-buried dressing for the Thaw.
- Why: same terrain and themes, a different world on sight.

**7. Stone Landmarks (M).** Natural arches, sea stacks, rock bridges and hoodoos big enough to steer by.
- Trope: planetary romance scenery.
- Plugs in: world feature by landform.
- Reuses: POI far drawing, `rock` and `basalt` blocks.
- New: a rock-formation site generator. Far terrain is a heightfield, so overhangs must be block structures to show at a distance (see section 4, group 9).
- Why: fixes the one shape the terrain can't draw, and gives every world a skyline.

**8. The Still Wave (M).** A breaking wave forty metres high, stopped at the shore and turned to something like dark stone. The harbour town behind it is untouched. Rare and silent.
- Trope: time stopped mid-catastrophe, the frozen moment at world scale.
- Plugs in: rare outlier near the core; Hollow-flavoured, never explained.
- Reuses: block far drawing, opaque `fused` material.
- New: one sculpted site part.
- Why: a screenshot people share, with no neon and no explanation.

**9. Hill Signals (S).** On low-tech worlds, a chain of arm-towers crosses the hills, some still working, with a mirror flash from a far ridge by day.
- Trope: optical telegraphs in alternate-history SF.
- Plugs in: low-band infrastructure, where telecom and power lines don't fit.
- Reuses: `tower` part, deco animation, hamlet siting.
- New: one arm mesh, one sun-glint effect.
- Why: gives low-tech worlds a linked network, and something on the horizon that moves.

### Sky sights

**10. Second Noon (S).** One hard white point in the night sky casts pale shadows across a region. It's an old orbital mirror still lighting a city that's gone. Plants nearby keep the wrong hours, and night creatures avoid the patch.
- Trope: soletta mirrors in terraforming fiction, and the real Znamya experiment.
- Plugs in: sky feature on terraformed and high-tech worlds.
- Reuses: one of the three moon slots in `sky.gdshader`, `NightLight`.
- New: a tiny bright body, a light bias over a region.
- Why: a night that looks wrong for a reason you can go and find.

**11. Day Eclipse (S).** On a timetable, a moon crosses the sun at noon. The temperature dips, night creatures wake, and townsfolk go indoors.
- Trope: the rare darkness that changes everything.
- Plugs in: world feature on worlds with a large moon.
- Reuses: `SkyBody` periods, `Daylight`, species activity hours, town night rules.
- New: an overlap test that dims the sun.
- Why: an event you can see coming and plan around. Pairs with idea 48.

**12. The Visitor Moon (S).** A body in the sky that's slightly bigger each time you check. Sky-watchers keep a tally of it. It never arrives.
- Trope: the slow approach of a rogue world.
- Plugs in: rare world feature near the core.
- Reuses: `SkyBody.Radius`.
- New: radius as a function of the world clock, which needs saving (section 4, group 7).
- Why: dread on a timescale of hours of play.

**13. Swollen and Pinprick Suns (S).** A dying sun filling a fifth of the sky, or a white dwarf that's a tiny, savagely bright point.
- Trope: dying-earth skies.
- Plugs in: star class ranges.
- Reuses: `SunRadius`, `SunEnergy`, `StarColor`.
- New: numbers and a tuning pass, plus a check that the setting sun stays bearable.
- Why: almost free, and it changes every shot taken on those worlds.

**14. Evening Train (M).** At dusk, a line of dead satellites crosses the sky in single file, at the same time every day. People in towns set their day by it.
- Trope: orbital junk, and machines that outlast their makers.
- Plugs in: sky feature on high-tech worlds.
- Reuses: `star_turn` in the sky shader, town schedules.
- New: one moving point line in the shader.
- Why: makes old orbit infrastructure visible from the ground.

**15. Falling Night (S).** A harmless shower of bright meteors on a calm world. Townsfolk come outside to watch, and it's the one time they all stand still.
- Trope: comet and meteor sky events.
- Plugs in: event on inhabited worlds. Differs from the design's meteor hazard, as it's beautiful and safe.
- Reuses: the shader's star layer, town routine.
- New: a streak term and a townsfolk "look up" pose.
- Why: a moment of peace with people in it.

**16. The Light on the Moon (M).** From the ground, a light blinking on the moon's dark side. If you go there, it's a beacon still run by a lone machine or keeper.
- Trope: the sentinel left on a moon.
- Plugs in: sky feature linked to a POI on the same system's moon.
- Reuses: moon discs, `Charts`, orbit zones, `SkyBody` positions.
- New: a blinking point on a moon, a marker linking the two zones.
- Why: the purest "call to adventure" available: seen in the first hour, reached much later.

**17. Night Lights (S).** After dark, inhabited places show as warm specks from kilometres off, and a few old installations throw a beam straight up.
- Trope: the lit window in a dark land.
- Plugs in: POI far drawing, by occupancy.
- Reuses: POI layer's far mesh, `Mood`, `light` colours.
- New: a far emissive pass and one beam mesh.
- Why: night becomes a time for finding people, and the map reads itself.

### Space and orbit

**18. The Parked Fleet (M).** Rows of identical warships in perfect formation, sealed and cold, waiting for a crew. Most are stripped or dead inside; a few can be claimed.
- Trope: reserve fleets, and the alert fleet that never got the order.
- Plugs in: orbit feature, on war apocalypses.
- Reuses: `Derelicts.cs`, `orbit.txt`, core claiming.
- New: formation placement, one hull with several stripped states.
- Why: scale and order, against the usual tumbling debris.

**19. The Tended Forest (M).** A freighter carrying a sealed dome of real forest, the last of some world's trees, kept alive by a dying watering system.
- Trope: forests in orbit, tended by one caretaker.
- Plugs in: orbit POI, with `colonydome` kit.
- Reuses: `dome` site part, flora meshes, hydroponics rooms.
- New: a dome-on-hull layout and block-based soil.
- Why: green in space, and a place players want to protect.

**20. Standing Orders (M).** A claimed derelict's nav console still holds its last course. One press sends you there, with no idea what's waiting.
- Trope: ships that go somewhere when you press the button, prospecting by blind jump.
- Plugs in: reward on derelicts; ties to signal leads and deep-space sites.
- Reuses: warp, `ZoneLedger`, deep-space site records.
- New: a course record on a wreck, one console prompt. It always leaves enough fuel to get back, by the no-stranding rule.
- Why: turns claiming a wreck into a gamble with a story.

**21. The Long Way Round (L).** A generation ship still under way, its crew's descendants living in the decks, with no idea the hull is a ship, or that the galaxy fell.
- Trope: the generation ship that forgot it is one.
- Plugs in: rare orbit or deep-space site, capital hull.
- Reuses: `shiphull` kit, town people, boarding space.
- New: a very large interior and a people-in-space path.
- Why: a set piece players will remember for years, though it's a big job.

### POI families and ruins

**22. The Simulated Voyage (S).** A buried colony hull that never left the ground. The ports are screens still showing passing stars. Its people lived and died believing they were in deep space. You walk in from a meadow through an airlock marked "VACUUM".
- Trope: the voyage that never left, and the airlock that opens on grass.
- Plugs in: POI family for exodus, ark and decree apocalypses.
- Reuses: `shiphull` kit, `ship` and `cryo` deco, buried hatch siting, `media-screen`.
- New: one program, one star-loop screen.
- Why: an eerie reveal that needs no text, and the player gets it at the door.

**23. Late Arrivals (S).** A clean, shining ship has just set down. Its crew left before the collapse and travelled for a long time. They're in pressed uniforms, asking where the port is. They're the only people in the galaxy who don't know.
- Trope: time dilation, and homecoming to a changed world.
- Plugs in: rare POI family on any inhabited world.
- Reuses: `rocket` part, `landing_clearing`, `Town` roles, deco's fresh age (`Age.New`).
- New: one uniform set, a few lines, pristine old-world gear as stock.
- Why: an encounter that turns the player into the one with the bad news.

**24. The One Before You (S).** A failed first base near a bunker, built in your own blocks: a half-finished hover vessel, a survival tent, a skeleton in your suit. Some got further than others.
- Trope: finding a prior expedition's remains.
- Plugs in: flavour family for start worlds and worlds near them.
- Reuses: `data/blueprints/starter.gbp`, `pod.gbp`, blocks, deco.
- New: five or so failed-base blueprints, each dying of a different cause.
- Why: a ghost of the player's own game that also teaches what to build next, without a single line of text.

**25. Dummy Town (S).** A complete town of empty houses, with mannequins seated at every table, all the same face. Half of it is scorched. It was built to be destroyed in a test.
- Trope: real weapon-test towns, and the uncanny perfect suburb.
- Plugs in: POI family near `blast_crater` and military theme regions.
- Reuses: `flavour_house`, canteen and residential sets, `Burnt` state.
- New: one mannequin piece with sitting and standing poses.
- Why: a strong creep factor with an ordinary explanation, so it doesn't weaken the Hollow.

**26. The Visitor Centre (S).** A Precursor ruin with a ticket booth, handrails, gift shop and information boards built around it. The old civilisation came here as tourists, right before it all ended.
- Trope: misread archaeology, ruin as attraction.
- Plugs in: variant ring around `precursor_vault` and `alien_library`.
- Reuses: `themepark` and `resort` sets, `ticket-booth`, `info-kiosk`, `queue-rail`.
- New: a site plan and a few signs.
- Why: funny and chilling, and it quietly fits the fact that they dug something up.

**27. Perpetual Care (S).** Racks of stored minds with family names on them, and overdue payment notices. Rows have gone dark where the power budget ran out. One aisle is still lit.
- Trope: cyberpunk uploaded minds, the commercial afterlife.
- Plugs in: `data_centre` variant, with chapel rooms.
- Reuses: lab and office sets, `powered` share in disorder, `chapel`.
- New: rack plaque variants.
- Why: gives cyberpunk's tone to a world with no neon.

**28. Sealed Believers (M).** A living enclave that thinks the outside is death. You can't enter. A hatch and a trade slot let you barter, and a sealed door, warning lights and barricades keep you back. The air outside is fine.
- Trope: the sealed city that won't believe the world survives.
- Plugs in: `habitat_dome`, `deep_bunker`, as an SQT variant.
- Reuses: `Town`, trade screen, door locks.
- New: a hatch trade point, and one rule to keep the door shut.
- Why: a settlement you experience entirely from outside.

**29. Bodies Not Ours (M).** Alien rooms built for other bodies: stairs too tall for you, ramps with no landings, seats at the wrong height, doors wide and low. You need the jetpack to get round them.
- Trope: alien ergonomics, the unease of an interior not meant for you.
- Plugs in: every `alien_*` and `vharn` program.
- Reuses: programs, kits, grid.
- New: per-kit room scale rules, and walk tests that allow for jetpack steps.
- Why: makes alien places feel alien in the legs, not only in the palette.

### POI states and dressing (multipliers)

**30. Never Opened (S).** A new state for any family: finished, sealed and never used. Dust sheets, plastic on chairs, price stickers, a banner for an opening day.
- Trope: the world that ended the day before.
- Plugs in: POI state.
- Reuses: `disorder` table in `deco-arrangements.json`.
- New: one disorder row, wrap deco variants.
- Why: every family gains a site with no decay at all.

**31. Never Finished (M).** A new state for any family: half-built. Scaffolding, stacked materials, cranes, floors with no walls, foundations with rebar waiting.
- Trope: the unfinished megaproject.
- Plugs in: POI state. `PoiState` has 58 uses in code, so a real state is a careful job.
- Reuses: `Decay.cs`, `gantry-crane`, `pallet-stack`.
- New: a pass that strips upper storeys by progress, and scaffold pieces.
- Why: every family gets a second silhouette, and the rule of nothing floating is easy to keep because scaffolds stand on the ground.

**32. Building Cultures (S).** Each world rolls how its people built: ribbed adobe, stacked container blocks, stilted timber, earth-sheltered mounds, heavy brutalist slab, tiled courtyards. Same hospital plan, different place.
- Trope: planetary romance's sense of distinct civilisations.
- Plugs in: the owner's "architecture" world variable. Kits are palette files, so this is close to free.
- Reuses: all 26 files in `poi-generator/data/kits`, `Style`.
- New: about six non-faction kits and a roll.
- Why: the biggest multiplier here, as it touches every generic POI on a world.

**33. Afterhours (S).** Where long-life power survives, places keep their schedule: lights at dusk, a barrier lowering for no train, sprinklers on dead lawns, a departure board flipping, a bell for a shift.
- Trope: the automated house that carries on without its family.
- Plugs in: the owner's "what's still running" variable, on any POI.
- Reuses: deco `anim`, `light`, `powered`, town clock.
- New: a small table of timed actions.
- Why: places change by hour, so coming back at night is a different visit.

**34. Still Selling (S).** Powered billboards in high-tech ruins, advertising dead companies. A few greet the passer-by, and they've never stopped.
- Trope: cyberpunk's advertising as weather, corporate power outliving everyone.
- Plugs in: high-tech dressing, in dark city ruins.
- Reuses: `neon-sign`, `media-screen`.
- New: a set of original fictional brands and looping screens.
- Why: a dead city that's still trying to talk to you.

**35. The Vignette Deck (S).** A deck of tiny two-to-six-piece scenes dealt into rooms by tag. Examples: boots at a threshold, two chairs turned to a window, a child's height marks on a door frame, a table laid for one with two cups, a coat hung on a chair facing the door, a bed moved to block a window, a ladder to a roof with one chair at the top.
- Trope: environmental storytelling in post-apocalyptic fiction.
- Plugs in: `touches` in the disorder table, keyed by dressing tag.
- Reuses: `DecoArrangements` templates, `StoryProp`.
- New: 30 or so small templates.
- Why: the cheapest density available. It's what makes a room feel like someone lived there.

### People, factions and survivor culture

**36. Creeds (M).** A settlement rolls what it believes, which changes its dressing, dress, stock and rules. Examples: the Numbered, who hold inherited places in a queue that hasn't moved in 150 years; the Unmoving, who keep the collapse's last table exactly as it was and forbid touching it; the Stampers, who trade by forms; the Knockers, who talk through the pipes.
- Trope: lost knowledge turned into ritual, cargo cults, monks preserving what they can't read.
- Plugs in: a new light layer on SQT and CLT occupancy.
- Reuses: `Town`, `Mood`, deco, trade stock.
- New: a creed record and about eight sets of dressing.
- Why: settlements become places to tell stories about.

**37. The Timber Fleet (S).** A hilltop field of full-sized wooden starships, built plank by plank by people waiting for them to work. They repaint them yearly.
- Trope: cargo cult, ritual copies of technology.
- Plugs in: POI family on low post-tech worlds that once had ports.
- Reuses: the `rocket` and `aircraft` site parts with a timber kit, `survivor_hamlet`.
- New: a timber kit if none fits.
- Why: an unforgettable silhouette from two existing parts.

**38. Name Drift (S).** Survivors' town names are worn-down versions of the signs on the ruins beside them, so the place the old sign calls Meridian Central Interchange is now Merry Change. Every world gets its own.
- Trope: place names worn down over deep time.
- Plugs in: town and map naming.
- Reuses: place names, `NameGen` syllable rules.
- New: a small corruption routine.
- Why: costs almost nothing, and players will read the map for fun.

**39. World-Bred Folk (M).** People have changed to suit where they live: squat and thick on heavy worlds, long-limbed on light ones, big-eyed under dim suns.
- Trope: adapting people to worlds instead of worlds to people.
- Plugs in: people rig, by world profile.
- Reuses: the procgen people asked for in the playtest list, `Mutation`-style scaling.
- New: a proportion table.
- Why: every world's people read as natives. Waits on the people rework.

**40. Scavenger Marks (S).** Chalk signs near hazards and stashes mean real things: bad air, safe water, armed people, a cache, don't sleep here. Each band has its own mark too. Nothing explains them, and players learn them by being burnt.
- Trope: oral culture rebuilding a language.
- Plugs in: SCV occupancy and traces.
- Reuses: chalk arrows from the occupancy table, hazard locations, cache locations.
- New: a dozen decals and placement rules. They must be truthful almost always.
- Why: real information with no UI at all, which suits the quiet-UI rule.

**41. The Good Kilometre (S).** In the middle of ruined road, one perfect stretch: fresh paint, clean verges, straight kerbs. A hereditary crew maintains it, and has no idea why any more.
- Trope: duty that's outlived its reason.
- Plugs in: road dressing, `SQT` camp.
- Reuses: road `Wear`, wayside stops.
- New: one low-wear segment, a road crew role.
- Why: a small absurd sight that says a lot about the setting.

**42. The Last Round (M).** A sealed mailbag in a wreck, addressed to a town elsewhere on the world. Deliver it, and the town gathers round as the bag is opened. They pay with a marked map.
- Trope: the mail carrier as the restorer of hope.
- Plugs in: found item plus town interaction, on inhabited worlds.
- Reuses: items, `Town`, map reveal.
- New: a town hand-in, without a quest system.
- Why: a reason to cross a world that has no mission log.

### Creatures and alien behaviour

**43. Echo Birds (S).** Flocks that repeat sounds from generations ago: a door chime, a train announcement's tone, and on some worlds the evacuation siren, handed down bird to bird since the collapse.
- Trope: mimic birds, including real birds picking up machine sounds.
- Plugs in: sound on any flier on inhabited worlds.
- Reuses: flock and call systems.
- New: a table of mimicked sounds tied to a world's apocalypse. The siren is CC0 or self-made.
- Why: the apocalypse lives on in the wildlife, and it's a chill you hear before you understand it.

**44. Wreck Hermits (S).** A rusty hulk on the road shifts. A large crustacean-thing wears vehicles as shells. Flakes of rust sift off before it moves, so the ambush is fair.
- Trope: mimicry and shell-borrowing as predator strategy.
- Plugs in: Ambusher on roads and car parks.
- Reuses: `Ambusher`, `CrawlRig`, vehicle deco meshes, `Tack`-style attachment.
- New: a shell attachment, kept menacing and never comic.
- Why: makes the road's scenery suspect, and it's a clip people will send each other.

**45. Lode Grazers and Tell Plants (S).** Some animals lick at ore seams, and one plant only grows over copper. Watch them and you know where to dig.
- Trope: reading an alien ecology; real geobotanical prospecting.
- Plugs in: Grazer, flora, deposits.
- Reuses: `DepositFinder`, `Grazer`, flora scatter.
- New: a seek-deposit preference, one tell plant per world.
- Why: turns nature into a tool, and replaces bright ore rocks with something subtler, which the playtest list asks for.

**46. Bower Builders (S).** A creature that sorts ruin litter into careful displays round its nest: cups by size, a ring of hubcaps, keys in rows.
- Trope: animal aesthetics, and an intelligence you can't quite place. Real bowerbirds use human litter.
- Plugs in: NST dressing, Scavenger.
- Reuses: deco arrangements, lair marks.
- New: a few layouts. It takes nothing from the player, under the rule that nothing vanishes.
- Why: a moment of "was that a person?"

**47. Pack Wit (S).** Alone, these creatures are timid and stupid. Five together stop and flank you. Kill enough and the rest forget what they were doing.
- Trope: minds spread across a pack.
- Plugs in: `PackHunter` behaviour.
- Reuses: pack logic, `Senses`.
- New: a threshold that switches tactics.
- Why: a fight with a solution, and a creature you learn.

**48. Lamp-Shy Hunters (S).** Things that won't cross a pool of light. A work light or fire holds them off, and they surge in the dark, in caves or in a Day Eclipse.
- Trope: light-averse predators in an eclipse.
- Plugs in: night and cave species.
- Reuses: `Senses`, activity hours, work light.
- New: a light-avoid rule in the brain.
- Why: makes the player's light a defence, and gives weight to idea 11.

**49. Dusk Climbers (S).** Herd animals that stop eating and climb to the highest point at dusk, and stay there, standing still. Some are found dead on the peaks, with something fruiting from them.
- Trope: a parasite steering its host, as in real ant fungus.
- Plugs in: grazer variant, with `Mutation` parts.
- Reuses: Herd, `Rests`, height sampling.
- New: a high-point goal.
- Why: an eerie thing that happens every evening on the skyline. Wildlife only, so no player ailments.

**50. Slowstones (S).** Boulder-like animals that move a few metres a day and leave long tracks. You only notice when you come back and they aren't where you marked them.
- Trope: life on a very different timescale; real sailing stones.
- Plugs in: oddity on arid and salt worlds.
- Reuses: oddity form, `Furrow` decals.
- New: position as a function of the world clock, which needs saving.
- Why: an animal that only a patient player finds out about.

**51. Burden Giants (S).** Enormous grazers with a village's worth of old huts, tanks and masts lashed to their backs, the last sign that someone used to live up there.
- Trope: walking cities, the world on a beast's back.
- Plugs in: `TerritorialGiant`, `Tack`.
- Reuses: giant forms, `Tack`, deco meshes.
- New: a large tack set, and no climbing. A walkable version is in section 4, group 4.
- Why: a landmark that walks past your base.

**52. Web Lines (S).** Thick strands sagging between pylons, across gorges and between towers, all anchored at both ends. Something uses them as roads.
- Trope: alien architecture made from existing structures.
- Plugs in: NST on worlds with pylons.
- Reuses: `PowerLines` sag code.
- New: a strand material, a few spiders of the right size.
- Why: visibly changes what an infested world looks like from afar.

**53. The Old Rounds (S).** Feral beasts in rotted harness still walk the old delivery route between dead towns, stopping at each door.
- Trope: the faithful machine, here in an animal.
- Plugs in: domestic species on low-tech worlds.
- Reuses: `Domestic`, `Tack`, roads.
- New: a route follow.
- Why: touching, and it makes the road feel like someone still uses it.

**54. Tethered Drifters (M).** Gas-filled plant bladders on long stalks, swaying far above a swamp or bog, always rooted, never floating free.
- Trope: living balloons in gas-giant ecologies.
- Plugs in: flora by biome. Fits "nothing floats".
- Reuses: `GasBagRig`, procgen flora when built.
- New: a stalk, wind sway.
- Why: a sky-level silhouette for wet worlds.

### Found objects and consumables

**55. The Chipped Cup (S).** The same small chipped cup, a certain colour, turns up in places across the whole galaxy: on a Vharn console, in a Precursor vault, on a table in your own bunker, once. It's just a cup, and the game never says otherwise.
- Trope: the recurring sign of something behind everything.
- Plugs in: rare placement rule in every POI family.
- Reuses: deco, room dressing.
- New: one piece, one rule.
- Why: it's the thread between worlds, and players will go looking for it and argue about it online.

**56. Wrong Things (M).** Ordinary objects with one wrong property: a kettle that's always warm, a key that's heavy near vaults, a watch that runs backwards near the core, a coat that's never wet.
- Trope: ordinary objects touched by something else, in alien-leftover and paranormal fiction.
- Plugs in: rare loot near the core, as booster-slot items or carried items.
- Reuses: boosters, `items.json`.
- New: a passive effect hook. Plain item names, no flavour text. These aren't weapons.
- Why: collectable, and it brings the mystery into the inventory.

**57. Unlabelled Tins (S).** The labels fell off a century ago. It could be anything: good food, pet food, fruit in syrup, something that sets your teeth on edge.
- Trope: survivor food roulette.
- Plugs in: loot, consumable.
- Reuses: the `consumables` table.
- New: a random outcome from the food table.
- Why: a tiny gamble, and it's funny.

**58. Flight Recorders (S).** A crashed ship's recorder can be pulled from the wreck. Sell it, or plug it in and get a bearing to where the other half came down.
- Trope: the black box.
- Plugs in: wreck loot, signal leads.
- Reuses: `crashed_freighter`, lead direction and distance.
- New: an item that produces a lead.
- Why: chains two wrecks together, and no text is needed.

**59. Atlas Pages (S).** A found page of an old road atlas fills in a region's roads and town names on the map, all with the old names.
- Trope: the old map that's still half right.
- Plugs in: loot in cars and petrol stations.
- Reuses: map reveal.
- New: a reveal-by-item call.
- Why: a useful reward for looting the humblest ruins, and it sets up Name Drift.

### Mysteries and events

**60. Facing Coreward (S).** Empty suits, upright, standing in rows on open ground, all facing the same way. Nearer the core there are more. Nothing is inside them.
- Trope: cosmic horror's sign of a distant attention.
- Plugs in: a Hollow trace, density rising toward the core.
- Reuses: `suit-rack`, Hollow depth curve.
- New: one upright empty suit.
- Why: quiet, unexplained, and an observant player can use them as a compass.

**61. The Still Minute (S).** Every so often, on worlds near the core, all animal sound stops for about a minute. Creatures stand with their heads up. Then it goes on.
- Trope: the uncanny hush before something unseen.
- Plugs in: event, rising toward the core.
- Reuses: creature `Activity`, ambient audio.
- New: a timer and a mute.
- Why: the Hollow done with no assets at all.

**62. The Knock (S).** Three slow thuds from far below, felt rather than heard, with dust falling from ceilings. Always three. It comes more often toward the core.
- Trope: something very large, very far down.
- Plugs in: event, shares the planned earthquake work.
- Reuses: camera shake, dust.
- New: one sound.
- Why: gives the unexplained a rhythm, at almost no cost.

**63. The Hour Horn (S).** Once a day, a great horn sounds across a region from an old works. Townsfolk pause and look up. Nobody knows why they do. The player can walk to it.
- Trope: machines running past their makers.
- Plugs in: region feature, on industrial and transit worlds.
- Reuses: sirens, town routines, Afterhours.
- New: one sound with long range.
- Why: a sound landmark that makes a whole region feel connected.

**64. Strange Rains (S).** New rain kinds by world: sideways glass sleet on hot worlds, thin iron drizzle that rusts in the air, soot snow, oily black rain on tar flats, red rain.
- Trope: real exoplanet weather models; acid and iron rain in SF.
- Plugs in: `weather.json`, by air and star.
- Reuses: `Rain`, `Snow`, `Embers`, `Debris` params, `CloudTint`.
- New: one `WeatherKind` each and a tinted emitter.
- Why: the first ten seconds on a world feel different, for very little data.

**65. Seed Fall (S).** Once a season, slow drifting seeds fill the sky like snow, and creatures come out to feed. A day later, the ground is carpeted in short-lived plants.
- Trope: alien reproduction as weather.
- Plugs in: event on lush worlds.
- Reuses: weather emitters, `CreatureActivity`, flora.
- New: one emitter, one bloom palette for a day.
- Why: a beautiful, harmless event for a genre that leans on threats. Not on Hollow worlds, and no glow, so it keeps clear of the Bloom.

### Tools, gadgets and vehicles

**66. Field Glasses (S).** Hold to look far: POIs, smoke, moving things. No labels, no markers.
- Trope: the scout's lens.
- Plugs in: tool at level 1 or 2.
- Reuses: the camera, hotbar.
- New: FOV zoom and a soft frame. Nothing in the design covers this.
- Why: rewards stopping to stare, which is the whole point of the game. Pairs with Marks, Night Lights and every landmark.

**67. Ticking Counter (S).** A hand instrument that clicks faster near radiation and mutagen. You hear danger, and the visor doesn't need to say it.
- Trope: the zone-walker's detector.
- Plugs in: the detector tool.
- Reuses: radiation field, audio.
- New: one sound loop. The warning rule still holds through the sound and the visor.
- Why: tension by ear, and a quieter UI.

**68. Hand Radio (M).** A small radio with a dial. Turn your head and the static clears toward a signal, so you walk to it. Pirate stations, loops, tones.
- Trope: the numbers station, following a voice through a dead land.
- Plugs in: tool; hooks into Whisper Flats, Pirate Radio and signal leads.
- Reuses: signal leads, bearings.
- New: a signal list, static mix by bearing.
- Why: gives discovery a sense of its own, and brings existing themes to life.

**69. Sail Rig (M).** A device block: a tall sail that gives thrust from the wind. A free ride across salt flats and steppe, with gang land-yachts to match.
- Trope: land yachts on dry seas.
- Plugs in: small-grid device, low tech tier.
- Reuses: `WeatherBridge` wind, hover vessels, `Device`.
- New: one device and its mesh.
- Why: a new way to build a vehicle, using wind that already exists.

**70. Lift Bags (M).** A gas-cell device that holds a vessel up for free, so you can build a slow airship. Survivors on low-tech worlds use them too, once NPC vehicles exist.
- Trope: airships as the frontier's transport.
- Plugs in: vessel device, on worlds with air.
- Reuses: `HoverController`, block devices.
- New: a device with lift scaled by air density.
- Why: a different build goal on the way to vessels, and a new shape for the sky.

**71. Folding Wing (M).** A worn glider that opens at height. Drop off a mesa or an old tower and ride the wind down.
- Trope: personal flight on planetary romance worlds.
- Plugs in: suit item, shares jet fuel rules.
- Reuses: `WalkController` floating, wind, gravity.
- New: a glide state.
- Why: makes height worth having, and it changes with every world's gravity and wind.

## 3. Top 20 by payoff for effort

None of these needs a new engine system. Each has a route into the game before the world stack exists.

| # | Idea | Effort | Why it ranks here |
| --- | --- | --- | --- |
| 1 | 55 The Chipped Cup | S | One model threads the whole galaxy; the first thing players will post about |
| 2 | 32 Building Cultures | S | Kits are palette files, so it touches every generic POI on a world |
| 3 | 24 The One Before You | S | Uses blueprints that exist; a ghost of the player's own game, and teaches by example |
| 4 | 43 Echo Birds | S | One sound table; the apocalypse heard in living things |
| 5 | 22 The Simulated Voyage | S | The reveal happens at the door, with no text needed |
| 6 | 35 The Vignette Deck | S | Cheapest room-level density; plugs into `touches` in the disorder table |
| 7 | 1 Warning Country | S | Existing site parts; a landmark players can't resist |
| 8 | 37 The Timber Fleet | S | Two existing site parts in a new kit give a huge silhouette |
| 9 | 44 Wreck Hermits | S | Makes all road scenery suspect; existing Ambusher |
| 10 | 45 Lode Grazers and Tell Plants | S | New use of existing brains, and a subtler answer to the bright ore tell complaint |
| 11 | 40 Scavenger Marks | S | Real information with no UI |
| 12 | 66 Field Glasses | S | Small code, and every landmark gains from it |
| 13 | 33 Afterhours | S | Places change by the hour, using what deco already supports |
| 14 | 23 Late Arrivals | S | A single rare encounter with a huge story |
| 15 | 30 Never Opened | S | A new look for every family from one disorder row; can be a variant of Abandoned at first |
| 16 | 38 Name Drift | S | A small routine; every map becomes worth reading |
| 17 | 64 Strange Rains | S | Data plus a tinted emitter, and each world feels new at once |
| 18 | 10 Second Noon | S | One moon slot, and night reads differently |
| 19 | 11 Day Eclipse | S | Sets up a timed event from what the sky already knows; pairs with 48 |
| 20 | 60 Facing Coreward | S | One model; subtle Hollow, as the owner asked |

Close behind: 41 The Good Kilometre, 62 The Knock, 61 The Still Minute, 25 Dummy Town, 51 Burden Giants, 26 The Visitor Centre.

Combinations are where these earn most. A Thaw world with a Timber Fleet, Echo Birds still sounding the siren, and a horn each dusk is four table entries, and no one wrote that world by hand.

## 4. Worth engine work

The owner asked that ideas needing engine work be weighed, not thrown out. Nothing here is a plan, and no work should start from it. Sizes are from reading the code, and none of it has been tried.

Size words: **small** is one agent run; **medium** is several runs plus new tests; **large** is a batch of its own, with real risk to what's already working.

### The short version

| Group | Size | Risk | Unlocks | View |
| --- | --- | --- | --- | --- |
| 1 Soundscape | Small | Low | About 12 | Do first: cheapest and most ideas |
| 2 Sky objects | Medium | Low to medium | About 12 | Best value; wait for `proto/space` |
| 3 Local fields | Medium | Medium | About 12 | Strong; split into two stages |
| 4 Movers | Medium, then large | High | About 14 | The design needs it anyway; do the rail tier first |
| 5 Small worlds and band skies | Medium to large | Medium | About 11 | The big surprise; prove it with one small test |
| 6 Local water | Medium | Low to medium | About 8 | Yes for rooms; no for live tides |
| 7 Saved world clock | Small | Low | About 7 | Do with Saves |
| 8 Mass fauna | Medium | Medium | About 8 | Only after the hitches are fixed |
| 9 Terrain vertical range | Very large | Very high | About 4 | Too risky, don't |
| 10 Rotated structures | Medium | Medium to high | About 5 | Right angles only, after the face fixes |
| 0 World stack | Large | Medium | Everything | Already on the owner's list |

**Group 0, the world stack.** Not a rewrite, but it comes first. Theme, apocalypse, features, occupancy and the world profile (gravity, air, light) are written in `world-themes.md` and aren't in code. `SurfaceZone.cs` line 104 reads one `Gravity` from `slice.json`, so a per-world value is trivial once a profile record exists.

### Group 1. Soundscape

- **System:** `src/Game/SliceAudio.cs` builds sounds in code, and the slice and planet-surface have five uses of a positioned audio player between them. There's no table of sounds tied to places, deco, events or the clock.
- **Change:** small. A data table of positioned loops and one-shots with distance culling, plus a sample loader.
- **Risk:** low. CC0 sources only, no Kenney assets, and licences rechecked before use.
- **Unlocks:** Hour Horn, Echo Birds in full, Afterhours, Still Selling, Hand Radio, The Knock, Ticking Counter, and from the existing docs Pirate Radio, Whisper Flats, Choir Canyon, Sound Gardens and the Wind Chime Coast.
- **Note:** many of the eerie ideas in `world-themes.md` are really sound ideas, so this one quietly pays for existing themes.

### Group 2. Sky objects

- **System:** `planet-surface/shaders/sky.gdshader` has one sun, an array of three moons, one giant and one ring. `SkyRig` has one `Sun` and one `NightLight`. Everything else is hard-wired.
- **Change:** medium. A small general list of sky objects (disc, line, band, point, streak), each with direction, size and colour. Second suns are the costly part: a second shadow-casting light is expensive, so use an unshadowed fill and fake the double shadows only where it matters.
- **Risk:** low to medium. `proto/space` is working on the climb out of the atmosphere in the same shader, and `SkySeams` tests guard it, so this has to wait for that merge. Cost is per sky pixel, so keep the list short.
- **Unlocks:** Evening Train, Falling Night, The Light on the Moon, The Visitor Moon at real scale, binary and trinary suns (in the design), the shattered moon overhead (in the design), and comets seen from the ground, plus these new ones:
  - **Ring Overhead.** A pale line from horizon to horizon: an orbital ring, with cable stumps standing at points on the ground beneath it.
  - **Sunshade Dusk.** A vast orbital shade, half broken, so one band of the world sits in constant twilight.
  - **Tidally locked sky.** `SunAt` only needs to hold still; the hard part there is climate, not sky.
  - **The Arc's band and habitat skies.** See group 5.

### Group 3. Local fields

- **System:** `ZoneGravity.At` is one uniform pull per zone. `Suit.cs` reads oxygen by air kind, radiation by archetype and temperature from the planet map. There's no way to say "this circle is different". The design already calls for circles as zone data.
- **Change:** medium. A spatial list of fields (circle or box) carrying gravity, heat, air, radiation and mutagen, sampled by vitals and physics.
- **Stage one:** vitals only (heat, air, radiation, mutagen). Small and safe, and it delivers the climate control node on the owner's list as a field.
- **Stage two:** gravity. `GravityAt` runs for every body every tick, so it needs a fast lookup. Creatures walk with their own locomotion and would ignore it until they're taught. `WalkController` already walks on whatever face `Body.Up` says is underfoot, and mag boots already do this on hulls, so true "down is that way" patches are nearer than they look.
- **Risk:** medium. Floating objects clash with the nothing-floats rule (owner decision 3). Warning rule still applies.
- **Unlocks:** warned radiation zones, mutagen zones, climate control nodes, thin-air and sealed-dome rooms, cold spots, heated oases, the gravity anomalies in the design, Z54 gravity shift, weightless wreck rooms, plus:
  - **Anomaly Patches.** Invisible warned spots in Hollow-touched ground, with tells such as bent grass, hanging dust or a ring of dead birds. A player throws a bolt to test the air.
  - **Wall Rooms.** One small ruin where down is sideways.

### Group 4. Movers

- **System:** the design wants crewed NPC vehicles, and the slice has none. `Slice.csproj` compiles neither the `encounters` nor the `boarding` prototype, and there's no pilot AI. `TerrainStreamer` builds ground collision only within `CollisionRadius` = 2 chunks of 16 m around the focus, so nothing far from the player has ground to touch. Anything moving out there has to follow `SurfaceHeight` or a route.
- **Tier A, rail-bound, medium size.** A structure following an `InfraRoute`, with riders on board. Lift cars already carry riders in places, and `Body.Frame` already handles riding. Risk is medium: seams, stops at stations, and streaming chunks past a rider at speed.
- **Tier B, free pilots, large size.** NPC vessels with AI crew, block-built, as the design says. Risk is high: physics load, the camera vibration bug near wrecks, a moving structure's mesh and collision, and later co-op. Also, `Place` is static by design, so a moving POI is a `Vessel` with people on it, not a moved `Place`.
- **Unlocks, tier A:** **The Working Line** (a real train you board), trams, cable cars between mesas, ferries, and a moving **Market Train** that calls at towns.
- **Unlocks, tier B:** the one roving ship on start worlds (on the owner's list), sky patrols, convoys, gang vehicles, invasions by ship, **Crawler Towns** (a walking or tracked settlement, from the walking-city and traction-city tropes), walkable Burden Giants, Lift Bag traffic.
- **View:** the design needs tier B regardless. Tier A is safer and gives the first big proof.

### Group 5. Small worlds and band skies

This is the find of the review.

- **System:** every surface is already a flat torus that wraps both ways, with uniform gravity. `PlanetSpec` takes a whole-number `WidthKm`, with height half of it, and rolls 8 km at the smallest. `Handover.cs` moves bodies between zones without a gap, and each zone keeps its own records.
- **The insight:** several ideas the docs treat as heavy may not need new terrain at all.
  - The **Folded Town** (theme 52) says walking out brings you back in by the other end. A tiny surface zone already does exactly that, with no portals and no trick. It is the torus.
  - A **Precursor Arc** is a flat band with gravity straight down. That's what we have. It needs a sky that shows land rising overhead (group 2), and walls at the two edges so the north-south wrap stays hidden. The design says Arcs need their own generator path; this may be smaller than feared.
  - The inside of a **colony cylinder or terrarium** is the same thing at a smaller size. A wrecked ark could hold a whole small world, with a walk through an airlock into farmland curving up overhead.
  - **Shell worlds, undercities and deep caverns** can be separate small zones with a rock sky, reached by a long shaft, instead of a second terrain layer.
- **Change:** medium for one tiny zone, large for the whole family. The generator's landforms assume kilometres, the map's sizes are whole kilometres, and the horizon mesh reaches 7 km, so on a tiny map it repeats the town out to the horizon (fog hides it, or it's a spooky feature). The hidden entry needs a transition such as a long lift or tunnel, which the design's seam rule supports.
- **Risk:** medium. Mainly unknown: nobody has run the generator this small. A single test map would settle most of it.
- **Unlocks:** Folded Town, Mirror Cities' bridges, `time_pocket`, `wrong_house`, Arcs, `ring_remnant`, `ring_lock`, habitat cylinders, orbit terrariums, shell worlds, undercity districts.

### Group 6. Local water

- **System:** `IWaterField.SurfaceAt` gives one surface per column, decided by natural ground alone. `PlanetTerrainSource.WaterLevel` is a constant zero, referenced 31 times in 15 files.
- **Change, room water:** medium. Box volumes inside structures, for buoyancy and the underwater state, and a surface plane for each. Risk is low to medium, and it should wait for the planned underwater state.
- **Change, sea level per world:** small. Shift the terrain rather than the water, so that everything else keeps working. This gives the Drowned and Drained worlds.
- **Change, live tide:** medium to large with high risk. Shore flora, wrecks, ore, roads and the river mask all assume water doesn't move. Not worth it now. A small fake tide near shore would do most of the job.
- **Unlocks:** **Flooded Floors** (POIs drowned to a waterline), `drowned_cistern`, flooded metros, Floodlines interiors, sunk ship holds, cave pools, coolant pits, tanks and aquariums.

### Group 7. Saved world clock

- **System:** `SkyRig._days` is private and isn't in `ZoneLedger` or the saves code that I could find, and the world starts at `StartTimeOfDay`.
- **Change:** small, as part of the Saves item.
- **Risk:** low.
- **Unlocks:** Visitor Moon, Slowstones, Long Year timetables, eclipse timetables, comet seasons, Regrowth timers, and repeat visits showing change.

### Group 8. Mass fauna

- **System:** creatures are a patch around the player, with rigs and a `Brain` each. The owner's list already wants fewer, because of garbage collection hitches.
- **Change:** medium. A separate visual-only layer: `MultiMesh` herds and flocks far off, with no AI, handing a few near ones to the real sim.
- **Risk:** medium. It's performance work on a known sore point, so it's only worth doing after the hitches are fixed and after the stress test on the owner's list.
- **Unlocks:** **The Great Crossing** (a migration filling a valley, faked by a path), Titans' Meadows at real scale, locust fronts, roosts of thousands, crowds of sleepwalkers and husks, stadium crowds, and full fish schools.

### Group 9. Terrain vertical range

- **System:** near ground is 1 m voxels out to `VoxelRadius` = 5 chunks, about 90 m. Everything further is a heightfield, then a horizon mesh. Bedrock is at 30 m and the cave band ends at 36 m. So big overhangs, arches, floating slabs and second layers can't exist past about 90 m.
- **Change:** a true multi-layer terrain would touch `PlanetTerrainSource`, `FarBuilder`, `TerrainStreamer`, stamps, caves, the digging rules and every surface query. Very large.
- **Risk:** very high. The owner's top-priority bug is seeing through ground, and this is the code in question.
- **View:** too risky, and mostly pointless, because cheaper routes exist. Overhangs and arches are block structures (idea 7). Shell worlds and shattered worlds are separate zones or block slabs (group 5).

### Group 10. Rotated structures

- **System:** POIs sit on the grid, a place and its far drawing share the same blocks, and stamps cut the ground to the footprint. Route walkers assume floors are floors.
- **Change:** medium for 90 degree turns, which stay on the grid. Any other angle is large, and the far drawing, terrain cut and walls must all agree, or the wall-gap bugs come back.
- **Risk:** medium to high. It sits on top of the current face and terrain-gap work.
- **Unlocks:** **Toppled Tower** (walking a skyscraper along its side, with floors as walls), capsized liners, ships nose-down in bog, and the tilted towers in Z7 and Z25.
- **View:** right angles only, and only once the face bugs are closed. A lean of a few degrees can be faked by building the lean into the blocks.

### Changes that are too risky or pointless

- **True round planets.** Pointless. The flat torus works, and Arcs and habitats depend on it.
- **Multi-layer voxel terrain.** Too risky, as in group 9.
- **Portal rendering for impossible spaces.** Pointless. Wrapped small zones do it with no rendering work.
- **Real fluid simulation.** The design rules it out, and tides don't justify it.
- **Living, flexing ships and buildings (biopunk).** Pointless. Structures are rigid block chunks, and a rewrite for this breaks the core "build what you fly" rule. A grown look can come from kits and sound.
- **Real time dilation between zones.** Pointless. It would break co-op and saves. Late Arrivals gives the same feeling for free.
- **Off-screen world simulation.** The design rejects it. Fake it with routes and the clock.
- **Two shadow-casting suns everywhere.** Too expensive for what it gives. Fake it.

## 5. Great, but too big for now

- **21 The Long Way Round.** Needs a huge interior and people in space. After boarding.
- **Crawler Towns.** Needs movers tier B.
- **The Working Line.** Needs movers tier A.
- **Precursor Arcs and habitat cylinders.** Need groups 2 and 5.
- **Uplifted settlements.** Whole towns of raised animals, using the town brain on creature rigs. Strong concept, but it needs new animation and an owner call (decision 4).
- **Machine under the world.** A planet whose deep interior is a Precursor engine, with shafts down to it, reached through a small zone. Needs group 5, and a lot of art.
- **Procedural history of a world.** Rival accounts of a founder, shown only through statues and plaques, as in generated sultans. Great, but it depends on the text decision and the world stack.
- **Self-copying machine ecology.** A machine world evolving by itself, with machines stripping each other. Would need a sim, and overlaps BOT occupancy. Cheaper to hint at with dressing.

## 6. Notes on rules kept

- No drones, nothing from Earth, no real brands. All names are ours.
- No item, creature or event here gives the player an ailment.
- Hollow ideas (8, 12, 55, 60, 61, 62) add no glow and no label. Neon stays with Bloom worlds.
- Weapons were left out. Flares, traps and bolts as attacks are for the weapons agent.
- Anything meant as a landmark is a block structure on the grid, or sits on stamps, and stands on the ground.

## 7. Sources

Web pages are for trope reference only; no text or design was copied. Pages were read through search summaries or direct fetches. TV Tropes refused a direct fetch, so trope pages were used only as far as search results showed them. Two low-quality mirror pages in search results held instructions aimed at AI tools; they were ignored and aren't cited.

Concepts and catalogues:

- Macrostructures: https://sf-encyclopedia.com/entry/macrostructures
- Generation starships: https://sf-encyclopedia.com/entry/generation_starships
- Terraforming: https://sf-encyclopedia.com/entry/terraforming
- Ruins and futurity: https://sf-encyclopedia.com/entry/ruins_and_futurity
- Megastructure: https://en.wikipedia.org/wiki/Megastructure
- List of planet types: https://en.wikipedia.org/wiki/List_of_planet_types
- Big Dumb Object: https://en.wikipedia.org/wiki/Big_Dumb_Object
- Speculative evolution: https://en.wikipedia.org/wiki/Speculative_evolution
- Uplift: https://en.wikipedia.org/wiki/Uplift_(science_fiction)
- Dying Earth genre: https://en.wikipedia.org/wiki/Dying_Earth_(genre)
- Self-replicating spacecraft: https://EN.wikipedia.com/wiki/Von_Neumann_Probe

Works and games:

- Heechee Saga: https://en.wikipedia.org/wiki/Heechee_Saga
- Rendezvous with Rama: https://en.wikipedia.org/wiki/Rendezvous_with_Rama
- Nightfall: https://en.wikipedia.org/wiki/Nightfall_(Asimov_novelette_and_novel)
- Helliconia: https://en.wikipedia.org/wiki/Helliconia
- The Postman: https://en.wikipedia.org/wiki/The_Postman
- The Forever War: https://EN.wikipedia.com/wiki/The_Forever_War
- There Will Come Soft Rains: https://www.encyclopedia.com/arts/educational-magazines/there-will-come-soft-rains
- A Canticle for Leibowitz: https://tvtropes.org/pmwiki/pmwiki.php/Literature/ACanticleForLeibowitz
- The Mote in God's Eye: https://en.wikipedia.org/wiki/The_Mote_in_God%27s_Eye
- Pitch Black: https://en.wikipedia.org/wiki/Pitch_Black_(film)
- Silent Running: https://sf-encyclopedia.com/entry/silent_running
- Mortal Engines: https://wikipedia.com/wiki/Mortal_Engines_(film)
- Walking City: https://en.wikipedia.org/wiki/Ron_Herron
- Blame!: https://en.wikipedia.org/wiki/Blame!
- Nausicaa: https://en.wikipedia.org/wiki/Nausica%C3%A4_of_the_Valley_of_the_Wind_(manga)
- Roadside Picnic: https://www.tvtropes.org/pmwiki/pmwiki.php/Literature/RoadsidePicnic
- STALKER 2 anomalies: https://www.pcgamesn.com/stalker-2/anomalies
- Caves of Qud sultans and relics: https://wiki.cavesofqud.com/wiki/Sultan
- Stellaris relic worlds: https://admin-forum.paradoxplaza.com/forum/developer-diary/stellaris-dev-diary-148-relics-relic-worlds.1177865/latest
- Control's Objects of Power: https://control.fandom.com/wiki/Object_of_Power
- Annihilation: https://www.sf-encyclopedia.com/entry/annihilation
- Cyberpunk notes: https://www.remnote.com/learn/literature/science-fiction/introduction-to-cyberpunk-study-deck

Real-world oddities:

- Long-term nuclear warnings: https://www.damninteresting.com/this-place-is-not-a-place-of-honor/ and https://www.wikiwand.com/en/articles/Long-term_nuclear_waste_warning_message
- Soletta: https://en.wikipedia.org/wiki/Soletta
- Znamya: https://www.wikipedia.com/wiki/Znamya_(satellite)
- Nazca lines: https://atlasobscura.com/places/nazca-lines-peru
- Geobotanical prospecting: https://www.wikipedia.org/wiki/Geobotanical_prospecting
- Sailing stones: https://livescience.com/planet-earth/geology/racetrack-playa-the-home-of-death-valley-s-mysterious-sailing-stones
- Bowerbirds: https://blog.nature.org/2021/01/04/bowerbirds-meet-the-bird-worlds-kleptomaniac-love-architects/
- Lyrebird mimicry, with its doubts: https://www.sbs.com.au/news/article/lyrebirds-mimicking-chainsaws-fact-or-lie/z81p3cdh4
- Iron and glass rain: https://www.discovermagazine.com/5-of-the-strangest-and-most-dangerous-exoplanets-ever-discovered-47295 and https://zmescience.com/other/feature-post/rain-titan-earth-neptune

Named from memory, not confirmed in this session: the fossil pilot in Alien, Melancholia's approaching planet, Aldiss's Hothouse, optical telegraphs in alternate-history SF, Logan's Run and the sealed-city trope, real weapon-test towns, and the ant fungus behind Dusk Climbers. These only inspired ideas, so nothing in the game depends on them being exact.

Code read, in `G:\GalHack-wt\batch`: `shared/Physics/Core/Gravity.cs`, `Water.cs`, `PhysicsWorld.cs`, `WalkController.cs`; `shared/Terrain/*`; `shared/Items/data/deco.json`, `deco-arrangements.json`; `prototypes/planet-surface/src/Terrain/TerrainStreamer.cs`, `src/Sky/*`, `shaders/sky.gdshader`, `src/World/Infra/*`; `prototypes/planet-generator/src/Gen/PlanetSpec.cs`, `Climate.cs`; `prototypes/poi-generator/data/*`, `src/Gen/Dressing.cs`, `Family.cs`, `Arch/Sites.cs`; `prototypes/weather/data/weather.json`, `src/Model/*`; `prototypes/wildlife/src/*`; `prototypes/slice/src/*`, `Slice.csproj`.

## 8. Decisions for the owner

**Answered by the owner:** NO WRITTEN LOGS, NO DIALOG. Any idea below or above that needs speech or readable text must lose it or be cut. "No drones" means only Empyrion-style drones that invade in waves; other machines and drones are fine and follow procgen rules. Floating is allowed only in a gravity anomaly that is unmistakable, or it will read as a bug. Still open: how far human-likes go, and which engine groups go on the list.

1. **Do written fragments exist?** `DESIGN.md` talks of logs that stop mid-sentence and `world-themes.md` mentions logs in most entries, but `Dressing.cs` states that there are no written logs and terminals only show power. Ideas here work without text, but it's a real fork: short found text, or none at all.
2. **What does "no drones" cover?** `world-themes.md` still has `drone_hive`, `drone_factory`, crop drones, cleaner drones, and Z9 Drone Wars. Do the rule's limits stop at combat, or should those all become crewed vehicles and other machines?
3. **Can anything float inside a gravity anomaly?** The design's floating rocks and Z54 clash with "nothing floats". Only matters if local fields go ahead.
4. **How far do human-likes go?** The playtest list wants humans and human-likes. World-Bred Folk stays human. Uplifted animal settlements would go further, and creatures are meant to stay menacing.
5. **Which engine groups, if any, should go on the list?** My suggested order is 1 Soundscape, 7 Clock, 2 Sky objects, 3 Local fields stage one, then a one-map test of 5 Small worlds. That test is cheap, and it decides whether Arcs and the Folded Town are far easier than the design assumes.
