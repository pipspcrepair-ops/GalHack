# Layer coverage: what world-layers.txt names against what the game builds

Audited on the merged batch build (`data/world-layers.txt`, 1,182 lines). "Consumed" means the game reads it: a family that exists, a `bias` key something reads, a dress tag or selector in `world-dressing.txt` or code that uses it, an occupant code, or a `like` whose target value is itself built.

## Off list (never rolled, never offered; forced values still work)

Owner decision: a value whose signature is not built is marked `off <reason>` in the data. The roller skips it, the new-game pickers do not list it, and `--layer` or a test can still force it. 221 values are off. Weak values stay on and are listed after this table.

The dead references of the values that stay on (247 families, 164 tags, 137 likes, 14 orbit words, 12 stamps) were moved out of their working fields into a `later` field, so nothing is lost and a new check (LayersTest, "values not built yet") can say every value that is on names only what exists. The data header still says the file is written from `docs/world-layers.md` by `tools/layers_from_doc.py`; neither is in this repository, so the `off` and `later` fields have to be carried into that source or the next regeneration drops them.

| Layer | Count | Reason | Values |
|---|---|---|---|
| build | 25 | nothing reads it: no family, bias, tag, selector or code uses any of it | arcology, army_prefab, brick_terrace, buried_warren, concrete_slab, container_stack, corporate_block, earth_mound, felt_canvas, gabled_timber, glass_tower, ice_block, pastel_estate, pressure_dome, resort_curve, ribbed_adobe, scrap_weld, sealed_module, ship_hull_town, stilt_timber, stone_keep, tiled_courtyard, undersea_domes, vharn_grown, white_clinical |
| fauna | 26 | nothing reads it: no family, bias, tag, selector or code uses any of it | blind_crawlers, bower_builders, burrowers, cliff_fliers, dusk_climbers, echo_birds, giants, heat_lovers, hive_castes, ice_beasts, lamp_shy, light_floaters, lode_grazers, lost_migrants, metal_eaters, pollinators, reef_stalkers, sand_swimmers, sea_life, slowstones, static_nesters, swarms, vacuum_rarities, waders, web_liners, wreck_hermits |
| feature | 54 | nothing reads it: no family, bias, tag, selector or code uses any of it | active_volcano, beached_liners, beehive_domes, border_line, burning_seams, buttes, cliff_pulpits, columnar_towers, coral_land, crater_lake, crystal_fields, crystal_ridges, debris_line, dismantled_arsenal, facing_suits, failed_base, fallen_elevator, folded_district, fungal_deep, geyser_basin, giant_bones, giant_statues, glowing_caves, great_falls, herd_giants, high_marks, hollow_basin, hollow_patch, hoodoos, kelp_mats, launch_ramp, lone_rock, long_furrow, machine_drifts, mast_forest, mega_dam, methane_lakes, mirror_district, mirror_lakes, natural_arches, petrified_trunks, pipe_racks, rainbow_strata, rock_spires, sea_stacks, singing_rock, standing_stones, still_wave, stopped_trains, sunk_town, survey_beacon, tar_flats, thorn_maze, wreck_beach |
| flora | 2 | nothing reads it: no family, bias, tag, selector or code uses any of it | kelp_forest, mangrove_reed |
| hazard | 39 | nothing reads it: no family, bias, tag, selector or code uses any of it | arc_flash, bad_air, boggy_footing, cold, collapsing_floors, corrosion, crevasses, deep_water, disorientation, entanglement, eruptions, failing_seals, falling_debris, falling_facades, getting_lost, glare, gravity_pockets, grey_front, gusts, heat, lava, lightning, meteors, misplaced_things, molten_pits, night_swarms, quakes, scalding, sharp_ground, sinkholes, sound_lures, strange_chemistry, surges, thin_air, tides, time_slips, unstable_ground, unstable_wrecks, vacuum |
| ran | 11 | nothing reads it: no family, bias, tag, selector or code uses any of it | absentees, alien_regime, company, elders, elected, founders, great_houses, guilds, machines, ministry, syndicate |
| running | 5 | nothing reads it: no family, bias, tag, selector or code uses any of it | banked_furnaces, billboards, hour_horn, long_life_lamps, weather_plant |
| sky | 23 | nothing reads it: no family, bias, tag, selector or code uses any of it | aurora, broken_stations, day_eclipse, dead_train, double_sun, drifting_hulks, dust_lane, falling_night, giant_overhead, glimpse_beyond, hard_stars, light_on_moon, many_moons, one_big_moon, pinprick_sun, ring_arc, seam, second_noon, stuck_sun, stutter_sun, swollen_sun, visitor_moon, wrong_stars |
| sound | 10 | nothing reads it: no family, bias, tag, selector or code uses any of it | creaking_ice, crystal_hum, generators, hour_horn, rail_hum, soft_drone, stone_flutes, surf, wind_chimes, wind_low |
| weather | 7 | nothing reads it: no family, bias, tag, selector or code uses any of it | fog_banks, gold_evenings, iron_drizzle, morning_mist, pollen_clouds, seed_fall, snowfall |
| fall | 1 | signature unbuilt: bloom_scar, observatory, sky.seam, bent_rooms | dimensional_tear |
| fall | 1 | none of its places exist: wrong_house, mirror_hall, time_pocket | folding |
| fall | 1 | none of its places exist: data_centre, public_library, archive_vault, ministry_hall | memory_rot |
| fall | 1 | none of its places exist: time_pocket, wrong_house, observatory | time_stutter |
| feature | 1 | none of its places exist: monastery | bell_towers |
| feature | 1 | none of its places exist: colony_ship_wreck, ark_dome, seed_vault | colony_hull |
| feature | 1 | none of its places exist: observatory | domed_observatories |
| feature | 1 | none of its places exist: barge_town, drowned_cistern | drowned_city |
| feature | 1 | none of its places exist: shadow_wall | glass_plains |
| feature | 1 | none of its places exist: cemetery_vault | graveyard_hills |
| feature | 1 | none of its places exist: highway_interchange | jam_highways |
| feature | 1 | none of its places exist: hollow_shrine | leaning_world |
| feature | 1 | none of its places exist: quarantine_wall, sorting_gate | quarantine_walls |
| feature | 1 | none of its places exist: sentry_line | sentry_lines |
| feature | 1 | none of its places exist: timber_fleet | timber_ships |
| feature | 1 | none of its places exist: trench_line, battlefield_memorial, motor_pool | trench_lines |
| theme | 1 | signature unbuilt: feature.buttes, feature.rock_spires, feature.columnar_towers, feature.natural_arches | grand_scenery |
| theme | 1 | none of its places exist: fuel_fort, gang_arena, caravan_stop | road_warrens |
| theme | 1 | none of its places exist: wall_city, hilltop_keep, toll_gate | wall_cities |

Sky seam, the wall cities and the rest the owner looked for are in this list. Weak values left on:

- **theme** (10): hush_suburbs, moon_lakes, wrong_stars, folded_town, lamplight, pit_worlds, teeth_trees, titans_meadows, glass_garden, empty_ark
- **fall** (51): orbital_bombardment, chemical_war, kinetic_strikes, drone_wars, sleep_sickness, husks_fall, sterility, drought, rising_seas, long_winter, runaway_heat, great_quake, air_loss, poison_air, meteor_swarm, pollinator_loss, grey_goo, grid_cascade, ai_silence, terraform_failure, weather_failure, teleport_accident, drive_disaster, cloning_mishap, debris_cascade, lockdown, economic_collapse, faith_takeover, enclave_wars, mutiny, great_migration, dark_patch, gravity_shift, rogue_body, wormhole_tides, slow_fade, emigration, neglect, supply_cutoff, ageing_colony, vanishing, hollow_front, replacement, long_sleep, liquidation, seam_fire, magnetic_flip, mirror_burn, sinkage, last_ark, first_contact
- **moment** (9): rush_hour, shift_change, deep_night, festival, term_time, harvest, election, signing_day, drill
- **creed** (11): stampers, knockers, horn_pausers, cargo_builders, sealed_believers, coreward_leaners, sky_counters, seed_keepers, ancestor_tenders, machine_kin, mender_faith
- **presence** (23): gangs, warlords, resistance, smugglers, water_lords, caravaners, fisher_folk, fur_folk, pale_folk, hermits, keepers, pilgrims, cultists, debris_diggers, bandit_riders, hunters, routine_walkers, husk_nights, frenzied, duplicates, machine_lords, hollow_shore, listening
- **running** (12): timers, transit_loops, farm_machines, factory_lines, vats, sentries, clerks, cleaners, broadcast, automata, life_support, beacons
- **feature** (14): hive_mounds, green_valley, big_gun, launch_fields, inward_fences, sunk_ships, stranded_ships, turbine_ranks, pylon_forest, dead_city_ash, overgrown_city, visitor_centre, simulated_voyage, precursor_plates
- **ran** (2): generals, faith

## 1. Summary

**2571 references in total, 1809 consumed (70%), 762 not.** By kind (consumed of total): like 372/531, poi 237/514, bias 496/496, tag 106/327, occ 320/320, region 78/81, industry 38/68, style 49/60, orbit 42/58, hub 24/48, stamp 19/36, farm 6/10, plant 9/9, needs 8/8, depot 5/5. Of 44 themes, 31 show, 10 are weak, 3 are missing; of 82 falls, 27 show, 51 weak, 4 missing.

Whole layers with no consumer at all: **sky** (no code draws a rolled sky value; the sky is the planet's own star system), **build** (the `kit` field is parsed and never read), **fauna ids** (creatures read only `bias`), **hazard** (39 of 54 have no reader), **sound** values beyond bias, **ran/running** values beyond their tags. Their only effect is steering other rolls through `like`. `mod` and `land` values are labels beside the planet generator's own modifiers and landforms; the roll never changes the planet, so they were not judged.

## 2. Themes

Signature = identity or name, its places, and its strongest leans. Built = consumed references; missing = unconsumed ones.

| Value | Signature | Built | Missing | Verdict |
|---|---|---|---|---|
| plain | Nothing pulls this world one way, and what it is comes from its parts alone | - | - | shows |
| long_queue | Everyone here wanted the same one door out; leans: infra.port_funnel, fall.exodus | fall.exodus, fall.decreed_exodus, fall.tide_of_refugees, infra.port_funnel (+6) | weather.gold_evenings | shows |
| hush_suburbs | Nobody home and nothing broken, and the quiet is the unease; leans: fall.vanishing, running.long_life_lamps | fall.vanishing, fall.walked_out_apoc, moment.ordinary_evening, sound.silence (+2) | running.long_life_lamps, build.pastel_estate | weak |
| parade_ground | The happiest day a world ever had, stopped; leans: moment.festival, moment.match_day | moment.festival, moment.match_day, trade.sport, trade.roadside (+3) | - | shows |
| closed_season | The rich left first and best, and it still looks ready for them; leans: trade.resort, trade.gaming | trade.resort, trade.gaming, moment.holiday, fall.decreed_exodus (+4) | build.resort_curve | shows |
| deserted_capital | An order is still being carried out, and nobody remembers giving it; leans: trade.administration, running.clerks | trade.administration, running.clerks, fall.ai_silence, fall.decreed_exodus (+4) | ran.ministry, build.concrete_slab | shows |
| clockwork_hours | Everything runs, slightly off, for no one; leans: running.automata, running.farm_machines | running.automata, running.farm_machines, running.timers, running.transit_loops (+3) | sound.generators | shows |
| sentry_world | Everything is guarded and nobody is home; leans: running.sentries, feature.sentry_lines | running.sentries, feature.sentry_lines, trade.military, fall.drone_wars (+3) | - | shows |
| mercy | People stayed to look after other people, long past any sense in it; leans: trade.medicine, running.carers | trade.medicine, running.carers, presence.keepers, fall.plague (+3) | build.white_clinical, weather.morning_mist | shows |
| wall_cities | Every settlement is a fortress against its neighbours; places: wall_city, hilltop_keep, toll_gate; leans: infra.toll_roads, build.stone_keep | infra.toll_roads, presence.warlords, presence.militias, fall.enclave_wars (+2) | wall_city, hilltop_keep, toll_gate, build.stone_keep, ran.great_houses | missing |
| road_warrens | Order failed and the survivors became convoys, and fuel is the only law; places: fuel_fort, gang_arena, caravan_stop; leans: hazard.gang_ambush, presence.gangs | presence.gangs, infra.road_web, trade.fuel, fall.supply_cutoff (+5) | fuel_fort, gang_arena, caravan_stop | missing |
| water_tax | One scarce thing rules, and whoever holds it holds the world; leans: feature.green_valley, fall.drought | fall.drought, fall.famine, fall.monoculture_failure, presence.water_lords (+4) | - | shows |
| foundry_belt | Work without end, stopped mid-shift, and the furnaces never quite cold; leans: trade.heavy_industry, trade.breaking | trade.heavy_industry, trade.breaking, fall.general_stoppage, fall.poison_air (+5) | running.banked_furnaces | shows |
| moon_lakes | Beautiful, lightly lived in and in no hurry; leans: phase.spared, feature.mirror_lakes | phase.spared, fall.slow_fade, fall.emigration, trade.subsistence (+3) | sky.one_big_moon, feature.mirror_lakes, moon_lakes_set | weak |
| overgrown_metropolis | A city to the horizon, in leaf; leans: type.city_covered, feature.overgrown_city | type.city_covered, feature.overgrown_city, flora.reclaiming_forest, infra.skyways | fauna.cliff_fliers | shows |
| pilgrim_roads | A world organised around walking to something; leans: trade.pilgrimage, infra.pilgrim_ways | trade.pilgrimage, infra.pilgrim_ways, fall.faith_takeover, ran.faith (+4) | - | shows |
| library_towns | What was known is being kept, whether or not anyone can still use it; leans: trade.learning, trade.archives | trade.learning, trade.archives, ran.scholars, fall.memory_rot (+2) | - | shows |
| lighthouses | One keeper, one light, still going, for nobody in particular; leans: running.beacons, running.broadcast | running.beacons, running.broadcast, presence.keepers, sound.radio_murmur (+1) | feature.mast_forest | shows |
| wrong_stars | The numbers have stopped adding up, and the careful people noticed first; leans: sky.wrong_stars, sky.stutter_sun | trade.star_survey, feature.domed_observatories, creed.sky_counters, fall.time_stutter (+1) | sky.wrong_stars, sky.stutter_sun, sky.visitor_moon, feature.mast_forest, hazard.sound_lures | weak |
| folded_town | A place that doesn't quite close; leans: fall.folding, feature.folded_district | fall.folding, weather.calm, sound.silence | feature.folded_district, feature.mirror_district | weak |
| hollow_orchards | Land that has started pointing at something, and people who have started listening; leans: feature.leaning_world, feature.singing_rock | feature.leaning_world, flora.bent_coreward, creed.coreward_leaners, presence.pilgrims (+1) | feature.singing_rock | shows |
| lamplight | One lit window in each town, and nobody says who keeps it; leans: creed.lamp_keepers, running.long_life_lamps | creed.lamp_keepers, lamp_rooms | running.long_life_lamps, weather.morning_mist, sound.soft_drone | weak |
| souvenir | A world made to be looked at, still smiling; leans: trade.roadside, trade.art | trade.roadside, trade.art, marks.mural_walls, palette.gold_dust | feature.giant_statues | shows |
| trench_moon | A stalemate cut so deep into the land that nobody can leave it; leans: feature.trench_lines, feature.machine_drifts | feature.trench_lines, fall.civil_war, fall.invasion, hazard.mines (+3) | feature.machine_drifts | shows |
| eye_bloom | A world with days left, and everyone in it knows; leans: phase.falling, fall.hollow_front | phase.falling, fall.hollow_front, marks.warning_stripes, sound.distant_guns | - | shows |
| diesel_frontier | A frontier that never reached the modern day and didn't need a collapse; leans: phase.spared, infra.diesel_rail | phase.spared, infra.diesel_rail, trade.mining, trade.herding (+2) | build.gabled_timber | shows |
| pit_worlds | The surface is the lid, and everything that matters is below; leans: build.buried_warren, infra.deep_tunnels | infra.deep_tunnels, presence.pale_folk, feature.sinkhole_chain, type.shell | build.buried_warren, feature.fungal_deep | weak |
| teeth_trees | The land itself hunts; leans: flora.carnivorous, hazard.ambush_plants | flora.carnivorous, flora.thorn_lanes, feature.hive_mounds, hazard.ambush_plants | fauna.hive_castes, fauna.sand_swimmers, fauna.reef_stalkers, feature.thorn_maze, feature.coral_land | weak |
| titans_meadows | Everything here is bigger than you, and mostly doesn't care; leans: fauna.giants, feature.herd_giants | trade.digs, fauna.burden_giants | fauna.giants, feature.herd_giants, feature.giant_bones | weak |
| glass_garden | Quiet wonder, with hardly anyone left to see it; leans: feature.crystal_fields, palette.prismatic | trade.star_survey, trade.research, palette.prismatic | feature.crystal_fields, sky.aurora, sky.hard_stars, sound.crystal_hum | weak |
| old_ground | Far older than anyone here, and not finished; leans: feature.standing_stones, feature.precursor_plates | feature.precursor_plates, feature.vault_field, marks.glyph_marks, presence.precursor_guardians | feature.standing_stones | shows |
| empty_ark | The founders' order is intact, and the people walked out of it by plan; leans: feature.colony_hull, ran.founders | feature.colony_hull, fall.walked_out_apoc | ran.founders, build.ship_hull_town | weak |
| waiting_world | Everything was built and made ready, and the settlers never came; leans: feature.waiting_town, moment.opening_day | feature.waiting_town, moment.opening_day, running.cleaners, running.timers (+2) | - | shows |
| test_range | This place was built to be destroyed, and parts of it still are; leans: feature.dummy_town, feature.warning_country | feature.dummy_town, feature.warning_country, feature.missile_grid, ran.generals (+2) | - | shows |
| memorial_world | The whole world has been made into its own grave marker, and it's tended daily; leans: marks.memorial_names, creed.ancestor_tenders | phase.recovered, phase.mending, marks.memorial_names, creed.ancestor_tenders (+2) | - | shows |
| liquidation | Someone is selling the place out from under itself, room by room; leans: fall.liquidation, marks.repossession_tags | fall.liquidation, marks.repossession_tags, trade.mining, stripped_rooms | ran.absentees | shows |
| false_spring | Fresh paint on old wounds, and everyone very cheerful about it; leans: phase.mending, presence.reclaimers | phase.mending, phase.recovered, presence.reclaimers, patched | weather.gold_evenings | shows |
| imitation | People copy what they can no longer understand and wait for it to work; leans: feature.timber_ships, creed.cargo_builders | feature.timber_ships, creed.cargo_builders, creed.stampers | - | shows |
| duty | Work handed down so long that nobody remembers what it was for; leans: feature.good_kilometre, creed.road_menders | creed.road_menders, feature.good_kilometre, presence.keepers, creed.horn_pausers | running.hour_horn | shows |
| nature_reserve | The old world set this aside on purpose, and the fences outlived the reason; leans: trade.parks, fauna.mixed_exotics | trade.parks, fauna.mixed_exotics, fauna.herds_hunters, flora.meadow_grass (+1) | - | shows |
| second_wind | It survived the collapse once and is finding out what that cost; leans: presence.refugees, phase.falling | phase.falling, fall.slide, presence.reclaimers, phase.recovered (+1) | - | shows |
| wild_country | Nobody has ever stood here, and everything alive is busy; leans: fauna.herds_hunters, fauna.pack_hunters | fauna.herds_hunters, fauna.pack_hunters, flora.deep_forest, flora.giant_flora (+1) | - | shows |
| grand_scenery | The land has no audience and puts on the show anyway; leans: feature.buttes, feature.rock_spires | - | feature.buttes, feature.rock_spires, feature.columnar_towers, feature.natural_arches, feature.great_falls (+3) | missing |
| stillness | Nothing here moves, and a footprint would last a million years; leans: sound.silence, weather.dead_calm | sound.silence, weather.dead_calm, fauna.sparse | sky.hard_stars, sky.black_sky | shows |

## 3. Falls

Signature = identity or name, its places, and its strongest leans. Built = consumed references; missing = unconsumed ones.

| Value | Signature | Built | Missing | Verdict |
|---|---|---|---|---|
| nuclear_war | a nuclear exchange; places: blast_crater, deep_bunker, command_bunker; leans: hazard.radiation, flora.dead_forest | blast_crater, deep_bunker, command_bunker, missile_silo (+8) | fallout_shelter, shadow_wall, sealed_doors, crater_ring | shows |
| orbital_bombardment | bombardment from orbit; places: crashed_freighter, abandoned_spaceport, blast_crater; leans: hazard.unstable_wrecks, feature.debris_line | crashed_freighter, abandoned_spaceport, blast_crater, wrecks (+2) | debris_camp, orbital_wreck_site, hazard.unstable_wrecks, feature.debris_line, sky.broken_stations (+2) | weak |
| invasion | invasion and occupation; places: military_base, vharn_outpost, vharn_sam_site; leans: hazard.mines, presence.resistance | military_base, vharn_outpost, vharn_sam_site, prison_block (+5) | ransacked_offices | shows |
| civil_war | civil war; places: military_base, command_bunker, hospital; leans: marks.two_flags, presence.militias | military_base, command_bunker, hospital, evacuation_camp (+5) | trench_line | shows |
| machine_uprising | a machine uprising; places: kessari_works, kessari_spire, abandoned_factory; leans: hazard.armed_hardware, presence.kessari | kessari_works, kessari_spire, abandoned_factory, hazard.armed_hardware (+2) | sentry_line, welded_doors, spotless_zones | shows |
| chemical_war | chemical war; places: mask_depot, deep_bunker, refinery; leans: hazard.bad_air, hazard.corrosion | deep_bunker, refinery, hospital, masks (+1) | mask_depot, hazard.bad_air, hazard.corrosion | weak |
| kinetic_strikes | rocks dropped from space; places: blast_crater, old_mine, precursor_vault; leans: hazard.unstable_ground | blast_crater, old_mine, precursor_vault, debris | hazard.unstable_ground, tilted_towers, crater_deep | weak |
| blockade | siege and blockade; places: ration_depot, abandoned_spaceport, scrappy_port; leans: presence.smugglers, hazard.mines | abandoned_spaceport, scrappy_port, command_bunker, hazard.mines (+4) | ration_depot, fleet_parked | shows |
| drone_wars | autonomous weapons fighting themselves out; places: drone_factory, sentry_line, military_base; leans: feature.machine_drifts, hazard.armed_hardware | military_base, hazard.armed_hardware | drone_factory, sentry_line, motor_pool, feature.machine_drifts, spare_airframes (+1) | weak |
| mercenary_seizure | hired guns taking the cities; places: scrap_fort, military_base, casino_resort; leans: presence.warlords, infra.toll_roads | scrap_fort, military_base, exchange_depot, presence.warlords (+1) | casino_resort, gilded_forts | shows |
| plague | plague; places: hospital, evacuation_camp, burial_pits; leans: marks.plague_marks, fauna.ferals | hospital, evacuation_camp, biolab, marks.plague_marks (+3) | burial_pits | shows |
| biohazard | a lab agent let loose; places: biolab, research_base, cryo_vault; leans: hazard.spores, hazard.mutagen | biolab, research_base, cryo_vault, hospital (+5) | bloom_garden, vat_hall, grown_through | shows |
| sleep_sickness | a sickness of sleep; places: hospital, hospice_ward, cryo_vault; leans: presence.routine_walkers | hospital, cryo_vault, presence.routine_walkers, sleep_wards | hospice_ward, sleeper_ward, routine_loop_street | weak |
| frenzy | a contagion of rage; places: hospital, prison_block, evacuation_camp; leans: presence.frenzied | hospital, prison_block, evacuation_camp, presence.frenzied | gouged_doors | shows |
| husks_fall | bodies that kept walking; places: cemetery_vault, shrine_hall, hospital; leans: presence.husk_nights, hazard.night_swarms | hospital, presence.husk_nights, marks.tally_marks | cemetery_vault, shrine_hall, mortuary_deep, hazard.night_swarms, churchyards | weak |
| fungal_takeover | mould through the cities; places: fruiting_hall, sewage_works, hospital; leans: hazard.spores, flora.fungal_giants | sewage_works, hospital, old_mine, hazard.spores (+1) | fruiting_hall, mat_streets | shows |
| sterility | the births stopping; places: school_campus, hospice_ward, survivor_town; leans: sound.silence | survivor_town, sound.silence | school_campus, hospice_ward, empty_nurseries | weak |
| parasite_swarm | a swarm in the walls; places: sewage_works, hospital, meridian_apartments; leans: hazard.swarm_nests | sewage_works, hospital, meridian_apartments, hazard.swarm_nests | webbed_lifts | shows |
| famine | famine; places: grain_elevator, ration_depot, old_farm; leans: presence.gangs | grain_elevator, old_farm, evacuation_camp, presence.gangs (+2) | ration_depot | shows |
| drought | the rivers failing; places: water_works, water_tower_keep, drowned_cistern; leans: hazard.thirst, presence.water_lords | water_works, hazard.thirst, weather.dust_storms, presence.water_lords | water_tower_keep, drowned_cistern, caravan_stop, tally_rods, stranded_boats | weak |
| rising_seas | the sea rising; places: docks, sewage_works, barge_town; leans: feature.drowned_city, hazard.surges | docks, sewage_works, hospital, feature.drowned_city (+2) | barge_town, hazard.surges | weak |
| long_winter | a winter that stayed; places: ice_outpost, coal_plant, old_mine; leans: hazard.cold, weather.blizzards | ice_outpost, coal_plant, old_mine, weather.blizzards (+1) | ski_lodge, hazard.cold, stove_rooms, buried_cars | weak |
| runaway_heat | the air baking; places: water_works, deep_bunker, solar_farm; leans: hazard.heat, weather.fire_storms | water_works, deep_bunker, solar_farm, weather.fire_storms | hazard.heat, cellar_towns | weak |
| ash_year | an eruption closing the sky; places: coal_plant, old_mine, deep_bunker; leans: weather.ash_fall, flora.dead_forest | coal_plant, old_mine, deep_bunker, weather.ash_fall (+2) | ash_roofs | shows |
| great_quake | the ground moving; places: highway_interchange, rail_yard, hospital; leans: hazard.unstable_ground, hazard.sinkholes | rail_yard, hospital | highway_interchange, hazard.unstable_ground, hazard.sinkholes, tilted_towers, fault | weak |
| air_loss | the air thinning; places: habitat_dome, greenhouse_dome, research_lab; leans: hazard.thin_air | research_lab, masks | habitat_dome, greenhouse_dome, hazard.thin_air, dead_gardens | weak |
| poison_air | a toxic haze from a thousand chimneys; places: refinery, steelworks, abandoned_factory; leans: hazard.bad_air, weather.acid_rain | refinery, steelworks, abandoned_factory, weather.acid_rain (+2) | dust_mill, mask_depot, hazard.bad_air | weak |
| stellar_flare | the star flaring; places: solar_farm, telecom_exchange, observatory; leans: hazard.radiation | solar_farm, telecom_exchange, hazard.radiation | observatory, sunward_scorch | shows |
| meteor_swarm | years of meteors; places: blast_crater, observatory, habitat_dome; leans: hazard.meteors | blast_crater | observatory, habitat_dome, hazard.meteors, holed_domes, crater_scatter | weak |
| industrial_collapse | factories eating the land; places: steelworks, refinery, coal_plant; leans: hazard.toxic_runoff | steelworks, refinery, coal_plant, hazard.toxic_runoff (+1) | acid_pool_works | shows |
| pollinator_loss | the pollinators stopping; places: orchard, old_farm, greenhouse_dome; leans: flora.pollen_heavy | old_farm, flora.pollen_heavy | orchard, greenhouse_dome, bare_orchards | weak |
| grey_goo | small machines eating everything; places: research_lab, kessari_works, data_centre; leans: hazard.grey_front, fauna.metal_eaters | research_lab, kessari_works, abandoned_factory | data_centre, hazard.grey_front, fauna.metal_eaters, lace_steel, grey_scar | weak |
| reactor_failures | every plant failing at once; places: nuclear_plant, fusion_plant, substation; leans: hazard.radiation | nuclear_plant, fusion_plant, substation, evacuation_camp (+1) | alarm_rooms | shows |
| grid_cascade | the lights going out together; places: substation, coal_plant, nuclear_plant; leans: hazard.arc_flash, feature.pylon_forest | substation, coal_plant, nuclear_plant, rail_yard (+2) | dam_hydro, hazard.arc_flash, candle_stubs | weak |
| ai_silence | every mind-machine going quiet; places: data_centre, ministry_hall, abandoned_factory; leans: presence.kessari | abandoned_factory, telecom_exchange, presence.kessari | data_centre, ministry_hall, quiet_terminals, sats | weak |
| terraform_failure | the world-fixer breaking; places: weather_engine, research_lab, solar_farm; leans: mod.half_terraformed, marks.survey_stakes | research_lab, solar_farm, mod.half_terraformed, marks.survey_stakes | weather_engine, observatory, hazard.strange_chemistry, climate_towers, terraform_grid | weak |
| weather_failure | the weather control breaking; places: weather_engine, telecom_exchange, wind_farm; leans: weather.perpetual_storm, hazard.lightning | telecom_exchange, wind_farm, weather.perpetual_storm, weather.dead_calm | weather_engine, hazard.lightning, shelter_halls | weak |
| teleport_accident | the gates misfiring; places: gate_ruin, abandoned_spaceport, research_lab; leans: hazard.misplaced_things | abandoned_spaceport, research_lab, lost_luggage | gate_ruin, hazard.misplaced_things | weak |
| drive_disaster | a drive failing on its pad; places: abandoned_spaceport, crashed_freighter, blast_crater; leans: hazard.unstable_wrecks, hazard.radiation | abandoned_spaceport, crashed_freighter, blast_crater, hazard.radiation | hazard.unstable_wrecks, melted_pads, crater_ring | weak |
| cloning_mishap | too many of everyone; places: clone_farm, hospital, cryo_vault; leans: presence.duplicates | hospital, cryo_vault, presence.duplicates | clone_farm, name_doubles | weak |
| memory_rot | the records rotting away; places: data_centre, public_library, archive_vault; leans: marks.blanked_signs | marks.blanked_signs | data_centre, public_library, archive_vault, ministry_hall, cold_racks | missing |
| debris_cascade | orbit filling with wreckage; places: crashed_freighter, debris_camp, observatory; leans: hazard.falling_debris, sky.drifting_hulks | crashed_freighter, abandoned_spaceport, debris, wrecks | debris_camp, observatory, hazard.falling_debris, sky.drifting_hulks, feature.debris_line (+2) | weak |
| monoculture_failure | one crop and one blight; places: grain_elevator, old_farm, ration_depot; leans: flora.dead_fields | grain_elevator, old_farm, flora.dead_fields | ration_depot, field_stores | shows |
| exodus | everyone trying to leave; places: abandoned_spaceport, scrappy_port, evacuation_camp; leans: feature.jam_highways, infra.port_funnel | abandoned_spaceport, scrappy_port, evacuation_camp, infra.port_funnel (+4) | processing_hall, luggage_field, hazard.unstable_wrecks | shows |
| lockdown | walls going up; places: quarantine_wall, sorting_gate, front_checkpoint; leans: feature.quarantine_walls, marks.colour_zones | prison_block, feature.quarantine_walls, marks.colour_zones, newspapers_stand | quarantine_wall, sorting_gate, front_checkpoint | weak |
| economic_collapse | money ceasing to mean anything; places: exchange_market, casino_resort, shopping_mall; leans: presence.gangs | exchange_market, exchange_depot, presence.gangs | casino_resort, shopping_mall, dead_banks | weak |
| faith_takeover | prophets filling the gap; places: shrine_hall, monastery, hollow_shrine; leans: marks.prayer_flags, creed.lamp_keepers | alien_cathedral, creed.lamp_keepers, marks.prayer_flags, altars | shrine_hall, monastery, hollow_shrine | weak |
| decreed_exodus | an order for everyone to leave; places: evacuation_camp, ministry_hall, abandoned_spaceport; leans: running.sentries | evacuation_camp, abandoned_spaceport, running.sentries, door_notices | ministry_hall | shows |
| general_stoppage | everyone putting tools down; places: steelworks, rail_yard, abandoned_factory; leans: marks.strike_banners | steelworks, rail_yard, abandoned_factory, marks.strike_banners (+1) | company_town | shows |
| enclave_wars | gated estates fighting; places: wall_city, scrap_fort, meridian_apartments; leans: hazard.crossfire | scrap_fort, meridian_apartments, hazard.crossfire | wall_city, estate_house, trophy_halls | weak |
| mutiny | the workers rising; places: company_town, ministry_hall, abandoned_factory; leans: marks.strike_banners, ran.company | abandoned_factory, old_mine, marks.strike_banners | company_town, ministry_hall, ran.company, meeting_halls | weak |
| great_migration | a whole people walking away; places: evacuation_camp, caravan_stop, pilgrim_hostel; leans: marks.route_paint | evacuation_camp, marks.route_paint, road_camps | caravan_stop, pilgrim_hostel | weak |
| dark_patch | the light dimming; places: observatory, solar_farm, greenhouse_dome; leans: flora.glow_moss, fauna.night_hunters | solar_farm, fauna.night_hunters, flora.glow_moss | observatory, greenhouse_dome, lamp_arrays | weak |
| gravity_shift | the pull changing in patches; places: research_lab, crashed_freighter, space_elevator_base; leans: hazard.gravity_pockets | research_lab, crashed_freighter | space_elevator_base, hazard.gravity_pockets, marker_posts | weak |
| rogue_body | a dark world passing close; places: docks, observatory, lighthouse; leans: hazard.tides, feature.stranded_ships | docks, feature.stranded_ships, flood, debris | observatory, lighthouse, hazard.tides, stranded_boats | weak |
| time_stutter | hours repeating in patches; places: time_pocket, wrong_house, observatory; leans: hazard.time_slips | - | time_pocket, wrong_house, observatory, hazard.time_slips, repeat_rooms | missing |
| dimensional_tear | a seam opening overhead; places: bloom_scar, alien_cathedral, observatory; leans: sky.seam | alien_cathedral | bloom_scar, observatory, sky.seam, bent_rooms | missing |
| wormhole_tides | passages opening at random; places: crashed_freighter, gate_ruin, abandoned_spaceport; leans: hazard.misplaced_things | crashed_freighter, abandoned_spaceport, lost_luggage | gate_ruin, hazard.misplaced_things | weak |
| slow_fade | a slow fade; places: survivor_town, hospital, school_campus | survivor_town, hospital | school_campus, one_open_street | weak |
| emigration | the young leaving; places: school_campus, hospice_ward, survivor_town | survivor_town | school_campus, hospice_ward, one_open_street | weak |
| neglect | nobody maintaining it; places: water_works, rail_yard, dam_hydro; leans: hazard.collapsing_floors | water_works, rail_yard | dam_hydro, hazard.collapsing_floors, propped_stairs | weak |
| supply_cutoff | the ships ceasing to come; places: exchange_depot, scrappy_port, ration_depot | exchange_depot, scrappy_port, ration_shelves | ration_depot | weak |
| ageing_colony | machines wearing out first; places: kessari_works, old_farm, habitat_dome; leans: hazard.failing_seals | kessari_works, old_farm | habitat_dome, hazard.failing_seals, hand_parts | weak |
| vanishing | everyone vanishing; places: survivor_hamlet, meridian_apartments, hospital; leans: running.long_life_lamps, sound.silence | survivor_hamlet, meridian_apartments, hospital, sound.silence (+1) | running.long_life_lamps | weak |
| walked_out_apoc | everyone leaving calmly at once; places: military_base, meridian_apartments, survivor_town | military_base, meridian_apartments, survivor_town, door_notices (+1) | - | shows |
| silence | sound failing; places: radio_mast_site, telecom_exchange, hospital; leans: sound.silence, marks.hand_signs | telecom_exchange, hospital, sound.silence, marks.hand_signs | radio_mast_site, clapperless_bells | shows |
| hollow_front | the Hollow growth arriving; places: bloom_scar, alien_hive_ruin, hollow_shrine; leans: feature.hollow_basin, flora.hollow_overrun | alien_hive_ruin, military_base, hazard.mutagen, flora.hollow_overrun (+1) | bloom_scar, hollow_shrine, front_checkpoint, bloom_garden, feature.hollow_basin (+2) | weak |
| folding | a place folding onto itself; places: wrong_house, mirror_hall, time_pocket; leans: hazard.disorientation, feature.folded_district | - | wrong_house, mirror_hall, time_pocket, hazard.disorientation, feature.folded_district (+1) | missing |
| replacement | the people being swapped for copies; places: wrong_house, hospital, meridian_apartments; leans: presence.duplicates | hospital, meridian_apartments, presence.duplicates | wrong_house, one_wrong_object | weak |
| long_sleep | everyone entering cryo, and the vaults failing; places: cryo_vault, sleeper_barracks, hospice_ward; leans: running.cold_storage | cryo_vault, running.cold_storage, pod_rows | sleeper_barracks, hospice_ward | weak |
| slide | a slide into barbarism; places: scrap_fort, survivor_town, wall_city; leans: presence.warlords, presence.gangs | scrap_fort, survivor_town, presence.warlords, presence.gangs (+1) | wall_city | shows |
| liquidation | the owners selling the world for parts; places: exchange_depot, breaker_yard, abandoned_factory; leans: marks.repossession_tags, ran.absentees | exchange_depot, abandoned_factory, marks.repossession_tags, stripped_rooms (+1) | breaker_yard, ran.absentees, ran.company | weak |
| seam_fire | fires under the ground; places: burn_town, coal_plant, old_mine; leans: hazard.burning_ground, feature.burning_seams | coal_plant, old_mine, hazard.burning_ground | burn_town, hazard.bad_air, feature.burning_seams, slumped_towns | weak |
| magnetic_flip | the magnetic field failing; places: telecom_exchange, observatory, substation; leans: sky.aurora, fauna.lost_migrants | telecom_exchange, substation, hazard.radiation | observatory, sky.aurora, fauna.lost_migrants, dead_compasses | weak |
| mirror_burn | an orbital mirror turning on its own cities; places: solar_farm, blast_crater; leans: sky.second_noon, hazard.glare | solar_farm, blast_crater, burn | sky.second_noon, hazard.glare, sunward_scorch, mirrors | weak |
| gene_drift | children being born slightly changed; places: biolab, school_campus, hospital; leans: creed.body_keepers | biolab, hospital, creed.body_keepers, height_marks | school_campus | shows |
| broadcast | a signal that changed how people behaved; places: telecom_exchange, radio_mast_site; leans: running.broadcast, sound.radio_murmur | telecom_exchange, running.broadcast, sound.radio_murmur | radio_mast_site, cut_cables | shows |
| sinkage | the ground under the cities giving way; places: sunk_town, water_works, metro_station; leans: hazard.sinkholes | water_works, pit | sunk_town, metro_station, hazard.sinkholes, tilted_towers | weak |
| overreach | a megaproject that broke the economy; places: works_complex, abandoned_factory; leans: feature.unfinished_works | works_complex, abandoned_factory, feature.unfinished_works, scaffolds | - | shows |
| last_ark | the rich leaving in the only ship; places: abandoned_spaceport, estate_house, scrap_fort; leans: ran.great_houses, ran.company | abandoned_spaceport, scrap_fort | estate_house, ran.great_houses, ran.company, gilded_forts | weak |
| first_contact | something landing and then leaving; places: alien_cathedral, blast_crater, research_lab; leans: feature.long_furrow | alien_cathedral, blast_crater, research_lab, cordon_tents | feature.long_furrow | weak |
| tide_of_refugees | more people arriving than it could feed; places: evacuation_camp, aid_station, ration_depot; leans: presence.refugees | evacuation_camp, scrappy_port, presence.refugees, road_camps (+1) | aid_station, ration_depot | shows |

## 4. Moments and eras

| Value | Signature | Built | Missing | Verdict |
|---|---|---|---|---|
| moment.ordinary_evening | on an ordinary evening, dinner on the table | set_tables | - | shows |
| moment.rush_hour | at rush hour; leans: feature.stopped_trains | - | feature.stopped_trains, ticket_halls | weak |
| moment.shift_change | at shift change | - | lockers_notes | weak |
| moment.deep_night | in the dead of night | - | beds_made | weak |
| moment.festival | in the middle of a great festival; places: fairground, stage_hall; leans: marks.faded_bunting, feature.stalled_floats | marks.faded_bunting, feature.stalled_floats, costumes, stalls | fairground, stage_hall | weak |
| moment.match_day | on the day of a final; leans: trade.sport | trade.sport, scoreboards | - | shows |
| moment.term_time | in the last week of term; places: school_campus | - | school_campus, lockers_notes | weak |
| moment.holiday | in the off-season, everything closed for winter | dust_sheets | - | shows |
| moment.harvest | at harvest | - | field_stores | weak |
| moment.election | during an election | - | council_rooms | weak |
| moment.wedding_season | in the wedding season | set_tables | - | shows |
| moment.opening_day | on the day before everything opened | dust_sheets | - | shows |
| moment.signing_day | on the day a treaty was signed; places: border_checkpoint | - | border_checkpoint, banquet | weak |
| moment.launch_day | on launch day | luggage | - | shows |
| moment.drill | in the middle of a drill nobody knew was real; leans: marks.warning_stripes | marks.warning_stripes | alarm_rooms | weak |
| moment.over_decades | over months and decades | - | one_open_street | shows |
| moment.still_going | and it's still going | - | - | shows |
| phase.fallen | still in ruins | - | - | shows |
| phase.mending | mending, slowly | patched | - | shows |
| phase.recovered | recovered, the fall showing only in spots | memorials | - | shows |
| phase.spared | barely touched | - | - | shows |
| phase.untouched | never settled | - | - | shows |
| phase.falling | falling right now | front_barricades | - | shows |
| age.now | in the last few months | - | - | shows |
| age.recent | within a generation, after holding on | - | - | shows |
| age.century | about 110 years ago | - | - | shows |
| age.long | about 170 years ago | - | - | shows |
| pre.L | low tech; places: survivor_hamlet; leans: trade.subsistence, port.E | survivor_hamlet, coal_plant, wind_farm, water_works (+6) | post.M, ran.elders, ran.great_houses, build.gabled_timber | shows |
| pre.M | mid tech; places: meridian_apartments; leans: post.H | meridian_apartments, coal_plant, nuclear_plant, wind_farm (+5) | - | shows |
| pre.H | high tech; places: meridian_apartments, ominous_tower; leans: trade.herding, trade.subsistence | meridian_apartments, ominous_tower, fusion_plant, solar_farm (+8) | - | shows |
| post.L | low tech | - | - | shows |
| post.M | mid tech | - | - | weak |
| post.H | high tech | - | - | shows |
| port.A | a great port | highport, yards, traffic | stations, port_hub | shows |
| port.B | a large port | yards, traffic | stations, port_hub | shows |
| port.C | a working port | - | stations, port_hub | shows |
| port.D | a poor port | scrappy_pad | - | shows |
| port.E | a frontier field; places: landing_clearing | landing_clearing, scrappy_pad | - | shows |
| port.0 | no port at all | - | - | weak |
| people.none | with nobody left | - | - | shows |
| people.survivors | with a few survivors; places: survivor_hamlet | survivor_hamlet | - | shows |
| people.outposts | with scattered outposts; places: frontier_base, survivor_hamlet | frontier_base, survivor_hamlet | - | shows |
| people.settled | with settled country; places: survivor_town, survivor_hamlet | survivor_town, survivor_hamlet | - | shows |
### Mid-collapse (phase.falling, `WorldForce.MidCollapse`), end to end

`MidCollapse = true` gives the roll `phase.falling`; `false` bars it. The phase is `weight 12`, needs `people=settled` and a drop of 0, 1 or up, and only takes a fall that is `now`. A fall forced from the picker is taken as given, so mid-collapse with Dimensional Tear is "falling" over a fall that was never meant to be current. What it changes, all of it read by code:

| Effect | Falling | Long after |
|---|---|---|
| Age (`age.now`, `RuinAge.Wear`) | wear 0.04-0.06: no moss, mould, leaf drift, tile fall, bars | wear 0.65-0.92, `overgrown` 0.25-0.45 |
| Power and lights (`States.PowerOff`, `Decay`) | left on in all but infested places | cut |
| Place states (`state active 6 abandoned 3 infested 1 ruined 1`) | mostly lived-in | `fallen`: active 1, abandoned 6, ruined 3 |
| Occupants (`occ SQT 14 GNG 12 FAC 8 EMP 8`) | settlers and gangs hold places | mostly empty or scavengers |
| Streets (`lean phase.falling`, `tag.front_barricades`) | barricades, luggage, sandbags, fresh | none |
| Bias (`severity 0.9 strife 0.7`) | soot and fire looks (`Violence` at least 0.46), war music, more ferals | `severity 0.6`, no strife |

Not built: bodies, fires still burning, smoke, evacuation in progress (moving crowds, vehicles under way), sirens, damage that is days old as against years. The difference is therefore one of freshness and state odds, not of events, and it is slight on a world whose fall is old, low-tech or whose named places do not exist. The owner's world (Wall Cities, Dimensional Tear, old tech low, now high) had both its theme places and its fall's seam missing, so almost nothing was left to show the difference.

## 5. The 15 missing references with the most weight

Counted at audit time over every layer that leans on the reference: layers x strength (a `like` weighs by its multiplier, a place by its count). Notes say how to build each by rule from parts that exist.

| # | Reference | Layers | Strength | What it would take |
|---|---|---|---|---|
| 1 | family `observatory` | 12 | 19 | A site family: the colonydome kit's dome room on a tower module from the architect families, placed on high ground by the existing siting rule. |
| 2 | `hazard.bad_air` | 4 | 27 | One hazard rule: a hazard value sets a vitals drain (the foul-air path planets already use for `AirKind.Ash`) and a mask need; all `hazard.*` share it. |
| 3 | family `school_campus` | 7 | 12 | A building program (`rooms.txt` classrooms and a hall) on the city-families lot rule, kit `urban`. |
| 4 | family `ration_depot` | 6 | 13 | A warehouse program with the crate and shelf props the loot tables already place, kit `industrial`. |
| 5 | `hazard.heat` | 3 | 22 | The same hazard rule: day-side temperature up, from the weather model's existing heat input. |
| 6 | `hazard.cold` | 3 | 22 | The same rule, cold side; `frozen_*` climates already set the weather. |
| 7 | `hazard.unstable_wrecks` | 4 | 15 | Wreck sites already exist: a rule that makes their hull blocks collapsible by the damage system. |
| 8 | family `ministry_hall` | 6 | 10 | A city-families civic block: tower plus hall programs, kit `concrete`. |
| 9 | family `radio_mast_site` | 5 | 12 | A utility family: the power-line pylon model scaled up with a hut, sited on high ground. |
| 10 | `running.long_life_lamps` | 4 | 14 | A lamp-state flag on street lamps (`part lamp`): some stay lit by the world's age. |
| 11 | family `greenhouse_dome` | 5 | 10 | The dome room with plant blocks; the same family can serve `habitat_dome` (5 layers) with a sealed airlock door. |
| 12 | family `wrong_house` | 5 | 10 | The house program with one mirrored or misplaced room, an offset on the homes rule. |
| 13 | family `company_town` | 5 | 9 | The existing town plan with an industry hub and one-kit rows. |
| 14 | family `dam_hydro` | 5 | 9 | A utility family beside the water rule: a wall module across a river cell and a plant hut. |
| 15 | family `hospice_ward` | 5 | 9 | A clinic-kit building program: long ward rooms with bed props. |

Seen by the owner but lower: family `wall_city` (4 layers, 7) and `hilltop_keep`, `toll_gate` (a gate-and-wall lot rule on the town plan would serve all three); `sky.seam` (2 layers, 22; a sky layer value could add a body to the planet's star system the way `ring_arc` and `giant_overhead` would); `build.*` (25 values, 0 read: the `kit` field would pick the house kit in `homes.txt`).
