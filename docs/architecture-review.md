# Adversarial review: docs/ARCHITECTURE.md (draft, 25 Nov)

> Review of ARCHITECTURE.md draft v1 (25 Nov 2026) by a fresh-eyes agent. All 19 findings were folded into draft v2. The director checked findings 1 and 3 against the code and DESIGN.md 1578.

Code checked: G:\GalHack-wt\batch (proto/batch2). Ranked worst first.

## Claims checked

| Doc claim | Result |
|---|---|
| `FreeAbroad` / `ParkHome`, Spaceflight.Fold.cs:288-289 | Correct |
| Fold.cs:473 drops a structure that won't restore, with only a console line | Correct (`GD.PrintErr` then `continue`) |
| ArmsHost returns on `Zone?.Planet == null` | Correct (ArmsHost.cs:110). The same guard is in BlockDoors:166, ForceFields:62, SkyLight:91, PoiPlaces:127/144, RuinLoot:36, Underworld:57, PlaceFauna:599 and Hulks:61, so more than "force fields and doors" go quiet in orbit |
| MoonTest.cs:101-103 calms the weather and freezes wildlife | Correct |
| SleeperToolTest.cs:286-297 asserts that a coreless block comes back whole | Correct |
| ClimateTest.cs:466 passes through the felled-list bypass | Correct |
| ZoneLedger uses the saves core | Correct (`GalHack.Persistence`). It only keeps four types, and it deletes the folder on every new game |
| "`Town.Open`/`Close` and the `Survivor.Zone` setter already work this way" (s5) | **Overstated.** The `Survivor.Zone` setter (Survivor.cs:31-40) swaps one event (`Removing`). It isn't a full Attach. `Town.Close` (Town.cs:441) finds what to unsubscribe from by reading `_places.Zone.Wildlife` again, not the patch it subscribed to. Only Town is a fair model |
| s11.3 "(up to 4, rule 4)" | Points at the wrong rule. Rule 4 in s1 is ownership; the limit of 4 is s11 item 4 |

---

## 1. No world has its own physics or scene, and the doc never asks for one (gap, risk to the frame budget)
- **Where:** s2 world model, s11.3, s12.
- **Problem:** DESIGN.md 1578 says each live zone is "its own Godot world (its own physics, scene and navigation)", and that "a small prototype must confirm that physics runs in worlds that aren't drawn". In the code, every zone shares the one viewport World3D. Parking a zone turns off its processing and "shelves" its bodies by zeroing their collision layers (Spaceflight.cs:481-497 `Park`, `Run`, `_shelved`). The doc lets up to 4 worlds run at once, but never says each one needs its own World3D and physics space.
- **Why it matters:** two occupied worlds in one physics space overlap near the origin. Ships, shots and creatures from world A collide with world B, which breaks "nothing passes through shapes". Also, rays (`GetWorld3D().DirectSpaceState`, used in about 15 places) return hits from both worlds. Every rule in s1 can hold and co-op across worlds still breaks.
- **Fix:** add a rule: "Each world in play owns its own World3D: physics space, navigation and scene root. Queries go only through `WorldQuery` on that space. Parking pauses the space; it never shelves collision layers." Add a step 2a before the world model: the DESIGN 1578 prototype, with 2 to 4 worlds simulating and one drawn, measured on the minimum-spec machine. List the engine options to bring to the owner: physics on its own thread, and the cost per space.

## 2. Ownership mixes up who owns a thing with where it is (contradiction, band-aid)
- **Where:** s3 table, rule 4.
- **Problem:**
  - "Shots in flight" are owned by the sleeper. A shot flies in one world's physics. If a sleeper crosses mid-volley, their rounds would cross with them. This keeps ArmsHost player-owned, which is the cause of C-B2.
  - "Something owned by the faction is kept by the game" says the game keeps the faction's bases and bags. That contradicts s11.1 (empty worlds are freed) and DESIGN 1591 (records belong to a zone).
  - The table gives "bikes and vessels standing there" to the world, but gives "every sleeper's structures" to the faction. A sleeper's ship parked on a moon falls under both.
  - Debris, gibs, blood and effects aren't assigned. s5 lists "effects" as player-owned.
- **Why it matters:** this is the same class of bug as "data with no lasting identity". Agents will copy whichever row they find first.
- **Fix:** split the table into two columns:
  - **Claim** (owner): sleeper, Sleepers faction, NPC faction, or none.
  - **Holder** (location): which world record or cabin the thing is in, or the game.

  Add the rule: "Everything physical, shots and effects included, is held by the world it is in. It carries a `Source` (the sleeper or NPC) for blame and XP. Only non-physical state is held by the sleeper (vitals, pack contents, progress, tech, destination) or by the game (seed, clock, faction record, list of sleepers). Faction-claimed things are held in their world's record. The game keeps only an index of them by ID."

## 3. The ledger is not crash-safe for the new attach, park and free (gap, items lost)
- **Where:** s4b "When it's written", s5 `Move`.
- **Problem:**
  - **Records swept as gone:** `ZoneLedger.Refresh` (ZoneLedger.cs:127-137) deletes as gone every record not in `Holders()`, unless `Kept(zone)` says its zone is out of play. Today `Kept` is true only for star zones (`KeptInLedger`, Fold.cs:446). When worlds park and free by interest points, a world that is parked but not yet freed, or the start system once `ParkHome` goes, will have its records deleted.
  - **Overlapping crossings:** one crossing at a time. `_commit`, `_plan` and `_before` are single fields (ZoneLedger.cs:194-196). Four players crossing at once, or a world writing before it parks while a crossing is in flight, will overlap `PrepareCommit` calls.
  - **Failure after the commit:** the doc says Move = Detach + Attach "with the ledger committing around it", but not in what order. It also doesn't say what happens if Attach fails after the commit has landed.
  - **DESIGN 1583:** "the load audit moves any record pointing at a missing zone to the last base". The doc drops this.
- **Why it matters:** DESIGN 1587 makes item loss a release blocker. These are the exact paths the new model adds.
- **Fix:** add to s4b:
  - "One commit queue. Commits run one at a time on a worker."
  - "A record counts as gone only when its holder reports it destroyed. A missing holder is never gone."
  - "The order is: commit the new holder, then `Detach(from)`, then `Attach(to)`. If `Attach` fails, the thing is put back in `from` with a second commit, and the failure is reported loudly."
  - "A record whose world key won't load is moved to the faction's last base, per DESIGN 1583."
  - Test: crash at every point of a park, a free and two crossings at once, then reload.

## 4. The order of work breaks the game between steps (order)
- **Where:** s12.
- **Problems:**
  - Step 2 removes the starter special case, so `ParkHome` goes and the start system is freed. But the change record that would bring back its terrain edits, place memory, loot taken and deaths arrives in step 3. Doing step 2 as written throws away the start system's state on the first fold.
  - The harness (s9) and required systems (s8) aren't steps at all. Yet s9 says every finding gets a check *before* its fix lands.
  - Step 2 covers suit, tools, BuildTool, arms, doors, bikes, effects, compass, places, interest points and the sleeper list in one go. That is too big to test or merge alone.
  - Nothing says the old and new code may not live side by side during step 4.
- **Fix:**
  - Step 0: the harness, `WorldChoice` and the required-system probe.
  - Step 1 as written.
  - Step 2a: separate worlds (finding 1).
  - Step 2b: the world model in slices, each merged green with the probe: sleeper list, then interest points, then places, then player systems.
  - Step 3: identity and the change record.
  - Only then remove `ParkHome`.
  - Add the rule: "a replacement merges only together with deleting what it replaces" (the owner's tight-code rule).

## 5. Running four worlds will break the 16.7 ms frame on the minimum spec, and the doc has no per-world budget (risk)
- **Where:** s11.3 ("That budget is checked on the minimum-spec machine"), s4b.
- **Problems:**
  - **More than 4 worlds:** the real peak is higher than "up to 4". Add each occupied ship's cabin world (DESIGN 1582), destinations streamed in ahead (DESIGN 1581), and the about-2-minute grace from DESIGN 1579. Every vessel already builds a `CabinWorld` with a second set of colliders (Vessel.cs:406-409), wrecks included, not only occupied ships.
  - **Visuals on worlds nobody sees:** there is no split between simulation and view inside a world. On undrawn worlds the sky field flood (SkyLight), foliage, render meshes, sound, effects and `HushStep` must not run.
  - **Engine-wide settings:** weather and sky write engine-wide shader values (`weather_wind`, `weather_ground`, `weather_flash`, `sky_field`, `planet_size`: SurfaceWeather.cs:960-963, SkyLight.cs:337-392, SurfaceZone.cs:339-343), and sound changes engine-wide buses (`AudioServer`). With 4 worlds ticking, whichever weather ran last sets the host's picture.
  - **Writes on the main thread:** `Refresh` serialises every holder on the main thread at each commit. The ledger's own comment (Fold.cs:487-490) says saving one wreck's blueprint at every commit "was 140 ms a fold". "A world writes its records before it parks" multiplies that.
  - **No threading plan:** the owner's "use all cores" rule says split heavy work between threads, but nothing says which thread runs which world.
- **Fix:** add a section "Running several worlds":
  - "A world that isn't drawn runs simulation only. View systems exist only for the drawn world, made in that world's view attach."
  - "Engine-wide state (shader values, environment, audio buses) is written only by the drawn world's view."
  - "Records are dirty-tracked and written in small pieces off the main thread. Nothing serialises a whole world in one frame."
  - "Per-world tick budget: ___ ms. Extra worlds run AI and wildlife at a lower rate. Which thread runs which world: ___."
  - "Gate: 4 occupied worlds plus 2 cabins on the 4-core GTX 1050 stand-in, with the lowest preset and headroom."

## 6. Rule 1 contradicts rule 5 and the checklist; "stores a world" isn't defined (too vague)
- **Where:** rule 1, s2 ("each knows its world"), s5, s10 first bullet.
- **Problem:** rule 1 says nothing stores a world past one call, yet every thing "knows its world" (`Vessel.Zone`, `Survivor.Zone`). Detach has to unregister from exactly what Attach registered with. Workers that build a world (Place, SkyLight and PlaceFauna use `Task.Run`) hold it for many frames. An agent can read the rule as banning all three, or as allowing any field "for now".
- **Why it matters:** the checklist can't be applied the same way twice, and the vacarms-style re-pointing patch slips back in.
- **Fix:**
  - "Each world-held thing has exactly one `World` field, set only by `Attach`/`Detach`/`Move`."
  - "`Attach` returns a handle that holds every registration. `Detach` disposes that handle, never by looking the world up again."
  - "Systems acting for a thing read `thing.World` each call and keep no copy."
  - "Worker jobs carry the world key plus a generation number, and drop their result if the world was freed or rebuilt."

## 7. Ship cabins and sleepers aboard are missing from the model (gap)
- **Where:** s2 tree, s4b "or a ship's cabin", s5.
- **Problem:** the cabin is a real private world in the code (`CabinWorld`) and in DESIGN 1582. The tree has no place for it. A sleeper aboard has two worlds: the frame they move in (the cabin) and the place outside (the air, weather, exposure and gravity of the ship's world). When the ship crosses, DESIGN says passengers "ride through zone changes untouched". The doc's `Move` would detach and reattach every passenger's suit, tools and arms, or none of them; it doesn't say which. NPC crew aboard aren't mentioned.
- **Fix:**
  - Add "Cabin" under World in the tree.
  - Rule: "A thing aboard has `Frame = cabin` and `Outside = ship.World`. When a ship moves, only the ship goes through the ledger. Things aboard re-attach only their outside-world registrations, in one call from the ship's `Move`."
  - Make cabins per occupied ship, as DESIGN 1582 says, not per vessel.

## 8. "Each viewer's floating origin" contradicts DESIGN and can't work on the host (contradiction)
- **Where:** s2 Frames.
- **Problem:** DESIGN 1580 says the host pins each zone's physics origin at its centre, with no floating origin needed. On the host, one physics space per world can't have one origin per viewer: two sleepers on one world share the bodies. Orbit zones recentre on arrival (`o.Recentre(at)`, Fold.cs:277), and the doc says nothing about how big an orbit or space zone can be.
- **Fix:** "The host simulates each world in its own fixed frame, centred on the zone (DESIGN 1580). Only clients offset their drawing. Simulation positions never shift. An orbit zone's size limit is set so precision holds."

## 9. The server and client split is missing (gap)
- **Where:** s4b ("clients are sent the differences"), rule 3.
- **Problem:** the doc mixes up who writes the save with who simulates and what goes over the network. DESIGN 1550 and 1532 say the host owns world state, and each player's own movement and shots are trusted. DESIGN 1581 says clients load only their own zone and every networked object carries a zone ID. DESIGN 1088 says positions inside a ship are sent relative to the ship. The doc doesn't say where suit vitals, arms or a seated vehicle are simulated, and doesn't mention prototypes/coop.
- **Fix:** add a table: system, simulated where, sent how. The rule: "Simulation runs on the host. Client-trusted inputs are listed by name. Every networked object carries a world key and is sent only to clients on that world."

## 10. s1 contradicts s8 on optional systems; empty systems can hide failures like `?.` did (contradiction, band-aid)
- **Where:** rule 9 ("no weather in a vacuum" given as an optional absence) against s8 ("an orbit has a weather system that holds no weather").
- **Problem:** two different rules. An empty stand-in class (offworld C-G1 "empty stand-in wildlife") brings the silent no-op back. "Callers that must wait are written to wait" doesn't say how.
- **Fix:**
  - Delete the vacuum example from rule 9.
  - "Empty = the same class with empty data from the generator, never a stub class."
  - "A world is attachable only once every required system is `Ready`, so no caller ever waits or checks."

## 11. Regrowth contradicts itself and DESIGN (contradiction)
- **Where:** s4 ("Regrowth runs on its timers while no one is near") against s11.1 ("Nothing ticks"). Also DESIGN 1579 (an empty zone stays live for about 2 minutes), DESIGN 706 (resource meteorites below about 10% deposits), and DESIGN 1592 (regeneration touches only what was never player-owned).
- **Fix:**
  - "Regrowth, meteorite rolls and decay are worked out from elapsed galaxy-clock time when the world is made, using `Seed(worldKey, …, elapsed bucket)`."
  - "They touch only records with no claim."
  - "Grace before parking: ___ s." Then update DESIGN 1579 to match the owner's freeze.

## 12. NPC vessels and references across worlds (gap)
- **Where:** s11.2.
- **Problem:** "Sleepers' vessels are the only travellers the world manager moves" also bans NPC ships moving between the surface and orbit of one body. SkyTraffic exists, and pirates follow you up. There is also no rule for a reference to a thing in a world that has been freed: AI targets, markers, `Opened` containers, the `_ids` map keyed by object (Fold.cs:399, 466).
- **Fix:**
  - "Any vessel may `Move` between two worlds that are both in play. Only travel between stars by NPCs goes unsimulated."
  - "A reference across worlds is an ID resolved through the world manager. A world that is out of play gives back 'not here', never a stale object."

## 13. "Any vessel or system that asks" keeps worlds alive without limit (too vague, risk to the budget)
- **Where:** s2 interest points.
- **Fix:** list them: "a sleeper; an occupied cabin; a destination being prepared, for N seconds. Nothing else. Bases, turrets and parked ships never hold a world."

## 14. The world key is underspecified (too vague)
- **Where:** s4.
- **Problem:**
  - The surface and the orbit of one body are two worlds, but the key is per body.
  - The star key is a list index today (`StarZoneOf(int star)`).
  - Rocks in a field need keys too.
  - The ledger and `IsStarZone` work out what a zone is by matching name prefixes with `StartsWith` (ZoneLedger.cs:284, Fold.cs:440). That is "facts, not names" broken again.
- **Fix:** "`WorldKey` is a typed value: galaxy seed, star seed key, orbital path, and layer (surface, orbit, cabin, rock). It is never parsed back from a string. Code reads its fields."

## 15. Per-world rules are mixed in with game-wide settings (gap)
- **Where:** rule 10.
- **Problem:** offworld risk 2 says `Rules.ClimateNode` and `Rules.Planet` are shared by every world, so the suit's clamp stays the starter's. Rule 10 could be read as "put it all in the game object".
- **Fix:** "Game data (items, blueprints) is game-wide. Planet and climate rules come from the world's spec, read through `thing.World`."

## 16. Gaps in the merge checklist
- **Where:** s10.
- **Fix:** add:
  - writing engine-wide state (shader values, `AudioServer`, environment) outside the drawn world's view;
  - parsing a world key or ID from a string;
  - a reference to a thing across worlds;
  - a worker job without a world generation check;
  - a new implementation merged without deleting the old one.

## 17. Test rules (too vague, conflicts with an owner rule)
- **Where:** s9.
- **Problem:**
  - "Rolls again until it finds one" has no limit. That breaks the owner's "tests must quit themselves" and the hung-process problem.
  - No test covers two sleepers on two worlds at once, a crossing while another world parks, or the multi-world frame budget.
- **Fix:**
  - "Up to N rolls, then fail with the seeds."
  - Add `AddSleeper` on a second world, plus a soak test of 4 worlds. Each counts as one Godot run in the queue.

## 18. Over-engineering
- **Per-viewer floating origin** (finding 8): heavy and wrong. Drop it.
- **The release-build "refuses item-holding actions" mode** (s8): a second code path that adds a quietly degraded state. Prefer "the world won't attach until it is ready, and a release build stops and offers a backup, as DESIGN 1595 does on load".
- **A folder per asteroid** (`bodies/<body key>/`): with 60 to 200 rocks per field, keep one field record keyed by rock ID, written only for rocks that were touched.
- The others are sized to the problem: `Harm` adapters, `WorldQuery`, `Markers`.

## 19. Engine work isn't weighed
- **Fix:** the owner's rule is that engine work stays on the table. Bring separate physics spaces on threads, and a sim-only World3D with no rendering, as options for the 4-world budget.
