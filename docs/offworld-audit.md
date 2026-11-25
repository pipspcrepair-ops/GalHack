# Off-world audit: what only works on the starter planet

- **Branch:** proto/batch2 at f20ac912. Read only.
- **Code path:** paths below are under `G:\GalHack-wt\batch\prototypes\slice\src`.
- **proto/vacarms:** 7c11ef50 is a patch that swaps captured zones. It will **not** merge (owner, 25 Nov; playtest list). So the root fix also covers what it touched: ArmsHost's node is still a child of the starter zone, SleeperTool keeps `readonly _zone`, and Spaceflight.TakeHands still takes the hands off-world. BuildTool (`readonly _zone`, about 230 uses) is in the same root fix. Keep vacarms' tests (MoonTest.Hands).
- **Not counted:** everything that rightly differs in space.

**Root fix the owner asked for:** one current world that every system reads. No system keeps the starter zone. The findings below are the list of places that fix must reach.

**Root cause.** `Game.OnPlanetBuilt` (Game/Game.cs:294-400) and `SetUpPlaces` (Game/Game.Places.cs:43-87) build about 20 systems against the starter zone. `EnterZone` (Orbit/Game.Orbit.cs:50) then moves only Camera, Console and Shots. Every other system stays parented to the starter zone, keeps a reference to it, or wired its events to it once. Other worlds' zones (Orbit/Spaceflight.Worlds.cs:117 `BodyZone`) do get their own Wildlife, Weather, Sfx, Water, Loot and Pois. The player's systems are never hooked up to them.

**Second root cause: data, not tied to a world.** Positions are stored as bare coordinates with no world. Only four kinds of things go into the zone ledger. Other kinds of state live only in memory in a zone node, so they vanish when a star's zones are freed. Every world-changing thing needs a (star, body) key before travel between stars is real.

**Sure:** C = confirmed by reading the code, L = likely (read part of it), U = unsure (needs a run or a closer read).
**Fix:** S = small (under an hour, one or two files), M = medium (a day, a few files), L = large (a design change or several days).

---

## Critical: wrong world, lost or duplicated things, or a whole system missing

| # | What breaks off-world (what the player sees) | Where | Sure | Fix |
|---|---|---|---|---|
| 1 | **Respawn puts you in the wrong world.** `Respawn` → `WakeAgain` puts the survivor at `Bunker.CryoFront` in the *current* zone. It does no zone handover. Die on the moon or at another star and you wake at the starter bunker's coordinates on that world: in mid-air, underground or in the sea. Respawn points (`RespawnPoint`, survival-vitals `Death.cs:53`) hold a `Pos` but no world, so a tent placed on the moon could not be told apart from one at home. | Game/Game.Items.cs:277, Game/Game.Start.cs:72-127 | C | M: a world key on RespawnPoint, plus respawn through a handover |
| 2 | **Suit measures the starter planet.** `readonly SurfaceZone _zone` is built once at start. In orbit and on airless worlds, the crossing sets `Suit.Beyond = Space` (Orbit/Spaceflight.cs:370, 454), which correctly replaces the air and temperature reading. **Two problems remain.** (a) On another world *with air*, `Beyond` is null, so temperature, oxygen, radiation, biome, water and nearby climate devices come from the starter planet at your coordinates. `Sim.Environment` is also set to `Vacuum` there (Spaceflight.cs:453). (b) **Everywhere off-world, spacewalks included:** `Outdoors` (Suit.cs:335-348) still runs the starter's weather (blizzard or fire-storm cold and heat damage) and its ice water. It is not gated by `Beyond`. The starter's weather is parked while you're away, so a storm that was on when you left would keep hurting you in space. | Player/Suit.cs:28, 123-170, 321, 335-348; built at Game/Game.Start.cs:66 | C for the code path; L that a stored storm still hurts | M: make it follow `Game.Zone`; climate rules per world |
| 3 | **No places on other worlds.** `PoiPlaces`, `RuinLoot`, `Hulks`, `SkyTraffic`, `Caves`, `Underworld` and `BlockDoors` exist once, under the starter zone, with `Zone = Zone` set once. Other worlds get POI plans (`Zone.Pois`), but no buildings, towns, people, ruin loot, cave creatures, ground hulks or air traffic stand there. Everything that reads `Game.Places` (arms fire lines, music, the suit's closed-room check, town reactions) reads the starter's places. | Game/Game.Places.cs:43-87; Arms/SimArms.cs:111, 186; Game/MusicScenes.cs:110-124; Game/Game.Start.cs:68 | C | L: one places stack per world zone, made with it and handed over in EnterZone |
| 4 | **Doors break off-world.** `BlockDoors` is one node under the starter zone. Its shut pass lists `Zone.Vessels` of the starter only (`:188`), and E uses its `_known` list (`:89`). **Owner, 25 Nov:** doors on a ship built on the starter can still be opened and shut in orbit and on other planets, but *they glitch graphically in many ways*. Likely causes, not yet proved by a run: (a) the leaves hang under the vessel, but they are posed by `BlockDoors`' physics tick in the starter zone, which is parked and hidden while you're away (Spaceflight.cs:481 `Park`, Spaceflight.Seam.cs:290 `ShowGround`). They can freeze mid-slide, never be removed, or move out of step with a ship that's moving and being frame-shifted. (b) The door list for a vessel (`_known`) refreshes only in the shut pass, so after any change in orbit it's stale. (c) A door only turns solid again when its leaves meet, so a frozen slide leaves a doorway drawn shut but open, or the reverse. Doors on a vessel built or claimed off-world can't be used at all, and no door shuts by itself off-world. | Vessels/BlockDoors.cs:25, 89, 166, 188; Game/Game.Places.cs:71, 249, 292 | C | S: point `Zone` at `Game.Zone` (`ForceFields` next to it already uses `ZoneOf = () => Zone`) and clear `_known` on a zone change |
| 5 | **Hover bikes live on the starter planet.** `HoverBikes` keeps `readonly _zone` and is a child of the starter zone. Physics rules, weather forces, terrain height and Godot ray casts are all the starter's. `PlaceBike` casts in the current zone but puts the bike in the starter's. Off-world the bike is invisible or falls through, or it floats over the starter's terrain height. The bike is also not in the ledger (see #8). | Vehicles/HoverBike.cs:426-603; Game/Game.cs:331-337, 512-522 | C | M: bikes per zone (or reparent plus a zone getter); ledger holder |
| 6 | **Wildlife, people and Husks aren't wired to the world you're on.** `SetPlayer(Target)`, `Attacked`, `Quaked`, `Telegraphed`, `Died`, `Seized`, `LetGo`, `Offered`, `Turned` and `Loud` are hooked once to the starter's `WildlifePatch`, as are `SolidAt` and `StandingAt`. Creatures on another world don't target you, bites don't hurt or flash the visor, kills give no drops, XP or corpse, Husks can't grab you, and threat warnings don't show. | Game/Game.cs:351-361; Game/Game.Husks.cs:38-39; Game/Game.Aftermath.cs:44-45; Game/Game.Places.cs:76-77, 83 | C for wiring, L for what play shows | M: one `WireZone(zone)` / `UnwireZone(zone)` called from EnterZone |
| 7 | **State left at another star is lost or comes back fresh.** Only the sleeper, structures, containers and devices go into the ledger (Orbit/Spaceflight.cs:681-711). When a star's zones are freed (Orbit/Spaceflight.Worlds.cs:240), these are lost and come back fresh next visit: terrain digs and craters (`PlanetEdits`), felled trees, mined deposits (`DepositFinder.Depleted`), killed or harvested animals, corpses, placed bikes, and a POI's broken walls, loot and dead townspeople. Loot respawns, which is a duplication route. | Orbit/Spaceflight.cs:681-711; Orbit/Spaceflight.Fold.cs:446-498; Surface/PlanetEdits.cs | C for what is kept; L for respawn | L: a per-world change record (edits, deaths, deposits, place memory) under the star/body key; this is the main data job |
| 8 | **Devices at another star never come back; wrecks may duplicate.** `PlacingOf` returns null for a `Device` (Fold.cs:503-511), and `Restore` only puts back structures and containers (Fold.cs:463-483). A device left at another star and its contents count in the totals forever but never reappear. A derelict comes back "from its plan" under the same id (Fold.cs:487-489), so salvaged blocks are probably whole again and its loot refilled. Machine trays are folded into structure storage, so machine state is lost on return. | Orbit/Spaceflight.Fold.cs:460-511; Orbit/Spaceflight.cs:693-707 | C for devices; L for wreck duplication | M |

## High: plays wrong or looks broken, nothing lost

| # | What breaks off-world | Where | Sure | Fix |
|---|---|---|---|---|
| 9 | **Weather and wind follow the starter.** `Weather.Observer`, `Shelter`, `Enclosed` and `Happened` (lightning and strikes on you) are set on the starter's weather only. On another world with air, the weather isn't centred on you, ignores your shelter, and its hits never reach you. `WindHowl` and `AmbientExposure` are starter children with a fixed `Zone`, so you hear the starter's wind (on a vacuum moon too) and exposure is measured from the wrong ground. | Game/Game.cs:362-368, 384-387; Surface/WindHowl.cs:20; Surface/AmbientExposure.cs:14 | C | S to M |
| 10 | **Terrain streaming and quality settings only reach the starter.** `LoadAhead` feeds the heading to `Surface.Streamer` and `Surface.Infra` (the starter's), so another world loads blind and pops in. `ApplyScene` sets `VoxelRadius` and `ShadowReach` on `Surface.Streamer` only, so the owner's ground detail setting is ignored on every other world. That risks the frame budget and the minimum-spec machine. `Suns()` covers only the starter and the current orbit, so a moon's or another world's sun doesn't get the shadow settings. | Game/Game.cs:943-957; Game/Game.Settings.cs:113-132 | C | S: use `Zone.Streamer`, `Zone.Infra` and `Zone.Sky` |
| 11 | **The map shows the starter planet.** The local and planet levels use `_game.Surface.Planet` for size, wrap, terrain and the vessel list. Your position is wrapped by the starter's width and height on a moon of another size. Time of day comes from `Surface.Sky.Days`. On another world the map draws the wrong planet, with you in the wrong place on it. | Ui/MapLevels.cs:47, 210-261, 329, 573, 593 | C | M |
| 12 | **Dust, chips and gibs land in the starter world.** `Debris` (also `Zone.Dust` and `Tool.Debris`) and `Gibs` are starter children and never move. Impact chips, bike dust, explosions' grit and creature gibs don't show off-world. `Zone.Dust` is null on other zones, so `OnExploded` puffs are silently skipped. | Game/Game.cs:323-330; Game/Game.Bodies.cs:31-32; Game/Game.Places.cs:110-111 | C | S: add them to the EnterZone move list and set `zone.Dust` |
| 13 | **Hulls and vessels keep their first zone.** `HullBlocks` and the hulk's `SurfaceGround` hold `readonly` zones. A vessel built on the starter and flown to another world casts hull rays and checks ground readiness against the starter. Lights or blocks can go missing on hull checks, or the ground check never settles. `Device._zone` (Vessels/Device.cs:45) is not readonly, but its reassignment wasn't checked. | Vessels/HullBlocks.cs:18-32; Vessels/Vessel.Hulk.cs:142-146; Vessels/Device.cs:45 | L | S to M |
| 14 | **Death bags off-world are the wrong size and colour.** `Loot.BagSize` and `YourColour` are set only on the starter's `Lootables` (Game/Game.Aftermath.cs:42-43). Other zones fall back to 8×8 and the default colour, so a big pack may not fit and spill. | Game/Game.Aftermath.cs:42-43; Items/Lootables.cs:91 | C | S |

## Medium: wrong but minor, or limited to the start system

| # | What breaks off-world | Where | Sure | Fix |
|---|---|---|---|---|
| 15 | **Zone names in the ledger don't follow one scheme.** The home system uses `surface`, `orbit`, `moon<index>` (index into the start orbit's body list), then `surface/<key>`. Other stars use `star<n>/surface/<key>`. A moon's name depends on list order, so a reordered body list after a generator change would map records to the wrong moon. Saves will need a single (star, body) key. | Orbit/Spaceflight.Worlds.cs:88-98 | C | S now, cheaper than after saves |
| 16 | **Bunker map marker and "home" checks** compare against `Game.Surface`. Fine for one start world. Breaks if a second "home" (a claimed base world) is ever needed. | Ui/Visor.cs:415; Orbit/Spaceflight.Moon.cs:37 | C | S |
| 17 | **Music and mood read the starter planet.** Biome and time of day come from `g.Surface.Planet` and `g.Surface.Sky`. `WorldReady` needs the starter's `Places`. Off-world you hear the starter's music. | Game/MusicScenes.cs:64, 103-124; Game/SliceMusic.cs:51 | C | S |
| 18 | **Lights near places** — `Zone.Light.Places` is set on the starter only. There are no places off-world anyway (#3), so this goes with that fix. | Game/Game.Places.cs:78 | C | S |
| 19 | **Replicator markers** are added to whichever zone is current when they're first seen, and never moved. Off-world they show up in the wrong world or not at all. | Game/Game.cs:964-982 | L | S |
| 20 | **Cave and underworld rules about the bunker** (`Keep`, `Indoors`) assume the starter's bunker. They only matter once #3 gives other worlds caves. | Game/Game.Places.cs:60-70 | C | S, with #3 |
| 21 | **Deposit discovery IDs carry no world.** The ID is `deposit:x,z` only. A deposit at the same rounded x,z on another world (or star) gives no XP and no count, because the starter's deposit already used that ID. | Game/Game.Progress.cs:41 | C | S: prefix the world key |
| 22 | **"Landed on the moon" is one discovery for every body.** Landing on any world but the starter pays the same one-time `moon` discovery with that label. The second, third and every later world give nothing. The same goes for `orbit` (only the first orbit pays) and `fold` (only the first star). | Orbit/Spaceflight.cs:373, 455; Orbit/Spaceflight.Fold.cs:293 | C | S: one discovery per world key |
| 23 | **Wreck charting uses the bare site ID.** `Charts.SeeWreck` records `Derelict.Id`, with no world. Every orbit numbers its sites alike (comment at Spaceflight.Worlds.cs:100), so a wreck around another world or star that shares a number with one already charted never shows on the chart. | Orbit/Spaceflight.cs:232; Orbit/Charts.cs:100-104 | L: depends on whether Charts is reset per star | S |
| 25 | **Compass shown in orbit** (owner, 25 Nov, folded into the root fix). It is always drawn (Ui/Visor.cs:181). In orbit, `Spaceflight.Local` turns it to the ground under you on the orbited world, even in open space and beside a moon. Show it only on a world's surface, reading that world's poles (it already does on the ground). Decide where COMPASS-mode markers go in orbit (Ui/Visor.Markers.cs). | Ui/Visor.cs:181, 338-344; Orbit/Spaceflight.Readout.cs:63 | C | S |
| 26 | **Two salvage systems.** In orbit, the hands run a second, separate salvage (Orbit/Spaceflight.Hands.cs:97-160). It uses a timer per block by mass, its own refusals ("YOURS · USE BUILD TOOL", "SALVAGE · WRECKS ONLY") and its own yield. On the ground, the Sleeper tool salvages hulks and POI walls through `Places.Work` and takes down your own blocks through `BuildTool.TakeDown` (Player/SleeperTool.cs:395-396, 701-715). A wreck block in orbit and the same block on the ground are broken by different rules. Root fix: one block damage and salvage path for every structure, everywhere; delete the space copy. | Orbit/Spaceflight.Hands.cs:97-160; Player/SleeperTool.cs:163, 395, 533-550, 701-715 | C | M, part of Root 1 |
| 24 | **Species discovery** uses `species:<id>`. That is right if a species ID means the same animal everywhere. If generated species reuse IDs across worlds, meeting a new world's animals pays nothing. | Game/Game.Progress.cs:49, 58 | U | S |

## Spacewalking (on foot in orbit)
Orbit is a zone of its own (`OrbitZone` is a `SurfaceZone`), so the same root cause applies.

**Hits a spacewalk:**
- #2(b): storm damage from the starter's weather.
- #4: doors. On a vessel built on the starter they open and shut, but glitch graphically in many ways (owner, 25 Nov). On a vessel built or claimed in orbit they don't work at all. They never shut by themselves.
- #1: respawn wakes you at the bunker's coordinates inside the orbit zone.
- #12: no dust, grit or gibs.
- #13: hull checks on vessels built on the starter.
- #14: death bag size.
- Known items: hands (TakeHands) and building (BuildTool).

**Right in space:** air and temperature (`Beyond = Space`), gravity and magboots (`Zone.Physics`), the map's orbit view, wind howl (quiet when the air is gone).

## Checked and fine (follow `Game.Zone`)
- **Zone-following code:** ArmsHost and SimArms logic (`Zone => Game.Zone`; the ArmsHost node is still a starter child), MachineScreens, the core scanner, CreatureBodies (logic; its node is still a starter child), ForceFields, Survivor gravity (`Zone.Physics.GravityAt`), death bag placement (`Zone.Loot`), and SetDown and containers.
- **Deposit scanner** (`Game.Scanner.cs:90`): reads `Zone.Planet`, the current world.
- **Visor planet readout and the info sections** (`Ui/Visor.cs:420`, `Ui/InfoSections.cs:176-187`): different in orbit on purpose.
- **Spaceflight.Weather `Here.IsHome`:** rightly limited to the home globe.
- **Static caches:** none in shared/ or planet-surface hold a planet or zone (mesh caches only).

## Not read closely (left over)
- Hands and consumables
- Game.Gestures
- `PoiSiting.Loading` (static, set false after the starter loads; may affect other worlds' first POI build once #3 exists)
- Whether Charts is reset per star (#23)
- The `Device` zone reassignment

---

## Wider risks of not being world-agnostic
These are patterns that will keep producing bugs like the ones above, not single bugs.

1. **Record IDs are save-format debt.** `structure-N`, `container-N`, `device-N` and `thing-N` come from an in-memory counter (Orbit/Spaceflight.cs:89, 718-729). They are unique only for one session, and they are numbered in the order things are first seen. A wreck's ID gets a world prefix only outside the first orbit (`WreckPrefix`, Spaceflight.Worlds.cs:101). Before saves, IDs must be stable and unique across worlds and stars: world key plus a persistent counter, or a seeded ID.
2. **Rules are game-wide, not per world.** Every world shares one `GameData` (`BodyZone` sets `Data = home.Data`). That's right for items and blueprints. But `Rules.ClimateNode` and `Rules.Planet` are then the same on every world, so the suit's temperature clamp (Suit.cs:115) will be the starter's even after #2 is fixed. Per-world values must come from the world's spec.
3. **One clock, set from the starter.** Orbits read `Below.Sky.Days`, and jumps copy it into the next world's sky (Spaceflight.Jump.cs:271-279; Spaceflight.Moon.cs:110). That's fine as a galaxy clock. Anything that reads a world's day length or season from it gets the starter's.
4. **"Home" is a special case everywhere.** `Game.Surface`, `OnOtherBody` (`Zone != Surface`), `Here.IsHome`, the bunker checks, and `moon<index>` names exist only for the start orbit. Every one is a code path other worlds never take, so bugs hide there.
5. **Tests only prove the starter.** Seeds are random, but nearly every test plays on a starter planet; only MoonTest touches another body. Every finding here passed the test suite. Fix: let the test runner start on any body (moon, vacuum world, another star) and run a share of each suite there.
6. **Wiring per world needs unwiring.** Once systems wire to the current world, every `+=` on a zone needs a matching `-=` on leaving. Otherwise callbacks fire on freed zones (crashes) and dropped worlds stay in memory (leaks). One `WireZone` / `UnwireZone` pair, not ad-hoc hooks.
7. **Memory and frame time.** Home-system worlds stay in memory for the whole game, each with its own streamer, wildlife and weather. With settings applied only to the starter (#10), the others stream at default detail. That is a risk on the minimum-spec machine.
8. **One-time setup that assumes one world.** The bunker, the starting kit, `PoiSiting.Loading` (a static flag cleared after the starter loads) and music readiness (`WorldReady` needs `Game.Places`). Any of these can skip or misfire on the first visit to another world.

**What would stop it coming back:**
- Every brief says no system keeps a zone, and everything reads the current world through one accessor.
- The merge review flags any new `readonly SurfaceZone`, `Game.Surface` use outside the spaceflight code, or text ID built without a world key.

---

## The fix: at the root, no band-aids (owner, 25 Nov)
The findings are symptoms of two root causes. Fix those, and the findings must fall out of the result. Don't work through them one by one.

**Not acceptable:**
- Re-pointing captured zones on a crossing (the vacarms patch).
- A longer reparent list in `EnterZone`.
- `if (InOrbit)` or `if (Zone != Surface)` branches.
- Per-item null checks.
- Any fix in the table's "Fix" column that only moves a reference.

The fix sizes in the tables measure how far each symptom reaches, not a separate job.

**Root 1: one current world, read by every system.**
- **One accessor.** Systems read the world they act in through one accessor (the current world, or the world a thing belongs to). No system stores a zone, planet, weather, wildlife, places or sound reference past one call.
  - Remove `readonly SurfaceZone` fields and constructor-captured zones everywhere (Suit, SleeperTool, BuildTool, HoverBikes, HullBlocks, SurfaceGround, ScatterSolids).
  - Remove the `Zone = Zone` assignments in setup.
- **Ownership.** Decide for every system whether it belongs to the player (it travels and reads the current world) or to a world (each world has its own, made with that world).
  - Player-owned: suit, hands, tools, arms, shots, debris, gibs, wind and exposure, doors' animator, settings, compass, map.
  - World-owned: places stack (#3), bikes standing on that world, the world's weather and wildlife.
- **Events.** Player systems subscribe to the world they're in through one subscribe/unsubscribe point. They're never wired once at setup.
- **No "home" special case.** The starter is just the first world. `Game.Surface`, `OnOtherBody` and the bunker checks become questions about the current world or the respawn point's world.

**Root 2: every world-tied fact carries a world key.**
- **The key.** A stable world key from galaxy seed, star and body, never list order. It goes on every stored position, ID and discovery: respawn points, ledger zone names, record IDs, discovery IDs, chart IDs, deposits, edits.
- **Per-world change record.** Edits, felled trees, mined deposits, deaths, place memory, bikes and devices go in a per-world record under that key, kept when a world is freed and put back when it's made again.

**Proof it's fixed at the root:**
- Tests start on any body (moon, vacuum world, another star), not just the starter, and every off-world finding above has a check there.
- The merge review fails on a captured zone field, `Game.Surface` outside world management, or a stored position or ID without a world key.

**Order:** the design pass first (the accessor, the ownership split, the world key; brought to the owner), then Root 1, then Root 2. Places per world (#3) is the largest single piece of Root 1.
