# Root-fix task list, in priority order

**25 Nov 2026.** This is the work that makes every system work on any world, at any star, for any of four sleepers. It turns [ARCHITECTURE.md](ARCHITECTURE.md) section 12 into jobs that can be briefed. **Approved 26 Nov 2026**, with ARCHITECTURE.md, after seven independent reviews (the owner left the approval to Claude). Nothing starts until the owner says go, because of usage quota. Each task's agent re-checks its task against the code as it stands when it's briefed.

**Priority (owner, 25 Nov): "FIX CORE ISSUES FIRST".** These root fixes outrank all other work, the playtest list included. Owner bugs that come from these causes (weapons, tool and building off-world, door glitches, the compass) are fixed here, not patched sooner.

**Core first, clean-ups around it (owner, 25 Nov).** Only fixes to broken or structurally wrong code are core tasks (T-numbers). New features found during the reviews are listed apart at the end (F-numbers). The point of this list is fixing the core issues: Tiers 1 to 4. Tier 0 exists only to prove them (T0.1a, T0.2, T0.3, T0.4, T0.5). Clean-ups (T0.1b, T0.6, F5, stale worktrees, the history clean) fit into the gaps around the core work and never hold it up.

**Revised 25 Nov after a second look:**
- the prototype (now T0.4) runs in Tier 0;
- identity (T2.9, T3.1a) starts beside the world model; references across worlds (T3.1b) wait for the world manager;
- worlds only freeze in T2.4 and are freed in T3.4;
- each task writes its own failing checks first.

**Reordered 26 Nov (owner): core before saving.**
- World memory (T3.1b, T3.2, T3.4) and the transit world (T2.8a, T2.8b) are core and stay in place.
- The climb and descent (T2.8c) and the saved game (T3.3b) move after Tier 5, under "After the core", because nothing in the core needs them.
- T3.4 and T4.4 no longer wait on T3.3b. T4.4's save proof moves to T3.3b.
- Tier 4 can now run beside Tier 3, since it needs only Tier 2.

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
  5. Scan the diff for the ARCHITECTURE.md section 10 patterns: a `readonly` world field, `Game.Surface`, `Game.Zone` or `Game.Survivor` in simulation code, a `+=` on a world outside `Attach`, `?.` on a system, a direct `GetWorld3D()` query, string ID compares, new statics with state.
- **Known failures:** built from a full run of test-build when T0.5 merges, so the gate is green on today's code. Every entry gives its cause and its owner: a T task, or "outside the root fixes" with its playtest item. The gate fails only on failures not on the list, and the owner takes its entry off when it fixes it. Known before that run:
  - InsideTest `--unseen` (T2.6), SleeperToolTest "a door on someone else's structure is shot out of it" (T4.2), MoonTest's climb into orbit (T1.6);
  - still to be traced and given an owner by the run: SleeperToolTest's varying fails (the door, 8 core-aim fails, tree felling), WeaponTest "the burnt one is frightened" (1 in 102), OrbitTest's two 0.46° body-direction fails, BestiaryTest 264/268 (playtest-list.md, "BestiaryTest fails on batch2"), SliceTest's vehicle checks (playtest-list.md, "Open test failures (13 Nov)"), LayersTest, SurfaceTest's infrastructure checks and BuildTest's one-base check.
- **Today:** `tools/test-all.ps1` and the queue scripts exist, but they're run by hand. The rebuild and quit-yourself rules live only in briefs and memory.
- **Proof:** a branch with a stale DLL, a hanging test or a captured zone fails the gate on its own.

**T0.1a Test harness and probe** (M, high effort; after T0.2)
- **Builds:**
  - `WorldKey` as a typed value (game seed, an anchor in the system tree, a layer; a cabin's key is its structure's ID, a transit world's its jump's ID; ARCHITECTURE.md 4). Only the type here: the tree and storage come in T3.1a.
  - `WorldChoice` taking a start `WorldKey`.
  - `Harness` beside Driver.cs with `Arrive(body)`, `AddSleeper(world)` and `SaveAndReload()`. The last two are stubs that fail loudly until T2.3 and T3.3b.
  - The world probe (every system present, `Ready`, attached).
- **Closes:** TC harness gaps.
- **Proof:** any existing suite starts on a moon, a vacuum world, orbit and another star, and reports. Those runs fail today (that is the bug), so they stay out of the gate until the task that fixes each makes it pass.

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
  - the cost per cabin space (from a pool), the real structure count per world (orbit fragments and pods, hulks, traffic, built and severed pieces) and the seam mirror's body count three deep, for T2.7's gate and section 2b's cap;
  - waking or parking a zone: 37 to 175 ms.
- **Sets:** the budget split per occupied world (each at the full rate), the lower rate for a whole outermost world nobody occupies or a destination being prepared (a cabin has no rate of its own and steps in lockstep with its outermost world), the thread plan and the shares of the process-wide budgets (`FramePacer`, `UploadBudgetMs`, `GcPacer`).
- **Then:** two things go to the owner: the engine options (physics spaces on threads, a no-render World3D), and the largest orbit zone that still holds precision.
- **This is a decision gate. T2.2 and the world-model tasks after it don't start before it.** T2.9 doesn't depend on it.

**T0.3 Clean up the tests that hide bugs** (S, medium effort; after T0.1a)
- **Not here:** the two tests that assert bugs are rewritten in the tasks that fix those bugs, T4.3 and T4.7. Rewritten now, they would turn the gate red until Tier 4.
- **Removes:** about 30 silent passes. They retry up to N rolls, then fail with the seeds.
- **Stops:** travel tests calming weather and freezing wildlife before they leave.
- **Moves** any new flaky or old failure it finds onto T0.5's known-failure list, with its cause and owning task.
- **Closes:** TC.
- **Rule for every later task:** its first step writes failing checks for the findings it closes, off the starter. It merges when they pass. Checks aren't written for all findings up front, because some need harness features that come later (two sleepers in T2.3, save and reload in T3.3b).

**T0.6 Repo hygiene** (S, low effort; mostly done 25 Nov)
- **Done (owner, 25 Nov: "that should get a clean"):**
  - the 16 zero-byte stray `.cs` files in the main checkout (7 in the root, and 9 empty copies of files that have content on test-build) were removed;
  - `asset-notes/` was committed;
  - `.gitignore` now keeps out new screenshots (`prototypes/*/screenshots/`, `shots/`), `scratchpad/`, and the working folders the owner chose to keep local: `docs/font/`, `docs/mockups/`, `docs/music-samples/` and the research PDFs.
- **Screenshots from now on:** a shot a README or the gallery needs is added on purpose with `git add -f`. The gallery's images live in `docs/gallery/`. Agents' proof shots go to their scratchpad, never into git.
- **Left for later:** `.git` is about 930 MB, and 482 MB of that is 904 PNGs in history. The owner chose to stop the growth now and not rewrite history, since a rewrite means re-cutting every branch and the roughly 250 worktrees. Revisit once fewer branches are live. Pruning stale worktrees goes with it.

---

## Tier 1: small root fixes for live bugs
Once Tier 0 is in, T1.1, T1.2, T1.3 and T1.7 are independent of each other. T1.4 and T1.6 follow T1.3, and T1.5 follows T1.1.

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
- **`Move` happens at once:** detach, attach, and the record marked dirty, in memory, with nothing waiting on a write (ARCHITECTURE.md 4c). T3.3a adds the loss or duplicate audit inside it, in memory, before the holder changes. Saving it is T3.3a's commit queue.
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
  - the spawn distance becomes a host simulation setting in the game settings, and the static `SpawnReach` (SpawnReach.cs:26-27, set from a client's display settings at Game.Settings.cs:120) is deleted. T2.4 and T2.6 only read it;
  - **a core's 1 g is one game constant** (`Units.StandardGravity`), not a planet rule. Today it reads `zone.Data.Rules.Planet.Gravity` (Vessel.cs:322, 360; Vessel.Hulk.cs:86), which this task makes per world, so cores would take a high-g world's pull. A wreck claimed in orbit takes the starter's surface gravity instead (`Claimed(Game.Surface.Physics.Zone.Strength)`, Spaceflight.Hands.cs:239). All four take the constant;
  - **a world's gravity is standard g × its own `GravityScale`** (DESIGN.md 588). Today a body's gravity is the starter's surface gravity scaled by its size against that system's home (`StarSystem.GravityOf`, StarSystem.cs:154, fed `Game.Surface`, Spaceflight.Worlds.cs:119, 129), so another star's worlds are scaled from the starter. `GravityOf` goes.
- **Proof:** switch to Creative while away, arrive at a world made earlier, and its machines charge nothing. A frozen moon's own rules give its temperature range, not the starter's, checked on the world's rules. A world at another star has standard g × its own scale. A core on a high-g world still gives 1 g. (The suit reads them in T2.5, which proves it in play.)

**T1.4 Every world has every system** (L, high effort, 3 slices: every system on every world, with the probe; no swallowed faults and required collaborators; one air figure; faults it uncovers go on T0.5's known-failure list with an owner; depends on T0.1a's probe and T1.3, because air and the other facts come from the world's own spec)
- **Builds:** orbit and every other zone get the same systems as a surface, with empty data and named facts (`HasAir`, `HasLife`, `HasWater`, ground present). This replaces stand-ins and nulls.
- **Deletes:**
  - every `Zone?.Planet == null` early return: ArmsHost.cs:110, BlockDoors:166, ForceFields:62, SkyLight:91, PoiPlaces:127/144, RuinLoot:36, Underworld:57, PlaceFauna:599, Hulks:61, SkyTraffic:108, Game.Scanner:65, BuildTool.Whole:149;
  - `?.` on required systems.
- **Also:**
  - **No fault is swallowed** (SF R4). A generator or meshing fault is logged with its world key and seed and fails a test run, never dropped quietly: `catch (Exception) { continue; }` with no log (Place.Deco.cs:79, HullVessel.Finish.cs:143, Town.cs:513), `catch (KeyNotFoundException)` that skips or returns false with no log (Town.Rooms.cs:77, PlaceParts.cs:86), and places, hulks, meshes and routes dropped after `PushError` (PoiPlaces.cs:312, Hulks.cs:69, Place.cs:419, Town.cs:367, Town.Runs.cs:94).
  - **Dust reads the world's grit through the world,** not `Zone.Planet.Grit` (ArmsHost.cs:1069; SleeperTool.cs:338, 379, 451; SF #5), which throws once orbit has dust.
  - **Required collaborators are required.** SleeperTool's `Places`, `Debris`, `Sfx`, `Fx`, `Arms` and `Builder` are passed in when it's made, not set afterwards and called with `?.` 30 times (SleeperTool.cs:53-60; SF #10). A missing decorations library or physics `Hover` block fails loudly instead of falling back silently (Place.Deco.cs:36-45, HoverBike.cs:440; SF #11, #12). Hooks that default to stand-ins which look like they work go too, such as `Focus = () => Vector3.Zero` (PoiPlaces.cs:32, Hulks.cs:30, RuinLoot.cs:22, SkyTraffic.cs:31; SF #9): they're passed in when made.
  - **Orbit's loot is made like a surface's:** today orbit's `Lootables` is made without `BodyAt`, `Settle` or `DrawnOf` (OrbitZone.cs:180; SA G1).
  - **Wrongness isn't forced to 0 in space** (Game.Wrongness.cs:52; SA C-G1). It comes from the world, which has no Husks in orbit until they're placed there.
  - **Weather comes from each world's facts** (air, star activity, fields), never its layer (ARCHITECTURE.md 8). Orbit's facts give no weather today, and its weather system still gives the `Strike` and `Events` API, with no hits. (`Hazard` and `Hazards` are already gone.)
  - **Air is one fact per world, not a bool.** Today two oxygen sources disagree:
    - Bestiary's `BadAir` (Vacuum or Oxygen < 15, shared/Bestiary/Core/Conditions.cs:72) uses archetype figures;
    - the sky's `OxygenByAir` gives Ash 13% and Sulfurous 6%.

    They become one oxygen figure per world, from its spec. `HasAir` is derived from it. T4.4 adds the query at a point.
- **Closes:**
  - SF (about 105 hiding calls), SF #9, SF R4 apart from the ledger audit and the failed restore (T3.3a);
  - **NO WEAPONS IN SPACE** in part: the guard that stops shots and NPC weapons stepping in orbit goes. The second stop, ArmsHost being a child of the starter zone (Game.cs:320-322), goes in T2.5;
  - SA C-G1 (empty stand-in wildlife and weather, wrongness forced to 0), C-G2 (the scanner's `InSpace` guard, Game.Scanner.cs:65).
- **Proof:** the world probe passes on every body for the zone's systems (T2.6 adds the places rows). A missing system throws in tests. A swallowed generator fault fails a test run.

**T1.5 The same seed builds the same world** (M, high effort; after T0.1a, to build the moon and another star's world; T0.5, so its check runs in the merge gate; and T1.1, because worker threads racing on `StartWorld` and `WorldForce.Current` are one cause)
- **Today:** the world fingerprint test (`SurfaceTest --only=Dump`, 13 Nov) found city planning giving a different city, lots and grounds for the same seed on two runs: 2 of 12 worlds at 16 km and 1 of 4 at 64 km. Storage keeps only the seed and the changes (ARCHITECTURE.md 4b), so a world that rebuilds differently puts its saved changes on the wrong things. Co-op (F4) needs every machine to build the same world too.
- **Finds and fixes the cause:** a step that depends on run order, time or threads (worker completion order, hash or dictionary order, a shared random stream drawn from more than one thread, the clock). Fixed at that step, not by retrying until two runs agree.
- **Not this task:** seeds that change when the generator's order changes (SA 5.2: `SpawnSeed = rng.NextU64()` during placement). That is T3.1a's seed helper. This task is the same code giving two answers.
- **Closes:** playtest "Worlds are not deterministic" (playtest-list.md, "Worlds are not deterministic").
- **Proof:** every fresh seed is built twice and fingerprinted, on the start world, a moon and another star's world, at 16 and 64 km, with random seeds each run. The check joins the merge gate (T0.5), so no later task can bring the drift back.

**T1.6 The Skiff's climb into orbit stalls** (S, medium effort; after T0.5 and T1.3, which changes every world's gravity and could undo the fix)
- **Today:** MoonTest fails "it crossed into orbit" on some worlds, on clean code too: the Skiff climbs at only about 5 m/s (playtest-list.md, "Open test failures (13 Nov)"). The entry's "6.8 km" can't be right with the 1,060 m crossing (orbit.json:59), so the job re-measures first. Before a guard in `Bindings.cs` it also threw at line 107 and never built the Skiff. That points at the vessel's climb (thrust, mass or drag against the world's gravity and air), not the surface-to-orbit seam.
- **Finds and fixes the cause** in the vessel's climb, not by easing the test.
- **Proof:** MoonTest's climb passes on fresh worlds, and the entry comes off T0.5's known-failure list.

**T1.7 Z-fighting: one rule at every builder, one failing check** (M, high effort, 2 slices: the rule in every builder; the scene check as a failing test; after T0.5, whose test run it joins)
- **Today (owner: "it's everywhere"; 9, 17 and 18 Nov: "z-fighting based into decos and ship parts across the entire game", cargo containers, painted floor finishes):** one detector and rule exist, `Coplanar` (shared/Geometry/Coplanar.cs: `Find` returns faces of different looks lying within `Gap` 2 mm and overlapping; `Separate` stands a part `Standoff` 3 mm off the face it lies on). But each builder opts in on its own: the POI generator (`MeshStandoff`), deco (`DecoStandoff`) and the bestiary (`MeshBuilder`) do; the block mesher for ships and bases (building `ChunkMesher`) and every other mesh builder don't. The whole-scene check (`Staging.ZFight`, `--view=zfight`) only prints pairs; nothing fails. The stand-off is a fixed 3 mm, which flickers at range and at grazing angles (the floor finishes). Unfinished work making `Find` fast enough for whole meshes (buckets, threads, block faces known apart) sits on `proto/zfix` (f917d188, saved 26 Nov, not reviewed).
- **Builds:** every mesh the game draws passes one path that keeps the rule by construction: whatever lies on a face stands off it, or is cut into it or merged with it. No builder places a flush face of another look. The stand-off follows the depth buffer's precision at the distance the face is seen from, not one fixed figure.
- **Replaces:** the per-builder stand-offs (`MeshStandoff`, `DecoStandoff`, the bestiary's) with that one path; the deco and bestiary coplanar tests fold into the one check.
- **Proof:** `Staging.ZFight` becomes a check in the test run that fails on any fighting pair, at the start's place, a city lot, a ruin, a capital hulk, a player-built ship and base with every block and device, and the cargo containers, over fresh seeds; and one per generated piece (every block, device, deco, POI part, creature, ship form). Owner's shots re-taken from a distance and at grazing angles: floor finishes, containers, door insides, ship parts.
- **Closes:** the playtest list's z-fighting entries ("Lots of z-fighting on a lot of objects", "Z-fighting still on painted floor finishes", "Z-FIGHTING ON ASSETS", "Z-fighting on the inside faces of doors").
- **Every builder after it** (F10's places, F12's fauna, F13's ship forms from `proto/shipforms`) goes through the same path, and the check covers it.

---

## Tier 2: the world model
In slices, each merged on its own, within the budgets T0.4 set.


**T2.9 The galaxy from the game seed** (M, medium effort; depends on T0.1a, and T1.4 so new air values feed one oxygen figure, not today's two; needed by T2.8a and T3.1a)
- **Builds:** the galaxy made from the game seed, and guarantees as a set of requirements any system can carry into the one generator (ARCHITECTURE.md 2). The start's set: a G or K star, at the rim, the player's chosen world kind (DESIGN.md 1528), a 16 × 8 km world, a vacuum moon and the drive materials. solar-systems.md's start steps (step 3's chain scaling, step 5's first giant, step 8's start system, section 8's moon and drive-material rule) are requirements in that set.
- **Replaces:** the home system built by `SkySystem.For(spec)` beside `StarProfile.Of`; the start is generated like any other, and no generation step asks "is this the start".
- **One star model.** Today there are two: the galaxy's `StarKind` (12 kinds, shared/Galaxy/Core/Galaxy.cs:8, with only a brightness) and the system's `StarClass` (5 values, planet-surface/src/Sky/SkySystem.cs:8), which is what lights a world. `SkyRig.ClassOf` (SkyRig.cs:826) folds 12 into 5, so blue giants, pulsars, magnetars and black holes all become blue-white, and red giants become orange. `StarClass` and both mappings are deleted. Each star carries the physical numbers its class gives: temperature, radius, mass and brightness, from solar-systems.md step 1's star table. Subclasses and the wider spread are F7. Colour, light, its size in the sky, the zones and the worlds' temperatures all come from those numbers.
- **The start is found, not flagged.** Today the planet is chosen first and the star rolled to suit it (`SkySystem.PickStar`, SkySystem.cs:167-176), which can give a start a red dwarf, white or blue-white star. The start star is found by searching the galaxy's rim stars for one that can meet the start's set, then its system is generated carrying that set.
- **Closes:** SA A-6.1, A-6.2, A-5.3 (two wreck-seed schemes, dead `SystemSeed`); **MOONS: NO RINGS, SMALL ONES AIRLESS**: rings and air are rolled in the system model from the body's kind and size. This changes only how the planet's spec gets its values; T1.4's one oxygen figure already reads the spec.
- **Proof:** over fresh seeds and every world kind a player can choose, the start system meets every requirement in its set, on a rim G or K star found by the search, and its system matches what the galaxy gives that star. Another system made to carry the same set meets it too. A red giant, a white dwarf and a black hole each keep their own kind through to the sky (F1 then draws how a black hole lights). Small moons are airless and no moon has rings.

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
- **`Game.Zone` becomes the local sleeper's view world only.** Today it is the one "current world" (48 uses), and simulation reads it: creature contact bodies exist only on it (CreatureBodies.cs:80, 97, 255-271), and creatures' weapons fire into it (`SimArms.Zone => _host.Game.Zone`, SimArms.cs:67). T2.4 and T2.5 move those readers onto their own world.
- **Replaces:** `Game.Survivor` in simulation code: wildlife's one `Player`, towns' one "you", shots' `owner == 0` and static `You`, the crossing's `Carrier()`, death and aftermath.
- **Death makes a corpse** (ARCHITECTURE.md 3): the body becomes an ordinary world thing, a corpse held by its world with no claim, labelled with the sleeper's ID like a bag (it can rise as a Husk, DESIGN.md 682), and the sleeper gets a new body at respawn. This replaces today's single `Game.Body`, "the sleeper's body from the last death" (Game.Aftermath.cs:26-28), so each sleeper's every corpse is its own thing.
- **Offline sleepers are held records** (ARCHITECTURE.md 2): a sleeper who logs off leaves no body in a world, only a held record inside its holder's record (the structure they were in, or the world they left), like any thing in `things/`, at its local position. Splits, destruction, freeing and jumps carry it by the same child test, applied to its stored position. The sleeper's own record keeps only non-physical state (vitals, pack, progress, tech, respawn pod IDs, the home flag).
- **Builds the claims** (ARCHITECTURE.md section 3): every lasting thing carries its claim (a sleeper, the Sleepers faction, an NPC faction or none; a structure's is its core's, below), kept apart from where it is held. The Sleepers faction record holds only what isn't physical: charts and standing. Its index of claimed things by ID comes in T3.1b, once IDs exist; it never holds the objects. Today ownership is an owner string compared by name: `Structure.Owner = Building.DefaultOwner` (Vessel.Hulk.cs:84, BuildTool.Whole.cs:320, BlueprintLibrary.cs:157, Spaceflight.Hands.cs:230), read by `Vessel.IsPlayers` (Vessel.cs:201), `SleeperTool.Ours` (SleeperTool.cs:683), `StructureArea.Linkable` (StructureArea.cs:52) and Visor.cs:417; the string is also set in the shared building code: `Building.DefaultOwner = "player"` (prototypes/building/src/Structures/Building.cs:90), `Place(..., owner = DefaultOwner)` on every core placed (:145, :152), `Blueprint.Load` and `Build` defaulting it (Blueprint.cs:70, 318, 326, 346) and Designer.cs:24. The fold restore calls `Blueprint.Load` with no owner (Spaceflight.Fold.cs:470), so any restored structure with a core is stamped as the player's. A bag's "ours" is `Lootable.Yours` (Lootables.cs:54, 146, 156; InfoSections.cs:181). The claim field replaces the owner string, its setters and compares, and `Yours`, all deleted in the same merge, so there is never a second way to check ownership. **A structure's claim is its core's claim** (ARCHITECTURE.md 6, Wreck): with no core there is no claim, and "wreck" is just "no core" (DESIGN.md 964, 911, 1321). Placing a core claims; destroying or removing it unclaims; severed pieces have no core, so no claim. No separate field is set by hand. The core is carried in the blueprint and structure record, so the claim survives a crossing. Today shooting out a faction core changes nothing, because `CoreDestroyed` (PoiPlaces.Interact.cs:327-335) has no subscriber (SF #19); it goes, since the claim follows the core. `bag.Owner` (Lootables.cs:56, 157) stays as the bag's label (ARCHITECTURE.md 3), typed as the ID of whoever's things they were, of any kind (sleeper, person or creature), so the one generic bag (DESIGN.md 683) and the corpse check (SleeperTool.cs:644) match people and animals alike. "Ours" reads only the claim, never the label. T2.5's command-chair check, T3.3b's record fallback, T4.3's yield and T4.7's `Spill` all read the claim.
- **XP on the visor reads the local sleeper's own progress** (today `Progress.Earned` isn't joined to the visor's XP line).
- **Also:** all four cryopods are live, each the wake point of its own sleeper, with its own locker (owner decision 4). Each sleeper's record holds their respawn choices as pod IDs (start pod, home, others), with the home flag in the record, not on the shared block, recording when it was set (ARCHITECTURE.md 3). T3.3b's record fallback reads it: a claimed record whose world won't load goes to the structure holding the home pod, on a world that loads, most recently flagged by any sleeper of its claiming faction, else to the structure holding that faction's newest start pod on a world that loads (ARCHITECTURE.md 4c). Respawn is per sleeper, not hard-wired to the bunker (decided-vs-built.md 2.2), and a pod takes one waking sleeper at a time. The other respawn choices there (home base, a cryopod built on a structure, tent) are F8, built on this.
- **World things come out of the sleeper's models** (SA A-R3, S7, A-3.1 to 3.4). Today the suit's survival sim (`SurvivalSim`) holds the world's replicators, pods, bushes and food caches as bare positions and counters, `Death` keeps backpacks on one suit's clock, and charts and travel state sit in `Spaceflight` (`Charts.Mark` has no world). The survival sim keeps only vitals. Replicators, pods, caches and bags become world things, held by the world they're in, so they take frame shifts like anything else (A-3.3). Charts and standing go to the Sleepers faction record. There are no respawn devices yet: no respawn or healing block was ever built in the slice (the survival-vitals prototype has them only as fixed points, planet.json:62-63). Built cryopods come in F8, as world things the same way.
- **Room air comes off the suit too.** Today every room's air lives in the one sleeper's suit, with its own air clock (`_airOf`, `_roomsAir` and `_airClock`, Suit.cs:69-71, advanced at :255): two sleepers in one cabin each keep their own copy, people and creatures can't read it, it's never saved, and a room's air only moves while that sleeper stands in it. Each room's `RoomAir` is held by its structure or place, keyed by the room, and advanced on the galaxy clock together with the structure's power and fuel timers, in event-ordered segments (ARCHITECTURE.md 4's one catch-up path, T3.2). The suit's `Breathe` (Suit.cs:253-300) moves out with it. T4.4 then makes these nested air volumes.
- **One breather model**, not a rule owned by the sleeper's suit (hold, tank, suffocation, from survival-vitals/data/vitals.json). Built for sleepers now, in a shape people can use later. It breathes the air of the room it's in, held by that room's structure or place; T4.4 then answers which volume a point is in. Today a closed town room gives people air but not the sleeper: a closed place sets the suit's room to `Closed` with its RoomAir (Suit.cs:290-297), but `InSealedRoom` is set in `Suit.Tick` (Suit.cs:314) only from structure rooms or the bunker.
- **Closes:** OW#1 (respawn in the wrong world); SA A-R1 (A-1.1 to 1.9), A-R3 (A-3.1 to 3.4), A-6.3; `Harness.AddSleeper` goes live.
- **Proof:** two sleepers on the start world are both targeted, hurt and credited (on other worlds once T2.4 hooks their wildlife to every sleeper). Each wakes in their own pod. A sleeper who dies on a moon wakes in their own pod on the start world. Two sleepers in one cabin share one room air, and a person in that room reads it too. (Saving both is proven in T3.3b.)

**T2.4 The world manager, interest points and freezing** (L, high effort, 3 slices: the world manager split out of `Spaceflight` (Spaceflight.cs 775 lines, Worlds 297, plus parking); streaming and collision per point; wildlife, weather and places per point; depends on T2.2, T2.3, and T1.3 for simulation size from the game settings)
- **Builds:**
  - Each world keeps its interest points (ARCHITECTURE.md 2): a connected sleeper, a connected sleeper in a cabin on it, a destination from "transition likely" (nearing the atmosphere edge, or a warp spooling, DESIGN.md 169) until arrival or a stated cancel (the spool called off, or the ship leaving the approach band), and a committed jump for its transit world and destination until arrival commits (T2.8a).
  - **Offline sleepers hold no world.** Only connected sleepers are interest points; an offline sleeper is a held record inside its holder's record (T2.3) and holds none of the 4 world slots. Logging in spawns the body by `Move` from wherever that held record now is.
  - **NPC vessels never create interest points,** and cross only between two worlds that are both in play, such as pirates following you up (DESIGN.md 1456: traffic is placed by rule when a world is made, never tracked).
  - **A connected sleeper in a cabin keeps every world up its chain in play.** Cabins nest to any depth (ARCHITECTURE.md section 2, owner 26 Nov), and a nested ship is a body in the cabin around it.
  - Streaming, collision, wildlife, weather hazards and places run per point. Collision is streamed apart from drawing. Large-scale weather (fronts, storm cells) is a pure function of seed and galaxy clock (ARCHITECTURE.md 8); only hazards near a point are simulated.
  - A world with no points freezes at once (owner). Until T3.4, other stars are still freed as they are today and the start system stays parked, so memory doesn't grow with every star visited. Freeing every world the same way comes in T3.4, once the change record (T3.2) and the safe ledger (T3.3b) can bring it back as it was.
  - **A world with an interest point is never freed.** Today `FreeAbroad` (Fold.cs:288, :303) frees every other star's system on every fold, so a second sleeper's fold would free the first sleeper's star.
  - Simulation size comes from game settings, not a client's display sliders.
  - **Wildlife population doesn't depend on the path walked** (A-2.8). The roll is keyed by the world and the patch's cell, not the patch origin the player happened to arrive at (`Rng.For(Fauna.Seed ^ (Origin.X*31 + Origin.Z), "population")`, WildlifePatch.cs:366). The refills (:453) are keyed by a running count, so they depend on the path too.
  - **One galaxy clock** that every world in play reads, so two sleepers on two worlds see the same sky (structure-audit 6.4: each world advances its own `SkyRig.Days` today). T3.2 adds the catch-up on it.
  - **The multi-world budget T0.4 measured:** every occupied world at the full rate, the budget split by the number of occupied worlds, a lower rate only for a whole outermost world that nobody occupies or a destination being prepared (a cabin has no rate of its own: it steps in lockstep with its outermost world, running or paused), the thread plan, and each world's share of `FramePacer`, `UploadBudgetMs` and `GcPacer`.
  - **Making a world ready never costs one long frame.** Approaching the moon today gives one frame of 18 to 24 ms when its terrain streamer is set up (`TerrainStreamer.SetWorld`, playtest-list.md, "Approaching the moon: one frame of 18-24 ms"). Setting up a world's streaming is split across frames, within the budget T0.4 sets.
- **Replaces:**
  - the one `Focus` and one weather `Observer`;
  - creature contact bodies made only on `Game.Zone` (CreatureBodies.cs:80, 97, 255-271): each world's wildlife has its own;
  - `Streamer.CollisionRadius` set from one sleeper (Game.cs:925);
  - what triggers parking and freeing: one player's place today, interest points after (the pause itself is T2.2's);
  - the one `Away` and one `Orbit`.
- **Closes:** SA A-R2 (A-2.1, A-2.3 to 2.9; A-2.2, the frame shift, is T2.2), A-3.5 (world manager split out of `Spaceflight`), OW#6, OW#9's weather observer, OW#10's streaming and streamer settings (its shadow settings are T2.7).
- **A frozen or freed world's view nodes leave the drawn scene,** its lights included. Which lights cast shadows is T2.7's (engine-wide view state).
- **Proof:** two sleepers far apart on one world both have ground and life. A ship flown to a stormy moon is struck there, with the moon's hazards simulated round its sleeper, and its storms where seed and clock put them. Two sleepers on a moon are both targeted by its wildlife. A world nobody is on stops costing frame time. A sleeper who logs off aboard a ship holds no world, and their held record follows the ship. (Two sleepers at two stars is proven in T2.8a, which builds jumps per vessel.)
- **Note:** `ParkHome` and today's freeing of other stars stay until T3.4.

**T2.6 The places stack per world** (L, high effort, 3 slices: places, ruins and hulks per world; caves and the underworld; sky traffic; depends on T1.1, T2.4)
- **Builds:** `PoiPlaces`, `RuinLoot`, `Hulks`, `SkyTraffic`, `Caves` and `Underworld` made with each world, not once on the starter. The bunker gets no special rules.
- **Closes:**
  - OW#3, #18, #20; SA C-E2, C-E3 (C-E1, the bunker as the suit's only shelter, is T4.4);
  - InsideTest `--unseen` "nothing of a cave's sim in a place's dug rooms", which fails on plain batch2 (18 Nov): caves and the underworld bleed into places;
  - bases already keep creatures clear (`WildlifePatch.SyncBases`, WildlifePatch.cs:524, read by `WildSim.InBaseZone`); that comes with each world's wildlife. decided-vs-built.md 1.7 was stale. If creatures still spawn beside a base in play, it is traced as its own bug;
  - `SkyTraffic` (the lone trader or scout, playtest-list.md, "A LONE TRADER OR SCOUT IN THE START WORLD'S SKY") is made once, on the starter, today. It is made with each world instead, and reads how much traffic that world has from the world itself; none is a valid answer. What the traffic is (busy, empty, hostile) is feature F6, not this task.
- **Caves and undergrounds per world** (line numbers from test-build):
  - **The bunker is generated claimed by the Sleepers faction** (SA Root E), with a working core and a life-support pod on power, as the sleepers' first base (DESIGN.md 1529). "Our structures" then covers its marker, salvage, building, pods and air with no clause of its own. Its dug-out ground is a terrain stamp like any place's. Delete `Keep` and the bunker rules (`Game.Places.cs:66`; consumed at `PlaceFauna.cs:571, 639, 704`). Part of the cave bleed is already fixed: 4acacb45 makes `LoadedVoxels` read `PlanetTerrainSource.Dug` as rock. Rerun InsideTest `--unseen` first to see what still fails.
  - **`Underworld` serves every underground on a world,** not only the start town's (`Underworld.cs:68`, :102). `IncidentalTest.cs:206-211`, which counts only the start town's underworld, also counts a moon's.
  - **Life underground comes from the world's roster, not its air.** `Fauna = Zone.Wildlife.Airless || … ? null` (Game.Places.cs:50) assumes an airless cave is empty. It reads the roster instead; on airless worlds the roster is empty until vacuum fauna (owner: later).
  - **The underworld and ruin caches use the spawn distance** from the game settings (T1.3), not a fixed 90/150 m (Underworld.cs:44) and 70 m (RuinLoot.cs:25).
  - **Stamps are planned before a world's first chunk column is read.** Otherwise the `_columns` cache (`PlanetTerrainSource.cs:61`) bakes in no cave mouths, because `CaveMouths` returns null while `Stamps.Count == 0` (`CaveMouths.cs:45`, :54).
- **Proof:**
  - a moon and another star's world have their own towns, ruins, people and caves, rolled with their own profile;
  - the world probe's places rows pass on every body;
  - InsideTest `--unseen` passes;
  - a moon's undergrounds are served by its own `Underworld`, and its cave mouths are in the first chunk columns read;
  - an airless world's caves take their life from its roster.

**T2.5 Player systems read `thing.World`** (L, high effort, 3 slices: arms and the tool; building and doors; the view bits (compass, map, music, markers, effects); depends on T2.3, T2.4, and T2.6, because weapons' fire lines and music read the world's places (SimArms.cs:111, 186; OW#3, #17))
- **Creatures' weapons fire into their own world,** not `Game.Zone` (SimArms.cs:67).
- **`Arming`'s statics become per world** (shared/Bestiary/Sim/Arming.cs:13-22: `Reach`, `Fire`, `BlowDamage`, `Masked`), set by each world's arms, not by closures on one ArmsHost. Unset today, armed creatures quietly fall back to melee (SF #21).
- **Moves onto the drawn world's view (rule 12):** `WindHowl` and `AmbientExposure`, made once as children of the starter with a fixed `Zone` today (Game.cs:384-387; WindHowl.cs:20, 65; OW#9).
- **Moves onto the sleeper's world:** suit, SleeperTool, BuildTool, ArmsHost, shots and effects (debris, gibs, blood), the doors' animator (`BlockDoors`), force fields, hover bikes, map, music and markers.
- **Deletes:**
  - `Spaceflight.TakeHands`, its "NO BUILDING IN ORBIT", its stand-in shot and its hand salvage (Spaceflight.Hands.cs:97). Orbit salvage runs on the ground path until T4.3 makes one yield formula;
  - `readonly SurfaceZone` fields everywhere;
  - `Game.Surface` uses outside world management.
- **Also:** the compass shows only on a world's surface, tuned to that world's poles.
- **Closes:**
  - OW#2, #4, #5, #11, #16's "home" checks against `Game.Surface` (`OnOtherBody`, Spaceflight.Moon.cs:37; its bunker marker, Visor.cs:415, is T4.6's), #12, #13, #14, #17, #19, #25, and OW#9's hits reaching you and its wind and exposure sound (its observer is T2.4, its shelter T4.4);
  - **NO WEAPONS IN SPACE** (the rest), **BUILDING IN ORBIT**, **DOORS GLITCH OFF-WORLD**, **COMPASS ONLY ON A WORLD**;
  - the owner's to-do to block getting into command chairs on cores we don't own (a claim check).
- **Force fields stay per vessel:** power draw (`v.Power.AddLoad(Draw(v))`) and `PoweredFor` read the vessel's own power and wreck state, never the world's. The door hook that feeds fields across open doors (`BlockDoors.OpenDoors`) keeps working.
- **E always reaches doors and containers.** A held item never takes E from a door or a container. On vacarms, holding a blueprint off-world let the builder take the aim, and a wreck door's E prompt went blank at 3.0 m. The hands chain is Game/Game.cs:729-750. One E order comes from `WorldQuery`'s Use mask (T2.2).
- **Claiming a wreck** (a core in hand) moves into the building slice as ordinary core placement through the build tool, at the same reach as any E action. Today `Spaceflight.Claim` works at any range; vacarms made it UseReach+1, derelicts only.
- **Proof:**
  - weapons, the tool and building work in orbit, on a vacuum moon and at another star, and shots step in orbit;
  - doors on any ship open and animate cleanly off-world;
  - the suit reads the world you're on, including a frozen moon's temperature range;
  - a vacuum moon is silent, and a moon with air has its own wind;
  - OrbitTest's door and "box: take-all" step passes with a blueprint in hand.
- **Test lessons from vacarms (6aca3cad), reused here:**
  - in orbit the test's aim hit its own Skiff cabin at 2.1 m, so "0 hits" proved nothing: the sleeper floats clear before aiming;
  - TestRows needs a row that can be placed in space.
- **Note:** vacarms' MoonTest.Hands checks are reused here.

**T3.1a `WorldKey` everywhere, IDs and the seed helper** (M, high effort; depends on T0.1a, T2.9)
- **Replaces:**
  - ledger zone names (`surface`, `moon<index>`, `star<n>/…`) and the `StartsWith` checks;
  - star and body list indices;
  - counter IDs;
  - reference-keyed persistent maps: `PoiSite`, `PlaceMemory`, `FlavourRuin`, creatures.
- **Builds:**
  - **the system tree of anchors** (ARCHITECTURE.md 4): system → barycentre → star or body → … → site, with a star, a barycentre (close binaries, circumbinary planets), a gap site between orbits, a rogue body and a black hole's inner systems each an anchor with its own orbital path. `WorldKey` is an anchor plus a layer, the layer a fact of its anchor. Which asteroid field or ring is part of an orbit world, and which is its own world, is settled in the tree. A fold arrives in orbit of a star, which is an anchor. A cabin's key is its structure's ID, a transit world's its jump's ID;
  - **opaque IDs** for everything that lasts, carrying no location: nothing reads where a thing is out of its ID, only from the record of what holds it;
  - `Seed(worldKey, siteId, purpose)` for every persistent roll.
- **Closes:**
  - OW#15, #21 to #24 (deposit, milestone, chart and species IDs);
  - SA A-R4 and A-R5, including A-5.1 (the same loot in every copy of a layout) and A-4.4 (creature drops rolled again).
- **Proof:** a reordered generator list doesn't remap anything, and nor does adding a companion star (F1) or a gap site. Two copies of one layout roll different loot. A structure that moves keeps its ID and its cabin's key.
- **Note:** the ledger's zone names live in Spaceflight.Worlds.cs, which T2.4's first slice moves into the world manager. That part of this task merges after that slice, so the two don't collide.

**T3.3a One ledger for many crossings** (M, high effort; listed before T2.7, whose cabin freeing commits through it; depends on T1.2 (`Move` changes the holder at once), T2.3 (every sleeper is a holder), T2.4 (the world manager) and T3.1a (IDs); needed before T2.8a and T2.8c, whose proofs run two crossings at once)
- **Today:** the ledger has one commit, one plan and one "before" record (`_commit`, `_plan`, `_before`, ZoneLedger.cs:167, 194-196), so a second `Begin` while a commit is in flight overwrites the first. `Holders()` yields one sleeper record, the constant `ZoneLedger.Sleeper` (Spaceflight.cs:684-687). A second sleeper's crossing would throw or corrupt the first's, and items could be lost.
- **Builds:**
  - one commit queue on a worker, for durability only: dirty records flushed as shadow-paged snapshots, one at a time, each holding whole moves, so "one atomic write per crossing" (DESIGN.md 1726) applies to the snapshot, not to the timing of each handover (ARCHITECTURE.md 4c);
  - every sleeper as a holder, by ID;
  - the `Move` order: audit, detach, attach and mark dirty, in memory at once, with the thing put back if the attach fails; a sleeper stepping through a hatch never waits behind another world's record write;
  - a record is gone only when its holder reports it destroyed (death bags included);
  - the conservation audit (`Ledger.Audit`) runs inside `Move`, in memory, before the holder changes, instead of only printing (Spaceflight.cs:677, Jump.cs:251). A move that would lose or duplicate items is refused, so it never happens; nothing is rolled back later. A structure whose restore fails (Spaceflight.Fold.cs:473) stays as its own unchanged record inside the snapshot and never blocks the commit queue.
- **Proof (at the ledger, since the climb is still one crossing at a time until T2.8c: `Pending _pending`, Spaceflight.cs:82-83):** two `Begin`s queued through the harness both commit, with every count matching; a crossing commits while another world parks and frees (ARCHITECTURE.md 9); a sleeper steps through a hatch with no wait while another world's snapshot is being written; a forced loss is refused inside `Move` and the holder never changes; a forced failed restore stays its own unchanged record in the snapshot while the queue keeps committing.

**T2.7 Simulation and view apart, and cabins** (M, high effort; depends on T2.2; T2.3 and T2.4, because a cabin is made, frozen and freed with its world; T2.5 and T2.6, so its budget is measured with places and per-world arms live; and T3.1a and T3.3a, because freeing a cabin commits its things through the ledger: today `Refresh` deletes any record whose holder isn't seen, unless it sits in a star zone (ZoneLedger.cs:127-137, `KeptInLedger` at Spaceflight.Fold.cs:446), and one commit slot would clash with a crossing)
- **Builds:**
  - Undrawn worlds run simulation only.
  - Engine-wide state (shader globals, environment, `AudioServer` buses) is written only by the drawn world's view.
  - **One cabin model for every structure** (ARCHITECTURE.md 2, owner 26 Nov):
    - a cabin is a physics space whose systems are its world's, made with its structure (world make, spawn, building, severing) from a pool, with no rate of its own: it steps in lockstep with its outermost world, running where the per-point sims run, paused elsewhere, freed only with its world or structure;
    - one inside-set per structure (a flood fill of its built shape, capped at its top, unchanged by damage) for membership and gravity;
    - a child: support decides first (the child of whatever holds its dominant support, through stacks; the world's ground makes it the world's), and enclosure (centre of mass in the inside-set) only for unsupported bodies; innermost wins; tested every tick with hysteresis past the set's edge and a hold on losing support (a hop or a stumble keeps its parent); `Move` only on a change;
    - splits, merges and destruction re-run the test on every child affected and commit the moves as one batch;
    - the one-sided seam: the cabin mirrors bodies up its chain within extent + hysteresis + v·dt as Jolt dynamic bodies with real mass, re-posed each tick, their velocity change returned to the real body as one impulse, `Ram` for damage only; a kinematic mover's move sweeps its world and each cabin it overlaps; mirrors collide only with cabin children and carry no gravity (contacts among outer bodies, the ground and the structure stay the outer world's); the outer world sees cabin children only as ray and hit targets, and area queries count each body once; ground is the streamer's shared shapes while the structure is fixed or landed; a cabin wakes when anything moving enters its extent, and moves its budget with its structure;
    - impacts (closing speed only) on the structure's copy act on the structure; its mass includes its cabin's children, summed up the chain;
    - gravity: the innermost structure that holds the body as a child or holds its centre of mass in its inside-set, while its core works, else outward (a bare point uses the inside-set alone).
  - Things aboard get `Outside = ship.World`, which T4.4's lookup uses as a cabin's parent.
  - **Cabins nest to any depth** (owner, 26 Nov: "A cabin can be inside a cabin can be inside a cabin"). `Outside` may itself be a cabin, and lookups walk the chain out to the first world that isn't one. Inner cabins are freed before outer ones, and a freed cabin commits the ships in it, each with its own record, into its ship's record.
  - **Bases have cabins like ships** (owner, 26 Nov). Today a base is walked on in its world (Vessel.cs:339-341); it joins the vessels' path. The core's mode decides nothing about worlds.
  - **Replaces:** `ParkedOn` and the `BodyKind.Base` checks (PhysicsWorld.cs:275, 285-363): a thing's parent is its world's structure; `CarryParked`'s carried mass becomes the chain sum.
  - **Things join and leave one at a time,** by the test, so nothing ever moves a crowd between worlds: not boarding, not a mode switch, not liftoff.
  - **The cross-chain rule** (ARCHITECTURE.md 6), in `WorldQuery`, the event spread and AI targeting: wherever a ray's segment overlaps a structure's extent the span is also tested in its cabin, in the structure's frame, both ways and to any depth; nearest hit wins. AI holds a target anywhere under the same outermost world.
  - The three `LightningFx` copies (weather, planet-surface, slice) become one view.
  - **Engine-wide render state has one owner, the drawn world's view.** Today it's written by many places:
    - `SunShadows.Apply` sets the directional shadow atlas, the soft shadow filter qualities and the viewport's positional atlas (SunShadows.cs:63-66). It runs only from `ApplyScene` on a settings or zone change (Game.Settings.cs:112-139).
    - `SkyRig` sets the sun's shadow opacity and enable, the night light's enable, and the shadow skew every frame (SkyRig.cs:94, 608-617), although SunShadows' doc says nothing else touches a light's shadow.
    - Global shader values: `shade_fill` (SkyRig.cs:687), `sky_ground` and `sky_ground_rect` (SkyLight.cs:337-392), `sky_field`, `planet_size`, and `weather_*` from three places (SurfaceWeather.cs:960-963, WeatherRig.cs:239-242, Spaceflight.Fold.cs:353). SurfaceZone.cs:339-343 zeroes them on entering a zone with no weather sim, so today they belong to whichever zone entered last, not the drawn one.
  - **Only the drawn world's lights cast shadows:** one per star you can see (binary and trinary systems, owner design; see F1), sharing the directional atlas by brightness; a stale light never takes part of it. The stale space sun once took half of it: SpaceSky.cs:208-209 creates it with shadows on, and :426 hides it only while share is 0. PopRoute.Edges' shadow-casting sun count after a landing becomes a check.
  - **Every drawn world's sky gets the shadow settings.** `Suns()` (Game.Settings.cs:125-133) walks only the starter, the current orbit and the space sun.
  - **Shaders:** nothing moves vertices from `CAMERA_POSITION_WORLD` in a shadow-casting shader, because in the shadow pass that is the light's eye. terrain.gdshader:946 and flora.gdshader:51-54 already follow the rule. water.gdshader:72 (`round_sink`) is brought under it too, and the merge gate's diff scan (T0.5) flags the pattern in any shader.
- **Closes:**
  - architecture review findings 5 and 7; OW#10's shadow settings (`Suns()`);
  - **DARK DISC AFTER RETURNING FROM ANOTHER PLANET**: engine-wide shadow and environment state left by a world that isn't drawn;
  - **PLANET TO SPACE**: the planet left behind showing a low-detail skin (with T3.4).

  Far trees and horizon voxels follow this rule when built.
- **Proof:** T0.4's budget holds at the full stated peak (ARCHITECTURE.md 2b's gate): 4 occupied worlds, each at the full rate with all its per-world sims, each sleeper in a three-deep cabin chain, plus every structure's cabin within the per-point sims' range, with their seam mirrors, plus four destinations being prepared at once. A sleeper on the dirt floor of an enclosed base stands at its 1 g and digs the floor; walking from deck to dirt and back along the seam changes world at most once each way, and hopping on the dirt inside the base changes nothing; a creature at the door pushes a crate on the deck and neither passes through the other. Liftoff: whoever stood on the deck goes, whoever stood on the dirt stays. A rat at the door can't shove a 2 t crate on the deck, and a cross-line push is applied once. A frigate cut in half sends each half's crew to its own cabin in one commit; a carrier with three frigates aboard accelerates as that heavy; a jetpacker high above a base deck reads the world's gravity, and a sleeper walking a powered ship's top in orbit stays at its 1 g. A barrel rolled into an open bay stops against the cargo. A raider chases a sleeper through the base door without letting go; a shot through a blown-out panel or across a roofless deck hits the crew. A pirate frigate's crew fight aboard it in flight with no sleeper aboard. Stepping out of a base and back in makes and frees nothing. After a return from another world, only the drawn world's suns cast shadows (PopRoute.Edges' shadow-casting sun count). The weather shader globals are the drawn world's, whichever zone was entered last. Drop a bag in a cabin, everyone leaves, come back: the bag is there and the counts match. **Mode switches** with sleepers and loose cargo aboard, each landed and in mid-air: base to flight and back, base to hover and back. Nothing aboard moves or changes world, and every count matches. A base switched to flight lifts off with everyone in it; a ship switched to base mode in mid-air falls with its crew still standing at 1 g. A creature walks in through a base door and out again, and a bag is carried out of a base and dropped outside: each changes world once.

**T2.8a Jumps per vessel** (M, high effort; depends on T3.3a, T2.3, T2.4, T2.9 (fold targets are star indices today, `FoldTo(int)`), T3.1a (targets are anchors), and T2.5, T2.6 and T2.7, so both sleepers can play at their stars)
- **Targets are anchors** (T3.1a's tree, from T2.9's galaxy), not list indices. A fold arrives in orbit of a star, which is an anchor.
- **Moves:** each vessel's jump, target and fold destination onto the vessel, replacing `Spaceflight.Jumping`, `Target` and `FoldTo` (Jump 282, Warp 308 and Fold 506 lines; WarpTest has about 260 checks).
- **Rules:** only the command-chair pilot cancels, with J, during the spool only. Everyone aboard gets the spool warning (owner, 25 Nov).
- **The destination's lifetime** (ARCHITECTURE.md 2c rule 1): it is an interest point from the warp spooling (DESIGN.md 169) until arrival or a stated cancel (the spool called off, or the ship leaving the approach band). Once the jump commits, it is an interest point for its transit world and destination until arrival commits, whoever is aboard.
- **NPC vessels never create interest points,** and cross only between two worlds that are both in play, such as pirates following you up (DESIGN.md 1456: traffic is placed by rule when a world is made, never tracked).
- **Arrival waits** on every connected sleeper whose outermost world is the transit world, anyone who stepped out mid-tunnel included, up to a stated limit. After it, arrival commits, and a lagging client loads the destination as an ordinary join.
- **Proof:** two sleepers jump their own ships to different stars at once, and both play there.
- **Lines up with INTERSTELLAR TRAVEL** (playtest-list.md, "INTERSTELLAR TRAVEL"; rules in solar-systems.md section 8). Before T2.8a to c and T2.9 are briefed, that entry's open jobs are checked against them, so jumps are built once, on this design.

**T2.8b The transit world** (M, high effort; depends on T2.8a, T2.7 (passengers read `Outside`, which becomes the transit world))
- **Builds:**
  - **a jump as two ordinary `Move`s** (ARCHITECTURE.md 2c): source to transit at commit, transit to destination at arrival. The transit world's key is the jump's ID and it keeps records like any world, so nothing exists twice. Its record carries its jump (target anchor, time left);
  - commit takes the ship and its cabin, which holds everything that is its child at commit (ARCHITECTURE.md 2): sleepers inside or on the hull, cargo, and ships clamped on or parked inside;
  - **at arrival, everything the transit world holds moves to the destination** in the ship's frame, as one ledger batch, decided by "held in the transit world", never by being a sleeper: the ship, a sleeper who stepped out (so they arrive beside the ship), their death bag, a crate thrown out, a shuttle that undocked mid-tunnel;
  - **arrival waits** until the host's world is ready and every connected sleeper whose outermost world is the transit world (anyone who stepped out mid-tunnel included) has reported their client ready, up to a stated limit; after it, arrival commits and a lagging client loads the destination as an ordinary join;
  - the transit world has its own named facts (T1.4): no air and 0 g, so anyone outside the cabin reads them.
- **Closes:** ARCHITECTURE.md section 2c.
- **Proof:**
  - a passenger walks out mid-tunnel and arrives beside the ship; a crate thrown out, a bag dropped by a sleeper who died out there and a shuttle undocked mid-tunnel all arrive too, in one batch, with every count matching;
  - clamped ships arrive clamped;
  - a jump whose pilot steps out after commit, leaving the cabin empty, still arrives, with the pilot beside the ship;
  - **a nest arrives whole:** a carrier jumps with a frigate in its cabin and a shuttle in the frigate's, with a sleeper in the shuttle. All three arrive nested, and the sleeper's air and gravity are read up the chain throughout;
  - **nothing arrives Frozen.** The tunnel's `Hold` (Jump.cs:169) once left ships arriving "landed". ae0d775b's wake-on-turn covered it, but the root (`CheckLanding` settling after the hold) was never traced.
- **Note:** after a crash, the transit world loads like any world: rebuilt from its record, its tunnel resumed as an interest point, and arrival by the normal arrival `Move`. It is proven in T3.3b.


---

## Tier 3: identity, records and the safe ledger
World memory: worlds freed to their records, the start system included (owner, 26 Nov: core). T3.1a and T3.3a are listed in Tier 2, before the jumps: T3.1a can start as soon as T2.9 is in, and T3.3a needs it, because the jumps' proofs cross two sleepers at once. T3.1b follows T2.4. T3.2 onward follow T2.4. Freed worlds live in memory as records until T3.3b saves them, which comes after the core.

**T3.1b References across worlds as IDs** (M, high effort; depends on T3.1a, T2.3 and T2.4, because references are resolved through the world manager)
- **Builds:** the Sleepers faction's index of claimed things (structures, vessels, bags) by ID.
- **Replaces:** references to things in another world with IDs resolved through the world manager: AI targets, markers, `Opened` containers and the fold's `_ids` map keyed by object (Fold.cs:399, 466). A world out of play answers "not here", never a stale object.
- **Closes:** architecture review finding 12.
- **Proof:** a creature whose target crosses to another world outside its chain (another body, a jump) lets go; one whose target steps into a cabin of its own world keeps it. A container left open on a world that is then freed shows "not here", and so does a marker for a thing on it.

**T3.2 A change record per world, and the clock catch-up** (L, high effort, 4 slices: the record and its storage; terrain, plants and deposits; places, caves and survival points; the clock catch-up; depends on T3.1a, T2.4, T2.6 (caves and places made per world), T2.7 (cabins, for the three-deep proof), and T1.5, because changes are stored against a world rebuilt from its seed)
- **Builds:**
  - Records for terrain edits per chunk, felled plants, mined deposits, deaths, place memory, salvage progress, touched rocks and things set down, plus the survival points and bags T2.3 moves out of the suit (replicators, pods, bushes, food caches, backpacks), and the air of places' closed rooms.
  - **Caves:** found caves and their deaths, by cell key. Today `Caves._caves` and `Underworld._spots` live only in nodes (`PlaceFauna.cs:584`, `Underworld.cs:37`), and which caves exist depends on the walk (:630-640). A freed world's caves would refill and its kills be undone, against the owner's "find things as you left them". The cave sims' time comes from the galaxy clock (`PlaceFauna.cs:640`, `Underworld.cs:113` copy the zone's).
  - **One catch-up path on T2.4's galaxy clock** (ARCHITECTURE.md 4). Every simulated thing stamps its last tick on the clock. Whenever it ticks again (thawed inside the grace, rebuilt from its record, a cabin unpaused, a slow tick), it catches up over the elapsed time, after its state is complete, meaning after its folder is applied. Catch-up advances in segments ordered by events (fuel running out, a field going down, sunrise and sunset), outermost volume first, so each volume steps against its parent's path over the gap. Frozen, grace, paused and rebuilt are one path, applied to every record, claimed or not: the sky's positions, running timers, and each structure's rooms' air together with its power and fuel timers, segment by segment (`RoomAir.Advance` already catches up from a clock, RoomAir.cs:26-36). So a generator that ran dry while away is accounted for, and a claimed base's rooms drift from the moment their power ran out. Large-scale weather is read from seed and clock, never caught up. Regrowth, decay and resource meteorites aren't built yet (DESIGN.md 706, 725); they are feature F9, on this path, and only they are limited to unclaimed records (DESIGN.md 1592).
- **Closes:** OW#7, SA B-B10 (salvage resets), A-6.4.
- **Proof:** leave a star, come back, and every change is as left, with the sky and timers moved on by the time away. A base whose generator runs dry an hour into a day away comes back with its rooms drifted from that hour on; a world thawed inside the grace and a cabin unpaused advance by the same path and agree with a rebuilt one over the same elapsed time. Three deep (a shuttle in a frigate in a carrier, each with its own power and air), a live run and a caught-up one over the same gap agree.

**T3.4 The start system like any other** (M, medium effort; depends on T3.2, T3.3a, T3.1b (nothing may hold a freed world's objects), T2.4, and T2.5 and T2.6, which remove the roughly 20 starter holds)
- **Builds:** freeing for every world: a frozen world stays loaded about 2 minutes, then is freed to its record, with a full collection spread over frames so its memory comes back (ARCHITECTURE.md 2b). Freeing hands the world's dirty records to T3.3a's commit queue as a snapshot; nothing in play waits on that write (ARCHITECTURE.md 4c).
- **Deletes:** `ParkHome`. The start system is frozen, kept about 2 minutes and freed like any star's.
- **No longer waits on T3.3b** (owner, 26 Nov). A world is freed to its in-memory record (T3.2) and rebuilt from it. Crash points for park and free come with T3.3b, whose proof already crashes at every step of a park and a free. The agent checks, when briefed, that freeing needs nothing else from T3.3b.
- **Closes:** rule 3, OW risk 7.
- **Proof:** fold away, come back, and the start world is as you left it. A sleeper who dies at another star after the start world was freed wakes in their pod, with their locker intact. Freeing a world gives its memory back to T0.4's freed figure, with no frame over budget.

---

## Tier 4: one path per job
Each unifies a job and deletes the copies. Tier 4 needs only Tier 2, so it runs beside Tier 3 (26 Nov).

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
- **`Source` is any thing's ID** (ARCHITECTURE.md rule 6): a sleeper, a person, a device such as a turret, a loose item such as a chain-exploding drum, or the world (weather). A source's claim is its own: a device's is its structure's core claim, a loose item's its own claim if any, the world's none. Harm compares the source's claim with the target's once, in the shared path, under the friendly-fire setting (off by default, DESIGN.md 1514), with self-harm as DESIGN.md 659 states. XP goes only when the source is a sleeper (turret kills give none, DESIGN.md 613).
- **Exposure is a Harm source too:** suffocation, cold, heat and radiation. Today the sleeper takes it through `Suit.Hit` with its own damage kind. The target adapter carries a "doesn't breathe" flag (`Species.Mechanical` today), so people use the same path when F2 gives them air.
- **Closes:** SA B-B1, B-B2, B-B5, B-B6, and B-B4's two ram copies (the fuel drum that spares townsfolk; fire and wind that never touch them; health in two units). B-B3 is T4.2; the bike half of B-B4 is T4.5.
- **Proof:** every source hurts every target kind by the same rule. With friendly fire off, a sleeper's shot and our turret's both spare a teammate, and so does any Sleepers-claimed device; a turret's kill gives no XP; lightning and an unclaimed chain-exploding drum hurt everyone in reach.

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

**T4.4 Shelter and air: air volumes like cabin worlds** (L, medium effort, 4 slices: volumes and the `Exposure` query; nesting and openings; life support on the power model; the shelter tests, with the bunker's hard-coded shelter deleted; depends on T4.2, T1.4 for the world's air, and T2.7 for ships' cabins)
- **The model (owner, 25 Nov: air follows the cabin worlds' logic).** A ship owns its cabin world and anything aboard reads the cabin, then `Outside`. Air works the same way:
  - **every sealed volume owns its air:** a ship's or base's room (the bunker's included) or a closed room in a place. It holds oxygen, temperature and pressure, and is kept with its structure or place;
  - **volumes nest:** each has a parent, another volume or the world's own air. A ship parked inside a sealed base room reads that room, which reads the planet;
  - **an opening is any boundary face that isn't airtight now,** read from the block world's seal report (T4.2): a breach, an open door with no holding field, or a failed field. It is never "a door is open". Door and bay force fields hold the air (DESIGN.md 1026; today `FieldDoors`, building Rooms.cs:29-30). Openings link a volume to its parent, and air equalises through them, so a holed room drains to its parent (vacuum in orbit);
  - **life support regulates oxygen and temperature in every enclosed space** of its structure (owner, 25 Nov): powered, a sealed room eases to full oxygen and 20 °C (slice.json `sealedRoomC`; owner, 25 Nov: 20 °C is fine), as `RoomAir.Advance` does today (RoomAir.cs:27-36); off, it holds a while, then drifts to its parent's. The device already exists: the life support pod (`HasLifeSupportPod` and `Vessel.LifeSupport`, Vessel.cs:146-160; playtest LIFE SUPPORT IN CORE STRUCTURES). It feeds the volumes on the power model; no new device. A place's closed room without one only shelters part of the way from the outside temperature, as today;
  - **area sources** such as the survival climate pod (9 m, fuelled) warm a radius in the open; they're not volumes.
- **Builds one lookup, "what am I inside"**, used by air here and by gravity in T4.5: for a point or a thing, the innermost structure and sealed volume it's in, then the parent. A volume's parent is the volume or structure around it; a structure's is the structure whose cabin it is in, else its world, whatever its core's mode. For a body, gravity comes from the innermost structure that holds it as a child or holds its centre of mass in its inside-set; for a bare point, from the inside-set alone (ARCHITECTURE.md 2), air from the innermost sealed volume; a thing's parent is its world's structure (no `ParkedOn`).
- **Builds:** one `Exposure(point)` query on that lookup: the innermost volume at a point, then its parents, then the world. Sleepers, people, creatures, weather and later fire (F3) all ask it. The air rule moves off the suit (`Suit.Outside`, Suit.cs about 142: no air below `SurfaceWeather.NoAir` density or above the 1,060 m ceiling) onto the world.
- **Closed rooms on every world:** places, ships and bases (occupied or not) answer the same closed-room question. Today room air is only `Place.Closed` (Places/Place.Air.cs:28) plus RoomAir's kinds (Player/RoomAir.cs:7), and the bunker is a hard-coded full shelter (Suit.cs:104). That rule is deleted: the bunker is a claimed base with a life-support pod on power (T2.6), so its rooms answer like any other.
- **Replaces:** the nine shelter tests, `RoomKind` and the suit's room check (`InRoom`, Suit.cs:211-235). `Breathe` already left the suit in T2.3.
- **Closes:** SA B-B14 (a 3 m hole counts as shelter), OW#9's `Shelter` and `Enclosed`, C-E1 (the suit's shelter check knows only the bunker).
- **Proof:**
  - one answer for wind, lightning, creatures, rain and the suit at the same spot;
  - two sleepers in one cabin read the same air; opening its door in orbit with the door's field up holds the air, and with the field down or unpowered drains it to vacuum for both;
  - a breach drains a room whatever its doors are doing;
  - a ship parked inside a sealed base room on an airless moon reads the room's air;
  - **three deep:** a sleeper in a shuttle with no air of its own, inside a frigate with its life support off, inside a powered carrier, reads the carrier's air. Opening the carrier's bay in orbit holds while its field is powered; cutting the field's power drains all three in turn;
  - with life support powered, a sealed room on an ice world or a lava world eases to full oxygen and 20 °C; with it off, the room holds, then drifts to the outside's;
  - T4.4 adds pressure to the room record. That a room's air is the same after a save and reload is proven in T3.3b.

**T4.5 Ground, gravity and vehicles** (L, high effort, 3 slices: the gravity rule; the `Ground` service and the cave sims' one ground source; the bike and the seated state; depends on T2.2, T2.5 (the bike and digging moved per world), T2.7 (when cabins exist), T4.1 (`ArmsHost.SolidHit` rewritten) and T4.4, whose "what am I inside" lookup it reads)
- **Builds:**
  - one `Ground` service per diggable body, with `ArmsHost.SolidHit` folded in;
  - **one gravity rule, read from where a thing is** (like air, T4.4):
    - a world gives its own gravity: a planet or moon its own, the orbit zone 0 (OrbitZone.cs:166);
    - **gravity follows cabin-world logic** (owner, 25 Nov): inside a functioning ship or base you're at its core's fixed 1 g, whatever the world outside, so 1 g aboard on a high-g world. With a core off or unpowered, gravity comes from its parent, as air does: the structure it sits inside (a ship with its core off in a powered carrier gets the carrier's 1 g), else the world, turned into the ship's frame. Today `PhysicsWorld.GravityAt` falls back to the world only (PhysicsWorld.cs:222). Out on the hull there are no mag boots (owner, 26 Nov): only a hull's top is walkable, and off it you fall or float under the parent's gravity;
    - **bases too:** a base is walked on in its world, not a cabin (Vessel.cs:339-341), so people on a base get the world's gravity today and its core gravity does nothing. From T2.7 a base has a cabin like a ship, and the lookup covers anyone in or on it, dirt floor included, so one rule serves both. **Gravity follows the structure, air follows its sealed rooms** (owner, 25 Nov): a base's 1 g covers its inside-set (ARCHITECTURE.md 2: dirt floor, open decks and walled courtyards included, capped at its top, unchanged by damage), the same as a ship's. Blow the roof off and the air vents, but you stay at 1 g while the core runs; lose the core or its power and gravity drops to the parent's. The outside of its roof and walls is its outer hull, as on a ship;
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
- **Proof:** three deep, the innermost working core gives 1 g, and with its core off a ship takes the next one out, then the world. On a low-gravity moon the bike and creatures fall by the moon's gravity. A bag tossed in orbit drifts; tossed in a powered cabin it falls at 1 g, and with the core's gravity off it drifts. Aboard a powered base on a high-g world you're at 1 g, and still are after its roof is blown off, while its air vents. A ship with its core off parked in a powered carrier is at the carrier's 1 g. Aboard a powered ship on a high-g world you and its loose cargo are at 1 g; switch its core gravity off and both take the world's. PhysicsTest's HoverCushion no-roll check and its AcrossWater checks still pass.

**T4.6 Events, sound and markers** (M, medium effort; depends on T2.7, and T2.5, which moves markers onto the sleeper's world)
- **Builds:**
  - one `WorldEvent`, which audio, AI hearing (`Noise`, `Gunfire`), visor cues and effects all subscribe to;
  - the four voice pools brought to one set of rules;
  - `SoundCues.Heard` (Game/SoundCues.cs:13-15) carries its world, so the visor shows only the cues of the viewer's chain, their cabins and the world around them (SA A-1.11);
  - every event has a subscriber or is deleted (SF #20 lists the unused ones);
  - one `Markers(world, viewer)` source, built from faction records (marks, discoveries, claimed things, respawn points) plus the viewer's own live scan pings; only transient scan results are filtered per viewer (DESIGN.md 621, 215; ARCHITECTURE.md 6). Places not yet discovered show only through a live scan, within its range and for its time (owner, 8 and 17 Nov). The bunker is a claimed structure, so it needs no marker rule of its own. The playtest marker entries (playtest-list.md's "Markers (places, deposits, the bunker, scanner pings)", "Compass marks discovered POIs", "POIs reveal in proximity", "Compass crowded by ore labels" and "SCANNER: PLACE MARKERS ALWAYS SHOWN": markers on the thing, discovered POIs, scanner pings) are built on this source, not each on their own.
- **Closes:** SA B-B21 to B-B24; OW#16's bunker marker (`Game.Bunker && Zone == Surface`, Visor.cs:415), on the one marker source.
- **Proof:** a fuel drum is heard by creatures and towns and shows on the visor. One marker shows the same in all three views. A mark one sleeper places shows for a teammate on another world; one sleeper's scan pings show only on their own visor.

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

## After the core: climb and descent, and saving
These come after Tiers 1 to 5 (owner, 26 Nov): nothing in the core needs them, and no reported bug waits on them. Until T3.3b, a crash or a quit loses the session, as today.

**T2.8c The climb and descent between surface and orbit** (M, high effort; depends on T2.8a, T2.7, T3.3a)
- **What:** the seam (Spaceflight.Seam.cs, 621 lines) becomes a vessel `Move`, like warp and fold.
- **Its view half goes under rule 12:** the sky fade, the environment swap, the wreck fade through `GeometryInstance.Transparency`, the sky's `eye_lift` and `HoldSurfaceBehind`.
- **Replaces:** the one pending crossing (`Pending _pending`, Spaceflight.cs:82-83) with one per vessel.
- **Proof:** two sleepers climb and descend at once on one world without either's view affecting the other's, and every count matches afterwards for both (through T3.3a's queue).

**T3.3b Crash safety and the saved game** (L, high effort, 3 slices: crash points and reload; the game, faction and sleeper records with loading a game; the remaining structure records; depends on T3.2 (the per-world records), T2.4 (the galaxy clock), T3.3a, T3.1a, T3.1b (the faction's index of claimed things by ID), T2.4 (parking and freeing) and T2.8b (its proof crashes mid-tunnel))
- **Builds:**
  - the records ARCHITECTURE.md 4b lists that nothing writes today: `game` (seed, galaxy clock, mode and settings, the sleepers), `faction` (charts, standing, the index of claimed things by ID), `sleepers/<id>` (non-physical state only: vitals, pack, progress and tech, respawn choices as pod IDs and the home flag with when it was set), offline sleepers' bodies as held records inside their holders' records, like any thing in `things/`, and `manifest`. Today the ledger's sleeper record holds only the pack contents (Spaceflight.cs:687);
  - loading a game from disk;
  - **logging in:** the body is spawned by `Move` from wherever its held record now is, so one who logged off in a seat comes back wherever the ship has gone (ARCHITECTURE.md 2);
  - force field state (powered, field HP) saved with its vessel, which nobody has checked yet;
  - each room's air (oxygen, temperature, time unpowered; T4.4 adds pressure) saved with its structure;
  - a claimed record whose world won't load goes to the structure holding the home pod, on a world that loads, most recently flagged by any sleeper of its claiming faction; with no home pod, to the structure holding that faction's newest start pod on a world that loads; with neither, it stays as its own unchanged record until one exists. Unclaimed records stay as their own unchanged records. Nothing disappears, and nothing is keyed on core mode (ARCHITECTURE.md 4c, DESIGN.md 1726);
  - after a crash mid-jump, the transit world loads like any world: rebuilt from its record (which carries its jump's target anchor and time left), its tunnel resumed as an interest point, and arrival by the normal arrival `Move`;
  - devices restored;
  - records dirty-tracked and written in pieces off the main thread.
- **Closes:** OW#8, architecture review finding 3; `Harness.SaveAndReload` goes live. With T3.2 this is the save system: playtest "Saves" (playtest-list.md, "Saves"; the saves prototype isn't in the slice) is closed by these two, not built apart.
- **Proof:**
  - crash at every step of a park, a free, two crossings at once and a jump mid-tunnel, with a sleeper and a crate out in the transit world (the tunnel resumes and all arrive by the normal arrival); every count matches on reload;
  - a claimed record on a world that won't load comes back on the structure holding its faction's most recently flagged home pod on a world that loads, with none on the one holding its newest start pod on a world that loads, and with neither stays its own unchanged record; an unclaimed one stays its own unchanged record;
  - a sleeper who logs off in a seat and logs in after the ship has jumped spawns in that seat at the destination;
  - quit and load: the clock, mode, charts and standing, and each sleeper's vitals, level, tech, pack and world all match;
  - two sleepers are both saved and both come back;
  - a room's air, pressure included, is the same after a save and reload (moved from T4.4, 26 Nov);
  - **ships nested three deep** (a shuttle in a frigate in a carrier, with loose cargo in each) come back nested with every count matching, after a save and reload and after their cabins are freed and made again.

---

# Built on the core: features, not fixes

These came up during the reviews. They are **new behaviour or design work, not fixes to broken code**, so they are not part of the core work above. Each waits for the core tasks it names, then is briefed as its own feature, in whatever order the owner picks.

**F1 Many suns: binaries, trinaries and black holes** (after T2.7, T2.9). Was T2.10.
- **Today:** the galaxy knows a star's companions (`Star.Companions` and `Star.Companion`, shared/Galaxy/Core/Galaxy.cs:20; the map shows "double" or "triple"). A system's model has one `Star` (planet-surface/src/Sky/StarSystem.cs:203), so every sky, sun direction, light and shadow comes from one star. There is no light model for a black hole.
- **Design:** DESIGN.md 181 and 196 ("double shadows and sunsets, shifting temperatures"); solar-systems.md 112 and 148 (close pairs circled by planets, wide pairs with planets round one star, combined brightness sets the zones) and 144 and 660 (black holes and neutron stars).
- **Builds:**
  - Every star is a body in the system model, orbiting the pair's centre of mass, drawn from the one source of truth (celestial bodies are real). Keys don't change: every star and barycentre is already an anchor in T3.1a's tree.
  - One light per star you can see, from its direction, with its colour and its brightness by distance. It drops out below the horizon or behind an eclipsing body. A wide companion is a bright point with negligible light.
  - The shadow budget is shared by brightness. The brightest sun gets most of the atlas, a second close sun casts a softer shadow of its own (double shadows), and a third lights without shadow unless the budget allows. Each preset sets how many suns cast shadows. At the lowest preset a second sun casts its shadow only if it fits the frame budget, measured in T0.4 (owner, 25 Nov).
  - **Black holes:** the accretion disc is the light source (a large, soft, warm light from the disc, mostly ambient, weak shadows), plus the lensing look.
  - **Neutron stars and pulsars:** dim point lights. A pulsar's sweep is a radiation event on its beat, not a light.
  - Heat and climate come from the same suns: zones from the combined brightness for a close pair, and temperature shifting as each sun rises, sets or is eclipsed.
- **Proof:** on a circumbinary world, two suns cross the sky on their own paths with two shadows. The same scene has one shadow at the lowest preset if T0.4 says so. A black-hole system is lit by its disc.

**F2 Places that fit their world** (after T2.6, T1.4, T4.4)
- **Towns fit their world's air** (DESIGN.md 362): sealed habitats on vacuum and unbreathable-air worlds, with suits worn outside. On an airless test world only 67 of 150 town spots stood in closed rooms. NPC air comes back as part of this (owner, 24 Nov), not before.
- **Caves by world type:** `CaveStrength` (`PlanetTerrainSource.cs:766`) limits caves by region, not by archetype, and DESIGN.md doesn't say which archetypes have cave networks. Which do is the owner's call.
- **Vacuum fauna** (owner, 25 Nov: later): airless caves and undergrounds get their own life, through the roster T2.6 reads.
- **NPC air:** people breathe, suit up and suffocate through the shared breather model (T2.3), with proto/npcair (a0a0da94, shelved) as a starting point, not a merge.
- **Proof:** a quiet town on a fresh vacuum world runs 15 minutes with no air deaths.

**F3 Fire** (planned, not started).
Fire (planned, not started) uses up a volume's oxygen through T4.4's air volumes. It is briefed only once these are in: T1.4 (wind and rain on every world), T2.7 (fire effects in the view only), T4.1 (burn damage through Harm), T4.2 (blocks and plants as burnable material), T4.4 (rain, wet and shelter put fire out), and T4.6 (crackle, smoke, AI hearing). Fire hits are never telegraphed.

**F4 Host and clients: real co-op** (after T1.5, Tiers 2 and 3, T2.8c and T3.3b). Was T6.
- Confirm ARCHITECTURE.md section 9b against `prototypes/coop`.
- A client gets and loads everything under its sleeper's outermost world (every cabin in it, its own and others'), plus every destination that world holds as an interest point. Every networked object carries its `WorldKey`, and the per-key send filter checks it against that set, not against one world.
- The one-per-game UI statics (`Selection` and `GridView`'s drag and click state, Ui/GridView.cs:20-106) become per client.

This is the start of real co-op. It depends on Tiers 2 and 3, T2.8c and T3.3b.

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
- It builds on the lone trader's open phases (playtest-list.md, "A LONE TRADER OR SCOUT IN THE START WORLD'S SKY": crews, reactions, rim pirates and small traders) and each faction's own ships.

**F7 The full breadth of stars and worlds** (after T2.9)
- **Stars:** subclasses (G2, M4), luminosity classes (V dwarf, III giant) and brightness varied about ×0.6 to ×1.6 within a class, on T2.9's star model. The spread of the stellar classification used for variety (owner, 25 Nov: "using the qualities of classifications to increase the breadth of star types"): every subclass within each class, giants and supergiants, white dwarfs, and rarer kinds placed by region or as a system's notable feature (solar-systems.md step 1 and its special stars, such as red giants with thawing former ice worlds).
- **Worlds:** the 40 world types, each read from its zones (solar-systems.md, "Each of the 40 world types by position"). Today `StarSystem.TypeOf` gives six types by zone; the class comment (StarSystem.cs:108-111) lists the 40 types as not yet in.

**F8 Buildable cryopods and the healing booth** (after T2.3 and T3.3b)
- **Today:** neither a respawn block nor a healing block exists (owner, 25 Nov: "We never made spawn blocks, or healing bays"). Empyrion uses a cloning bay and a medic station.
- **Our respawn point is one block: the cryopod we start the game in, reused** (owner, 25 Nov), built on any structure. Respawn only; the healing booth stays its own block (DESIGN.md 581: it restores all health). DESIGN.md 120, 614 and 909 say so.
- **Two pods exist today, so they become one:**
  - the bunker's `CryoPod` (Start/Bunker.cs:737), a hand-built node of boxes, 1.04 × 2.26 × 0.98 m (Bunker.cs:760-764), with its lid, cold light and number;
  - a deco `cryo-pod` (shared/Items/data/deco.json:236), 1 × 2.5 × 1 m, a POI dressing piece.

  The block is the bunker's pod made a block of 1 × 3 × 1 cells: its 4 cm roof and base lips are trimmed to fit 1 m. The bunker's four pods, built pods and POIs' cryo rooms (dead and frosted, as in the bunker) all use it, so the bunker's hand-built pod goes (with SA Root E: the bunker is a claimed structure like any other, T2.6).
- **Builds:**
  - the cryopod block on a structure's power: a respawn point a sleeper can choose, its ID added to their record's respawn choices, and flag as home in their own record, never on the shared block (ARCHITECTURE.md 3);
  - the healing booth, restoring health;
  - the rest of DESIGN.md 680's choices: the nearest built cryopod, searched over the faction's index of pods in the same world first by distance, then the same system, then by galaxy route distance, the sleeper's own start pod, a survival tent, or a fresh start, which just adds a new start pod ID;
  - a pod takes one waking sleeper at a time.
- **Proof:** die on a moon with a cryopod built on your base there, choose it, and wake in it, the lid opening as in the bunker. Two sleepers flag different pods as home, and each wakes in their own. Two sleepers dying at once near one pod wake one at a time. The bunker's pods are the same block. A healing booth restores full health.

**F9 Regrowth, decay and resource meteorites** (after T3.2)
- Designed (DESIGN.md 706, 725) but not built: no meteorite code exists, and nothing regrows but force fields.
- Built on T3.2's one catch-up path, so a world you come back to has regrown and decayed by the time away, touching only unclaimed records.

**F10 POI variety push** (owner, 26 Nov; after T2.6, with F2)
- A large round of new place kinds and more variety within each, once places stand up on every world (T2.6) and fit their world's air (F2).
- **Starting lists:** the 15 heaviest missing references in layer-coverage.md section 5 (an observatory, school campus, ration depot, ministry hall and the rest, plus wall cities, hilltop keeps and toll gates), the everyday commerce kinds (playtest-list.md, "NEW PLACE KINDS"), "PLACE VARIETY AND OLD PARTS" and NPC gathering points (playtest-list.md), and the faction places by body plan (ship-forms.md, stations).
- **Colossal dead machines as landmarks** (owner, 26 Nov): giant machines half-buried in fields, walkers stalled on highways, huge towers and spheres over farms, and rare landmark towers in high-tech cities.
- Each new kind is built by rule from parts that exist (families, kits, room recipes), not by hand, and switches its world-layer values back on (layer-coverage.md: 280 values are off because what they name isn't built).
- **Proof:** a contact sheet of the new kinds from fresh seeds on three world types; no two of a kind alike; LayersTest's "values not built yet" check shrinks by the values the round turns on.

**F11 Landscape mixing** (owner, 26 Nov; world generation, needs no core task)
- **The idea:** the shape of the land and the look of the world are separate choices, so any landform can stand on any world type: canyons and mesas on an ice world, rolling temperate hills on a fungal one.
- **Today:** the 24 landforms already exist apart from world types (planet-generator/src/Gen/Landform.cs), and each world mixes two to five of them by region (`LandformMix`, Landforms.cs). What ties them to the type: each archetype has its own landform odds (`Odds(...)` in Archetypes.cs), and `Landforms.Recipe` scales every landform with the archetype's own numbers (billow, canyon depth, shelf, terrace step, plateau height).
- **Builds:**
  - one shared landform pool that any world type rolls from; the archetype gives the look (palette, ground materials, flora, sky), and its odds become a lean, not a limit;
  - the archetype's reshaping kept as an option, so a world can have plain canyons or canyons in its own style;
  - dry forms of the water landforms (deltas, wetlands, fjords, rivers) for worlds with no liquid: dry beds, salt flats, frozen deltas;
  - wildlife still follows the landform (LandformFauna), so mixed land brings its matching creatures.
- **Proof:** a sheet of the same seed's landform mix on six world types; every landform appears on at least three types across 50 fresh rolls; no water landform holds liquid on a world without it.

**F12 Fauna variety push** (owner, 26 Nov; after T2.6 and T4.1)
- A large round of new creatures and more variety within each world's roster, once the roster is read per world (T2.6) and creatures take damage through one path (T4.1).
- **Starting lists:** flocks and space creatures (decided-vs-built 2.22 and 3.13: schools and flyers are in, flocks and space life are not), vacuum fauna (F2), the aggressive enemies varied across body plans (playtest-list.md: "Same bugs everywhere"), and each landform's own creatures (DESIGN.md 312) as F11 mixes the land.
- **Proof:** three fresh worlds of one type share no more than a set share of their rosters (the owner sets the figure); every landform region has a creature fitted to it.

**F13 Faction ship and station forms** (owner, 26 Nov; after T2.6's hulks per world, with F6)
- **The spec:** ship-forms.md. Each faction has its own body plans, several per size, shape first, livery last: Freeholder truss spines, Meridian hammerheads, Ledger pod rings, Reaver rams, Kessari lattices, Vharn segments, and Husk-hijacked hulls. Stations follow the same plans, scaled up. Crashes roll pose, breakup and age.
- **Today:** only the start sky's trader and scout have body plans (nine each, proto/skyship2, merged; owner: happy with them). Capital hulks, derelicts and stations come from one hull builder (`HullVessel`, proto/derelicts) whose faction shows only in livery (playtest-list.md, "FACTION STYLES IN THE SHIP AND STRUCTURE GENERATORS").
- **Samples (found 26 Nov, owner):** `ship-forms/capital-forms-sheet.jpg` is the forms sheet: five rolls each for Freeholders (raft, spine, pusher), Meridian (cross, hammerhead, twin), Exchange (wheel, seedpod, tandem), Reavers (ramsled, piggyback, welded), Kessari (tube, vanes, wheels), Vharn (wasp, centipede, beetle) and Husk-hijacked hulls of each, as liners, freighters, warships, carriers and miners of 62 to 167 m. `ship-forms/small-craft-sheet.png` is the start sky's craft (merged). Both have greyed twins (`capital-forms-grey.png`, `small-craft-grey.png`). **The code that made the capital sheet** is `prototypes/poi-generator/src/Gen/Arch/ShipForm*.cs` on branch `proto/shipforms`, pushed 26 Nov (35 commits past test-build, its tip a WIP on interiors). The work starts from it, not from the spec alone.
- **Interiors that work** (owner, 26 Nov): every ship of every size is a working vessel inside: crew and passenger quarters, operational areas, storage and drive spaces, budgeted by role and size and fitted inside the body plan. Spec and reference (Traveller deck plans, ideas only) in ship-forms.md, "Interiors that work". They carry the player's own working devices (weapons, shields spaced not to overlap, generators, thrusters and the rest), so a captured ship works as it is. **Shields first need the 12 Nov rule built** (owner; one-metre-grid.md and DESIGN.md, shields): today one ellipsoid covers the whole structure (`slice/src/Vessels/ShieldShell.cs`) and a broken shield comes back after a 15 s cooldown (`power.json` `shield`). It becomes one bubble per generator, parts outside unprotected, a generator fried at 0 until repaired, and bubbles that touch, on one ship or two, frying every generator involved. Player ships follow the same rule.
- **Gates:** ship-forms.md's four: the forms sheet (six faction forms plus a Husk, five rolls each, greyed), the crash sheet (12 sites), the silhouette check alongside the sheets, and the interiors sheet and check.

**F14 Space weather** (after T1.4, T2.4 and T4.1)
- **Design:** ion storms (static on scanners, draining shields, a light show from orbit) and magnetar storms (shields and scanners out for a few seconds), DESIGN.md 213 and 202.
- **Builds:** space weather as facts of the world (star activity, fields), never of its layer (ARCHITECTURE.md 8), so an orbit's weather holds what those facts give. Large-scale storms are a pure function of seed and galaxy clock, like fronts on a surface; only their hazards near an interest point are simulated, as Harm sources (T4.1), never telegraphed.

