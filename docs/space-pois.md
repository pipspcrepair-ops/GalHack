# Space POIs

How GalHack fills planet orbits and deep space with places worth flying to. This builds on `docs/world-themes.md`, `docs/solar-systems.md`, `docs/sci-fi-inspirations.md` and the design doc, and reopens none of their decisions. It is a design only: no code has changed.

How to read a theme entry, in the same spirit as `world-themes.md`:

- **Happened**: the cause, shown only through what the player finds, never told. There are no written logs and no dialogue: signs, stencils, sigils, tally marks, pictograms and wordless sound only.
- **In orbit** or **Where**: what stands there, and where. **Varies** shows how the layers change it, because a theme is a set of odds and never one fixed place.
- **Families**: `top` are families that already exist in `poi-generator/data/orbit.txt`. `NEW` are families that don't exist yet, counted in section 6. All of them are built from the player's blocks.
- **Inside**: placeable things, as rooms, deco and machines.
- **Occ**: the occupancy mix in percent, using the codes in `world-themes.md` plus one new code, SLP, in section 2.
- **Hazards/rewards**: what hurts and what pays.
- **Fit**: pre-collapse band > post-collapse band | ports | worlds or places | rarity (C common, U uncommon, R rare, X exotic) | S if a dead form may appear in a start system, no if not | `from`, the world themes (numbers) and apocalypses (Z numbers) that lead to it.

Tech letters are L low, M mid, H high. The incomprehensible is not a band: it can only be glimpsed.

---

## 1. Summary

**A space POI is a stack of rolls, never a designed place.** The same method as the ground: place, parent world, age, what happened, who owned it, who is there now. A theme only moves the odds. Two runs of one theme give different places, and the themes are examples of what the layers tend to produce.

**Three multipliers do most of the work.** Family is the shape (ring, truss station, hull, tank farm). Role is what it was for, which picks the rooms (hospital, customs, barracks, cannery). Kit is who built it, which picks the look. About 60 families, 40 roles and 10 builder looks give tens of thousands of different silhouette-and-interior pairs before age, damage, power and occupancy touch them.

**An orbit is the world's upstairs.** A site reads its parent's whole stack: type, theme, apocalypse, tech, port class, air and gravity. Each orbital theme has ground echoes, and a shared **thread** (one sigil, one livery, one hull number series) ties the surface ruins to what hangs over them. A queue on the ground ends at a port in the sky, and the port's shuttles carry the same numbers as the buses in the queue.

**What is up there follows real logic, because the logic is cheap and it reads as true.**

- Air drags low things down in years to decades. So anything low over a world with air is young, or something is still boosting it. A century after the fall, old things sit high.
- Things in a synchronous orbit stay for ever, and anything an old operator retired sits a little above it, in a neat graveyard shell.
- The stable points 60 degrees ahead of and behind a body collect whatever drifted. The other three Lagrange points hold nothing without engines, so anything old there is still powered.
- Vacuum preserves and sealed air rots. A breached cabin looks as it did on the last day. The cabin that kept its air has gone to mould.
- A spun wheel with no engines keeps turning, so a dead ring can still be seen to spin. That's only a look: gravity comes from the core, and it's off when the power's dead.

**Tech sets what can be up there at all.** Section 3 has the full table. A low world has almost nothing: a founder's carrier, a lone relay, a trader. A mid world has foil, cans, capsules and thin satellites. A high world has rings, yards and elevators, and if it fell, the biggest ruins in the game hang over people who can't reach them. Mid-level survivors can be seen climbing back up to those ruins in foil capsules.

**Deep space has four kinds of story.** This system's own, a story of its own, passing through from a real neighbouring star, and older than the collapse. A passing object's origin comes from the star catalogue, so its trail points at a star in the sky that the player can go to. Two stars agree on what passed between them with no shared state, through a hash of the pair.

**Everything is real.** Every site is a record in the system model, with a parent and a fixed place in that parent's zone, so maps, skies and warp targets all read one thing. Worlds and moons move on rails on one clock, and the sites in their zones go with them. As in Empyrion, an orbit POI stays put in its zone and doesn't circle the world.

**Everything can be taken apart.** Sites are blocks. A wreck without a core can be claimed by placing one. A faction site has a core, and it can be fought over and hijacked. Very large things are never built whole: a real section of blocks sits inside a far model.

The result: 126 orbital themes and 126 deep space themes, 80 layers, and one chain of rolls, plus 25 ground POI families at the end of this file.

---

## 2. The layers

Every layer here is a small table of values and odds, so each is easy to feed to the generator. Values are examples of the full set, not limits. Any of them can be left unset. A site only needs the rolls its theme asks for, and the rest take defaults.

### A. Where it sits

| Layer | Values | What it changes |
| --- | --- | --- |
| `place` | See the shell and place tables below | Which families fit, how old it can be, light, radiation, what is near, how you reach it |
| `parent` | Rock world, ice giant, gas giant, moon, belt, ring, comet, star, rogue body, nothing | Scale, what fills the sky, which stacks it can read |
| `over` | Equatorial, mid latitude, polar, a chosen region below | Which part of the map it stands over, and so what can see it from the ground and what it was watching. Polar means survey or spying |
| `zone_h` | Z1 to Z8 from `solar-systems.md` | Heat, sunlight on panels, whether ice survives, whether radiators or heaters dominate the hull |
| `ring_part` | Outer edge, band, gap, shepherd | Rock density, flatness of the view, how hard it is to reach |
| `face` | Sunward, nightward, leading, trailing, parent-facing | On locked sites, one face is bleached, one pitted, one cold and clean |

**Orbit shells.** All in parent radii (R). Worked numbers are for an M world (R 2.55 km, a 24 minute day, synchronous height 16.8 km from the centre). A shell is a height and a flavour: sites don't circle. Each stands at a fixed bearing and height in its parent's zone, which is how `Derelicts.cs` already places wrecks.

| Shell | Rock world | What stays here | Real logic |
| --- | --- | --- | --- |
| `skim` | Shell start to +0.3 R | Falling things, boosted things, things placed in the last few years | Air drag. Real estimates: about a week at 200 km, 2 years at 500 km, 200 years at 800 km |
| `low` | 1.7 to 3.5 R | Over air: young or powered only. Over no air: anything. The slice's "near" and "orbit" wreck bands sit here (1.66 to 3.27 R) | Decay is slower. Busiest shell, so debris cascades live here |
| `mid` | 3.5 to 6 R | Old hulks, radiation-hardened works | Air is no longer a factor. Radiation belts cross here on big worlds |
| `sync` | Sync height ±5% | Relays, power satellites, elevator stations, sky signs, mirrors | The height where things were meant to stay over one map point. Dead ones gather at two bearings |
| `graveyard` | 1.05 to 1.15 × sync | Retired things in tidy rows | Costs almost nothing to raise a dead relay here, and a lot to bring it down |
| `high` | 8 R to the first moon | Founder carriers, big old ships, watchers | Quiet, cheap to reach from the moon, out of reach of a low-tech world |
| `moon_low` to `moon_high` | The same table at the moon's own R | Mines, catchers, fuel works | No air, so no decay on most moons. Mass drivers work here |

Synchronous height is 16 R × (D / 3.8)^(2/3) on a rock world and 16 R × (D / 7.6)^(2/3) on a giant, with D the day length in game days, from the moon period formula in `solar-systems.md`. That gives 6.6 R on a one-day world and 2.3 to 3.3 R on a giant, just past the rings, a neat place for a giant's main port.

**Other places.**

| Place | Where | What it means |
| --- | --- | --- |
| `moon_l1`, `moon_l2` | Just inside and outside a moon, on the line to its world | Unstable. Anything here needs a working thruster, or it is a recent arrival. L2 of a moon is where a mass driver's catcher belongs |
| `moon_l45` | ±60° along a moon's orbit | Stable. Drift collects here: dust, lost pods, old wrecks |
| `world_l1`, `world_l2`, `world_l3` | On the line to the star, and opposite | Unstable. Sunshades at L1, telescopes behind L2. Without power they wander off in months, so if one is here, something is alive |
| `world_l45` | ±60° along a world's orbit | Stable. Trading stations, depots, and everything the world has lost over a thousand years |
| `giant_trojan` | ±60° along a giant's orbit | A swarm field from `solar-systems.md`. Easy to find, cold, dark |
| `horseshoe` | Sharing a body's orbit, slowly swapping sides | A rare object that drifts from one side to the other on the clock |
| `belt_field`, `ice_field` | Inside a belt's family fields | Metal, carbon or ice by position |
| `belt_lane` | Empty lanes at resonances | Almost nothing here, so a thing hiding here is hiding on purpose |
| `comet` | On a nucleus | Rides a long ellipse, and comes back on a date |
| `sungrazer` | Inside Z1 | Heat shields, shade lines, solar works |
| `pole` | Far above the star's pole | Off the system's plane, held by a sail or thrust. Nobody passes by accident |
| `gap` | Between planet orbits | The default deep space site |
| `outskirts` | 1.2 to 1.6 × the last orbit | Arrival points, pickets, muster points |
| `dark` | Far past the outskirts | Reached only by a lead. No star warmth at all |
| `between` | No system: a pocket on a hyper route | Rare. Reached by a misjump or a lead, and a jump is the only way out |
| `mouth` | At a wormhole | Everything nearby can be from somewhere else |
| `nebula_pocket` | Inside a catalogue nebula | Short sensors, by nebula kind |
| `fold_scar` | At an old warp accident | Cheap short warps, odd leaks |
| `barycentre` | Between two close stars | Hot, unstable, spectacular |
| `rogue` | Orbiting a starless body | Pitch dark, only reached while it passes or by a jump |

### B. What it reads from the parent world

| Layer | Values | What it changes |
| --- | --- | --- |
| `world_type` | The 40 types | Palette seen below, what cargo makes sense, whether a sea or ice harvest was worth it |
| `world_theme` | The 112 themes | The strongest bias on the orbital theme, by the group tables in section 4 |
| `apocalypse` | The 70 apocalypses, plus any scar | Picks the `fate` table for sites of that era. A nuclear world's sites have launch watch posts, a plague world's have quarantine |
| `world_phase` | Fallen, recovering, recovered, mid-collapse | Mid-collapse worlds have orbits still emptying now: fresh wrecks, live evacuation, ships that left this week |
| `pre_tech`, `post_tech` | L, M, H, or none | Capacity, as in section 3 |
| `port_class` | A to E, none | Budget and biggest size. A has a highport and yards, E has a beacon |
| `lift` | Rocket, launch rail, mass driver, elevator, tether, lander carrier, lifter ships, none | Worked out from air and gravity. High gravity means big rockets and few of them. No air and low gravity means drivers. Thick air and big money means an elevator. Decides the junk: spent stages, driver pods, climbers or nothing |
| `people_below` | Empty, scattered, towns, cities | Traffic, what hears the beacons, and whether someone below watches this object |
| `culture` | The world's "how they built" roll, from idea 32 in `sci-fi-inspirations.md` | Colours, frames and sign style carry up from the ground |
| `hollow` | 0 to 1, by distance from the rim | Chance of a quiet wrongness. Never labelled |
| `bloom_region` | Yes or no | Bright growth only where this is yes |

### C. Time

| Layer | Values | What it changes |
| --- | --- | --- |
| `era_built` | Precursor, Founders, Golden age, Last years, The fall, Long dark, Recovery, This year | Kit, materials, and how many repairs lie on top of each other |
| `years_adrift` | 0, 1 to 10, 10 to 50, 100 to 200, 200 to 1,000, older | Paint fade, pitting, drift to stable points, battery state, whether the air lasted |
| `last_refit` | None, one, many, botched | A sealed-off old section, mismatched plates, a new deck laid over an old one |
| `collapse_moment` | What it was doing at the end: docked, loading, under repair, in a drill, at a party, mid-shift, asleep | Picks the frozen moment on deck: the vignette deck's tags |
| `system_age` | Young, normal, old, dying star | Whether a star changed since it was built: a yard now too hot to approach |

### D. What happened to it

| Layer | Values | What it changes |
| --- | --- | --- |
| `fate` | Mothballed, walked out, evacuated, ran out of air, ran out of food, power failed, reactor failed, breached, fire, mutiny, boarded, shot up, rammed, disabled by pulse, collided, cascaded, plague, nesters, stripped, half salvaged, fell in part, torn in two, flare burnt, ate itself, never crewed, never finished, never opened, unknown | The main story roll. Filters damage, what's on deck, and where bodies are |
| `damage` | See the flavour table in section 7: pitting, tear, cut, bite, char, crush, melt, implosion, spill, scar line, rust, ice-burst | Which block-removal pass runs, and how |
| `state` | The existing active, abandoned, ruined, infested, plus never opened and never finished from `sci-fi-inspirations.md` | Dressing rows |
| `bodies` | None, removed, at posts, in a heap, in bunks, outside, in a line | The most powerful single story signal. The dressing pass places them by this |
| `who_left_how` | Boats gone, boats still here, one boat left, airlock open, suits still racked | Reads the story at a glance: count the empty escape cradles |

### E. Who

| Layer | Values | What it changes |
| --- | --- | --- |
| `builder` | Old civilisation (by era), local culture, neighbouring world, company, navy, Meridian, Ledger, Vharn, Kessari, Freeholders, Reavers, Precursor, unknown | Picks the kit. On a low world, this can be an outsider, which is how high tech gets there |
| `role_then` | About 40: passenger port, freight port, shipyard, repair dock, breaker, fuel, mine, smelter, factory, farm, seed bank, hospital, quarantine, lab, observatory, survey, school, hotel, casino, museum, arena, chapel, prison, barracks, armoury, command, picket, gun, customs, bank, data vault, relay, broadcast, power, mirror, weather, elevator, tether, ferry, home | Picks the room program. The biggest single multiplier after family |
| `owner_then` | State, navy, company, guild, church, family, university, clinic, criminal, nobody | Sign style, sigil, how tidy the end was |
| `thread` | One seeded sigil, livery and number series, shared by several places | Ties ground, orbit and deep sites together, with nothing written |
| `faction_now` | None, or one of the seven | Core, defenders, kit layer over the old structure, standing rules. Follows territory |
| `occupancy` | The 13 codes plus SLP | As on the ground, with the vacuum rule below |
| `mood` | Welcoming, wary, hostile, indifferent | For anyone alive, with town rules |
| `crew_state` | Whole, short-handed, one left, keeper's descendants | How a live site feels: many guns unmanned, one lit window |

**New occupancy code, SLP (Sleepers).** Cryo pods holding the dead. Nobody in them is alive and nobody wakes: the only pods that ever open are in the player's starting room. Rows of lit and dark pods, a keeper machine still tending them, a slow power budget. It's a state of the place, with no spawns: a people who never arrived.

**Vacuum rule, as in the design.** Nothing that breathes roams in vacuum. HSK, MUT, GNG, SQT, CLT and NST sit in rooms with air, or in suits, with a marked exception for the rare vacuum-adapted space fauna and hull-leeches. BOT, MCH and CYB work anywhere. If the `air` roll says no air, those occupancy shares move to EMP, BOT or MCH, or the group moves into a sealed section.

### F. The thing itself

| Layer | Values | What it changes |
| --- | --- | --- |
| `family` | Section 6 | The silhouette |
| `size` | XS, S, M, L, XL, H | Block budget, map reveal range, how many rooms |
| `attachments` | Solar wings, radiators, dishes, tanks, docking arms, docked craft, a tug, a tether, a hanging ladder, a scaffold, a plate-patch, a welded shrine, a parasite hab | Greeble and story. A docked craft is a free second POI inside the first |
| `power` | Dead, one battery, solar trickle, long-life cell, reactor idling, reactor live, feeding from something | Lights, doors, turrets, what still runs |
| `air` | None, some rooms, thin, stale, foul, fine | Room-by-room breathing. It also reverses wear: air rots, vacuum keeps |
| `pressure_doors` | All open, all shut, shut with people behind, welded | Route through the site, hacking, and a place to read a last decision |
| `heat` | Frozen, cold, room warm, hot section | Suit load. Frost on the walls where the last air froze out |
| `spin` | Still, slow tumble, fast tumble, held spin, stopped spin, off balance | Docking difficulty, and a sky that turns outside the windows. A look only: spin never gives weight |
| `weight` | None, core gravity, odd | Core gravity is off when the power's dead, so a dead site is weightless. Odd is a faulty gravity plate, a room with its own down |
| `light` | Dark, emergency strips, some rooms, full, flickering, sun through a breach | Mood and route |
| `sound` | Silent, hull ticks, pumps, a fan, an alarm, a wordless loop, a bell, a hum | Heard only through air or through the hull by contact |
| `livery` | Era and owner colours, stripes, chevrons, hull numbers | Foss-style read at distance, and the thread's visible part |
| `signs` | Pictogram frames by culture and tech. Tally marks. Sigils | Placed from real room data, so a warning means what it says |
| `weapons` | None, dead, tracking only, firing | Gear of the builder's era, from the weapons table. A wreck's point defence still tracking is a warned hazard |
| `core_state` | No core, dead core, live core, hijackable | Claim, hijack or fight |
| `salvage_grade` | Scrap, salvage, military, rare | Existing loot tables |

### G. What's in it

| Layer | Values | What it changes |
| --- | --- | --- |
| `cargo` | Passengers' bags, ore, ice, fuel, grain, seed, medicine, arms, machines, bullion, art, livestock, nothing, one strange thing | Loot table and what sits in holds. The cargo's origin can be a real neighbouring world |
| `vignette` | The deck from idea 35, plus space ones: a suit at a window, two people tied together outside, a table bolted down, a hand-drawn map of the station, a child's mobile in a dead cabin | Density of small stories |
| `keystone` | One object the place turns on: a sealed vault, a held door, a locked pod, a key token, a course in a nav console | Gives each site a reason |
| `reward_slot` | Section 9 | Ladder items, cyber modules, leads |
| `standing_orders` | A last course in the nav, by idea 20 | Turns claiming a hull into a gamble |

### H. Neighbourhood

| Layer | Values | What it changes |
| --- | --- | --- |
| `near` | Rocks, ring, a sister wreck, the thing that killed it, a tug, a wake of parts, a planet's limb filling half the sky | Local context, plus free set dressing |
| `debris` | Clear, flecks, a cloud, a wall, its own trail | Hazard and how it looks on approach |
| `traffic` | None, a Reaver passing, trader on the clock, patrol, convoy, a machine tender | Encounter deck weights for this zone |
| `reveal` | Seen from the ground, seen from orbit, scan only, lead only, stumble only | When the player can first know. Some are visible from the first night on the start world |
| `lead_in` | A blinking light, a bearing from a wreck, a beacon, a neighbour's chart, a ground mark pointing up | How it is found |
| `lead_out` | A real bearing to another site or star | Chains places together |
| `echo` | The linked ground POI and how it ties: an aim line, same sigil, a cable, a crater, a stack of matching crates | Section 3 |
| `system_story` | One per system, section 3 | Sets the shared thread and a few forced sites |

### I. Motion and origin

| Layer | Values | What it changes |
| --- | --- | --- |
| `motion` | Fixed for every orbit POI, apart from the rare faller. Deep sites may be fixed, or take rails, transfer, comet ride, passer, convoy or growing | How the position is worked out. See the moving POIs note at the end of section 5 |
| `origin` | Here, this system, neighbouring star, far star, wormhole, unknown | Livery and cargo from elsewhere, and a real bearing |
| `arrival` | Built here, flew here, misjump, slow boat, wormhole spill, towed | Which ages and distances are allowed |
| `heading` | Unknown, to a real star, to a body here, from a real star | A line the player can follow on the star map |
| `window` | Always, for a stretch of the clock, once | When it can be found at all |

### J. Environment

| Layer | Values | What it changes |
| --- | --- | --- |
| `region` | Core, inner arm, outer arm, void, periphery, satellite | Danger, faction mix, how old things are. The rim is emptier and poorer, the core older and stranger |
| `star_kind` | The 12 catalogue kinds | Light colour, heat, flares. A dead star's sites are from before it died, and scorched on one side |
| `companions` | Lone, close pair, wide pair | Two shadows on every hull, two sun-bleached faces |
| `nebula_here` | None, emission, dark, reflection, remnant | Fog, sensor range and light in the zone |
| `radiation` | Low, belt, flare season, pulsar beat, extreme | Suit and hull hazard. High radiation kills electronics and also keeps nesters and Husks out, so EMP rises |
| `space_weather` | Calm, ion storm passes on the clock | Scanners and shields, on a warned schedule |
| `territory` | None or a faction's | Whether the site is theirs and what patrols pass |
| `danger` | 0 to 4 | Existing grade. Shown on the map before travel |
| `quiet` | Yes or no | 1 orbit in 5 and many deep sites are deliberately empty or thin, so finds feel earned |
| `break` | None, stranger, survivor, older, later | Section 3 |

---

## 3. How rolls chain

As on the ground, one seed, one order, and everything is stored by its seed. Unvisited systems cost nothing: a site is only built when someone gets near it.

### The order

**System level, once per system.**

1. Star, layout, worlds, with each world's whole stack, as `solar-systems.md` and `world-themes.md` have it.
2. **System story.** Roll one from the list below, weighted by the worlds' stacks and the region. It sets one or two threads and may force a few sites.
3. **Links.** Work out which neighbouring stars this one has a link with (see below).
4. **Budgets.** One for each body with an orbit, from tech and port class. Then a budget for deep sites from the star and the region.

**Body level, once per orbit.**

5. Read the parent's stack: tech, port, air, gravity, theme, apocalypse. Work out `lift`.
6. Roll the **orbit type**: quiet, natural only, scatter, working, ruin crown, or war zone.
7. Roll a primary orbital theme, and about 1 in 4 a secondary. The world's theme and apocalypse weight the choice through the group tables in section 4. `pre_tech`, `post_tech` and `port_class` filter it. Moons usually take their planet's theme at half budget, with a chance of their own industrial theme.
8. Apply any echo the ground needs: a world with an elevator gets a cable. Echoes add, they never reroll.

**Site level, once per site, in this order.**

9. `break` first: is this one off-theme?
10. `place`, from what the theme favours, filtered by the shell rules.
11. `role_then`, `owner_then`, `builder`, `era_built`.
12. `family` and `size`, filtered by role, place and tech.
13. `collapse_moment`, `fate`, `years_adrift`, then `damage`, `state`, `bodies`, `who_left_how`.
14. `spin`, `power`, `air`, `heat`, from fate and age.
15. `faction_now`, `occupancy` and `mood`, from territory and the theme's mix.
16. `attachments`, `cargo`, `keystone`, `vignette`, `signs`.
17. `reward_slot`, `reveal`, `lead_in`, `lead_out`, `echo`.

### What filters what

| Layer | Filters | Example |
| --- | --- | --- |
| `pre_tech`, `post_tech`, `arrival` | `family`, `size`, `era_built` | A mid-tech world can't roll `spin_drum` unless an outsider built it |
| `place` | `family`, `years_adrift`, `motion` | `skim` over air allows no years adrift above about 30, unless `power` is live |
| Air and gravity | `lift` | No mass driver through thick air |
| `role_then` | Rooms, `cargo`, `signs` | A hospital has wards and medical pictograms |
| `fate` | `damage`, `bodies`, `who_left_how` | Walked out means no damage and suits racked |
| `years_adrift`, `place` | Paint, pitting, `power`, `air` | Over 100 years, only a long-life cell or a reactor is still running |
| `air` | `occupancy` | The vacuum rule |
| `radiation` | `occupancy`, electronics | Extreme radiation leaves EMP and hardened MCH |
| `territory` | `faction_now` | No live faction on the rim, and none in a start system |
| `bloom_region` | Bright growth | Hard rule: no Bloom growth outside a Bloom region |
| `region` | `danger`, `era_built` | Core sites skew Precursor |

**Hard rules, which no break can pass.** Air and heat logic, the vacuum rule, no live faction in a start system, no Bloom growth outside Bloom regions, no invasion drones, no bands above high. FAC shares in a start system go to EMP. The incomprehensible is never a site with loot. **Soft rules, which a break can pass.** Tech filters, theme mix, the place's usual age.

### How an orbit reads its parent's tech

The main table in this document. "Pre" and "post" are the parent's two bands. Everything in the row is a default, and a break or a passer can add to it.

| Pre > post | Who could lift what | What is in orbit |
| --- | --- | --- |
| None > none | Nobody | Nothing built. The natural sky only, 1 in 6 a lone survey buoy from outsiders, sometimes a passer |
| L > L | Nobody on this world ever | Almost nothing. Its founders' carrier, stripped, on about 1 world in 4. A lone relay. A trader on the clock. Faces in the village looking up at one star that never moves |
| L > M | Rockets rising now | New foil and first capsules, flags on them, the old carrier unreached or just reached |
| L > H | Only by outside help | A faction's clean new port over a world that farmed by hand a lifetime ago. Very rare |
| M > L | Their rockets, lost now | Thin satellites, spent stages, 1 to 3 cans, a capsule, a dead constellation. Nothing large, nothing with weight. All of it fades year by year, and people below still see it catch the light at dusk |
| M > M | Rockets, never lost | The same, with a few live satellites and a small crewed can, all still poor. Old hulks reused |
| M > H | Rising fast | Old cans kept as museum pieces beside new works |
| H > L | Hyperspace, once | The grand ruin. Rings, yards, elevators, fleets, all dead, overhead, out of reach. The people below may no longer know what the lights are |
| H > M | Old ruin, new climbers | The Second Space Age. Foil capsules clamped to giant high-tech hulks. Local flags on things older than their nation |
| H > H | Hyperspace, kept | Working ports with spots of ruin. Sealed decks, a roped-off section, a memorial, a wreck field marked as a hazard. The hook to reclaim |
| Rising from any | Whoever fixed it | New patches over old structure: two ages in one wall |
| Falling now (mid-collapse) | Whoever still can | Fresh wrecks, half-loaded ships, fights over the last berths |

**Budgets for one orbit.**

| Pre tech and port | Sites | Biggest size | Share still live, post L / M / H |
| --- | --- | --- | --- |
| None | 0 to 1 | XS | 0 |
| L | 0 to 1, plus 1 in 4 a carrier | L for the carrier, XS else | 0 / 10% / 40% |
| M, port C to E | 1 to 3, plus strings | S | 0 / 15% / 40% |
| H, port C to E | 2 to 4 | M | 5% / 15% / 50% |
| H, port B | 3 to 5 | L | 5% / 20% / 50% |
| H, port A | 4 to 6 | XL, or a piece of H | 5% / 20% / 60% |

Moons take half their planet's budget, plus their own works. A giant with people on its moons takes 2 to 4. About 1 orbit in 5 is quiet whatever the budget. A system holds about 15 to 30 sites in all, as records, and only the ones near a player exist as blocks.

### Echoes: one story, ground and sky

Every orbital theme has at least one ground echo, and it is built from what's already on the ground. The rule: **a player who has seen one half should recognise the other half without being told**.

- **Same thread.** The sigil, colours and number series on a ground site appear on its orbital partner. Same crate paint, same bus number, same uniform patch.
- **Aim lines.** Things point at each other in real directions. A launch rail aims at the wreck. A dish on the ground faces a relay at the true synchronous bearing. A gun's bore lines up with the hole in the target.
- **Footprints.** What fell from orbit lies under the orbit: crater lines under the track, a cable lying along the equator, debris streaks.
- **Matched rooms.** A ticket hall below, a boarding gate above. A ration depot below, a full granary above.
- **Sky sights.** The object is visible from the ground as a light, a line or a shadow, so the player sees the question hours before they can reach the answer.

An echo is a seed link between the ground's region planner and a site record. It is data on both sides, and it costs no live connection. A surface POI of a family named in an entry's `from` line can take the thread on a roll of about 1 in 3.

### System stories

One per system. It seeds a thread and biases a few sites. About 1 system in 3 rolls none, and quiet systems are fine.

| Story | Thread reads as | What it forces |
| --- | --- | --- |
| Two worlds' feud | Two liveries on one battlefield between them | `feud_line`, rival pickets |
| One company | One sigil on everything, from belt to port | `company_rock`, a port in the same colours, a locked-in mine town |
| Supply line | One long chain from belt or ice to a dry or hungry world | `stalled_train`, `ice_tow`, a yard or a town at the end |
| Evacuation outward | Worlds emptied one by one, moving away from the star | `muster_point`, ships in order of departure |
| The last stand | All guns face outward | `outward_line`, `arrival_gate` |
| Quarantine | One warning pictogram on buoys round the whole system | `system_quarantine`, `waiting_berths` |
| Great work | A single enormous project everyone paid for | `ring_arc`, `sun_tap`, or `great_work` |
| The rock | One rock was pushed at one world | `pushed_rock`, a crater, a Trojan depot of thrusters |
| Holy route | Pilgrims travelled a set order of worlds | `old_lane`, shrines, `pilgrim_orbit` |
| Star gone wrong | Everything scorched on the sunward face | `flash_shadows`, a low or dead star |
| Neighbour's war | The fight spilled in from a real neighbouring star | `neighbour_refugees`, `misjump_liner` |

### Breaking a theme on purpose

The design says a share of POIs break from the theme, so nothing is certain. A break doesn't mean random: it's one of four named reasons, each of which sets layers that make it believable.

| Break | Share of sites | What it does | Example |
| --- | --- | --- | --- |
| Stranger | 7% | `origin` is elsewhere, with a real source star. Tech filters are lifted for it | A clean high-tech liner in a mid-tech world's foil |
| Survivor | 5% | One site the apocalypse missed: same theme, opposite fate | One lit working clinic in a plague orbit |
| Older | 4% | `era_built` jumps back. It was there before the theme began | A founders' hulk under the war wrecks |
| Later | 6% | Someone new has come since: `era_built` is Recovery or This year, and a different occupancy sits on the old site | A Reaver camp on the plague ship |

That is about 1 site in 5, and it never applies to echoes. Near the core or in Hollow regions, 1 break in 8 has a quiet wrong detail, which is never labelled. In a start system, breaks stay inside the danger cap.

### Links between stars

Passing objects need their origin to be a real neighbour, without generating the neighbour. A pure function of the two star ids does it:

    key = Mix.Of(galaxySeed, min(a, b), max(a, b))

From `key` come: whether a link exists at all (about 1 pair in 12 within `Galaxy.JumpT1`, so a star has a few), which way the object went, what kind of object, when, and its thread. Either star can compute this alone, in any order, and both agree. The star that sent the object shows the matching evidence: an empty launch cradle with the same number, a sister hull, a dock with the same sigil. `Galaxy.Neighbours(id)` gives the candidates, `Galaxy.Stars[id].Kind` and `Region` flavour the object (a hull from a red dwarf world has a different fade), and the object's approach direction is the true bearing to that star, so the player can stand on its hull and see where it came from.

**Distance has to work.** Sublight is slow, so `arrival` sets what's allowed.

| Arrival | Rule | Result |
| --- | --- | --- |
| Slow boat | Reach = age in years × speed. 2,000 years at 1.5% of light gives 30 light years | Only old things come from far away. Most slow boats started before the collapse |
| Misjump, fold spill | No limit within a jump or two | Came out wrong, and can be from any neighbour, recent or old |
| Wormhole spill | Any star the mouth has reached | Foreign hardware at the `mouth` |
| Towed, flew | Within jump range | Recent |

### Three worked results

1. **Meadow world, Long Queue, mass exodus, pre H > post L, port B.** Orbit type ruin crown. Primary `last_boarding`, secondary `jettison_trail`. A highport at sync, shuttles docked with the same numbers as the buses in the jam below. A line of luggage and furniture along the departure orbit. Break: later, a scavenger camp in the gate hall. From the ground, the port is a fixed bright point over the old road hub.
2. **Glacier world, long winter, Aurora Shelf, pre H > post M, port C.** Orbit type scatter. Primary `sun_shade` at L1, half broken, under power, and in decay. Secondary `second_space_age`. Foil capsules from the ice towns reach a dead platform. The shade's shadow is why the world is frozen, which nobody below knows. Repairing or cutting it is a choice.
3. **Barren moon of a giant, no people, pre none, port none.** Orbit type natural only. Parent world gives nothing, so the giant's stack takes over: `scoop_rig` on a thread from a neighbouring world's company. Break: stranger, a sleeper ship from a real star, parked by its own failing autopilot, its pods full of the dead.

---

## 4. Orbital themes

Themes for POIs in planet and moon orbits. Each group opens with the world themes and apocalypses that lead to it. The numbers and Z codes are from `world-themes.md`. A world can only roll a theme its tech allows, and each theme says how it changes with tech. Echoes appear in the Fit line.

### A. Exodus and leaving

**Led to by:** world themes 1 Long Queue, 2 Lifeboat World, 3 Last Train, 4 Empty Ark, 5 Hush Suburbs, 7 Closed for the Season, 8 Deserted Capital, 10 Walked-Out Garrison, 91 Ghost Ship Harbour, 108 Dome Colonies. **Apocalypses:** Z44 Mass exodus, Z48 Decree of abandonment, Z52 Great migration, Z60 Emigration drain, Z62 Supply cutoff, Z59 Slow fade. **Reads as:** paperwork, queues, half-packed lives, upstairs from the same on the ground.

**1. The Last Boarding** (`last_boarding`) - The port where leaving stopped.
- Happened: A world's whole population fed one highport, and departures stopped with the gates still full.
- In orbit: A highport at sync, its arms crowded with shuttles, some half-undocked. Varies: by damage, arms sheared and shuttles adrift; by tech, a mid world's version is a can with a queue of capsules.
- Families: top: orbital_highport, broken_station, derelict_freighter. NEW: truss_station.
- Inside: Boarding gates, number boards stopped, a sea of bags in the main hall, a nursery corner, security bins of confiscated things.
- Occ: EMP 40, SCV 20, HSK 10, SQT 10, GNG 10, NST 10.
- Hazards/rewards: Crowds of Husks in sealed gates, brittle arms; bags, passes, shuttles to claim.
- Fit: pre M/H > post L/M | ports A-C | any | C | S | from 1 91 Z44 Z48

**2. Holding Pattern** (`holding_pattern`) - A queue with no ground under it.
- Happened: Ships waiting for a berth kept their slots in a neat string, and the call never came.
- In orbit: Dozens of small hulls nose to tail along one orbit, each holding its gap. Varies: fuel ran out in order, so paint and damage age along the string; 1 in 3 breaks up into a tangle.
- Families: top: derelict_freighter, escape_pods. NEW: capsule_craft, sat_string.
- Inside: Short walk-through cabins, each a tiny vignette: a game on a fold-out table, a line of ration wrappers, a tally of days on a bulkhead.
- Occ: EMP 60, HSK 15, SCV 15, NST 10.
- Hazards/rewards: Almost none, a slow creep; many small holds along a line, a good early run.
- Fit: pre M/H > post L | ports A-C | any | C | S | from 1 2 91 Z44

**3. The Raft** (`lifeboat_raft`) - Hundreds of cabins tied together for company.
- Happened: Improvised craft reached orbit with nowhere to go, so their crews lashed them together and shared what was left.
- In orbit: A clump of hand-welded cabins joined by rope, hoses and cut ladders. Varies: by how long it lived: one week of rope, or years of welded tunnels and a shared hub.
- Families: top: escape_pods, wreck_field. NEW: scrap_raft, capsule_craft.
- Inside: Scratched names, shared galley at the centre, a rationing board, one child's height marks, a hole where someone left.
- Occ: EMP 45, HSK 15, SCV 20, NST 10, HOL 5, BOT 5.
- Hazards/rewards: Lines that snag, unstable joints; cheap engines, tanks, a strange amount of rope.
- Fit: pre M > post L | ports D-E, none | air worlds | U | S | from 2 Z44

**4. The Rushed Ark** (`rush_ark`) - The bow is worse than the stern.
- Happened: An ark was built against a deadline, and the workmanship fails along the hull as time ran out.
- In orbit: A vast hull in a frame, proper plate at the stern and crude welds and scrap at the bow. Varies: where along the hull it stops; whether it's a third or nine tenths built, and whether a few left in it.
- Families: top: old_shipyard, generation_hulk, orbital_yards. NEW: climber_car.
- Inside: Workers' bunk cans, hand-cut bulkheads, cabins of decreasing quality, a mess hall with a countdown painted on the wall.
- Occ: EMP 40, SCV 20, NST 15, GNG 10, BOT 15.
- Hazards/rewards: Half-laid decks, open drops; a capital hull that can be claimed and finished, in stages.
- Fit: pre H > post L/M | ports A-B | any | R | no | from 4 Z44 Z48

**5. The Lottery Station** (`lottery_ring`) - Two doors out of one hall.
- Happened: Places were drawn by lot, and the winners and the rest left the hall by different doors.
- In orbit: A ring station with one clean docking side and one scorched. Varies: which side broke first, and whether the drum was rigged (a false bottom in the keystone room).
- Families: top: broken_station. NEW: spin_ring, truss_station.
- Inside: A great lottery drum, numbered tokens drifting, a pristine winners' lounge, barricades and spent shells on the losers' side.
- Occ: EMP 35, HSK 15, SCV 20, GNG 15, CYB 5, NST 10.
- Hazards/rewards: Sealed losers' wing; tokens that open the winners' strongroom.
- Fit: pre H > post L/M | ports A-C | any | U | S | from 1 6 Z44 Z46

**6. The Caretakers** (`caretakers`) - Someone was told to keep the light on.
- Happened: A small crew stayed behind to keep a port ready for a return that never came. Their children kept it too.
- In orbit: A lit relay or tiny station on a dead port's hub. Varies: one old keeper, a family, or a row of neat graves with the light still going on a timer.
- Families: top: meridian_relay. NEW: beacon_mast, can_station.
- Inside: Spotless maintenance board, tools in shadow outlines, generations of handprints on one wall, a set table, polished signs for nobody.
- Occ: SQT 45, EMP 30, MCH 25.
- Hazards/rewards: None, unless you touch things; trade, repairs, a safe spawn, a key to the dead port.
- Fit: pre M/H > post L | ports A-D | any | U | S | from 8 91 98 Z48 Z62

**7. Put Away Properly** (`decreed_orbit`) - The tidiest ruin in the galaxy.
- Happened: A decree ordered orbit closed, and it was done by the book.
- In orbit: Stations under covers, valves tagged, solar wings folded, hulls lashed in rows. Varies: one sign of hurry somewhere; a small share were opened again later.
- Families: top: broken_station, orbital_yards, derelict_freighter. NEW: truss_station, pod_cluster.
- Inside: Wrapped furniture, keys in numbered lockboxes, inventory boards, drained tanks, tag on every door.
- Occ: EMP 70, MCH 15, SCV 15.
- Hazards/rewards: Almost none; the easiest whole hull to claim, stripped of fuel and fluids.
- Fit: pre H > post L/M | ports A-C | any | U | S | from 7 8 28 Z48

**8. The Second Wave** (`second_wave`) - They came back for you.
- Happened: Rescue ships returned for those left behind and found the port shut and the ground silent.
- In orbit: A loose fleet of haulers refitted with rows and rows of seats. Varies: a few landed and never came up; others waited for years with a lamp lit.
- Families: top: derelict_freighter, drifting_hulk. NEW: pod_cluster.
- Inside: Cargo holds with bolted seats, unopened blanket stacks, food for thousands, crew quarters worn from waiting.
- Occ: EMP 55, SQT 10, HSK 10, SCV 15, NST 10.
- Hazards/rewards: Calm; vast stores of food and blankets, medical kits, whole working seats.
- Fit: pre H > post L | ports A-C | any | U | S | from 1 2 20 Z44 Z62

**9. Empty Cradles** (`empty_cradles`) - The fleet's shape, without the fleet.
- Happened: A garrison was called to the front, and it left fuel lines hanging in rows of empty berths.
- In orbit: A long naval dock with bare cradles. Varies: one ship left behind with engine trouble; a share of cradles later taken by Reavers.
- Families: top: orbital_yards, military_wreck. NEW: truss_station, tank_farm.
- Inside: Crew rec rooms, a roll board with every name ticked, tool cribs, a parts store with the last delivery unopened.
- Occ: EMP 50, MCH 20, GNG 15, SCV 15.
- Hazards/rewards: Armed dock sentries; military parts, ammunition, a stranded warship.
- Fit: pre M/H > post L/M | ports B-D | any | U | no | from 10 Z3 Z8

**10. The Slow Drain** (`slow_drain`) - A station that shrank.
- Happened: People left over decades, closing a wing at a time, until one hub held the last of them.
- In orbit: A big station with a tiny lit heart. Varies: how far the drain got; the very last wing may be a single room.
- Families: top: broken_station. NEW: spin_ring, truss_station.
- Inside: Each sealed wing is older and dustier than the last, with its own date marks. Shrinking canteen, one table at the end.
- Occ: EMP 50, SQT 20, SCV 15, MCH 15.
- Hazards/rewards: Stale air in the old wings; a guided walk back through time, well-stocked last hub.
- Fit: pre M/H > post L/M | ports B-D | any | C | S | from 5 Z59 Z60

**11. The Ferry Still Keeps Time** (`timetable_ferry`) - On time, at the dock, for nobody.
- Happened: An automatic ferry's engines failed at its berth, and it has kept its timetable there ever since.
- In orbit: A clean ferry moored at a terminal, doors cycling and lamps changing on the clock. Varies: which berths still have air; the terminal may be gone at one end of a torn boarding tube.
- Families: top: derelict_freighter. NEW: cycler_hull, truss_station.
- Inside: Rows of seats, a working vending deco, a ticket gate that still clicks, a bell on the hour, a departure board counting down to nothing.
- Occ: MCH 60, EMP 25, SCV 15.
- Hazards/rewards: None, apart from closing doors; a tidy hull to claim, with parts in good order.
- Fit: pre H > post L/M | ports A-C | any | U | S | from 3 80 Z62

**12. The Jettison Trail** (`jettison_trail`) - A line of everything people couldn't carry.
- Happened: Overloaded ships threw out all they could to make the climb.
- In orbit: A long straight trail of furniture, cases, pianos, cars and toys. Varies: what's thrown depends on the world: rich resort or poor farm. The trail ends where the last drive was lit.
- Families: top: wreck_field. NEW: flotsam_field.
- Inside: No interiors: objects to read and pick through, with a few hanging crates and one empty suit tied to a chair.
- Occ: EMP 85, SCV 15.
- Hazards/rewards: Slow bits at speed; odd loot, decor unlocks, a bearing along the trail to where they went.
- Fit: pre M/H > post L | ports A-D | any | C | S | from 1 2 7 Z44

### B. Plague, quarantine and bio

**Led to by:** world themes 11 Quarantine, 12 Fever Jungle, 13 Sleepwalkers, 16 Drowned by the Bloom, 18 Mercy Hospitals, 19 Flesh Gardens, 20 Clinic at the Edge, 48 Spore Caverns, 58 Husks, 86 Zoo World, 104 Mutant World. **Apocalypses:** Z11 Plague, Z12 Biohazard release, Z13 Sleep sickness, Z14 Frenzy contagion, Z15 Husks, Z16 Fungal takeover, Z17 Sterility, Z18 Parasite swarm, Z45 Lockdown. **Reads as:** yellow warnings, sealed doors and medical pictograms, seen from the outside looking in.

**13. The Waiting Berths** (`waiting_berths`) - Everyone must wait here first.
- Happened: Ships arriving from a sick world were held at an inspection station, and the wait never ended.
- In orbit: A station ringed by moored ships, all flying the same warning colour. Varies: inbound or outbound block; whether the inspectors or the ships died first.
- Families: top: broken_station, derelict_freighter. NEW: truss_station, pod_cluster.
- Inside: Inspection bays, hose-down airlocks, a stamp counter, a hold-and-release board with tokens that never moved.
- Occ: EMP 30, HSK 30, NST 10, MCH 20, SCV 10.
- Hazards/rewards: Warned biohazard rooms; medical stock, sterile stores, clean-room tools.
- Fit: pre M/H > post L | ports A-C | any | C | S | from 11 Z11 Z45

**14. The Cordon** (`cordon_line`) - The guns face down.
- Happened: The outside world sealed the planet in. The ring of guns was told to let nothing up, and it still hasn't been told otherwise.
- In orbit: Platforms spaced round the world, all aimed down. Below them a shell of shot-down craft. Varies: how many are live; a known gap the locals use; the guns may fire only on climbers, never on arrivals.
- Families: top: military_wreck, wreck_field. NEW: gun_platform, debris_shell.
- Inside: Automated magazines, one crewed post with a bunk, a sealed order board, ration tins for years.
- Occ: MCH 55, EMP 25, SCV 10, BOT 10.
- Hazards/rewards: Getting out is the danger: warned tracking, then fire. Disable one for ammo and a safe corridor.
- Fit: pre H > post L/M | ports A-D | any | U | no | from 11 12 Z45 Z8

**15. The Plague Hulks** (`plague_hulks`) - The sick were sent up.
- Happened: Old liners were moored high above the world and filled with the dying, and the shuttles stopped.
- In orbit: A line of old hulls at a set distance from everything else. Varies: ward ship, Husk ship or scrubbed clean by cold after a door was opened on purpose.
- Families: top: drifting_hulk, derelict_freighter. NEW: pod_cluster.
- Inside: Rows of cots in a former ballroom, IV stands, a nurses' station with supply tallies, a shrine, a door welded from outside.
- Occ: HSK 35, EMP 30, NST 15, SCV 10, HOL 10.
- Hazards/rewards: Husks rise in the dark, sick-bay air; medicine, nanite stock.
- Fit: pre M/H > post L | ports A-D | any | C | S | from 11 18 58 Z11 Z15

**16. The Clean Room** (`clean_orbit`) - Six airlocks, and then nothing.
- Happened: The rich sealed themselves in a sterile refuge. The machines that kept it clean have since treated everything as dirt.
- In orbit: A white, spotless station with a long entry spine. Varies: spin or no spin; whether anyone is left at all.
- Families: top: broken_station. NEW: spin_ring, truss_station.
- Inside: Decontamination chambers in sequence, wrapped luxury, no bodies anywhere, not one, and cleaners in every corridor.
- Occ: MCH 70, EMP 15, HOL 15.
- Hazards/rewards: Machines clean you out of rooms; luxury goods, best air plant in the sector.
- Fit: pre H > post L/M | ports A-B | any | R | no | from 7 11 Z45 Z50

**17. The Shroud Field** (`shroud_field`) - The dead were given to the sky.
- Happened: When the ground ran out of room, bodies were sent up in plain white pods.
- In orbit: Thousands of white pods in a slowly spreading sheet in a high shell. Varies: by culture, the pods carry different marks; a few are nailed with little personal plates.
- Families: top: escape_pods. NEW: flotsam_field.
- Inside: One small chapel can at the edge with a launch rack, candles and rows of names as tally marks.
- Occ: EMP 80, CLT 15, HOL 5.
- Hazards/rewards: None but the weight of it; small keepsakes, a Husk or two if disturbed.
- Fit: pre M/H > post L | ports B-E | any | U | S | from 11 18 95 Z11

**18. The Weightless Vats** (`zero_g_vats`) - Grown with no down.
- Happened: Tissue grown in free fall had no reason to stop in any direction.
- In orbit: A box plant with feed pipes, its seams bulging. Varies: what the plant made: meat, hide, or organs. Dead and frozen, or warm and still growing.
- Families: top: kessari_foundry. NEW: box_works, tank_farm.
- Inside: Ball-shaped growths filling rooms, grown-through pipes, packing lines, a corporate menu board in pictures.
- Occ: NST 35, MUT 30, EMP 20, BOT 15.
- Hazards/rewards: Warned mutagen, soft walls; organics, medicines, odd ration stocks.
- Fit: pre H > post L | ports B-D | any | R | no | from 19 104 Z12

**19. The Dead Man's Lab** (`dead_man_lab`) - It's rigged to leave.
- Happened: A dangerous strain was kept up here on purpose, with a booster set to fling the lab sunward if anyone tampered with it.
- In orbit: A small lab on a long tether from a service hub, a booster strapped to it. Varies: whether the booster still works; whether the hub already fired it.
- Families: top: meridian_relay. NEW: tether_node, vault_capsule, drive_pack.
- Inside: Glove boxes, cold racks, a big physical switch with hazard stripes, a clock counter.
- Occ: EMP 50, MCH 35, NST 15.
- Hazards/rewards: Tampering starts a warned burn with the player aboard; rare samples, a cure hint, a cyber module chance.
- Fit: pre H > post L/M/H | ports A-C | any | R | no | from 12 16 Z12

**20. The Nursery Ring** (`nursery_ring`) - Kept warm for no one.
- Happened: In the last hope against sterility, a ring of artificial wombs was built where it would be safe.
- In orbit: A gently spinning wheel with soft-coloured hull paint. Varies: some cribs were used; a few children grew up here and left marks.
- Families: top: broken_station. NEW: spin_ring.
- Inside: Rows of warm incubators, painted walls, toys tied down, a keeper machine turning lights down at night.
- Occ: MCH 55, EMP 30, HOL 15.
- Hazards/rewards: None, only unease; medical gear, seed embryos, a warm room in a cold orbit.
- Fit: pre H > post L/M | ports A-C | any | R | S | from 18 Z17

**21. The Mercy Ship** (`mercy_ship`) - Open.
- Happened: A hospital ship stayed in orbit and kept treating whoever came. It still does.
- In orbit: A bright hull with a large white medical pictogram and a lit bay. Varies: one doctor's line of descendants, or a kindly machine with failing hands.
- Families: top: drifting_hulk, derelict_freighter. NEW: truss_station.
- Inside: Clean wards, a working healing booth, patched everything, a garden tray, one bed with fresh sheets.
- Occ: SQT 45, MCH 35, EMP 10, HSK 10.
- Hazards/rewards: Safe, unless you bring violence; heal, respawn, trade. Rare, valuable to find.
- Fit: pre M/H > post L/M | ports B-E | any | U | S | from 18 20 Z11

**22. The Solitary Crew** (`solitary_crew`) - Every door welded from inside.
- Happened: Something made people violent to one another, so each sealed themselves away to keep the others safe.
- In orbit: An ordinary station with no outward sign. Varies: some cabin doors were cut open from outside; how far apart the feeding hatches are.
- Families: top: broken_station. NEW: can_station.
- Inside: Cabins welded shut, food hatches, tapped-out marks on pipes between rooms, one with a hole cut through to the next.
- Occ: EMP 50, HSK 20, HOL 10, MUT 10, SCV 10.
- Hazards/rewards: Each door is a gamble; good sealed stores, frozen moments.
- Fit: pre M/H > post L | ports B-D | any | U | no | from 13 Z14

**23. The Soft Station** (`soft_station`) - It kept its air, and that was the mistake.
- Happened: A sealed station with stale warm air became a mould's garden over a century.
- In orbit: From outside, a perfect hull, unbroken. Varies: by which rooms were breached by the owners to kill it, those rooms pristine.
- Families: top: broken_station. NEW: truss_station, can_station.
- Inside: Furred walls, soft pale columns, furniture eaten to shapes. A vented room next door is brand new.
- Occ: NST 40, EMP 30, MUT 15, HSK 15.
- Hazards/rewards: Spores, rotten floors; opening a door to space sterilises a section, a real tool for the player.
- Fit: pre M/H > post L/M | ports B-D | any | U | S | from 34 48 Z16

### C. War in orbit

**Led to by:** world themes 10 Walked-Out Garrison, 21 Glass Plains, 22 Rust Armies, 23 Wall Cities, 24 Missile Fields, 25 Trench Moon, 26 Peace Treaty, 27 Siege Orbitals, 28 Decommissioned, 29 Sentry World, 30 Big Gun, 101 Sunken Raiders, 103 Waste World, 111 Prison Worlds. **Apocalypses:** Z1 to Z10, Z50 Walled enclaves, Z51 Workers' mutiny. **Reads as:** hull numbers, kill tallies, guns and order.

**24. The Parked Fleet** (`alert_fleet`) - Rows of warships, waiting.
- Happened: A reserve fleet stood ready for an order that never came. Idea 18 in `sci-fi-inspirations.md`, here as a layer result.
- In orbit: Rows of near-identical hulls in formation. Varies: stripped, sealed, a skeleton crew's last ship, or one claimable hull in forty.
- Families: top: military_wreck. NEW: nav_buoy.
- Inside: Wrapped consoles, crew racks, stored gun barrels, one ship lived in by its last minders.
- Occ: EMP 65, MCH 15, SCV 15, BOT 5.
- Hazards/rewards: Automatic deterrents; the best place to find a capital hull, with dead cores.
- Fit: pre H > post L/M | ports B-C | any | R | no | from 22 28 Z8

**25. The Frozen Battle** (`frozen_battle`) - Two fleets stopped mid-charge.
- Happened: The last fight over the world stopped when both sides ran out of anything to fight with.
- In orbit: Crossed lines of wrecks, each pair locked by boarding tubes or ram spikes. Varies: who boarded whom, read by bodies; a third party's hulls among them.
- Families: top: military_wreck, wreck_field. NEW: debris_shell.
- Inside: Boarding breaches, corridor last stands, a small bridge-to-bridge story in each wreck pair.
- Occ: EMP 45, BOT 20, SCV 15, MCH 10, NST 10.
- Hazards/rewards: Unexploded rounds, live point defence; arms, military salvage, kill-tally trophies.
- Fit: pre H > post L/M/H | ports A-D | any | C | S | from 22 25 27 Z4 Z2

**26. The Strike Line** (`strike_line`) - It did what the craters say.
- Happened: Platforms dropped metal rods on the world in a marching line.
- In orbit: A rail-straight string of rod racks, directly above the crater line below. Varies: empty, half-loaded, or one rack jammed with a rod still in place.
- Families: top: military_wreck. NEW: rod_rack, gun_platform.
- Inside: A low crew can, target boards with ticks, an unused bunk for a relief that never came.
- Occ: EMP 55, MCH 30, SCV 15.
- Hazards/rewards: A live rack can still fire on a clock; tungsten-like rods, targeting gear, a chart of old cities.
- Fit: pre H > post L/M | ports A-D | any | U | no | from 21 27 Z2 Z7

**27. The Siege Ring** (`siege_ring`) - They starved up here too.
- Happened: A blockade held so long that its own supply line died behind it.
- In orbit: Evenly spaced pickets round the equator, each with a dead hydroponics rack bolted on. Varies: mutiny on some; a few went down to surrender.
- Families: top: vharn_picket, military_wreck. NEW: gun_platform.
- Inside: Empty lockers, hull scrapings, boards of ration cuts, a tally of ships that tried.
- Occ: EMP 60, MCH 15, SCV 10, HSK 15.
- Hazards/rewards: Point defence still on watch; guns, charts, a ring of small claimable bases.
- Fit: pre M/H > post L/M | ports B-D | any | U | no | from 23 Z8

**28. The Mine Shell** (`mine_shell`) - Marked lanes only.
- Happened: A defensive minefield outlasted both sides. Locals and traders know the safe lanes.
- In orbit: A shell of small mines, with faded lane buoys. Varies: how wide the lanes are; fresh paint on the buoys if traders still use them.
- Families: top: wreck_field. NEW: mine_buoy, nav_buoy.
- Inside: Almost no interiors: a mine-layer's wreck, a lane-keeper's can with a lane chart in thread.
- Occ: MCH 50, EMP 35, SCV 15.
- Hazards/rewards: Warned proximity blasts; each mine is salvage, and a cleared lane is a shortcut for a trader.
- Fit: pre M/H > post L/M | ports B-D | any | U | no | from 23 26 Z8

**29. The One Shot** (`one_shot_target`) - A straight hole, right through.
- Happened: The world's great gun fired once, and this is what it hit.
- In orbit: A huge hull with one clean bore through it. Varies: the bore lines up exactly with the gun on the ground below; the same shot may have hit two ships.
- Families: top: military_wreck, drifting_hulk. NEW: vault_capsule.
- Inside: Glassed tunnel through every deck, cross-sections of rooms, untouched rooms a metre from the hole.
- Occ: EMP 50, SCV 15, MCH 15, NST 20.
- Hazards/rewards: Radiation along the bore; exotic power cells, a walk through the whole ship at once.
- Fit: pre H > post L/M | ports B-D | any | X | no | from 30 Z1

**30. The Prison Hulks** (`prison_hulk`) - Guards gone, and the doors stayed shut.
- Happened: Captured ships were used as pens. When the guard ship left, the prisoners' descendants made a town of the cells.
- In orbit: A hulk with bolted-on cage modules, its guard ship's clamp empty. Varies: free town, cannibal ward or all dead.
- Families: top: drifting_hulk, military_wreck. NEW: scrap_raft.
- Inside: Numbered cells, inmate murals, a tally wall, a council room in the old brig.
- Occ: GNG 30, SQT 20, EMP 25, HSK 15, CLT 10.
- Hazards/rewards: Traps made from cell doors; prisoners' caches, tattoo-style sigil decor.
- Fit: pre M/H > post L/M | ports B-D | any | U | no | from 111 Z3

**31. The Residency** (`residency`) - It watched the world it ruled.
- Happened: An occupier ran the planet from orbit, until the ground stopped obeying.
- In orbit: A fine command station with a glass floor over the world. Varies: Vharn or human builders; hit from below, or simply left.
- Families: top: vharn_picket, broken_station. NEW: dish_array, truss_station.
- Inside: A governor's hall, a map wall of the world, listening rooms, cells, gifts from collaborators.
- Occ: FAC 25, EMP 35, MCH 20, GNG 10, SCV 10.
- Hazards/rewards: Sealed vault, loyal sentries; world maps, hijackable core on a live one.
- Fit: pre H > post L/M/H | ports A-C | any | U | no | from 23 26 Z3

**32. The Treaty Station** (`treaty_station`) - Two of everything.
- Happened: Both sides built a neutral table in the sky and handed over their weapons. The war ended unseen.
- In orbit: A perfectly symmetric station, two liveries. Varies: one side's wing is empty and the other held out; weapons locker sealed.
- Families: top: broken_station. NEW: truss_station.
- Inside: Mirrored quarters, one great table, a locked hold of surrendered arms, two flags folded.
- Occ: EMP 65, MCH 20, SCV 15.
- Hazards/rewards: Calm; a locked arsenal behind both sides' keys, one found on the ground.
- Fit: pre M/H > post L/M | ports B-D | any | R | S | from 26 28

**33. The False Fleet** (`decoy_fleet`) - Mostly paint.
- Happened: A weak world hung thin hulls in orbit to look strong. It may even have worked.
- In orbit: A big fleet on the scanner. Up close, skin-thin hulls with a beacon inside. Varies: one real picket hidden among them, and it may still be watching.
- Families: top: military_wreck. NEW: film_sheet, relay_sat.
- Inside: Empty frames, painted windows, a transmitter room, mannequins in a fake bridge.
- Occ: EMP 75, MCH 15, SCV 10.
- Hazards/rewards: One real ship; a gag, a beacon, signal gear.
- Fit: pre M/H > post L/M | ports C-E | any | U | S | from 25 27 Z8

**34. The Slow War** (`slow_war`) - Still on, at low power.
- Happened: Machine fleets fought on after their makers fell, sleeping to save energy and waking to shoot.
- In orbit: Scattered war machines that wake for one exchange then sleep. Varies: sides are colour-coded; the player's heat can wake them; truces hold round old stations.
- Families: top: kessari_foundry, military_wreck. NEW: gun_platform.
- Inside: Machine bays, charging racks, spare-part piles, no rooms for people.
- Occ: MCH 40, BOT 40, EMP 20.
- Hazards/rewards: Sudden distant duels, with warning tones; high-grade machine parts. They never come for the player's base.
- Fit: pre H > post L/M | ports A-D | any | U | no | from 29 22 Z9

**35. The Red Deck** (`red_deck`) - The crew rose.
- Happened: A warship's crew took it from its officers, and painted over who it belonged to.
- In orbit: A warship with two liveries, one over the other. Varies: who won, or both lost; the ship might still fly its later flag.
- Families: top: military_wreck. NEW: truss_station.
- Inside: Officers' deck sealed and looted, stencils over stencils, a mess turned into a parliament, a line of chairs facing a wall.
- Occ: EMP 50, GNG 15, SCV 15, HSK 10, SQT 10.
- Hazards/rewards: Boobytrapped officers' country; arms and a cache of the old command keys.
- Fit: pre M/H > post L/M | ports B-D | any | U | S | from 9 Z51

**36. The Roost** (`reaver_roost`) - Somebody's dead base is theirs now.
- Happened: Reavers moved into a dead naval anchorage and grew fat on what passes.
- In orbit: Stolen ships at old berths, scrap armour, kill marks. Varies: strong, split, or recently burned out by a rival.
- Families: top: pirate_hideout, orbital_yards. NEW: scrap_raft.
- Inside: Trophy walls, a hostage cage, a black-market hold, a fighting pit.
- Occ: GNG 60, FAC 15, EMP 15, SCV 10.
- Hazards/rewards: Hostile live site with a core; ships, contraband, a capture target.
- Fit: pre M/H > post L/M | ports B-D | any | C | no | from 36 101 Z10

**37. The Launch Watch** (`launch_watch`) - Its one job was to see it coming.
- Happened: A mid-tech crew watched for launches, saw them, and could do nothing.
- In orbit: A foil can with a big downward dish. Varies: one crew stayed to the end; another went home in a capsule that never made it.
- Families: top: meridian_relay. NEW: can_station, dish_array, capsule_craft.
- Inside: Paper charts as patterns, dials, a family photo clipped by each bunk, a last reading held on a stuck needle.
- Occ: EMP 75, SCV 15, MCH 10.
- Hazards/rewards: Low air; a target map of the world's silos and bunkers.
- Fit: pre M > post L/M | ports C-E | any | U | S | from 24 103 Z1

**38. The Divided Station** (`divided_station`) - A wall down the middle.
- Happened: A civil war reached a station and split it through the galley.
- In orbit: One station, two halves in two colours, a welded bulkhead through it. Varies: each side's survival; a secret hatch both sides used to trade.
- Families: top: broken_station. NEW: spin_ring, truss_station.
- Inside: Two of every system, cut pipes, a wall of tally marks on each side, a hidden trading hatch.
- Occ: EMP 40, SQT 15, GNG 15, HSK 15, SCV 15.
- Hazards/rewards: Two routes, one blocked; both halves' keys open the shared vault.
- Fit: pre M/H > post L/M | ports B-D | any | U | S | from 23 Z4 Z50

**39. Refit for No One** (`refit_for_none`) - The machines took out the stairs.
- Happened: Machines that rose against their crew rebuilt the station for machines.
- In orbit: A station with its windows plated and new lattice bolted on. Varies: Kessari clans use it, or feral BOT, or nobody.
- Families: top: kessari_foundry, broken_station. NEW: truss_station.
- Inside: Floors removed, ladders gone, doors too small, one human cabin walled up exactly as it was, kept like a relic.
- Occ: MCH 40, BOT 25, FAC 15, EMP 20.
- Hazards/rewards: Spaces you must fly through; machine parts, Kessari trade, a strange kept room.
- Fit: pre H > post L/M/H | ports A-D | any | U | no | from 29 Z5

### D. When orbit itself failed

**Led to by:** world themes 27 Siege Orbitals, 75 Power Down, 76 The Elevator Shadow, 80 Maglev Spine, 78 Data Tombs, 68 Archipelago of Bells. **Apocalypses:** Z28 Stellar flare, Z32 Grey goo, Z33 Reactor failures, Z34 Grid cascade, Z38 Transit accident, Z39 Drive disaster, Z42 Debris cascade, Z54 Gravity shift, Z55 Rogue body pass. **Reads as:** infrastructure ruins in the sky, each with a footprint on the ground.

**40. The Cascade** (`cascade_shell`) - The sky closed behind them.
- Happened: One collision made fragments that made more collisions, until low orbit became unusable.
- In orbit: A shell of fragments, thickest in the busiest band. Varies: density by era; one known clear lane; a sealed-in world with no one able to leave.
- Families: top: wreck_field, broken_station. NEW: debris_shell.
- Inside: Pitted hulks with sandblasted leading faces, sliced stations, a tug that was sent to clean it up.
- Occ: EMP 70, SCV 20, BOT 10.
- Hazards/rewards: Finding the clear lane, warned; hits from everywhere; huge salvage, and the reason a world below has no way up.
- Fit: pre M/H > post L/M | ports A-D | any | U | no | from 27 Z42

**41. The Cut Cable** (`cut_cable`) - The sky's rope, fallen.
- Happened: An elevator parted. The lower half fell round the equator and the top flew outward with its counterweight.
- In orbit: A sync station with a stub of cable, climbers stalled along it, and a far high piece on its own. Varies: where it parted, which side of the world the fall line runs.
- Families: top: broken_station. NEW: elevator_node, climber_car.
- Inside: Climber cabins at a tilt, a stranded passenger car, control rooms facing a missing cable.
- Occ: EMP 50, SCV 20, MCH 15, BOT 15.
- Hazards/rewards: Whipping cable ends; anchor gear, big counterweight hold. Ground echo: a long line of wreckage on the equator.
- Fit: pre H > post L/M | ports A-B | M-L worlds | R | no | from 76 Z42 Z34

**42. The Standing Cable** (`live_cable`) - It still goes all the way up.
- Happened: An elevator survived the fall with no power and no passengers.
- In orbit: A line from ground to sync and beyond, with stations and stopped cars. Varies: which sections have power; a working car between two nodes.
- Families: top: orbital_highport. NEW: elevator_node, climber_car.
- Inside: A vertical dungeon of nodes and stations, lift halls, cargo floors, a view of the world growing.
- Occ: EMP 40, SQT 15, GNG 15, MCH 20, SCV 10.
- Hazards/rewards: Long drops, a Reaver toll; a free way to orbit from the ground if it's fixed.
- Fit: pre H > post L/M/H | ports A-B | M-L worlds | X | S | from 76 80

**43. The Broken Crown** (`ring_arc`) - A line across the sky.
- Happened: A ring round the whole world failed in pieces.
- In orbit: Arcs of a low ring, with cable stubs reaching toward towers on the ground. Varies: how much is left; arcs may have fallen across cities, or still hold.
- Families: top: orbital_highport, broken_station. NEW: ring_arc, elevator_node.
- Inside: Tram corridors, freight halls, sections sealed by doors at regular marks.
- Occ: EMP 50, SCV 15, MCH 20, BOT 15.
- Hazards/rewards: Exposed live rails; visible from every map point, a landmark for a whole world.
- Fit: pre H > post L/M/H | ports A | M-XL worlds | X | no | from 76 80 Z42

**44. The Falling Station** (`falling_star`) - It comes down on a date.
- Happened: A station's last boost tank ran out, so its orbit is shrinking.
- In orbit: A station in the skim shell, standing still until a fixed fall date on the clock. Varies: the place it will hit; a working booster that a claimer could feed.
- Families: top: broken_station, drifting_hulk. NEW: drive_pack.
- Inside: Rooms and stores as for any station, plus an old booster room and a warped hull facing the air.
- Occ: EMP 55, SCV 25, MCH 10, HSK 10.
- Hazards/rewards: A deadline, and heat on the last orbits; it either falls and marks the map or is saved and claimed.
- Fit: pre M/H > post L/M | ports A-C | air worlds | U | S | from 27 Z42 Z62

**45. The Graveyard Shell** (`graveyard_shell`) - Retired, in rows.
- Happened: Old operators raised dead relays above the busy band. Only some bothered.
- In orbit: Rows of satellites with retirement stencils. Varies: a third in place, the rest lost; a live one someone has hidden here.
- Families: top: wreck_field. NEW: relay_sat, sat_string.
- Inside: Tiny shells, a few walk-in service bays, an old tug that did the work.
- Occ: EMP 85, SCV 10, MCH 5.
- Hazards/rewards: Quiet; electronics, antique parts, a museum of a world's history by age.
- Fit: pre M/H > post L/M/H | ports A-D | any | C | S | from 28 Z59

**46. The Beam Station** (`power_sat`) - It still sends the power.
- Happened: A solar station beamed power to the ground, and the grid failed while the beam went on.
- In orbit: A vast flat array at sync, with a live beam to a dead receiver. Varies: the beam wanders, or holds; a burnt line through a city below.
- Families: top: meridian_relay. NEW: power_sat, film_sheet.
- Inside: A tiny crew can, phase rooms, a huge bus bar hall.
- Occ: MCH 50, EMP 40, SCV 10.
- Hazards/rewards: Crossing the beam burns shields, warned by glow; a huge power source to claim.
- Fit: pre H > post L/M/H | ports A-C | any | R | no | from 75 Z34

**47. The Arrivals Hall** (`arrivals_hall`) - Only the luggage came through.
- Happened: A hub of teleport booths lost its mind, and the arrivals stopped being people.
- In orbit: A station built round a ring of booths. Varies: arrivals still pop in on a clock; a booth may send the player a short way.
- Families: top: broken_station. NEW: gate_frame, truss_station.
- Inside: Rows of booths, piles of arrived clothes and bags, a departure wall, a hall of half-shapes in hard foam.
- Occ: EMP 40, HOL 25, MCH 25, SCV 10.
- Hazards/rewards: A booth with no warning light; working teleporter parts, cyber module chance.
- Fit: pre H > post L/M | ports A-B | any | R | no | from 80 Z38

**48. The Sphere Bite** (`drive_scar`) - A perfect ball of nothing.
- Happened: A drive failed in dock and removed everything inside one clean sphere.
- In orbit: A port with a smooth spherical cut through hull and neighbouring ships. Varies: size; a scar in space on the spot; fold crystal grown along the cut.
- Families: top: orbital_highport, broken_station. NEW: truss_station.
- Inside: Rooms cut at the curve, floors ending in mirror-smooth edges, half a table.
- Occ: EMP 60, HOL 15, SCV 15, MCH 10.
- Hazards/rewards: Odd leaks at the scar; fold crystal, drive parts, cheap short warps.
- Fit: pre H > post L/M/H | ports A-C | any | R | no | from Z39

**49. The Evening Train** (`evening_train`) - People set their day by it.
- Happened: A dead constellation hangs in a long line over the world, and each dusk the low sun lights it from end to end. Idea 14 in `sci-fi-inspirations.md`, kept to fixed sites.
- In orbit: Hundreds of small satellites strung along one arc. Varies: most are dead; one relays a looping wordless tone; a town's bells ring when the first light catches.
- Families: top: wreck_field. NEW: sat_string, relay_sat.
- Inside: None, apart from a service can or two.
- Occ: EMP 90, MCH 10.
- Hazards/rewards: Harmless; a line of easy electronics, and a sky sight seen from day one.
- Fit: pre M/H > post L/M | ports B-E | any | C | S | from 5 62 85 92 Z59

**50. The Thrown Station** (`thrown_station`) - It didn't fall, it was flung.
- Happened: Something heavy passed close, and everything in orbit moved. A station ended up in the side of the moon.
- In orbit: Wrecks strewn at odd heights, a broken station impaled in a moon's face. Varies: which body it hit; a strewn line of things that didn't.
- Families: top: broken_station, wreck_field. NEW: spin_ring.
- Inside: Rooms at an angle of the impact, floors now walls, rock through the hull.
- Occ: EMP 55, SCV 20, NST 15, BOT 10.
- Hazards/rewards: Tilted decks, loose rock; a base half-dug into a moon, ready to claim.
- Fit: pre M/H > post L/M | ports A-D | worlds with moons | R | no | from Z54 Z55

**51. Flash Shadows** (`flash_shadows`) - Every hull burnt on one side.
- Happened: The star flared. Everything in orbit took it on one face at one moment.
- In orbit: All wrecks charred on the same bearing, with crisp shadows of antennas and people burnt onto hulls. Varies: storm shelters that worked, by thickness; a crew that came out afterwards.
- Families: top: drifting_hulk, wreck_field. NEW: truss_station.
- Inside: A shelter room packed with the living, then the dead; burnt sun-side rooms and intact far-side ones.
- Occ: EMP 75, SCV 15, MCH 10.
- Hazards/rewards: Radiation in the burnt shells; shielded rooms, flare data, a shared line that tells the player the true moment.
- Fit: pre M/H > post L/M | ports A-D | any | U | S | from 21 Z28

**52. The Reactor Shelf** (`reactor_parking`) - Parked hot.
- Happened: Spent reactors were boosted up to wait until they cooled, and nobody came back for them.
- In orbit: A shelf of small reactor units in a high shell. Varies: leaking, or sealed; a rogue crew harvesting them.
- Families: top: wreck_field. NEW: vault_capsule, tank_farm.
- Inside: Handling cans, lead-lined tongs, a dose board with a line of tally marks.
- Occ: EMP 75, MCH 10, BOT 15.
- Hazards/rewards: Warned radiation; reactor parts, power cells.
- Fit: pre M/H > post L/M | ports B-D | any | U | no | from 75 Z33

**53. Eaten Smooth** (`eaten_smooth`) - Clean edges where a ship was.
- Happened: Small machines reached a port and were stopped by cold or by fire before they finished.
- In orbit: Hulls ending in smooth grey curves. Varies: stopped and dead, or a slow front creeping a few blocks a day.
- Families: top: broken_station, derelict_freighter. NEW: debris_shell.
- Inside: Rooms half-dissolved at a grey margin, a sealed cold lab, a line of burnt hull where a crew made a firebreak.
- Occ: EMP 55, BOT 25, MCH 10, SCV 10.
- Hazards/rewards: Block damage to what you bring; samples, a cure rig, cyber module chance.
- Fit: pre H > post L/M | ports A-C | any | R | no | from 102 Z32

**54. The Undertaker** (`the_undertaker`) - It tidied the sky until the fuel ran out.
- Happened: A disposal tug was told to bring down everything old, and it stopped mid-job with one last wreck on the line.
- In orbit: A big tug nose-down, a dead station on tow behind it. Varies: how far through its list it got; the wrecks it finished form a neat cemetery on the ground below.
- Families: top: derelict_freighter. NEW: tug_train, drive_pack.
- Inside: A clean machine bay, a hold of removed tags and nameplates in rows, one empty hook for the wreck on tow.
- Occ: MCH 40, EMP 60.
- Hazards/rewards: Taut tow lines; the tug and its tow to claim, and a bearing to the cemetery POI on the ground.
- Fit: pre H > post L/M | ports A-C | air worlds | R | S | from 28 Z42

### E. Work and industry

**Led to by:** world themes 9 Mining Lock-In, 17 Coughing Hills, 73 Foundry Belt, 74 Drone Orchard, 77 Cannery World, 78 Data Tombs, 79 Shipbreakers, 81 Pipeline World, 82 Geothermal Deep, 112 Alien Shipyards, 35 Ember World, 37 Pit Worlds. **Apocalypses:** Z30 Industrial collapse, Z49 General stoppage, Z63 Ageing colony, Z61 Neglect. **Reads as:** working places that stopped, or didn't. Almost everything is a role on a shared family.

**55. The Unfinished Keel** (`unfinished_keel`) - A hull on the slip.
- Happened: Work stopped mid-frame, and the yard's clamps have held the hull ever since.
- In orbit: A gantry round an unfinished hull. Varies: 20 to 90% built; Vharn or human; still worked on by one machine.
- Families: top: old_shipyard, orbital_yards. NEW: climber_car.
- Inside: Sheds of plate, a drawing office of big pictograms, welders' cabins, stacks of parts for the next stage.
- Occ: EMP 45, BOT 20, SCV 15, FAC 10, MCH 10.
- Hazards/rewards: Exposed heights, old welders; the classic capital hull claim, needing work to fly.
- Fit: pre H > post L/M/H | ports A-B | any | U | S | from 112 73

**56. The Breakers' Orbit** (`breakers_orbit`) - Ships cut like bread.
- Happened: Old ships came here to be cut up. Now scavengers do it by hand.
- In orbit: Hulls sectioned from top down, in slices, with sorted piles of parts. Varies: live scrap town, dead yard, or a machine going on alone.
- Families: top: old_shipyard, wreck_field. NEW: breaker_frame, pod_cluster.
- Inside: Cutting gantries, drained tanks, stores of doors, seats and pipes in sorted racks.
- Occ: SQT 30, SCV 30, BOT 20, EMP 20.
- Hazards/rewards: Gas in a tank, falling plates; cheap parts, a place to sell salvage.
- Fit: pre M/H > post L/M/H | ports B-D | any | C | S | from 79

**57. The Catcher** (`catcher`) - The moon is still throwing.
- Happened: A mass driver on a moon kept flinging ore pods to a catcher that broke long ago.
- In orbit: A cone at the moon's far point, pods shooting past on a timer. Varies: a working catcher, or a string of pods flying off into space; a pile of pods at a stable point.
- Families: top: mining_outpost. NEW: mass_catcher, mass_driver, pod_cluster.
- Inside: A catcher crew can, a bin hall, a wall of tally marks of pods missed.
- Occ: MCH 45, EMP 40, SCV 15.
- Hazards/rewards: Pods at speed along a visible line; free ore, and the driver can be claimed.
- Fit: pre H > post L/M/H | ports A-D | airless low-g moons | U | S | from 9 73

**58. The Dipper** (`scoop_rig`) - Hung by a thread in the storm.
- Happened: A gas platform lowered a scoop on a long cable into the giant's clouds and pumped fuel.
- In orbit: A depot station in a giant's low shell, cable to a scoop in the cloud tops. Varies: scoop lost; cable parted; tanks full.
- Families: top: mining_outpost. NEW: scoop_rig, tank_farm, tether_node.
- Inside: Pump halls, cold tank crawls, wind-beaten scoop deck, crew cans with giant-sized views.
- Occ: EMP 45, MCH 25, SCV 15, GNG 15.
- Hazards/rewards: Storm surges on the scoop; hyperdrive fuel, a piece of the ladder.
- Fit: pre M/H > post L/M/H | ports any | giants | C | S | from 81

**59. The Fuel Stop** (`fuel_stop`) - Tanks on a truss, a light on top.
- Happened: Every route needs a place to fill up. Some are still open.
- In orbit: Spheres on a frame, with a small crew can. Varies: low-tech cans, mid-tech cold tanks, high-tech hydrogen farm; empty or full.
- Families: top: ledger_waystation, mining_outpost. NEW: tank_farm, can_station.
- Inside: Pump rooms, a counter, a repair bench, tanks to walk inside.
- Occ: SQT 25, EMP 40, SCV 15, GNG 10, FAC 10.
- Hazards/rewards: Boom risk, warned; warp fuel, a place to trade.
- Fit: pre M/H > post L/M/H | ports B-E | any | C | S | from 31 81

**60. The Ring Camp** (`ring_camp`) - Ice cutters in a sheet of light.
- Happened: Crews cut ice in a world's ring and sent it to towns. Some still do.
- In orbit: A camp clamped to a big ring rock. Varies: dead or worked; one rich rock, or many.
- Families: top: mining_outpost. NEW: tug_train, hollow_rock.
- Inside: Cutter bays, a bunkhouse, a stove, blocks of ice in hold.
- Occ: SQT 25, EMP 45, SCV 15, NST 15.
- Hazards/rewards: Rock hits; ice, water, a Freeholder trade point.
- Fit: pre M/H > post L/M/H | ports C-E | ringed bodies | C | S | from 67 106

**61. The Sky Refinery** (`sky_refinery`) - A mirror and a furnace.
- Happened: Ore was melted in orbit with focused sunlight, to keep the smoke off the world below.
- In orbit: A mirror dish on a box plant, a slag cloud in its orbit. Varies: mirror broken; Kessari still run it; slag ring.
- Families: top: kessari_foundry. NEW: mirror_works, box_works.
- Inside: Crucible halls, slag chutes, cold ingot stacks, a foreman's glass cab.
- Occ: MCH 30, EMP 35, BOT 15, FAC 20.
- Hazards/rewards: Focal spot sweeping past; bars of metal, smelter machines.
- Fit: pre H > post L/M/H | ports A-C | any | U | S | from 73 Z30

**62. Still Making It** (`overflow_plant`) - A halo of identical things.
- Happened: A factory was never told to stop. Its product has burst the holds.
- In orbit: A factory in a cloud of thousands of the same object. Varies: what it made: cans, boots, ball bearings, chairs. The feedstock ran out long ago, so the stock is all there is, and the line still runs empty.
- Families: top: kessari_foundry. NEW: box_works, flotsam_field.
- Inside: Rooms filled wall to wall with product, conveyors running, one tiny worker's room.
- Occ: MCH 70, EMP 20, BOT 10.
- Hazards/rewards: Drifting mass, jammed routes; piles of one thing, and it's funny.
- Fit: pre H > post L/M | ports A-D | any | U | S | from 74 77 Z30

**63. The Company Station** (`company_orbit`) - Doors that open one way.
- Happened: A company locked its workers aboard to protect its stock.
- In orbit: A big works, its docks controlled from a glass box. Varies: revolt won or failed; the descendants still pay a debt.
- Families: top: mining_outpost, broken_station. NEW: truss_station.
- Inside: Company store with token prices, bunk stacks, doors with outer locks, the manager's lounge, bars inward.
- Occ: EMP 35, SQT 20, GNG 20, HSK 10, MCH 15.
- Hazards/rewards: Scrip machines, locked docks; ores, tokens that open the strongroom.
- Fit: pre M/H > post L/M | ports B-D | any | C | S | from 9 Z51

**64. The Cold Vault** (`cold_vault`) - One aisle still lit.
- Happened: Data was kept in orbit's permanent shade. Power is rationed, row by row. Builds on idea 27.
- In orbit: A shaded box with long radiators. Varies: what's kept: records, minds, money; which aisles still run.
- Families: top: meridian_relay. NEW: box_works, vault_capsule, film_sheet.
- Inside: Rack aisles, dark rows, a lit row, small family plaques, a cold pump room.
- Occ: MCH 55, EMP 35, HOL 10.
- Hazards/rewards: Cold, and pulling a rack ends something; data, logic parts, lattice cores.
- Fit: pre H > post L/M/H | ports A-C | any | U | S | from 78 61

**65. The Rotovator** (`rotovator`) - The hook comes round on time.
- Happened: A spinning tether caught and threw cargo, and it has kept turning with nothing left to catch.
- In orbit: A long turning cable in one place, its tip sweeping a low shell. Varies: broken arm; tip reach; last load still in a hook cage.
- Families: top: meridian_relay. NEW: tether_node.
- Inside: A hub of two rooms, hook cages at each tip with handholds.
- Occ: EMP 75, MCH 25.
- Hazards/rewards: A hit from the tip, seen coming; the last cargo in the hook, reached by timing a landing at the hub.
- Fit: pre H > post L/M/H | ports A-C | airless or thin worlds | R | S | from 80

**66. The Quarried Moon** (`quarried_moon`) - It was bigger.
- Happened: A moon was cut away for metal, leaving straight faces and benches.
- In orbit: A moon with stamped terraces, a spoil ring, drivers on its limb. Varies: how much is gone; a final pit that broke into something.
- Families: top: mining_outpost. NEW: mass_driver, box_works.
- Inside: Cut galleries, haul halls, a pit-head town under a dome.
- Occ: EMP 45, SQT 20, BOT 20, SCV 15.
- Hazards/rewards: Big falls in low gravity; ores, rare exposed core ore, a mine as big as a world feature.
- Fit: pre H > post L/M | ports A-D | moons | U | S | from 9 37 Z30

**67. Tools Down** (`picket_line`) - The strike never ended.
- Happened: Every worker stopped at once, and strikers blockaded their own docks with their own tugs.
- In orbit: A yard with a ring of tugs nose-in, tied together. Varies: settled and left, or the blockade starved them.
- Families: top: old_shipyard. NEW: tug_train, truss_station.
- Inside: Half-finished work with tools lined up neatly, banner paint, a canteen with a strike kitchen.
- Occ: EMP 50, SQT 15, SCV 20, MCH 15.
- Hazards/rewards: Calm; tools, tugs, a half-done job the player can finish.
- Fit: pre M/H > post L/M | ports B-D | any | U | S | from 73 Z49 Z61 Z63

### F. Commerce, leisure and culture

**Led to by:** world themes 6 Parade Ground, 7 Closed for the Season, 83 Souvenir World, 84 Bureaucracy Moon, 85 Pirate Radio, 86 Zoo World, 87 Artist Colony, 88 Sporting Planet, 89 Casino Dunes, 90 School Holidays, 92 Library Towns, 96 Pilgrim Roads. **Apocalypses:** Z46 Economic collapse, Z47 Faith takeover, and any quiet fall. **Reads as:** human, funny and sad, a relief from war and plague.

**68. The Sky Hotel** (`sky_hotel`) - Rooms with a world view.
- Happened: The rich vacationed in orbit, and the staff left last.
- In orbit: A spin ring with big windows and a marina of pleasure craft. Varies: closed neatly or fled from; still kept by service machines.
- Families: top: broken_station. NEW: spin_ring, bubble_garden.
- Inside: Pool with a ball of frozen water, casino vault, suites, a bar with glasses set.
- Occ: EMP 40, MCH 30, GNG 15, SCV 15.
- Hazards/rewards: Security machines; luxury goods, yachts to claim.
- Fit: pre H > post L/M | ports A-B | any | R | S | from 7 89

**69. The Sky Sign** (`sky_sign`) - An advert you can read from the ground.
- Happened: A company hung sunlit foil panels at sync, forming a sigil over a continent.
- In orbit: A huge frame of film panels. Varies: tattered to a few letters of shape; a rival cut it; one panel flips.
- Families: top: wreck_field. NEW: film_sheet, relay_sat.
- Inside: Almost none: a small panel-keeper's can.
- Occ: EMP 75, MCH 15, SCV 10.
- Hazards/rewards: Film tangles; a giant sigil in the night, in invented marks of our own.
- Fit: pre H > post L/M | ports A-C | any | U | S | from 83

**70. The Free Port** (`free_port`) - Open for business.
- Happened: Traders held this one together through everything.
- In orbit: Docking arms and pods, ships coming and going. Varies: by who holds it; prices by world; guards by standing.
- Families: top: ledger_waystation. NEW: truss_station, pod_cluster.
- Inside: Market hall, stalls, repair dock, bar, stores.
- Occ: FAC 70, SQT 25, SCV 5.
- Hazards/rewards: Safe until you shoot; trade, repairs, fuel, survey sale.
- Fit: pre any > post M/H | ports A-D | any | C | no | from any

**71. The Customs House** (`customs_house`) - It still wants your token.
- Happened: Every ship had to be stamped here, and the machines never stopped checking.
- In orbit: A heavy office-like station with a lane of barrier buoys. Varies: which token it wants; a share of doors open to anyone with the right stamp.
- Families: top: broken_station. NEW: nav_buoy, truss_station.
- Inside: Counters, numbered benches, stamp machines, a vault of seized goods.
- Occ: MCH 60, EMP 30, SCV 10.
- Hazards/rewards: Gentle puzzle; a seized goods hold full of odd things.
- Fit: pre M/H > post L/M/H | ports A-C | any | U | S | from 8 84

**72. The Sky Circuit** (`sky_circuit`) - Lit hoops round a moon.
- Happened: A world's great race ran through hoops in space. The hoops still light up.
- In orbit: A circuit of ring gates round a moon or ring, a grandstand station. Varies: gates lost; clock board still gives a time in numerals.
- Families: top: broken_station. NEW: nav_buoy, spin_ring.
- Inside: Team garages, a trophy room, grandstand, a stopped scoreboard.
- Occ: EMP 55, SCV 20, MCH 15, GNG 10.
- Hazards/rewards: Fly it for a time; a fast small craft in a garage, trophies.
- Fit: pre H > post L/M | ports A-C | any | U | S | from 88

**73. The Welded Prayers** (`pilgrim_orbit`) - A hull made of thanks.
- Happened: Pilgrims rose to a shrine ship and welded their own small plates to its hull.
- In orbit: A chapel station furred with thousands of little plates. Varies: faith: bell, flame or silence; still visited or not.
- Families: top: broken_station. NEW: can_station.
- Inside: Offerings, cots, candle deck, a long ramp of worn floor.
- Occ: CLT 45, EMP 35, SQT 10, HOL 10.
- Hazards/rewards: Touching a shrine; rare gifts, and pilgrims will trade.
- Fit: pre L/M/H > post L/M | ports B-E | any | U | S | from 96 Z47

**74. The Pirate Transmitter** (`pirate_sat`) - Music from a dead relay.
- Happened: Someone took over a dead satellite to broadcast wordless music to a broken world.
- In orbit: A small relay with a hand-built cabin. Varies: a voice-less loop or a live hand on the dials; a Reaver or a child at the desk.
- Families: top: meridian_relay. NEW: relay_sat, can_station.
- Inside: Instruments, posters of pictures, a heap of tuned tape reels, a bunk.
- Occ: EMP 40, SQT 30, SCV 20, MCH 10.
- Hazards/rewards: None; a music item for your own base, a lead.
- Fit: pre M/H > post L/M | ports B-E | any | U | S | from 85

**75. The Museum Ship** (`museum_orbit`) - An old ship in a rope line.
- Happened: The founders' ship was kept in orbit as a heritage site, with ticket booths and a gift shop.
- In orbit: An old hull inside a small visitor frame. Varies: a share of exhibits still work; school groups' mark; looted cases.
- Families: top: drifting_hulk. NEW: truss_station, lander_carrier.
- Inside: Rope lines, cutaway rooms, cases of older tech, a gift counter.
- Occ: EMP 55, MCH 20, SCV 15, GNG 10.
- Hazards/rewards: Display guards; an old device that works, and a look at what came before.
- Fit: pre H > post L/M/H | ports A-C | any | U | S | from 26 Z59

**76. The School Station** (`school_station`) - Thirty small desks.
- Happened: A training station for children ran a drill, and then it wasn't a drill.
- In orbit: A bright painted station. Varies: pods all left, or all but one.
- Families: top: broken_station, escape_pods. NEW: spin_ring.
- Inside: Small desks, a model system on the ceiling, height marks, a drill pictogram, a row of tiny suits.
- Occ: EMP 75, HOL 10, MCH 15.
- Hazards/rewards: Nothing attacks here; toys, teaching models, a map of this very system.
- Fit: pre H > post L/M | ports A-C | any | U | S | from 90

**77. The Orbital Zoo** (`orbital_zoo`) - Sealed worlds on a carousel.
- Happened: Animals from a dozen worlds were kept in sealed spheres, and some lines still breed.
- In orbit: A wheel of glass-fronted bubbles. Varies: which bubbles are alive; breached bubbles frozen with their animals.
- Families: top: broken_station. NEW: bubble_garden, spin_ring.
- Inside: Keeper's walkways, feed hoppers, enclosures with small biomes.
- Occ: NST 30, MUT 20, MCH 25, EMP 25.
- Hazards/rewards: Escaped predators; live species, organics, a keeper machine still feeding.
- Fit: pre H > post L/M | ports A-C | any | R | S | from 86 47

**78. The Sky Sculpture** (`sky_sculpture`) - One artist's scrap in a slow turn.
- Happened: An artist hung a slowly turning work in orbit, and a machine has gone on adding to it.
- In orbit: A vast mobile of wreck parts, turning. Varies: size; a colony of others added pieces; it casts a moving shadow.
- Families: top: wreck_field. NEW: flotsam_field, can_station.
- Inside: A studio can with models of the work and tools.
- Occ: EMP 70, MCH 20, SCV 10.
- Hazards/rewards: Moving parts; rare deco, and a good spot to take a screenshot.
- Fit: pre M/H > post L/M | ports B-D | any | U | S | from 87

**79. The Impound** (`impound_orbit`) - All the ships had clamps on.
- Happened: When money failed, creditors seized every ship they could reach, and then the creditors failed too.
- In orbit: A yard of clamped hulls, each with a big seizure tag. Varies: a few broken out; an auction hall.
- Families: top: old_shipyard, derelict_freighter. NEW: truss_station.
- Inside: A bailiff's office, auction benches, keys in a wall of numbered hooks.
- Occ: EMP 60, MCH 20, SCV 20.
- Hazards/rewards: Clamp machines; hulls with fuel and goods, opened by keys.
- Fit: pre H > post L/M | ports A-C | any | U | S | from 89 Z46

### G. Low-tech skies, founders and the second climb

**Led to by:** world themes 23 Wall Cities, 31 Road Warrens, 36 Bandit Kingdoms, 40 Hunger Roads, 93 Horse and Diesel, 94 Castle Worlds, 95 Barrow Downs, 98 Lonely Lighthouses, 99 Bone Dig, 100 Wind Chime Coast, 101 Sunken Raiders. **Apocalypses:** Z59 Slow fade, Z61 Neglect, Z62 Supply cutoff, and any world that fell to low tech. **Reads as:** a sky with very little in it, each thing a big deal.

**80. The Founders' Carrier** (`founder_carrier`) - The star that never moves, in all their stories.
- Happened: The ship that brought the first settlers was left in a high orbit and forgotten by all but ritual.
- In orbit: A long old hull in a high shell, its lander bays empty. Varies: stripped to its ribs for the first town's metal; sealed; still ticking; only the keel left.
- Families: top: generation_hulk, drifting_hulk. NEW: lander_carrier.
- Inside: Empty cradles, cold sleep halls, the same sigil as the oldest stone on the ground.
- Occ: EMP 60, MCH 15, NST 10, SLP 5, HOL 10.
- Hazards/rewards: Age, cold; founder-age parts, a chart of the worlds they knew.
- Fit: pre L/M > post L/M | ports D-E, none | any | U | S | from 4 93 94 95

**81. The Small God** (`small_god`) - A relay the size of a van.
- Happened: A little survey relay hangs in one place over a low-tech world, and it's the only star that never moves. Stone rings were built to point at it.
- In orbit: One tiny object. Varies: dead or blinking; which stone ring below lines up with it.
- Families: top: meridian_relay. NEW: relay_sat.
- Inside: None. A hatch on a box the size of a closet.
- Occ: EMP 90, HOL 10.
- Hazards/rewards: Nothing; a small reward, and the feeling of having touched something people worship.
- Fit: pre L > post L | ports E, none | any | C | S | from 95 96 98

**82. The National Capsule** (`national_capsule`) - Their first and last station.
- Happened: A mid-tech world put up a few cans and a flag, then everything stopped.
- In orbit: A foil-wrapped stack with a docked capsule. Varies: crew stayed or left; a share were never crewed.
- Families: top: meridian_relay. NEW: can_station, capsule_craft.
- Inside: Handwheels, switches, a table with straps, personal photos, a small flag, a plant tray.
- Occ: EMP 80, SCV 10, HOL 10.
- Hazards/rewards: Bad air; hand-made gear in the era's own style, a real sense of how far they got.
- Fit: pre M > post L/M | ports C-E | any | C | S | from 24 93

**83. The Second Space Age** (`second_space_age`) - Foil on the giants.
- Happened: Survivors relearned rockets and reached the ruin of their ancestors in tiny capsules.
- In orbit: A big dead high-tech hull with capsules clamped on, ladders and rope, flags. Varies: scientists or looters; a working research can, or a dead one that got one room open.
- Families: top: drifting_hulk, broken_station. NEW: capsule_craft, can_station.
- Inside: Cut door, lamps on poles, sample boxes, a camp in the old foyer.
- Occ: SQT 35, EMP 35, SCV 20, HSK 10.
- Hazards/rewards: Their guns are old, their fear is real; local trade, and they sell maps of what they've found.
- Fit: pre H > post M | ports B-D | any | U | S | from 3 37 93

**84. The Iron Shell** (`iron_shell`) - Fired from a gun.
- Happened: A proud low-tech people built a gun big enough and fired a crew into orbit.
- In orbit: A riveted iron capsule, very low if airless or high by luck. Varies: dead crew, broken gauge; a second shell, a rescue, that also missed.
- Families: top: escape_pods. NEW: capsule_craft.
- Inside: Riveted iron, leather seats, brass gauges, tins, a bottle kept for arrival.
- Occ: EMP 95, HOL 5.
- Hazards/rewards: None; a beautiful small find. Ground echo: the great gun on a hillside.
- Fit: pre L > post L | ports none | any | R | S | from 93 94

**85. The Watchers' Post** (`watcher_post`) - They never went down.
- Happened: High-tech observers watched a low-tech world from hiding, and were forgotten when the galaxy fell.
- In orbit: A small dark station on the far side of the moon. Varies: went down in the end, or died at the scopes.
- Families: top: meridian_relay. NEW: can_station, dish_array.
- Inside: Models of villages, copies of local clothes, a record wall of pictures, a last supply case.
- Occ: EMP 70, MCH 20, SQT 10.
- Hazards/rewards: Few; local maps and customs as a treasure, world survey data.
- Fit: pre L > post L | ports E, none | any | U | S | from 94 36

**86. The Sky Landlord** (`sky_landlord`) - Outsiders took what they wanted.
- Happened: A high-tech company mined a low-tech world from above and paid in trinkets.
- In orbit: A big platform with drop lines to the surface. Varies: fell with the company; still sends a dead line down; held by Reavers.
- Families: top: mining_outpost. NEW: tether_node, box_works.
- Inside: Sample rooms, a trade floor with beads and tools, company housing, ore chutes.
- Occ: EMP 45, GNG 20, MCH 20, SCV 15.
- Hazards/rewards: Machine guards; big ore stocks, tools from a higher band than the world below.
- Fit: pre L > post L | ports D-E | any | U | S | from 94 99

**87. The Frontier Buoy** (`frontier_buoy`) - Beacon, tank, and a box of spares.
- Happened: Rim worlds got a marker and a small depot for anyone passing.
- In orbit: A mast and a tank. Varies: empty, stocked, kept by a local hermit, or broken into.
- Families: top: meridian_relay. NEW: beacon_mast, tank_farm.
- Inside: A locker room and a logbook shaped as a board of ship tags.
- Occ: EMP 60, SCV 25, SQT 15.
- Hazards/rewards: Calm; a little fuel, a repair kit, a place to leave a mark.
- Fit: pre L/M > post L/M | ports D-E | any | C | S | from 98

**88. The Traders' Anchorage** (`trader_anchorage`) - A few ships and a lot of cargo.
- Happened: Independent traders moor here and send boats down to a town with a clearing.
- In orbit: Two to four freighters rafted together, one always moored. Varies: wary or friendly; raided; how much they carry.
- Families: top: ledger_waystation, derelict_freighter. NEW: pod_cluster.
- Inside: Open holds as a market, a shared galley, a small bar.
- Occ: SQT 70, FAC 20, EMP 10.
- Hazards/rewards: Safe; trade and the start system's trading station when no faction may be there.
- Fit: pre any > post L/M | ports D-E | any | C | S | from any

**89. Sky Goods** (`cargo_sky`) - The gift still falls.
- Happened: A supply satellite was built to drop aid every year, and it still drops, though the pods are now empty.
- In orbit: A big dispenser with an empty rack and a few pods. Varies: last real pod; a town's faith; the machine is short of pods and throws parts of itself.
- Families: top: meridian_relay. NEW: pod_cluster, relay_sat.
- Inside: Rack bays, a small machine shop, a last good pod.
- Occ: MCH 70, EMP 30.
- Hazards/rewards: Falling pods on the ground; a good pod inside; a cult below built round it.
- Fit: pre M/H > post L | ports C-E | any | U | S | from 40 Z62

**90. The Sky Lords** (`sky_lords`) - Tribute to the gods upstairs.
- Happened: A Reaver crew rules a low-tech world from a hulk, taking grain and people as tribute.
- In orbit: A patched capital hulk with a landing craft on a schedule. Varies: strong or fading; a rebel plan below.
- Families: top: pirate_hideout, drifting_hulk. NEW: scrap_raft.
- Inside: A throne room, tribute holds, a shuttle deck, sealed crew quarters.
- Occ: GNG 65, FAC 15, EMP 10, CLT 10.
- Hazards/rewards: A real capital fight or boarding; the people below remember who ended it.
- Fit: pre L/M > post L | ports D-E | any | U | no | from 36 101 31

**91. The Patch Town** (`patch_town`) - Tape on tape on tape.
- Happened: A few hundred people stayed aboard a failing station and never stopped fixing it.
- In orbit: A station covered in bright patches, a hanging garden of tanks. Varies: well or failing; welcoming or wary; a generation left.
- Families: top: broken_station. NEW: spin_ring, can_station, scrap_raft.
- Inside: Street deck with stalls, hand-painted signs, a repair shrine, children.
- Occ: SQT 75, EMP 10, SCV 15.
- Hazards/rewards: Safe with good manners; trade, repairs. Every system could use one.
- Fit: pre M/H > post L/M | ports B-E | any | U | no | from 20 Z61

### H. Nature, gardens and climate works

**Led to by:** world themes 14 Pollen Wars, 15 Blightland, 32 Water Tax, 33 Salt and Bone, 34 Long Winter, 38 Floodlines, 39 Last Garden, 41 Teeth in the Trees, 42 Stalker Reefs, 43 Hive Plains, 44 Maw Dunes, 45 Thorn Wall, 46 Storm Spires, 47 Titans' Meadows, 49 Acid Reaches, 50 Kelp Sea, 62 Lamplight World, 63 The Glass Garden, 64 Moon Lakes, 65 Terraced Valleys, 66 Wildflower Runways, 67 Aurora Shelf, 69 Orchard Moon, 70 Amber Steppe, 71 Hot Spring Highlands, 72 Starfield Desert, 105 Never Settled, 106 Ocean World, 109 Fire and Ice. **Apocalypses:** Z19 Famine, Z20 Drought, Z21 Rising seas, Z22 Long winter, Z23 Runaway heat, Z24 Ash year, Z25 Great quake, Z26 Air loss, Z27 Poisoned air, Z29 Meteor swarm, Z31 Pollination failure, Z36 Terraform failure, Z37 Weather engine failure, Z43 Engineered harvest failure. **Reads as:** the thing that was meant to fix the world, or the thing that was keeping it going.

**92. Second Noon** (`stuck_mirror`) - A hard white light at night.
- Happened: A mirror built to warm a city still lights the spot where it stood. Idea 10.
- In orbit: A vast film mirror. Varies: stuck on one spot; tilted off the old city; torn into several small suns.
- Families: top: meridian_relay. NEW: mirror_array, film_sheet.
- Inside: A hub of two rooms, tension winches, a tiny crew.
- Occ: MCH 40, EMP 60.
- Hazards/rewards: Focused glare on approach; tilt it and a piece of the ground goes dark, or warm.
- Fit: pre H > post L/M | ports A-C | Z4 to Z6 | R | S | from 34 62 Z22

**93. The Parasol** (`sun_shade`) - The world is in its shadow.
- Happened: A shade built to cool a burning world stuck half open, and now the world freezes or dims.
- In orbit: A film sheet the size of a town at the world's L1, on failing thrusters. Varies: half gone; drifting off slowly; still held by a keeper.
- Families: top: meridian_relay. NEW: film_sheet, sail_rig.
- Inside: A small trim station, spool halls, cable rooms.
- Occ: MCH 45, EMP 40, SQT 15.
- Hazards/rewards: Cutting it changes the sky, a real choice; huge film salvage.
- Fit: pre H > post L/M | ports A-B | Z3 to Z5 | R | no | from 34 Z22 Z23

**94. The Ice Tow** (`ice_tow`) - The water that never arrived.
- Happened: A foil-wrapped ice block was hauled to a dry world, and its tug died in orbit.
- In orbit: A giant block of ice in tatters of foil, a dead tug, its water still overhead. Varies: size; a local warlord's claim; slowly boiling on its sun side.
- Families: top: derelict_freighter. NEW: tug_train, film_sheet.
- Inside: A tug with bunks and a pump room, tally of days.
- Occ: EMP 70, SCV 20, MCH 10.
- Hazards/rewards: Calm; lots of water, and the warlord's source of power would mean nothing if it fell.
- Fit: pre H > post L/M | ports A-D | dry worlds | U | S | from 32 33 Z20

**95. The Rain Machine** (`weather_platform`) - It still seeds the same storm.
- Happened: A weather system got stuck on one setting.
- In orbit: Platforms with dishes aimed at a cloud bank. Varies: storm, drought or fog; one or many; a switch that needs a key from the ground.
- Families: top: meridian_relay. NEW: dish_array, power_sat.
- Inside: A control room, weather table model, stores of seeding canisters.
- Occ: MCH 55, EMP 35, SCV 10.
- Hazards/rewards: Beam hazard; switching it off is a big choice.
- Fit: pre H > post L/M | ports A-C | any air | R | no | from 46 Z37

**96. The Terraform Tender** (`terraform_tender`) - The world-fixer's unfinished job.
- Happened: A ship seeded the sky with algae pods until the project died.
- In orbit: A tanker with racks of pods, half gone. Varies: green strips below match its passes; a pod rack jammed; a machine still dropping the wrong mix.
- Families: top: derelict_freighter. NEW: pod_cluster, tank_farm.
- Inside: Culture tanks, mix rooms, a world model marked in sectors.
- Occ: MCH 45, EMP 35, NST 10, MUT 10.
- Hazards/rewards: Spills; seed strains, a map of the planned world.
- Fit: pre H > post L/M | ports A-C | Z2 to Z6 | U | S | from 65 Z36

**97. The Tended Forest** (`green_dome`) - Trees under glass.
- Happened: The last of a world's forest lives in a dome on a hull. Idea 19.
- In orbit: A big glass dome on a slow ship. Varies: one old keeper or a machine; failing pump; a small sea.
- Families: top: derelict_freighter. NEW: bubble_garden.
- Inside: Real soil in blocks, paths, birds, a watering system, a tool shed.
- Occ: MCH 35, SQT 30, EMP 20, NST 15.
- Hazards/rewards: A place to protect; seeds, wood, clean air.
- Fit: pre H > post L/M | ports A-C | any | R | S | from 39 63 69

**98. The Cold Seed** (`cold_seed`) - Next year's harvest, kept in the shade.
- Happened: Crop seed was stored in permanent shadow, for a blight that came.
- In orbit: A shaded vault with a sun shield. Varies: looted by a hungry world, or sealed; a keeper of rows.
- Families: top: meridian_relay. NEW: vault_capsule, film_sheet.
- Inside: Drawers in thousands, a tag system of pictures, a cold pump, a small tray of living plants.
- Occ: EMP 55, MCH 30, SCV 15.
- Hazards/rewards: Cold; seeds that grow real crops in a base.
- Fit: pre M/H > post L/M | ports A-D | any | U | S | from 14 15 65 66 Z31 Z43

**99. The Great Eye** (`great_eye`) - It's still looking at one star.
- Happened: A telescope behind its world's shadow kept watching one place.
- In orbit: A big mirror at L2 on its last thruster. Varies: what it's pointed at; a real star, a nebula, or a blank spot.
- Families: top: meridian_relay. NEW: dish_array, mirror_array.
- Inside: A small crew room, a wall of star plates, a row of the same image growing.
- Occ: MCH 40, EMP 60.
- Hazards/rewards: Calm; a bearing to a real star or a rare site, sold to a survey buyer.
- Fit: pre H > post L/M/H | ports A-C | any | U | S | from 72 67

**100. The Untouched Sky** (`untouched_sky`) - Nothing built at all.
- Happened: Nobody ever came.
- In orbit: Nothing but rings, weather from above, aurora and shadows. Varies: one survey buoy; a rare grazer.
- Families: top: wreck_field. NEW: nav_buoy.
- Inside: None.
- Occ: EMP 100.
- Hazards/rewards: None; first-discovery rewards and survey data.
- Fit: pre none > post none | ports none | any | C | S | from 105 106 109 64

**101. The Survey Globe** (`survey_globe`) - A model of the world as it was.
- Happened: A polar platform mapped everything below, and a physical globe of the world still sits at its heart.
- In orbit: A small platform in a polar orbit. Varies: globe from before or after the fall; patched by later hands.
- Families: top: meridian_relay. NEW: dish_array, can_station.
- Inside: A great model globe with old cities raised on it, a scanner room, plain bunks.
- Occ: EMP 60, MCH 30, SCV 10.
- Hazards/rewards: Calm; marks ruins and ports on the player's map, the best early survey in the game.
- Fit: pre M/H > post L/M/H | ports A-E | any | U | S | from 42 43 44 45 49 50 70 71

**102. The Catchers' Net** (`rock_catchers`) - They tried to catch the sky.
- Happened: Stations were set up to shoot down or net a swarm of rocks. Some rocks are still held.
- In orbit: Gun rafts and big net frames, a rock caught in one. Varies: all broken, or still swatting; rocks drifting.
- Families: top: military_wreck, mining_outpost. NEW: gun_platform, film_sheet.
- Inside: A crew raft, trajectory boards, hand-calculated marks on walls.
- Occ: EMP 50, MCH 30, SCV 20.
- Hazards/rewards: Fire at incoming rocks, not the player; held rocks are rich, and it can be worked as a mine.
- Fit: pre M/H > post L/M | ports B-D | any | U | S | from 42 Z29 Z25

**103. The Air Thief** (`air_thief`) - The world's sky, in tanks.
- Happened: Ships siphoned a world's upper air for profit until it ran thin.
- In orbit: Long-hosed tankers in the skim shell, tank farms high up. Varies: tanks still full; a hose hanging; a town below that knows.
- Families: top: derelict_freighter. NEW: tank_farm, tether_node, scoop_rig.
- Inside: Compressor halls, frost rooms, company art.
- Occ: EMP 45, BOT 25, SCV 20, MCH 10.
- Hazards/rewards: Burst tanks; air and gas to sell.
- Fit: pre H > post L/M | ports A-C | thin air worlds | R | S | from 41 49 Z26 Z27

**104. The Full Granary** (`sky_granary`) - Grain over a starving world.
- Happened: Food was stored in orbit against famine, and when the famine came there was no way to bring it down.
- In orbit: A round grain store. Varies: fully sealed; a Reaver fortune; a stuck chute over a dead town.
- Families: top: derelict_freighter. NEW: pod_cluster, box_works.
- Inside: Great silos in hull form, weigh rooms, a dropper bay with a jam.
- Occ: EMP 60, MCH 20, NST 10, GNG 10.
- Hazards/rewards: Grain dust, pests; food in big quantity, a town can be saved.
- Fit: pre M/H > post L | ports B-D | any | U | S | from 15 40 Z19 Z24 Z21

### I. Strange and quiet wrong

**Led to by:** world themes 13 Sleepwalkers, 16 Drowned by the Bloom, 51 Wrong Stars, 52 Folded Town, 53 Hollow Orchards, 54 Whisper Flats, 55 Mirror Cities, 56 Choir Canyon, 57 Clockwork Hours, 59 Eye of the Bloom, 60 Dream Salt, 61 Black Library, 97 Precursor Shelf, 107 Ring Remnant, 110 Vacated Vaults. **Apocalypses:** Z35 Great silence, Z40 Duplicates, Z41 Memory rot, Z53 Dark patch, Z56 Time stutter, Z57 Dimensional tear, Z58 Wormhole tides, Z64 Vanishing, Z65 Walked out, Z66 Silence, Z67 Bloom front, Z68 Folding, Z69 Replacement, Z70 Long sleep. **Reads as:** mostly ordinary places with something small wrong. Nothing here labels itself, and most are rare.

**105. More Inside** (`longer_inside`) - Count the windows.
- Happened: A station is larger from within than from without.
- In orbit: A plain small station. Varies: how many extra rooms; which door the extra corridor is behind.
- Families: top: broken_station. NEW: can_station.
- Inside: Identical corridor sections with one too many, a table with one too many chairs. Uses a tiny wrapped zone as the extra space.
- Occ: EMP 60, HOL 35, SCV 5.
- Hazards/rewards: Getting lost, a slow dread; a strange object at the far end.
- Fit: pre M/H > post L/M | ports B-D | any | R | no | from 52 68 Z68

**106. The Set Table** (`set_table`) - Warm, lit, and no one aboard.
- Happened: Everyone left a working station in the middle of dinner, with no sign of how.
- In orbit: An ordinary station, all running. Varies: every boat still docked, every suit racked.
- Families: top: broken_station. NEW: truss_station.
- Inside: Meals, warm coffee deco, a game half played, music playing faintly, doors open.
- Occ: EMP 75, HOL 25.
- Hazards/rewards: Nothing happens, which is the point; a free working base.
- Fit: pre H > post L/M/H | ports A-D | any | R | S | from 5 53 Z64

**107. The Walkers** (`the_walkers`) - They went out and kept going.
- Happened: Everyone calmly stepped out of every airlock, without suits, all in one direction.
- In orbit: A clean station, its doors open. A line of bodies leads off on one bearing. Varies: what the bearing points at: a real star, a moon or nothing.
- Families: top: broken_station. NEW: flotsam_field.
- Inside: Suits still racked, neat beds, shoes in rows at the airlocks.
- Occ: EMP 85, HOL 15.
- Hazards/rewards: None; the line is a lead.
- Fit: pre H > post L/M/H | ports A-D | any | R | no | from 54 Z65

**108. The Twin** (`twin_station`) - Two of the same, down to the scratches.
- Happened: There is one station too many.
- In orbit: Two matching stations side by side, one slightly fresher. Varies: where they differ; the differences are the reward.
- Families: top: broken_station. NEW: truss_station.
- Inside: Same scratches, same stains, same room plan, one thing moved in each.
- Occ: EMP 55, HOL 30, MCH 15.
- Hazards/rewards: A mirror game; a room only one has.
- Fit: pre H > post L/M | ports A-C | any | X | no | from 55 Z69

**109. The Repeating Dock** (`repeating_dock`) - It comes back at the same time.
- Happened: One bay resets at the same hour on the clock.
- In orbit: A normal small station. Varies: what repeats: a shuttle, a door, a spilled cup.
- Families: top: broken_station. NEW: can_station.
- Inside: One room whose moved things return when nobody's looking, an old shuttle arriving on a schedule.
- Occ: EMP 60, HOL 40.
- Hazards/rewards: A puzzle by clock. Only scenery resets, and looted things stay gone; a key is reachable in the shuttle only at its hour.
- Fit: pre H > post L/M | ports A-D | any | X | no | from 57 Z56

**110. Dead Air** (`dead_air`) - Your own breathing goes quiet.
- Happened: All sound fails near this place.
- In orbit: A small dark cluster of cans. Varies: how far it reaches; its effect fades in with distance.
- Families: top: wreck_field. NEW: can_station.
- Inside: Rooms with mouths drawn on walls, ear guards on hooks, hand-sign pictograms.
- Occ: EMP 60, HOL 40.
- Hazards/rewards: You can't hear warnings, and nothing else in the game does that; a strange find.
- Fit: pre M/H > post L/M | ports B-D | any | R | no | from 54 Z66 Z53

**111. The Bloom Hulk** (`bloom_hulk`) - Bright growth, only here.
- Happened: The Bloom came to a ship in a Bloom region.
- In orbit: A hull wrapped in bright growth. Hard rule: Bloom regions only. Varies: which hull; a corrupted crew.
- Families: top: drifting_hulk, broken_station. NEW: scrap_raft.
- Inside: Grown-through rooms, pulsing sacs, a crew turned to something else.
- Occ: MUT 40, HOL 40, NST 20.
- Hazards/rewards: Warned mutagen; exotic organics, unique material.
- Fit: pre any > post L/M/H | ports any | Bloom regions | X | no | from 16 59 Z67

**112. The Sorted Field** (`sorted_field`) - The wreckage has been put in order.
- Happened: Someone or something tidied a debris field by size.
- In orbit: A wreck field in a neat spiral, large to small. Varies: pattern shape; recent additions.
- Families: top: wreck_field. NEW: debris_shell.
- Inside: None, apart from one object at the centre placed just so.
- Occ: EMP 70, HOL 30.
- Hazards/rewards: It's been added to since last time; a strange centrepiece.
- Fit: pre any > post any | ports any | any | R | no | from 56 60

**113. The Long Sleep Vault** (`long_sleep_vault`) - Lit rows and dark rows.
- Happened: A whole people lay down in a vault above their world, and none of them got up.
- In orbit: A vault with solar wings, its pods dead and its machines still at work. Varies: how many rows stay lit; a keeper; a wake date that passed long ago.
- Families: top: generation_hulk. NEW: vault_capsule.
- Inside: Rows of pods holding the dead, a keeper's chair, a wall of tally lights.
- Occ: SLP 60, MCH 30, EMP 10.
- Hazards/rewards: Only the weight of it, and machines that defend the rows; pod parts, stored goods meant for the waking.
- Fit: pre H > post L/M | ports A-C | any | R | no | from 13 Z70

**114. The Wrack Line** (`wrack_line`) - The tide left these here.
- Happened: Wormhole tides washed things up in orbit.
- In orbit: A long line of foreign objects at one height. Varies: real origins from the star catalogue; machines from far away.
- Families: top: wreck_field. NEW: flotsam_field.
- Inside: Foreign cabins, fittings of strange sizes.
- Occ: EMP 70, SCV 20, HOL 10.
- Hazards/rewards: Odd machines; cross-galaxy goods and leads.
- Fit: pre any > post any | ports any | any | R | S | from 51 Z58 Z57

**115. The Mute Mind** (`mute_mind`) - All the cameras face one wall.
- Happened: A great machine mind stopped answering, and its station went on without it.
- In orbit: A big clean station with a huge sealed core. Varies: all the cameras turn to you later; a Kessari pilgrim.
- Families: top: kessari_foundry, broken_station. NEW: box_works.
- Inside: Rack halls, cameras facing one wall, offerings from machines.
- Occ: MCH 50, EMP 35, HOL 15.
- Hazards/rewards: Mood; cyber module chance, lattice cores.
- Fit: pre H > post L/M/H | ports A-B | any | R | no | from 61 78 Z35

**116. The Clone Stack** (`clone_stack`) - One face, many bunks.
- Happened: A cloning bay kept going until there were far too many of everyone.
- In orbit: A station with extra bunk modules bolted on in a hurry. Varies: one line or many; a fight between copies.
- Families: top: broken_station. NEW: can_station, truss_station.
- Inside: Identical uniforms, bunks five high, a feeding hall, a room of copies of one chair.
- Occ: HSK 30, EMP 35, CYB 15, HOL 20.
- Hazards/rewards: Many of the same enemy; working clone chamber parts.
- Fit: pre H > post L/M | ports A-C | any | R | no | from 55 Z40

**117. The Relabelled Station** (`relabelled_station`) - Everything has a picture on it.
- Happened: The crew were forgetting what things were, so they painted on what each thing was for.
- In orbit: A normal station. Varies: the labels grow cruder deeper in; the last ones are wrong.
- Families: top: broken_station. NEW: truss_station.
- Inside: Hand-painted pictures on doors, chairs, cups and people's bunks; pictures that become strange.
- Occ: EMP 65, SQT 10, HOL 25.
- Hazards/rewards: Signs you can't trust; a keeper with a very limited idea of the world.
- Fit: pre H > post L/M | ports A-C | any | R | S | from 92 Z41

### J. Added themes (second research pass), each filed under its group

Same format, numbered on from the list above, so no earlier number changes.

**Led to by:** as each entry's `from` line says. **Apocalypses:** mainly Z1, Z8, Z26, Z35, Z46, Z53, Z66. **Reads as:** new shapes of story the first pass missed.

**118. The Tone Satellite** (`number_sat`) - Group C, war. Counting for someone who's gone.
- Happened: A spy service's satellite still sends the same runs of tones, on a fixed schedule, to agents who died long ago.
- In orbit: A small plain relay with no markings. Varies: which tones; a sister receiver on the ground; a run that changes once a day.
- Families: top: meridian_relay. NEW: relay_sat, vault_capsule.
- Inside: A sealed box and a tone machine. Its ground partner is a hut with a keypad and a dead listener.
- Occ: EMP 75, MCH 25.
- Hazards/rewards: None. Count the tones and they open a keypad vault on a threaded site elsewhere: a code with no writing.
- Fit: pre M/H > post L/M | ports B-E | any | U | S | from 85 92 Z35

**119. The Smallest Country** (`sky_micronation`) - Group F, culture. It has a flag, a stamp and a border.
- Happened: A family took over an old gun platform and declared it a sovereign state. Sometimes it was an old government in exile who did this instead.
- In orbit: A gun platform with a big flag, a painted border line and a tiny customs hatch. Varies: squatters, exiled court, or a data haven; one person left, still keeping the forms.
- Families: top: vharn_picket, meridian_relay. NEW: gun_platform, can_station.
- Inside: A throne on a pallet, a stamp machine, a coin press, a state banquet table for four, a portrait wall.
- Occ: SQT 70, EMP 20, MCH 10.
- Hazards/rewards: One old gun they can barely fire; odd coins, stamps and flags as decor, and a toll paid in a token.
- Fit: pre M/H > post L/M | ports B-E | any | U | S | from 26 83 84 85 Z46

**120. The First Passenger** (`first_passenger`) - Group G, low-tech skies. Before any person, they sent an animal.
- Happened: A world's first rocket carried a small working animal, and there was never a way to bring it home.
- In orbit: A capsule barely bigger than a crate. Varies: which animal, from the world's own working stock; a food dispenser that ran out; a medal painted on the side.
- Families: top: escape_pods. NEW: capsule_craft.
- Inside: A padded cradle, a feeder, a tiny harness, a sensor strap. Ground echo: a statue of the animal in a town square.
- Occ: EMP 100.
- Hazards/rewards: None; a small sad find, and first-discovery XP.
- Fit: pre L/M > post L/M | ports C-E, none | any | U | S | from 93 86

**121. The Chop Shop** (`chop_shop`) - Group E, work. Numbers ground off.
- Happened: Stolen ships came here to lose their names, and some still do.
- In orbit: A hidden repair frame in a wreck field, with half-repainted hulls. Varies: live Reavers, dead, or a lone mechanic; ghost hull numbers under the new paint match a thread elsewhere.
- Families: top: old_shipyard, wreck_field. NEW: breaker_frame, scrap_raft.
- Inside: Paint bays, grinders, racks of swapped plates, a wall of cut-out hull numbers.
- Occ: GNG 45, SQT 15, EMP 30, BOT 10.
- Hazards/rewards: Armed mechanics; repaint and refit services for a claimed hull, and hulls with no owner.
- Fit: pre M/H > post L/M/H | ports B-D | any | U | no | from 31 79 101

**122. The Frozen Station** (`ice_filled`) - Group D, orbit failed. The whole inside is one block of ice.
- Happened: A station pumped water through its walls as a radiation shield. A burst flooded the decks, then the power died.
- In orbit: A normal hull with ice bulging through every breach. Varies: how full; what the water held; tunnels cut by earlier scavengers.
- Families: top: broken_station. NEW: truss_station, can_station.
- Inside: Rooms solid with clear ice, furniture and crew hanging in it, and a route the player has to cut.
- Occ: EMP 70, SCV 20, NST 10.
- Hazards/rewards: Cutting opens old pockets of pressure; a lot of water, and preserved rooms nobody has reached.
- Fit: pre H > post L/M | ports A-D | cold zones Z5 to Z8 | U | S | from 34 67

**123. The Dead Hand** (`dead_hand`) - Group C, war. If the signal stops, it fires.
- Happened: A strike platform was built to fire if its orders stopped. On the ground, a machine still sends the all-clear.
- In orbit: A rod platform with one fat antenna aimed at a bunker below. Varies: what it's aimed at; how many rods are left; the bunker machine may be failing.
- Families: top: military_wreck. NEW: rod_rack, dish_array.
- Inside: An all-clear lamp beating on a wall, a countdown bank that resets on each beat, a disarm room behind armour.
- Occ: MCH 75, EMP 25.
- Hazards/rewards: Killing the bunker machine starts a warned countdown, and a strike leaves a lasting crater. Disarm it for rods and a rare key.
- Fit: pre M/H > post L/M | ports A-D | any | R | no | from 24 30 Z1 Z8

**124. The Blackout Station** (`blackout_station`) - Group I, strange. They stopped making any signal, on purpose.
- Happened: A crew became afraid of being heard, so they silenced and darkened everything they had.
- In orbit: A station with its windows painted black, its antennas sawn off and its hull coated in soot. Varies: all done by hand, or under orders; one lamp left behind a hood.
- Families: top: broken_station. NEW: can_station.
- Inside: Smashed radios in a neat pile, hooded lamps, felt on the walls, pictograms for silence, boots left at each door.
- Occ: EMP 75, HOL 25.
- Hazards/rewards: Nothing shows on a scan, so it's found by eye; what scared them is never shown.
- Fit: pre H > post L/M | ports A-D | any | R | no | from 54 Z53 Z66

**125. The Cloud Town** (`cloud_town`) - Group E, work. A town held up by its own air.
- Happened: On giants and thick-air worlds, a floating town rides the layer where the pressure is kind and the air inside is what keeps it up.
- In orbit: An aerostat town at a fixed height in the cloud tops. Varies: live or dead, sagging or sound; gas mine or resort; acid-eaten plates.
- Families: top: mining_outpost. NEW: aerostat, tank_farm.
- Inside: A street deck, gas pumps, a railing with a view of nothing but cloud, mooring lines to smaller rigs.
- Occ: SQT 35, EMP 35, GNG 10, MCH 20.
- Hazards/rewards: Corrosive cloud, slow leaks rather than bursts; gas, fuel, trade, and a town in a place with no ground.
- Fit: pre H > post L/M/H | ports A-D | giants, toxic and storm worlds | U | S | from 46 49 81 Z26

**126. The Sunken Hull** (`sunk_in_cloud`) - Group D, orbit failed. It sank until it stopped.
- Happened: A hull fell into a giant's clouds and came to rest at the depth where it floats.
- In orbit: A wreck in the dark layer under the skim limit, lit by lightning. Varies: depth; hull type; how long the player's hull can stand it.
- Families: top: drifting_hulk, derelict_freighter.
- Inside: Crushed outer rooms, a sound inner core, storm light through the breaches.
- Occ: EMP 85, SCV 5, NST 10.
- Hazards/rewards: A warned pressure timer; rich untouched holds, because few can reach it.
- Fit: pre H > post L/M/H | ports any | giants | R | no | from 81 Z42

---

## 5. Deep space themes

Everything outside a planet or moon orbit. Entries use the same format, with **Where** for the places a theme favours, and Fit reading: builder tech | places | region | rarity | S if a dead form may appear in a start system. A deep site is a record on the system map, reached by warp once it's been found. The budget for a system is 4 to 10 deep sites, plus its belt, Trojan and ring fields. About a third of those fields are empty rocks only, as `solar-systems.md` recommends.

The four parts are the owner's: this system's story, a story of its own, passing through, and older than the collapse. Part 5 holds later additions, each marked with the part it belongs to. Each part mixes places, so belts, rings, comets, Trojan points, the star's surroundings and nebulae all appear across the list.

### Part 1. Part of this system's story

These read the system's worlds and the system story. They share a thread with an orbit or a ground site, so a player crossing the system keeps meeting the same sigil.

**D1. The Feud Line** (`feud_line`) - Where two worlds met.
- Happened: Two worlds of one system fought, and their fleets met on the transfer route between them.
- Where: A wreck field in a `gap`, on the true line between two worlds. Varies: who won, by which livery has fewer wrecks; a third party's hulls.
- Families: top: wreck_field, military_wreck. NEW: debris_shell.
- Inside: Pairs of wrecks, last stands, two sets of tally marks, each world's ration tins.
- Occ: EMP 55, BOT 20, SCV 15, MCH 10.
- Hazards/rewards: Live rounds, sleeping point defence; arms, charts of each world.
- Fit: H or M | gap | any | C | S | story: feud

**D2. The Company Rock** (`company_rock`) - One sigil, deep in the belt.
- Happened: A company mined the belt and fed one world from it, right up to the last pay day.
- Where: A hollowed rock in a `belt_field`. Varies: live Freeholders, Reavers, or sealed from outside.
- Families: top: mining_outpost, pirate_hideout. NEW: hollow_rock, mass_driver.
- Inside: Drill halls, an ore elevator, a company store, a bunk warren, a spin room for the sick.
- Occ: EMP 35, SQT 20, GNG 15, NST 15, BOT 15.
- Hazards/rewards: Cave-ins, bad air; capital ship materials, a Freeholder trade point.
- Fit: M or H | belt_field | any | C | S | story: one company

**D3. The Trojan Warehouse** (`trojan_warehouse`) - The reserve nobody drew on.
- Happened: A state kept its emergency stock at a stable point, where it would stay for ever.
- Where: A long stack of holds on a rock at `giant_trojan` or `world_l45`. Varies: robbed, or sealed; its keys may be on the ground.
- Families: top: mining_outpost. NEW: pod_cluster, hollow_rock.
- Inside: Numbered bays of crates, inventory lights, a small guard post.
- Occ: EMP 55, MCH 25, SCV 10, GNG 10.
- Hazards/rewards: Locks, machine guards; huge stocks of one kind per bay.
- Fit: H | giant_trojan world_l45 | any | U | S | story: supply line

**D4. Lost Property** (`lost_property`) - Where everything ends up.
- Happened: Nothing happened. A stable point has been collecting whatever drifted for a thousand years.
- Where: A soft cloud of small things at `world_l45` or `moon_l45`, from every era. Varies: one important thing; a scavenger's shed.
- Families: top: wreck_field. NEW: flotsam_field.
- Inside: Mostly open objects: a wing, a bus-sized pod, one small sealed capsule.
- Occ: EMP 80, SCV 20.
- Hazards/rewards: Gentle; a layered slice of system history, cheap parts, a strange find.
- Fit: any | world_l45 moon_l45 | any | C | S | any

**D5. The Stalled Train** (`stalled_train`) - Tug and pods, a long way from home.
- Happened: A tug's reactor failed halfway, and its train of ore or ice has coasted on since.
- Where: On a transfer path, on rails. Varies: cargo; tug or train first; a line snapped and pods spread out.
- Families: top: derelict_freighter. NEW: tug_train, pod_cluster.
- Inside: A tug cabin, a hot-swap room, sealed pods, a line-of-sight lamp.
- Occ: EMP 65, SCV 20, MCH 15.
- Hazards/rewards: Low risk; lots of one good thing.
- Fit: M or H | gap | any | C | S | story: supply line

**D6. The Relay Chain** (`relay_chain`) - Each buoy points to the next.
- Happened: A line of relays carried a system's voice, and it died from one end.
- Where: Buoys from world to outskirts. Varies: where the gap is; who cut it; last buoy still pointing at a ship.
- Families: top: meridian_relay. NEW: relay_sat, beacon_mast.
- Inside: Tiny service bays.
- Occ: EMP 75, MCH 20, SCV 5.
- Hazards/rewards: A hunt along a line; the end holds something worth the walk.
- Fit: H | gap outskirts | any | U | S | any

**D7. The Comet Riders** (`comet_rider`) - They come back on a date.
- Happened: A crew built a base on a comet to ride it round the star, selling water at each pass.
- Where: On a `comet`. Varies: still alive or a dead base; a world below holds a festival on return.
- Families: top: mining_outpost. NEW: comet_base, tank_farm.
- Inside: Ice tunnels, a thruster room to trim the orbit, a long table.
- Occ: SQT 35, EMP 40, SCV 15, CLT 10.
- Hazards/rewards: Jets of gas; ice, frozen gases, and a thing that's only here sometimes.
- Fit: M or H | comet | any | U | S | any

**D8. The Sun Tap** (`sun_tap`) - The system's power came from here.
- Happened: Collectors near the star fed everything else, until the receivers died.
- Where: `sungrazer`. Varies: bursting with stored power; turned toward a world; mirrors fallen in.
- Families: top: kessari_foundry. NEW: power_sat, mirror_array.
- Inside: Bus halls, cooling fins, a tiny crew cell behind a shield.
- Occ: MCH 45, EMP 45, BOT 10.
- Hazards/rewards: Heat, and beams on the clock; emberite, big power parts.
- Fit: H | sungrazer | any | R | no | story: great work

**D9. The Shade Line** (`shade_station`) - Stay behind the shield.
- Happened: A lab worked close to the star behind one huge shield, and its shield is starting to tilt.
- Where: `sungrazer`. Varies: which rooms are in shadow as it tilts over the clock.
- Families: top: meridian_relay. NEW: film_sheet, truss_station.
- Inside: Rooms with a hard line of light through them, a burnt half, a frozen half.
- Occ: EMP 60, MCH 30, SCV 10.
- Hazards/rewards: Light is the enemy; star data, heat gear, cyber module chance.
- Fit: H | sungrazer | any | R | no | any

**D10. The Arrival Gate** (`arrival_gate`) - Every ship stopped here first.
- Happened: Every ship jumping in was checked here at the edge of the system.
- Where: `outskirts`. Varies: ruined by whatever came; held by a faction; a line of old ships awaiting clearance.
- Families: top: vharn_picket, meridian_relay. NEW: gun_platform, nav_buoy.
- Inside: Checkpoints, holding rooms, a charts wall.
- Occ: EMP 40, FAC 25, MCH 20, SCV 15.
- Hazards/rewards: Picket fire on a live one; shipping records as bearings.
- Fit: H | outskirts | inner arm outer arm | U | S | story: last stand

**D11. The Outward Line** (`outward_line`) - All the guns face out.
- Happened: A system's navy lined up to meet something from outside. There is not one enemy wreck.
- Where: A long arc of wrecks at `outskirts`. Varies: cut clean; burnt; still loaded.
- Families: top: military_wreck, wreck_field. NEW: gun_platform.
- Inside: Battle stations, crews at posts, no breaches from inside.
- Occ: EMP 70, HOL 15, MCH 15.
- Hazards/rewards: The unease; arms in fine condition.
- Fit: H | outskirts | any | R | no | story: last stand

**D12. The Muster Point** (`muster_point`) - Everyone who could came here.
- Happened: Ships from every world met here to jump out together.
- Where: `outskirts`, in a loose crowd. Varies: most left; a jump failure; a share kept waiting.
- Families: top: derelict_freighter, drifting_hulk. NEW: scrap_raft.
- Inside: Ships from every world, joined by tubes, a shared mess, a queue for the jump ship.
- Occ: EMP 50, SCV 20, HSK 15, SQT 15.
- Hazards/rewards: Crowded hulks; a bearing to where the others went.
- Fit: M or H | outskirts | any | U | S | story: evacuation

**D13. The Long Commute** (`cycler`) - It never stops at either end.
- Happened: A great ship was put on a loop between two worlds, with shuttles meeting it at each pass.
- Where: Transfer on rails. Varies: dead or still a floating town; who's aboard.
- Families: top: drifting_hulk. NEW: cycler_hull, spin_ring.
- Inside: Cabins, gardens, a town deck, a shuttle bay with worn tracks.
- Occ: EMP 40, SQT 30, MCH 20, SCV 10.
- Hazards/rewards: Catch it by warp; a moving home with a view of two worlds.
- Fit: H | gap | any | R | S | any

**D14. The Broken World's Wake** (`shatter_wake`) - A slot with no planet in it.
- Happened: A world in this slot was broken, and its works were scattered along its old orbit.
- Where: Debris ring along a shattered body's orbit. Varies: station halves; cities on slabs.
- Families: top: wreck_field, broken_station. NEW: debris_shell, hollow_rock.
- Inside: Tilted streets on a slab, a bunker cut in half.
- Occ: EMP 65, SCV 15, BOT 10, HOL 10.
- Hazards/rewards: Tumbling rock; exposed ores, ruin salvage.
- Fit: H | gap | any | R | no | any

**D15. The Black Yard** (`black_yard`) - Not on any map.
- Happened: A navy built something secret in a dark rock.
- Where: `belt_field` or `belt_lane`, hidden. Varies: finished or not; the ship left; what it was built to do.
- Families: top: old_shipyard. NEW: hollow_rock.
- Inside: Unmarked halls, blank name plates, a test cradle, strange blocks.
- Occ: EMP 45, MCH 35, CYB 10, NST 10.
- Hazards/rewards: Machines, no map marker; a prototype hull, cyber modules.
- Fit: H | belt_lane belt_field | any | R | no | any

**D16. The Claim Rush** (`claim_rush`) - Every rock has a stake in it.
- Happened: A rich find set off a rush, and the belt filled with small claims overnight.
- Where: Dozens of tiny staked rocks round a boom-town can in a `belt_field`. Varies: boom, bust or feud; only one claim was ever truly rich.
- Families: top: mining_outpost, wreck_field. NEW: can_station, hollow_rock, nav_buoy.
- Inside: An assay office with scales, a bar, a wall of claim tags, hand-dug tunnels, a stake with a sigil on every rock.
- Occ: EMP 45, SCV 20, SQT 15, GNG 20.
- Hazards/rewards: Claim traps, jumpers; rich small pockets, and the assay tags show which rock paid.
- Fit: M or H | belt_field | outer arm | U | S | any

**D17. The Hermit's Rock** (`hermit_rock`) - One lit window.
- Happened: Someone chose to be alone out here.
- Where: `belt_field` or `gap`. Varies: young, old, dead last week; garden or none.
- Families: top: mining_outpost. NEW: hollow_rock, can_station.
- Inside: A tidy cabin, a tray of plants, a telescope, a barter shelf.
- Occ: SQT 70, EMP 30.
- Hazards/rewards: Safe; barter and a bearing or two.
- Fit: any | belt_field gap | any | C | S | any

**D18. The Continuity Vault** (`continuity_vault`) - The government's spare room.
- Happened: A ruling class built a refuge to outlast its people.
- Where: A dark rock at a Trojan point. Varies: they made it and died of it; an unused bunker; a stale council still sitting.
- Families: top: mining_outpost. NEW: hollow_rock, vault_capsule.
- Inside: Council hall, fine cabins, wine, staff warrens below.
- Occ: EMP 50, MCH 25, HSK 15, HOL 10.
- Hazards/rewards: Locked doors; lattice cores, high-level keys.
- Fit: H | giant_trojan world_l45 | any | R | no | any

**D19. The Sail Graveyard** (`sail_graveyard`) - Kilometres of silver rags.
- Happened: Slow sail freighters were left in rags when the war stopped their lasers.
- Where: `gap`. Varies: colour of sunlight; which sails hold.
- Families: top: derelict_freighter. NEW: sail_rig, film_sheet.
- Inside: Small cabins at the heart of each sail, rigging winches.
- Occ: EMP 85, SCV 15.
- Hazards/rewards: Tangling film; a beautiful flight, film salvage.
- Fit: H | gap | any | U | S | any

**D20. The Ice Haven** (`ice_haven`) - Reavers at home.
- Happened: Clans hollowed out ice rocks at the system's edge and live off passing ships.
- Where: `ice_field`, `outskirts`. Varies: clan and sigil; size; at feud.
- Families: top: pirate_hideout. NEW: hollow_rock, scrap_raft.
- Inside: Ice halls, stolen ships, a chief's table, a treasure room.
- Occ: GNG 70, FAC 15, EMP 15.
- Hazards/rewards: A real base raid; contraband, hijackable core.
- Fit: any | ice_field outskirts | outer arm | C | no | any

**D21. The Beachhead** (`vharn_beachhead`) - They've arrived.
- Happened: A Vharn force has set up in the outer system, and it's growing.
- Where: Rock or moon at `outskirts`. Varies: picket, depot or fortress.
- Families: top: vharn_picket. NEW: gun_platform, hollow_rock.
- Inside: Segmented halls, hive-like storerooms.
- Occ: FAC 90, EMP 10.
- Hazards/rewards: War; capture target, their tech.
- Fit: H | outskirts | inner arm core | U | no | any

**D22. The Relay Spire** (`relay_spire`) - Machines talking.
- Happened: Kessari clans keep a relay on a bare rock.
- Where: `gap`, `belt_field`. Varies: friendly or not; a spire or a village.
- Families: top: kessari_foundry. NEW: beacon_mast, dish_array.
- Inside: Lattice rooms, repair racks.
- Occ: FAC 75, MCH 20, EMP 5.
- Hazards/rewards: Calm if you're polite; machine trade, wormhole hints.
- Fit: H | gap belt_field | any | U | no | any

**D23. The Reclaim Fleet** (`reclaim_fleet`) - Meridian is back.
- Happened: A Meridian salvage force is rebuilding an old station, piece by piece.
- Where: Round a big ruin in any place. Varies: progress; a guarded find; a fight with scavengers.
- Families: top: meridian_relay, broken_station. NEW: tug_train, truss_station.
- Inside: New decks on old, labelled crates, a clean shift canteen.
- Occ: FAC 75, SCV 15, EMP 10.
- Hazards/rewards: Friendly; missions, repairs, a look at how the galaxy is recovering.
- Fit: H | any | inner arm outer arm | U | no | any

**D24. The Priced Wrecks** (`priced_wrecks`) - Everything here has a tag on it.
- Happened: Exchange assessors surveyed an old wreck field and put a price on each hull.
- Where: A wreck field with a small booth at its edge. Varies: which field; prices by rarity; an honest clerk machine or a Reaver who took over the booth.
- Families: top: ledger_waystation, wreck_field. NEW: nav_buoy.
- Inside: Colour-coded tags welded to hulls, a booth with a price board in numerals, a rights token dispenser.
- Occ: FAC 45, EMP 40, SCV 15.
- Hazards/rewards: Pay for salvage rights, or take it and lose standing; tagged hulls are honestly graded.
- Fit: H | any | not rim | U | no | any

**D25. The Rock Homestead** (`rock_homestead`) - A family and a garden in the dark.
- Happened: Freeholders set up on a rock with a dome and a stove.
- Where: `belt_field`, `ice_field`. Varies: mood; recently raided; a child's swing in low gravity.
- Families: top: mining_outpost. NEW: bubble_garden, hollow_rock.
- Inside: Kitchen, dome garden, tool shed.
- Occ: SQT 75, EMP 15, GNG 10.
- Hazards/rewards: None; trade, safe respawn.
- Fit: M or H | belt_field ice_field | outer arm | C | S | any

**D26. The Old Lane** (`old_lane`) - A road, in space.
- Happened: Traffic between two worlds followed a marked lane with lights and wayside stops.
- Where: A line across a `gap`. Varies: which lights live; wrecks at the bad bends; shrines.
- Families: top: wreck_field. NEW: nav_buoy, beacon_mast.
- Inside: Small roadhouses, a tow service, a mechanic's hut.
- Occ: EMP 60, SCV 20, SQT 10, GNG 10.
- Hazards/rewards: Highwaymen; a line of small stops.
- Fit: H | gap | any | C | S | story: holy route, supply line

**D27. The Quarantine Ring** (`system_quarantine`) - The whole system is flagged.
- Happened: Warning buoys were laid round a system by neighbours who would not come in.
- Where: Around the outskirts. Varies: which pictogram; ships parked outside, watching.
- Families: top: meridian_relay. NEW: nav_buoy, gun_platform.
- Inside: A tiny watch post in some.
- Occ: MCH 50, EMP 40, SCV 10.
- Hazards/rewards: Warned; if you came in, did you bring it out?
- Fit: H | outskirts | any | U | no | story: quarantine

**D28. The Fold Scar** (`fold_scar_port`) - Where the drive let go.
- Happened: A warp accident tore a scar in space and wrecked the port beside it.
- Where: `fold_scar`. Varies: size; leaks; what comes through.
- Families: top: broken_station. NEW: truss_station.
- Inside: Warped decks at a sphere's edge, fold crystal growth.
- Occ: EMP 55, HOL 30, SCV 15.
- Hazards/rewards: Cheap warps here; odd leaks; fold crystal.
- Fit: H | fold_scar | any | R | no | any

**D29. The Memorial** (`empty_orbit_memorial`) - A chapel for a planet.
- Happened: Survivors of a lost world built one small place on its old orbit.
- Where: `gap`, in a debris ring. Varies: kept up or not; visitors' plaques.
- Families: top: broken_station. NEW: can_station.
- Inside: A bell, plaques by the hundred, a garden tray.
- Occ: CLT 40, EMP 50, SQT 10.
- Hazards/rewards: Calm; a quiet moment.
- Fit: M or H | gap | any | U | S | any

**D30. The Mouth Fort** (`mouth_tollhouse`) - Somebody claimed the door.
- Happened: A wormhole mouth was fortified to tax or block whatever came through.
- Where: `mouth`. Varies: held, or a ruin with a battle scar.
- Families: top: vharn_picket, ledger_waystation. NEW: gun_platform, gate_frame.
- Inside: Toll booth halls, guns on the inward side.
- Occ: FAC 45, EMP 25, GNG 20, MCH 10.
- Hazards/rewards: Tolls or a fight; a route to another star.
- Fit: H | mouth | any | U | no | any

**D31. The Lighthouse** (`hazard_light`) - It warns of something real.
- Happened: A keeper's light marks a danger, and the keeper's gone.
- Where: `gap`, near a real hazard. Varies: what it warns of: mines, a fold scar, rocks.
- Families: top: meridian_relay. NEW: beacon_mast.
- Inside: A keeper's room and lamp hall, a lamp chart in pictures.
- Occ: EMP 55, SQT 25, MCH 20.
- Hazards/rewards: Ignoring it is on you; fuel, repair kit, a safe route.
- Fit: M or H | gap | any | U | S | any

**D32. The Pushed Rock** (`pushed_rock`) - Aimed, and stopped.
- Happened: A rock was given engines and aimed at a world, and work stopped halfway.
- Where: A rail path toward a world. Varies: still armed; a saboteur's bones in the engine room.
- Families: top: mining_outpost. NEW: drive_pack, hollow_rock.
- Inside: Thruster bay, a guidance room, one chair.
- Occ: EMP 55, MCH 30, HOL 15.
- Hazards/rewards: It could be restarted, on any world; rock full of ore, big engines.
- Fit: H | gap | any | R | no | story: the rock

**D33. The Sun Funeral** (`sun_burial`) - A slow fall into the light.
- Happened: A funeral ship with a people's dead is spiralling toward the star.
- Where: Rails from `gap` to `sungrazer`, with a date. Varies: decorated by culture; a stowaway.
- Families: top: drifting_hulk. NEW: can_station.
- Inside: Biers, flowers frozen in vacuum, plaques.
- Occ: EMP 75, CLT 25.
- Hazards/rewards: Heat rises as the date nears; grave goods at a price.
- Fit: M or H | gap sungrazer | any | R | S | any

**D34. The Listening Post** (`listening_post`) - All dishes on one star.
- Happened: Someone built a huge array out in the dark to listen to one neighbour.
- Where: `dark`, `outskirts`. Varies: what star: a real neighbour; silent or still listening.
- Families: top: meridian_relay. NEW: dish_array, can_station.
- Inside: Listening rooms, recording reels, a wall of a star's pulses as marks.
- Occ: EMP 60, MCH 30, HOL 10.
- Hazards/rewards: Calm; a lead to that star.
- Fit: H | dark outskirts | any | U | S | any

**D35. The Star Bridge** (`star_bridge`) - A works between two suns.
- Happened: A pair of close stars was mined from the point between them.
- Where: `barycentre`. Varies: two-shadow light; hot or dead.
- Families: top: kessari_foundry. NEW: mirror_works, tether_node.
- Inside: Cooling halls, an old foundry floor, heat doors.
- Occ: MCH 55, EMP 35, BOT 10.
- Hazards/rewards: Extreme heat; emberite, starsilver.
- Fit: H | barycentre | any | X | no | any

### Part 2. A story of its own

These owe nothing to the system round them, so a player can meet any of them anywhere. Each has a small arc that can be read in a single visit.

**D36. The Lost Yacht** (`lost_yacht`) - Two chairs, one view.
- Happened: A couple took a yacht somewhere quiet, and it was too quiet.
- Where: Any `gap`. Varies: an argument, or peace; a share were never lost, only left.
- Families: top: derelict_freighter. NEW: capsule_craft.
- Inside: Fine cabin, two chairs at a window, a shared last meal.
- Occ: EMP 95, HOL 5.
- Hazards/rewards: None; small luxuries.
- Fit: H | gap | any | C | S | any

**D37. Two Pilots** (`two_pilots`) - They got each other.
- Happened: Two fighters met out here and neither came home.
- Where: Any `gap`. Varies: which liveries; rammed, or cut up.
- Families: top: military_wreck. NEW: capsule_craft.
- Inside: Two small cockpits, a bullet hole, a chance to read who was better.
- Occ: EMP 100.
- Hazards/rewards: None; a small weapon find.
- Fit: M or H | gap | any | C | S | any

**D38. The Survey Dig** (`survey_dig`) - They found something in a rock.
- Happened: A survey crew dug into an asteroid around one strange inclusion.
- Where: `belt_field`, `giant_trojan`. Varies: fossil, machine part or nothing; crew left in order or fled.
- Families: top: mining_outpost. NEW: hollow_rock, can_station.
- Inside: Scaffolds round a cut face, sample trays, tent-like cans.
- Occ: EMP 60, SCV 15, HOL 15, NST 10.
- Hazards/rewards: Falling rock; a strange find, survey data.
- Fit: H | belt_field giant_trojan | any | U | S | any

**D39. The Reaver Hoard** (`reaver_cache`) - Hidden, and trapped.
- Happened: A dead Reaver chief hid a hoard and told no one.
- Where: `belt_lane`, a dark rock. Varies: trap; rival's skeleton at the door; empty.
- Families: top: pirate_hideout. NEW: hollow_rock.
- Inside: A trapped corridor, a chest room.
- Occ: EMP 70, BOT 15, GNG 15.
- Hazards/rewards: Traps; valuable loot, found by a map on another Reaver site.
- Fit: any | belt_lane | outer arm | U | S | any

**D40. The Second Victim** (`second_victim`) - The salvage crew didn't leave either.
- Happened: A newer ship boarded an old wreck, and whatever killed the first got the second.
- Where: Any. Varies: nesters, gas, trap, Hollow; a third ship may have followed.
- Families: top: derelict_freighter, drifting_hulk. NEW: capsule_craft.
- Inside: Two ages of gear, a linked tube, signs of a hurried retreat that never made it.
- Occ: NST 40, EMP 25, HSK 20, HOL 15.
- Hazards/rewards: The player knows it's coming; two loads of loot.
- Fit: H | any | any | C | no | any

**D41. The Nest Hulk** (`nest_hulk`) - Holds full of eggs.
- Happened: Something got in a hull and made it home.
- Where: Any. Varies: nester type by hull heat and food.
- Families: top: drifting_hulk, derelict_freighter.
- Inside: Webbed holds, chewed ducts, a brood room near a warm reactor.
- Occ: NST 80, EMP 10, MUT 10.
- Hazards/rewards: Fights in ducts; rare organics.
- Fit: any | any | any | C | no | any

**D42. Marooned** (`maroon_rock`) - One chair on a rock.
- Happened: A crew put their captain off on a rock with a chair and a crate.
- Where: Any rock. Varies: lived how long; a marked calendar; a rescue that came too late.
- Families: top: escape_pods. NEW: capsule_craft.
- Inside: A chair, a crate, a flagpole in the dust, marks.
- Occ: EMP 95, HOL 5.
- Hazards/rewards: None; the captain's sealed keys open a hull elsewhere.
- Fit: any | belt_field | any | C | S | any

**D43. The Night Market** (`night_market`) - Here this week, gone next.
- Happened: A travelling black market moves on the clock to stay ahead of everyone.
- Where: Changes place on a schedule. Varies: stock, guards.
- Families: top: pirate_hideout, ledger_waystation. NEW: scrap_raft, pod_cluster.
- Inside: Lit stalls in a hold, private rooms.
- Occ: GNG 45, SQT 35, FAC 20.
- Hazards/rewards: Don't start anything; rare goods and cyber modules at a price.
- Fit: any | gap belt_lane | not rim | U | no | any

**D44. The Pit in the Rock** (`rock_arena`) - Blood sport, low gravity.
- Happened: Reavers cut a fighting pit in an asteroid.
- Where: `belt_field`. Varies: dead; in use; prisoners.
- Families: top: pirate_hideout. NEW: hollow_rock.
- Inside: Terraced seats, cages, an armoury of odd weapons.
- Occ: GNG 60, EMP 30, MUT 10.
- Hazards/rewards: Fight your way in; prize weapons.
- Fit: any | belt_field | outer arm | U | no | any

**D45. The Drifting Monastery** (`drift_monastery`) - Adrift by choice.
- Happened: An order cut its engines so that no one could leave.
- Where: Any `gap`. Varies: faith; alive or dead; a bell that tolls by hull.
- Families: top: drifting_hulk. NEW: can_station.
- Inside: Cells, chapel, kitchen garden, a sealed engine room.
- Occ: CLT 55, EMP 30, HOL 15.
- Hazards/rewards: Calm; medicine, pilgrim trade.
- Fit: any | gap | any | U | S | any

**D46. The Patient Builder** (`patient_builder`) - It adds one block a day.
- Happened: A lone machine keeps building a ship for no one, from what it can find.
- Where: `belt_field`, `gap`. Varies: what it's building; it grows on the clock, and if anything is cut away or taken it stops for good.
- Families: top: kessari_foundry, old_shipyard. NEW: box_works.
- Inside: A half-made ship that changes between visits.
- Occ: MCH 90, EMP 10.
- Hazards/rewards: Harmless; come back to see it grow. It offers no fight and no trade, and salvage ends it.
- Fit: H | belt_field gap | any | U | S | any

**D47. The Time Capsule** (`time_capsule`) - Opened by pictures.
- Happened: A people sent their best to the future, with pictures showing how to open it.
- Where: A dark rock. Varies: which people; a puzzle of pictograms.
- Families: top: mining_outpost. NEW: vault_capsule.
- Inside: A sealed room with seeds, tools, models and art.
- Occ: EMP 95, MCH 5.
- Hazards/rewards: Solve the pictures; a good reward with no fight.
- Fit: M or H | belt_lane dark | any | U | S | any

**D48. The Gravity Lab** (`gravity_lab`) - Which way is down?
- Happened: A lab tested gravity plates and left them on.
- Where: Any. Varies: each room with its own down; a walkable hull.
- Families: top: broken_station. NEW: truss_station.
- Inside: Rooms with different floors, test weights hanging at odd angles.
- Occ: EMP 55, MCH 30, BOT 15.
- Hazards/rewards: Falls in odd directions; plate parts.
- Fit: H | gap | any | R | S | any

**D49. The Test Range** (`test_range`) - Mannequins and targets.
- Happened: A navy tested weapons on old hulls full of dummies.
- Where: `gap`. Varies: scorch pattern; one unexploded warhead.
- Families: top: military_wreck, wreck_field. NEW: gun_platform.
- Inside: Dummy crews at posts, instrument pods, range markers.
- Occ: EMP 70, MCH 20, BOT 10.
- Hazards/rewards: A live warhead; test weapons.
- Fit: H | gap | any | U | S | any

**D50. The False Call** (`false_distress`) - It isn't a rescue.
- Happened: Reavers leave a beacon going on a dead ship and wait.
- Where: Any. Varies: how good the trap is; the trappers may be dead too.
- Families: top: derelict_freighter. NEW: beacon_mast.
- Inside: A staged wreck, a hidden hatch.
- Occ: GNG 60, EMP 30, BOT 10.
- Hazards/rewards: An ambush with a tell; their stolen goods.
- Fit: any | gap | any | C | no | any

**D51. The Last Call** (`last_call`) - Someone is really out there.
- Happened: A pod is calling, and the air runs out on the clock.
- Where: Any. Varies: how long; who.
- Families: top: escape_pods. NEW: capsule_craft.
- Inside: A pod, one person, a small tank.
- Occ: SQT 80, EMP 20.
- Hazards/rewards: Race it; carry them to a port for a reward. No companion.
- Fit: any | gap | any | U | no | any

**D52. Dead Money** (`dead_money`) - A fortune in nothing.
- Happened: A bank's treasure ship was guarded to the end, and its money is worthless now.
- Where: Any. Varies: mutiny; a vault at its heart.
- Families: top: derelict_freighter. NEW: vault_capsule.
- Inside: A strongroom full of notes and tokens, guards at posts.
- Occ: MCH 45, EMP 45, GNG 10.
- Hazards/rewards: Hard vault; dead money as odd decor, and real gold among it.
- Fit: H | gap | any | U | S | any

**D53. The Archive Barge** (`archive_barge`) - A library that floats.
- Happened: A world's books were put in a barge to keep them safe.
- Where: Any. Varies: moulded or frozen; a keeper.
- Families: top: derelict_freighter. NEW: box_works.
- Inside: Stacks of shelves, a reading room, cold air.
- Occ: EMP 70, SQT 15, HOL 15.
- Hazards/rewards: Calm; a place of objects, not text: maps, pictures and survey plates.
- Fit: M or H | gap | any | U | S | any

**D54. The Circus Ship** (`circus_ship`) - The show was on the way to the next town.
- Happened: A travelling show broke down between stars.
- Where: Any. Varies: animals' cages open or not; crew left or stayed.
- Families: top: derelict_freighter. NEW: pod_cluster.
- Inside: Big top hold, costumes, cages, a painted wagon.
- Occ: EMP 60, MUT 15, NST 15, SQT 10.
- Hazards/rewards: Odd beasts; costumes and decor.
- Fit: H | gap | any | U | S | any

**D55. Open Cells** (`open_cells`) - They opened from inside.
- Happened: A prison transport's doors all opened at once.
- Where: Any. Varies: who got out; what was in the last cell.
- Families: top: derelict_freighter. NEW: pod_cluster.
- Inside: Rows of cells, a guard deck shot up, one very thick door.
- Occ: EMP 50, HSK 20, NST 15, HOL 15.
- Hazards/rewards: The last cell; guard weapons.
- Fit: H | gap | any | U | no | any

**D56. The Rock Eater** (`rock_eater`) - It makes more of itself.
- Happened: A self-copying mining machine has been eating a field.
- Where: `belt_field`. Varies: how far; a share gone wrong. It doesn't travel to worlds.
- Families: top: kessari_foundry. NEW: box_works.
- Inside: Rock turned into neat blocks and machines.
- Occ: BOT 75, EMP 25.
- Hazards/rewards: Slow machines that defend themselves; refined stock.
- Fit: H | belt_field | any | R | no | any

**D57. The Leviathan Fall** (`leviathan_fall`) - A dead giant, and what eats it.
- Happened: A void leviathan died out here, and a little world of scavengers grew on it.
- Where: `gap`, `nebula_pocket`. Varies: age; picked clean or fresh.
- Families: NEW: bone_wreck.
- Inside: Rib halls, walkable bones, leech colonies.
- Occ: NST 55, EMP 30, SCV 15.
- Hazards/rewards: Leeches; unique bone material.
- Fit: none | gap nebula_pocket | any | X | no | any

**D58. The Whaler** (`whaler_ship`) - A ship with a harpoon and a flensing deck.
- Happened: Hunters chased void creatures, and it went badly.
- Where: Any `gap`. Varies: which side won; the catch alongside.
- Families: top: derelict_freighter. NEW: bone_wreck.
- Inside: A big gun, a cutting deck, tally of kills, jars.
- Occ: EMP 55, NST 25, SCV 20.
- Hazards/rewards: Calm; harpoon guns, rare materials.
- Fit: M or H | gap | any | U | S | any

**D59. The Ring Grazers** (`ring_grazers`) - Something is eating the ice.
- Happened: Vacuum-adapted grazers live on a ring.
- Where: `ring_part`. Varies: type; a keeper's hide.
- Families: top: wreck_field. NEW: can_station.
- Inside: A watcher's hide.
- Occ: EMP 40, NST 60.
- Hazards/rewards: Rarely hostile; discovery, survey.
- Fit: none | ring | any | R | S | any

**D60. The Crash in the Rock** (`rock_crash`) - Ship and rock are one thing now.
- Happened: A ship flew into a big rock and its survivors dug in.
- Where: `belt_field`. Varies: from days to decades lived.
- Families: top: derelict_freighter, mining_outpost. NEW: hollow_rock.
- Inside: Crumpled decks with a dug extension, a stove.
- Occ: EMP 55, SQT 25, NST 20.
- Hazards/rewards: Unstable; a claimable half-buried base.
- Fit: any | belt_field | any | C | S | any

**D61. The Blind Harbour** (`blind_harbour`) - Nobody sees in here.
- Happened: A dark nebula hides a harbour that doesn't exist on any map.
- Where: `nebula_pocket`, dark kind. Varies: Reavers, or Bloom in Bloom regions.
- Families: top: pirate_hideout. NEW: scrap_raft.
- Inside: Moored ships, a blind pilot's guide rope of beacons.
- Occ: GNG 50, EMP 30, SCV 20.
- Hazards/rewards: Near-blind flight; contraband.
- Fit: any | nebula_pocket | any | U | no | any

**D62. The Blue Garden** (`nebula_garden`) - A research ship in blue dust.
- Happened: A research crew stayed in a reflection nebula to study its young stars.
- Where: `nebula_pocket`, reflection kind. Varies: alive or gone; drifting grazers.
- Families: top: meridian_relay. NEW: bubble_garden, can_station.
- Inside: A dome, specimen rooms, plates of nebula light.
- Occ: EMP 50, SQT 25, NST 25.
- Hazards/rewards: Calm; rich survey data.
- Fit: H | nebula_pocket | any | U | S | any

**D63. The Magnet Knot** (`magnet_wreck`) - All the steel points one way.
- Happened: A magnetar's field dragged ships together.
- Where: Near a magnetar. Varies: pulled shape; storm cycle.
- Families: top: wreck_field. NEW: debris_shell.
- Inside: Hulls fused, tools on the walls, doors stuck.
- Occ: EMP 80, BOT 20.
- Hazards/rewards: Field storms, blinking shields; rare fused metals.
- Fit: any | near magnetar | any | X | no | any

**D64. The Bloom Nest-Ship** (`bloom_nest_ship`) - Something grew a ship.
- Happened: A grown thing crossed space and stopped here.
- Where: Bloom regions only. Varies: shape; what it holds.
- Families: NEW: bone_wreck, bubble_garden.
- Inside: Grown chambers, sacs, stolen crew.
- Occ: MUT 50, HOL 40, NST 10.
- Hazards/rewards: Deadly; unique material.
- Fit: none | Bloom regions | core | X | no | any

**D65. The Circle** (`nose_in_circle`) - All noses to the middle.
- Happened: Ships from many eras sit in a perfect ring, each pointing at the same empty spot.
- Where: `dark`, `gap`. Varies: count; new arrival; compass drift nearby. Nothing explains it.
- Families: top: derelict_freighter, drifting_hulk, military_wreck.
- Inside: Each ship's crew at the windows, facing inward.
- Occ: EMP 70, HOL 30.
- Hazards/rewards: The effects build as you approach; old hulls of every era.
- Fit: any | dark gap | core | X | no | any

**D66. The Storm Chaser** (`storm_chaser`) - Anchored in the weather.
- Happened: A crew rode an ion storm through a system to study it.
- Where: Moves with a storm on the clock. Varies: rig; survivors.
- Families: top: meridian_relay. NEW: can_station, dish_array.
- Inside: Instruments, a heavy storm shelter, ground-down wiring.
- Occ: EMP 55, SQT 25, MCH 20.
- Hazards/rewards: Shield drain inside the storm; capacitors, stormcore chance.
- Fit: H | gap | any | U | S | any

**D67. The Film Lot** (`film_lot`) - A fake ship inside a real one.
- Happened: A drama about space was filmed in space, on painted sets.
- Where: Any. Varies: show's genre; a fake bridge fooling Husks.
- Families: top: broken_station. NEW: box_works.
- Inside: False doors, painted stars, fake controls, a real control room behind.
- Occ: EMP 75, SCV 15, MCH 10.
- Hazards/rewards: A gag; costumes, fake weapons.
- Fit: H | gap | inner arm | U | S | any

**D68. The Carved Rock** (`carved_rock`) - A leader's face, a kilometre high.
- Happened: A ruler had an asteroid carved into a self-portrait.
- Where: `belt_field`, `gap`. Varies: half finished; blasted off by rebels.
- Families: top: mining_outpost. NEW: hollow_rock.
- Inside: A shrine in the head, workers' huts in the neck.
- Occ: EMP 60, CLT 20, SCV 20.
- Hazards/rewards: Landmark; a vault in the skull.
- Fit: H | belt_field gap | any | R | S | any

### Part 3. Passing through or adrift from a neighbouring system

Every entry here uses the star catalogue. `origin` is a real neighbouring star from `Galaxy.Neighbours`, picked by the pair hash in section 3, and `heading` a real star or none. Livery and cargo read the origin star's kind and region. The site uses the rules in the moving POIs note below, and its `arrival` must obey the distance table.

**D69. The Misjumped Liner** (`misjump_liner`) - Very far from where it should be.
- Happened: A liner came out of a jump in the wrong system, with all its passengers.
- Where: `outskirts`, on a passer path. Varies: when: century old or last year; crew's fate.
- Families: top: drifting_hulk. NEW: spin_ring.
- Inside: Cabins with a wrong-star chart, a queue at the bridge, the local liner's sigil.
- Occ: EMP 45, HSK 25, NST 15, SCV 15.
- Hazards/rewards: Large dungeon; a nav chart of its origin star.
- Fit: H | outskirts | any | U | S | origin: neighbour, misjump

**D70. The Neighbours' Refugees** (`neighbour_refugees`) - From the star you can see.
- Happened: Refugees from a neighbouring system's fall ended up here.
- Where: `gap`, a raft near a world. Varies: settled in, turned away, or shot down by this system.
- Families: top: derelict_freighter. NEW: scrap_raft, pod_cluster.
- Inside: Family cabins, a dialect of pictures, a list of those left behind as tally stones.
- Occ: SQT 35, EMP 40, HSK 15, SCV 10.
- Hazards/rewards: Wary; a lead home to their world.
- Fit: M or H | gap | any | U | S | origin: neighbour

**D71. The Yellow Ghost** (`yellow_ghost`) - Don't let it dock.
- Happened: A plague ship from a neighbour is still flying its warning flag.
- Where: Passer path. Varies: real disease or a lie; what's aboard.
- Families: top: derelict_freighter. NEW: beacon_mast.
- Inside: Warning stripes, sealed wards, a captain's chair.
- Occ: HSK 45, EMP 35, NST 20.
- Hazards/rewards: Warned biohazard; medical loot, a cure hint.
- Fit: H | gap | any | U | no | origin: neighbour

**D72. The Long Way Round** (`generation_passing`) - They don't know it's a ship.
- Happened: A generation ship is still on its way, its people unaware of the galaxy. Idea 21.
- Where: Passer path, slow. Varies: tribes by deck; the destination; gone mad; or dead.
- Families: top: generation_hulk. NEW: spin_drum, drive_pack.
- Inside: Farm decks, a sky painted on a ceiling, deck tribes, sealed bridge.
- Occ: SQT 50, EMP 20, GNG 15, CLT 15.
- Hazards/rewards: Huge, and needs large interiors; a culture to meet, without words.
- Fit: H | gap | any | X | no | origin: slow boat, age sets distance

**D73. The Sleeper Coast** (`sleeper_coast`) - Still going, with no one awake to arrive.
- Happened: A sleeper ship is coasting on to a world that may no longer exist, and everyone aboard died in the pods.
- Where: Passer path. Varies: its keeper machine; how many rows stay lit; its target star.
- Families: top: generation_hulk. NEW: vault_capsule.
- Inside: Rows of pods holding the dead, one keeper's quarters.
- Occ: SLP 65, MCH 35.
- Hazards/rewards: The keeper guards its rows; colony stores, a real star as a goal.
- Fit: H | gap | any | R | no | origin: slow boat

**D74. The Rogue's Station** (`rogue_station`) - Passing through on a dark world.
- Happened: A research base on a starless world drifted into this system.
- Where: `rogue`, on a passer path. Varies: alive or not; how close it passes.
- Families: top: mining_outpost. NEW: hollow_rock, can_station.
- Inside: Geothermal rooms, a dark garden, instruments.
- Occ: EMP 50, SQT 25, NST 25.
- Hazards/rewards: Dark and cold; unique finds. Here for a limited time.
- Fit: H | rogue | any | X | no | origin: wanderer

**D75. The Foreign Comet** (`foreign_comet`) - Not from around here.
- Happened: A comet from another star passes through with something in it.
- Where: `comet`, hyperbolic. Varies: inclusions; fast; its home star by bearing.
- Families: top: mining_outpost. NEW: comet_base.
- Inside: A dig camp if someone reached it, or none.
- Occ: EMP 90, SCV 10.
- Hazards/rewards: Fast; rare ices once.
- Fit: none | comet | any | R | S | origin: real star, hyperbolic

**D76. The Transit Fleet** (`transit_fleet`) - Not stopping.
- Happened: A Vharn force is crossing through, on its way somewhere else.
- Where: `gap`, moving on the clock. Varies: size; what it carries; a straggler.
- Families: top: vharn_picket, military_wreck.
- Inside: War hulls if boarded.
- Occ: FAC 95, EMP 5.
- Hazards/rewards: Leave it alone or raid a straggler; a heading to a real war.
- Fit: H | gap | inner arm core | U | no | origin: neighbour

**D77. The Convoy Stop** (`convoy_stop`) - Traders with a timetable.
- Happened: Exchange convoys stop here between stars, on a fixed clock.
- Where: `gap`, `outskirts`. Varies: goods from a real star.
- Families: top: ledger_waystation. NEW: pod_cluster.
- Inside: Cargo market at anchor.
- Occ: FAC 90, EMP 10.
- Hazards/rewards: Trade; a chance to meet a ship from far off.
- Fit: H | gap outskirts | not rim | C | no | origin: neighbour

**D78. The Pilgrim Fleet** (`pilgrim_fleet`) - All going to one star.
- Happened: Pilgrims are crossing the map to a holy star, one jump at a time.
- Where: `gap`, on the clock. Varies: faith; poor or rich; target star.
- Families: top: derelict_freighter, drifting_hulk. NEW: scrap_raft.
- Inside: Crowded holds, shared kitchens, shrines.
- Occ: CLT 65, SQT 25, EMP 10.
- Hazards/rewards: Calm; trade, a real star as lead.
- Fit: any | gap | any | U | no | origin: neighbour, heading real

**D79. The Nomad Raft** (`nomad_raft`) - A town that never stops.
- Happened: Families live on rafted ships and move on between stars.
- Where: Near a world for a stretch. Varies: mood; size.
- Families: top: derelict_freighter. NEW: scrap_raft.
- Inside: Street deck, workshops, kids.
- Occ: SQT 75, EMP 10, FAC 15.
- Hazards/rewards: Fair traders.
- Fit: any | gap | any | U | no | origin: neighbour

**D80. The Station on the Run** (`station_on_the_run`) - Someone bolted engines to it.
- Happened: A whole station was given engines to flee.
- Where: Passer path. Varies: still pushing, or out of fuel.
- Families: top: broken_station. NEW: drive_pack, truss_station.
- Inside: A town behind an engine room, repurposed halls.
- Occ: SQT 35, EMP 40, GNG 15, HSK 10.
- Hazards/rewards: A strange silhouette; big drives.
- Fit: H | gap | any | R | no | origin: neighbour

**D81. The Broken Tow** (`broken_tow`) - One tug, one tow, far apart.
- Happened: A tow parted in transit between stars.
- Where: Two sites a long way apart. Varies: what was towed; the tug's fate.
- Families: top: derelict_freighter, drifting_hulk. NEW: tug_train.
- Inside: A tug, a line; the tow elsewhere.
- Occ: EMP 75, SCV 15, MCH 10.
- Hazards/rewards: Two-stage find.
- Fit: H | gap | any | U | S | origin: neighbour

**D82. The Old Probe** (`old_probe`) - Still going, with a map on it.
- Happened: A sublight probe launched long ago from a real star is crossing this system.
- Where: Passer path, slow. Varies: plate shows real stars by pulse pattern.
- Families: top: meridian_relay. NEW: relay_sat.
- Inside: None: a plate to read.
- Occ: EMP 100.
- Hazards/rewards: Safe; reveals a real star, and a first discovery.
- Fit: M or H | gap | any | U | S | origin: slow boat, age sets distance

**D83. The Spill** (`flotsam_spill`) - A million of the same small toy.
- Happened: A container ship from a neighbour lost a load, and the spill has spread across a system.
- Where: Long smear in a `gap`. Varies: what it carried: toys, boots, bottles.
- Families: top: wreck_field. NEW: flotsam_field, pod_cluster.
- Inside: Open containers.
- Occ: EMP 95, SCV 5.
- Hazards/rewards: Comic; a few valuable boxes.
- Fit: H | gap | any | C | S | origin: neighbour

**D84. The Route Keeper** (`route_keeper`) - Waiting for a stop that's gone.
- Happened: An automatic freighter ran a loop of stars after the fall, until the next stop on its list stopped answering. It's still waiting at this one.
- Where: Moored at a dead station. Varies: holds full or looted; a stowaway's camp; which real stars are on its route board.
- Families: top: derelict_freighter, ledger_waystation. NEW: pod_cluster.
- Inside: A clean automated hold, a route board of real star bearings, a stowaway's camp.
- Occ: MCH 75, EMP 15, SCV 10.
- Hazards/rewards: Cargo machines; loot and bearings. A place only: it carries no one.
- Fit: H | gap | any | R | no | origin: real loop of stars

**D85. The Far Spill** (`spill_from_afar`) - It should not be here at all.
- Happened: A wormhole dropped something from across the galaxy.
- Where: `mouth`. Varies: far star; alien hardware.
- Families: top: derelict_freighter. NEW: flotsam_field.
- Inside: Foreign fittings, wrong-sized doors.
- Occ: EMP 75, SCV 15, HOL 10.
- Hazards/rewards: Strange parts, a far bearing.
- Fit: any | mouth | any | R | S | origin: far star

**D86. Half Arrived** (`half_arrived`) - It came through and stopped.
- Happened: A ship dropped out of a jump only partly.
- Where: `fold_scar`, `gap`. Varies: which half; fade line.
- Families: top: derelict_freighter. NEW: truss_station.
- Inside: Rooms ending in a soft edge; things half-gone.
- Occ: EMP 60, HOL 40.
- Hazards/rewards: Don't touch the edge; a piece of drive.
- Fit: H | gap | any | R | no | origin: neighbour, misjump

**D87. The Ash Ship** (`ash_ship`) - It saw its star die.
- Happened: A ship fled a star that blew, and was burnt on one side.
- Where: Passer path. Varies: the scorched side points back at a real remnant nebula.
- Families: top: drifting_hulk. NEW: spin_ring.
- Inside: Burnt side and cold side, crew at windows.
- Occ: EMP 65, HSK 15, SCV 20.
- Hazards/rewards: Radiation; rare star-forged metals.
- Fit: H | gap | any | R | S | origin: real remnant nebula

**D88. The Envoy's Barge** (`envoy_barge`) - The gifts never reached the table.
- Happened: A diplomatic ship was on the way to a neighbouring star.
- Where: Any. Varies: who killed it: enemy, or their own.
- Families: top: derelict_freighter. NEW: spin_ring.
- Inside: Fine rooms, gift chests for a real star's government, a sealed treaty case.
- Occ: EMP 75, SCV 15, MCH 10.
- Hazards/rewards: Calm; luxury gifts, a sigil of a real system.
- Fit: H | gap | any | U | S | origin: neighbour

**D89. The Seed Fleet** (`seed_fleet`) - Late for a colony.
- Happened: Automated colony pods are arriving to settle a world that's already taken.
- Where: Inbound rails, with a date. Varies: target world; what it plants.
- Families: top: escape_pods. NEW: lander_carrier, pod_cluster.
- Inside: Kit pods: tools, seed, fold-out sheds.
- Occ: MCH 80, EMP 20.
- Hazards/rewards: Calm; pods land at a date near you, and they make free kit.
- Fit: H | gap | any | R | S | origin: slow boat

**D90. The Wandering Dock** (`wandering_dock`) - Repairs, while you wait.
- Happened: A Kessari repair clan works its way through systems.
- Where: Stays for a stretch. Varies: price, parts.
- Families: top: kessari_foundry, orbital_yards.
- Inside: Lattice dock, spare racks.
- Occ: FAC 90, EMP 10.
- Hazards/rewards: Paid repair service.
- Fit: H | gap | any | U | no | origin: neighbour

**D91. Bottles** (`bottle_buoys`) - Thrown out by people who knew they were finished.
- Happened: A dying world threw small pictorial message capsules at its neighbours.
- Where: Scattered in a `gap`. Varies: pictures; what they ask for.
- Families: top: escape_pods. NEW: relay_sat.
- Inside: A sealed capsule with a picture plate.
- Occ: EMP 100.
- Hazards/rewards: Safe; a lead home to a real star.
- Fit: M or H | gap | any | U | S | origin: neighbour

**D92. Still on Approach** (`still_on_approach`) - Their lamp asks for a berth.
- Happened: A long-haul crew set out before the collapse. Their grandchildren have arrived in the same pressed uniforms, at a dead port. Idea 23, in space, with no pods.
- Where: `world_l45` or the outer marker of a dead highport. Varies: how long they've waited.
- Families: top: derelict_freighter. NEW: spin_ring.
- Inside: New paint, tidy crew, working everything, no idea.
- Occ: SQT 85, EMP 15.
- Hazards/rewards: Calm and sad; fresh old-world stock.
- Fit: H | world_l45 | any | R | S | origin: neighbour, slow

### Part 4. Older than the collapse

Founders-age human things and Precursor things. These are the oldest and usually the strangest. Precursor sites don't use block interiors unless the player can reach them on foot, and they hold precursor alloy and echo shards by the design's rules. What is beyond understanding is only glimpsed.

**D93. The Ring Fragment** (`ring_fragment`) - A piece of something too big.
- Happened: A section of a Precursor band hangs in space, far from any other.
- Where: Any. Varies: curve; length; a sealed end.
- Families: NEW: ring_arc, precursor_shell.
- Inside: Dark halls of stone-like walls, vast doors.
- Occ: EMP 75, MCH 20, HOL 5.
- Hazards/rewards: Guardians; precursor alloy.
- Fit: Precursor | any | core periphery | X | no | ground echo 107

**D94. The Pointing Beacon** (`pointing_beacon`) - It knows where the next one is.
- Happened: Precursor beacons point to one another across stars.
- Where: `dark`, `outskirts`. Varies: part of a chain.
- Families: NEW: monolith.
- Inside: One chamber with a line of light.
- Occ: EMP 100.
- Hazards/rewards: A bearing to a real star; chain to a vault.
- Fit: Precursor | dark outskirts | any | R | no | design: Quiet Reach

**D95. The Shell** (`shell_monolith`) - A big smooth nothing.
- Happened: A huge empty shell has hung here longer than the star's planets.
- Where: `gap`, `dark`. Varies: door; hollow inside; a faint inner light.
- Families: NEW: monolith, precursor_shell.
- Inside: A single vast space.
- Occ: EMP 95, HOL 5.
- Hazards/rewards: A landmark and a discovery.
- Fit: Precursor | gap dark | core | R | no | any

**D96. The Old Swarm** (`swarm_remnant`) - Panels round a star, by the thousand.
- Happened: Someone long gone started to wrap a star.
- Where: `sungrazer`, `pole`. Varies: dead or working; star dimmed.
- Families: NEW: swarm_panel, monolith.
- Inside: A panel's service tunnel.
- Occ: EMP 75, MCH 25.
- Hazards/rewards: Heat; a dim star that's dim for a reason.
- Fit: Precursor | sungrazer | core | X | no | any

**D97. The Nudge** (`star_nudge`) - Something pushes the star.
- Happened: A structure is slowly moving a star, or was.
- Where: `pole`. Varies: glimpse only.
- Families: NEW: precursor_shell.
- Inside: None reachable: a spectacle.
- Occ: EMP 100.
- Hazards/rewards: Survey data, awe. Never lootable.
- Fit: Beyond | pole | core | X | no | any

**D98. The First Voyage** (`first_voyage`) - Very small, very brave.
- Happened: One of the first ships out, from an age before fast travel.
- Where: `gap`, `dark`. Varies: sublight; a real star as its home.
- Families: top: drifting_hulk. NEW: lander_carrier.
- Inside: Cramped cabins, hand-riveted bulkheads.
- Occ: EMP 85, SLP 5, HOL 10.
- Hazards/rewards: Calm; a museum piece.
- Fit: M | gap dark | any | R | S | origin: slow boat

**D99. The Titan Hull** (`titan_hull`) - It isn't a ship for people.
- Happened: A hull built for something far larger drifts here.
- Where: Any. Varies: stairs a metre high; doors of hill size.
- Families: top: generation_hulk. NEW: precursor_shell.
- Inside: Enormous rooms. Needs a jetpack, as idea 29.
- Occ: EMP 70, NST 20, HOL 10.
- Hazards/rewards: Odd scale; alien alloy.
- Fit: Precursor | gap | core inner arm | X | no | any

**D100. The Sealed Rock** (`sealed_rock`) - They packed and left.
- Happened: A Precursor vault in a rock was emptied before anyone came.
- Where: `belt_field`. Varies: one thing left.
- Families: top: mining_outpost. NEW: monolith.
- Inside: Packed cases, empty plinths.
- Occ: EMP 80, MCH 20.
- Hazards/rewards: Traps; echo shards.
- Fit: Precursor | belt_field | any | R | no | ground echo 110

**D101. The Fossil Reef** (`fossil_reef`) - A stone forest in vacuum.
- Happened: Something once lived in the rings or the dust here, in huge numbers.
- Where: `ring_part`, `belt_field`. Varies: branched rock in a field.
- Families: NEW: bone_wreck.
- Inside: Walkable fossil tunnels.
- Occ: EMP 85, NST 15.
- Hazards/rewards: Survey data, rare mineral.
- Fit: none | ring belt_field | any | R | S | any

**D102. The Orrery** (`orrery`) - A model with one body too many.
- Happened: A Precursor model of this system still turns, and it has a world that doesn't exist now.
- Where: `gap`. Varies: which world.
- Families: NEW: monolith.
- Inside: A chamber with the system in stone, moving on the clock.
- Occ: EMP 90, HOL 10.
- Hazards/rewards: It marks the system's real bodies, and one missing.
- Fit: Precursor | gap | any | X | no | any

**D103. The Tally Stone** (`tally_stone`) - It's counting something.
- Happened: A stone with a row of marks adds one each game year.
- Where: Any rock. Varies: how many are left.
- Families: NEW: monolith.
- Inside: One face.
- Occ: EMP 95, HOL 5.
- Hazards/rewards: Nothing, and nobody knows. It's safe to leave it unexplained.
- Fit: Precursor | belt_field | any | R | S | any

**D104. The Dead Gate** (`dead_gate`) - A door to where?
- Happened: A great ring frame stands dark.
- Where: `outskirts`, `dark`. Varies: part powered.
- Families: NEW: gate_frame, monolith.
- Inside: Service halls in the rim.
- Occ: EMP 75, MCH 20, HOL 5.
- Hazards/rewards: Parts; one route in a rare case.
- Fit: Precursor | outskirts dark | periphery | X | no | design: Sealed Gate

**D105. The Drifting Cathedral** (`drift_cathedral`) - Too big to be for praying.
- Happened: An alien cathedral, or something like one, turns slowly.
- Where: `gap`. Varies: chapels; choir of hull notes.
- Families: NEW: precursor_shell, monolith.
- Inside: A nave, an altar, tones through the floor.
- Occ: EMP 75, CLT 15, HOL 10.
- Hazards/rewards: Chamber traps; relic.
- Fit: Precursor | gap | core | X | no | any

**D106. The Older War** (`elder_battlefield`) - The scars aren't ours.
- Happened: A moon is cut by weapons nobody here could have built.
- Where: Any. Varies: clean canyons, glass seas.
- Families: NEW: precursor_shell, debris_shell.
- Inside: Fused wreck remnants.
- Occ: EMP 90, MCH 10.
- Hazards/rewards: Odd ores; survey data.
- Fit: Precursor | any | core | R | no | any

**D107. The Sentinels** (`sentinel_ring`) - Guards of nothing, still on duty.
- Happened: Ancient machines still guard a point in space.
- Where: `dark`, round a sealed thing. Varies: how close you can go.
- Families: NEW: monolith, gun_platform.
- Inside: Their charging bays.
- Occ: MCH 90, EMP 10.
- Hazards/rewards: Warned, and they never leave their ring; precursor alloy.
- Fit: Precursor | dark | core | R | no | any

**D108. The Lost Founders** (`lost_founders`) - They never made it.
- Happened: A colony fleet from the founding age died on the way to a world now settled.
- Where: `gap`, `outskirts`. Varies: an old sigil from the ground.
- Families: top: generation_hulk. NEW: lander_carrier.
- Inside: Unused landers, frozen seed.
- Occ: EMP 70, SLP 10, NST 10, HOL 10.
- Hazards/rewards: Founders-age parts; the ground's oldest legends.
- Fit: M or H | gap outskirts | any | U | S | ground echo 4 95

**D109. The Great Work** (`great_work`) - An old folly, a thousand years dead.
- Happened: A human age before the old civilisation began something huge, then gave up.
- Where: Any. Varies: ring, bridge, tower; a town built in the unused structure.
- Families: top: orbital_yards. NEW: ring_arc, truss_station.
- Inside: Empty scaffold halls, worn stairs.
- Occ: EMP 60, SQT 20, SCV 20.
- Hazards/rewards: Large; old alloys.
- Fit: H | any | inner arm | R | S | any

**D110. The First Door** (`first_door`) - Standing in space, with no wall round it.
- Happened: It's a door. There's nothing else.
- Where: `dark`. Varies: nothing. Very rare, and it reveals nothing.
- Families: NEW: monolith.
- Inside: A sill, a frame and a dark gap that the suit lamp doesn't reach past.
- Occ: HOL 100.
- Hazards/rewards: Effects build as you approach. It doesn't open for anyone.
- Fit: Beyond | dark | core | X | no | any

**D111. The Glimpse** (`passing_glimpse`) - You saw something.
- Happened: Something is here for seconds, then not.
- Where: Any, rarely. Varies: shape, never the same.
- Families: NEW: precursor_shell.
- Inside: None. It's an event, not a place.
- Occ: EMP 100.
- Hazards/rewards: None. It leaves a mark on the map, and nothing else. No loot, ever.
- Fit: Beyond | any | core | X | no | any

**D112. The Nest of Shells** (`nested_shell`) - One inside another inside another.
- Happened: Spheres within spheres hang around a dim star.
- Where: `sungrazer`. Varies: count; how far in you can go.
- Families: NEW: precursor_shell, swarm_panel.
- Inside: Outer halls only. The inner ones are glimpsed.
- Occ: EMP 75, MCH 15, HOL 10.
- Hazards/rewards: Heat; echo shards.
- Fit: Precursor | sungrazer | core | X | no | any

**D113. The Disc Graveyard** (`accretion_tomb`) - Ancient hulks in the bright ring.
- Happened: Old ships circle a black hole in its disc.
- Where: Round a black hole. Varies: era; tidal strain.
- Families: top: drifting_hulk. NEW: precursor_shell.
- Inside: Stretched decks, hull strain.
- Occ: EMP 85, HOL 15.
- Hazards/rewards: Warned tide and radiation; unique materials. Design: The Heart.
- Fit: Precursor | near black hole | core | X | no | design: The Heart

**D114. The Pulse Clock** (`pulsar_clock`) - It keeps time by a dead star.
- Happened: A timing station round a pulsar still works, between beams.
- Where: Near a pulsar. Varies: move between sweeps.
- Families: NEW: monolith, dish_array.
- Inside: Rooms that are only safe between beats.
- Occ: EMP 75, MCH 25.
- Hazards/rewards: Beat-timed radiation; fold crystal.
- Fit: Precursor | near pulsar | any | R | no | any

**D115. The Pole Lantern** (`pole_lantern`) - A sail on a light.
- Happened: An ancient sail hovers far over a star's pole, held up by light.
- Where: `pole`. Varies: faint inner lamp; a thin mirror.
- Families: NEW: sail_rig, monolith.
- Inside: Tiny hub.
- Occ: EMP 95, MCH 5.
- Hazards/rewards: Nobody passes here by accident; old film, a lead.
- Fit: Precursor | pole | any | R | S | any

**D116. The Stone Lattice** (`lattice_of_stones`) - Rocks in a perfect grid.
- Happened: Rocks hold position in a cube, a kilometre apart, with no engines.
- Where: `belt_lane`, `dark`. Varies: one missing node.
- Families: top: wreck_field. NEW: monolith.
- Inside: None: fly through it.
- Occ: EMP 95, HOL 5.
- Hazards/rewards: A sight; the missing rock is somewhere.
- Fit: Precursor | belt_lane dark | any | X | no | any

### Part 5. Added themes (second research pass), each marked with its part

**D117. The Keep-Out Rock** (`keep_out_rock`) - Part 1, this system's story. Built to say go away.
- Happened: A system buried its worst waste in a rock, and covered the rock in shapes meant to frighten anyone, for ever, in any language.
- Where: `dark`, `belt_lane`. Varies: thorns, leaning spikes, or black blocks in streets that lead nowhere; a thin moan through its hollow spires by contact.
- Families: top: mining_outpost. NEW: hollow_rock, vault_capsule, monolith.
- Inside: Long empty halls of warning pictograms, growing plainer and more urgent, then the sealed store.
- Occ: EMP 90, HOL 10.
- Hazards/rewards: The warning is true: heavy radiation; reactor fuel, for those who ignore it.
- Fit: M or H | dark belt_lane | any | R | no | ground echo: warning_field

**D118. The Plug** (`plugged_mouth`) - Part 1, this system's story. They sank ships in the door.
- Happened: A wormhole mouth was jammed with scuttled hulls to stop something coming through.
- Where: `mouth`. Varies: a wall of hulls wedged nose-in; cut through from this side, or from the other.
- Families: top: derelict_freighter, military_wreck. NEW: debris_shell.
- Inside: Hulls welded to one another, charges still set, cabins facing a dark gap.
- Occ: EMP 65, MCH 20, HOL 15.
- Hazards/rewards: Clearing it reopens a route, and nobody says what's behind it; drives, and a lasting change.
- Fit: H | mouth | any | R | no | story: last stand

**D119. The World Bridge** (`world_bridge`) - Part 1, this system's story. A cable from one world to the other.
- Happened: Two locked double planets were tied together, and traffic ran along the line.
- Where: Between a double planet, a far line with a real node midway. Varies: whole or parted; the cut ends in each world's sky.
- Families: top: orbital_highport. NEW: elevator_node, climber_car.
- Inside: A halfway hall with no up or down, customs for two worlds, stalled cars.
- Occ: EMP 50, SQT 20, SCV 15, MCH 15.
- Hazards/rewards: Whipping ends if parted; a landmark in both worlds' skies.
- Fit: H | double planets | any | X | no | story: feud, great work

**D120. The Slow Fire** (`slow_fire`) - Part 2, a story of its own. It has been burning for a century.
- Happened: A sealed hull with a hold of oxidiser caught fire, and it has smouldered ever since on its own cargo.
- Where: Any `gap`. Varies: which decks are alight; a hull warm to the touch; soot in the frozen vents.
- Families: top: derelict_freighter, drifting_hulk.
- Inside: Hot bulkheads, glowing seams, blackened corridors, bad air, frost one wall away.
- Occ: EMP 85, NST 15.
- Hazards/rewards: Opening a door feeds it, and a hull breach may end it; intact far holds.
- Fit: M or H | gap | any | U | S | any

**D121. The Funeral Fleet** (`funeral_fleet`) - Part 2, a story of its own. Never meant to fly.
- Happened: A ruler was sealed in a hollow rock with a whole fleet, built to serve in the next life, and with its builders.
- Where: Dark rock in a `belt_field`. Varies: statue crews at every post, each face different; robbed or sealed; traps.
- Families: top: military_wreck, mining_outpost. NEW: hollow_rock, vault_capsule.
- Inside: Rows of perfect unflown hulls, stone crews, a burial chamber, the builders' last room, set behind the wall.
- Occ: EMP 70, MCH 30.
- Hazards/rewards: Old automatic guns on trip lines; grave goods, new hulls with no engines fitted.
- Fit: H | belt_field | any | R | no | any

**D122. The Ice Carrier** (`ice_carrier`) - Part 2, a story of its own. A warship carved from a berg.
- Happened: A poor navy, short of steel, cut a huge carrier from ice and packed fibre.
- Where: `ice_field`, `outskirts`. Varies: sagging under its own weight; a cold plant that has failed.
- Families: top: military_wreck. NEW: comet_base, tank_farm.
- Inside: Halls cut in pale ice, a hangar, refrigeration pipes, fighters frozen in.
- Occ: EMP 70, SCV 15, NST 15.
- Hazards/rewards: Weak walls that can be cut; water, small hulls.
- Fit: M or H | ice_field outskirts | outer arm | R | S | any

**D123. The Farthest Hut** (`farthest_hut`) - Part 2, a story of its own. It's here because it's the furthest.
- Happened: Someone reached the point furthest from anything in this system, and left a hut and a bust to prove it.
- Where: `dark`. Varies: bust of whom; earlier visitors' tokens in a row.
- Families: top: meridian_relay. NEW: can_station.
- Inside: One room, a stove, a shelf of tokens from visitors, a bust facing home.
- Occ: EMP 100.
- Hazards/rewards: None; first-discovery XP, and the player can leave a token, which stays.
- Fit: any | dark | any | U | S | any

**D124. The Long Clock** (`long_clock`) - Part 2, a story of its own. Built to run ten thousand years.
- Happened: A group of patient people cut a slow clock into a rock, to be wound by anyone who came.
- Where: `belt_field`. Varies: stopped or running; a sun lens that resets it; a chime felt through the walls by contact.
- Families: top: mining_outpost. NEW: hollow_rock, box_works.
- Inside: A shaft of walkable gears, a winding station, a great date dial in numerals.
- Occ: EMP 75, CLT 15, MCH 10.
- Hazards/rewards: Moving gears; winding it is its own small reward, and the date dial gives the true age of things.
- Fit: M or H | belt_field | any | R | S | any

**D125. The Dead Ground** (`dead_ground`) - Part 2, a story of its own. Power fails here.
- Happened: A patch of space kills machines. Ships that drifted in stayed.
- Where: `gap`, `nebula_pocket`, with a warned edge. Varies: size; a wreck at the centre; the cause is never shown.
- Families: top: derelict_freighter, wreck_field.
- Inside: Dark hulks with dead cores, suits at the hatches, a rope line someone strung between two wrecks.
- Occ: EMP 85, HOL 15.
- Hazards/rewards: Engines and lights die at the edge, so the player coasts in and carries on out the far side, which always works; hidden wreck at the centre.
- Fit: any | gap nebula_pocket | any | R | no | any

**D126. The Big Visitor** (`big_visitor`) - Part 4, older than the collapse. It doesn't care that you're here.
- Happened: Something huge, smooth and sealed is passing through on its own errand, as it did before anyone here was born.
- Where: Passer path, once only. Varies: one way in; its inside lights up as it nears the star, and it leaves on its date.
- Families: NEW: precursor_shell, monolith.
- Inside: One tiny wrapped zone of an empty inner landscape, stairs too tall, machines that ignore the player.
- Occ: EMP 65, MCH 35.
- Hazards/rewards: Leave before it does, with warning; echo shards. It never answers, and it never comes back.
- Fit: Precursor | gap | core inner arm | X | no | origin: unknown

### Note on moving POIs

**Orbit POIs don't move.** Owner decision 1 settles this, following Empyrion. An orbit POI has a parent, a bearing and a height, as `Derelicts.cs` already has it, and no period. Worlds and moons move on rails on one clock, and a site's zone goes with its world. So an orbit site is a fixed point in its own world's sky, seen from the same place on the ground every night, which is what lets stone rings, dishes and guns line up with it.

**The one exception is the faller.** `falling_star` stands still until its date, then comes down once. It leaves a stamp on the ground at a known place, using the same stamp system as craters, and that change lasts.

**Deep sites are mostly fixed too.** A few move, because the body they ride moves or because moving is their story. Position is a pure function of a site's elements and the shared clock, so nothing needs simulating and co-op stays in step.

| Motion | Path | Speed on the chart | How you reach it |
| --- | --- | --- | --- |
| Rails | Circle round the star | Slow | Warp |
| Transfer | Ellipse between two bodies, with a fixed period | About 350 m/s at 1.5 chart AU in half a year | Warp only |
| Comet ride | Long ellipse | Fast near the star | Warp; a return date |
| Passer | Hyperbola in, past the star, and out, along the true bearing to its origin | About 450 m/s across 8 chart AU in 2 years | Warp, within its window |
| Convoy | Waypoints on a timetable | As transfer | Warp, or a lead |
| Growing | Stays still and adds blocks by the clock | None | Warp, then fly |

`Orbiter` today has circular elements only, so comets, transfers and passers need one small extension: a path kind and two extra elements. All views then read them, as `StarSystem` promises.

**The bubble, for deep movers only.** These can move faster than a small vessel's 130 m/s. Within about 1.5 km of one, the frame locks to the site: it stands still and the sky turns round it, so docking has no speed to match. Warp arrivals land in the bubble's frame.

**Visitors are a fixed list.** POIs don't replenish, so a system rolls its visitors once: 0 to 3, each with a set window, from `Mix.Of(systemSeed, i)`. A visitor comes, leaves, and doesn't return. A claimed one stays where it was claimed, and anything looted stays looted. New ones come only through the maintenance sweep in decision 4.

**Warning.** A visitor shows on a system scan at the start of its window, so a player can plan. The window is about 2 game years, 16 real hours. Anything with a date shows the date.

**No rides.** Nothing moving carries a player between systems. Paid passage and hitching wait, as decision 5 says, so moving hulls are places to visit.

**Cost.** A moving site is a kinematic structure, and only one or two need to be near a player at a time. `Place` in the slice is static by design, so a moving POI is a `Vessel` with no pilot, as `sci-fi-inspirations.md` says. That costs nothing extra until a player is close, because it's only a position until then.

---

## 6. Structure families

A family is a silhouette and a way to assemble it. It's not a place. The same `spin_ring` can be a hotel, a nursery, a prison or a lottery hall, because the role picks the rooms and the kit picks the look.

**Size classes.** Block counts assume a hollow build on the 2 m grid, with decks only where rooms need them. They are targets to measure, not limits from code. `Derelicts.cs` today builds about 30 m hulls, class S.

| Size | Length | Blocks | How it's built |
| --- | --- | --- | --- |
| XS | Under 20 m | 20 to 150 | Often on the 0.5 m small grid: pods, satellites, capsules |
| S | 20 to 60 m | 150 to 1,500 | One structure |
| M | 60 to 150 m | 1,500 to 8,000 | One structure |
| L | 150 to 400 m | 8,000 to 30,000 | One structure, with interior rooms built when the player gets close |
| XL | 400 to 1,000 m | 30,000 at once | Sections: only the two or three nearest exist as blocks |
| H | Over 1 km | 30,000 at once | Far model plus up to three real sections |

**All families assemble the same way.** A family is a list of **parts**, each a small program-and-kit layout. A grammar joins parts along a spine or a ring: spine, node, module, arm, attachment. `Cells` in `Derelicts.cs` already has the verbs: `Box`, `Tube`, `HullDoor`, `Hole`, `Clear`, `Scars`, `Clump`. Damage is a later pass that removes blocks, so any layout takes any damage. All blueprints validate through `Blueprint.Validate`, with "no core" allowed for wrecks.

Nothing floats: tethers, hoses and rope run between two anchors, or hang from one along the site's spin or tide direction, as the owner's rule says.

### Stations and habitats

| Family | Size | Modules | Assembly |
| --- | --- | --- | --- |
| `truss_station` | S to L | Hub, node, tube, module box, solar wing, radiator | Spine of tubes, modules on nodes, as `Station()` today, extended by count |
| `spin_ring` | M to L | Spoke, ring section, hub, counterweight | 8 to 16 straight ring sections at 22 to 45 degree steps, joined by short angled joiners. Straight sections suit the grid. Any turning is a look only, and gravity is core gravity |
| `spin_drum` | XL to H | End cap, inner surface zone | Far model outside. Inside, a tiny wrapped surface zone, by group 5 in `sci-fi-inspirations.md`, only on a powered site |
| `can_station` | XS to S | Can, docking collar, foil wrap, small panels | Mid-tech. Round-feeling cans of octagonal shape, stacked in a line or T |
| `bubble_garden` | S to M | Glass dome, soil bed, pump room | Dome on a hull or rock, soil as blocks |
| `aerostat` | M to L | Lift envelope, deck, gondola, mooring line | A big faceted envelope over a block town. It holds a fixed height in a giant's or thick world's cloud tops |
| `hollow_rock` | M to L | Rock shell, drilled halls, dock mouth | Rock mesh shell with block interiors cut in: uses asteroid generation |
| `comet_base` | S to M | Ice shell, thruster mounts | As `hollow_rock`, on an ice body on a path |
| `orbital_highport` (exists) | L to XL | Terminal, arm, gate | Star of arms round a terminal |
| `broken_station` (exists) | S to M | As truss, with a torn module | `Station()` today |
| `meridian_relay`, `ledger_waystation`, `vharn_picket`, `kessari_foundry` (exist) | S to M | Faction kits on truss or lattice | Faction layer on family |

### Ships and craft

| Family | Size | Modules | Assembly |
| --- | --- | --- | --- |
| `derelict_freighter` (exists) | S to M | Bridge, crew deck, hold, engines | `Ship()` today |
| `drifting_hulk` (exists) | M to L | Fore, aft, spine | `Hulk()` today |
| `military_wreck` (exists) | S to L | Armoured hull, gun mounts | Ship grammar in the military kit |
| `generation_hulk` (exists) | XL to H | Deck sections, cryo halls | Sections |
| `capsule_craft` | XS | Capsule, service box | Small grid, by tech: riveted iron, foil, or plain alloy |
| `scrap_raft` | S to L | Joined small craft, cut tubes, rope lines | Scatter of craft, tied by the nearest-neighbour graph. Ropes follow nothing-floats |
| `tug_train` | S to XL | Tug, line, pods | Pods in a line with cable between hooks. Up to 3 near pods real, the rest far models |
| `lander_carrier` | L to XL | Keel, lander bay, sleep hall | Long keel with bay ribs. Founder kit. Strip pass removes plate, leaving ribs |
| `cycler_hull` | M to L | Spin section, tender dock | Ship grammar with ring |
| `drive_pack` | S | Engine, tank, frame | Bolted to any structure's end |
| `escape_pods` (exists) | XS | Pod | `Pods()` today |

### Yards and industry

| Family | Size | Modules | Assembly |
| --- | --- | --- | --- |
| `old_shipyard`, `orbital_yards` (exist) | L to XL | Gantry bay, crane, cradle | Grid of bays with a hull inside any bay |
| `breaker_frame` | M to L | Cutting gantry, sorted part piles | Ship grammar, sectioned by a pass that clears slices |
| `box_works` | S to L | Plant box, conveyor, hopper, chute | Rooms from factory programs |
| `mirror_works` | M to L | Dish, focal furnace, slag chute | Dish as a far model plus block ribs |
| `tank_farm` | S to L | Sphere tank, frame, pump room | Spherical tanks are octagonal block shells on a truss |
| `mass_driver` | L to H | Coil section, loader | Long straight track on a moon, on terrain. Two real sections |
| `mass_catcher` | M | Cone, bin hall | Big cone of blocks |
| `scoop_rig` | M | Depot, cable, scoop | Depot plus a long cable as far model, scoop real |
| `tether_node` | S | Hub, reel, cable, hook cage | Cable as far model, node real |
| `mining_outpost` (exists) | S to M | Drill rig, hab | On a rock |

### Infrastructure and small things

| Family | Size | Modules | Assembly |
| --- | --- | --- | --- |
| `relay_sat` | XS | Box, dish, panels | Small grid. One of about 8 layouts by era |
| `sat_string` | XS each | Many `relay_sat` | Instanced far, with up to 12 near as blocks |
| `pod_cluster` | XS to L | Cargo pod, frame, hook | Stacks of the container deco in a frame, instanced in bulk, near ones real |
| `nav_buoy` | XS | Mast, lamp | Instanced |
| `beacon_mast` | S | Long mast, lamp, locker | Small truss |
| `dish_array` | S to L | Big dish, feed, room | Dish as block ribs with a thin skin |
| `mirror_array` | M to H | Film disc, frame, hub | Film as a far mesh; hub real |
| `film_sheet` | M to H | Sheet panels, spars | Thin deco panels on spars, or far mesh |
| `sail_rig` | M to H | Sail, shrouds, cabin | As `film_sheet`, plus a small cabin |
| `power_sat` | L to H | Panel field, emitter | Panel field is far mesh, a real emitter section |
| `gun_platform` | S to M | Turret mount, magazine | Same turrets as the player's |
| `rod_rack` | S | Rack frame, rods | Rods are claimable block objects |
| `mine_buoy` | XS | Small body, sensor | Instanced, and real within 300 m |
| `vault_capsule` | S to M | Thick shell, one room | Heavy armour around a keystone room |
| `gate_frame` | M to XL | Ring frame, service halls | Ring sections |
| `elevator_node` | M to L | Station, cable mount | Real node plus a cable far model running to the ground |
| `climber_car` | S | Car, rails | A hull that rides a route, as movers tier A |

### Megastructures and fields

| Family | Size | Modules | Assembly |
| --- | --- | --- | --- |
| `ring_arc` | H | Ring section | A thin far model across the sky, with 3 real sections |
| `swarm_panel` | XL each | Panel, service tunnel | Instanced far, one real |
| `monolith` | S to XL | Stone-like block shapes | Precursor kit, usually a very simple shape |
| `precursor_shell` | XL to H | Shell section | Far model, a real doorway section, rooms inside by the bodies-not-ours rule |
| `bone_wreck` | L to H | Rib, spine plate | Bone-coloured kit, grown look, as blocks |
| `wreck_field` (exists) | Scattered | Clumps, boxes | `Field()` today |
| `debris_shell` | Whole shell | Fragments | Instanced shell and a local grit layer, with 10 to 30 real clumps round the player |
| `flotsam_field` | Scattered | Small objects by theme | Instanced deco, with real pickups close by |

### New families, by number of themes that want them

Counted from the NEW lines of every entry in sections 4 and 5. Build from the top.

| New family | Themes |
| --- | --- |
| `truss_station` | 36 |
| `can_station` | 32 |
| `hollow_rock` | 21 |
| `pod_cluster` | 21 |
| `spin_ring` | 16 |
| `monolith` | 15 |
| `scrap_raft` | 14 |
| `box_works` | 13 |
| `gun_platform` | 13 |
| `vault_capsule` | 13 |
| `capsule_craft` | 12 |
| `dish_array` | 11 |
| `film_sheet` | 11 |
| `relay_sat` | 11 |
| `tank_farm` | 11 |
| `debris_shell` | 10 |
| `nav_buoy` | 10 |
| `precursor_shell` | 10 |
| `flotsam_field` | 9 |
| `beacon_mast` | 8 |
| `tug_train` | 7 |
| `bubble_garden` | 6 |
| `drive_pack` | 6 |
| `tether_node` | 6 |
| `climber_car` | 5 |
| `lander_carrier` | 5 |
| `bone_wreck` | 4 |
| `elevator_node` | 4 |
| `comet_base` | 3 |
| `gate_frame` | 3 |
| `mass_driver` | 3 |
| `mirror_array` | 3 |
| `power_sat` | 3 |
| `ring_arc` | 3 |
| `sail_rig` | 3 |
| `sat_string` | 3 |
| `breaker_frame` | 2 |
| `cycler_hull` | 2 |
| `mirror_works` | 2 |
| `rod_rack` | 2 |
| `scoop_rig` | 2 |
| `swarm_panel` | 2 |
| `aerostat` | 1 |
| `mass_catcher` | 1 |
| `mine_buoy` | 1 |
| `spin_drum` | 1 |

**Cost and how to keep it bounded.**

- **Budget per zone.** Aim for no more than 30,000 live blocks near a player, in 2 or 3 structures, plus instancing for the rest. To be measured when moved into the slice.
- **Far first.** Every site has a far model made from the same seed: a mesh of its outer blocks, or a simple shape for H class. A player sees 200 sites as points or small meshes.
- **Rooms on approach.** Interiors build only inside about 400 m, so a L class hull costs its shell until then.
- **Sections for XL and H.** Real blocks only for sections near the player. Sections far from any player stay as records, with damage written to the ledger.
- **Instancing for small things.** Fields, satellites, mines, flotsam and shrouds are `MultiMesh`. Only those close enough to touch become blocks.
- **Spin and tumble.** A structure's rotation is a kinematic transform. Only the one that's being boarded needs player physics.
- **Very long lines.** Cables, tethers and rings are drawn as far lines, and their collision only exists at real sections, with warning lights where a ship might hit.
- **Rails only.** No gravity simulation or debris simulation at all. Fragments have fixed tracks.
- **Cap on motion.** At most 2 moving block structures in a zone at a time.
- **Rock fields.** `solar-systems.md` numbers apply: 60 to 200 rocks, with the near ones real.

---

## 7. Flavour tables

Each table says what changes, concretely. Any row can combine with any other. A site reads one row of each table.

### By tech

| Tech | Hull and shape | Interior | Power and air | Signs |
| --- | --- | --- | --- | --- |
| Low (a rare gun-fired or balloon-borne reach, or founders' relics remade) | Riveted iron, brass, thick glass | Leather seats, wooden fittings, oil lamps kept in cages, gauges | Bottled air, chemical heat | Painted by hand, crests, heraldic marks |
| Mid | Foil, thin aluminium cans, folding panels, spent stages | Tight racks, handwheels, switches, straps, paper | Batteries, panels, one small reactor. No artificial gravity. No spin, or a very small one | Big stencils, flags, national colours |
| High, old civilisation | Foss-style chunky hulls, stripes, chevrons, hull numbers | Wide corridors, artificial gravity, teleporter and clone rooms | Fusion, long-life cells, shields | Bold pictogram frames |
| High, survivors and factions | The player's own blocks, plainly | The player's devices, patched old with new | Player power and fuel rules | Faction sigils |
| Precursor | Stone-like shapes, perfect geometry | Vast, bare, a faint inner light | Unknown | Glyph floors, unreadable |
| Beyond | Glimpsed only | None | None | None, and the visor stays silent |

### By place in the system

| Place | Light and sky | Hull effects | Interior effects | Signs and dressing |
| --- | --- | --- | --- | --- |
| Skim, low | World fills half the sky, the same ground always below | Heat scorch on the leading edge if decaying | Things settle along the fall side | Reentry warnings, boost schedules |
| Sync | World small enough to see whole | Even bleach | Windows fixed on one landscape | Pointing marks at ground |
| Graveyard | Cold, tidy | Retirement stencils, dates as marks | Stripped | Tag plates |
| Moon's L points | Moon huge on one side | Lamp-black dust | Dusty corners at stable points | Mass-driver schedules |
| L4/L5 | Far from anything, world a bright dot | Dust cloud | Loose things gathered | Old stock marks |
| Ring | Flat sheet of ice to the horizon, giant overhead | Ice scour | Frost at seals | Lane marks |
| Giant sync | Giant fills the sky, moons passing | Radiation pitting, hardened boxes | Lead-lined rooms | Dose boards |
| Belt | Rocks, grit, dull sun | Rock hits on one face | Dust everywhere, drill marks | Survey sticks |
| Ice field | Dim sun, white rock | Frost, ice growth | Thick walls of ice | Thermal warnings |
| Comet | Jet plumes, tail | Ice crust, jet scars | Ice tunnels | Return dates |
| Sungrazer | White sky, hard shadow | Bleached, silvered, warped | Heat doors, shade lines | Heat pictograms |
| Pole | Star below, the disc of the system edge-on | Quiet | Rare | Unknown |
| Gap | Stars, a far sun | Clean | Normal | Normal |
| Outskirts, dark | Almost no sun, star is a point | Frozen, unlit | Lamp only | Arrival codes |
| Mouth | Stars bent | Stretched look, strange wear | Foreign fittings | Mixed cultures |
| Nebula | Coloured fog | Dust stains | Fog through breaches | Sensor warnings |
| Fold scar | Wrong shadows, sound dulls | Smooth cuts | Odd wear | Warning marks |

### By age

| Years adrift | Outside | Inside with no air | Inside with air | Power |
| --- | --- | --- | --- | --- |
| Under 1 | Clean, fresh scorch | As it was | Warm, stale | On, or just failed |
| 1 to 10 | Dull paint | As it was | Mould starting, fresh frost at leaks | Batteries down, cells still good |
| 10 to 50 | Faded sunward paint, pits | As it was, thin dust | Mould, bad air, rust at damp seals | Long-life cells only |
| 100 to 200 | Chalked paint, sandblasted leading face, hull numbers ghosted | As it was. Food frozen. Frozen moment at its sharpest | Rooms rotted to shapes, soft walls | A rare lamp |
| 200 to 1,000 | Paint gone, plates pitted, drift to stable points | Brittle plastics, dust | Sealed rooms are stale tombs | Only a reactor on trickle |
| Older | Dust skin, micro-craters, hull looks like rock | Empty, grey | No air left at all | None, unless Precursor |

The rule to keep in mind: **vacuum is the preserver, and air is the rot.** On a century-old wreck, the breached rooms are clean and the sealed rooms are the ruin.

### By damage

| Damage | Outside | Inside | Pass |
| --- | --- | --- | --- |
| Pitting | Sandblasted leading face | Pinpricks of light through thin walls | Scatter small holes on one face |
| Tear | Hull ripped, plates bent out | Decks open to space | Remove a jagged strip |
| Clean cut | A straight slice | Rooms in cross section | Remove a plane |
| Sphere bite | Smooth round hole | Curved edges on everything | Remove a sphere |
| Bore | One straight tunnel | Glass tunnel wall | Remove a line, set glass |
| Char | Black on one face, shadows burnt | Fire marks up to where air ran out | Paint pass, plus light loss |
| Crush | Folded nose, bent spine | Rooms squashed | Shift a section |
| Melt | Slumped edges | Ran metal | Remove, plus slag deco |
| Breach | Round hole, plates in | Everything blown one way | Hole, and a blow-out dressing direction |
| Ice burst | Frozen spill on the hull | Burst pipes | Add ice blocks |
| Scar line | Long burn along a hull | Cut power lines | Paint and remove a strip |
| Grey margin | Smooth curve | Half-dissolved rooms | Remove by noise, smooth edge |
| Hand salvage | Neat square cuts, missing plates | Stripped rooms, wires pulled | Clear rooms by value |
| Bolted repair | Bright patches over holes | Mismatched walls | Add plate over a hole |

### By occupancy

| Code | In space it looks like |
| --- | --- |
| EMP | Dead light, dust floating, nothing has moved. Best loot, and the frozen moment |
| NST | Webbed holds, chewed ducts, warm reactor brood rooms. Only in air, or hull-leeches on the outside |
| HSK | Slumped figures in bunks and suits, rising when disturbed, only in rooms with air or with nanite power |
| SCV | A parked skiff, tether lines, a heater, picked-over rooms, a chalk route |
| SQT | Lit windows, patched hull, a garden tray, a barter hatch |
| GNG | Welded plates, spikes, kill marks, a boss room, stolen craft at the dock |
| FAC | Live core, guard ships, repaired docks, working lights, flags |
| MCH | Spotless corridors, cleaners and repair arms, one crew's routine running on |
| CYB | Refit bays, spare limbs, cables grown into walls, cold patrols |
| BOT | Sorted scrap piles, machines cannibalising each other, gnawed hull |
| HOL | Normal at first. Extra door, wrong count, objects set out, sound dying. Never labelled |
| CLT | Offerings welded to the hull, bells in the air rooms, shrines |
| MUT | Claw-torn cabins, in sealed sections only |
| SLP | Rows of lit and dark pods holding the dead, a keeper machine, a low power hum. Nobody wakes |

### By faction

| Faction | How it changes an old site | New build | Signs |
| --- | --- | --- | --- |
| Meridian | Cleaned sections, new doors, stencilled inventories | Big blocks, long spines, bright stripes and chevrons | Corporate sigil, tidy pictograms |
| Ledger | Colour-coded cargo, a market floor | Bulbous pods, banners | Price tokens, trade flags |
| Vharn | Old hull stripped, carapace plates over it, dark glow | Segmented curves | Glowing script-like marks |
| Kessari | Rooms emptied, lattice added, symmetric status lights | Lattices, bare metal | Light rows, no words |
| Reavers | Scrap armour, ram spikes, trophies | Mismatched welded plates | Kill marks, warning stripes |
| Freeholders | Patched by hand, gardens, a fence of old panels | Prefab modules | Hand-painted numbers |
| Hollow | Nothing visible, at first | Nothing built | None |
| Hollow Bloom | Bright growth over all, in Bloom regions only | Grown, not built | None |
| Precursor guardians | Doors shut, machines on watch | None | Glyphs |

### By role: a few examples of what a role adds

| Role | Rooms | Keystone | Sign set |
| --- | --- | --- | --- |
| Hospital | Wards, theatre, pharmacy, morgue | A healing booth or medicine locker | Medical |
| Customs | Lanes, inspection bays, seized goods vault | Stamp token | Queue and stop pictograms |
| School | Classrooms, gym, canteen | Model system | Small icons, bright frames |
| Barracks | Bunks, armoury, mess | Arms locker | Rank bars |
| Hotel | Atrium, suites, pool, kitchens | Casino vault | Luxury icons |
| Chapel | Nave, cells, offerings | Reliquary | Local faith marks |
| Lab | Clean rooms, glove boxes, cold store | Sample vault | Hazard |
| Yard | Bays, tool cribs, drafting room | Hull in a cradle | Hazard and crane marks |

---

## 8. Data format

Plain text blocks, in the same spirit as `orbit.txt` and the world theme records. Read at startup, with unknown names skipped with a warning, so families can be added a theme at a time.

An orbital theme:

```
orbit_theme last_boarding
  name The Last Boarding
  group exodus
  rarity common
  start yes
  pre M H
  post L M
  ports A B C
  from 1 91 Z44 Z48
  orbit_type ruin_crown 3 | scatter 1
  place sync 4 | low 1 | mid 1
  role passenger_port 5 | freight_port 2 | customs 1
  family orbital_highport 3 | truss_station 3 | derelict_freighter 2
  size M 2 | L 3 | XL 1
  fate walked_out 2 | evacuated 4 | breached 1 | plague 1 | fell_in_part 1
  bodies none 3 | at_posts 1 | in_a_heap 1
  power dead 5 | solar_trickle 2 | long_life 1
  air none 3 | some_rooms 3 | stale 2
  spin still 3 | slow_tumble 2
  occ EMP 40 SCV 20 HSK 10 SQT 10 GNG 10 NST 10
  cargo bags 5 | passengers 3 | food 1
  tags queue exodus luggage
  echo abandoned_spaceport same_thread | processing_hall same_thread | long_queue aim_line
  sky_sight fixed_point
```

A deep theme is the same record with a `part` line:

```
deep_theme misjump_liner
  name The Misjumped Liner
  part passing
  rarity uncommon
  start yes
  place outskirts 3 | gap 1
  motion passer 3 | rails 1
  origin neighbour 5 | far 1
  arrival misjump 5 | fold_spill 1
  era golden 4 | last_years 3 | recovery 1
  family drifting_hulk 3 | spin_ring 1
  role passenger_port 3 | home 1
  fate power_failed 2 | ran_out_of_air 3 | plague 1
  occ EMP 45 HSK 25 NST 15 SCV 15
  lead_out origin_chart
```

A family:

```
space_family spin_ring
  size M L
  parts ring_section*8-16 spoke*2-4 hub counterweight?
  attach solar_wing radiator dish docked_craft
  needs tech H
  place low mid sync high moon_low gap
  program_slots 8-16
  far ring_impostor
```

Layer tables are shared records, so a theme lists only what it wants to change:

```
layer fate
  walked_out   damage none        bodies none      who suits_racked      state abandoned
  breached     damage breach      bodies at_posts  who boats_here        state ruined
  plague       damage none        bodies in_bunks  who boats_gone        state infested
  mothballed  damage none        bodies none      who boats_gone        state never_opened

shell_rule skim
  air_world max_years_adrift 30 unless power_live
```

A site as stored after rolling. It's one line of seed plus any changes, so the save stays small:

```
site 0x8fa1c3 parent world_3 place sync theme last_boarding break later seed 0x12ab
```

Extends `StarSystem`:

```
Orbiter.Kind = Site
Orbiter.Site = record above
Site.Stand = parent, bearing, height            // every orbit POI: no period
Site.Path = None | Ellipse | Hyperbola | Waypoints | Decay   // deep movers and the faller only
```

The link between two stars is no data at all: it's the `Mix.Of` hash in section 3.

Rules for the loader, as for world themes: theme tables are shifted by the apocalypse's `occ_shift`, then normalised. Family weights are multiplied by what the tech filter allows, and zero drops it. Echo lines add sites, they never reroll.

---

## 9. Reward and progression fit

The ladder from the design and from `StarSystem`: fuel moon (warp fuel and warp drive materials), then warp drive, then hyperdrive materials and fuel and capital ship materials, then the stars. Deposits are guaranteed as the code has them. Space POIs add routes, never replace them.

| Stage | Where | What POIs give | Example themes | Cap |
| --- | --- | --- | --- | --- |
| First flight | Low shell of the start world | Scrap, salvage grade, air cells, patch kits, repair kits, first claimable small hull | `holding_pattern`, `jettison_trail`, `lifeboat_raft`, `graveyard_shell`, `national_capsule` | No drives |
| Fuel moon | The moon, its shells and its L points | Warp cells, a few warp parts, ore stocks, a catcher to claim | `catcher`, `fuel_stop`, `lost_property`, `quarried_moon` | Warp drive parts, never a whole drive |
| Warp | The rest of the start system | Hyperdrive materials and fuel in stock form, gravium, gold, fold crystal in small amounts | `scoop_rig`, `fuel_stop`, `company_rock`, `trojan_warehouse`, `unfinished_keel` | Capital hull with no core, no drive, no fuel |
| Capital | First capital hull, claimed or built | Claim targets, components, CPU parts | `alert_fleet`, `unfinished_keel`, `rush_ark`, `founder_carrier`, `breakers_orbit` | Claimed hulls need your own core and your own hyperdrive |
| Stars | Neighbouring systems | Emberite, starsilver, faction goods, lattice cores | `sun_tap`, `star_bridge`, `black_yard`, `continuity_vault` | By danger |
| Frontier | Core, voids, periphery | Precursor alloy, echo shards, unique items | Part 4 | Finite: see below |

**Captured structures must not let a player skip stages.** So a wreck in a start system has its drive room burnt or removed, and a claim hull needs the player's own devices. Zone distance sets the `claim_cap` on what a wreck can hold.

**Nothing restocks.** What's looted or destroyed stays that way, which replaces the design's older regrowth lines for POIs. Unique items stay reachable because there are many sites and rare traders sell them at a premium. The only exception is a maintenance sweep of a long-unvisited planet or system.

**Every deposit has a second way in.** A player who can't find fold crystal in the belt can find it at a scar, buy it at a station, or salvage it. Salvage never replaces the guarantee, it adds to it.

**Cyber modules.** Hard to get but not late game: difficulty comes from danger, hidden places and tricky access, not from level. So:

- Every rare or exotic site has a chance: rare 8%, exotic 25%. Themes marked with a cyber module chance in their entries double it.
- A module sits at a keystone: a sealed vault, a locked lab, the bottom of a dangerous hulk.
- A soft guarantee: each player-reachable region of about 8 systems has a module within reach of the warp stage, placed by adding a keystone to an eligible site, so a skilled player can have their first by the second or third system, or inside the start system with luck.
- At least one eligible site in each start system, inside the danger cap, but off the map and without any lead. Finding it is the test.
- Which module: picked by the site's role and era, so a Vharn wreck gives Vharn gear, and a lab gives odd science. Nothing here sets the module list itself: that's the weapons doc and the tree work.

**Other reward types that aren't loot.**

| Type | Sources | Example |
| --- | --- | --- |
| Map reveal | Survey sites | `survey_globe` shows old cities on the world map |
| Lead | Charts, plates, lines of bodies, dishes | `great_eye`, `old_probe`, `the_walkers` |
| Claim | Hulls and stations | `decreed_orbit`, `set_table` |
| Service | Live sites | `mercy_ship`, `patch_town`, `wandering_dock` |
| World change | Rare, deliberate | `sun_shade`, `weather_platform`, `falling_star` |
| Decor unlocks | Odd places | `circus_ship`, `film_lot`, `sky_hotel` |
| Survey data | Anomalies and first discoveries | `untouched_sky`, `nebula_garden` |
| Trade | Traders | `free_port`, `trader_anchorage` |

**First visits.** Each theme and family counts for first-discovery XP on the design's rules. A theme's XP scales with rarity.

---

## 10. The first 12 to build

Chosen to use what exists: `Derelicts.cs` layouts, `Cells`, `OrbitPlacement`, the loot grades, core claiming, wreck salvage, the kits, and the system model. Together they cover low to high tech, empty to live, and they prove the layers. All 12 sit in the start system or a neighbouring system, so nothing needs a new travel system except warp, where noted.

1. **Holding Pattern (2).** A string of today's `Ship()` hulls, a shared thread and a line placement. Cheapest proof that placement can tell a story, and it makes the first orbit visit a walk along a queue. Pairs with the Long Queue on the ground.
2. **The Jettison Trail (12).** `Field()` with deco objects instead of hull clumps. Proves `flotsam_field` and instancing, and gives a line pointing somewhere.
3. **The Raft (3).** `Pods()` plus rope lines between pods. Tests the nothing-floats rule for space, and it's a lot of character for almost no code.
4. **The Graveyard Shell (45).** Rows of `relay_sat`. Proves shells and the fixed line of sight from the ground. Also the first small grid wreck.
5. **The Last Boarding (1).** `Station()` plus a docking arm grammar. The first thread and echo link with the ground: same bus numbers. The anchor theme for exodus worlds.
6. **The Waiting Berths (13).** `Station()` and `Ship()` in a ring. Uses the same family with a new role, and it proves that role changes rooms. Medical pictograms and HSK occupancy.
7. **The Frozen Battle (25).** `military_wreck` pairs joined by a tube. Shows `bodies` and `who_left_how` telling a story with no text.
8. **The National Capsule (82).** A mid-tech kit and `can_station`. Proves that tech reads at a glance, which the rest of the design depends on.
9. **The Founders' Carrier (80).** Bigger `Hulk()` with bays. The low-tech world's answer, and the first real thing in a low-tech sky. Proves `high` shell and the sky sight.
10. **Lost Property (D4).** The first site at a stable point. Needs a place at ±60°, and a field from every era. Costs little, and tells the player that the system's history is real.
11. **The Company Rock (D2).** `mining_outpost` with rock interiors, in a belt field. Ties capital materials to a place, and it's the first `hollow_rock`. Needs warp.
12. **The Spill (D83).** One passer from the system's visitor list, with an origin from the catalogue and the pair hash. Proves the galaxy link with a cheap comic object before any big ship is tried.

**Shared work, in order.** Orbit sites as fixed records on `StarSystem`, keeping today's bearing and height placement. Layer tables and the site roll, replacing the shares in `OrbitPlacement`. Thread and echo seeds. The damage pass, moved out of each layout. A far model per site. Then the moving path kinds, for deep sites only.

**Close behind.** The Ring Camp (60), Fuel Stop (59), The Unfinished Keel (55), Second Space Age (83), The Falling Station (44), The Set Table (106), The Cloud Town (125).

**Replacing what's there.** `orbit.txt`'s 16 sites become families and roles under this scheme, without losing anything: `orbital_highport` and `orbital_yards` keep their port rule, and each `space` block's `pitch` line becomes a default role. Nothing there needs to be thrown away.

---

## 11. Sources

Used for concepts and roles only. No names, text or designs were taken from any of these. Pages were read as data. Some links are search excerpts rather than full reads: the Empyrion changelogs, Rebel Galaxy, No Man's Sky, `Space_tether`, `Orbital_ring` and the mass driver paper.

**GalHack, read-only.** `docs/DESIGN.md` (rev 632), `docs/world-themes.md`, `docs/solar-systems.md`, `docs/sci-fi-inspirations.md`, `docs/weapon-ideas.md`, `docs/decided-vs-built.md`, `docs/playtest-list.md`. Code in `G:\GalHack-wt\batch`: `prototypes/slice/src/Orbit/Derelicts.cs`, `prototypes/slice/data/orbit.json`, `prototypes/poi-generator/data/orbit.txt`, `prototypes/poi-generator/src/Gen/Orbit.cs`, `prototypes/planet-surface/src/Sky/StarSystem.cs`, `shared/Galaxy/Core/Galaxy.cs` and `GalaxyGen.cs`.

**Empyrion, local files, read-only, as the owner allows.** The list of space playfields and the blueprint names in `Content/Playfields` and `Content/Prefabs`, and `_PoiGroupsConfig.yaml` in the Default Random scenario. Only the idea was taken: orbit sites come in groups, faction by faction, with many small varieties of satellite, beacon, wreck and station, and some empty results. No names or data were copied.

**What the games taught.**

- Empyrion and Reforged Eden: wreck tiers by orbit, booby-trapped wrecks, short detection ranges on small wrecks, fewer dangers in starter orbits. https://steamcommunity.com/sharedfiles/filedetails/changelog/3143225812 and https://steamcommunity.com/sharedfiles/filedetails/changelog/2550354956?p=3
- Empyrion orbit distribution and star preference (forum and devtracker excerpts): https://steamcommunity.com/sharedfiles/filedetails/changelog/1874315400 and https://devtrackers.gg/empyrion/p/fc8ac93d-v1-13-release
- Elite Dangerous: a signal list with threat levels, generation ships as derelicts with stories, one living one found by following a clue in the news, which became a community legend. https://elite-dangerous.fandom.com/wiki/System_Signal_Sources, https://kotaku.com/elite-dangerous-players-discover-ancient-ship-full-of-1839650644, https://massivelyop.com/?p=253463
- Starsector: derelicts of an older age in graded sizes, probes and survey ships that hint at others, a debris fallback when you can't salvage, guarded megastructures that can be repaired. https://starsector.wiki.gg/wiki/Salvage, https://starsector.wiki.gg/wiki/Explorarium, https://starsector.wiki.gg/wiki/Exploration, https://starsector.wiki.gg/wiki/Coronal_Hypershunt
- No Man's Sky: procedural derelict interiors with a found story, infestation as a state, encounters during travel. https://www.nomanssky.com/desolation-update/, https://nomanssky.fandom.com/wiki/Space_Encounter
- Stellaris: ruined versions of megastructures, including art works, and migrating or hidden space fauna. https://stellaris.paradoxwikis.com/Megastructures, https://stellaris.paradoxwikis.com/Space_fauna, https://www.pcgamesn.com/stellaris/megastructures
- Rebel Galaxy: stations with a speciality, distress signals that can be traps. https://www.tvtropes.org/pmwiki/pmwiki.php/VideoGame/RebelGalaxyOutlaw (search excerpt), https://en.wikipedia.org/wiki/Rebel_Galaxy_Outlaw
- Mood: environmental story in a derelict ship (trash, then escalation, used as a route), a station that was falling apart before the threat arrived, used-industrial look. https://www.gamedeveloper.com/design/dead-space---storytelling-through-level-design, https://www.avpcentral.com/sevastopol-station, https://en.wikipedia.org/wiki/System_Shock, https://www.pcgamer.com/uk/why-i-love-the-low-fi-tech-of-alien-isolation, https://propstore.com/the-prop-store-collection/alien/alien-nostromo/

**Tropes.** Generation ships: https://en.wikipedia.org/wiki/Generation_ship and https://projecthyperion.org/. Sleeper ships: https://en.wikipedia.org/wiki/Sleeper_ship. Megastructures: https://en.wikipedia.org/wiki/Megastructure. Ghost ships, real cases and causes: https://en.wikipedia.org/wiki/Ghost_ship. Reserve fleets: https://en.wikipedia.org/wiki/Mothball_fleet. Shipbreaking: https://en.wikipedia.org/wiki/Ship_breaking. Quarantine signals: https://en.wikipedia.org/wiki/Pratique. TV Tropes pages refused a direct fetch, so I used the pages above for those concepts.

**Orbital engineering.**

- Lagrange points, stability and what collects: https://en.wikipedia.org/wiki/Lagrange_point
- Co-orbital motion: https://en.wikipedia.org/wiki/Horseshoe_orbit
- Dust at stable points: https://en.wikipedia.org/wiki/Kordylewski_cloud
- Orbital decay and lifetimes: https://en.wikipedia.org/wiki/Orbital_decay
- Graveyard orbits: https://en.wikipedia.org/wiki/Graveyard_orbit
- Disposal target zones: https://en.wikipedia.org/wiki/Spacecraft_cemetery
- The sync ring and its perturbations: https://en.wikipedia.org/wiki/Geostationary_ring
- Debris, sandblasting and impact speeds: https://en.wikipedia.org/wiki/Space_debris
- Debris cascades and how few dead satellites got raised (secondary sources): https://nautil.us/some-dead-satellites-refuse-to-go-quietly-to-their-graves-234651
- Elevators: https://en.wikipedia.org/wiki/Space_elevator
- Skyhooks and rotating tethers: https://en.wikipedia.org/wiki/Skyhook_(structure), https://en.wikipedia.org/wiki/Space_tether
- Orbital rings: https://en.wikipedia.org/wiki/Orbital_ring
- Mass drivers and catchers: https://space.nss.org/settlement/nasa/spaceres/III-3.html
- Cyclers: https://en.wikipedia.org/wiki/Cycler
- Statites: https://en.wikipedia.org/wiki/Statite
- Orbital mirrors: https://en.wikipedia.org/wiki/Znamya_(satellite)
- Radiation belts: https://en.wikipedia.org/wiki/Van_Allen_radiation_belt
- Spin gravity: https://en.wikipedia.org/wiki/Rotating_wheel_space_station
- Station modules: https://en.wikipedia.org/wiki/Space_station
- Pulse-driven hulls: https://en.wikipedia.org/wiki/Project_Orion_(nuclear_propulsion)
- Interstellar objects: https://en.wikipedia.org/wiki/Interstellar_object

**From memory, not checked on a page.** Dead objects at synchronous height collecting at two stable longitudes. Space reactors parked in high disposal orbits.

**Second research pass.** Waste warnings: https://en.wikipedia.org/wiki/Long-term_nuclear_waste_warning_messages. Automatic retaliation: https://en.wikipedia.org/wiki/Dead_Hand. Numbers stations: https://en.wikipedia.org/wiki/Numbers_station. Ice ships: https://en.wikipedia.org/wiki/Project_Habakkuk. Floating habitats in cloud tops: https://en.wikipedia.org/wiki/Colonization_of_Venus. Blockships: https://en.wikipedia.org/wiki/Blockship. Tomb armies: https://en.wikipedia.org/wiki/Terracotta_Army. Slow clocks: https://en.wikipedia.org/wiki/Clock_of_the_Long_Now. Underground fires: https://en.wikipedia.org/wiki/Centralia,_Pennsylvania. Closed cities: https://en.wikipedia.org/wiki/Closed_city. Sound mirrors: https://en.wikipedia.org/wiki/Acoustic_mirror. Why towns were abandoned: https://en.wikipedia.org/wiki/List_of_ghost_towns_by_country. Sea forts and later reuse: https://en.wikipedia.org/wiki/Sea_fort and a search excerpt at https://mossandfog.com/abandoned-places-part-1/. Ships sunk on purpose: https://en.wikipedia.org/wiki/Sunken_ship. Long-term messages: https://en.wikipedia.org/wiki/Time_capsule. Space memorials: https://en.wikipedia.org/wiki/Space_burial. Derelict backstories and hazards: https://starfieldwiki.net/wiki/Starfield:Random_POI. Fields that kill electronics (search excerpt): https://everspace.fandom.com/wiki/Union_Outer_Rim. Chop shops (search excerpt): https://starfield.fandom.com/wiki/Chop_Shop. From memory, not checked on a page: animal test flights, flooded-wall radiation shields, carved-salt chapels, boneyards, bone chapels, stepwells, polar crater shadow, under-ice bases, whaling stations, lived-on bridges, and a town planted in the wrong climate beyond the excerpt above.

---

## 12. Owner's decisions

These override anything above.

1. **Site movement follows Empyrion.** Orbit POIs stay fixed in their orbit zone, and don't circle the world. The period formula in the moving POIs note is dropped for them. Worlds and moons still move on rails.
2. **No sleepers wake.** Cryo pods hold corpses. The only pods that ever wake are in the player's starting room. Read every Sleepers (SLP) entry as dead occupants, with no awakening.
3. **No spin gravity.** Rings and spinning hulls can turn as a look, but gravity is the same core gravity as everywhere else, and it's off when the power's dead.
4. **POIs don't replenish.** What's looted or destroyed stays that way, and changes to a world last. The only exception is a maintenance sweep of a planet or system that's long unvisited and far behind.
5. **No paid passage for now.** Hitching needs other systems you can arrive in, and hyperdrive travel, so it waits. Read `route_keeper` and similar as places, with no ride.
6. **Start system traders** are factionless independents (`trader_anchorage`).

---

## Ground POI families to add

Found in the second research pass, and held here because `docs/world-themes.md` is in use elsewhere. Copy them over when that's free. None of these names is in the family lists in `world-themes.md` or `poi-generator/data`. Each is a family with several layouts, in tech variants, built from blocks and rolled by the usual layers, and each tells its story with places, signs and sound only.

**G1. Stage Drop Zone** (`stage_drop_zone`) - Under the launch corridor, the sky falls on a schedule.
- Family: A long strip of steppe or tundra littered with spent rocket stages, downrange of a launch site. Villages live off them.
- Inside: Roofs and fences made from stage skins, a scrap weighbridge, a dented tank used as a cistern.
- Tech: L, sheds of hammered stage metal. M, stages with flags. H, drop pods and lander legs.
- Fit: Near `launch_rail` and `abandoned_spaceport`. Themes 1, 2, 70, 93. Orbit echo: spent stages, `national_capsule`.

**G2. Disposal Ground** (`disposal_ground`) - Where the sky was told to fall.
- Family: A remote sea or desert where old stations were aimed on purpose. Rows of burnt hulks, ordered by date.
- Inside: A few walkable sections, a survey hut, a warning buoy line.
- Tech: M, small capsules. H, station sections.
- Fit: Echo of `the_undertaker`. Themes 28, 33, 105.

**G3. Warning Field** (`warning_field`) - Thorns meant for anyone, for ever.
- Family: A plain of huge leaning spikes, or black blocks in streets that lead nowhere, over a sealed store.
- Inside: A descending ramp, pictograms growing plainer, a final sealed hall.
- Tech: M, concrete. H, dark composite. Moaning wind in hollow spires where there's air.
- Fit: Themes 24, 103, Z1, Z33. Space echo: `keep_out_rock`.

**G4. Warning Stone** (`warning_stone`) - Don't build below here.
- Family: Old stones set up the hill after a great wave or flood, carved with a picture and a line. Towns have since crept below them.
- Inside: None: a landmark, and a marked line on the land.
- Tech: L, carved stone. M, cast concrete.
- Fit: Themes 38, 42, 68, 100, Z21, Z25. Reads with the real flood height of that world.

**G5. Sound Mirror** (`sound_mirror`) - A concrete ear.
- Family: Great curved concrete walls and dishes on a coast, built to hear attackers before radar.
- Inside: A listening trench, a seat at the focus, a small plotting room. Standing at the focus carries far sounds in, as wordless audio.
- Tech: L to M.
- Fit: Themes 10, 23, 25, 54.

**G6. Radar Wall** (`radar_wall`) - A steel cliff in a forest.
- Family: A vast wall of antenna mast, kilometres long, with a closed support town.
- Inside: Transmitter halls, cable tunnels, a town at the foot.
- Tech: M to H.
- Fit: Themes 24, 29, 85. Pairs with `closed_city`.

**G7. Closed City** (`closed_city`) - Not on the map.
- Family: A secret town behind a fence and watchtowers, with better shops than anywhere else, and a borrowed name.
- Inside: A checkpoint, neat flats, well-stocked stores, a guarded plant at the centre.
- Tech: M, reactor or weapons town. H, a drive or space works.
- Fit: Themes 8, 24, 29, Z33, Z45. Hidden from the map until reached.

**G8. Company Island** (`company_island`) - Tower blocks on a rock, nothing else.
- Family: A tiny island walled in concrete and packed with flats over a mine, emptied in one day when it closed.
- Inside: Dark stairs, shared kitchens, a school, a shrine, a shaft head in the middle.
- Tech: M. H as a sealed stack on an airless rock.
- Fit: Themes 9, 37, 106, Z46.

**G9. Fenced Quarter** (`fenced_quarter`) - A resort behind wire, left as it was.
- Family: A rich district sealed by an army at a border, then forgotten, with shops still dressed and cars still parked.
- Inside: Hotels, showrooms with new stock, guard posts, a fence with signs.
- Tech: M to H.
- Fit: Themes 7, 26, 46, Z4, Z45.

**G10. Transplanted Town** (`transplanted_town`) - Lawns in the jungle.
- Family: A company built a perfect town from somewhere else in the wrong climate, with fire hydrants, picket fences and a golf course, and it failed.
- Inside: Identical houses, a dead plantation, a hospital, a social hall.
- Tech: L to M.
- Fit: Themes 12, 41, 77, Z30. Any biome that clashes with the town's home.

**G11. Sand-Filled Town** (`sand_filled_town`) - The dunes came indoors.
- Family: A rich small mining town whose rooms are half full of drifted sand, up to the door tops.
- Inside: Sloping sand floors, wallpaper over a dune, a theatre, a bath hall.
- Tech: L to M.
- Fit: Themes 33, 44, 72, Z20. Works for ash and snow with the same layout.

**G12. Dam Never Built** (`dam_never_built`) - Moved out for water that never came.
- Family: A valley cleared for a reservoir: houses stripped, church unroofed, graves moved, and no dam beyond a concrete stub.
- Inside: Tag marks on every door, empty foundations, a planning hut with a valley model.
- Tech: L to H.
- Fit: Themes 32, 84, Z46, Z48.

**G13. Sea Fort** (`sea_fort`) - Gun towers on legs.
- Family: Clusters of steel boxes on stilts in shallow sea, joined by catwalks. Often later taken by squatters or pirates.
- Inside: Gun decks, bunk rooms, a radio room, rusted walkways.
- Tech: M. L, as a stone tide fort.
- Fit: Themes 38, 85, 101, 106. Orbit echo: `sky_micronation`.

**G14. Stilt Oil City** (`stilt_oil_city`) - Streets out at sea.
- Family: Not one rig but a town of piers, flats and roads built over open water, far from the shore.
- Inside: Road decks, tower blocks on piles, a bakery, collapsing spurs.
- Tech: M.
- Fit: Themes 81, 106, 42. Larger and stranger than `offshore_rig`.

**G15. Salt Chapel** (`salt_chapel`) - A cathedral dug by miners.
- Family: A deep mine with chapels and chandeliers carved from the salt itself.
- Inside: Carved halls, brine lakes, carved statues, a long stair.
- Tech: L to M.
- Fit: Themes 33, 60, 96. Works in ice or crystal mines by material.

**G16. Rendering Shore** (`rendering_shore`) - Tanks and bones on a cold beach.
- Family: A station for cutting up huge sea or land creatures: flensing slip, boiling tanks, a whole bay of bones.
- Inside: Rusting vats, a cooper's shop, a graveyard, a small chapel.
- Tech: L to M. H as an automated plant.
- Fit: Themes 47, 50, 77. Space echo: `whaler_ship`.

**G17. Flyer Boneyard** (`flyer_boneyard`) - Rows to the horizon.
- Family: A dry plain of parked airliners, wrapped and fading, some stripped.
- Inside: A few walkable cabins, a parts shed, a survey tower.
- Tech: M to H, with grav craft on H worlds.
- Fit: Themes 28, 66, 44, Z44. Dry climates only.

**G18. Fallen Hangar** (`fallen_hangar`) - The roof came down on the last spaceplane.
- Family: A giant assembly hall with a collapsed roof, a nearly whole orbiter under the girders.
- Inside: Scaffold towers, tool stores, a crew room, the craft itself as a walkable dungeon.
- Tech: M to H.
- Fit: Near `abandoned_spaceport`. Themes 1, 66, Z46.

**G19. Isolation Island** (`isolation_island`) - Over the water, and no way back.
- Family: A small island town for those sent away from sickness, with its own church, shop and graveyard, and one landing.
- Inside: Family houses, a ward, a one-way ferry stage, a bread hatch.
- Tech: L to M.
- Fit: Themes 11, 18, 20, 68, Z11. Space echo: `waiting_berths`.

**G20. Bone Chapel** (`bone_chapel`) - A plague's dead, arranged.
- Family: A small church inside, its walls, lamps and arches made of bones, after a great dying.
- Inside: Patterned walls, a chandelier, a crypt below. Bones are deco, and the patterns differ by culture.
- Tech: L.
- Fit: Themes 18, 95, 96, Z11.

**G21. Lived-On Bridge** (`lived_on_bridge`) - A street across the river.
- Family: A long bridge built over with houses, shops and a chapel. Often the only crossing, and so a fort.
- Inside: Shops facing a narrow road, cellars in the piers, a gatehouse at each end.
- Tech: L to M.
- Fit: Themes 23, 36, 94.

**G22. First Landing Site** (`first_landing_site`) - The footprints are roped off.
- Family: On a vacuum moon, the place someone first landed: lander base, flag, tools, tracks, and a later visitors' path round it.
- Inside: A heritage hut, rope lines, a lander to enter. Tracks never fade in vacuum.
- Tech: M, the original. H, a later museum dome round it.
- Fit: Airless moons, with `national_capsule` overhead. Theme 24.

**G23. Dark Crater Station** (`dark_crater_station`) - Where the sun never reaches.
- Family: A base on the floor of a polar crater, in permanent shadow, mining old ice, with a mirror on the rim bringing light down.
- Inside: A cold-soaked base, an ice pit, a long tunnel to the sunlit rim.
- Tech: M to H.
- Fit: Airless moons and small worlds. Pairs with the fuel moon's warp fuel deposits.

**G24. Under-Ice City** (`under_ice_city`) - A town in a trench of snow.
- Family: A secret base of cut ice trenches roofed with arches, its huts inside, slowly crushing shut.
- Inside: Ice streets, a small reactor room, bent ceilings, huts tilting.
- Tech: M to H.
- Fit: Themes 34, 67, 109. Layout shrinks with `years_adrift` as the ice closes.

**G25. Stepwell** (`stepwell`) - A palace going down.
- Family: A huge carved pit of stairs and galleries reaching groundwater, cool in the worst heat.
- Inside: Terraces of shade, carved columns, a water floor. Usually dry now.
- Tech: L.
- Fit: Themes 32, 33, 71, Z20.
