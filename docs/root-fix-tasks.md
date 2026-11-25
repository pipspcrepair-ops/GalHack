# Root-fix task list, in priority order

**25 Nov 2026.** This is the work that makes every system work on any world, at any star, for any of four sleepers. It turns [ARCHITECTURE.md](ARCHITECTURE.md) section 12 into jobs that can be briefed. Nothing starts until the owner approves ARCHITECTURE.md and there is quota.

**Priority (owner, 25 Nov): "FIX CORE ISSUES FIRST".** These root fixes outrank all other work, the playtest list included. Owner bugs that come from these causes (weapons, tool and building off-world, door glitches, the compass) are fixed here, not patched sooner.

**Core first, clean-ups around it (owner, 25 Nov).** Only fixes to broken or structurally wrong code are core tasks (T-numbers). New features found during the reviews are listed apart at the end (F-numbers). The point of this list is fixing the core issues: Tiers 1 to 4. Tier 0 exists only to prove them (T0.1a, T0.2, T0.3, T0.4, T0.5). Clean-ups (T0.1b, T0.6, F5, stale worktrees, the history clean) fit into the gaps around the core work and never hold it up.

**Revised 25 Nov after a second look:**
- the prototype (now T0.4) runs in Tier 0;
- identity (T2.9, T3.1a) starts beside the world model; references across worlds (T3.1b) wait for the world manager;
- worlds only freeze in T2.4 and are freed in T3.4;
- each task writes its own failing checks first.

**How to read it:**
- **Order:** work top to bottom. The list is in build order: every task comes after everything it needs, so nothing waits on a task written below it. A task starts only when the tasks it depends on have merged. Tasks in the same tier with no dependency on each other can run side by side, about 3 agents at once.
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
- **Frame-sensitive tasks** (T1.1, T2.2, T2.4, T2.6, T2.7, T3.4, T4.1, T4.5) pass the frame check both quiet and live.
- **Every brief carries:**
  - never touch the main checkout;
  - tests quit themselves, at most 4 Godot runs, through the queue;
  - random seeds;
  - tight code;
  - no external assets.

---

## Tier 0: the proof first
Nothing else can be shown fixed without these. **Order:** T0.2 and then T0.5 first, because every later gate and T0.4's numbers run on them. T0.5 carries the known-failure list, so the gate is green on today's code from the start. Then T0.1a and T0.4 side by side, then T0.3. T0.1b and T0.6 are clean-ups that fit in any gap and hold nothing up.

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
  - timing checks run only in quiet windows (they take the pause file, as the frame check does). PhysicsTest's 2 ms physics-step check fails under machine load with no code change (2.17 to 2.23 ms), so the gate would flap. The 2 ms threshold is never loosened;
  - logs written as UTF-8 (PowerShell `*>` writes UTF-16).
- **Closes:** TC, the Director's notes.
- **Proof:** the gate and the moving-picture check run from repo tools alone, on a branch, before its merge.

**T0.5 One scripted merge gate** (M, medium effort; after T0.2)
- **What:** one script that every merge runs, so the owner's playtest stops being the only safety net between merges.
- **Steps:**
  1. Rebuild every project, so a stale DLL can't fake a result (owner rule).
  2. Run the test suites through the queue, at most 4 Godot runs.
  3. Give every scene a hard timeout. A scene that doesn't quit itself fails the run and reports its PID to the owner. Nothing is killed (owner rule).
  4. Run the frame check on the branch against the baseline from test-build's reflog (T0.2). Only a regression fails the gate: today's build already fails bike and fly frame checks (ARCHITECTURE.md 2b, 13 fails on 18 Nov), and those stand as the baseline.
  5. Scan the diff for the ARCHITECTURE.md section 10 patterns: a `readonly` world field, `Game.Surface` or `Game.Survivor` in simulation code, a `+=` on a world outside `Attach`, `?.` on a system, a direct `GetWorld3D()` query, string ID compares, new statics with state.
- **Known failures:** built from a full run of test-build when T0.5 merges, so the gate is green on today's code. Every entry gives its cause and its owner: a T task, or "outside the root fixes" with its playtest item. The gate fails only on failures not on the list, and the owner takes its entry off when it fixes it. Known before that run:
  - InsideTest `--unseen` (T2.6), SleeperToolTest "a door on someone else's structure is shot out of it" (T4.2), MoonTest's climb into orbit (T1.6);
  - still to be traced and given an owner by the run: SleeperToolTest's varying fails (the door, 8 core-aim fails, tree felling), WeaponTest "the burnt one is frightened" (1 in 102), OrbitTest's two 0.46° body-direction fails, BestiaryTest 264/268 (playtest-list.md 871), SliceTest's vehicle checks (playtest-list.md 875), LayersTest, SurfaceTest's infrastructure checks and BuildTest's one-base check.
- **Today:** `tools/test-all.ps1` and the queue scripts exist, but they're run by hand. The rebuild and quit-yourself rules live only in briefs and memory.
- **Proof:** a branch with a stale DLL, a hanging test or a captured zone fails the gate on its own.

**T0.1a Test harness and probe** (M, high effort; after T0.2)
- **Builds:**
  - `WorldKey` as a typed value (game seed, star key, orbital path, layer). Only the type here: storage comes in T3.1a.
  - `WorldChoice` taking a start `WorldKey`.
  - `Harness` beside Driver.cs with `Arrive(body)`, `AddSleeper(world)` and `SaveAndReload()`. The last two are stubs that fail loudly until T2.3 and T3.3.
  - The world probe (every system present, `Ready`, attached).
- **Closes:** TC harness gaps.
- **Proof:** any existing suite runs started on a moon, a vacuum world, orbit and another star.

**T0.1b One shared `Check`** (S, low effort; after T0.2)
- **Replaces:** the 81 copies of `Check` with one in the shared testing code. This is mechanical.

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
- **This is a decision gate. T2.2 and the world-model tasks after it don't start before it.** T2.9 doesn't depend on it.

**T0.3 Clean up the tests that hide bugs** (S, medium effort; after T0.1a)
- **Not here:** the two tests that assert bugs are rewritten in the tasks that fix those bugs, T4.3 and T4.7. Rewritten now, they would turn the gate red until Tier 4.
- **Removes:** about 30 silent passes. They retry up to N rolls, then fail with the seeds.
- **Stops:** travel tests calming weather and freezing wildlife before they leave.
- **Moves** any new flaky or old failure it finds onto T0.5's known-failure list, with its cause and owning task.
- **Closes:** TC.
- **Rule for every later task:** its first step writes failing checks for the findings it closes, off the starter. It merges when they pass. Checks aren't written for all findings up front, because some need harness features that come later (two sleepers in T2.3, save and reload in T3.3).

**T0.6 Repo hygiene** (S, low effort; mostly done 25 Nov)
- **Done (owner, 25 Nov: "that should get a clean"):**
  - the 16 zero-byte stray `.cs` files in the main checkout (7 in the root, and 9 empty copies of files that have content on test-build) were removed;
  - `asset-notes/` was committed;
  - `.gitignore` now keeps out new screenshots (`prototypes/*/screenshots/`, `shots/`), `scratchpad/`, and the working folders the owner chose to keep local: `docs/font/`, `docs/mockups/`, `docs/music-samples/` and the research PDFs.
- **Screenshots from now on:** a shot a README or the gallery needs is added on purpose with `git add -f`. The gallery's images live in `docs/gallery/`. Agents' proof shots go to their scratchpad, never into git.
- **Left for later:** `.git` is about 930 MB, and 482 MB of that is 904 PNGs in history. The owner chose to stop the growth now and not rewrite history, since a rewrite means re-cutting every branch and the roughly 250 worktrees. Revisit once fewer branches are live. Pruning stale worktrees goes with it.

---

## Tier 1: small root fixes for live bugs
Once Tier 0 is in, T1.1, T1.2, T1.3 and T1.6 are independent of each other. T1.4 follows T1.3, and T1.5 follows T1.1.

**T1.1 A per-world build context instead of statics** (M, high effort; after T0.1a, which gives `WorldKey`)
- **Replaces:**
  - `PoiSiting.StartWorld`, `WorldForce.Current` and `PoiSiting.Loading` with a context passed into `PlanetWorld.Build`, made from the world key;
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
- **Proof:** a lightning strike on a moon reaches the handler of a ship there. A freed hulk is gone from every list. Shots in flight on a moon stay on their line across a frame shift. (Shots in orbit only step after T2.5, which proves them there.)

**T1.3 One settings object, and rules per world** (S, medium effort)
- **Replaces:**
  - mode and Creative living in three flags and copied into every world and machine, with one game-level settings object read when acting;
  - planet and climate rules, which become the world's own, read from its spec.
- **Closes:**
  - SA C-C1, C-C2, A-3.6;
  - OW risk 2 (the suit's temperature clamp is the starter's);
  - the two settings stores (`user://settings.cfg`, GameSettings.cs:22, and `user://audio-settings.json`, SliceAudio.cs:39) become one;
  - **a core's 1 g is one game constant** (`Units.StandardGravity`), not a planet rule. Today it reads `zone.Data.Rules.Planet.Gravity` (Vessel.cs:322, 360; Vessel.Hulk.cs:86), which this task makes per world, so cores would take a high-g world's pull. A wreck claimed in orbit takes the starter's surface gravity instead (`Claimed(Game.Surface.Physics.Zone.Strength)`, Spaceflight.Hands.cs:239). All four take the constant;
  - **a world's gravity is standard g × its own `GravityScale`** (DESIGN.md 588). Today a body's gravity is the starter's surface gravity scaled by its size against that system's home (`StarSystem.GravityOf`, StarSystem.cs:154, fed `Game.Surface`, Spaceflight.Worlds.cs:119, 129), so another star's worlds are scaled from the starter. `GravityOf` goes.
- **Proof:** switch to Creative while away, arrive at a world made earlier, and its machines charge nothing. A frozen moon's own rules give its temperature range, not the starter's, checked on the world's rules. A world at another star has standard g × its own scale. A core on a high-g world still gives 1 g. (The suit reads them in T2.5, which proves it in play.)

**T1.4 Every world has every system** (L, high effort, 3 slices: every system on every world, with the probe; no swallowed faults and required collaborators; one air figure; faults it uncovers go on T0.5's known-failure list with an owner; depends on T0.1a's probe and T1.3, because air and the other facts come from the world's own spec)
- **Builds:** orbit and every other zone get the same systems as a surface, with empty data and named facts (`HasAir`, `HasLife`, `HasWater`, ground present). This replaces stand-ins and nulls.
- **Deletes:**
  - every `Zone?.Planet == null` early return: ArmsHost.cs:110, BlockDoors:166, ForceFields:62, SkyLight:91, PoiPlaces:127/144, RuinLoot:36, Underworld:57, PlaceFauna:599, Hulks:61, SkyTraffic:108, Game.Scanner:65, BuildTool.Whole:149;
  - `?.` on required systems.
- **Also:**
  - Join `Progress.Earned` to the visor's XP line.
  - Make the Fold.cs:473 restore failure keep its record and block the commit.
  - **No fault is swallowed** (SF R4). A generator or meshing fault is logged with its world key and seed and fails a test run, never dropped quietly: `catch (Exception) { continue; }` with no log (Place.Deco.cs:79, HullVessel.Finish.cs:143, Town.cs:513), `catch (KeyNotFoundException)` that skips or returns false with no log (Town.Rooms.cs:77, PlaceParts.cs:86), and places, hulks, meshes and routes dropped after `PushError` (PoiPlaces.cs:312, Hulks.cs:69, Place.cs:419, Town.cs:367, Town.Runs.cs:94).
  - **Dust reads the world's grit through the world,** not `Zone.Planet.Grit` (ArmsHost.cs:1069; SleeperTool.cs:338, 379, 451; SF #5), which throws once orbit has dust.
  - **Required collaborators are required.** SleeperTool's `Places`, `Debris`, `Sfx`, `Fx`, `Arms` and `Builder` are passed in when it's made, not set afterwards and called with `?.` 30 times (SleeperTool.cs:53-60; SF #10). A missing decorations library or physics `Hover` block fails loudly instead of falling back silently (Place.Deco.cs:36-45, HoverBike.cs:440; SF #11, #12). Hooks that default to stand-ins which look like they work go too, such as `Focus = () => Vector3.Zero` (PoiPlaces.cs:32, Hulks.cs:30, RuinLoot.cs:22, SkyTraffic.cs:31; SF #9): they're passed in when made.
  - **Orbit's loot is made like a surface's:** today orbit's `Lootables` is made without `BodyAt`, `Settle` or `DrawnOf` (OrbitZone.cs:180; SA G1).
  - **Wrongness isn't forced to 0 in space** (Game.Wrongness.cs:52; SA C-G1). It comes from the world, which has no Husks in orbit until they're placed there.
  - Orbit's empty weather still gives the `Strike` and `Events` API, with no hits. (`Hazard` and `Hazards` are already gone.)
  - **Air is one fact per world, not a bool.** Today two oxygen sources disagree:
    - Bestiary's `BadAir` (Vacuum or Oxygen < 15, shared/Bestiary/Core/Conditions.cs:72) uses archetype figures;
    - the sky's `OxygenByAir` gives Ash 13% and Sulfurous 6%.

    They become one oxygen figure per world, from its spec. `HasAir` is derived from it. T4.4 adds the query at a point.
- **Closes:**
  - SF (about 105 hiding calls), SF #9, SF R4 apart from the ledger audit (T3.3);
  - **NO WEAPONS IN SPACE** in part: the guard that stops shots and NPC weapons stepping in orbit goes. The second stop, ArmsHost being a child of the starter zone (Game.cs:320-322), goes in T2.5;
  - SA C-G1 (empty stand-in wildlife and weather, wrongness forced to 0), C-G2 (the scanner's `InSpace` guard, Game.Scanner.cs:65).
- **Proof:** the world probe passes on every body for the zone's systems (T2.6 adds the places rows). A missing system throws in tests. A swallowed generator fault fails a test run.

**T1.5 The same seed builds the same world** (M, high effort; after T0.1a, to build the moon and another star's world; T0.5, so its check runs in the merge gate; and T1.1, because worker threads racing on `StartWorld` and `WorldForce.Current` are one cause)
- **Today:** the world fingerprint test (`SurfaceTest --only=Dump`, 13 Nov) found city planning giving a different city, lots and grounds for the same seed on two runs: 2 of 12 worlds at 16 km and 1 of 4 at 64 km. Storage keeps only the seed and the changes (ARCHITECTURE.md 4b), so a world that rebuilds differently puts its saved changes on the wrong things. Co-op (F4) needs every machine to build the same world too.
- **Finds and fixes the cause:** a step that depends on run order, time or threads (worker completion order, hash or dictionary order, a shared random stream drawn from more than one thread, the clock). Fixed at that step, not by retrying until two runs agree.
- **Not this task:** seeds that change when the generator's order changes (SA 5.2: `SpawnSeed = rng.NextU64()` during placement). That is T3.1a's seed helper. This task is the same code giving two answers.
- **Closes:** playtest "Worlds are not deterministic" (playtest-list.md 585).
- **Proof:** every fresh seed is built twice and fingerprinted, on the start world, a moon and another star's world, at 16 and 64 km, with random seeds each run. The check joins the merge gate (T0.5), so no later task can bring the drift back.

**T1.6 The Skiff's climb into orbit stalls** (S, medium effort; after T0.5)
- **Today:** MoonTest fails "it crossed into orbit" on some worlds, on clean code too: the Skiff climbs at only about 5 m/s (playtest-list.md 875). The entry's "6.8 km" can't be right with the 1,060 m crossing (orbit.json:59), so the job re-measures first. Before a guard in `Bindings.cs` it also threw at line 107 and never built the Skiff. That points at the vessel's climb (thrust, mass or drag against the world's gravity and air), not the surface-to-orbit seam.
- **Finds and fixes the cause** in the vessel's climb, not by easing the test.
- **Proof:** MoonTest's climb passes on fresh worlds, and the entry comes off T0.5's known-failure list.

---

## Tier 2: the world model
In slices, each merged on its own, within the budgets T0.4 set.


**T2.9 The galaxy from the game seed** (M, medium effort; depends on T0.1a, and T1.4 so new air values feed one oxygen figure, not today's two; needed by T2.8a and T3.1a)
- **Builds:** the galaxy made from the game seed, with the start star and body chosen in it.
- **Replaces:** the home system built by `SkySystem.For(spec)` beside `StarProfile.Of`; the start is generated like any other.
- **One star model.** Today there are two: the galaxy's `StarKind` (12 kinds, shared/Galaxy/Core/Galaxy.cs:8, with only a brightness) and the system's `StarClass` (5 values, planet-surface/src/Sky/SkySystem.cs:8), which is what lights a world. `SkyRig.ClassOf` (SkyRig.cs:826) folds 12 into 5, so blue giants, pulsars, magnetars and black holes all become blue-white, and red giants become orange. `StarClass` and both mappings are deleted. Each star carries the physical numbers its class gives: temperature, radius, mass and brightness, from solar-systems.md step 1's star table. Subclasses and the wider spread are F7. Colour, light, its size in the sky, the zones and the worlds' temperatures all come from those numbers.
- **The start is picked the right way round.** Today the planet is chosen first and the star rolled to suit it (`SkySystem.PickStar`, SkySystem.cs:167-176), which can give a start a red dwarf, white or blue-white star. solar-systems.md says start systems are always G or K.
- **Closes:** SA A-6.1, A-6.2, A-5.3 (two wreck-seed schemes, dead `SystemSeed`); **MOONS: NO RINGS, SMALL ONES AIRLESS**: rings and air are rolled in the system model from the body's kind and size. This changes only how the planet's spec gets its values; T1.4's one oxygen figure already reads the spec.
- **Proof:** the start star's system matches what the galaxy gives that star. A red giant, a white dwarf and a black hole each keep their own kind through to the sky (F1 then draws how a black hole lights). Small moons are airless and no moon has rings.

**T2.2 Each world its own World3D, and `WorldQuery`** (L, high effort, 3 slices: a World3D per world with today's parking deleted; `WorldQuery` for every ray and shape query; one fixed frame and the A* grids per world; depends on T0.4 and Tier 1: worlds built from their own context (T1.1), things moved between worlds by handles (T1.2), and every world with every system (T1.4))
- **Builds:** every world in play gets its own physics space and scene. Every ray and shape query goes through that world's `WorldQuery`, with named masks.
- **Replaces:**
  - the direct `DirectSpaceState` calls (about 29 sites in slice and shared code);
  - the static `WorldRay` (Game/WorldRay.cs, about 23 to 27 uses, with its static `Ignored` list), so it doesn't survive as a second ray path;
  - ArmsHost's `RayCast3D` node;
  - the separate AI sight masks;
  - parking by clearing collision layers.
- **Builds the pause for a world's space** and moves `Park` and `ParkHome` onto it. Today's parking is deleted: `Spaceflight.Park` (Spaceflight.cs:481) is `SetProcess(false)` plus `Hold` (out of physics) plus `Shelve`, with the Run, Park and Prepare piece machinery and the `CanProcess()` skip at :493. T2.4 changes only what triggers a pause.
- **Frames:** `FrameAt` (Spaceflight.Jump.cs:260) switches the frame to the body whose reach you arrive in, and zones shift frames. That breaks "simulation positions never shift". Each world gets one fixed frame, within the orbit-zone size the owner signs off from T0.4.
- **Also per world:** our own A* grids (`NavGrid`, `SpaceNav` and the town walking grid). Pathing leftovers wait on this: getting stuck indoors, cliffs.
- **Closes:** architecture review finding 1; SA B-B19, B-B20, A-2.2 (frame shifts).
- **Proof:** two occupied worlds never collide or hit each other's rays. Parking pauses a space without touching layers.

**T2.3 Sleepers, the four cryopods and the local view** (L, high effort, 5 slices: the sleeper object and shots' `Source`; claims and the faction record; the four pods and respawn; world things out of the suit; the breather; depends on T1.2)
- **Builds:** a `Sleeper` object owning body, suit, pack, progress, tools, shots' `Source`, death bag and the open container. `Game` holds the list and "the local sleeper".
- **Replaces:** `Game.Survivor` in simulation code: wildlife's one `Player`, towns' one "you", shots' `owner == 0` and static `You`, the crossing's `Carrier()`, death and aftermath.
- **Builds the claims** (ARCHITECTURE.md section 3): every lasting thing has a claim (a sleeper, the Sleepers faction, an NPC faction or none), kept apart from where it is held. The Sleepers faction record holds structures, vessels, bags, charts and standing. Today nothing records this: `Spaceflight.Claim` only takes a wreck. T2.5's command-chair check, T3.3's "the faction's last base", T4.3's yield and T4.7's `Spill` all read it. A destroyed core changes its claim: today `CoreDestroyed` (PoiPlaces.Interact.cs:327-335) has no subscriber, so shooting out a faction core changes nothing (SF #19).
- **Also:** all four cryopods are live, each the wake point and first respawn of its own sleeper, with its own locker (owner decision 4). Respawn is per sleeper, not hard-wired to the bunker (decided-vs-built.md 2.2). The other respawn choices there (home base, clone chamber, tent) are features built on this later.
- **World things come out of the sleeper's models** (SA A-R3, S7, A-3.1 to 3.4). Today the suit's survival sim (`SurvivalSim`) holds the world's replicators, pods, bushes and food caches as bare positions and counters, `Death` keeps backpacks on one suit's clock, and charts and travel state sit in `Spaceflight` (`Charts.Mark` has no world). The survival sim keeps only vitals. Replicators, pods, caches, bags and respawn devices become world things, held by the world they're in, so they take frame shifts like anything else (A-3.3). Charts and standing go to the Sleepers faction record.
- **Room air comes off the suit too.** Today every room's air lives in the one sleeper's suit (`_roomsAir` and `_airOf`, Suit.cs:69-70): two sleepers in one cabin each keep their own copy, people and creatures can't read it, it's never saved, and a room's air only moves while that sleeper stands in it. Each room's `RoomAir` is held by its structure or place, keyed by the room, and advanced on the world's clock. The suit's `Breathe` (Suit.cs:253-300) moves out with it. T4.4 then makes these nested air volumes.
- **One breather model**, not a rule owned by the sleeper's suit (hold, tank, suffocation, from survival-vitals/data/vitals.json). Built for sleepers now, in a shape people can use later. It breathes the air of the room it's in, held by that room's structure or place; T4.4 then answers which volume a point is in. Today a closed town room gives people air but not the sleeper: a closed place sets the suit's room to `Closed` with its RoomAir (Suit.cs:290-297), but `InSealedRoom` is set in `Suit.Tick` (Suit.cs:314) only from structure rooms or the bunker.
- **Closes:** OW#1 (respawn in the wrong world); SA A-R1 (A-1.1 to 1.9), A-R3 (A-3.1 to 3.4), A-6.3; `Harness.AddSleeper` goes live.
- **Proof:** two sleepers on the start world are both targeted, hurt and credited (on other worlds once T2.4 hooks their wildlife to every sleeper). Each wakes in their own pod. Two sleepers in one cabin share one room air, and a person in that room reads it too. (Saving both is proven in T3.3.)

**T2.4 The world manager, interest points and freezing** (L, high effort, 3 slices: the world manager split out of `Spaceflight` (Spaceflight.cs 775 lines, Worlds 297, plus parking); streaming and collision per point; wildlife, weather and places per point; depends on T2.2, T2.3, and T1.3 for simulation size from the game settings)
- **Builds:**
  - Each world keeps its interest points: a sleeper, an occupied cabin, a destination being prepared.
  - Streaming, collision, wildlife, weather hazards and places run per point. Collision is streamed apart from drawing.
  - A world with no points freezes at once (owner). Until T3.4, other stars are still freed as they are today and the start system stays parked, so memory doesn't grow with every star visited. Freeing every world the same way comes in T3.4, once the change record (T3.2) and the safe ledger (T3.3) can bring it back as it was.
  - **A world with an interest point is never freed.** Today `FreeAbroad` (Fold.cs:288, :303) frees every other star's system on every fold, so a second sleeper's fold would free the first sleeper's star.
  - Simulation size comes from game settings, not a client's display sliders.
  - **Wildlife population doesn't depend on the path walked** (A-2.8). The roll is keyed by the world and the patch's cell, not the patch origin the player happened to arrive at (`Rng.For(Fauna.Seed ^ (Origin.X*31 + Origin.Z), "population")`, WildlifePatch.cs:366). The refills (:453) are keyed by a running count, so they depend on the path too.
  - **Making a world ready never costs one long frame.** Approaching the moon today gives one frame of 18 to 24 ms when its terrain streamer is set up (`TerrainStreamer.SetWorld`, playtest-list.md 838). Setting up a world's streaming is split across frames, within the budget T0.4 sets.
- **Replaces:**
  - the one `Focus` and one weather `Observer`;
  - `Streamer.CollisionRadius` set from one sleeper (Game.cs:925);
  - what triggers parking and freeing: one player's place today, interest points after (the pause itself is T2.2's);
  - the one `Away` and one `Orbit`.
- **Closes:** SA A-R2 (A-2.1, A-2.3 to 2.9; A-2.2, the frame shift, is T2.2), A-3.5 (world manager split out of `Spaceflight`), OW#6, OW#9's weather observer, OW#10's streaming and streamer settings (its shadow settings are T2.7).
- **A frozen or freed world drops its lights, not just its meshes.** The only shadow-casting `DirectionalLight3D`s in the tree are the drawn world's, one per star you can see (binary and trinary systems, owner design; see F1). They share the directional atlas by brightness, and a stale light must never take part of it. The stale space sun once took half of it: SpaceSky.cs:208-209 creates it with shadows on, and :426 hides it only while share is 0. PopRoute.Edges already prints the shadow-casting sun count after a landing; that becomes a check.
- **Proof:** two sleepers far apart on one world both have ground and life. A ship flown to a stormy moon is struck there, with the moon's weather centred on its sleeper. Two sleepers on a moon are both targeted by its wildlife. A world nobody is on stops costing frame time. (Two sleepers at two stars is proven in T2.8a, which builds jumps per vessel.)
- **Note:** `ParkHome` and today's freeing of other stars stay until T3.4.

**T2.6 The places stack per world** (L, high effort, 3 slices: places, ruins and hulks per world; caves and the underworld; sky traffic; depends on T1.1, T2.4)
- **Builds:** `PoiPlaces`, `RuinLoot`, `Hulks`, `SkyTraffic`, `Caves` and `Underworld` made with each world, not once on the starter. The starter's bunker gets no special rules.
- **Closes:**
  - OW#3, #18, #20; SA C-E2, C-E3 (C-E1, the bunker as the suit's only shelter, is T4.4);
  - InsideTest `--unseen` "nothing of a cave's sim in a place's dug rooms", which fails on plain batch2 (18 Nov): caves and the underworld bleed into places;
  - bases already keep creatures clear (`WildlifePatch.SyncBases`, WildlifePatch.cs:524, read by `WildSim.InBaseZone`); that comes with each world's wildlife. decided-vs-built.md 1.7 was stale. If creatures still spawn beside a base in play, it is traced as its own bug;
  - `SkyTraffic` (the lone trader or scout, playtest-list.md 739) is made once, on the starter, today. It is made with each world instead, and reads how much traffic that world has from the world itself; none is a valid answer. What the traffic is (busy, empty, hostile) is feature F6, not this task.
- **Caves and undergrounds per world** (line numbers from test-build):
  - **The bunker is an ordinary structure with no owner** (SA Root E); its dug-out ground is a terrain stamp like any place's. Delete `Keep` and the bunker rules (`Game.Places.cs:66`; consumed at `PlaceFauna.cs:571, 639, 704`). Part of the cave bleed is already fixed: 4acacb45 makes `LoadedVoxels` read `PlanetTerrainSource.Dug` as rock. Rerun InsideTest `--unseen` first to see what still fails.
  - **`Underworld` serves every underground on a world,** not only the start town's (`Underworld.cs:68`, :102). `IncidentalTest.cs:206-211`, which counts only the start town's underworld, also counts a moon's.
  - **Life underground comes from the world's roster, not its air.** `Fauna = Zone.Wildlife.Airless || … ? null` (Game.Places.cs:50) assumes an airless cave is empty. It reads the roster instead; on airless worlds the roster is empty until vacuum fauna (owner: later).
  - **The underworld and ruin caches use the spawn distance setting** (Surface/SpawnReach.cs), not a fixed 90/150 m (Underworld.cs:44) and 70 m (RuinLoot.cs:25).
  - **Caves follow the world's archetype.** `CaveStrength` (`PlanetTerrainSource.cs:766`) takes none, but DESIGN.md 213 gives cave networks to only some archetypes. It reads T1.3's rules from the spec.
  - **Stamps are planned before a world's first chunk column is read.** Otherwise the `_columns` cache (`PlanetTerrainSource.cs:61`) bakes in no cave mouths, because `CaveMouths` returns null while `Stamps.Count == 0` (`CaveMouths.cs:45`, :54).
- **Proof:**
  - a moon and another star's world have their own towns, ruins, people and caves, rolled with their own profile;
  - the world probe's places rows pass on every body;
  - InsideTest `--unseen` passes;
  - a moon's undergrounds are served by its own `Underworld`, and its cave mouths are in the first chunk columns read;
  - an airless world's caves take their life from its roster.

**T2.5 Player systems read `thing.World`** (L, high effort, 3 slices: arms and the tool; building and doors; the view bits (compass, map, music, markers, effects); depends on T2.3, T2.4, and T2.6, because weapons' fire lines and music read the world's places (SimArms.cs:111, 186; OW#3, #17))
- **`Arming`'s statics become per world** (shared/Bestiary/Sim/Arming.cs:13-22: `Reach`, `Fire`, `BlowDamage`, `Masked`), set by each world's arms, not by closures on one ArmsHost. Unset today, armed creatures quietly fall back to melee (SF #21).
- **Moves onto the sleeper's world:** suit, SleeperTool, BuildTool, ArmsHost, shots and effects (debris, gibs, blood), the doors' animator (`BlockDoors`), force fields, hover bikes, map, music and markers.
- **Deletes:**
  - `Spaceflight.TakeHands`, its "NO BUILDING IN ORBIT", its stand-in shot and its hand salvage (Spaceflight.Hands.cs:97). Orbit salvage runs on the ground path until T4.3 makes one yield formula;
  - `readonly SurfaceZone` fields everywhere;
  - `Game.Surface` uses outside world management.
- **Also:** the compass shows only on a world's surface, tuned to that world's poles.
- **Closes:**
  - OW#2, #4, #5, #11, #12, #13, #14, #17, #19, #25, and OW#9's hits reaching you (its observer is T2.4, its shelter T4.4);
  - **NO WEAPONS IN SPACE** (the rest), **BUILDING IN ORBIT**, **DOORS GLITCH OFF-WORLD**, **COMPASS ONLY ON A WORLD**;
  - the owner's to-do to block getting into command chairs on cores we don't own (a claim check).
- **Force fields stay per vessel:** power draw (`v.Power.AddLoad(Draw(v))`) and `PoweredFor` read the vessel's own power and wreck state, never the world's. The door hook that feeds fields across open doors (`BlockDoors.OpenDoors`) keeps working.
- **E always reaches doors and containers.** A held item never takes E from a door or a container. On vacarms, holding a blueprint off-world let the builder take the aim, and a wreck door's E prompt went blank at 3.0 m. The hands chain is Game/Game.cs:729-750. One E order comes from `WorldQuery`'s Use mask (T2.2).
- **Claiming a wreck** (a core in hand) moves into the building slice as ordinary core placement through the build tool, at the same reach as any E action. Today `Spaceflight.Claim` works at any range; vacarms made it UseReach+1, derelicts only.
- **Proof:**
  - weapons, the tool and building work in orbit, on a vacuum moon and at another star, and shots step in orbit;
  - doors on any ship open and animate cleanly off-world;
  - the suit reads the world you're on, including a frozen moon's temperature range;
  - OrbitTest's door and "box: take-all" step passes with a blueprint in hand.
- **Test lessons from vacarms (6aca3cad), reused here:**
  - in orbit the test's aim hit its own Skiff cabin at 2.1 m, so "0 hits" proved nothing: the sleeper floats clear before aiming;
  - TestRows needs a row that can be placed in space.
- **Note:** vacarms' MoonTest.Hands checks are reused here.

**T2.7 Simulation and view apart, and cabins** (M, high effort; depends on T2.2, and T2.3 and T2.4, because cabins are made only for ships with a sleeper aboard)
- **Builds:**
  - Undrawn worlds run simulation only.
  - Engine-wide state (shader globals, environment, `AudioServer` buses) is written only by the drawn world's view.
  - Cabins are made for occupied ships only.
  - Things aboard get `Outside = ship.World`, which T4.4's lookup uses as a cabin's parent.
  - A base isn't a cabin world: it never moves, so it stays a fixed body in its world (Vessel.cs:339-341). T4.4's lookup finds it by position.
  - The three `LightningFx` copies (weather, planet-surface, slice) become one view.
  - **Engine-wide render state has one owner, the drawn world's view.** Today it's written by many places:
    - `SunShadows.Apply` sets the directional shadow atlas, the soft shadow filter qualities and the viewport's positional atlas (SunShadows.cs:63-66). It runs only from `ApplyScene` on a settings or zone change (Game.Settings.cs:112-139).
    - `SkyRig` sets the sun's shadow opacity and enable, the night light's enable, and the shadow skew every frame (SkyRig.cs:94, 608-617), although SunShadows' doc says nothing else touches a light's shadow.
    - Global shader values: `shade_fill` (SkyRig.cs:687), `sky_ground` and `sky_ground_rect` (SkyLight.cs:337-392), `sky_field`, `planet_size`, and `weather_*` from three places (SurfaceWeather.cs:960-963, WeatherRig.cs:239-242, Spaceflight.Fold.cs:353). SurfaceZone.cs:339-343 zeroes them on entering a zone with no weather sim, so today they belong to whichever zone entered last, not the drawn one.
  - **Every drawn world's sky gets the shadow settings.** `Suns()` (Game.Settings.cs:125-133) walks only the starter, the current orbit and the space sun.
  - **Shaders:** nothing moves vertices from `CAMERA_POSITION_WORLD` in a shadow-casting shader, because in the shadow pass that is the light's eye. terrain.gdshader:946 and flora.gdshader:51-54 already follow the rule. water.gdshader:72 (`round_sink`) is brought under it too, and the merge gate's diff scan (T0.5) flags the pattern in any shader.
- **Closes:**
  - architecture review findings 5 and 7; OW#10's shadow settings (`Suns()`);
  - **DARK DISC AFTER RETURNING FROM ANOTHER PLANET**: engine-wide shadow and environment state left by a world that isn't drawn;
  - **PLANET TO SPACE**: the planet left behind showing a low-detail skin (with T3.4).

  Far trees and horizon voxels follow this rule when built.
- **Proof:** T0.4's budget holds with 4 occupied worlds plus 2 cabins. After a return from another world, only the drawn world's suns cast shadows (PopRoute.Edges' shadow-casting sun count). The weather shader globals are the drawn world's, whichever zone was entered last.

**T2.8a Jumps per vessel** (M, high effort; depends on T2.3, T2.4, T2.9 (fold targets are star indices today, `FoldTo(int)`), and T2.5, T2.6 and T2.7, so both sleepers can play at their stars)
- **Targets are star keys** (T0.1a's `WorldKey`, from T2.9's galaxy), not list indices.
- **Moves:** each vessel's jump, target and fold destination onto the vessel, replacing `Spaceflight.Jumping`, `Target` and `FoldTo` (Jump 282, Warp 308 and Fold 506 lines; WarpTest has about 260 checks).
- **Rules:** only the command-chair pilot cancels, with J, during the spool only. Everyone aboard gets the spool warning (owner, 25 Nov).
- **Proof:** two sleepers jump their own ships to different stars at once, and both play there.
- **Lines up with INTERSTELLAR TRAVEL** (playtest-list.md 704; rules in solar-systems.md section 8). Before T2.8a to c and T2.9 are briefed, that entry's open jobs are checked against them, so jumps are built once, on this design.

**T2.8b The transit world** (M, high effort; depends on T2.8a, T2.7 (passengers read `Outside`, which becomes the transit world))
- **Builds:**
  - the tunnel as a transit world per jump;
  - commit takes the cabin, hull riders, and ships clamped on or inside it;
  - stepping out mid-tunnel arrives beside the ship;
  - the transit world has its own named facts (T1.4): no air and 0 g, so anyone outside the cabin reads them.
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

---

## Tier 3: identity, records and the safe ledger
Needed before star travel ships and before saves. T3.1a starts as soon as T2.9 is in, beside the world model. T3.1b follows T2.4. T3.2 onward follow T2.4.

**T3.1a `WorldKey` everywhere, IDs and the seed helper** (M, high effort; depends on T0.1a, T2.9)
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
- **Note:** the ledger's zone names live in Spaceflight.Worlds.cs, which T2.4's first slice moves into the world manager. That part of this task merges after that slice, so the two don't collide.

**T3.1b References across worlds as IDs** (M, high effort; depends on T3.1a, T2.3 and T2.4, because references are resolved through the world manager)
- **Replaces:** references to things in another world with IDs resolved through the world manager: AI targets, markers, `Opened` containers and the fold's `_ids` map keyed by object (Fold.cs:399, 466). A world out of play answers "not here", never a stale object.
- **Closes:** architecture review finding 12.
- **Proof:** a creature targeting a sleeper on a world that is then freed lets go. A marker for a thing on a freed world shows "not here".

**T3.2 A change record per world, and the clock catch-up** (L, high effort, 4 slices: the record and its storage; terrain, plants and deposits; places, caves and survival points; the clock catch-up; depends on T3.1a, T2.4, T2.6 (caves and places made per world), and T1.5, because changes are stored against a world rebuilt from its seed)
- **Builds:**
  - Records for terrain edits per chunk, felled plants, mined deposits, deaths, place memory, salvage progress, touched rocks and things set down, plus the survival points and bags T2.3 moves out of the suit (replicators, pods, bushes, food caches, backpacks), and the air of places' closed rooms.
  - **Caves:** found caves and their deaths, by cell key. Today `Caves._caves` and `Underworld._spots` live only in nodes (`PlaceFauna.cs:584`, `Underworld.cs:37`), and which caves exist depends on the walk (:630-640). A freed world's caves would refill and its kills be undone, against the owner's "find things as you left them". The cave sims' time comes from the galaxy clock (`PlaceFauna.cs:640`, `Underworld.cs:113` copy the zone's).
  - One galaxy clock; regrowth, decay, meteorites and the sky worked out from elapsed time on return, touching only unclaimed records.
- **Closes:** OW#7, SA B-B10 (salvage resets), A-6.4.
- **Proof:** leave a star, come back, and every change is as left, with regrowth by the time away.

**T3.3 The safe ledger** (M, high effort; depends on T3.1a, T1.2 (`Move` is commit, detach, attach), T2.4 (parking and freeing) and T2.8b (its proof crashes mid-tunnel))
- **Builds:**
  - force field state (powered, field HP) saved with its vessel, which nobody has checked yet;
  - each room's air (oxygen, temperature, time unpowered; T4.4 adds pressure) saved with its structure;
  - one commit queue on a worker;
  - a record is gone only when its holder reports it destroyed (death bags included);
  - the conservation audit (`Ledger.Audit`) refuses and rolls back a crossing or jump that loses or duplicates items, instead of only printing (Spaceflight.cs:677, Jump.cs:251);
  - the `Move` order (commit, detach, attach, with roll-back);
  - a record whose world won't load goes to the faction's last base;
  - devices restored;
  - records dirty-tracked and written in pieces off the main thread.
- **Closes:** OW#8, architecture review finding 3; `Harness.SaveAndReload` goes live. With T3.2 this is the save system: playtest "Saves" (playtest-list.md 915; the saves prototype isn't in the slice) is closed by these two, not built apart.
- **Proof:** crash at every step of a park, a free, two crossings at once and a jump mid-tunnel (it reloads where the jump began). Every count matches on reload. Two sleepers are both saved and both come back.

**T3.4 The start system like any other** (M, medium effort; depends on T3.2, T3.3, T3.1b (nothing may hold a freed world's objects), T2.4, and T2.5 and T2.6, which remove the roughly 20 starter holds)
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
- **Exposure is a Harm source too:** suffocation, cold, heat and radiation. Today the sleeper takes it through `Suit.Hit` with its own damage kind. The target adapter carries a "doesn't breathe" flag (`Species.Mechanical` today), so people use the same path when F2 gives them air.
- **Closes:** SA B-B1, B-B2, B-B5, B-B6, and B-B4's two ram copies (the fuel drum that spares townsfolk; fire and wind that never touch them; health in two units). B-B3 is T4.2; the bike half of B-B4 is T4.5.
- **Proof:** every source hurts every target kind by the same rule.

**T4.2 The block world** (L, high effort, 3 slices: the shared interface and its surface query; doors and collapse; caches and flavour ruins as Places; depends on T4.1)
- **Builds:** one interface for `Structure` and `Place`.
- **Unifies:** the three door systems, the two collapse rules and the copied cache-making.
- **Makes:** flavour ruins into Places.
- **A surface query on the interface:** ray to the real wall, solid behind, open floor in front. Its consumers:
  - creature cracks and lairs in places, which today are placed from room boxes and float (`WallCrack`, shared/Bestiary/Sim/PoiFauna.cs:992). Cave wall cracks were removed on 24 Nov (dcb7052d), so `CaveFauna`'s leftover `sim.Cracks` read (shared/Bestiary/Sim/CaveFauna.cs:56) is deleted;
  - cave sims, which use the voxel `ISolidSpace` through `SpaceMath` today, a working model for the query;
  - the owner's planned real holes;
  - the worm masses, which do this ad hoc today (PoiFauna.cs:1014 `Root`, :1037 `Faces`, through `ISolidSpace`/`SpaceMath`).

  Creature cracks stay their own nodes, never part of the damage look.

  The query:
  - runs read-only on a worker and never generates chunks (`Caves.Search` is `Task.Run`; `LoadedVoxels` returns solid for unbuilt chunks, `PlaceFauna.cs:707`);
  - can answer "this cell belongs to another body" (today's `Kept`/`Dug` test, `PlaceFauna.cs:704`);
  - works in a sim's local frame (integer `_offset`, `NearestCopy`/`Canonical`).
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
- **First step:** SleeperToolTest.cs:286-297, which asserts that a coreless block comes back whole (B-B8), is rewritten to the right rule. It fails until this task's fix.
- **Deletes:** the other six formulas and `SleeperTool.CutPlant`.
- **Closes:**
  - OW#26;
  - SA B-B7 to B-B9, including B-B8 (a coreless hulk taken apart 1:1);
  - SA C-F (four definitions of a wreck).
- **Proof:** the same block gives the same yield on the ground and in orbit.

**T4.4 Shelter and air: air volumes like cabin worlds** (L, medium effort, 4 slices: volumes and the `Exposure` query; nesting and openings; life support on the power model; the bunker and the shelter tests; depends on T4.2, T1.4 for the world's air, T2.7 for ships' cabins, and T3.3, which saves the rooms' air)
- **The model (owner, 25 Nov: air follows the cabin worlds' logic).** A ship owns its cabin world and anything aboard reads the cabin, then `Outside`. Air works the same way:
  - **every sealed volume owns its air:** a ship's or base's room, a closed room in a place, the bunker. It holds oxygen, temperature and pressure, and is kept with its structure or place;
  - **volumes nest:** each has a parent, another volume or the world's own air. A ship parked inside a sealed base room reads that room, which reads the planet;
  - **openings link a volume to its parent:** an open door or a hull breach. Air equalises through them, so a holed room drains to its parent (vacuum in orbit);
  - **life support regulates oxygen and temperature in every enclosed space** of its structure (owner, 25 Nov): powered, a sealed room eases to full oxygen and 23 °C (owner, 25 Nov: a regulated space sits at 23 °C), as `RoomAir.Advance` does today (RoomAir.cs:27-36), though today's figure is 20 °C (slice.json `sealedRoomC`, GameData.cs:223); off, it holds a while, then drifts to its parent's. The device already exists: the life support pod (`HasLifeSupportPod` and `Vessel.LifeSupport`, Vessel.cs:146-160; playtest LIFE SUPPORT IN CORE STRUCTURES). It feeds the volumes on the power model; no new device. A place's closed room without one only shelters part of the way from the outside temperature, as today;
  - **area sources** such as the survival climate pod (9 m, fuelled) warm a radius in the open; they're not volumes.
- **Builds one lookup, "what am I inside"**, used by air here and by gravity in T4.5: for a point or a thing, the innermost structure and sealed volume it's in, then the parent. A volume's parent is the volume or structure around it; a ship's is the ship it's docked on (`ParkedOn`, PhysicsWorld.cs:126, 151), else `Outside`; a base's is its world. A base is found by position, since people on a base aren't riding it.
- **Builds:** one `Exposure(point)` query on that lookup: the innermost volume at a point, then its parents, then the world. Sleepers, people, creatures, weather and later fire (F3) all ask it. The air rule moves off the suit (`Suit.Outside`, Suit.cs about 142: no air below `SurfaceWeather.NoAir` density or above the 1,060 m ceiling) onto the world.
- **Closed rooms on every world:** places, ships' cabins and sleepers' bases answer the same closed-room question. Today room air is only `Place.Closed` (Places/Place.Air.cs:28) plus RoomAir's kinds (Player/RoomAir.cs:7), and the bunker is a hard-coded full shelter (Suit.cs:104). The bunker becomes a powered sealed structure like any other.
- **Replaces:** the nine shelter tests, `RoomKind` and the suit's room check (`InRoom`, Suit.cs:211-235). `Breathe` already left the suit in T2.3.
- **Closes:** SA B-B14 (a 3 m hole counts as shelter), OW#9's `Shelter` and `Enclosed`, C-E1 (the suit's shelter check knows only the bunker).
- **Proof:**
  - one answer for wind, lightning, creatures, rain and the suit at the same spot;
  - two sleepers in one cabin read the same air; opening its door in orbit drains it to vacuum for both;
  - a ship parked inside a sealed base room on an airless moon reads the room's air;
  - with life support powered, a sealed room on an ice world or a lava world eases to full oxygen and 23 °C; with it off, the room holds, then drifts to the outside's;
  - a room's air is the same after a save and reload. T4.4 adds pressure to the room record T3.3 saves.

**T4.5 Ground, gravity and vehicles** (L, high effort, 3 slices: the gravity rule; the `Ground` service and the cave sims' one ground source; the bike and the seated state; depends on T2.2, T2.5 (the bike and digging moved per world), T2.7 (when cabins exist), T4.1 (`ArmsHost.SolidHit` rewritten) and T4.4, whose "what am I inside" lookup it reads)
- **Builds:**
  - one `Ground` service per diggable body, with `ArmsHost.SolidHit` folded in;
  - **one gravity rule, read from where a thing is** (like air, T4.4):
    - a world gives its own gravity: a planet or moon its own, the orbit zone 0 (OrbitZone.cs:166);
    - **gravity follows cabin-world logic** (owner, 25 Nov): inside a functioning ship or base you're at its core's fixed 1 g, whatever the world outside, so 1 g aboard on a high-g world. With a core off or unpowered, gravity comes from its parent, as air does: the structure it sits inside (a ship with its core off in a powered carrier gets the carrier's 1 g), else the world, turned into the ship's frame. Today `PhysicsWorld.GravityAt` falls back to the world only (PhysicsWorld.cs:222). Out on the hull, mag boots hold you to the face underfoot;
    - **bases too:** a base is walked on in its world, not a cabin (Vessel.cs:339-341), so people on a base get the world's gravity today and its core gravity does nothing. **Gravity follows the structure, air follows its sealed rooms** (owner, 25 Nov): a base's 1 g covers anyone inside its own bounds, standing on its floors, the same as anyone aboard a ship. Blow the roof off and the air vents, but you stay at 1 g while the core runs; lose the core or its power and gravity drops to the parent's. The outside of its roof and walls is its outer hull, as on a ship;
    - **asteroids:** a diggable body's own gravity (ARCHITECTURE.md 2) is the world's gravity for anything on it;
    - characters already work this way (`PhysicsWorld.GravityAt`, PhysicsWorld.cs:215-222; power drops it, Vessel.Power.cs:38-41). Loose things in the cabin world don't: with the core off they get 0 g even on a planet (`CabinWorld.SetGravity`, StructureShapes.cs:167-173). One rule serves both;
    - everything reads gravity from where it is, through T4.4's lookup: bodies, creatures, loot, people's falls, flung trees, the bike and claimed wrecks. The hard-coded fallbacks go (Town.Falls.cs:39 `?? 9.8f`, Lootables.cs:100's standard gravity off a surface); `Units.StandardGravity` stays as the core's 1 g (T1.3);
  - the bike as a hover body in the shared physics;
  - one seated state.
- **Closes:**
  - **a bug today:** loose cargo in a cabin floats with the core off, even parked on a planet (StructureShapes.cs:167-173);
  - **a bug today:** loose things in a powered cabin in orbit drift instead of falling at 1 g, because loot reads its zone's gravity (0 in orbit, which is right in open space) and the world's down, never the ship's (Lootables.cs:100, 290); town falls default to 9.8 (Town.Falls.cs:39);
  - **a bug today:** a base's core gravity does nothing (see bases above);
- **Also closes:** SA B-B15 to B-B18, and B-B4's bike half (the bike rams nothing, through T4.1's one ram).
- **One ground source for cave sims too.** Today they read three:
  - the voxel store plus `Streamer.IsReady` (`PlaceFauna.cs:707`);
  - the analytic `planet.GroundHeight` (`PlaceFauna.cs:651`, `Underworld.cs:111`);
  - `Zone.GroundAt` (`PlaceFauna.cs:637`).
- **Ground edits reach every sim on that body.** `Edits.Edited` feeds only `Places.Dug` and `Wildlife.Dug` (`Game.Places.cs:54`, `SurfaceZone.cs:268`). A dug floor is never seen by a cave's `WNav`, which is built once (`PlaceFauna.cs:660`).
- **The bike keeps its feel.** The port must keep:
  - the probe reading the higher of ground and water, with lava not counted as water;
  - the shared spring (`HoverController.Cushion`: 1.2 Hz, damping 0.4, vertical only, push only);
  - `Follow` (tilt keeps heading, no self-turn);
  - hover lift only on a powered core.
- **Proof:** on a low-gravity moon the bike and creatures fall by the moon's gravity. A bag tossed in orbit drifts; tossed in a powered cabin it falls at 1 g, and with the core's gravity off it drifts. Aboard a powered base on a high-g world you're at 1 g, and still are after its roof is blown off, while its air vents. A ship with its core off parked in a powered carrier is at the carrier's 1 g. Aboard a powered ship on a high-g world you and its loose cargo are at 1 g; switch its core gravity off and both take the world's. PhysicsTest's HoverCushion no-roll check and its AcrossWater checks still pass.

**T4.6 Events, sound and markers** (M, medium effort; depends on T2.7, and T2.5, which moves markers onto the sleeper's world)
- **Builds:**
  - one `WorldEvent`, which audio, AI hearing (`Noise`, `Gunfire`), visor cues and effects all subscribe to;
  - the four voice pools brought to one set of rules;
  - `SoundCues.Heard` (Game/SoundCues.cs:13-15) carries its world, so the visor shows only the viewer's world's cues (SA A-1.11);
  - every event has a subscriber or is deleted (SF #20 lists the unused ones);
  - one `Markers(world, viewer)` source, keeping the owner's 17 Nov marker rule. The playtest marker entries (playtest-list.md 804 to 807, 817: markers on the thing, discovered POIs, scanner pings) are built on this source, not each on their own.
- **Closes:** SA B-B21 to B-B24; OW#16 (the bunker marker and "home" checks against `Game.Surface`, Visor.cs:415, Spaceflight.Moon.cs:37).
- **Proof:** a fuel drum is heard by creatures and towns and shows on the visor. One marker shows the same in all three views.

**T4.7 Shared entry points** (S, low effort; after T2.3, which builds the claims `Spill` takes)
- **Makes the shared paths the only way in:**
  - `Planet.Felled` private to `Fell`;
  - one inventory `Receive(bill, at, from)`;
  - `Spill` takes a claim.
- **First step:** ClimateTest.cs:466, which asserts the tornado felling bypass (B-B25), is rewritten to the right rule. It fails until this task's fix.
- **Closes:** SA B-B25 (a tornado-felled tree leaves a ship floating), B-B26 (overflow lost; a stranger's cargo becomes your bag).
- **Proof:** nothing floats after a tornado. A stranger's spill isn't "ours".

---

## Tier 5: facts, not names

**T5.1 Data fields instead of name checks** (M, medium effort)
- **Adds fields for:**
  - hand kind and item use, instead of IDs;
  - weather harm type, instead of a label;
  - one person role (`Role` and `Job` merged);
  - tags for place families, world layers and space-site kinds (`hollow_rock` and asteroid fields included).
- **Closes:** SA C-D1 to C-D7, G3, G4.
- **Proof:** a new vehicle, food, site kind or alien weather works from data alone.

---

# Built on the core: features, not fixes

These came up during the reviews. They are **new behaviour or design work, not fixes to broken code**, so they are not part of the core work above. Each waits for the core tasks it names, then is briefed as its own feature, in whatever order the owner picks.

**F1 Many suns: binaries, trinaries and black holes** (after T2.7, T2.9). Was T2.10.
- **Today:** the galaxy knows a star's companions (`Star.Companions` and `Star.Companion`, shared/Galaxy/Core/Galaxy.cs:20; the map shows "double" or "triple"). A system's model has one `Star` (planet-surface/src/Sky/StarSystem.cs:203), so every sky, sun direction, light and shadow comes from one star. There is no light model for a black hole.
- **Design:** DESIGN.md 181 and 196 ("double shadows and sunsets, shifting temperatures"); solar-systems.md 112 and 148 (close pairs circled by planets, wide pairs with planets round one star, combined brightness sets the zones) and 144 and 660 (black holes and neutron stars).
- **Builds:**
  - Every star is a body in the system model, orbiting the pair's centre of mass, drawn from the one source of truth (celestial bodies are real).
  - One light per star you can see, from its direction, with its colour and its brightness by distance. It drops out below the horizon or behind an eclipsing body. A wide companion is a bright point with negligible light.
  - The shadow budget is shared by brightness. The brightest sun gets most of the atlas, a second close sun casts a softer shadow of its own (double shadows), and a third lights without shadow unless the budget allows. Each preset sets how many suns cast shadows. At the lowest preset a second sun casts its shadow only if it fits the frame budget, measured in T0.4 (owner, 25 Nov).
  - **Black holes:** the accretion disc is the light source (a large, soft, warm light from the disc, mostly ambient, weak shadows), plus the lensing look.
  - **Neutron stars and pulsars:** dim point lights. A pulsar's sweep is a radiation event on its beat, not a light.
  - Heat and climate come from the same suns: zones from the combined brightness for a close pair, and temperature shifting as each sun rises, sets or is eclipsed.
- **Proof:** on a circumbinary world, two suns cross the sky on their own paths with two shadows. The same scene has one shadow at the lowest preset if T0.4 says so. A black-hole system is lit by its disc.

**F2 Places that fit their world** (after T2.6, T1.4, T4.4)
- **Towns fit their world's air** (DESIGN.md 362): sealed habitats on vacuum and unbreathable-air worlds, with suits worn outside. On an airless test world only 67 of 150 town spots stood in closed rooms. NPC air comes back as part of this (owner, 24 Nov), not before.
- **Vacuum fauna** (owner, 25 Nov: later): airless caves and undergrounds get their own life, through the roster T2.6 reads.
- **NPC air:** people breathe, suit up and suffocate through the shared breather model (T2.3), with proto/npcair (a0a0da94, shelved) as a starting point, not a merge.
- **Proof:** a quiet town on a fresh vacuum world runs 15 minutes with no air deaths.

**F3 Fire** (planned, not started).
Fire (planned, not started) uses up a volume's oxygen through T4.4's air volumes. It is briefed only once these are in: T1.4 (wind and rain on every world), T2.7 (fire effects in the view only), T4.1 (burn damage through Harm), T4.2 (blocks and plants as burnable material), T4.4 (rain, wet and shelter put fire out), and T4.6 (crackle, smoke, AI hearing). Fire hits are never telegraphed.

**F4 Host and clients: real co-op** (after T1.5 and Tiers 2 and 3). Was T6.
- Confirm ARCHITECTURE.md section 9b against `prototypes/coop`.
- Every networked object carries its `WorldKey`, and is sent only to clients on that world.
- The one-per-game UI statics (`Selection` and `GridView`'s drag and click state, Ui/GridView.cs:20-106) become per client.

This is the start of real co-op. It depends on Tiers 2 and 3.

**F5 Placeholder decisions** (owner; then S each). Was T5.2.
- **Decides:**
  - warship turrets that never fire;
  - raw parts with no use;
  - the placeholder numbers in places.json;
  - orbit fauna and Husks in derelicts, once orbit has a wildlife roster (vacuum fauna, later).
- **Closes:** SA C-H.

**F6 Traffic that fits the world** (after T2.6; with the faction ships in ship-forms.md and NPC pilots)
- How much traffic a world has, and what kind, varies a lot by world (owner, 25 Nov): some worlds are busy, some have none, and some have hostile ships.
- It comes from the world: its place on the journey to the core, the factions present, its towns and starport, and its occupancy.
- It builds on the lone trader's open phases (playtest-list.md 739: crews, reactions, rim pirates and small traders) and each faction's own ships.

**F7 The full breadth of stars and worlds** (after T2.9)
- **Stars:** subclasses (G2, M4), luminosity classes (V dwarf, III giant) and brightness varied about ×0.6 to ×1.6 within a class, on T2.9's star model. The spread of the stellar classification used for variety (owner, 25 Nov: "using the qualities of classifications to increase the breadth of star types"): every subclass within each class, giants and supergiants, white dwarfs, and rarer kinds placed by region or as a system's notable feature (solar-systems.md step 1 and its special stars, such as red giants with thawing former ice worlds).
- **Worlds:** the 40 world types, each read from its zones (solar-systems.md, "Each of the 40 world types by position"). Today `StarSystem.TypeOf` gives six types by zone; the class comment (StarSystem.cs:108-111) lists the 40 types as not yet in.
