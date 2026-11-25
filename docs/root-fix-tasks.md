# Root-fix task list, in priority order

**25 Nov 2026.** This is the work that makes every system work on any world, at any star, for any of four sleepers. It turns [ARCHITECTURE.md](ARCHITECTURE.md) section 12 into jobs that can be briefed. Nothing starts until the owner approves ARCHITECTURE.md and there is quota.

**Priority (owner, 25 Nov): "FIX CORE ISSUES FIRST".** These root fixes outrank all other work, the playtest list included. Owner bugs that come from these causes (weapons, tool and building off-world, door glitches, the compass) are fixed here, not patched sooner.

**Core first, clean-ups around it (owner, 25 Nov).** The point of this list is fixing the core issues: Tiers 1 to 4. Tier 0 exists only to prove them (T0.1a, T0.2, T0.3, T0.4, T0.5). Clean-ups (T0.1b, T0.6, T5.2, stale worktrees, the history clean) fit into the gaps around the core work and never hold it up.

**Revised 25 Nov after a second look:**
- the prototype (now T0.4) runs in Tier 0;
- identity (T2.9, T3.1) starts beside the world model;
- worlds only freeze in T2.4 and are freed in T3.4;
- each task writes its own failing checks first.

**How to read it:**
- **Order:** work top to bottom. A task starts only when the tasks it depends on have merged. Tasks in the same tier with no dependency on each other can run side by side, about 3 agents at once.
- **Each task gives:**
  - what it closes (audit finding numbers, and owner's playtest items in capitals);
  - what it depends on;
  - its proof (the checks that must pass, off the starter);
  - its size (S under a day, M a few days, L a week or more);
  - the agent effort.
- **Finding numbers:**
  - `OW#n` = [offworld-audit.md](offworld-audit.md)
  - `SA xx` = [structure-audit.md](structure-audit.md)
  - `SF` = [silent-failure-audit.md](silent-failure-audit.md)
  - `TC` = [test-coverage-audit.md](test-coverage-audit.md)
- **Every task:**
  - is a root fix, merged green on test-build;
  - deletes what it replaces in the same merge;
  - is reviewed against ARCHITECTURE.md section 10;
  - passes the moving-picture check on its own branch before it merges, because test-build is the owner's build now (one branch);
  - leaves the game playable.
- **Size:** an L task is split into slices of one subsystem each, one agent job per slice. One focused job on the shot damage frame cost used 366k tokens, and a second used 440k.
- **Frame-sensitive tasks** (T1.1, T2.2, T2.4, T2.7, T4.1, T4.5) pass the frame check both quiet and live.
- **Every brief carries:**
  - never touch the main checkout;
  - tests quit themselves, at most 4 Godot runs, through the queue;
  - random seeds;
  - tight code;
  - no external assets.

---

## Tier 0: the proof first
Nothing else can be shown fixed without these. **Order:** T0.2 and then T0.5 first, because every later gate and T0.4's numbers run on them. Then T0.1a, T0.1b and T0.4 side by side, then T0.3. T0.6 can run any time.

**T0.2 Tools into the repo** (S, low effort; FIRST)
- **Moves in:**
  - the test queue scripts (`test-all-q.ps1` and the rest);
  - the owner's settings file (`owner-settings.cfg`);
  - the quiet frame check (`quietpop.ps1`). Its parameters are made explicit: today it hard-codes the Director's scratchpad paths, writes the "lead" pause, waits for Godot to exit, then runs PopRoute `--check=1 --settings=... --seed=`.
- **Also:**
  - the same-seed A/B step, with the baseline taken from test-build's reflog;
  - PopRoute gets a `--live` pass;
  - the gate passes `--airless`;
  - one min-spec flag shared by every test: CombatTest `--minspec=1` and PopRoute's own stand-in become one;
  - timing checks become load-aware. PhysicsTest's 2 ms physics-step check fails under machine load with no code change (2.17 to 2.23 ms), so the gate would flap;
  - logs written as UTF-8 (PowerShell `*>` writes UTF-16).
- **Closes:** TC, the Director's notes.
- **Proof:** the gate and the moving-picture check run from repo tools alone, on a branch, before its merge.

**T0.5 One scripted merge gate** (M, medium effort; after T0.2)
- **What:** one script that every merge runs, so the owner's playtest stops being the only safety net between merges.
- **Steps:**
  1. Rebuild every project, so a stale DLL can't fake a result (owner rule).
  2. Run the test suites through the queue, at most 4 Godot runs.
  3. Give every scene a hard timeout. A scene that doesn't quit itself fails the run and reports its PID to the owner. Nothing is killed (owner rule).
  4. Run the frame check on the branch.
  5. Scan the diff for the ARCHITECTURE.md section 10 patterns: a `readonly` world field, `Game.Surface` or `Game.Survivor` in simulation code, a `+=` on a world outside `Attach`, `?.` on a system, a direct `GetWorld3D()` query, string ID compares, new statics with state.
- **Today:** `tools/test-all.ps1` and the queue scripts exist, but they're run by hand. The rebuild and quit-yourself rules live only in briefs and memory.
- **Proof:** a branch with a stale DLL, a hanging test or a captured zone fails the gate on its own.

**T0.6 Repo hygiene** (S, low effort; mostly done 25 Nov)
- **Done (owner, 25 Nov: "that should get a clean"):**
  - the 16 zero-byte stray `.cs` files in the main checkout (7 in the root, and 9 empty copies of files that have content on test-build) were removed;
  - `asset-notes/` was committed;
  - `.gitignore` now keeps out new screenshots (`prototypes/*/screenshots/`, `shots/`), `scratchpad/`, and the working folders the owner chose to keep local: `docs/font/`, `docs/mockups/`, `docs/music-samples/` and the research PDFs.
- **Screenshots from now on:** a shot a README or the gallery needs is added on purpose with `git add -f`. The gallery's images live in `docs/gallery/`. Agents' proof shots go to their scratchpad, never into git.
- **Left for later:** `.git` is about 930 MB, and 482 MB of that is 904 PNGs in history. The owner chose to stop the growth now and not rewrite history, since a rewrite means re-cutting every branch and the roughly 250 worktrees. Revisit once fewer branches are live. Pruning stale worktrees goes with it.

**T0.1a Test harness and probe** (M, high effort; after T0.2)
- **Builds:**
  - `WorldKey` as a typed value (game seed, star key, orbital path, layer). Only the type here: storage comes in T3.1.
  - `WorldChoice` taking a start `WorldKey`.
  - `Harness` beside Driver.cs with `Arrive(body)`, `AddSleeper(world)` and `SaveAndReload()`. The last two are stubs that fail loudly until T2.3 and T3.3.
  - The world probe (every system present, `Ready`, attached).
- **Closes:** TC harness gaps.
- **Proof:** any existing suite runs started on a moon, a vacuum world, orbit and another star.

**T0.1b One shared `Check`** (S, low effort; after T0.2)
- **Replaces:** the 81 copies of `Check` with one in the shared testing code. This is mechanical.

**T0.3 Clean up the tests that hide bugs** (S, medium effort; after T0.1a and T0.1b)
- **Rewrites:** the two tests that assert bugs: SleeperToolTest.cs:286-297 (coreless block back whole) and ClimateTest.cs:466 (tornado felling bypass).
- **Removes:** about 30 silent passes. They retry up to N rolls, then fail with the seeds.
- **Stops:** travel tests calming weather and freezing wildlife before they leave.
- **Sorts the known flaky and old failures** into a list with causes, so they don't hide new ones:
  - SleeperToolTest fails different checks each run (the door; 8 core-aim fails, likely a rebuild mid-run; tree felling);
  - WeaponTest "the burnt one is frightened" fails 1 in 102;
  - OrbitTest's two body-direction fails (0.46°) are older and unrelated.
- **Closes:** TC.
- **Rule for every later task:** its first step writes failing checks for the findings it closes, off the starter. It merges when they pass. Checks aren't written for all findings up front, because some need harness features that come later (two sleepers in T2.3, save and reload in T3.3).

**T0.4 Prototype: several real worlds at once** (M, high effort; after T0.2, beside T0.1a and T0.1b)
- **On a throwaway branch that never merges.**
- **What:** 2 to 4 real slice worlds made by the game's own generator (the starter plus a moon, and more), with wildlife live, places and streaming running. Not empty scenes: a prototype without the game's load measures nothing useful. Also tried, per DESIGN.md 1578:
  - one Godot world each, with per-world physics spaces;
  - a simulation-only mode for the undrawn ones;
  - a multi-world PopRoute route: one sleeper drawn and walking, the others simulating live.
- **Measures:** in quiet windows only (it takes the pause file, as quietpop does, so it's scheduled); on the 4-core stand-in, lowest preset, Mobile renderer, from today's measured baseline. It records:
  - FrameSplit and SystemSplit keyed by world;
  - garbage per world's tick (`GC.GetAllocatedBytesForCurrentThread` round each tick);
  - physics step time per space (needs manual stepping, or engine work);
  - RAM for a world loaded, frozen and freed, including the full collection on free;
  - GPU per layer from PopRoute.Gpu, as a ratio only (there's no real GTX 1050, and the stand-in limits only the CPU).
- **Known numbers from the warp work,** to beat or spread out:
  - a new zone's first ticks: 140 to 260 ms;
  - adding a physics space: 140 to 320 ms (cabin worlds are already their own Jolt spaces);
  - waking or parking a zone: 37 to 175 ms.
- **Sets:** the per-world tick budget, the thread plan and the shares of the process-wide budgets (`FramePacer`, `UploadBudgetMs`, `GcPacer`).
- **Then:** two things go to the owner: the engine options (physics spaces on threads, a no-render World3D), and the largest orbit zone that still holds precision.
- **This is a decision gate. Tier 2 doesn't start before it.**

---

## Tier 1: small root fixes for live bugs
Independent of each other once Tier 0 is in.

**T1.1 A per-world build context instead of statics** (M, high effort)
- **Replaces:**
  - `PoiSiting.StartWorld`, `WorldForce.Current` and `PoiSite.Loading` with a context passed into `PlanetWorld.Build`, made from the world key;
  - plus a generation check on every worker job: `Place.RebuildStale`, the `DepositFinder` recount, `TerrainStreamer` voxel and height jobs, world builds.
- **Closes:**
  - SA C-A1 (every moon and star world is built as a start world);
  - C-A2 (the forced roll on every world);
  - C-A3 and C-A4 (counters and races).
- **Also swept:** `WildlifePatch.RollAhead` (the destination's first wildlife patch) and `MakeOrbit`'s wreck shapes built on a worker (`StructureShapes.Hold`/`Return`). Neither has a generation check.
- **Proof:** a moon and another star's world roll their own profile and keep their forced roll off. Two worlds building at once give the same result as one at a time. After the `TerrainStreamer` changes, rerun the fly route's column-wait p95: a streamer change regressed fly on 14 Nov (fixed in 5987c13f).

**T1.2 Attach and Detach with handles** (M, high effort)
- **Replaces:** `Vessel.Register`, `Handover.Move` and `SurfaceZone.Remove`'s hand-kept lists with `Attach(world)` returning a handle, and `Detach(handle)`. Every `+=` on a world moves inside one.
- **Closes:**
  - SA C-B1 (vessel weather subscription leaks; ships never struck by lightning on another world), with the `VesselDamage.OnWeather` guard deleted;
  - C-B2 (shots in flight jump on another world's frame shift);
  - C-B3 (SkyTraffic).
- **Force fields re-bind** their emitters on `Attach` and `Detach`, so fields on a ship that crosses worlds keep working.
- **Proof:** a ship flown to a stormy moon is struck there. A freed hulk is gone from every list. Shots in flight in orbit stay on their line across a frame shift.

**T1.3 One settings object, and rules per world** (S, medium effort)
- **Replaces:**
  - mode and Creative living in three flags and copied into every world and machine, with one game-level settings object read when acting;
  - planet and climate rules, which become the world's own, read from its spec.
- **Closes:**
  - SA C-C1, C-C2, A-3.6;
  - OW risk 2 (the suit's temperature clamp is the starter's);
  - **MOONS: NO RINGS, SMALL ONES AIRLESS**, which becomes a world fact from the spec (`HasAir`).
- **Proof:** switch to Creative while away, arrive at a world made earlier, and its machines charge nothing. A frozen moon isn't clamped to the starter's range.

**T1.4 Every world has every system** (M, high effort; depends on T0.1a's probe)
- **Builds:** orbit and every other zone get the same systems as a surface, with empty data and named facts (`HasAir`, `HasLife`, `HasWater`, ground present). This replaces stand-ins and nulls.
- **Deletes:**
  - every `Zone?.Planet == null` early return: ArmsHost.cs:110, BlockDoors:166, ForceFields:62, SkyLight:91, PoiPlaces:127/144, RuinLoot:36, Underworld:57, PlaceFauna:599, Hulks:61;
  - `?.` on required systems.
- **Also:**
  - Join `Progress.Earned` to the visor's XP line.
  - Make the Fold.cs:473 restore failure keep its record and block the commit.
  - Orbit's empty weather still gives the `Strike` and `Events` API, with no hits. (`Hazard` and `Hazards` are already gone.)
  - **Air is one fact per world, not a bool.** Today two oxygen sources disagree:
    - Bestiary's `BadAir` (Vacuum or Oxygen < 15, shared/Bestiary/Core/Conditions.cs:72) uses archetype figures;
    - the sky's `OxygenByAir` gives Ash 13% and Sulfurous 6%.

    They become one oxygen figure per world, from its spec. `HasAir` is derived from it. T4.4 adds the query at a point.
- **Closes:**
  - SF (about 105 hiding calls);
  - **NO WEAPONS IN SPACE** in part: shots and NPC weapons never step in orbit;
  - SA C-G1 (empty stand-in wildlife and weather).
- **Proof:** the world probe passes on every body. Shots step in orbit. A missing system throws in tests.

---

## Tier 2: the world model
In slices, each merged on its own, within the budgets T0.4 set.


**T2.2 Each world its own World3D, and `WorldQuery`** (L, high effort; depends on T0.4)
- **Builds:** every world in play gets its own physics space and scene. Every ray and shape query goes through that world's `WorldQuery`, with named masks.
- **Replaces:**
  - about 15 direct `GetWorld3D().DirectSpaceState` calls;
  - ArmsHost's `RayCast3D` node;
  - the separate AI sight masks;
  - parking by clearing collision layers.
- **Deletes all of today's parking:** `Spaceflight.Park` (Spaceflight.cs:481) is `SetProcess(false)` plus `Hold` (out of physics) plus `Shelve`, with the Run, Park and Prepare piece machinery and the `CanProcess()` skip at :493.
- **Frames:** `FrameAt` (Spaceflight.Jump.cs:260) switches the frame to the body whose reach you arrive in, and zones shift frames. That breaks "simulation positions never shift". Each world gets one fixed frame, within the orbit-zone size the owner signs off from T0.4.
- **Also per world:** our own A* grids (`NavGrid`, `SpaceNav` and the town walking grid). Pathing leftovers wait on this: getting stuck indoors, cliffs.
- **Closes:** architecture review finding 1; SA B-B19, B-B20.
- **Proof:** two occupied worlds never collide or hit each other's rays. Parking pauses a space without touching layers.

**T2.3 Sleepers, the four cryopods and the local view** (L, high effort; depends on T1.2)
- **Builds:** a `Sleeper` object owning body, suit, pack, progress, tools, shots' `Source`, death bag and the open container. `Game` holds the list and "the local sleeper".
- **Replaces:** `Game.Survivor` in simulation code: wildlife's one `Player`, towns' one "you", shots' `owner == 0` and static `You`, the crossing's `Carrier()`, death and aftermath.
- **Also:** all four cryopods are live, each the wake point and first respawn of its own sleeper, with its own locker (owner decision 4).
- **One breather model** for sleepers and people (hold, tank, suffocation, from survival-vitals/data/vitals.json), not a rule owned by the sleeper's suit. Today a closed town room gives people air but not the sleeper: `Suit.InSealedRoom` (Suit.cs:43) is set only by RoomAir. proto/npcair (a0a0da94, shelved) has people reading the sleeper's rules through `Town.Breathe`, which is a starting point, not a merge.
- **Closes:** SA A-R1 (A-1.1 to 1.9), A-6.3; `Harness.AddSleeper` goes live.
- **Proof:** two sleepers on one world are both targeted, hurt, credited and saved. Each wakes in their own pod.

**T2.4 The world manager, interest points and freezing** (L, high effort, 3 slices: the world manager split out of `Spaceflight` (Spaceflight.cs 775 lines, Worlds 297, plus parking); streaming and collision per point; wildlife, weather and places per point; depends on T2.2, T2.3)
- **Builds:**
  - Each world keeps its interest points: a sleeper, an occupied cabin, a destination being prepared.
  - Streaming, collision, wildlife, weather hazards and places run per point. Collision is streamed apart from drawing.
  - A world with no points freezes at once (owner). It stays loaded and frozen until T3.4. Freeing comes only once the change record (T3.2) and the safe ledger (T3.3) can bring it back as it was.
  - **A world with an interest point is never freed.** Today `FreeAbroad` (Fold.cs:288, :303) frees every other star's system on every fold, so a second sleeper's fold would free the first sleeper's star.
  - Simulation size comes from game settings, not a client's display sliders.
- **Replaces:**
  - the one `Focus` and one weather `Observer`;
  - `Streamer.CollisionRadius` set from one sleeper (Game.cs:925);
  - parking and freeing by one player's place;
  - the one `Away` and one `Orbit`.
- **Closes:** SA A-R2 (A-2.1 to 2.9), A-3.5 (world manager split out of `Spaceflight`), OW#6, OW#10 (streaming and settings).
- **A frozen or freed world drops its lights, not just its meshes.** At most one shadow-casting `DirectionalLight3D` is in the tree at a time, because all of them share the directional atlas. The stale space sun once took half of it: SpaceSky.cs:208-209 creates it with shadows on, and :426 hides it only while share is 0. PopRoute.Edges already prints the shadow-casting sun count after a landing; that becomes a check.
- **Proof:** two sleepers far apart on one world both have ground and life. Two sleepers at two stars both play. A world nobody is on stops costing frame time.
- **Note:** `ParkHome` stays until T3.4, and so does freeing.

**T2.5 Player systems read `thing.World`** (L, high effort, 3 slices: arms and the tool; building and doors; the view bits (compass, map, music, markers, effects); depends on T2.3, T2.4)
- **Moves onto the sleeper's world:** suit, SleeperTool, BuildTool, ArmsHost, shots and effects (debris, gibs, blood), the doors' animator (`BlockDoors`), force fields, hover bikes, map, music and markers.
- **Deletes:**
  - `Spaceflight.TakeHands`, its "NO BUILDING IN ORBIT" and its stand-in shot;
  - `readonly SurfaceZone` fields everywhere;
  - `Game.Surface` uses outside world management.
- **Also:** the compass shows only on a world's surface, tuned to that world's poles.
- **Closes:**
  - OW#1, #2, #4, #5, #9, #11, #12, #13, #14, #17, #19, #25;
  - **NO WEAPONS IN SPACE** (the rest), **BUILDING IN ORBIT**, **DOORS GLITCH OFF-WORLD**, **COMPASS ONLY ON A WORLD**;
  - the owner's to-do to block getting into command chairs on cores we don't own (a claim check).
- **Force fields stay per vessel:** power draw (`v.Power.AddLoad(Draw(v))`) and `PoweredFor` read the vessel's own power and wreck state, never the world's. The door hook that feeds fields across open doors (`BlockDoors.OpenDoors`) keeps working.
- **E always reaches doors and containers.** A held item never takes E from a door or a container. On vacarms, holding a blueprint off-world let the builder take the aim, and a wreck door's E prompt went blank at 3.0 m. The hands chain is Game/Game.cs:729-750. One E order comes from `WorldQuery`'s Use mask (T2.2).
- **Claiming a wreck** (a core in hand) moves into the building slice as ordinary core placement through the build tool, at the same reach as any E action. Today `Spaceflight.Claim` works at any range; vacarms made it UseReach+1, derelicts only. Wreck salvage moves to T4.3.
- **Proof:**
  - weapons, the tool and building work in orbit, on a vacuum moon and at another star;
  - doors on any ship open and animate cleanly off-world;
  - the suit reads the world you're on;
  - OrbitTest's door and "box: take-all" step passes with a blueprint in hand.
- **Test lessons from vacarms (6aca3cad), reused here:**
  - in orbit the test's aim hit its own Skiff cabin at 2.1 m, so "0 hits" proved nothing: the sleeper floats clear before aiming;
  - the moon has no wildlife sim until T1.4;
  - TestRows needs a row that can be placed in space.
- **Note:** vacarms' MoonTest.Hands checks are reused here.

**T2.6 The places stack per world** (L, high effort; depends on T1.1, T2.4)
- **Builds:** `PoiPlaces`, `RuinLoot`, `Hulks`, `SkyTraffic`, `Caves` and `Underworld` made with each world, not once on the starter. The starter's bunker gets no special rules.
- **Closes:**
  - OW#3, #18, #20; SA C-E1 to E3;
  - InsideTest `--unseen` "nothing of a cave's sim in a place's dug rooms", which fails on plain batch2 (18 Nov): caves and the underworld bleed into places.
- **Towns fit their world's air** (DESIGN.md 362): sealed habitats on vacuum and unbreathable-air worlds, with suits worn outside. On an airless test world only 67 of 150 town spots stood in closed rooms. NPC air comes back as part of this (owner, 24 Nov), not before.
- **Proof:**
  - a moon and another star's world have their own towns, ruins, people and caves, rolled with their own profile;
  - a quiet town on a fresh vacuum world runs 15 minutes with no air deaths.

**T2.7 Simulation and view apart, and cabins** (M, high effort; depends on T2.2)
- **Builds:**
  - Undrawn worlds run simulation only.
  - Engine-wide state (shader globals, environment, `AudioServer` buses) is written only by the drawn world's view.
  - Cabins are made for occupied ships only.
  - Things aboard read `Outside = ship.World`.
  - The three `LightningFx` copies (weather, planet-surface, slice) become one view.
  - **Engine-wide render state has one owner, the drawn world's view.** Today it's written by many places:
    - `SunShadows.Apply` sets the directional shadow atlas, the soft shadow filter qualities and the viewport's positional atlas (SunShadows.cs:63-66). It runs only from `ApplyScene` on a settings or zone change (Game.Settings.cs:112-139).
    - `SkyRig` sets the sun's shadow opacity and enable, the night light's enable, and the shadow skew every frame (SkyRig.cs:94, 608-617), although SunShadows' doc says nothing else touches a light's shadow.
    - Global shader values: `shade_fill` (SkyRig.cs:687), `sky_ground` and `sky_ground_rect` (SkyLight.cs:337-392), `sky_field`, `planet_size`, and `weather_*` from three places (SurfaceWeather.cs:960-963, WeatherRig.cs:239-242, Spaceflight.Fold.cs:353). SurfaceZone.cs:339-343 zeroes them on zone entry, so today they belong to whichever zone entered last, not the drawn one.
  - **Every drawn world's sky gets the shadow settings.** `Suns()` (Game.Settings.cs:125-133) walks only the starter, the current orbit and the space sun.
  - **Shaders:** nothing moves vertices from `CAMERA_POSITION_WORLD` in a shadow-casting shader, because in the shadow pass that is the light's eye. It's fixed in terrain.gdshader:946 and flora.gdshader:51-54. water.gdshader:72 (`round_sink`) has the same pattern: check whether water casts.
- **Closes:**
  - architecture review findings 5 and 7;
  - **DARK DISC AFTER RETURNING FROM ANOTHER PLANET**: engine-wide shadow and environment state left by a world that isn't drawn;
  - **PLANET TO SPACE**: the planet left behind showing a low-detail skin (with T3.4).

  Far trees and horizon voxels follow this rule when built.
- **Proof:** T0.4's budget holds with 4 occupied worlds plus 2 cabins.

**T2.8a Jumps per vessel** (M, high effort; depends on T2.3, T2.4, T2.9 (fold targets are star indices today, `FoldTo(int)`))
- **Moves:** each vessel's jump, target and fold destination onto the vessel, replacing `Spaceflight.Jumping`, `Target` and `FoldTo` (Jump 282, Warp 308 and Fold 506 lines; WarpTest has about 260 checks).
- **Rules:** only the command-chair pilot cancels, with J, during the spool only. Everyone aboard gets the spool warning (owner, 25 Nov).
- **Proof:** two sleepers jump their own ships to different stars at once.

**T2.8b The transit world** (M, high effort; depends on T2.8a, T2.7 (passengers read `Outside`, which becomes the transit world))
- **Builds:**
  - the tunnel as a transit world per jump;
  - commit takes the cabin, hull riders, and ships clamped on or inside it;
  - stepping out mid-tunnel arrives beside the ship.
- **Closes:** ARCHITECTURE.md section 2c.
- **Proof:**
  - a passenger walks out mid-tunnel and arrives beside the ship;
  - clamped ships arrive clamped;
  - **nothing arrives Frozen.** The tunnel's `Hold` (Jump.cs:169) once left ships arriving "landed". ae0d775b's wake-on-turn covered it, but the root (`CheckLanding` settling after the hold) was never traced.
- **Note:** "a crash mid-tunnel reloads at the start" is proven in T3.3.

**T2.8c The climb and descent between surface and orbit** (M, high effort; depends on T2.8a, T2.7)
- **What:** the seam (Spaceflight.Seam.cs, 621 lines) becomes a vessel `Move`, like warp and fold.
- **Its view half goes under rule 12:** the sky fade, the environment swap, the wreck fade through `GeometryInstance.Transparency`, the sky's `eye_lift` and `HoldSurfaceBehind`.
- **Proof:** two sleepers climb and descend at once on one world without either's view affecting the other's.

**T2.9 The galaxy from the game seed** (M, medium effort; depends on T0.1a only, so it runs beside Tiers 1 and 2; before T3.1)
- **Builds:** the galaxy made from the game seed, with the start star and body chosen in it.
- **Replaces:** the home system built by `SkySystem.For(spec)` beside `StarProfile.Of`; the start is generated like any other.
- **Closes:** SA A-6.1, A-6.2, A-5.3 (two wreck-seed schemes, dead `SystemSeed`).
- **Proof:** the start star's system matches what the galaxy gives that star.

---

## Tier 3: identity, records and the safe ledger
Needed before star travel ships and before saves. T3.1 starts as soon as T2.9 is in, beside the world model. T3.2 onward follow T2.4.

**T3.1 `WorldKey` everywhere, IDs and the seed helper** (M, high effort; depends on T0.1a, T2.9)
- **Replaces:**
  - ledger zone names (`surface`, `moon<index>`, `star<n>/…`) and the `StartsWith` checks;
  - star and body list indices;
  - counter IDs;
  - reference-keyed persistent maps: `PoiSite`, `PlaceMemory`, `FlavourRuin`, creatures.
- **Builds:** `Seed(worldKey, siteId, purpose)` for every persistent roll.
- **Closes:**
  - OW#15, #21 to #24 (deposit, milestone, chart and species IDs);
  - SA A-R4 and A-R5, including A-5.1 (the same loot in every copy of a layout) and A-4.4 (creature drops rolled again).
- **Proof:** a reordered generator list doesn't remap anything. Two copies of one layout roll different loot.

**T3.2 A change record per world, and the clock catch-up** (L, high effort; depends on T3.1, T2.4)
- **Builds:**
  - Records for terrain edits per chunk, felled plants, mined deposits, deaths, place memory, salvage progress, touched rocks and things set down.
  - One galaxy clock; regrowth, decay, meteorites and the sky worked out from elapsed time on return, touching only unclaimed records.
- **Closes:** OW#7, SA B-B10 (salvage resets), A-6.4.
- **Proof:** leave a star, come back, and every change is as left, with regrowth by the time away.

**T3.3 The safe ledger** (M, high effort; depends on T3.1)
- **Builds:**
  - force field state (powered, field HP) saved with its vessel, which nobody has checked yet;
  - one commit queue on a worker;
  - a record is gone only when its holder reports it destroyed (death bags included);
  - the `Move` order (commit, detach, attach, with roll-back);
  - a record whose world won't load goes to the faction's last base;
  - devices restored;
  - records dirty-tracked and written in pieces off the main thread.
- **Closes:** OW#8, architecture review finding 3; `Harness.SaveAndReload` goes live.
- **Proof:** crash at every step of a park, a free, two crossings at once and a jump mid-tunnel (it reloads where the jump began). Every count matches on reload.

**T3.4 The start system like any other** (M, medium effort; depends on T3.2, T3.3, T2.4, and T2.5 and T2.6, which remove the roughly 20 starter holds)
- **Builds:** freeing for every world: a frozen world stays loaded about 2 minutes, then is freed to its record.
- **Deletes:** `ParkHome`. The start system is frozen, kept about 2 minutes and freed like any star's.
- **Closes:** rule 3, OW risk 7.
- **Proof:** fold away, come back, and the start world is as you left it.

---

## Tier 4: one path per job
Each unifies a job and deletes the copies.

**T4.1 Harm** (L, high effort, 2 slices; depends on T2.2 and T2.5, because both rewrite ArmsHost. Its arms part may fold into T2.5's arms slice instead.)
- **Builds:** one `Harm` event and an adapter per target kind, with one falloff and one health unit.
- **Must keep:** the owner's 17 Nov damage rules.
- **Replaces:** the three blast copies, the two ram copies, both people sims' damage, and the per-target copies (`ArmsHost.World`, `PoiPlaces.Damage`, `WearPlant`).
- **Weather hits are Harm sources on every target kind.**
  - Today `Game.OnWeather` hurts the player from lightning and debris.
  - `VesselDamage.OnWeather` hurts blocks from lightning only.
  - planet-surface debris does no damage.
  - Nothing weather-borne hits people or creatures.

  Weather hits stay untelegraphed (owner, 24 Nov), and Harm never adds a warning.
- **Exposure is a Harm source too:** suffocation, cold, heat and radiation, for sleepers and people alike. Today npcair takes health off people directly, with a kinetic `Struck` for the killing blow (Town.Air.cs about 55-57), while the sleeper takes it through `Suit.Hit` with its own damage kind. The target adapter carries a "doesn't breathe" flag (`Species.Mechanical` today).
- **Closes:** SA B-B1 to B-B6 (the fuel drum that spares townsfolk; fire and wind that never touch them; health in two units).
- **Proof:** every source hurts every target kind by the same rule.

**T4.2 The block world** (L, high effort; depends on T4.1)
- **Builds:** one interface for `Structure` and `Place`.
- **Unifies:** the three door systems, the two collapse rules and the copied cache-making.
- **Makes:** flavour ruins into Places.
- **A surface query on the interface:** ray to the real wall, solid behind, open floor in front. Its consumers:
  - creature cracks and lairs, which today are placed from room boxes and float (`WallCrack`, PoiFauna.cs:935; CaveFauna.cs:51; drawn by shared/Bestiary/View/DeepView.cs:205-233);
  - the owner's planned real holes;
  - the worm masses, which do this ad hoc today (PoiFauna.cs:957 `Root`, :980 `Faces`, through `ISolidSpace`/`SpaceMath`).

  Creature cracks stay their own nodes, never part of the damage look.
- **Wear stays paint.** Place cracks are surface paint, not damage (owner, 25 Nov):
  - age cracks are painted by the place shader by age, View/MeshBuild.cs:1249;
  - the weak-wall tell is a line figure on one face, Gen/Mesher.cs:914;
  - the unsafe-floor warning is painted round a hole, Gen/Arch/BuildingRooms.cs:400.

  HP damage is its own look, drawn on top. They never mean the same thing, so nothing is drawn twice.
- **Keep:** the hidden-room cracked panel, a secret wall with its own HP (PoiPlaces.Interact.cs:116, :180, :303; Place.Fixtures.cs:334, its draft dust). It survives the merge into one door, cache and collapse path.
- **Keep for later, untouched:** the in-wall crack network (Brain.Deep.cs:160-200, travel within 60 m). The colony half (PoiFauna.cs:176-198) may be superseded by the 24 Nov "insects are decoration" rule.
- **Part-work:** proto/nocracks acea648c, untested and not merged. Re-cut from test-build if it's used.
- **Closes:**
  - SA B-B3, B-B11 to B-B13;
  - **A DOOR WON'T OPEN AGAIN** (the bunker door), **WINDOWS LET YOU THROUGH**, **CORE STRUCTURE DOOR ANIMATION** (17 Nov);
  - SleeperToolTest "a door on someone else's structure is shot out of it", which fails on plain batch2 (18 Nov).
- **Proof:** a POI wall cracks like a ship's. One door behaves the same everywhere.

**T4.3 Salvage** (M, high effort; depends on T4.1, T4.2)
- **Builds:** one yield formula, with the claim check inside.
- **Deletes:** the other six formulas, `SleeperTool.CutPlant` and orbit's hand salvage, which T2.5 hands over here.
- **Closes:**
  - OW#26;
  - SA B-B7 to B-B9, including B-B8 (a coreless hulk taken apart 1:1);
  - SA C-F (four definitions of a wreck).
- **Proof:** the same block gives the same yield on the ground and in orbit.

**T4.4 Shelter** (M, medium effort; depends on T4.2)
- **Builds:** one `Exposure(point)` query, including oxygen % at the point. The air rule moves off the suit (`Suit.Outside`, Suit.cs about 142: no air below `SurfaceWeather.NoAir` density or above the 1,060 m ceiling) onto the world.
- **Closed rooms on every world:** places, ships' cabins and sleepers' bases answer the same closed-room question. Today room air is only `Place.Closed` (Places/Place.Air.cs:28) plus RoomAir's bunker kinds (Player/RoomAir.cs:7).
- **Replaces:** the nine shelter tests.
- **Closes:** SA B-B14 (a 3 m hole counts as shelter).
- **Proof:** one answer for wind, lightning, creatures, rain and the suit at the same spot.

**T4.5 Ground, gravity and vehicles** (L, high effort; depends on T2.2)
- **Builds:**
  - one `Ground` service per diggable body, with `ArmsHost.SolidHit` folded in;
  - the gravity constants replaced by the body's own gravity;
  - the bike as a hover body in the shared physics;
  - one seated state.
- **Closes:** SA B-B15 to B-B18, B-B4 (the bike rams nothing).
- **The bike keeps its feel.** The port must keep:
  - the probe reading the higher of ground and water, with lava not counted as water;
  - the shared spring (`HoverController.Cushion`: 1.2 Hz, damping 0.4, vertical only, push only);
  - `Follow` (tilt keeps heading, no self-turn);
  - hover lift only on a powered core.
- **Proof:** on a low-gravity moon the bike and creatures fall by the moon's gravity. PhysicsTest's HoverCushion no-roll check and its AcrossWater checks still pass.

**T4.6 Events, sound and markers** (M, medium effort; depends on T2.7)
- **Builds:**
  - one `WorldEvent`, which audio, AI hearing (`Noise`, `Gunfire`), visor cues and effects all subscribe to;
  - the four voice pools brought to one set of rules;
  - one `Markers(world, viewer)` source, keeping the owner's 17 Nov marker rule.
- **Closes:** SA B-B21 to B-B24.
- **Proof:** a fuel drum is heard by creatures and towns and shows on the visor. One marker shows the same in all three views.

**T4.7 Shared entry points** (S, low effort)
- **Makes the shared paths the only way in:**
  - `Planet.Felled` private to `Fell`;
  - one inventory `Receive(bill, at, from)`;
  - `Spill` takes a claim.
- **Closes:** SA B-B25 (a tornado-felled tree leaves a ship floating), B-B26 (overflow lost; a stranger's cargo becomes your bag).
- **Proof:** nothing floats after a tornado. A stranger's spill isn't "ours".

---

## Tier 5: facts, not names, and placeholders

**T5.1 Data fields instead of name checks** (M, medium effort)
- **Adds fields for:**
  - hand kind and item use, instead of IDs;
  - weather harm type, instead of a label;
  - one person role (`Role` and `Job` merged);
  - tags for place families, world layers and space-site kinds (`hollow_rock` and asteroid fields included).
- **Closes:** SA C-D1 to C-D7, G2, G3, G4.
- **Proof:** a new vehicle, food, site kind or alien weather works from data alone.

**T5.2 Placeholder decisions** (owner; then S each)
- **Decides:**
  - warship turrets that never fire;
  - raw parts with no use;
  - the placeholder numbers in places.json;
  - orbit fauna and Husks in derelicts, once T1.4 gives orbit real wildlife.
- **Closes:** SA C-H.

---

## Fire waits for these
Fire (planned, not started) is briefed only once these are in: T1.4 (wind and rain on every world), T2.7 (fire effects in the view only), T4.1 (burn damage through Harm), T4.2 (blocks and plants as burnable material), T4.4 (rain, wet and shelter put fire out), and T4.6 (crackle, smoke, AI hearing). Fire hits are never telegraphed.

## After the root fixes
**T6 Host and clients**
- Confirm ARCHITECTURE.md section 9b against `prototypes/coop`.
- Every networked object carries its `WorldKey`, and is sent only to clients on that world.

This is the start of real co-op. It depends on Tiers 2 and 3.
