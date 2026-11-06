# World themes

A theme is a seeded flavour that sits on top of a world profile (pre-collapse tech, post-collapse tech, starport class, main trade). It does not replace the terrain generator. It biases it, and it decides what the region planner builds, which POI families dominate, what is inside them, and what lives there. Every world rolls one primary theme, and about one world in four rolls a secondary theme in one region (a "mixed" world). A theme only changes odds and contents, so any theme can appear on any suitable terrain.

How to read an entry:

- **Happened**: the one-sentence cause, shown only through what the player finds, never told.
- **Land/sky**: landform and biome bias, weather, palette.
- **Regions/infra**: what the region planner lays down; road, rail, power and wreck character.
- **POIs**: `top` are existing families that should dominate. `NEW` are families that do not exist yet (snake_case, counted in the last section). `Inside` is what the rooms hold and the story they tell.
- **Life/people**: wildlife, Hollow presence, survivors, factions.
- **Hazards/rewards**: what hurts and what pays.
- **Occupancy**: who or what is in the places. It is its own layer, separate from layout, with its own section and a per-theme table near the end (shares, including how often places are completely empty).
- **Fit**: pre-collapse tech band, post-collapse tech band, starport classes, rarity (C common, U uncommon, R rare, X exotic), and whether it can be a start world (S = yes, rim and survivable; no = not a start world).

Tech band letters: L low, M mid, H high. "Pre M > post L" means a modern-day world whose survivors now live at a low level.

Faction note: Hollow presence is always "felt, not shown": sounds, stains, misplaced objects. It rises toward the core. A theme with no faction line simply has none on most rolls.

---

## A. Exodus and abandonment

Worlds where people left, or tried to. The evidence is paperwork, queues and half-packed lives.

**1. The Long Queue** (`long_queue`) - Every road ends at the same fence.
- Happened: A whole population tried to leave through one port. The ships stopped coming before the queue did.
- Land/sky: Plains and rolling hills, mild weather, long gold evenings; dust and faded paint.
- Regions/infra: One huge port region at the centre; every highway runs to it, jammed nose to tail for tens of kilometres with cars, buses and trailers. Many scrappy improvised pads ring it. Power lines run to the port only.
- POIs: top: abandoned_spaceport, evacuation_camp, scrappy_port, landing_clearing, crashed_freighter. NEW: processing_hall, luggage_field. Inside: ticket booths with numbered slips, a hall of unclaimed suitcases, a family's picnic still laid on a bonnet, a lottery drum with the last names drawn.
- Life/people: Grazers in the verges, scavenger packs on the roads, a few hermits living in the queue itself. Hollow: very faint, a silence that settles over the last kilometre.
- Hazards/rewards: Little danger on the road; the port is held by gangs or sentries. Rewards: clothes, food tins, passenger manifests, whole ships on the pads.
- Fit: pre M/H > post L | ports A-C | C | S

**2. Lifeboat World** (`lifeboat`) - Hundreds of tiny ships, all pointed at the sky.
- Happened: The port was too small, so everyone built launch rails and flung improvised craft upward. Few reached orbit.
- Land/sky: Coastal cliffs and flat headlands, grey sea sky, salt wind; rust orange and white.
- Regions/infra: Dozens of makeshift launch fields strung along a coast, linked by cinder roads; no railway. Crashed hulls litter the sea approach and the fields downrange.
- POIs: top: scrappy_port, landing_clearing, crashed_freighter, evacuation_camp. NEW: launch_rail, wreck_beach. Inside: hand-welded cabins with names scratched on the bulkhead, launch logs counting down, a field where all the rockets fell short in a neat arc.
- Life/people: Shore scavengers, sea birds nesting in hulls, Reaver salvage crews working the beach.
- Hazards/rewards: Unstable half-fuelled wrecks that can burn; cheap engines, thrusters, tanks.
- Fit: pre M > post L | ports D-E, 0 | U | S

**3. The Last Train** (`last_train`) - A rail world; every line has a stopped train on it.
- Happened: Rail was the only way out for a landlocked people; trains were stopped by something at the end of the line.
- Land/sky: Long valleys, tunnels through ridges, viaducts; overcast, soot grey and signal red.
- Regions/infra: A dense rail web with stations, sidings and rail tunnels; trains standing in rows, some derailed on viaducts. Roads are minor and decayed. Power lines follow the rails.
- POIs: top: rail_yard, coal_plant, steelworks, old_mine, substation. NEW: rail_wreck, terminus_station, signal_box_keep. Inside: carriages full of luggage and sleeping bunks, a dining car with the soup still in bowls, a signalman's log that ends mid-sentence.
- Life/people: Tunnel dwellers, rail-gang scavengers on handcars, lone drivers who never left.
- Hazards/rewards: Dark tunnels with ambushers; locomotive parts, rail guns, cargo.
- Fit: pre L/M > post L | ports C-E | C | S

**4. Empty Ark** (`empty_ark`) - A whole colony ship stands on the ground, doors open.
- Happened: The founders' colony ship landed whole, the colony thrived for a century, then everyone walked out one day.
- Land/sky: Gentle green plains with a vast hull on the horizon; bright, clear air.
- Regions/infra: A ring of settlements around the hull, tidy grid roads, farms; power from the ship. Little wreckage; deliberate order, as if people left by plan.
- POIs: top: old_farm, survivor_hamlet, exchange_depot, research_lab. NEW: colony_ship_wreck, ark_dome, seed_vault. Inside: cabins with their original manifests, a crew deck frozen mid-handover, a seed vault emptied and relabelled in a different hand.
- Life/people: Tame-seeming herds, wary survivors who claim the ship is theirs; Hollow visible only as a quiet corridor that no one will walk.
- Hazards/rewards: Few hazards early; high-tech salvage in the hull, a good start world for the curious.
- Fit: pre H > post M/L | ports A-C | R | S

**5. Hush Suburbs** (`hush_suburbs`) - Rows of houses with the lights left on.
- Happened: People vanished overnight; no bodies, no breakage, no panic.
- Land/sky: Rolling green hills, lawns gone to meadow, clear skies; pastel paint under moss.
- Regions/infra: Planned estates of identical houses, cul-de-sacs, malls, a school, a main road to a distant city; power lines intact and some house lights still run on long-life cells.
- POIs: top: meridian_apartments, survivor_hamlet, water_works, hospital. NEW: shopping_mall, school_campus, estate_house. Inside: dinner on tables, TVs on static, children's drawings on fridges, one house with every door locked from inside.
- Life/people: Deer-like grazers on lawns, foxlike scavengers; a single survivor family in one house, unsure why. Hollow: strong sense of absence.
- Hazards/rewards: Almost none, which is the unease; domestic salvage, medicine, kitchens.
- Fit: pre M/H > post L | ports C-D | U | S

**6. Parade Ground** (`parade_ground`) - A festival that never finished.
- Happened: A planet-wide holiday was in full swing at the moment of the collapse.
- Land/sky: Temperate hills and lakes; faded bunting, bright flat light.
- Regions/infra: Town squares with stages, fairgrounds along every road, banners strung between poles; floats stalled in processions on the highways.
- POIs: top: survivor_town, exchange_market, old_airfield. NEW: fairground, theme_park, stage_hall. Inside: costumes on racks, food stalls with fossilised cakes, a stage with a half-set band, a rigged prize wheel.
- Life/people: Stray flocks, bird roosts in the lights; survivors who still hold a smaller festival each year.
- Hazards/rewards: Collapsed rigging, mild; costumes, instruments, odd decor, a good trading town.
- Fit: pre M/H > post L | ports B-D | U | S

**7. Closed for the Season** (`closed_season`) - Resorts, empty and clean.
- Happened: Rich holiday worlds were evacuated first and best; they are almost undamaged.
- Land/sky: Beaches, lagoons, palm-like trees, bright sea; white and turquoise.
- Regions/infra: Island chains and coasts of hotels, marinas and private strips; sleek roads, monorails; little wreckage because everything left by order.
- POIs: top: abandoned_spaceport, docks, hospital, meridian_apartments. NEW: casino_resort, hotel_tower, ferry_terminal, spa_springs. Inside: pool bars with glasses set out, vault rooms in casinos, staff quarters hastily emptied, locked suites with the guests' secrets.
- Life/people: Sea life, resort grazers gone feral, Reaver crews raiding hotels, a handful of caretakers.
- Hazards/rewards: Armed security machines still running; luxury goods, rare vintages, fuel.
- Fit: pre H > post L/M | ports A-B | R | S

**8. The Deserted Capital** (`deserted_capital`) - A city for millions, no one home.
- Happened: The administrative world's government left first and left orders; the bureaucracy kept working for decades.
- Land/sky: A vast flat built-up basin, haze, monumental avenues; grey stone and brass.
- Regions/infra: Concentric ring roads, a central government district, rail and skyway rings, power running dimly on automated plants.
- POIs: top: ominous_tower, meridian_apartments, command_bunker, telecom_exchange, prison_block. NEW: ministry_hall, data_centre, archive_vault. Inside: ranked filing rooms, orders stamped "evacuate" and "do not evacuate" in rival stacks, an empty council chamber with a vote half taken.
- Life/people: Rats and carrion fliers; faction patrols of Meridian remnants; sentry machines still guarding.
- Hazards/rewards: Automated defences; data, archives, high-level keys.
- Fit: pre H > post M/L | ports A-B | R | no

**9. Mining Lock-In** (`lockin`) - Every shaft sealed from outside.
- Happened: A company sealed its mine towns to save the ore; the miners were never let out.
- Land/sky: Barren hills, tailings, yellow-brown skies, cold; ochre and slag.
- Regions/infra: Company towns around shaft heads, ore railways, long conveyors, fences and gates facing inward; no roads out.
- POIs: top: old_mine, abandoned_mine, steelworks, rail_yard, prison_block. NEW: company_town, ore_sorting_hall. Inside: pay tickets with debts, union flyers, barricaded shafts with scratch-marks, a manager's office with a ledger of "reserves".
- Life/people: Tunnel scavengers; former miners' descendants; Exchange buyers who ask no questions.
- Hazards/rewards: Cave-ins, bad air; abundant metals, dormant machinery.
- Fit: pre M > post L | ports D-E | C | S

**10. The Walked-Out Garrison** (`walked_out`) - An army base with no army.
- Happened: A military world's units were called away to the front; the front closed behind them.
- Land/sky: Dry steppe and scrub, wide skies, hard light.
- Regions/infra: Neat base towns around parade squares, military roads, airfields, fenced depots, power to the bases only; vehicles parked in lines, fuelled and waiting.
- POIs: top: military_base, old_airfield, command_bunker, deep_bunker, vharn_sam_site. NEW: motor_pool, barracks_town. Inside: weapon racks locked, mess halls set for hundreds, orders on a desk "hold until relieved", a roll-call board where every name has been ticked.
- Life/people: Predators drawn to stores, squads of Reavers fighting over armouries, Meridian remnants.
- Hazards/rewards: Armed auto-turrets; weapons, armour, vehicles in working order.
- Fit: pre M/H > post L/M | ports B-D | U | no

---

## B. Plague, bloom and bio

**11. Quarantine** (`quarantine`) - A planet walled in, from the outside.
- Happened: A virus was real, and the outside world sealed the walls to keep it in.
- Land/sky: Pale plains, dull overcast, cold colour; yellow tape and white tents.
- Regions/infra: Long walls and checkpoint lines slicing the land into zones; roads end at gates; ghost trucks queued at the barriers.
- POIs: top: hospital, biolab, evacuation_camp, cryo_vault. NEW: quarantine_wall, sorting_gate, burial_pits. Inside: triage tents, rows of cots, discharge slips for the dead, a lab that cured it and did not announce it.
- Life/people: Few animals; masked hermits; Hollow stains inside the zones nearest the core side.
- Hazards/rewards: Hostile sentries on walls; medical gear and a hint of a cure.
- Fit: pre M/H > post L | ports C-E | U | S

**12. Fever Jungle** (`fever_jungle`) - The forest is the disease.
- Happened: A lab agent escaped into the canopy and turned the forest into a single organism.
- Land/sky: Rainforest, steam, green-gold haze; swollen trunks, fruiting bodies.
- Regions/infra: Roads swallowed; rail lines lifted by roots; overgrown biolabs at the heart.
- POIs: top: biolab, research_base, old_farm, hospital. NEW: bloom_garden, fruiting_hall. Inside: grown-through corridors, sample fridges overgrown with pulsing mats, a researcher's desk fused into a tree.
- Life/people: Mutated climbers, spore swarms, colony ants; hermit botanists.
- Hazards/rewards: Spore clouds, warned mutagen; rare organics and cures.
- Fit: pre M/H > post L | ports D-E, 0 | U | no

**13. Sleepwalkers' World** (`sleepwalkers`) - They are still on their feet.
- Happened: A condition left the population walking their old routines, indefinitely.
- Land/sky: Towns and fields, soft light, mist at dawn.
- Regions/infra: Ordinary towns with smooth upkeep failing; clocks stopped; crops half-tended.
- POIs: top: survivor_hamlet, hospital, old_farm, exchange_market. NEW: sleeper_ward, routine_loop_street. Inside: beds with straps, charts of sleep depth, a diner where figures set the table at the same time each cycle.
- Life/people: Slow, harmless-looking walkers (non-lethal until disturbed in numbers); carers who watch them.
- Hazards/rewards: Crowd panics; calm tech and medicine.
- Fit: pre M > post L | ports C-D | R | no

**14. The Pollen Wars** (`pollen_wars`) - Spring is a siege.
- Happened: Engineered crops escaped and now pollinate everything with sterility.
- Land/sky: Meadows, orchards, yellow clouds, lilac skies.
- Regions/infra: Endless orchards on gridded roads, drone depots; irrigation channels.
- POIs: top: old_farm, grain_elevator, water_works, biolab. NEW: orchard, greenhouse_dome, drone_hive. Inside: seed cabinets, half-built crop drones, a lab fridge of "next year's" seed.
- Life/people: Bees, great moths; farmers behind sealed doors.
- Hazards/rewards: Pollen storms reduce visibility and fog the suit; food, seeds, honey.
- Fit: pre M/H > post L | ports C-D | U | S

**15. Blightland** (`blightland`) - Grey grass, black rivers.
- Happened: A crop blight became general; the soil never recovered.
- Land/sky: Steppe and hill, dust, brown and ash grey.
- Regions/infra: Failed farms in rows, abandoned silos, emergency soup kitchens, ration depots.
- POIs: top: grain_elevator, old_farm, evacuation_camp, survivor_town. NEW: ration_depot, seed_vault. Inside: empty silos, ledgers of famine, a seed vault looted in a hurry.
- Life/people: Scavenger packs, hungry survivors, travelling traders.
- Hazards/rewards: Starving gangs; seeds and rare clean-water springs.
- Fit: pre M > post L | ports D-E | U | S

**16. Drowned by the Bloom** (`bloom_drowned`) - A shimmering growth fills the lowlands.
- Happened: The Hollow Bloom reached a flat plain and filled it like a lake.
- Land/sky: Basin of glowing growth, violet skies; paths of safe stone.
- Regions/infra: Roads dive into the growth and vanish; rail tracks emerge on the far side.
- POIs: top: alien_hive_ruin, research_lab, military_base. NEW: bloom_scar, hollow_shrine. Inside: rooms where the growth presses to the glass, a lab log that asks questions of the growth.
- Life/people: Corrupted local fauna; Hollow strong; survivors who live at the shore and trade through scouts.
- Hazards/rewards: Warned mutagen zone; exotic organics, signals.
- Fit: pre M/H > post L | ports C-E | R | no (rim variants S)

**17. Coughing Hills** (`coughing_hills`) - Dust that never settles.
- Happened: A mill fire released fine toxic dust over one valley system.
- Land/sky: Hills and basins, orange dust, brown fog; sun a pale disc.
- Regions/infra: Industrial valleys with chimneys and dead mills; masks stacked at checkpoints.
- POIs: top: steelworks, refinery, abandoned_factory, old_mine. NEW: dust_mill, mask_depot. Inside: respirator bins, shift logs counting coughs, a mill with its last batch.
- Life/people: Lung-adapted scavengers; shy fauna in high clean air.
- Hazards/rewards: Dust zones need filters; metals.
- Fit: pre L/M > post L | ports D-E | C | S

**18. The Mercy Hospitals** (`mercy`) - Every town has one, and each had a long ward.
- Happened: Healers kept going when everyone else left; their wards ended up a hundred years deep.
- Land/sky: Temperate rolling land; evening fog, warm lamp light.
- Regions/infra: Small hospitals on every hill road; ambulance convoys; helipads.
- POIs: top: hospital, cryo_vault, research_base, survivor_hamlet. NEW: hospice_ward, mortuary_deep. Inside: bedside notes, supply counts, a nurse's roster running to last day, one bed with fresh sheets.
- Life/people: Wary caregivers (survivors), feral dogs; Hollow rare but present in the mortuary levels.
- Hazards/rewards: Low hazard; medical kits, meds, healing booths.
- Fit: pre M/H > post L/M | ports C-E | U | S

**19. Flesh Gardens** (`flesh_gardens`) - A farm that grew the wrong things.
- Happened: A research company cultivated livestock in vats; the vats kept running.
- Land/sky: Flat greenhouse plains, steam, pink-white.
- Regions/infra: Long rows of grow houses on grid roads; conveyor rail; sealed pipelines.
- POIs: top: biolab, abandoned_factory, grain_elevator. NEW: vat_hall, rendering_plant. Inside: vats with half-grown shapes, packing lines, a CEO's menu card.
- Life/people: Escaped vat-stock, scavenging mutants; Hollow-touched.
- Hazards/rewards: Biohazard; organics, medicines.
- Fit: pre H > post L | ports C-D | R | no

**20. The Clinic at the Edge** (`clinic_edge`) - A medical frontier that stayed open.
- Happened: A rim world cared for refugees from the collapse; the staff outlasted the supplies.
- Land/sky: Coastal hills, thin clear air, cool dawns.
- Regions/infra: A ring of aid stations and tent hospitals on the main road; a harbour; carts and old trucks.
- POIs: top: evacuation_camp, hospital, scrappy_port, survivor_hamlet. NEW: aid_station. Inside: triage logs, children's wards, a shelf of unsent letters.
- Life/people: Friendly freeholders; Exchange traders; Meridian medics; low Hollow.
- Hazards/rewards: Calm; heal, trade, intel on the core.
- Fit: pre L/M > post L | ports D-E | U | S

---

## C. War and weapons

**21. Glass Plains** (`glass_plains`) - The whole world is glass.
- Happened: Mass strike fused plains; the gleaming ground is cracked and sharp.
- Land/sky: Flat glassy plains, mirage heat, white skies; refracted rainbow edges.
- Regions/infra: Highways pass on stilts above the glass; broken viaducts; ruins ringed outward from ground zeros.
- POIs: top: blast_crater, command_bunker, deep_bunker, military_base. NEW: shadow_wall. Inside: silhouettes burnt onto walls, shelters with calendar marks, a hospital where nobody turned up.
- Life/people: Sparse; sand-swimmers beneath; gangs on bikes.
- Hazards/rewards: Radiation hot-spots, glare; voidglass.
- Fit: pre M/H > post L | ports C-E | U | no

**22. Rust Armies** (`rust_armies`) - Two machine armies stopped mid-charge.
- Happened: Two fleets of war machines fought to a standstill and shut down.
- Land/sky: Rolling plain, brown grass, long shadows; armour red.
- Regions/infra: Battle lines marked by trench works, tank fields, cratered farms; supply roads, depots.
- POIs: top: military_base, vharn_sam_site, kessari_works, old_airfield, command_bunker. NEW: battlefield_memorial, trench_line, motor_pool. Inside: stalled walkers with cracked cockpits, ammo dumps, a command tent with a map and two flags.
- Life/people: Grazers among hulls; scavenger crews; Kessari clans reactivating.
- Hazards/rewards: Unexploded ordnance, awakened machines; vehicle parts, weapons.
- Fit: pre M/H > post L/M | ports C-D | U | no

**23. Wall Cities** (`wall_cities`) - Every town behind a fortress.
- Happened: Warlords fortified every settlement and sealed them against each other.
- Land/sky: Rolling hills, valleys, castles on hills; stone grey, banners.
- Regions/infra: Towns with towers, walls, gates, tolls; roads patrolled; no rail.
- POIs: top: survivor_town, scrap_fort, meridian_garrison, old_mine. NEW: wall_city, toll_gate, hilltop_keep. Inside: armoury, granaries, a council room with a broken table, torture-free but harsh prison.
- Life/people: Armed townsfolk, mercenary bands, hostile gangs between towns; fine horses-like beasts.
- Hazards/rewards: Raids; fortified bases to claim; trade.
- Fit: pre L/M > post L | ports D-E | U | S

**24. The Missile Fields** (`missile_fields`) - Silos in rows, hatches ajar.
- Happened: Launch happened; half the missiles never rose.
- Land/sky: Grass plains, wind-blown, vast sky.
- Regions/infra: Grids of silo access roads; command bunkers; ranch roads; radio masts.
- POIs: top: command_bunker, deep_bunker, vharn_sam_site, meridian_railgun, military_base. NEW: missile_silo. Inside: launch consoles with blinking keys, crews' bunks, a silo with a live rocket in it.
- Life/people: Roaming herds in silo shade; survivors using hatches as storm cellars.
- Hazards/rewards: Armed hardware; high-grade fuel, electronics.
- Fit: pre M/H > post L | ports D-E | U | S

**25. Trench Moon** (`trench_moon`) - A stalemate carved into the land.
- Happened: A century-old ground war used up a whole generation.
- Land/sky: Mud plains, craters, low clouds; grey and brown.
- Regions/infra: Line after line of trenches, rail spurs, supply depots, field hospitals.
- POIs: top: military_base, hospital, evacuation_camp, command_bunker. NEW: trench_line, battlefield_memorial. Inside: dugouts with letters, boots in rows, a gas shelter.
- Life/people: Scavenger folk, ravens, hostile gangs; Hollow touched in the oldest line.
- Hazards/rewards: Trapped ground, bad air; weapons, relics.
- Fit: pre M > post L | ports D-E | U | S

**26. The Peace Treaty** (`peace_treaty`) - The war ended; no one saw it.
- Happened: Both sides signed and left; the world is built for a handover that never came.
- Land/sky: Neutral highland, tidy, pale light.
- Regions/infra: A line of identical checkpoints and flags across a country; two road networks that do not join.
- POIs: top: meridian_garrison, meridian_relay, exchange_depot. NEW: border_checkpoint, ministry_hall. Inside: banquet tables set for two delegations, signed copies, an empty vault where the weapons were destroyed on schedule.
- Life/people: Odd peace of grazers; lone envoys; faction scouts.
- Hazards/rewards: Calm; trade permits, comms.
- Fit: pre M/H > post L/M | ports C-D | R | S

**27. Siege Orbitals** (`siege_orbitals`) - The sky holds a ring of broken stations.
- Happened: A war in orbit rained its wreckage down.
- Land/sky: Rocky desert, dusk skies full of drifting hulks and meteor scars.
- Regions/infra: Debris fields on the ground in long lines along the old orbit track; crater strings.
- POIs: top: crashed_freighter, blast_crater, abandoned_spaceport. NEW: orbital_wreck_site, debris_camp. Inside: tilted deck corridors, vacuum-sealed crews, cargo of ammo.
- Life/people: Debris diggers; Reaver crews; Vharn patrols in orbit.
- Hazards/rewards: Debris rain, falling remnants; ship parts.
- Fit: pre H > post L/M | ports A-C | U | no

**28. Decommissioned** (`decommissioned`) - Everything armed was taken apart.
- Happened: A disarmament programme dismantled arsenals, cleanly, then the collapse hit.
- Land/sky: Gentle steppe, wind turbines, soft light.
- Regions/infra: Scrapyards of dismantled kit, neat salvage depots, rail spurs.
- POIs: top: abandoned_factory, scrap_fort, military_base, steelworks. NEW: breaker_yard, motor_pool. Inside: gutted tanks in rows, workers' tool lockers, the "last gun" in a display case.
- Life/people: Scrap-picker families, ravens.
- Hazards/rewards: Calm; scrap, wiring, parts.
- Fit: pre M/H > post L | ports C-E | C | S

**29. Sentry World** (`sentry_world`) - Everything is guarded, nobody is home.
- Happened: Automated defences stayed on after their owners died.
- Land/sky: Hard rock plain, bright, dusty; steel blue.
- Regions/infra: Turret lines across every pass; patrolled roads; fenced zones.
- POIs: top: kessari_works, kessari_spire, military_base, command_bunker. NEW: sentry_line, drone_factory. Inside: dormant sentries in racks, maintenance bays, a chief engineer's override log.
- Life/people: Kessari clans (some friendly), roaming drones.
- Hazards/rewards: Death by auto-turret; AI cores, weapons.
- Fit: pre H > post H | ports B-D | R | no

**30. The Big Gun** (`big_gun`) - A single weapon the size of a mountain range.
- Happened: A planetary weapon was built, fired once, and never recovered.
- Land/sky: A vast scar along one meridian, ash fall at its end; red and black.
- Regions/infra: A barrel-straight track running for hundreds of kilometres; a support city at the breech.
- POIs: top: meridian_railgun, command_bunker, fusion_plant. NEW: breech_hall, ammunition_hall. Inside: control rooms with unspent checklists, ammunition halls, the crew's last log.
- Life/people: Few; survivors scavenge the supply line; Hollow echoes along the barrel.
- Hazards/rewards: Radiation; exotic power cells.
- Fit: pre H > post M/L | ports B-D | X | no

---

## D. Lawless and hard survival

**31. Road Warrens** (`road_warrens`) - Gang country on wheels.
- Happened: Fuel and order failed; the survivors became convoys.
- Land/sky: Flat dry plains and salt flats, heat shimmer, red dust; chrome and rust.
- Regions/infra: A dense web of patched highways, ramps, ring roads, pile-ups cleared into barricades; fuel is power.
- POIs: top: scrap_fort, refinery, rail_yard, old_airfield. NEW: gang_arena, fuel_fort, caravan_stop. Inside: shrines to engines, gladiator pits, stacks of jerry cans, a tally board of races.
- Life/people: Hostile roving convoys with buggies and bikes, rival packs, rare neutral caravans.
- Hazards/rewards: Chases; fuel, vehicles.
- Fit: pre M > post L/M | ports D-E | C | no

**32. The Water Tax** (`water_tax`) - Whoever holds the well holds the world.
- Happened: Rivers dried; the last springs became fortresses.
- Land/sky: Arid plateaus, canyon country; ochre and blue sky.
- Regions/infra: Wells and cisterns at the centre of every road; caravan trails; dry bridges.
- POIs: top: water_works, scrap_fort, old_farm, survivor_town. NEW: water_tower_keep, drowned_cistern, caravan_stop. Inside: tally rods, barrels, rationing ledgers, a deep cistern kept as a shrine.
- Life/people: Warlord owners, wandering water-sellers; burrowers.
- Hazards/rewards: Thirst, ambushes at wells; water, trade.
- Fit: pre M > post L | ports D-E | U | S

**33. Salt and Bone** (`salt_and_bone`) - A dried-out sea.
- Happened: A shallow sea evaporated.
- Land/sky: White flats, salt pans, bleached ships on the sea floor; glare.
- Regions/infra: Causeway roads, ports left high and dry, brine pipelines.
- POIs: top: docks, scrappy_port, crashed_freighter, refinery. NEW: salt_works, beached_liner. Inside: stranded liners, cargo rooms with crystals, harbour offices on stilts.
- Life/people: Salt-adapted burrowers, scavenger fisherfolk.
- Hazards/rewards: Glare, dehydration; salt, fuel, hulks.
- Fit: pre M > post L | ports C-E | U | S

**34. The Long Winter** (`long_winter`) - Snow on every road.
- Happened: An industrial haze cooled the planet; the thaw never came.
- Land/sky: Snowy plains, pine-like forests, white skies, blizzards.
- Regions/infra: Plowed single lanes between depots, heated outposts, ice roads, rail under snow.
- POIs: top: ice_outpost, survivor_hamlet, coal_plant, old_mine, deep_bunker. NEW: ski_lodge, ice_cutters. Inside: stove rooms, wood stacks, diaries of winters counted.
- Life/people: Ice beasts, fur-clad survivors; Hollow faint.
- Hazards/rewards: Cold; coal, furs, tech frozen intact.
- Fit: pre M > post L | ports D-E | C | S

**35. Ember World** (`ember_world`) - Burning for fifty years.
- Happened: Coal seams ignited under a continent.
- Land/sky: Smoking plains and black fields, orange nights, ash skies.
- Regions/infra: Roads that sink into smouldering seams; abandoned towns above the fire.
- POIs: top: coal_plant, old_mine, steelworks, rail_yard. NEW: burn_town. Inside: slumped ground, fire-fighters' trucks, a miner's last shift.
- Life/people: Heat-adapted wildlife, ash-nesters.
- Hazards/rewards: Burning ground, bad air; coal, geothermal.
- Fit: pre M > post L | ports D-E | U | no

**36. Bandit Kingdoms** (`bandit_kingdoms`) - Dozens of petty lords.
- Happened: A fragmented colony slid into feuding fiefdoms.
- Land/sky: Hills and valleys with castle-like holdfasts.
- Regions/infra: Each kingdom is a ring of fortified farms around a hold; toll roads; stolen power.
- POIs: top: scrap_fort, survivor_town, freeholder_farm, old_farm. NEW: hilltop_keep, toll_gate. Inside: feasting halls, trophy rooms, a hostage cell.
- Life/people: Gangs by territory; neutral pedlars; Freeholders pressed.
- Hazards/rewards: Raids; tribute, supplies, allies.
- Fit: pre L/M > post L | ports D-E | C | S

**37. Pit Worlds** (`pit_worlds`) - Everything interesting is below.
- Happened: The surface was unsafe, so everyone built down.
- Land/sky: Barren surface, high winds, orange sky.
- Regions/infra: Surface hatches and fan towers; underground cities linked by tunnels.
- POIs: top: deep_bunker, old_mine, command_bunker, research_base. NEW: undercity_district, fan_tower. Inside: shop streets, shared kitchens, the "deepest hall" many wouldn't enter.
- Life/people: Pale underground people, cave fauna.
- Hazards/rewards: Dark, close combat; big reward at depth.
- Fit: pre M/H > post L | ports C-E | U | S

**38. Floodlines** (`floodlines`) - The sea rose and kept rising.
- Happened: Storm surges drowned the lowlands.
- Land/sky: Flooded city grids, rooftops as islands, grey sea.
- Regions/infra: Sunk roads, bridges stranded; boat traffic; embankments.
- POIs: top: docks, water_works, sewage_works, hospital. NEW: barge_town, drowned_cistern. Inside: waterlines on the walls, upper floors lived in, boats hung from ceilings.
- Life/people: Fisher-folk, eels, birds.
- Hazards/rewards: Storm surges; boats, salvage.
- Fit: pre M/H > post L | ports C-E | C | S

**39. The Last Garden** (`last_garden`) - One valley still alive.
- Happened: Every other valley turned to dust; one remained green.
- Land/sky: A green valley in a brown world; jealous hills around.
- Regions/infra: A defended enclave; many roads lead to it.
- POIs: top: freeholder_farm, old_farm, survivor_town. NEW: walled_orchard. Inside: seed vaults, communal kitchens, guard towers.
- Life/people: Freeholders, besiegers, neighbours.
- Hazards/rewards: Politics; food, allies.
- Fit: pre M > post L | ports D-E | R | S

**40. Hunger Roads** (`hunger_roads`) - Cannibal rumour and empty larders.
- Happened: Supply chains died; some towns didn't.
- Land/sky: Dry farmland, drought, thin trees.
- Regions/infra: Burned farms, roadblocks, ration posts.
- POIs: top: survivor_hamlet, grain_elevator, old_farm. NEW: ration_depot. Inside: ledgers, barricades, a larder that is still locked.
- Life/people: Lean gangs, wary pedlars; scavengers.
- Hazards/rewards: Ambush; food, trade.
- Fit: pre M > post L | ports D-E | C | S

---

## E. Death worlds and wild nature

**41. Teeth in the Trees** (`teeth_trees`) - Carnivorous forest.
- Happened: Gardeners' hybrids escaped and spread.
- Land/sky: Green deep forest, shadow, humid.
- Regions/infra: Roads reduced to tunnels in the trees; bridges draped with vines.
- POIs: top: old_farm, research_lab, biolab. NEW: bloom_garden, treehouse_village. Inside: grown-through greenhouses, bones in pitcher-like traps.
- Life/people: Fanged vines, pack animals; hermits.
- Hazards/rewards: Ambush plants; rare organics.
- Fit: pre M/H > post L | ports D-E, 0 | U | no

**42. Stalker Reefs** (`stalker_reefs`) - Coral on land, walking.
- Happened: Sea animals adapted to dry shores.
- Land/sky: Coastal reefs, tide flats, shimmer pools.
- Regions/infra: Piers, docks stranded, pipelines.
- POIs: top: docks, scrappy_port, water_works. NEW: pier, lighthouse. Inside: salvaged boats, harbour offices.
- Life/people: Reef stalkers, schooling swarms.
- Hazards/rewards: Ambush at tide; pearl-like crystals.
- Fit: pre M > post L | ports D-E | U | S

**43. Hive Plains** (`hive_plains`) - Mounds to the horizon.
- Happened: Social insects grew into dominance.
- Land/sky: Flat steppe dotted with towering mounds.
- Regions/infra: Highways cut by mounds; towns eaten.
- POIs: top: alien_hive_ruin, old_farm, survivor_hamlet. NEW: mound_city. Inside: ribbed tunnels, queen chambers.
- Life/people: Swarms, soldier castes.
- Hazards/rewards: Swarm attacks; jelly, chitin.
- Fit: pre M > post L | ports D-E | U | no

**44. Maw Dunes** (`maw_dunes`) - Sand that eats.
- Happened: Burrowing giants spread.
- Land/sky: Dune seas, searing heat.
- Regions/infra: Buried roads, half-sunk towns.
- POIs: top: abandoned_spaceport, crashed_freighter, old_mine. NEW: sunk_town. Inside: sand-filled halls, shells of old ships.
- Life/people: Sand worms.
- Hazards/rewards: Vibration sensing; rare minerals.
- Fit: pre M > post L | ports D-E | U | no

**45. Thorn Wall** (`thorn_wall`) - Walls of thorn across the land.
- Happened: Hedges grown as fences became a maze.
- Land/sky: Hills of thorn lanes, green-black.
- Regions/infra: Roads become paths between walls; lookouts.
- POIs: top: old_farm, survivor_hamlet, scrap_fort. NEW: maze_manor, estate_house. Inside: hedges, hunting lodges.
- Life/people: Maze beasts, hunters.
- Hazards/rewards: Getting lost; hunt trophies.
- Fit: pre L > post L | ports E | U | S

**46. Storm Spires** (`storm_spires`) - Lightning highways.
- Happened: An atmospheric engine broke.
- Land/sky: Rock spires, constant storms.
- Regions/infra: Lightning rods, shelters.
- POIs: top: telecom_exchange, research_base, wind_farm. NEW: weather_engine. Inside: control halls, rods.
- Life/people: Static-cats, nesters.
- Hazards/rewards: Lightning; stormcore.
- Fit: pre H > post L | ports C-E | R | no

**47. Titans' Meadows** (`titans_meadows`) - Gigantic grazers.
- Happened: Wildlife grew immense in a quiet world.
- Land/sky: Rolling grass, giant skeletons.
- Regions/infra: Roads bent around herds.
- POIs: top: old_farm, survivor_hamlet, landing_clearing. NEW: bone_dig_site. Inside: dig tents, bones.
- Life/people: Giants; guides.
- Hazards/rewards: Stampedes; fossils.
- Fit: pre L/M > post L | ports D-E | R | S

**48. Spore Caverns** (`spore_caverns`) - A fungal underworld.
- Happened: Collapsed caves filled with fungus.
- Land/sky: Sinkholes, glow.
- Regions/infra: Sinkhole stairs, mine shafts.
- POIs: top: old_mine, abandoned_mine, deep_bunker. NEW: crystal_garden, fruiting_hall. Inside: glowing halls.
- Life/people: Blind crawlers.
- Hazards/rewards: Spore bursts; organics.
- Fit: pre M > post L | ports D-E | U | no

**49. Acid Reaches** (`acid_reaches`) - Rain that bites.
- Happened: Factory skies turned on their makers.
- Land/sky: Eroded canyons, yellow rain.
- Regions/infra: Rusted bridges, corroded plants.
- POIs: top: refinery, abandoned_factory, water_works. NEW: acid_pool_works. Inside: corroded tanks.
- Life/people: Acid-resistant crawlers.
- Hazards/rewards: Corrosion; chemicals.
- Fit: pre M > post L | ports D-E | U | no

**50. Kelp Sea** (`kelp_sea`) - The ocean is thick.
- Happened: Algae overgrew the ocean.
- Land/sky: Floating mats, island chains.
- Regions/infra: Ports choked.
- POIs: top: docks, scrappy_port, offshore_rig. NEW: offshore_rig, barge_town. Inside: oil rigs, boats.
- Life/people: Swimmers, drifters.
- Hazards/rewards: Entanglement; food.
- Fit: pre M > post L | ports C-E | U | S

---

## F. Strange, eerie and Hollow-touched

**51. Wrong Stars** (`wrong_stars`) - The sky has the wrong number of lights.
- Happened: Space near the core frayed; the night sky no longer matches the star map, and the astronomers noticed first.
- Land/sky: Ordinary hills and plains under a sky with extra moons, stars that hold still while others wheel, a sun that stutters at noon; cold violet light, long double shadows.
- Regions/infra: Normal roads and ruins, but signposts disagree about distances; observatory domes on every high point; power lines run to the domes.
- POIs: top: research_base, telecom_exchange, alien_library, deep_bunker. NEW: observatory, time_pocket. Inside: star charts with crossed-out entries, a dome whose telescope points at the ground, logs that count nights that did not happen.
- Life/people: Animals avoid open ground at night; lone astronomers who keep counting; a few pilgrims who sleep outside to watch.
- Hazards/rewards: Short time slips (a minute lost, never lethal); signal leads, star data, rare optics.
- Fit: pre M/H > post L | ports C-E | R | no

**52. The Folded Town** (`folded_town`) - One street that turns back on itself.
- Happened: A settlement was folded; walking out brings you back in by the other end.
- Land/sky: Flat farmland, perfect weather, no birdsong; colours slightly too clean.
- Regions/infra: A single too-perfect town with fields and one road that loops; no power lines yet the lamps burn.
- POIs: top: survivor_hamlet, hospital, old_farm. NEW: wrong_house, time_pocket. Inside: tables set for dinner, every calendar on the same day, the same note in every house in a different hand.
- Life/people: Nothing alive; Hollow strong; the unease is the point.
- Hazards/rewards: Disorientation; unique odd items that do not fit any recipe.
- Fit: pre M > post L | ports E,0 | X | no

**53. Hollow Orchards** (`hollow_orchards`) - Trees that grow toward the core.
- Happened: The Bloom's touch bent every plant coreward; the people who stayed built shrines to the lean.
- Land/sky: Forest and hills, every trunk leaning one way; pale amber light, no shadows at noon.
- Regions/infra: Roads straight along the lean; shrines at crossroads; orchards in perfect rows that no one planted.
- POIs: top: old_farm, alien_cathedral, survivor_hamlet. NEW: hollow_shrine, bloom_garden. Inside: pilgrim cots, offerings of fruit, a pit lined with roots that all point down.
- Life/people: Pilgrim hermits, bent herd animals that walk one way at dusk.
- Hazards/rewards: Mutagen patches (warned); rare organics.
- Fit: pre L/M > post L | ports E,0 | U | S

**54. Whisper Flats** (`whisper_flats`) - Radio carries voices that are not there.
- Happened: Old relays broadcast a signal nobody sent, in the voices of people who had left.
- Land/sky: Salt flats and scrub, masts to the horizon, huge pale sky.
- Regions/infra: Mast farms, relay huts, cable lines on wooden poles; a straight road to nowhere.
- POIs: top: meridian_relay, telecom_exchange, deep_bunker. NEW: radio_mast_site. Inside: operator desks with warm headsets, tape logs of conversations that have not yet happened, a booth where the dial only goes one way.
- Life/people: Lone listeners, strange birds that perch on masts in rows.
- Hazards/rewards: Voice lures draw you toward masts; intel leads and comms parts.
- Fit: pre M/H > post L | ports D-E | R | S

**55. Mirror Cities** (`mirror_cities`) - Every district has a twin.
- Happened: The Bloom copied a city onto itself, imperfectly.
- Land/sky: Urban plains, glass towers, bright haze, no clouds.
- Regions/infra: Identical districts either side of a river; bridges that lead back to the same bank; transit that runs both ways at once.
- POIs: top: ominous_tower, meridian_apartments, hospital, shopping_mall. NEW: wrong_house, mirror_hall. Inside: duplicate rooms each with one small edit, a lift with a button for a floor that is the same floor.
- Life/people: Few; Hollow strong; scavengers refuse to cross the river.
- Hazards/rewards: Lost bearings; odd tech, double-keyed vaults.
- Fit: pre H > post L | ports A-C | X | no

**56. Choir Canyon** (`choir_canyon`) - Wind plays the rock.
- Happened: Natural fluting in the stone; the Bloom taught it to repeat in sentences.
- Land/sky: Canyon country, fluted stone, warm gold light, steady wind.
- Regions/infra: Tracks along canyon rims; shrine huts; rope bridges.
- POIs: top: alien_cathedral, survivor_hamlet, old_airfield. NEW: sound_garden, hollow_shrine. Inside: wind chimes tuned to the canyon, listening posts, scratched song notation.
- Life/people: Pilgrims, canyon fliers that nest in the pipes.
- Hazards/rewards: Calm; resonant stone and strange materials.
- Fit: pre L/M > post L | ports E,0 | U | S

**57. Clockwork Hours** (`clockwork_hours`) - Everything runs, slightly off.
- Happened: Old automata kept their routines after their owners left.
- Land/sky: Quiet towns, always dusk, brass light.
- Regions/infra: Trams on loops, automated markets opening on schedule, lamps lit by timers.
- POIs: top: exchange_market, kessari_works, meridian_apartments. NEW: clock_tower_town, puppet_theatre. Inside: waiters that serve nothing, a theatre rehearsing for no one, a hotel register signed by machines.
- Life/people: Machines; few people; a handful of repair-folk who keep them going.
- Hazards/rewards: Calm; working parts and AI cores.
- Fit: pre H > post H | ports B-D | R | S

**58. The Slow Dead** (`slow_dead`) - They get up, eventually.
- Happened: Hollow-touched bodies rise at night, slowly, and lie down at dawn.
- Land/sky: Fogged farmland, cold nights, church-grey light.
- Regions/infra: Graveyards on every hill; walled churchyards; roads with gates.
- POIs: top: hospital, survivor_hamlet, old_farm. NEW: cemetery_vault, church_chapel. Inside: stacked coffins, barricaded vestries, nailed doors with tally marks.
- Life/people: Shamblers by night, cautious hamlets by day.
- Hazards/rewards: Night swarms; relics, medicine.
- Fit: pre L/M > post L | ports E | U | no

**59. Eye of the Bloom** (`eye_bloom`) - A bloom front a day away.
- Happened: The Bloom is arriving now, and a last evacuation is under way.
- Land/sky: Half green, half violet; a line of colour on the sky.
- Regions/infra: Evacuation traffic still on the road; fortified towns on the front.
- POIs: top: evacuation_camp, military_base, research_lab, hospital. NEW: bloom_scar, front_checkpoint. Inside: planning boards, half-burned records, medics still working.
- Life/people: Military, refugees, Meridian remnants.
- Hazards/rewards: The front itself; faction jobs and survey pay.
- Fit: pre M/H > post M | ports B-D | R | no

**60. Dream Salt** (`dream_salt`) - Sleepers who never left cryo.
- Happened: Cryo vaults failed slowly; their sleepers dream together.
- Land/sky: Ice plains, aurora, quiet.
- Regions/infra: Vault entrances in rows; buried power cables.
- POIs: top: cryo_vault, ice_outpost, research_base. NEW: sleeper_barracks. Inside: pods in rows, some empty, some fogged, a door that opens onto a room you remember.
- Life/people: Few; Hollow touch.
- Hazards/rewards: Cold; cryo tech, lore.
- Fit: pre H > post L | ports D-E | U | S

**61. Black Library** (`black_library`) - Knowledge, guarded.
- Happened: Archivists preserved the dangerous parts of what was known, and walled them in.
- Land/sky: Dry hills, thin air, lantern light.
- Regions/infra: Archives and monasteries on ridges; courier trails.
- POIs: top: alien_library, deep_bunker. NEW: monastery, archive_vault. Inside: reading rooms, hidden stacks, a card catalogue with entries that are crossed out.
- Life/people: Keepers, wardens, curious pilgrims.
- Hazards/rewards: Traps, wardens; data and blueprints.
- Fit: pre M/H > post L | ports E | R | no

**62. Lamplight World** (`lamplight`) - One lit window in each town.
- Happened: Something keeps a lamp lit in every settlement.
- Land/sky: Dim fields, moorland, mist.
- Regions/infra: Villages with single lamps; stone walls; lanes.
- POIs: top: survivor_hamlet, old_farm. NEW: hermit_cabin. Inside: an unfinished meal, an unwritten letter, a bed still warm.
- Life/people: Single kind survivors.
- Hazards/rewards: Quiet; stories and hospitality.
- Fit: pre L/M > post L | ports E,0 | U | S

---

## G. Quiet and beautiful

**63. The Glass Garden** (`glass_garden`) - Crystal flowers.
- Happened: Mineral rain seeded crystal across the plains.
- Land/sky: Crystal fields, prismatic dawn, bell-like wind.
- Regions/infra: Few roads; research huts on stilts.
- POIs: top: research_lab, landing_clearing. NEW: crystal_garden. Inside: logbooks, sample cases, a lab bench grown through with crystal.
- Life/people: Light grazers, collectors.
- Hazards/rewards: Sharp fields; crystals.
- Fit: pre L/M > post L | ports E,0 | U | S

**64. Moon Lakes** (`moon_lakes`) - Mirror lakes under huge moons.
- Happened: Lightly settled; summer houses and little else.
- Land/sky: Lake district, huge pale moons, still water.
- Regions/infra: Boat jetties, track roads, cabin clusters.
- POIs: top: landing_clearing, survivor_hamlet. NEW: lighthouse, pier. Inside: fishing gear, guest books with last entries.
- Life/people: Wading birds, a few fishers.
- Hazards/rewards: Calm; fish, quiet building land.
- Fit: pre L > post L | ports E,0 | C | S

**65. Terraced Valleys** (`terraced`) - Hillsides carved into steps.
- Happened: Old farming valleys, kept going by hand.
- Land/sky: Terraces, mist, waterfalls.
- Regions/infra: Stone stairs, waterwheels, irrigation channels.
- POIs: top: old_farm, freeholder_farm. NEW: water_mill. Inside: stores, shrines, ledgers of harvests.
- Life/people: Freeholders, goat-like climbers.
- Hazards/rewards: Calm; food.
- Fit: pre L > post L | ports E | C | S

**66. Wildflower Runways** (`wildflower`) - Airfields under flowers.
- Happened: A world of private flyers.
- Land/sky: Meadows, big sky, warm wind.
- Regions/infra: A landing strip in every valley, windsocks, hangars.
- POIs: top: old_airfield, landing_clearing. NEW: aero_club. Inside: planes under tarps, logbooks, a bar with a propeller on the wall.
- Life/people: Bees, grazers, hobby pilots.
- Hazards/rewards: Calm; aircraft.
- Fit: pre M > post L | ports D-E | C | S

**67. Aurora Shelf** (`aurora_shelf`) - Polar lights over quiet stations.
- Happened: Research stations only; never a colony.
- Land/sky: Ice shelf, aurora, long night.
- Regions/infra: A chain of stations joined by flagged routes.
- POIs: top: ice_outpost, research_base, cryo_vault. NEW: observatory. Inside: sleeping bags, notes, a kettle on a cold stove.
- Life/people: Seal-like grazers, few visitors.
- Hazards/rewards: Cold; research data.
- Fit: pre M > post L | ports D-E | U | S

**68. Archipelago of Bells** (`bells`) - Islands with bell towers.
- Happened: Island monasteries that rang the hours.
- Land/sky: Island chains, trade winds, white towers.
- Regions/infra: Ferries, causeways, harbour steps.
- POIs: top: docks, survivor_hamlet. NEW: monastery, ferry_terminal. Inside: bell ropes, refectories, gardens.
- Life/people: Monks, sea birds.
- Hazards/rewards: Calm; boats, food.
- Fit: pre L > post L | ports E | U | S

**69. Orchard Moon** (`orchard_moon`) - Low gravity, fruit trees.
- Happened: An orchard colony.
- Land/sky: Small low-g meadows, close horizon, huge parent planet.
- Regions/infra: Rows, ladders, packing sheds.
- POIs: top: old_farm, freeholder_farm. NEW: orchard. Inside: presses, jars, cider cellars.
- Life/people: Floating birds.
- Hazards/rewards: Calm; food.
- Fit: pre M > post L | ports D-E | U | S

**70. Amber Steppe** (`amber_steppe`) - Endless grass and herders.
- Happened: Nomad herding on a thinly settled world.
- Land/sky: Steppe, huge sky, gold grass.
- Regions/infra: Tracks, wells, felt camps.
- POIs: top: survivor_hamlet, old_farm. NEW: yurt_camp. Inside: felts, rails, cheese racks.
- Life/people: Herds, nomads.
- Hazards/rewards: Calm; trade.
- Fit: pre L > post L | ports E,0 | C | S

**71. Hot Spring Highlands** (`hot_springs`) - Steam and pools.
- Happened: A spa world.
- Land/sky: Geysers, steam, green moss.
- Regions/infra: Boardwalks, bathhouse rows.
- POIs: top: survivor_hamlet, hospital. NEW: spa_springs, ski_lodge. Inside: bathhouses, towels folded, locked lockers.
- Life/people: Steam-loving fauna.
- Hazards/rewards: Scalding; geothermal heat.
- Fit: pre M > post L | ports D-E | U | S

**72. Starfield Desert** (`starfield`) - Clear dark skies.
- Happened: Observatory world.
- Land/sky: Dry plateau, extreme stars.
- Regions/infra: Dome clusters on a ridge road.
- POIs: top: research_base, telecom_exchange. NEW: observatory. Inside: lenses, logs, a lunch left on a console.
- Life/people: Few.
- Hazards/rewards: Cold nights; optics.
- Fit: pre M > post L | ports D-E | U | S

---

## H. Industry and machines

**73. Foundry Belt** (`foundry_belt`) - Furnaces for a thousand kilometres.
- Happened: A heavy-industry world whose furnaces were banked, not blown out, when the orders stopped.
- Land/sky: Slag hills, smoke columns, red-lit nights, rust and soot palette.
- Regions/infra: Rail trunks between mills, slag-heap haul roads, pipe racks overhead, pylon lines thick as hedges.
- POIs: top: steelworks, refinery, rail_yard, coal_plant, old_mine. NEW: ore_sorting_hall. Inside: moulds with the last pour cooled in them, shift boards, locker rooms with names on tags, a foreman's office with a safe.
- Life/people: Crows, slag-nesting fliers, scavenger crews cutting rails for sale.
- Hazards/rewards: Molten pits where a furnace still holds heat; plate steel, alloys.
- Fit: pre M > post L | ports C-D | C | S

**74. The Drone Orchard** (`drone_orchard`) - Autonomous farming at scale.
- Happened: Machines kept farming long after the farmers left, and kept shipping to nobody.
- Land/sky: Perfect geometric fields, green-gold, huge sky.
- Regions/infra: Grid roads, charging pylons, grain rail to elevators, irrigation arms that sweep unseen.
- POIs: top: grain_elevator, kessari_works, old_farm. NEW: harvester_field, drone_hive. Inside: stalled harvesters, hives where drones dock in rows, silos that are full to the roof and sealed.
- Life/people: Farming machines, a few Kessari clans who see them as cousins, scavengers who steal grain.
- Hazards/rewards: Harvesters treat walkers as weeds; food, drone parts, machine cores.
- Fit: pre H > post H | ports C-D | U | S

**75. Power Down** (`power_down`) - Dark grids.
- Happened: The grid failed in a cascade; every plant tripped within the hour.
- Land/sky: Pylon forests, still air, silent towns, dim orange sunsets.
- Regions/infra: Substations at every junction, lines across the land, a power plant of each old kind.
- POIs: top: substation, nuclear_plant, fusion_plant, coal_plant, wind_farm, solar_farm. NEW: dam_hydro. Inside: switch halls, dead control rooms with the cascade replayed on a frozen board, a duty log of the last operator.
- Life/people: Scavengers stripping copper, nesters in transformer halls.
- Hazards/rewards: Arc flash and radiation; power cells, cable, transformers.
- Fit: pre M/H > post L | ports C-D | C | S

**76. The Elevator Shadow** (`elevator_shadow`) - A tower reaching orbit, fallen.
- Happened: The space elevator cable fell and laid itself across a continent.
- Land/sky: A broken line of ribbon wreck across plains and mountains, scarred craters at intervals.
- Regions/infra: A rail line to the base, a debris corridor, camps along the fall line.
- POIs: top: abandoned_spaceport, fusion_plant, crashed_freighter. NEW: space_elevator_base. Inside: climber cabins with seats still belted, cable drums, a control room where the last command is still queued.
- Life/people: Salvagers, Reavers, a faction patrol keeping others away from the base.
- Hazards/rewards: Unstable debris; exotic alloy cable.
- Fit: pre H > post L | ports A-B | X | no

**77. Cannery World** (`cannery_world`) - Tins forever.
- Happened: A food-export world whose lines ran until they ran out of fish.
- Land/sky: Grey coast, gulls, fish-scale light.
- Regions/infra: Lines of canneries along a coast road, fleets in harbour, tin-towers by the rail.
- POIs: top: abandoned_factory, docks, scrappy_port. NEW: cannery, rendering_plant. Inside: tins stacked to the ceiling, conveyor belts with the last tins half-labelled, offices with a label contest on the wall.
- Life/people: Gulls, scavenging rats, gang crews guarding tin stores.
- Hazards/rewards: Rot pockets and gas; food, tin, salvaged boats.
- Fit: pre M > post L | ports C-E | C | S

**78. Data Tombs** (`data_tombs`) - Server halls, cold.
- Happened: An archive world chose the polar ice to keep its records cold.
- Land/sky: Ice plains, pale blue light, low sun.
- Regions/infra: Cable corridors, ice roads, heat vents steaming from halls.
- POIs: top: telecom_exchange, research_base, ice_outpost. NEW: data_centre. Inside: racks in cold aisles, a cooling room with frost flowers, a terminal that offers to restore a backup of something.
- Life/people: Maintenance machines, a few archivists.
- Hazards/rewards: Cold; data and AI cores.
- Fit: pre H > post L | ports C-D | U | S

**79. Shipbreakers** (`shipbreakers`) - Hulls on slipways.
- Happened: Scrap trade on the shore; the yards kept cutting after the buyers left.
- Land/sky: Grey shore, cranes, rust and sparks.
- Regions/infra: Slipways, tide docks, yard rail, rows of cut-up hull.
- POIs: top: docks, crashed_freighter, scrappy_port. NEW: breaker_yard. Inside: cutting rigs, half-gutted liners, a foreman's lottery of salvage claims.
- Life/people: Crews who still cut, rival yards, scavenging gulls.
- Hazards/rewards: Falling plates; hull plates, engines.
- Fit: pre M/H > post L | ports B-D | U | S

**80. Maglev Spine** (`maglev_spine`) - One line crosses the world.
- Happened: A transit world built around a single great line that stopped mid-journey.
- Land/sky: Plains, a viaduct line to the horizon, clean light.
- Regions/infra: The line on pylons, depots every few hundred kilometres, trains standing in stations.
- POIs: top: rail_yard, substation. NEW: maglev_depot, monorail_station. Inside: gleaming cars with seats full of luggage, ticket gates, a driver's cab with a half-eaten meal.
- Life/people: Herds beneath the viaduct, tramp scavengers.
- Hazards/rewards: Calm; maglev parts, whole carriages as bases.
- Fit: pre H > post L | ports A-C | R | S

**81. Pipeline World** (`pipeline_world`) - Pipes everywhere.
- Happened: Fuel export world with one network that spans the planet.
- Land/sky: Desert and scrub, heat shimmer, flare stacks.
- Regions/infra: Pipes, pump stations, tank farms, gang-held junctions.
- POIs: top: refinery, water_works, scrap_fort. NEW: pump_station. Inside: valves, gauges frozen, a tank yard with one tank still full.
- Life/people: Gangs, pipeline walkers, burrowers under the line.
- Hazards/rewards: Fire and leaks; fuel.
- Fit: pre M > post L | ports D-E | C | S

**82. Geothermal Deep** (`geothermal`) - Drilled for heat.
- Happened: Power from the earth: wells tapped a whole province.
- Land/sky: Volcanic uplands, steam plumes, mineral colours.
- Regions/infra: Wellheads, pipe runs, turbine houses, hot roads.
- POIs: top: fusion_plant, old_mine, substation. NEW: geothermal_plant. Inside: steam halls, turbine floors, a control room with a warm panel.
- Life/people: Few; heat-lovers in the pools.
- Hazards/rewards: Steam and sinkholes; power.
- Fit: pre H > post L | ports C-E | U | S

---

## I. Odd, funny and human

**83. Souvenir World** (`souvenir`) - Giant roadside attractions.
- Happened: A tourist world whose economy was oversized roadside wonders; the visitors were evacuated first, in good order.
- Land/sky: Bright plains and hills, sun-faded paint in cheerful colours, huge signs leaning in the wind.
- Regions/infra: Highways lined with giant statues and shaped buildings, gift stops at every junction, coach parks, a monorail loop between the biggest sights; power lines run to the parks only.
- POIs: top: exchange_market, survivor_town, old_airfield, hospital. NEW: theme_park, museum. Inside: gift shops full of keepsakes, a ride that still turns once if you pull the lever, a museum of the planet's one famous thing with a gift-shop exit, a coach with the tour guide's microphone still on the seat.
- Life/people: Scavengers who collect souvenirs, birds nesting in the statues, a small town living inside a giant teapot.
- Hazards/rewards: Rusted ride structures can fall; trinkets sell well, and the planet is a good trading economy.
- Fit: pre M > post L | ports C-D | U | S

**84. Bureaucracy Moon** (`bureaucracy`) - Forms.
- Happened: An administrative world processed the evacuation by paperwork and is still processing it.
- Land/sky: Grey plains, tidy blocks, flat overcast light, beige and slate.
- Regions/infra: Office parks, filing halls, ring roads with numbered exits, queue marshalling yards at every junction; power and lights run on schedule.
- POIs: top: meridian_apartments, ominous_tower, exchange_depot, telecom_exchange. NEW: ministry_hall, archive_vault. Inside: stamp rooms, in-trays that never empty, a counter where a clerk machine asks for form nine, a vault of exit permits for people who left without them.
- Life/people: Clerk machines, a few patient survivors who learned the forms, a Meridian liaison.
- Hazards/rewards: Low danger; permits, records and travel documents that open doors.
- Fit: pre H > post H | ports B-D | U | S

**85. Pirate Radio** (`pirate_radio`) - A voice that never stops.
- Happened: A hobbyist station was kept on air by long-life cells and never told it could stop.
- Land/sky: Soft hills, masts on every ridge, warm evenings, wildflowers in the verges.
- Regions/infra: A mast farm, a studio hut, scattered listening posts joined by footpaths; telephone poles in lines.
- POIs: top: meridian_relay, survivor_hamlet, telecom_exchange. NEW: radio_mast_site. Inside: a cheerful host recording that loops, a request box with notes, shelves of tapes of callers, a coffee mug left on the desk.
- Life/people: Friendly survivors who tune in, a hermit who thinks he is the host.
- Hazards/rewards: Calm; comms parts, news of nearby worlds, a trade tip each night.
- Fit: pre M > post L | ports D-E | U | S

**86. Zoo World** (`zoo_world`) - Escaped menagerie.
- Happened: Zoos opened their doors in the last days so nothing would starve in a cage.
- Land/sky: Parkland, lakes and savannah patches, animals in the wrong places.
- Regions/infra: Enclosure belts and visitor roads, safari loops, a tram around the biggest park.
- POIs: top: old_farm, research_lab, hospital. NEW: zoo_menagerie. Inside: keeper logs with feeding schedules, a night house with sealed doors, a nursery with a notice to be kept warm, a locked house that was never opened.
- Life/people: Mixed exotic fauna from many worlds that breeds together, a few keepers who stayed.
- Hazards/rewards: Beasts out of their habitat; creature parts, rare specimens.
- Fit: pre M/H > post L | ports C-D | U | S

**87. Artist Colony** (`artist_colony`) - Murals.
- Happened: An artists' retreat painted the whole world before leaving.
- Land/sky: Coastal hills, bright colour on every wall, salt wind.
- Regions/infra: Studios on cliff paths, murals on ruins, sculpture trails, kilns, a small harbour.
- POIs: top: survivor_hamlet, docks, old_airfield. NEW: artist_colony, museum. Inside: canvases of the last days, a studio with an unfinished portrait of a stranger who looks like you, a gallery with price tags still on.
- Life/people: A few artists who stayed, goats on the sculpture trail.
- Hazards/rewards: Calm; art, pigments, decorative blocks.
- Fit: pre M > post L | ports E | U | S

**88. Sporting Planet** (`sporting`) - Stadiums.
- Happened: A league world whose fans lived in the stadiums at the end.
- Land/sky: Plains under floodlight pylons, cold bright mornings.
- Regions/infra: Stadium ring roads, fan camps, club halls, rail spurs to each ground.
- POIs: top: evacuation_camp, survivor_town, hospital. NEW: stadium_camp, gang_arena. Inside: locker rooms, trophy halls, a scoreboard frozen mid-final, a camp of bunk tents on the pitch.
- Life/people: Gangs who still hold matches, wandering fans, club stewards who check tickets.
- Hazards/rewards: Arena fights; trophies, supplies, a gambling economy.
- Fit: pre M/H > post L | ports C-D | U | S

**89. Casino Dunes** (`casino_dunes`) - Luck.
- Happened: A gambling world; the last high-rollers never cashed out.
- Land/sky: Desert, neon dead in daylight, haze at dusk.
- Regions/infra: A strip of resort towers, private strips, a monorail along the strip, generator farms.
- POIs: top: exchange_market, abandoned_spaceport. NEW: casino_resort, hotel_tower. Inside: vaults, chips under glass, a roulette table still spinning slowly, a penthouse with a host still waiting.
- Life/people: Gangs holding floors, machine croupiers, tourists' ghosts in lift logs.
- Hazards/rewards: Vault traps; luxury goods, credit chips.
- Fit: pre H > post L | ports B-D | R | S

**90. School Holidays** (`school_holidays`) - Campuses.
- Happened: An academy world; the term ended and no one came back.
- Land/sky: Green hills, lawns gone to meadow, rooks.
- Regions/infra: Campuses, dormitory ranges, a rail halt and a bus turning circle.
- POIs: top: research_lab, survivor_hamlet, library. NEW: school_campus. Inside: notes passed in class, lockers, a staff room with the kettle still plugged in, a science wing with an unfinished experiment.
- Life/people: Few; squatters in the boarding houses.
- Hazards/rewards: Calm; books, lab gear.
- Fit: pre M > post L | ports D-E | C | S

**91. Ghost Ship Harbour** (`ghost_harbour`) - Liners.
- Happened: Liners ran aground in the last rush for the coast.
- Land/sky: Long beaches, bone-white hulls, gull cries.
- Regions/infra: A harbour town, hulls laid up along the shore, cranes, a coastal highway cut by cargo.
- POIs: top: docks, crashed_freighter, scrappy_port. NEW: beached_liner. Inside: ballrooms, cabins with luggage, a captain's log recording the decision to beach, a casino deck with the tables set.
- Life/people: Reavers working the hulls, nesters in the lower decks.
- Hazards/rewards: Unstable decks; cargo and fine goods.
- Fit: pre M > post L | ports C-D | U | S

**92. Library Towns** (`library_towns`) - Book towns.
- Happened: Scholarly towns where everyone read to the end.
- Land/sky: Hills, mist, bookshop lanes.
- Regions/infra: Streets of shops and reading rooms linked by covered arcades.
- POIs: top: survivor_hamlet, alien_library. NEW: public_library. Inside: stacks, reading lamps, notes in margins that answer each other.
- Life/people: Hermits who read, a cat-like grazer in every window.
- Hazards/rewards: Calm; books, maps, blueprints.
- Fit: pre L/M > post L | ports E | C | S

---

## J. Frontier, low-tech and old things

**93. Horse and Diesel** (`diesel_frontier`) - Dirt roads and steam.
- Happened: A frontier colony that never reached the modern day, and did not need a collapse.
- Land/sky: Plains, wooden towns, big sky, dust on the boots.
- Regions/infra: Dirt roads, steam and diesel rail, water towers, stage stops, telegraph poles.
- POIs: top: survivor_town, rail_yard, freeholder_farm, old_mine. NEW: water_tower_keep. Inside: saloon rooms, telegraph office, sheriff's jail, a livery with tack on the pegs.
- Life/people: Freeholders, horse-like herd beasts, bandits on the line.
- Hazards/rewards: Trade, bandits; diesel gear.
- Fit: pre L > post L | ports E,0 | C | S

**94. Castle Worlds** (`castle_worlds`) - Stone keeps.
- Happened: A regressed colony rebuilt feudal order from timber and stone.
- Land/sky: Hills, stone towers, banners in the wind.
- Regions/infra: Keeps, stone bridges, tolled roads, mills, no rail.
- POIs: top: scrap_fort, survivor_town, old_farm. NEW: hilltop_keep. Inside: great halls, dungeons, armouries, a chapel with a lord's tomb.
- Life/people: Lords, retainers, outlaws.
- Hazards/rewards: Raids; fortified bases to hold.
- Fit: pre L > post L | ports 0 | U | S

**95. Barrow Downs** (`barrow_downs`) - Burial mounds.
- Happened: Old burials whose dead do not rest.
- Land/sky: Downs, mist, standing stones, a low sun.
- Regions/infra: Mounds, ridge tracks, stone circles.
- POIs: top: precursor_vault, alien_cathedral. NEW: tomb_mound. Inside: grave goods, stone doors, bones set in patterns, a chamber that is warm.
- Life/people: Few; Hollow wights at dusk.
- Hazards/rewards: Wights; grave goods.
- Fit: pre L > post L | ports 0 | R | S

**96. Pilgrim Roads** (`pilgrim_roads`) - Hostels.
- Happened: A pilgrimage world whose roads are walked still.
- Land/sky: Hills, shrines, waystations, prayer flags.
- Regions/infra: Pilgrim roads with hostels, chapels and well-houses at day-march intervals.
- POIs: top: survivor_hamlet, alien_cathedral. NEW: pilgrim_hostel, church_chapel. Inside: bunks, prayer boards, hot soup, a donation box.
- Life/people: Pilgrims, wardens of the road.
- Hazards/rewards: Calm; food, rumour.
- Fit: pre L/M > post L | ports E | C | S

**97. Precursor Shelf** (`precursor_shelf`) - Alien platforms.
- Happened: Precursor platforms stand on a world that never had a settler.
- Land/sky: Plains with smooth plates and pillars, odd light that never quite sets.
- Regions/infra: Platforms, causeways, no modern roads.
- POIs: top: precursor_vault, alien_library. NEW: megalith_ring. Inside: glyphs, floors that tilt, chambers with no door.
- Life/people: Guardians, nothing else.
- Hazards/rewards: Traps; exotic materials.
- Fit: pre (none) > post L | ports 0 | X | no

**98. Lonely Lighthouses** (`lighthouses`) - Beacons.
- Happened: A beacon chain along a dangerous coast, still lit.
- Land/sky: Cliffs, sea, storms.
- Regions/infra: Coast road, lighthouses at intervals, keepers' cottages.
- POIs: top: scrappy_port, docks. NEW: lighthouse. Inside: keepers' logs, spiral stairs, a lamp room with the lens still turning.
- Life/people: Keepers, sea birds.
- Hazards/rewards: Storms; views, boats.
- Fit: pre L > post L | ports E | U | S

**99. Bone Dig** (`bone_dig`) - Fossils.
- Happened: A fossil rush dug up something huge, and then dug up more.
- Land/sky: Badlands, striped rock, cutting winds.
- Regions/infra: Dig camps, tracks, spoil heaps.
- POIs: top: old_mine, abandoned_mine. NEW: bone_dig_site. Inside: casts, field notes, a cradle holding a skull, a tent with a letter home.
- Life/people: Diggers, nesters in the bones.
- Hazards/rewards: Cave-ins; fossils.
- Fit: pre L/M > post L | ports E | U | S

**100. Wind Chime Coast** (`chime_coast`) - Singing wind.
- Happened: Wind farms tuned to sing in the gale.
- Land/sky: Coast, ranks of turbines, steady wind.
- Regions/infra: Turbine ranks, a coast road, tuners' huts.
- POIs: top: wind_farm, survivor_hamlet. NEW: sound_garden. Inside: chimes, turbine nacelles with nests.
- Life/people: Birds, a few tuners.
- Hazards/rewards: Calm; power parts.
- Fit: pre M > post L | ports D-E | U | S

---

## K. Added themes (owner's ideas), each filed under its group

**101. Sunken Raiders** (`sunken_raiders`) - Group D, hard survival. A bog with ships in it and a pirate sky.
- Happened: A swamp world where freighters came down in the mire and a Reaver capital ship claimed the sky above.
- Land/sky: Waterlogged lowlands, mist, drowned trees, brown water, green-grey light.
- Regions/infra: Boardwalks and causeways over bog, a few firm rises, ships sunk nose-first with their tails in the air.
- POIs: top: crashed_freighter, scrappy_port, scrap_fort, evacuation_camp. NEW: stilt_village, bog_wreck_field. Inside: flooded holds, crew bunks on the high decks, Reaver tally boards.
- Life/people: Waders, ambushers, leech swarms; Reavers aboard the patrolling capital ship who drop landing parties.
- Hazards/rewards: Sinking mud, ship patrols; salvage, ship cores.
- Fit: pre M/H > post L | ports C-E | U | S

**102. Overgrown Metropolis** (`overgrown_metropolis`) - Group E, nature reclaims. A city to the horizon, in leaf.
- Happened: A city-covered world was left to the green over a century.
- Land/sky: Urban canyons with forest, roots through roads, rivers in old streets, soft green light.
- Regions/infra: Ring roads and overpasses collapsed into the canopy, metro lines under, skyways broken, trains in tunnels.
- POIs: top: ominous_tower, meridian_apartments, hospital, rail_yard, sewage_works. NEW: shopping_mall, highway_interchange, metro_station. Inside: apartments with trees in the lounge, malls with a canopy, tunnels.
- Life/people: Climbers, ambushers, flocks in the towers; hermits, rooftop farmers.
- Hazards/rewards: Falling facades; wide city salvage.
- Fit: pre H > post L | ports A-C | R | S

**103. Waste World** (`waste_world`) - Group C, war and weapons. Nuclear fallout everywhere.
- Happened: Every city was hit; the world is radioactive dust.
- Land/sky: Grey plains, glassy craters, dead forests, ash skies, radioactive dust storms.
- Regions/infra: Ground zeros ringed by flattened ruins, patched roads, bunkers, convoy routes.
- POIs: top: blast_crater, deep_bunker, command_bunker, military_base, nuclear_plant. NEW: shadow_wall, fallout_shelter. Inside: shelters with calendars, shadows burnt on walls, Geiger-lit lockers.
- Life/people: Mutated wildlife, gangs, bunker survivors.
- Hazards/rewards: Radiation zones; rad-proof gear.
- Fit: pre M/H > post L | ports C-E | U | S

**104. Mutant World** (`mutant_world`) - Group B, plague and bloom. Life gone wrong.
- Happened: A mutagen from a biolab, fallout or the Bloom twisted all life.
- Land/sky: Swollen trees, fused forests, wrong colours, sickly light.
- Regions/infra: Quarantine fences, overgrown biolabs, ruined farms.
- POIs: top: biolab, hospital, research_base, old_farm. NEW: bloom_garden, vat_hall. Inside: specimen rooms, grown-through labs, notes about "stable" strains.
- Life/people: Mutants with extra limbs and growths; hermit researchers.
- Hazards/rewards: Warned mutagen; rare organics.
- Fit: pre M/H > post L | ports C-E | U | no

**105. Never Settled** (`never_settled`) - Group G, quiet and beautiful. A wild world.
- Happened: No one ever came; the world is entirely natural.
- Land/sky: Full variety of untouched landforms, big skies.
- Regions/infra: None: no roads, no ruins, no wrecks; only terrain stamps and rivers.
- POIs: top: precursor_vault, landing_clearing. NEW: lone_beacon. Inside: a single pre-collapse survey beacon with a log.
- Life/people: Wildlife in full variety; no people.
- Hazards/rewards: Predators; pure resources.
- Fit: pre (none) > post (none) | ports 0 | R | S

**106. Ocean World** (`ocean_world`) - Group E, nature. Islands and sea.
- Happened: Water covers most of the world.
- Land/sky: Island chains, trenches, storms, blue-green light.
- Regions/infra: Island ports, offshore rigs, ferries, sunken highways.
- POIs: top: docks, scrappy_port, crashed_freighter. NEW: offshore_rig, barge_town, ferry_terminal. Inside: rig control rooms, drowned decks, wrecks.
- Life/people: Sea life, fisher-folk, pirates.
- Hazards/rewards: Open water; fold crystal in deep water.
- Fit: pre M/H > post L | ports C-E | U | S

**107. Ring Remnant** (`ring_remnant`) - Group K, Precursor. A megastructure.
- Happened: A Precursor band wraps the sky.
- Land/sky: Inner surface of a band, sky arching overhead.
- Regions/infra: Precursor causeways, vault doors, sluices.
- POIs: top: precursor_vault, alien_library, alien_cathedral. NEW: megalith_ring, ring_lock. Inside: control spires, glyph floors, silent guardians.
- Life/people: Guardians, a few seekers.
- Hazards/rewards: Traps; unique tech.
- Fit: pre (none) > post L | ports 0 | X | no

**108. Dome Colonies** (`dome_colonies`) - Group A, exodus. Abandoned domes.
- Happened: A harsh world colonised under domes, left behind.
- Land/sky: Barren plains, dust, low sun.
- Regions/infra: Dome clusters linked by tubes, rovers parked.
- POIs: top: research_lab, evacuation_camp, landing_clearing. NEW: habitat_dome, greenhouse_dome. Inside: dead gardens, bunk rows, last-day notes.
- Life/people: Few; squatters.
- Hazards/rewards: Thin air; sealed stores.
- Fit: pre H > post L | ports C-E | U | S

**109. Fire and Ice** (`fire_ice`) - Group E, nature reclaims. A world of zones.
- Happened: Natural: equatorial fire, polar ice, peaks glaciated, active volcanoes.
- Land/sky: Lava fields at the equator, ice caps and peak snow, smoking cones.
- Regions/infra: Roads around the volcanoes, polar stations.
- POIs: top: ice_outpost, research_base, geothermal_plant, old_mine. NEW: volcano_observatory. Inside: seismographs, lava-flow maps.
- Life/people: Heat-adapted fauna, ice beasts.
- Hazards/rewards: Eruptions; geothermal power.
- Fit: pre M > post L | ports D-E | U | S

**110. Vacated Vaults** (`vacated_vaults`) - Group K, Precursor. They left, and so should you.
- Happened: Precursor sites emptied before the Bloom.
- Land/sky: Empty plains with sealed domes.
- Regions/infra: Causeways, shrine halls.
- POIs: top: precursor_vault, alien_hive_ruin, alien_library. NEW: vault_gate. Inside: packed cases left behind.
- Life/people: Guardians.
- Hazards/rewards: Traps; relics.
- Fit: pre (none) > post L | ports 0 | X | no

**111. Prison Worlds** (`prison_worlds`) - Group C, war. Alien prisons.
- Happened: An alien regime used the world as a prison.
- Land/sky: Barren stone, gantries.
- Regions/infra: Cell blocks, wall lines.
- POIs: top: prison_block, vharn_outpost, vharn_sam_site. NEW: alien_cellblock. Inside: cells, chains, records.
- Life/people: Wardens, escaped prisoners.
- Hazards/rewards: Wardens; captive tech.
- Fit: pre H > post L | ports D-E | R | no

**112. Alien Shipyards** (`alien_shipyards`) - Group H, industry. Alien slipways.
- Happened: A shipyard world of the Vharn, half-built hulls on cradles.
- Land/sky: Slag plains, gantry cranes.
- Regions/infra: Cradle lines, rail.
- POIs: top: vharn_outpost, crashed_freighter, kessari_works. NEW: alien_slipway. Inside: hulls, jigs.
- Life/people: Vharn crews.
- Hazards/rewards: Guard crews; ship parts.
- Fit: pre H > post H | ports B-D | R | no

---

## Occupancy

Occupancy is a layer separate from layout. The same hospital layout is built once; a per-spawn occupancy roll decides who or what is in it, and the dressing follows. A theme gives a mix of shares (percent, summing to 100) for its places. "Empty" is a real result: nobody, nothing, no sound, only the dressing of the collapse. Shares apply per place; a few POI families override them (a nuclear plant leans machine or empty, a farm leans squatters).

| Code | Occupancy | What changes inside |
| --- | --- | --- |
| EMP | Empty | No spawns at all. Dust, dead light, no tracks. Loot is intact but untouched, so it is often the best loot. Still gets logs and the frozen-moment dressing. |
| NST | Nesters (creature nests) | Nests in rooms and vents, webbing and egg clusters, droppings, gnawed furniture, bones of prey, chewed doors, a smell sound-cue. Guarded brood rooms deeper in. |
| SHB | Turned (the slow dead) | Slumped figures in beds and corridors that rise when disturbed; barricades broken from inside, scratch marks, bodies in uniform, a last stand at a stairwell. |
| SCV | Scavengers passing | A few people, in and out: a camp fire in the ward, a tripod over ash, picked-over shelves, bedrolls, fresh boot tracks, a chalk arrow, a stash they will return for. Often neutral until threatened. |
| SQT | Squatters / settled survivors | A lived-in place: tidy bunks, cooking stove, water barrels, boarded windows, a notice board, a barter shelf. Friendly or wary; they trade. |
| GNG | Gang-held | Fortified: welded barricades, spikes, trophy walls, loot piles, a boss room, hostages or cages. Always hostile. |
| FAC | Faction-held | A core, patrols, sandbags, flags, supply crates, a working generator. Owned places only. |
| MCH | Machine-held | Sentries, cleaners and maintenance drones still running routines; lights on in some rooms; spotless floors beside decay. |
| CYB | Cyborgs | Half-machine survivors or converted staff: operating theatres turned into refit bays, spare limbs on racks, cables grown into walls, cold patrols that talk in fragments. |
| BOT | Robots gone feral | Old machines with broken orders, unlike MCH's tidy routines: scrap nests, dismantled furniture sorted into piles, machines stripping each other for parts. |
| HOL | Hollow-touched | Wrong geometry, extra doors, objects set out in patterns, whispers, stained walls, shadows that arrive late; creatures that mimic the place's old staff. |
| CLT | Cultists / pilgrims | Shrines, offerings, pilgrim cots, chalk or paint marks, robed figures who chant; rarely hostile unless a shrine is touched. |
| MUT | Mutated predators | Roaming rather than nesting: big prowlers, claw marks, torn mattresses, blood trails to a den. |

Rules for the generator: the roll is made once per place and stored with its seed; an empty place stays empty on revisit; a place may carry a "trace" of a second type (an EMP place with a dead campfire from long ago) for a story without a spawn; higher "ominous" depth biases toward NST, HOL and MUT whatever the mix.

### Mixed occupancy

A place can hold two groups at once. In the mix tables a pair written `A/B` is a mixed place: group A and group B both present, with the share counted once. A mixed place is rolled as a pair plus a relation, and the relation decides the layout of the spawns and the dressing:

| Relation | How they share the place | What shows |
| --- | --- | --- |
| Floors | Split by level: one group above, one below (scavengers hold the upper floors, nesters the basement; a sealed floor between them) | Barricaded stairwell, a trap-door rigged with cans, a rope ladder, the lower floors' smell rising |
| Front line | They face each other across a line (survivors barricaded in a wing, infested corridors beyond) | Sandbags, welded doors, scorched corridors, spent shells, a score of marks on a wall counting days |
| Hunting | One group preys on the other (gang hunting squatters, nests preying on scavengers, turned drawn to a camp) | Bodies of the hunted, bait, snares, blood trails, a camp fire gone out in a hurry |
| Wary truce | Two groups avoid each other (scavengers passing through a place held by machines) | Chalk marks showing safe routes, cleared and uncleared rooms, a toll box |
| Layers by age | The older group is gone, the newer sits on top (squatters in a dead gang's fort) | Two sets of marks, one old and rusted, one fresh |

Rules: a mixed place never pairs two groups that would not share a map (FAC with MUT is allowed; MCH with SQT is allowed; NST with NST is a single group). Loot sits on the dangerous side of the line. A mixed place keeps its relation on revisit, but the front line can shift when the player kills one side.

### Example: one hospital, eight worlds

Same layout, different occupancy. Nesters: egg sacs in the wards, webbed lifts, a gnawed gurney trail. Turned: bodies in beds that sit up, a barricaded mortuary door scratched from inside. Gang-held: welded stair gates, a loot pile in pharmacy, patients' wristbands as trophies. Empty: dust, a clock stopped, pill cups on trays, nothing moves. Passing scavengers: a fire in the cafeteria, bedrolls, the pharmacy picked over, boot tracks leading out. Squatters: a ward turned dormitory, a garden on the roof, a barter shelf. Machine-held: cleaner drones, a triage terminal that still calls numbers. Hollow-touched: a ward with one more bed than it should have, a nurses' roster in a hand that is not a hand.

### Occupancy mix by theme

Shares are percentages for places in that theme and sum to 100. Codes are in the table above.

| # | Theme | Mix |
| --- | --- | --- |
| 1 | long_queue | EMP 35, SCV 25, GNG 15, SQT 15, HOL 5, MUT 5 |
| 2 | lifeboat | SCV 35, EMP 30, NST 15, GNG 10, SQT 10 |
| 3 | last_train | EMP 30, NST 20, SCV 15, SCV/NST 10, GNG 15, MUT 5, HOL 5 |
| 4 | empty_ark | EMP 45, SQT 20, MCH 15, HOL 10, SCV 10 |
| 5 | hush_suburbs | EMP 60, HOL 15, SQT 10, SCV 10, NST 5 |
| 6 | parade_ground | EMP 35, SCV 25, SQT 25, NST 10, GNG 5 |
| 7 | closed_season | EMP 40, MCH 20, SCV 20, GNG 10, NST 10 |
| 8 | deserted_capital | MCH 30, EMP 30, FAC 15, NST 10, SCV 10, GNG 5 |
| 9 | lockin | EMP 30, SHB 20, MUT 15, SCV 15, GNG 10, NST 10 |
| 10 | walked_out | EMP 30, MCH 20, GNG 15, FAC 15, NST 10, SCV 10 |
| 11 | quarantine | EMP 30, SHB 25, MCH 15, SQT/SHB 10, HOL 10, NST 10 |
| 12 | fever_jungle | NST 30, MUT 20, EMP 20, SQT/MUT 10, HOL 10, SHB 10 |
| 13 | sleepwalkers | SHB 40, SQT 25, EMP 25, HOL 10 |
| 14 | pollen_wars | NST 30, EMP 30, SQT 20, MCH 10, MUT 10 |
| 15 | blightland | SCV 30, GNG 25, SQT 25, EMP 20 |
| 16 | bloom_drowned | HOL 35, MUT 30, EMP 15, CLT 10, SQT 10 |
| 17 | coughing_hills | SCV 30, EMP 30, SQT 20, GNG 10, NST 10 |
| 18 | mercy | EMP 40, SQT 25, SCV 10, SQT/SHB 10, HOL 10, SHB 5 |
| 19 | flesh_gardens | MUT 35, NST 20, MCH 20, EMP 15, HOL 10 |
| 20 | clinic_edge | SQT 45, EMP 20, SCV 20, FAC 15 |
| 21 | glass_plains | EMP 40, GNG 20, MUT 15, SCV 15, HOL 10 |
| 22 | rust_armies | MCH 30, EMP 30, SCV 20, GNG 10, FAC 10 |
| 23 | wall_cities | SQT 35, GNG 25, FAC 15, EMP 15, SCV 10 |
| 24 | missile_fields | EMP 40, MCH 20, SQT 15, SCV 15, GNG 10 |
| 25 | trench_moon | EMP 35, SHB 20, SCV 20, GNG 15, HOL 10 |
| 26 | peace_treaty | EMP 50, MCH 20, FAC 15, SCV 15 |
| 27 | siege_orbitals | EMP 35, SCV 30, NST 15, FAC 10, GNG 10 |
| 28 | decommissioned | EMP 40, SCV 40, MCH 10, SQT 10 |
| 29 | sentry_world | MCH 55, EMP 25, FAC 10, SCV 10 |
| 30 | big_gun | EMP 40, MCH 25, SCV 20, HOL 15 |
| 31 | road_warrens | GNG 55, SCV 20, EMP 15, SQT 10 |
| 32 | water_tax | GNG 35, SQT 30, EMP 20, SCV 15 |
| 33 | salt_and_bone | EMP 40, SCV 30, NST 20, GNG 10 |
| 34 | long_winter | EMP 35, SQT 30, MUT 15, SCV 15, NST 5 |
| 35 | ember_world | EMP 40, NST 25, MUT 15, SCV 15, GNG 5 |
| 36 | bandit_kingdoms | GNG 50, SQT 25, SCV 15, EMP 10 |
| 37 | pit_worlds | SQT 25, NST 15, EMP 20, GNG 15, SQT/NST 10, MUT 10, HOL 5 |
| 38 | floodlines | SQT 35, EMP 30, NST 20, SCV 15 |
| 39 | last_garden | SQT 55, EMP 15, FAC 15, GNG 15 |
| 40 | hunger_roads | GNG 35, SCV 30, EMP 20, SQT 15 |
| 41 | teeth_trees | NST 35, MUT 30, EMP 25, SQT 10 |
| 42 | stalker_reefs | NST 40, EMP 30, MUT 20, SCV 10 |
| 43 | hive_plains | NST 55, EMP 20, MUT 15, SQT 10 |
| 44 | maw_dunes | EMP 45, NST 30, SCV 15, MUT 10 |
| 45 | thorn_wall | MUT 30, EMP 30, SQT 20, GNG 10, NST 10 |
| 46 | storm_spires | EMP 50, NST 20, MCH 20, SCV 10 |
| 47 | titans_meadows | EMP 45, SQT 25, NST 15, SCV 15 |
| 48 | spore_caverns | NST 40, MUT 25, EMP 25, HOL 10 |
| 49 | acid_reaches | EMP 40, NST 25, SCV 20, MCH 15 |
| 50 | kelp_sea | SQT 30, EMP 30, NST 25, SCV 15 |
| 51 | wrong_stars | EMP 40, HOL 20, CLT 15, SCV 10, NST 10, SQT 5 |
| 52 | folded_town | EMP 70, HOL 30 |
| 53 | hollow_orchards | CLT 25, EMP 25, HOL 25, SQT 15, NST 10 |
| 54 | whisper_flats | EMP 50, SQT 25, HOL 15, SCV 10 |
| 55 | mirror_cities | EMP 50, HOL 30, SCV 10, NST 10 |
| 56 | choir_canyon | EMP 40, CLT 30, SQT 20, HOL 10 |
| 57 | clockwork_hours | MCH 50, EMP 30, SQT 15, SCV 5 |
| 58 | slow_dead | SHB 35, EMP 20, SQT 20, SQT/SHB 10, SCV 5, HOL 10 |
| 59 | eye_bloom | FAC 25, SQT 20, FAC/HOL 10, SQT/HOL 10, HOL 15, EMP 10, SCV 10 |
| 60 | dream_salt | EMP 50, HOL 20, SCV 15, NST 15 |
| 61 | black_library | EMP 40, CLT 25, MCH 20, SCV 15 |
| 62 | lamplight | EMP 55, SQT 30, HOL 15 |
| 63 | glass_garden | EMP 65, SQT 20, SCV 15 |
| 64 | moon_lakes | EMP 75, SQT 20, SCV 5 |
| 65 | terraced | EMP 45, SQT 40, SCV 15 |
| 66 | wildflower | EMP 60, SQT 25, SCV 15 |
| 67 | aurora_shelf | EMP 55, SQT 15, NST 15, SCV 15 |
| 68 | bells | EMP 45, SQT 35, SCV 20 |
| 69 | orchard_moon | EMP 50, SQT 35, SCV 15 |
| 70 | amber_steppe | EMP 40, SQT 40, SCV 20 |
| 71 | hot_springs | EMP 50, SQT 30, SCV 20 |
| 72 | starfield | EMP 65, SQT 20, SCV 15 |
| 73 | foundry_belt | EMP 30, SCV 30, GNG 20, MCH 10, SQT 10 |
| 74 | drone_orchard | MCH 55, EMP 30, SCV 15 |
| 75 | power_down | EMP 40, SCV 30, GNG 15, MCH 15 |
| 76 | elevator_shadow | EMP 40, SCV 30, FAC 15, MCH 15 |
| 77 | cannery_world | EMP 40, NST 25, SCV 20, GNG 15 |
| 78 | data_tombs | MCH 40, EMP 40, SCV 20 |
| 79 | shipbreakers | SCV 40, EMP 25, GNG 20, SQT 15 |
| 80 | maglev_spine | EMP 45, SCV 25, SQT 15, MCH 15 |
| 81 | pipeline_world | EMP 35, GNG 30, SCV 25, SQT 10 |
| 82 | geothermal | EMP 50, MCH 20, SCV 20, NST 10 |
| 83 | souvenir | EMP 45, SCV 30, SQT 25 |
| 84 | bureaucracy | EMP 50, MCH 35, SCV 15 |
| 85 | pirate_radio | SQT 40, EMP 40, SCV 20 |
| 86 | zoo_world | NST 35, EMP 35, SCV/NST 10, SCV 10, SQT 10 |
| 87 | artist_colony | EMP 50, SQT 35, SCV 15 |
| 88 | sporting | EMP 30, GNG 30, SQT 25, SCV 15 |
| 89 | casino_dunes | EMP 35, GNG 30, SCV 20, MCH 15 |
| 90 | school_holidays | EMP 60, SQT 25, SCV 15 |
| 91 | ghost_harbour | EMP 40, SCV 30, NST 15, GNG 15 |
| 92 | library_towns | EMP 55, SQT 30, SCV 15 |
| 93 | diesel_frontier | SQT 45, EMP 25, GNG 15, SCV 15 |
| 94 | castle_worlds | SQT 40, EMP 25, GNG 20, SCV 15 |
| 95 | barrow_downs | EMP 45, HOL 30, SCV 25 |
| 96 | pilgrim_roads | SQT 50, EMP 35, SCV 15 |
| 97 | precursor_shelf | MCH 40, EMP 40, HOL 20 |
| 98 | lighthouses | EMP 50, SQT 40, SCV 10 |
| 99 | bone_dig | EMP 40, SQT 35, NST 15, SCV 10 |
| 100 | chime_coast | EMP 55, SQT 30, SCV 15 |
| 101 | sunken_raiders | NST 25, NST/SCV 15, GNG 20, EMP 20, SQT 10, MUT 10 |
| 102 | overgrown_metropolis | NST 25, MUT 20, EMP 20, SQT/MUT 15, SCV 10, SQT 10 |
| 103 | waste_world | EMP 25, GNG 25, MUT 15, SQT/MUT 15, SQT 10, SCV 10 |
| 104 | mutant_world | MUT 35, NST 20, EMP 15, MUT/SQT 10, HOL 10, SHB 10 |
| 105 | never_settled | EMP 70, NST 30 |
| 106 | ocean_world | SQT 30, EMP 30, NST 20, SCV 10, GNG 10 |
| 107 | ring_remnant | MCH 40, EMP 40, HOL 20 |
| 108 | dome_colonies | EMP 45, SQT 25, MCH 15, SCV 15 |
| 109 | fire_ice | EMP 40, NST 25, SQT 15, MUT 10, SCV 10 |
| 110 | vacated_vaults | EMP 55, MCH 30, HOL 15 |
| 111 | prison_worlds | FAC 30, MCH 20, GNG 20, EMP 20, SHB 10 |
| 112 | alien_shipyards | FAC 35, MCH 25, EMP 25, SCV 15 |

---

## How the generator uses a theme as data

A theme is a record, never code. World generation picks a theme after the world profile is rolled, filtering by pre and post tech band, starport class and the rim/start rule, then weighting by rarity. Everything below is a bias on existing generators.

- **Land and sky** become weights on landform, biome, weather and palette presets, plus any modifier flags (glass ground, bent trees, extra moons).
- **Regions** become region-planner stamps: which region kinds to place, how many, how big, and the road/rail/power style (a style id the infrastructure pass already understands).
- **POIs** become a weight table over families (existing and new), plus per-family state weights (active, abandoned, infested, ruined) that replace the family's defaults for this world.
- **Occupancy** is a second weight table, rolled per place with the place's seed. A family may override it (a fusion plant never rolls NST in a machine theme).
- **Dressing** is a list of tags (for example `queue`, `famine`, `festival`) that decorations, logs and loot tables key on.
- **Life** is a list of wildlife hints and a Hollow level curve by distance from the rim.

Suggested compact format, one block per theme (plain text like the family files):

```
theme long_queue
  name The Long Queue
  group exodus
  rarity common
  start yes
  pre M H
  post L
  ports A B C
  land plains hills | weather clear dust | palette gold dust
  region port_hub 1 | jammed_highway 6 | scrappy_pad 8 | camp 4
  style road jammed | rail derelict | power port_only
  poi abandoned_spaceport 3 | evacuation_camp 3 | scrappy_port 3 | processing_hall 2 | luggage_field 2 | crashed_freighter 2
  state abandoned 6 ruined 2 infested 1 active 1
  occ EMP 35 SCV 25 GNG 15 SQT 15 HOL 5 MUT 5
  tags queue exodus luggage
  life grazers scavenger_packs
  hollow 0.1
```

Blocks are read at startup; unknown POI names are skipped with a warning, so new families can be added a theme at a time. A "mixed" world takes a second theme block for one region and blends its tables at region borders.

---

## The 10 themes to build first, and why

Chosen for three things: they reuse the most existing families, they look and play unlike each other in the first minute, and together they cover every occupancy type.

1. **The Long Queue (1).** The owner's exodus idea made literal; reuses ports, camps and freighters; the jammed highways are visible from orbit. Start world.
2. **Road Warrens (31).** The owner's brutal gang world. Shows GNG occupancy and vehicle chases; needs only three new families.
3. **Glass Plains (21).** The owner's nuclear idea; strongest silhouette; reuses craters and bunkers.
4. **Fever Jungle (12).** The owner's virus and death-flora idea; the only theme that tests NST and MUT with flora hazards at once.
5. **Hush Suburbs (5).** A quiet start world with no fighting; proves "empty" is a good result and tests sparse-loot dressing.
6. **Quarantine (11).** Uses hospitals, biolabs and camps; makes the Hollow Bloom feel like a cause; adds the walled-zone look.
7. **The Last Train (3).** Rail-driven region planner; exercises the infrastructure code harder than any other theme.
8. **Salt and Bone (33).** A completely different palette and a harbour dry on a plain; cheap, striking, start-capable.
9. **Sentry World (29).** Tests machine occupancy and the Kessari; non-start, so it gives a goal.
10. **The Folded Town (52).** The cheapest weird theme (a single place type plus rules); sets the tone for the Hollow and tests HOL occupancy.

Close behind: Water Tax (32), The Slow Dead (58), Clinic at the Edge (20), Empty Ark (4).

## New POI families, by number of themes that want them

Counted from the NEW lines of each theme entry. Build the top of the list first.

| New family | Themes |
| --- | --- |
| bloom_garden | 4 |
| observatory | 3 |
| motor_pool | 3 |
| ministry_hall | 3 |
| lighthouse | 3 |
| hollow_shrine | 3 |
| hilltop_keep | 3 |
| ferry_terminal | 3 |
| barge_town | 3 |
| archive_vault | 3 |
| wrong_house | 2 |
| water_tower_keep | 2 |
| vat_hall | 2 |
| trench_line | 2 |
| toll_gate | 2 |
| time_pocket | 2 |
| theme_park | 2 |
| spa_springs | 2 |
| sound_garden | 2 |
| ski_lodge | 2 |
| shopping_mall | 2 |
| shadow_wall | 2 |
| seed_vault | 2 |
| school_campus | 2 |
| rendering_plant | 2 |
| ration_depot | 2 |
| radio_mast_site | 2 |
| pier | 2 |
| ore_sorting_hall | 2 |
| orchard | 2 |
| offshore_rig | 2 |
| museum | 2 |
| monastery | 2 |
| megalith_ring | 2 |
| hotel_tower | 2 |
| greenhouse_dome | 2 |
| gang_arena | 2 |
| fruiting_hall | 2 |
| estate_house | 2 |
| drowned_cistern | 2 |
| drone_hive | 2 |
| data_centre | 2 |
| crystal_garden | 2 |
| church_chapel | 2 |
| casino_resort | 2 |
| caravan_stop | 2 |
| breaker_yard | 2 |
| bone_dig_site | 2 |
| bloom_scar | 2 |
| beached_liner | 2 |
| battlefield_memorial | 2 |
| zoo_menagerie | 1 |
| yurt_camp | 1 |
| wreck_beach | 1 |
| weather_engine | 1 |
| water_mill | 1 |
| walled_orchard | 1 |
| wall_city | 1 |
| volcano_observatory | 1 |
| vault_gate | 1 |
| undercity_district | 1 |
| treehouse_village | 1 |
| tomb_mound | 1 |
| terminus_station | 1 |
| sunk_town | 1 |
| stilt_village | 1 |
| stage_hall | 1 |
| stadium_camp | 1 |
| space_elevator_base | 1 |
| sorting_gate | 1 |
| sleeper_ward | 1 |
| sleeper_barracks | 1 |
| signal_box_keep | 1 |
| sentry_line | 1 |
| salt_works | 1 |
| routine_loop_street | 1 |
| ring_lock | 1 |
| rail_wreck | 1 |
| quarantine_wall | 1 |
| puppet_theatre | 1 |
| pump_station | 1 |
| public_library | 1 |
| processing_hall | 1 |
| pilgrim_hostel | 1 |
| orbital_wreck_site | 1 |
| mound_city | 1 |
| mortuary_deep | 1 |
| monorail_station | 1 |
| missile_silo | 1 |
| mirror_hall | 1 |
| metro_station | 1 |
| maze_manor | 1 |
| mask_depot | 1 |
| maglev_depot | 1 |
| luggage_field | 1 |
| lone_beacon | 1 |
| launch_rail | 1 |
| ice_cutters | 1 |
| hospice_ward | 1 |
| highway_interchange | 1 |
| hermit_cabin | 1 |
| harvester_field | 1 |
| habitat_dome | 1 |
| geothermal_plant | 1 |
| fuel_fort | 1 |
| front_checkpoint | 1 |
| fan_tower | 1 |
| fallout_shelter | 1 |
| fairground | 1 |
| dust_mill | 1 |
| drone_factory | 1 |
| debris_camp | 1 |
| dam_hydro | 1 |
| company_town | 1 |
| colony_ship_wreck | 1 |
| clock_tower_town | 1 |
| cemetery_vault | 1 |
| cannery | 1 |
| burn_town | 1 |
| burial_pits | 1 |
| breech_hall | 1 |
| border_checkpoint | 1 |
| bog_wreck_field | 1 |
| barracks_town | 1 |
| artist_colony | 1 |
| ark_dome | 1 |
| ammunition_hall | 1 |
| alien_slipway | 1 |
| alien_cellblock | 1 |
| aid_station | 1 |
| aero_club | 1 |
| acid_pool_works | 1 |
