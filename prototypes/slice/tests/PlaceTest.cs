using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using GalHack.Physics;
using GalHack.Physics.Engine;
using Godot;
using PoiGenerator.Gen;
using Int3 = PoiGenerator.Gen.Int3;
using Slice.Game;
using Slice.Places;
using Slice.Surface;

namespace Slice.Tests;

/// <summary>
/// The planet's places as real, playable structures in the slice.
///
/// Run: .\tools\test.ps1 -Project prototypes/slice -Scene res://tests/PlaceTest.tscn
/// (-- --survey: list the places round the start of a world and what each holds, nothing else)
/// </summary>
public partial class PlaceTest : Node
{
    const int WatchdogMinutes = 15;
    IEnumerator<int> _run;
    int _failures, _passes;
    readonly Stopwatch _clock = Stopwatch.StartNew();

    public override void _Ready()
    {
        var thread = new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(TimeSpan.FromMinutes(WatchdogMinutes));
            Console.Error.WriteLine($"FAIL  the run took over {WatchdogMinutes} minutes: watchdog");
            System.Environment.Exit(1);
        }) { IsBackground = true };
        thread.Start();
        ProcessPhysicsPriority = 100;
        _run = Scenario().GetEnumerator();
    }

    public override void _PhysicsProcess(double delta)
    {
        bool more;
        try { more = _run.MoveNext(); }
        catch (Exception e)
        {
            Fail($"scenario threw: {e}");
            more = false;
        }
        if (more) return;
        GD.Print(_failures == 0 ? $"ALL PASSED ({_passes} checks in {_clock.Elapsed.TotalSeconds:F1} s)" : $"{_failures} CHECK(S) FAILED ({_passes} passed)");
        SetPhysicsProcess(false);
        Wildlife.Shutdown.Quit(this, _failures == 0 ? 0 : 1);
    }

    void Check(bool ok, string what)
    {
        if (ok) _passes++;
        else _failures++;
        GD.Print($"  {(ok ? "ok   " : "FAIL ")} {what}");
    }

    void Fail(string what) => Check(false, what);

    static string WorstViews(Game.Game game) => game.Zone.Wildlife.Sites.OrderByDescending(s => s.WorstViewMs).FirstOrDefault()?.WorstViewNote ?? "no creatures";

    static string WorstPause(Game.Game game) => game.Zone.Wildlife.Sites.OrderByDescending(s => s.WorstPauseMs).Select(s => $"{s.WorstPauseMs:F1} ms, {s.WorstPauseNote}").FirstOrDefault() ?? "none";

    IEnumerable<int> Scenario()
    {
        string[] args = OS.GetCmdlineUserArgs();
        if (Array.IndexOf(args, "--survey") >= 0)
        {
            foreach (int x in Survey(WorldChoice.FromArgs(args) ?? WorldChoice.Tested)) yield return x;
            yield break;
        }
        if (Array.IndexOf(args, "--dump") >= 0)
        {
            var game = new Game.Game { Interactive = false, World = WorldChoice.FromArgs(args) ?? WorldChoice.Tested, SkipIntro = true };
            AddChild(game);
            foreach (int x in new Driver(game).UntilPlaying()) yield return x;
            PoiInstance inst = game.Bunker.Poi.Instance;
            var g = inst.Blocks;
            var raw = inst.Nav().Reachable(inst.Layout.EntranceCell);
            PlaceMemory mem = game.Places.MemoryOf(game.Bunker.Poi);
            var over = Place.NavOf(mem).Reachable(inst.Layout.EntranceCell);
            GD.Print($"reachable: raw {raw.Count}, with the dressing {over.Count}; by level raw {string.Join(" ", raw.GroupBy(c => c.Y).OrderByDescending(q => q.Key).Select(q => $"{q.Key}:{q.Count()}"))}; dressed {string.Join(" ", over.GroupBy(c => c.Y).OrderByDescending(q => q.Key).Select(q => $"{q.Key}:{q.Count()}"))}");
            foreach (var c in new[] { new PoiGenerator.Gen.Int3(13, -16, 8), new PoiGenerator.Gen.Int3(10, -16, 8), new PoiGenerator.Gen.Int3(9, -16, 9), new PoiGenerator.Gen.Int3(8, -16, 8), new PoiGenerator.Gen.Int3(8, -16, 7), new PoiGenerator.Gen.Int3(8, -16, 6) })
                GD.Print($"{c}: {g.Get(c)} / up {g.Get(c + PoiGenerator.Gen.Int3.Up)} / down {g.Get(c + PoiGenerator.Gen.Int3.Down)} raw {raw.Contains(c)} dressed {over.Contains(c)}");
            var rawPath = inst.Nav().FindPath(inst.Layout.EntranceCell, new PoiGenerator.Gen.Int3(13, -16, 8));
            var dressedNav = Place.NavOf(mem);
            var blockedCells = mem.Rooms.Values.SelectMany(r => r.Cells).ToHashSet();
            GD.Print($"rawpath {rawPath?.Count}: {string.Join(" ", rawPath?.Where(c => !over.Contains(c)).Select(c => $"{c}[{g.Get(c).Type} blocked {blockedCells.Contains(c)} standable {dressedNav.Standable(c)}]") ?? Array.Empty<string>())}");
            GD.Print($"rooms {mem.Rooms.Count}: {string.Join("; ", mem.Rooms.Values.Select(r => $"{inst.Layout.Modules[r.Module].Purpose}/{inst.Layout.Modules[r.Module].Kind}@{r.Corner} {r.Room.Width}x{r.Room.Depth} solid {r.Solid.Count} of {r.Plan.Placements.Count} cells {r.Cells.Count}"))}");
            var blockedAll = mem.Rooms.Values.SelectMany(r => r.Cells).ToHashSet();
            for (int z = 0; z <= 17; z++)
            {
                var row = new System.Text.StringBuilder();
                for (int x = 0; x <= 28; x++)
                {
                    var c = new PoiGenerator.Gen.Int3(x, -16, z);
                    row.Append(over.Contains(c) ? 'o' : raw.Contains(c) ? 'r' : blockedAll.Contains(c) ? 'B' : g.Get(c).IsAir ? ' ' : g.Get(c).Type == BlockType.Cube ? '#' : g.Get(c).Type == BlockType.Door ? 'D' : g.Get(c).Type == BlockType.Breakable ? 'X' : g.Get(c).Type == BlockType.Growth ? 'G' : '+');
                }
                GD.Print($"map{z,3} {row}");
            }
            for (int y = 0; y >= -17; y--)
            {
                GD.Print($"-- y {y} (x {g.Min.X}..{g.Max.X}, z {g.Min.Z}..{g.Max.Z})");
                for (int z = g.Min.Z; z <= g.Max.Z; z++)
                {
                    var row = new System.Text.StringBuilder();
                    for (int x = g.Min.X; x <= g.Max.X; x++)
                    {
                        Block b = g.Get(new PoiGenerator.Gen.Int3(x, y, z));
                        row.Append(b.Type switch
                        {
                            BlockType.Air => inst.Layout.Occupied[new PoiGenerator.Gen.Int3(x, y, z)] ? '.' : ' ',
                            BlockType.Cube => '#', BlockType.Stair => "NESW"[(int)b.Facing % 4], BlockType.Ramp => "nesw"[(int)b.Facing % 4],
                            BlockType.Door => 'D', BlockType.Ladder => 'H', BlockType.Cache => '$', BlockType.Breakable => 'X', BlockType.Rail => 'r',
                            _ => '+',
                        });
                    }
                    GD.Print($"{z,4} {row}");
                }
            }
            yield break;
        }
        if (Array.IndexOf(args, "--town-only") >= 0)
        {
            foreach (int x in Settlement(WorldChoice.FromArgs(args) ?? WorldChoice.Tested)) yield return x;
            yield break;
        }
        if (Array.IndexOf(args, "--find-town") >= 0)
        {
            foreach (int x in FindTown()) yield return x;
            yield break;
        }
        if (Array.IndexOf(args, "--walk-only") < 0)
        {
            foreach (int x in Systems(WorldChoice.FromArgs(args) ?? WorldChoice.Tested)) yield return x;
            foreach (int x in BuiltDoors(WorldChoice.FromArgs(args) ?? WorldChoice.Tested)) yield return x;
            foreach (int x in Bodies(WorldChoice.FromArgs(args) ?? WorldChoice.Tested)) yield return x;
        }
        if (Array.IndexOf(args, "--doors-only") >= 0) yield break;
        foreach (int x in Neutrals(WorldChoice.Fresh("temperate"))) yield return x;
        if (Array.IndexOf(args, "--neutrals-only") >= 0) yield break;
        foreach (int x in RealPlayer(WorldChoice.FromArgs(args) ?? WorldChoice.Tested)) yield return x;
        if (Array.IndexOf(args, "--walk-only") < 0) foreach (int x in Settlement(WorldChoice.FromArgs(args) ?? WorldChoice.Tested)) yield return x;
    }

    /// <summary>
    /// The real-player visit, with the game's own inputs only and no teleports: out of the bunker,
    /// over the land to the place in view, in through its door, a door opened, a cache looted, a
    /// hidden wall broken, a creature's tell met, down a level by its stairs, and out again.
    /// </summary>
    IEnumerable<int> RealPlayer(WorldChoice world)
    {
        var game = new Game.Game { Interactive = false, World = world, SkipIntro = true };
        AddChild(game);
        var drive = new Driver(game);
        var play = new Playthrough(game);
        foreach (int x in drive.UntilPlaying()) yield return x;
        game.Zone.Weather.Bridge.Calm = true;
        GD.Print($"-- the real-player visit, {world} --");
        int rescues = 0;
        game.Zone.Physics.Rescued += r => { if (r.Body == game.Survivor.Body) { rescues++; GD.Print($"  (rescued from {r.Inside}: {r.From} -> {r.To})"); } };
        float worst = 0f;
        bool fell = false;
        Vector3 last = game.Survivor.WorldFeet;
        IEnumerable<int> Watched(IEnumerable<int> steps)
        {
            foreach (int x in steps)
            {
                Vector3 now = game.Survivor.WorldFeet;
                worst = MathF.Max(worst, now.DistanceTo(last));
                last = now;
                WatchTells(game);
                if (game.Suit.Dead) yield break;
                if (game.Survivor.Body.Velocity.Y < -8f && !fell)
                {
                    fell = true;
                    Place at = game.Places.PlaceAt(now);
                    GD.Print($"  (falling at {now}, {(at != null ? $"cell {at.CellAt(now)}, block under {at.Inst.Blocks.Get(at.CellAt(now) + PoiGenerator.Gen.Int3.Down)}, nearest creature {at.Memory.Fauna?.Sim?.Creatures.Where(c => !c.Dead).Min(c => (c.Position - at.FromWorld(now)).Length()):F1} m" : "outside any place")})");
                }
                if (!_defending)
                {
                    _defending = true;
                    foreach (int y in Defend(game, play, drive)) yield return y;
                    _defending = false;
                }
                yield return x;
            }
        }
        game.Zone.Wildlife.Attacked += e =>
        {
            if (!e.Hit) return;
            _bites++;
            if (_firstBite < 0) _firstBite = game.Stats.Seconds;
            if (_bitBy.Add(e.Creature) && !_warned.Contains(e.Creature)) _untold++;
            GD.Print($"  (bitten by a {e.Creature.Species.CommonName} ({e.Creature.Species.Role}): {e.Damage:F0}, health {game.Suit.HealthShare * 100:F0}%)");
        };
        game.Zone.Wildlife.Telegraphed += e => _warned.Add(e.Creature);
        var walk = new PlaceWalk(game, drive);
        Slice.Start.Bunker b = game.Bunker;
        PlanetSurface.Poi.PoiSite site = b.Poi;

        // You wake empty-handed: the tool first, then out of the bunker with the locker's kit.
        foreach (int x in Watched(play.MakeTool())) yield return x;
        foreach (int x in Watched(drive.WalkTo(b.LockerFront, 0.4f, 15f))) yield return x;
        foreach (int x in Watched(drive.LookAt(b.Locker, 8))) yield return x;
        foreach (int x in Watched(drive.Tap(Key.E))) yield return x;
        foreach (int x in Watched(drive.Wait(2))) yield return x;
        foreach (int x in Watched(drive.ClickWidget("box:take-all"))) yield return x;
        foreach (int x in Watched(play.CloseMenu())) yield return x;
        foreach (int x in Watched(play.ToToolbar("patch-kit", 7))) yield return x;
        foreach (int x in Watched(play.CloseMenu())) yield return x;
        foreach (int x in Watched(play.Select(0))) yield return x;
        foreach (int x in Watched(drive.ClimbOut(b))) yield return x;
        Check(drive.OnSurface, "out of the bunker on foot, a patch kit on toolbar slot 8");

        // Over the land to the place in view; it stands up long before you're close enough to see in.
        Vector3 entrance = game.Zone.Planet.NearestCopy(new Vector3(site.Entrance.X + 0.5f, site.Entrance.Y, site.Entrance.Z + 0.5f), game.Survivor.WorldFeet);
        float startOff = game.Places.DistanceTo(site, game.Survivor.WorldFeet);
        float stoodAt = -1f;
        game.Places.Stood += p => { if (p.Site == site) stoodAt = game.Places.DistanceTo(site, game.Survivor.WorldFeet); };
        double t0 = game.Stats.Seconds;
        // A roaming pack on the way decides nothing about the land route or the place: held still from here (printed).
        GD.Print("  (TEST: creatures held still on the walk over the land)");
        game.Zone.Wildlife.Frozen = true;
        List<Vector3> way = walk.Overland(game.Survivor.WorldFeet, entrance, site);
        Check(way != null, $"a way over the land to it, picked as a player picks one by eye: {way?.Count * 4} m of 4 m steps round cave mouths, water and steep ground");
        if (way == null) { GD.Print($"  ({string.Join("; ", walk.Notes)})"); yield break; }
        for (int i = 2; i < way.Count; i += 3)
        {
            // Near the place, no hops: a hop on the jetpack lands you on its roof.
            bool near = way[i].DistanceTo(entrance) < 40f;
            foreach (int x in Watched(drive.WalkTo(way[i], near ? 1.5f : 2.5f, 40f, sprint: !near, dig: false, hop: !near))) yield return x;
            if (game.Suit.Dead) break;
        }
        foreach (int x in Watched(drive.WalkTo(entrance, 0.8f, 30f, sprint: false, dig: false))) yield return x;
        if (game.Suit.Dead) { Fail("the castaway died on the walk to the place"); game.QueueFree(); yield break; }
        Vector3 f = game.Survivor.WorldFeet;
        GD.Print($"  (arrived: feet {f}, the way in {entrance}, ground {game.Zone.GroundAt(f.X, f.Z).Y:F1})");
        Place place = game.Places.PlaceOf(site);
        Check(place is { Ready: true } && new Vector2(game.Survivor.WorldFeet.X - entrance.X, game.Survivor.WorldFeet.Z - entrance.Z).Length() < 3f,
            $"walked {startOff:F0} m over the land to {site.Name}'s way in ({game.Stats.Seconds - t0:F0} s); it stood up as a structure {stoodAt:F0} m off");
        if (place == null) yield break;
        Check(game.Places.WorstFrameMs < 8.0, $"built while walking with no hitch: worst frame {game.Places.WorstFrameMs:F1} ms of building ({string.Join(", ", game.Places.StageWorst.Select(kv => $"{kv.Key} {kv.Value:F1}"))})");

        PoiInstance inst = place.Inst;
        var nav = place.Nav();
        PoiGenerator.Gen.Int3 entry = inst.Layout.EntranceCell;
        GD.Print($"  (at the way in: {walk.Here(place, nav)}, the entrance cell {entry})");

        // Its creatures hold still while the place is walked (printed, as PlayerTest does): a nest on
        // the lowest level would decide the run. They wake below, for the meeting with one of them.
        GD.Print("  (TEST: creatures held still while the place is walked, so a nest can't decide the run; they wake for the meeting below)");
        game.Zone.Wildlife.Frozen = true;

        // In through the way in, and a door opened with E on the way to a cache.
        // A cache a level or more down where the place has one you can get to (the stairs are walked on
        // the way); a farmhouse keeps everything on the one floor, and then there are no stairs to walk.
        // (Not one behind a locked door: the keycard comes further on.)
        bool Locked(PoiGenerator.Gen.Int3 c) => place.Doors.Any(d => d.Clearance != null && (d.Cells.Contains(c) || d.Cells.Contains(c + PoiGenerator.Gen.Int3.Up)));
        var ways = inst.Caches.Where(c => !c.Secret && nav.AccessCell(c.Cell) is PoiGenerator.Gen.Int3)
            .Select(c => (Cache: c, Steps: FreeSteps(nav.FindPath(walk.Here(place, nav), nav.AccessCell(c.Cell).Value, 200_000)))).ToList();
        // No card in the bag yet: a way through a locked door is no way at all.
        int FreeSteps(List<PoiGenerator.Gen.Int3> path) => path == null || path.Any(Locked) ? 9999 : path.Count;
        bool Below((LootCache Cache, int Steps) w) => w.Steps < 9999 && w.Cache.Cell.Y <= entry.Y - 4;
        bool downstairs = ways.Any(Below);
        LootCache cache = ways.OrderBy(w => Below(w) ? 0 : 1).ThenBy(w => w.Steps).First().Cache;
        bool ok = false;
        foreach (int x in Watched(walk.To(place, nav.AccessCell(cache.Cell).Value, r => ok = r))) yield return x;
        Check(ok, $"in through the way in and to a cache, {walk.DoorsOpened} door{(walk.DoorsOpened == 1 ? "" : "s")} opened with E on the way {(walk.Notes.Count > 0 ? $"({string.Join("; ", walk.Notes)})" : "")}");
        Slice.Items.Lootable loot = place.Memory.Caches[cache];
        int before = game.Pack.Storage.Contents.Total;
        int had = loot.Storage.Contents.Total;
        foreach (int x in Watched(drive.LookAt(place.WorldOf(cache.Cell, 0.4f), 8))) yield return x;
        string prompt = game.UsePrompt();
        foreach (int x in Watched(drive.Tap(Key.E))) yield return x;
        foreach (int x in Watched(drive.Wait(2))) yield return x;
        bool opened = game.Opened?.Loot == loot && game.Menu.IsOpen;
        if (opened) foreach (int x in Watched(drive.ClickWidget("box:take-all"))) yield return x;
        Check(opened && loot.Storage.IsEmpty && game.Pack.Storage.Contents.Total >= before + had,
            $"'{prompt}': E opens it beside the bag; TAKE ALL takes its {had} items into the bag (now {string.Join(", ", game.Pack.Storage.Contents.Select(kv => $"{kv.Value} {kv.Key}"))})");
        foreach (int x in Watched(play.CloseMenu())) yield return x;
        Check(game.Zone.Loot.All.Contains(loot), "the emptied cache stays where it stands (a place's containers never go)");
        float entryY = place.ToWorld(new Vector3(0, entry.Y, 0)).Y;
        if (downstairs) Check(game.Survivor.WorldFeet.Y < entryY - 3.5f, // a mine goes down by its ramps, not stairs
            $"down a level to get there ({walk.StairCells} stair cells and {walk.LadderCells} of ladder walked, {entryY - game.Survivor.WorldFeet.Y:F1} m below the way in)");
        else GD.Print($"  ({inst.Caches.Count} caches, none below the way in here: no stairs to walk down to one)");


        // A locked door: refused without its card; the card, lying where its owner fell, taken with E; then it opens.
        var held = new HashSet<string>();
        Place.Door locked = null;
        KeyCard key = null;
        foreach (KeyCard k in inst.Extras.Keys.OrderBy(k => nav.FindPath(walk.Here(place, nav), k.Cell, 200_000)?.Count ?? 9999))
        {
            var toKey = nav.FindPath(walk.Here(place, nav), k.Cell, 200_000);
            if (toKey == null || toKey.Any(c => place.Doors.Any(d => d.Clearance != null && (d.Cells.Contains(c) || d.Cells.Contains(c + PoiGenerator.Gen.Int3.Up))))) continue;
            int rank = Array.IndexOf(Game.Game.Clearances, k.Clearance);
            locked = place.Doors.Where(d => d.Clearance != null && Array.IndexOf(Game.Game.Clearances, d.Clearance) <= rank)
                .OrderBy(d => d.Cells.Min(c => c.Manhattan(k.Cell))).FirstOrDefault();
            if (locked == null) continue;
            key = k;
            break;
        }
        if (inst.Extras.Keys.Count > 0) // a mine or a farm has none
            Check(key != null, $"a keycard ({key?.Clearance}) lies within reach, and a door it opens ({inst.Extras.Keys.Count} keys, {place.Doors.Count(d => d.Clearance != null)} locked doors)");
        if (key != null)
        {
            // To the locked door first: E says what it needs.
            PoiGenerator.Gen.Int3 side = DoorSide(place, nav, locked, walk.Here(place, nav));
            foreach (int x in Watched(walk.To(place, side, r => ok = r))) yield return x;
            foreach (int x in Watched(drive.LookAt(place.ToWorld(locked.Centre), 6))) yield return x;
            string refusedPrompt = game.UsePrompt();
            foreach (int x in Watched(drive.Tap(Key.E))) yield return x;
            foreach (int x in Watched(drive.Wait(60))) yield return x;
            Check(ok && locked.IsShut && refusedPrompt.Contains("LOCKED"), $"at the {locked.Clearance} door without its card: '{refusedPrompt}', and E leaves it shut (walked there {ok}, to {side}, feet {place.FromWorld(game.Survivor.WorldFeet)}, door {locked.Index} at {locked.Centre}, {game.Survivor.WorldEye().DistanceTo(place.ToWorld(locked.Centre)):F1} m off, lift {locked.Lift}, seen {DoorSight(game, place)}{(walk.Notes.Count > 0 ? $"; {walk.Notes[^1]}" : "")})");
            // The card.
            foreach (int x in Watched(walk.To(place, nav.AccessCell(key.Cell) ?? key.Cell, r => ok = r))) yield return x;
            foreach (int x in Watched(drive.LookAt(place.ToWorld(new Vector3(key.Cell.X + 0.55f, key.Cell.Y + 0.08f, key.Cell.Z + 0.42f)), 8))) yield return x;
            string cardPrompt = game.UsePrompt();
            foreach (int x in Watched(drive.Tap(Key.E))) yield return x;
            Check(ok && game.Pack.Storage.Count($"keycard-{key.Clearance}") == 1, $"'{cardPrompt}': E takes the {key.Clearance} keycard off its dead owner into the bag");
            // Back to the door: it opens now, and through it.
            side = DoorSide(place, nav, locked, walk.Here(place, nav));
            foreach (int x in Watched(walk.To(place, side, r => ok = r))) yield return x;
            foreach (int x in Watched(drive.LookAt(place.ToWorld(locked.Centre), 6))) yield return x;
            foreach (int x in Watched(drive.Tap(Key.E))) yield return x;
            foreach (int x in Watched(play.WaitFor(() => locked.Open > 0.95f, 3))) yield return x;
            Check(locked.Open > 0.95f, $"E on the {locked.Clearance} door with the card: it slides open ({walk.DoorsOpened + 1} doors opened with E)");
        }

        // A hidden room: the cracked panel's draft gives it away; the survival tool breaks it, and the stash behind is looted.
        SecretRoom secret = inst.Extras.Secrets.FirstOrDefault();
        if (secret != null)
        {
            // The other keycards first: the wall may lie behind a door that wants a higher one.
            foreach (KeyCard other in inst.Extras.Keys.Where(k => k != key && !place.Memory.KeysTaken.Contains(k)).ToList())
            {
                foreach (int x in Watched(walk.To(place, nav.AccessCell(other.Cell) ?? other.Cell, r => ok = r))) yield return x;
                foreach (int x in Watched(drive.LookAt(place.ToWorld(new Vector3(other.Cell.X + 0.55f, other.Cell.Y + 0.08f, other.Cell.Z + 0.42f)), 8))) yield return x;
                foreach (int x in Watched(drive.Tap(Key.E))) yield return x;
                GD.Print($"  (the other keycard: {other.Clearance} at {other.Cell}, reached {ok}, in the bag {game.Pack.Storage.Count($"keycard-{other.Clearance}")})");
            }
            PoiGenerator.Gen.Int3 low = secret.WallCells.OrderBy(c => c.Y).First();
            PoiGenerator.Gen.Int3 front = low + place.FoundFrom(secret);
            // The nearest clear spot in front of the panel; where the Bloom has grown over the way to
            // it, the tool cuts a way through, as a player would (nothing in a place is uncuttable).
            foreach (PoiGenerator.Gen.Int3 c in secret.WallCells.Where(c => c.Y == low.Y))
                if (!inst.Blocks.Get(c + place.FoundFrom(secret)).IsAir && inst.Blocks.Get(c + place.FoundFrom(secret)).Type != BlockType.Scatter) continue;
                else { low = c; front = c + place.FoundFrom(secret); break; }
            foreach (int x in Watched(walk.CutWay(place, front, r => ok = r))) yield return x;
            Vector3 wallMiddle = place.ToWorld(new Vector3(low.X + 0.5f, low.Y + 1.2f, low.Z + 0.5f));
            foreach (int x in Watched(drive.LookAt(wallMiddle, 8))) yield return x;
            string label = game.Tool.TargetLabel;
            // Furniture can stand before the panel: a player takes it away first.
            for (int i = 0; i < 4 && label != "CRACKED PANEL" && label != ""; i++)
            {
                string piece = label;
                foreach (int x in Watched(drive.HoldPrimary(wallMiddle, 300, () => game.Tool.TargetLabel != piece))) yield return x;
                foreach (int x in Watched(drive.Wait(2))) yield return x;
                label = game.Tool.TargetLabel;
            }
            foreach (int x in Watched(drive.HoldPrimary(wallMiddle, 90, () => place.Memory.Broken.Contains(secret)))) yield return x;
            Check(ok && label == "CRACKED PANEL" && place.Memory.Broken.Contains(secret) && secret.WallCells.All(c => inst.Blocks.Get(c).IsAir),
                $"the hidden room's cracked panel ('{label}' by the crosshair, a draft through it) broken with the survival tool");
            LootCache stash = inst.Caches.FirstOrDefault(c => c.Secret && c.Module == secret.Module);
            nav = place.Nav();
            if (stash != null && nav.AccessCell(stash.Cell) is PoiGenerator.Gen.Int3 at && nav.FindPath(walk.Here(place, nav), at, 400_000) == null)
                GD.Print("  (the Bloom has grown shut the way to the panel: the room behind is open, but it would take cutting a way through to walk in)");
            else if (stash != null && nav.AccessCell(stash.Cell) is PoiGenerator.Gen.Int3 at2)
            {
                at = at2;
                foreach (int x in Watched(walk.To(place, at, r => ok = r))) yield return x;
                foreach (int x in Watched(drive.LookAt(place.WorldOf(stash.Cell, 0.4f), 8))) yield return x;
                foreach (int x in Watched(drive.Tap(Key.E))) yield return x;
                foreach (int x in Watched(drive.Wait(2))) yield return x;
                bool stashOpen = game.Opened?.Loot == place.Memory.Caches[stash];
                int inStash = place.Memory.Caches[stash].Storage.Contents.Total;
                if (stashOpen) foreach (int x in Watched(drive.ClickWidget("box:take-all"))) yield return x;
                foreach (int x in Watched(play.CloseMenu())) yield return x;
                Check(ok && stashOpen && place.Memory.Caches[stash].Storage.IsEmpty, $"into the hidden room, and its stash looted ({inStash} items)");
            }
        }

        // Salvage: the survival tool takes the place apart for materials: a piece of a room's dressing, then a block.
        {
            nav = place.Nav();
            PoiGenerator.Gen.Int3 from = walk.Here(place, nav);
            var open = nav.Reachable(from);
            int Near(Vector3 at) { var q = place.CellAt(at); return open.Where(c => c.Y == q.Y).Select(c => c.Manhattan(q)).DefaultIfEmpty(9999).Min(); }
            DecoHit piece = place.Pieces().Where(h => h.Room.Solid.Contains(h.Index) && h.Node.GlobalPosition.Y - game.Survivor.WorldFeet.Y is > -2f and < 2f && Near(h.Node.GlobalPosition) <= 3)
                .OrderBy(h => h.Node.GlobalPosition.DistanceTo(game.Survivor.WorldFeet)).FirstOrDefault();
            if (piece != null)
            {
                var pc = place.CellAt(piece.Node.GlobalPosition);
                PoiGenerator.Gen.Int3 stand = open.Where(c => c.Y == pc.Y).OrderBy(c => c.Manhattan(pc)).First();
                foreach (int x in Watched(walk.To(place, stand, r => ok = r))) yield return x;
                Vector3 mid = piece.Node.GlobalPosition + Vector3.Up * MathF.Min(0.6f, piece.Piece.Height * 0.5f);
                int bag = game.Pack.Storage.Contents.Total;
                double Carried() => place.Memory.SalvageCarry.Values.Sum(); // a small piece pays in fractions, carried in the place's memory until they make a whole item
                double carried = Carried();
                string label = "";
                foreach (int x in Watched(drive.HoldPrimary(mid, 240, () => { label = game.Tool.TargetLabel.Length > 0 ? game.Tool.TargetLabel : label; return !piece.Node.Visible; }))) yield return x;
                Check(!piece.Node.Visible && game.Pack.Storage.Contents.Total + Carried() > bag + carried, $"the survival tool salvages a {piece.Piece.Name} ('{label}' by the crosshair): gone from the room, {game.Pack.Storage.Contents.Total - bag} items in the bag, {Carried() - carried:+0.##;-0.##;0} carried to the next piece");
            }
            // A block of the wall beside you: gone for good, the wall meshed again round the hole.
            Vector3 eye = game.Survivor.WorldEye(), look = game.Survivor.WorldLook() with { Y = 0f };
            PoiGenerator.Gen.Int3 wallCell = default;
            foreach (Dir d in Dirs.Horizontal)
            {
                var q = walk.Here(place, nav);
                for (int k = 1; k <= 5; k++)
                {
                    var c = q + d.Vec() * k + PoiGenerator.Gen.Int3.Up;
                    Block found = inst.Blocks.Get(c);
                    if (found.IsAir) continue;
                    if (found.Type == BlockType.Cube && game.Places.Yield(found) != null) wallCell = c;
                    break;
                }
                if (wallCell != default) break;
            }
            if (wallCell == default) GD.Print($"  (no built wall beside you here: {inst.Layout.Family.Id} is cut from the rock)");
            if (wallCell != default)
            {
                Block was = inst.Blocks.Get(wallCell);
                int bag = game.Pack.Storage.Contents.Total, chunks = place.ChunkCount;
                foreach (int x in Watched(drive.HoldPrimary(place.WorldOf(wallCell), 480, () => inst.Blocks.Get(wallCell).IsAir))) yield return x;
                Check(inst.Blocks.Get(wallCell).IsAir && place.Memory.Modified, $"and a {was.Material.ToString().ToLowerInvariant()} block of the wall comes away, finite: {BlockInfo.HitPoints(was)} hit points worked through ({game.Pack.Storage.Contents.Total - bag} items so far, {game.Places.Salvaged} pieces salvaged)");
            }
        }

        // The creatures: up to one of the place's hunters, awake: its tell (grit, a shadow, a scratch,
        // a rattle) comes before it strikes, its wind-up is warned, and it can be shot down and looted.
        foreach (int x in Watched(MeetHunter(game, place, walk, drive))) yield return x;
        if (_nothingHere) GD.Print("  (nothing to meet in this place: tells and shooting not exercised on this world)");
        else Check(_aimedTells > 0 && _untold == 0,
            $"met a {_hunter?.Species.CommonName} ({_hunter?.Species.Role}): {_aimedTells} tells aimed at you, the nearest {_nearestTell:F1} m off; {_bites} bites, every creature's first warned before it landed ({_untold} not), health {game.Suit.HealthShare * 100:F0}%");
        if (!_nothingHere)
        {
            Check(_lastDown != null, $"they can be shot down in the place's rooms ({_shotDown} shot down, {_healed} patch kits used)");
            Slice.Items.Lootable body = game.Zone.Loot.All.FirstOrDefault(l => l.Creature == _lastDown);
            Check(body != null, $"a body lies where it fell, a container: {body?.Name} ({body?.Storage.Contents.Total} items)");
            Check(game.Zone.Blood.Bursts > 0, $"hits show: {game.Zone.Blood.Bursts} spurts of its blood and {game.Zone.Blood.Splats} splats on the surfaces round it");
            if (_bites > 0) Check(game.Hands.PeakJolt > 0f, $"a bite knocks the view: {game.Hands.PeakJolt:F2}");
        }
        game.Zone.Wildlife.Frozen = true;

        // And out the way we came.
        foreach (int x in Watched(walk.To(place, entry, r => ok = r))) yield return x;
        Vector3 outward = place.ToWorld(new Vector3(entry.X + 0.5f, entry.Y, entry.Z + 0.5f) + new Vector3(inst.Layout.EntranceFacing.Vec().X, 0, inst.Layout.EntranceFacing.Vec().Z) * 12f);
        foreach (int x in Watched(drive.WalkTo(outward, 1.5f, 30f, sprint: false, dig: false, hop: false))) yield return x;
        Vector3 out1 = game.Survivor.WorldFeet;
        Check(ok && !inst.Layout.Occupied[place.CellAt(out1)] && MathF.Abs(out1.Y - game.Zone.GroundAt(out1.X, out1.Z).Y) < 1.5f && !game.Suit.Dead,
            $"and walks out of the way in, onto the open ground again (health {game.Suit.HealthShare * 100:F0}%)");
        Check(rescues == 0 && worst < 1.5f, $"no teleports all the visit (largest move in a tick {worst:F2} m), never rescued");

        double views = game.Zone.Wildlife.Sites.Count > 0 ? game.Zone.Wildlife.Sites.Max(s => s.WorstViewMs) : 0;
        Check(views < 8.0, $"its creatures went in a few a frame as you came near: worst {views:F1} ms a frame ({WorstViews(game)})");
        GD.Print($"  (longest collector pause by then: {WorstPause(game)})");
        GD.Print($"  (build log: {string.Join("; ", game.Places.BuildLog.Select(l => $"{l.Name} {l.Seconds:F2} s, {l.Chunks} chunks"))}; slow steps: {string.Join(", ", game.Places.SlowSteps.Select(s => $"{s.Stage} {s.Ms:F1}{(s.Collected ? " (GC)" : "")}"))})");
        GD.Print($"  (shot down {_shotDown} creatures that came for you, {_healed} patch kits used)");
        GD.Print($"  (walk notes: {string.Join("; ", walk.Notes)}; worst move {worst:F2} m, rescues {rescues}; tells {game.Zone.Wildlife.Sites.Sum(s => s.TellsShown)})");
        game.QueueFree();
        yield return 0;
    }

    bool _defending;
    int _shotDown, _healed;
    readonly Dictionary<Wildlife.Creature, double> _shotAt = new();

    /// <summary>
    /// What a player does when something in a place comes for them: stop, turn, and shoot it (the
    /// survival tool's bolt) until it is down or gone; and a patch kit when health runs low.
    /// </summary>
    IEnumerable<int> Defend(Game.Game game, Playthrough play, Driver drive)
    {
        if (game.Menu.IsOpen) yield break;
        if (game.Suit.HealthShare < 0.5 && game.Pack.Storage.Count("patch-kit") > 0 && game.Pack.Grid[7].Item == "patch-kit")
        {
            int was = game.Pack.Selected;
            game.Controls.ReleaseAll();
            foreach (int x in play.Select(7)) yield return x;
            foreach (int x in drive.Click()) yield return x;
            foreach (int x in drive.Wait(3)) yield return x;
            foreach (int x in play.Select(was)) yield return x;
            _healed++;
        }
        Vector3 feet = game.Survivor.WorldFeet;
        foreach (DeepSite s in game.Zone.Wildlife.Sites)
            foreach (Wildlife.Creature c in s.Sim.Creatures)
            {
                if (c.Dead || c.Removed || c.Species.Role == Wildlife.DeepRole.Subsister) continue;
                if (s.World(c.Position).DistanceTo(feet) > 7f || c.InWalls) continue;
                if (c.Target != s.Player && c.AttackTarget != s.Player && c.Noticed != s.Player && c.TellTarget != s.Player) continue;
                // Meeting a hunter to see its tell: hold fire till it has given one, or it would die before it warned anyone.
                if (_awaitTell && _aimedTells == 0 && c.Species.Tells.Length > 0 && game.Suit.HealthShare > 0.5) continue;
                // A clear shot only, and not the same one over and over: a player gives up on what they can't hit.
                if (_shotAt.TryGetValue(c, out double when) && game.Stats.Seconds - when < 4.0) continue;
                _shotAt[c] = game.Stats.Seconds;
                var sight = PhysicsRayQueryParameters3D.Create(game.Survivor.WorldEye(), s.World(c.Rig != null ? c.Rig.BoundsCenter : c.Position), Layers.World | Layers.Hull);
                if (game.Zone.GetWorld3D().DirectSpaceState.IntersectRay(sight).Count > 0) continue;
                // It's coming: stand and shoot it.
                game.Controls.ReleaseAll();
                var clock = Stopwatch.StartNew();
                foreach (int x in play.Tool(Slice.Player.ToolMode.Shoot)) yield return x;
                float hp = c.Health;
                for (int tick = 0; tick < 300 && !c.Dead && !c.Removed && s.World(c.Position).DistanceTo(game.Survivor.WorldFeet) < 16f && !game.Suit.Dead; tick++)
                {
                    drive.Face(s.World(c.Rig != null ? c.Rig.BoundsCenter : c.Position + Vector3.Up * 0.4f));
                    bool fire = tick % 14 == 0 && !c.InWalls;
                    game.Controls.Primary = fire;
                    game.Controls.PrimaryPressed = fire;
                    yield return 0;
                }
                game.Controls.Primary = false;
                GD.Print($"  (stood and shot at a {c.Species.CommonName} ({c.Species.Role}): {hp:F0} to {c.Health:F0} health, {(c.Dead ? "down" : c.InWalls ? "gone into the walls" : "got away")})");
                if (c.Dead) { _shotDown++; _lastDown = c; }
                foreach (int x in play.Tool(Slice.Player.ToolMode.Gather)) yield return x;
                yield break;
            }
    }

    int _aimedTells, _bites;
    double _firstTell = -1, _firstBite = -1;

    Wildlife.Creature _hunter, _lastDown;
    int _untold;
    bool _nothingHere, _awaitTell;
    readonly HashSet<Wildlife.Creature> _warned = new(), _bitBy = new();

    /// <summary>Walks up to a hunter that gives tells (held still), wakes the creatures, and stays until it is dealt with.</summary>
    IEnumerable<int> MeetHunter(Game.Game game, Place place, PlaceWalk walk, Driver drive)
    {
        DeepSite s = game.Zone.Wildlife.Sites.FirstOrDefault(q => q.Sim == place.Memory.Fauna?.Sim);
        if (s == null)
        {
            GD.Print("  (nothing lives here: a place can be empty and silent)");
            _nothingHere = true;
            yield break;
        }
        var nav = place.Nav();
        var reach = nav.Reachable(walk.Here(place, nav));
        // The hunter nearest the way in that has tells, on a level with a reachable floor near it.
        Vector3 way = place.ToWorld(new Vector3(place.Inst.Layout.EntranceCell.X, place.Inst.Layout.EntranceCell.Y, place.Inst.Layout.EntranceCell.Z));
        foreach (Wildlife.Creature c in s.Sim.Creatures
                     .Where(c => !c.Dead && !c.Removed && c.Species.Tells.Length > 0 && c.Species.Role is Wildlife.DeepRole.Ambusher or Wildlife.DeepRole.Lurker or Wildlife.DeepRole.Prowler)
                     .OrderBy(c => s.World(c.Position).DistanceTo(way) + Mathf.Abs(c.Position.Y - way.Y) * 3f).Take(4).ToList())
        {
            var cell = new PoiGenerator.Gen.Int3(Mathf.FloorToInt(c.Position.X), Mathf.FloorToInt(c.Position.Y), Mathf.FloorToInt(c.Position.Z));
            // Close enough that it can tell you are there: it won't give itself away for nothing.
            float keep = MathF.Max(2f, c.Species.StrikeRadius * 0.7f);
            PoiGenerator.Gen.Int3? near = null;
            float best = float.MaxValue;
            bool Clear(PoiGenerator.Gen.Int3 q)
            {
                var from = new Vector3(q.X + 0.5f, q.Y + 1f, q.Z + 0.5f);
                Vector3 step = (c.Position + Vector3.Up * 0.5f - from).Normalized() * 0.5f;
                for (float t = 0f, len = from.DistanceTo(c.Position); t < len - 1f; t += 0.5f, from += step)
                    if (PlaceFauna.Solid(place.Inst, Mathf.FloorToInt(from.X), Mathf.FloorToInt(from.Y), Mathf.FloorToInt(from.Z))) return false;
                return true;
            }
            foreach (var q in reach)
            {
                float d = new Vector2(q.X - cell.X, q.Z - cell.Z).Length() + Mathf.Abs(q.Y - cell.Y) * 3f;
                if (d >= keep && d <= keep + 2f && d < best && Clear(q)) { best = d; near = q; }
            }
            if (near == null) continue;
            _hunter = c;
            GD.Print($"  (going to meet a {c.Species.CommonName} ({c.Species.Role}, {c.Species.Stats.MaxHealth:F0} health) near {cell}, from {near})");
            bool ok = false;
            foreach (int x in walk.To(place, near.Value, r => ok = r)) yield return x;
            game.Zone.Wildlife.Frozen = false;
            _awaitTell = true;
            // A lurker can sit tight behind a wall and never know you are there: then on to the next one.
            for (int t = 0; t < 3600 && (_lastDown == null || _aimedTells == 0) && !game.Suit.Dead && (t < 1500 || _aimedTells > 0); t++) yield return 0;
            if (_aimedTells > 0 || game.Suit.Dead) yield break;
            game.Zone.Wildlife.Frozen = true;
            reach = nav.Reachable(walk.Here(place, nav));
        }
        // Everyone in reach sat tight, or nobody was in reach.
        _hunter = null;
        if (s.Sim.Creatures.FirstOrDefault()?.Space is { } rooms)
        {
            // A place holds few hunters now, and none may be in reach: one comes down the hall, staged.
            var here = walk.Here(place, nav);
            var cells = reach.Where(q => q.Y == here.Y && new Vector2(q.X - here.X, q.Z - here.Z).Length() is > 5f and < 10f).ToList();
            if (cells.Count > 0)
            {
                var cell = cells[0];
                Wildlife.Species prowler = game.Places.Fauna.Fauna.For(Wildlife.DeepRole.Prowler, Wildlife.PoiTheme.Industrial, 0);
                prowler = game.Places.Fauna.Fauna.Fitted(prowler, 1.5f, 3f) ?? prowler;
                var near = new Vector3(cell.X + 0.5f, cell.Y + 0.3f, cell.Z + 0.5f);
                _hunter = s.Sim.SpawnIn(prowler, rooms, near);
                Vector3 toward = new Vector3(here.X + 0.5f, cell.Y, here.Z + 0.5f) - _hunter.Position;
                _hunter.Facing = Wildlife.SpaceMath.Frame(toward, Vector3.Up);
                GD.Print($"  (SYSTEMS: a {prowler.CommonName} staged at {cell}, no hunter in reach of here)");
            }
        }
        if (_hunter == null)
        {
            _nothingHere = true;
            yield break;
        }
        game.Zone.Wildlife.Frozen = false;
        _awaitTell = true;
        for (int t = 0; t < 3600 && (_lastDown == null || _aimedTells == 0) && !game.Suit.Dead; t++) yield return 0;
    }


    float _nearestTell = float.MaxValue;
    readonly HashSet<DeepSite> _watchedSites = new();

    /// <summary>Counts the tells shown near the survivor and the bites they take, in every creature site that stands.</summary>
    void WatchTells(Game.Game game)
    {
        foreach (DeepSite s in game.Zone.Wildlife.Sites)
        {
            if (!_watchedSites.Add(s)) continue;
            s.Told += e =>
            {
                if (e.Target != s.Player) return;
                _aimedTells++;
                _nearestTell = MathF.Min(_nearestTell, s.World(e.Position).DistanceTo(game.Survivor.WorldFeet + Vector3.Up));
                if (_firstTell < 0) _firstTell = game.Stats.Seconds;
                GD.Print($"  (tell: {e.Kind} from a {e.Creature?.Species.CommonName} ({e.Creature?.Species.Role}), {s.World(e.Position).DistanceTo(game.Survivor.WorldFeet):F1} m off)");
            };
        }
    }

    /// <summary>A standable cell a step or two from a door, on the side reachable from here without passing it.</summary>
    /// <summary>What the crosshair lands on, for a door the aim didn't find.</summary>
    static string DoorSight(Game.Game game, Place p)
    {
        if (!WorldRay.Cast(game.Zone, game.Survivor, Game.Game.UseReach, GalHack.Physics.Layers.World | GalHack.Physics.Layers.Hull | GalHack.Physics.Layers.Props, out LookHit look)) return "nothing in reach";
        return $"{(look.Collider as Node)?.GetParent()?.Name}/{(look.Collider as Node)?.Name} on layer {(look.Collider as CollisionObject3D)?.CollisionLayer} at {look.Distance:F1} m, cell {p.FromWorld(look.Point)}, door {p.DoorOf(look.Collider)?.Index}, piece {p.PieceOf(look.Collider as GodotObject)?.Piece.Id}";
    }

    static PoiGenerator.Gen.Int3 DoorSide(Place p, PoiGenerator.Gen.NavGrid nav, Place.Door d, PoiGenerator.Gen.Int3 from)
    {
        int y = d.Cells.Min(c => c.Y);
        var cells = d.Cells.Where(c => c.Y == y).ToList();
        Vector3 n = d.Along.Cross(Vector3.Up).Normalized();
        var step = new PoiGenerator.Gen.Int3((int)MathF.Round(n.X), 0, (int)MathF.Round(n.Z));
        PoiGenerator.Gen.Int3 best = cells[0];
        int bestLen = int.MaxValue;
        var seen = new List<string>();
        foreach (var c in cells)
            foreach (int k in new[] { 1, -1, 2, -2 })
            {
                var q = c + step * k;
                if (!nav.Standable(q)) { seen.Add($"{q} not standable"); continue; }
                var path = nav.FindPath(from, q, 400_000);
                seen.Add($"{q} {(path == null ? "no path" : path.Any(r => d.Cells.Contains(r)) ? "through it" : $"{path.Count}")}");
                if (path == null || path.Any(r => d.Cells.Contains(r))) continue;
                if (path.Count < bestLen) { bestLen = path.Count; best = q; }
            }
        if (bestLen == int.MaxValue) GD.Print($"  (no side of door {d.Index} ({string.Join(" ", d.Cells)}) from {from}: {string.Join("; ", seen)})");
        return best;
    }

    /// <summary>
    /// Creatures are as solid as they look (their rigs' own bodies), shots land on them, vessels
    /// meet them, and it all stays in budget. Creatures are set down by hand here (printed).
    /// </summary>
    IEnumerable<int> Bodies(WorldChoice world)
    {
        var game = new Game.Game { Interactive = false, World = world, SkipIntro = true };
        AddChild(game);
        var drive = new Driver(game);
        var play = new Playthrough(game);
        foreach (int x in drive.UntilPlaying()) yield return x;
        game.Zone.Weather.Bridge.Calm = true;
        game.Zone.Wildlife.Frozen = true;
        foreach (int x in play.MakeTool()) yield return x; // you wake empty-handed, and a flyer gets shot at
        foreach (int x in drive.ClimbOut(game.Bunker)) yield return x;
        GD.Print("-- creature bodies (SYSTEMS: creatures set down by hand, held still) --");
        Slice.Surface.WildlifePatch w = game.Zone.Wildlife;
        Vector3 feet = game.Survivor.WorldFeet;
        Vector3 toward = (game.Bunker.Outside - game.Bunker.StairTop) with { Y = 0f };
        toward = toward.Normalized();
        // Everything else out of the way.
        foreach (Wildlife.Creature c in w.Sim.Creatures) w.Sim.Remove(c);
        Vector3 Spot(float ahead, float side = 0f) => w.PatchLocal(game.Zone.GroundAt(feet.X + toward.X * ahead - toward.Z * side, feet.Z + toward.Z * ahead + toward.X * side));
        var fauna = new Wildlife.FaunaGenerator(game.Zone.Data.Wildlife.Fauna).Generate(game.Zone.Planet.Spec.Seed, 0.25f, bloom: false);

        // A big one, the torso at head height: you can't walk through it.
        Wildlife.Species big = fauna.Species.Where(s => !s.Flies && s.Rig == Wildlife.RigKind.Legged).OrderByDescending(s => s.Body.Size).First();
        Wildlife.Creature giant = w.Sim.Spawn(big, Spot(8f));
        foreach (int x in drive.Wait(30)) yield return x;
        float chest = giant.Rig.Hits[0].Y + w.Origin.Y - game.Zone.GroundAt(w.FromPatch(giant.Position).X, w.FromPatch(giant.Position).Z).Y;
        Check(game.Bodies.InUse >= 3, $"a {big.CommonName} ({big.Body.Size:F1} m long) stands in {game.Bodies.InUse} capsules from its own body, torso {chest:F1} m up");
        // Rays at its body's height, across it, meet it where you see it.
        Vector3 torso = w.FromPatch(new Vector3(giant.Rig.Hits[0].X, giant.Rig.Hits[0].Y, giant.Rig.Hits[0].Z));
        var q = PhysicsRayQueryParameters3D.Create(torso - toward * 4f - Vector3.Right * 0.01f, torso + toward * 4f, Layers.Props);
        Check(game.Zone.GetWorld3D().DirectSpaceState.IntersectRay(q).Count > 0, $"solid across its torso at {chest:F1} m (a giant is solid where you see it, not just to 1.6 m)");
        // Walking at it: the feet stop short of its middle (a body at head height doesn't let you by).
        Vector3 start = game.Survivor.WorldFeet;
        float closest = float.MaxValue, torsoR = giant.Rig.Hits[0].W; // how near its torso's middle the feet ever came: round a body is no way through it
        foreach (int x in drive.WalkTo(w.FromPatch(giant.Position) + toward * 3f, 0.5f, 6f, sprint: false, dig: false, hop: false))
        {
            Vector3 middle = w.FromPatch(new Vector3(giant.Rig.Hits[0].X, giant.Rig.Hits[0].Y, giant.Rig.Hits[0].Z)) - game.Survivor.WorldFeet;
            closest = MathF.Min(closest, new Vector2(middle.X, middle.Z).Length());
            yield return x;
        }
        float got = (game.Survivor.WorldFeet - w.FromPatch(giant.Position)).Dot(toward);
        bool under = chest - giant.Rig.Hits[0].W > Slice.Player.Survivor.Height;
        Check(under || got < 0.5f || closest >= torsoR * 0.9f, under ? $"(its belly clears a person: {chest - giant.Rig.Hits[0].W:F1} m, you can walk under it)" : $"walking at it stops you {-got:F1} m short of its middle, or takes you round it: never through its torso ({closest:F1} m from the middle, {torsoR:F1} m wide)");
        w.Sim.Remove(giant);

        // A flyer: shapes too, and a shot lands.
        Wildlife.Species wing = fauna.Species.FirstOrDefault(s => s.Flies);
        if (wing != null)
        {
            Wildlife.Creature flyer = w.Sim.Spawn(wing, Spot(7f));
            flyer.Position += Vector3.Up * 2f;
            foreach (int x in drive.Wait(20)) yield return x;
            float hp = flyer.Health;
            foreach (int x in play.ShootAt(() => w.FromPatch(flyer.Rig.BoundsCenter), 3)) yield return x;
            Check(flyer.Health < hp || flyer.Dead, $"a {wing.CommonName} in the air: a shot lands ({hp:F0} to {flyer.Health:F0} health)");
            Check(game.Zone.Blood.Bursts > 0, $"hits show: {game.Zone.Blood.Bursts} spurts of its blood where it landed, {game.Zone.Blood.Splats} splats on the ground under it");
            w.Sim.Remove(flyer);
            foreach (int x in play.Tool(Slice.Player.ToolMode.Gather)) yield return x;
        }

        // A head takes a hit harder than a body (a hit location that counts).
        Wildlife.Creature target = w.Sim.Spawn(big, Spot(9f));
        foreach (int x in drive.Wait(5)) yield return x;
        float headR = big.Body.HeadSize * 0.6f;
        Vector4? head = null;
        for (int i = 0; i < target.Rig.HitCount; i++) if (MathF.Abs(target.Rig.Hits[i].W - headR) < 1e-3f) head = target.Rig.Hits[i];
        if (head is Vector4 h4) Check(w.HitScale(target, w.FromPatch(new Vector3(h4.X, h4.Y, h4.Z))) > 1f && w.HitScale(target, w.FromPatch(new Vector3(target.Rig.Hits[0].X, target.Rig.Hits[0].Y, target.Rig.Hits[0].Z))) == 1f, "a hit on the head counts half as much again as one on the body");
        w.Sim.Remove(target);

        // A vessel hits a small creature: knocked aside, and the vessel takes the formula's share.
        Wildlife.Species small = fauna.Species.Where(s => !s.Flies && s.Rig != Wildlife.RigKind.Swarm).OrderBy(s => s.Body.Size).First();
        Wildlife.Creature critter = w.Sim.Spawn(small, Spot(14f, 6f));
        foreach (int x in drive.Wait(5)) yield return x;
        Vector3 was = w.FromPatch(critter.Position);
        Slice.Vessels.Vessel skiff = Slice.Vessels.Vessel.Spawn(game.Zone, game.Zone.Data.Blueprint(game.Zone.Data.Rules.Build.StarterBlueprint), (was - toward * 9f + Vector3.Up * 0.8f).ToNumerics(), 0f, landed: false);
        for (int t = 0; t < 90 && game.Bodies.VesselHits == 0; t++)
        {
            // At the creature itself: on a slope the ground lifts a vessel held to a level course clean over it.
            skiff.Body.Velocity = ((w.FromPatch(critter.Position) - skiff.Body.Position.ToGodot()).Normalized() * 18f).ToNumerics();
            yield return 0;
        }
        foreach (int x in drive.Wait(5)) yield return x;
        Check(game.Bodies.VesselHits > 0 && (critter.Dead || w.FromPatch(critter.Position).DistanceTo(was) > 0.5f), $"a vessel hits a {small.CommonName}: knocked aside {w.FromPatch(critter.Position).DistanceTo(was):F1} m{(critter.Dead ? ", dead" : "")}, the vessel takes {skiff.Body.Damage:F2}");
        Check(game.Bodies.WorstMs < 3.0, $"creature bodies stay in budget: worst tick {game.Bodies.WorstMs:F2} ms, {game.Bodies.MostParts} capsules at most");

        // Day and night, and the tells' view, on the surface patch.
        Check(w.Sim.ActivityCycles && w.Tells != null, $"the surface's creatures keep day and night (their rest sites: {w.Sim.RestSites.Count}), and its tells are drawn");

        // A wreck the POI agent's flavour layer registers: finite, taken a share at a time.
        var hull = new StaticBody3D { Name = "Hull", CollisionLayer = Layers.World, CollisionMask = 0 };
        hull.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(2f, 2f, 2f) } });
        hull.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(2f, 2f, 2f) } });
        game.Zone.AddChild(hull);
        Vector3 hullAt = game.Survivor.WorldEye() + game.Survivor.WorldLook() with { Y = 0f } * 3.5f;
        hull.GlobalPosition = hullAt;
        bool emptied = false;
        var wreck = new SalvageSite { Name = "Hauler", Body = hull, Left = new GalHack.Items.Bill().Add("iron-bar", 12).Add("copper-wire", 6), Shares = 3, HitPoints = 120f, Emptied = _ => emptied = true };
        game.Places.AddSalvage(wreck);
        foreach (int x in drive.Wait(5)) yield return x;
        int had = game.Pack.Storage.Contents.Total;
        foreach (int x in play.Tool(Slice.Player.ToolMode.Gather)) yield return x;
        foreach (int x in drive.HoldPrimary(hullAt, 300, () => emptied)) yield return x;
        Check(emptied && game.Pack["iron-bar"] >= 12 && game.Pack["copper-wire"] >= 6, $"the salvage hook: a registered wreck gives up its bill a share at a time, and is empty after ({game.Pack.Storage.Contents.Total - had} items)");

        // A cave: the planet's own, with what lives in it (the survivor set down in it, printed).
        Vector3? chamber = null;
        Vector3 c0 = game.Survivor.WorldFeet;
        var source = game.Zone.Planet.Source;
        float searched = 20f;
        for (float r = searched; r < 900f && chamber == null; searched = r += 12f)
            for (int a = 0; a < 24 && chamber == null; a++)
            {
                float px = c0.X + MathF.Cos(a * MathF.Tau / 24f) * r, pz = c0.Z + MathF.Sin(a * MathF.Tau / 24f) * r;
                float h = game.Zone.GroundAt(px, pz).Y;
                for (float d = 6f; d < 34f && chamber == null; d += 1f)
                {
                    int x = (int)MathF.Floor((float)Planets.Gen.Mathx.WrapX(px, game.Zone.Planet.Width)), y = (int)MathF.Floor(h - d), z = (int)MathF.Floor((float)Planets.Gen.Mathx.Wrap(pz, game.Zone.Planet.Height));
                    if (source.CellDensity(x, y, z) < 0f && source.CellDensity(x, y + 1, z) < 0f && source.CellDensity(x, y + 2, z) < 0f && source.CellDensity(x, y + 3, z) < 0f && source.CellDensity(x, y - 1, z) > 0f)
                        chamber = new Vector3(px, y, pz);
                }
            }
        Check(chamber != null, $"a cave within 900 m of the start: {chamber}");
        if (chamber is Vector3 cave)
        {
            GD.Print("  (SYSTEMS: the castaway set down in the cave)");
            game.Survivor.PlaceOutside((cave + Vector3.Up * 0.4f).ToNumerics());
            for (int i = 0; i < 600 && game.Caves.Found == 0; i++) yield return 0;
            // A chamber under a place or too shallow to count as underground: the next one out, as a player would try.
            for (float r = searched + 60f; r < 900f && game.Caves.Found == 0; r += 60f)
            {
                Vector3 spot = game.Survivor.WorldFeet;
                GD.Print($"  (no chamber round {spot}, ground {game.Zone.GroundAt(spot.X, spot.Z).Y:F1}, in a place {game.Places.PlaceAt(spot) != null}: on to the next)");
                Vector3? next = null;
                for (int a = 0; a < 24 && next == null; a++)
                {
                    float px = c0.X + MathF.Cos(a * MathF.Tau / 24f) * r, pz = c0.Z + MathF.Sin(a * MathF.Tau / 24f) * r;
                    float h = game.Zone.GroundAt(px, pz).Y;
                    for (float d = 6f; d < 34f && next == null; d += 1f)
                    {
                        int x = (int)MathF.Floor((float)Planets.Gen.Mathx.WrapX(px, game.Zone.Planet.Width)), y = (int)MathF.Floor(h - d), z = (int)MathF.Floor((float)Planets.Gen.Mathx.Wrap(pz, game.Zone.Planet.Height));
                        if (source.CellDensity(x, y, z) < 0f && source.CellDensity(x, y + 1, z) < 0f && source.CellDensity(x, y + 2, z) < 0f && source.CellDensity(x, y + 3, z) < 0f && source.CellDensity(x, y - 1, z) > 0f)
                            next = new Vector3(px, y, pz);
                    }
                }
                if (next == null) continue;
                game.Survivor.PlaceOutside((next.Value + Vector3.Up * 0.4f).ToNumerics());
                for (int i = 0; i < 600 && game.Caves.Found == 0; i++) yield return 0;
            }
            Check(game.Caves.Found > 0 && game.Zone.Wildlife.Sites.Any(s => s.Sim.Creatures.Count > 0), $"a chamber is found round you underground and something lives in it: {game.Zone.Wildlife.Sites.Sum(s => s.Sim.Creatures.Count)} creatures; the search took {game.Caves.WorstMs:F1} ms at worst, on a worker");
        }
        game.QueueFree();
        yield return 0;
    }

    /// <summary>
    /// A world with a settlement on it, however far from the start: few start worlds have one, so
    /// the worlds rolled on from this one's seed are built bare until one does, and the game goes
    /// there (a failing run replays by the same --seed).
    /// </summary>
    IEnumerable<int> Settlement(WorldChoice first)
    {
        const int Worlds = 40;
        PlanetSurface.Poi.PoiCatalog catalog = SurfaceZone.PoiCatalog;
        WorldChoice found = null;
        PlanetSurface.Poi.PoiSiting.StartWorld = true;
        for (int i = 0; i < Worlds && found == null; i++)
        {
            WorldChoice world = first with { Seed = first.Seed + (ulong)i * 0x9E3779B97F4A7C15UL };
            var building = Task.Run(() => PlanetSurface.World.PlanetWorld.Build(world.Spec(), catalog));
            while (!building.IsCompleted) yield return 0;
            if (building.Result.Pois.Any(LivingTown)) found = world;
            else GD.Print($"  (no living settlement on {world}: on to the next)");
        }
        if (found == null) { Fail($"no living settlement on any of {Worlds} worlds from {first}"); yield break; }
        var game = new Game.Game { Interactive = false, World = found, SkipIntro = true };
        AddChild(game);
        var drive = new Driver(game);
        foreach (int x in drive.UntilPlaying()) yield return x;
        foreach (int x in Settlement(game, drive, found)) yield return x;
    }

    static bool LivingTown(PlanetSurface.Poi.PoiSite s) => s.Instance?.Townsfolk != null && s.Instance.State == PoiState.Active && s.Instance.Mood != Mood.Hostile; // not a camp in a ruin: Neutrals has those

    /// <summary>
    /// Scavengers camped in a large place: they watch you and leave you be, nothing that hunts lives
    /// where they do, and wildlife keeps off their ground. Only a shot starts it.
    /// </summary>
    IEnumerable<int> Neutrals(WorldChoice world)
    {
        PlanetSurface.Poi.PoiSiting.ForceFamily = "sewage_works";
        PlanetSurface.Poi.PoiSiting.ForceState = PoiState.Abandoned;
        PlanetSurface.Poi.PoiSiting.ForceOccupancy = new OccupancyRoll(Occupant.Scavengers);
        var game = new Game.Game { Interactive = false, World = world, SkipIntro = true };
        AddChild(game);
        var drive = new Driver(game);
        foreach (int x in drive.UntilPlaying()) yield return x;
        PlanetSurface.Poi.PoiSiting.ForceFamily = null;
        PlanetSurface.Poi.PoiSiting.ForceState = null;
        PlanetSurface.Poi.PoiSiting.ForceOccupancy = null;
        GD.Print($"-- scavengers in a large place, {world} --");
        game.Zone.Weather.Bridge.Calm = true;
        var site = game.Zone.Planet.Pois.FirstOrDefault(s => s.Family.Id == "sewage_works" && s.Instance.Occupancy?.Main == Occupant.Scavengers);
        if (site == null) { GD.Print("  (no works could be seated on this world: on to the next test)"); game.QueueFree(); yield return 0; yield break; }
        Vector3 Local(PoiGenerator.Gen.Int3 c) => game.Zone.Planet.NearestCopy(new Vector3(site.Origin.X + c.X + 0.5f, site.Origin.Y + c.Y, site.Origin.Z + c.Z + 0.5f), game.Survivor.WorldFeet);
        ActivitySpot camp = site.Instance.Townsfolk?.Spots.FirstOrDefault();
        Check(camp != null && site.Instance.Mood == Mood.Wary && site.Instance.Enemies.Count == 0, $"they are in the place as people, not enemies: {site.Instance.Townsfolk?.People.Count} people, {site.Instance.Mood}, {site.Instance.Enemies.Count} enemies");
        if (camp == null) yield break;
        game.Survivor.PlaceOutside(Local(camp.Cell).ToNumerics() + System.Numerics.Vector3.UnitY * 0.2f);
        for (int i = 0; i < 1800 && game.Places.PlaceOf(site) is not { Ready: true }; i++) yield return 0;
        Place place = game.Places.PlaceOf(site);
        Check(place is { Ready: true } && place.Memory.Town != null, $"it stands, and its people are about ({place?.Memory.Town?.People.Count})");
        if (place?.Memory.Town == null) yield break;
        Town town = place.Memory.Town;
        double health = game.Suit.HealthShare;
        // Among them for a while: a look, and nothing more.
        foreach (int x in drive.Wait(600)) yield return x;
        Check(town.GuardShots == 0 && town.GuardAims == 0 && game.Suit.HealthShare >= health && place.Memory.Mood == Mood.Wary, $"they watch and leave you be: {town.GuardShots} shots in 10 s among them, health {game.Suit.HealthShare * 100:F0}%, still {place.Memory.Mood}");
        var pop = place.Memory.Fauna?.Population;
        int hunters = pop == null || pop.Silent ? 0 : pop.Spawns.Count(s => s.Role != Wildlife.DeepRole.Subsister);
        Check(hunters == 0, $"nothing that hunts lives where they do ({pop?.Note ?? "nothing"})");
        var sim = game.Zone.Wildlife.Sim;
        Check(sim != null && sim.Settlements.Any(q => q.Name == site.Name), "and wildlife knows their ground as it knows a town's");
        town.Provoked();
        foreach (int x in drive.Wait(10)) yield return x;
        Check(place.Memory.Mood == Mood.Hostile, "a shot starts it: they turn on you");
        game.QueueFree();
        yield return 0;
    }

    /// <summary>
    /// A settlement on foot: over the land to it, through it among its people going about their
    /// day, at peace; night sends them indoors and morning brings them out; a shot turns the place
    /// hostile, its people run for cover and its guards take aim where you can see it before they fire.
    /// </summary>
    IEnumerable<int> Settlement(Game.Game game, Driver drive, WorldChoice world)
    {
        var play = new Playthrough(game);
        game.Zone.Weather.Bridge.Calm = true;
        GD.Print($"-- a settlement, {world} --");
        foreach (int x in play.MakeTool()) yield return x; // you wake empty-handed, and the shot in the town wants it
        foreach (int x in drive.ClimbOut(game.Bunker)) yield return x;
        Vector3 from = game.Survivor.WorldFeet;
        var site = game.Zone.Planet.Pois.Where(LivingTown).OrderBy(s => game.Places.DistanceTo(s, from)).First();
        GD.Print($"  a settlement on this world: {site.Name}, {site.Instance.Mood}, {site.Instance.Townsfolk.People.Count} people, {game.Places.DistanceTo(site, from):F0} m off");
        var walk = new PlaceWalk(game, drive);
        Vector3 gate = game.Zone.Planet.NearestCopy(new Vector3(site.Entrance.X + 0.5f, site.Entrance.Y, site.Entrance.Z + 0.5f), from);
        // The long walk is the place visit's to prove (this one is over a river, 2 km): the town is what's under test.
        Int3 facing = site.Instance.Layout.EntranceFacing.Vec();
        Vector3 startAt = gate + new Vector3(facing.X, 0f, facing.Z) * 55f;
        startAt.Y = game.Zone.GroundAt(startAt.X, startAt.Z).Y + 0.3f;
        GD.Print($"  (TEST: the castaway is set down 55 m from its way in, not walked the 2 km)");
        game.Survivor.PlaceOutside(startAt.ToNumerics());
        foreach (int x in drive.Wait(30)) yield return x;
        foreach (int x in drive.UntilSettled()) yield return x;
        double hurtBefore = game.Suit.HealthShare;
        gate = game.Zone.Planet.NearestCopy(new Vector3(site.Entrance.X + 0.5f, site.Entrance.Y, site.Entrance.Z + 0.5f), game.Survivor.WorldFeet); // set down over the planet's seam, the castaway is on another copy of it
        foreach (int x in drive.WalkTo(gate, 1f, 40f, sprint: false, dig: false, hop: false)) yield return x;
        // Those 55 m can be a gully or a river: the walk over land is proved above, and the town is what's under test.
        if (new Vector2(game.Survivor.WorldFeet.X - gate.X, game.Survivor.WorldFeet.Z - gate.Z).Length() > 8f)
        {
            GD.Print($"  (TEST: the last 55 m didn't reach its way in, stopped at {game.Survivor.WorldFeet}: set down at the gate)");
            Vector3 atGate = gate + new Vector3(facing.X, 0f, facing.Z) * 3f;
            atGate.Y = game.Zone.GroundAt(atGate.X, atGate.Z).Y + 0.3f;
            game.Survivor.PlaceOutside(atGate.ToNumerics());
            foreach (int x in drive.Wait(30)) yield return x;
            foreach (int x in drive.UntilSettled()) yield return x;
        }
        for (int i = 0; i < 300 && game.Places.PlaceOf(site) is not { Ready: true }; i++) yield return 0;
        Place place = game.Places.PlaceOf(site);
        Check(place is { Ready: true } && place.Memory.Town != null, $"it stands as a structure, its people about ({place?.Memory.Town?.People.Count})");
        if (place?.Memory.Town == null) yield break;
        Town town = place.Memory.Town;
        Vector3 Where(Town.Person q) => q.Pos;
        var before = town.People.ToDictionary(q => q, Where);
        foreach (int x in drive.Wait(600)) yield return x;
        int moved = town.People.Count(q => q.Pos.DistanceTo(before[q]) > 1f);
        int stayed = town.People.Count(q => q.Role is "vendor" or "worker" && q.Pos.DistanceTo(before[q]) < 1f);
        Check(moved >= 2 && town.People.Any(q => q.Role is "rusher" or "wanderer" && q.Pos.DistanceTo(before[q]) > 2f), $"in 10 s {moved} of {town.People.Count} have gone somewhere; {stayed} vendors and workers keep to their places ({string.Join(", ", town.People.GroupBy(q => q.Role).Select(g => $"{g.Count()} {g.Key}"))})");
        // Through the town at a walk: nobody minds.
        var nav = place.Nav();
        var far = town.Data.Spots.OrderByDescending(q => q.Cell.Manhattan(place.Inst.Layout.EntranceCell)).First();
        bool ok = false;
        foreach (int x in walk.To(place, far.Cell, r => ok = r)) yield return x;
        foreach (int x in walk.To(place, place.Inst.Layout.EntranceCell, r => ok = r)) yield return x;
        Check(ok && place.Memory.Mood == place.Inst.Mood && town.GuardShots == 0 && game.Suit.HealthShare >= hurtBefore - 0.3, $"walked through it and back in peace ({(ok ? "" : $"the walk stopped: {string.Join("; ", walk.Notes.TakeLast(2))}; ")}health {game.Suit.HealthShare * 100:F0}%): still {place.Memory.Mood}, no guard raised a gun");
        // Night sends them in (the clock wound on, printed), morning brings them out.
        GD.Print("  (TEST: the clock wound on to night and back)");
        double morning = game.Zone.Sky.TimeOfDay;
        game.Zone.Sky.TimeOfDay = 0.93;
        foreach (int x in drive.Wait(1500)) yield return x;
        int inside = town.People.Count(q => q.Indoors), civilians = town.People.Count(q => q.Role != "guard");
        Check(inside >= civilians / 2 && town.People.Where(q => q.Role == "guard").All(q => !q.Indoors), $"at night {inside} of {civilians} have gone indoors; the guards hold their posts");
        game.Zone.Sky.TimeOfDay = morning;
        foreach (int x in drive.Wait(240)) yield return x;
        Check(town.People.Count(q => q.Indoors) < inside, $"by day they come out again ({town.People.Count(q => q.Indoors)} still in)");
        // The wild keeps out; strays are culled.
        Check(game.Zone.Wildlife.Sim.Settlements.Count > 0, $"wildlife keeps out of the town: {game.Zone.Wildlife.Sim.Settlements.Count} settlement in the patch's sim; {town.Culled} strays culled");
        // A shot in the town.
        int aims = town.GuardAims;
        foreach (int x in play.ShootAt(() => game.Survivor.WorldEye() + game.Survivor.WorldLook() * 8f + Vector3.Down * 1f)) yield return x;
        Check(place.Memory.Mood == Mood.Hostile, "a shot in a peaceful place turns it hostile");
        foreach (int x in drive.Wait(300)) yield return x;
        Check(town.People.Where(q => q.Role != "guard").All(q => q.Indoors || q.Fleeing || q.Dead), $"its people run for cover ({town.People.Count(q => q.Fleeing)} fleeing, {town.People.Count(q => q.Indoors)} indoors)");
        Check(town.GuardShots == 0 || town.GuardAims > aims, $"its guards take aim where you can see it before they fire ({town.GuardAims - aims} aims, {town.GuardShots} shots)");
        game.QueueFree();
        yield return 0;
    }

    /// <summary>A door block on a base (set down whole for the test, printed): it opens as you come up, lets you through, shuts behind you; E works it too.</summary>
    IEnumerable<int> BuiltDoors(WorldChoice world)
    {
        var game = new Game.Game { Interactive = false, World = world, SkipIntro = true };
        AddChild(game);
        var drive = new Driver(game);
        foreach (int x in drive.UntilPlaying()) yield return x;
        game.Zone.Weather.Bridge.Calm = true;
        game.Zone.Wildlife.Frozen = true;
        foreach (int x in drive.ClimbOut(game.Bunker)) yield return x;
        GD.Print("-- a door on a base (SYSTEMS: its wall is set down whole, not built by hand) --");
        // On level ground along the way through: where it rises past the gate, your head meets the lintel in the doorway.
        Vector3 ahead = game.Bunker.Ahead(14f), start = game.Survivor.WorldFeet with { Y = 0f };
        Vector3 forward = (ahead with { Y = 0f } - start).Normalized(), side = forward.Cross(Vector3.Up);
        float gateHeading = game.Bunker.Heading, rise = float.MaxValue;
        Vector3 through = new Vector3(-MathF.Sin(gateHeading), 0f, -MathF.Cos(gateHeading));
        foreach (float d in new[] { 10f, 14f, 18f, 22f })
            for (float o = -12f; o <= 12f; o += 4f)
            {
                Vector3 p = start + forward * d + side * o;
                float low = float.MaxValue, high = float.MinValue;
                for (float k = -7f; k <= 8f; k += 1f)
                {
                    float g = game.Zone.GroundAt(p.X + through.X * k, p.Z + through.Z * k).Y;
                    low = MathF.Min(low, g);
                    high = MathF.Max(high, g);
                }
                if (high - low < rise) { rise = high - low; ahead = p; }
            }
        GD.Print($"  (the gate stands where the ground rises {rise:F2} m along the way through)");
        ahead.Y = game.Zone.GroundAt(ahead.X, ahead.Z).Y + 1f;
        string plan = "schema galhack.blueprint\nversion 1\nname \"Gate\"\ngrid large\nmode base\ngravity off\nblock -3..-1 0 0 steel\nblock 1..3 0 0 steel\nblock -3..3 1 0 steel\nblock 0 0 0 door\nblock 4 0 0 core\n";
        var s = GalHack.Structures.Blueprint.Load(plan, game.Zone.Data.Catalogue, out List<string> errors);
        Check(s != null && errors.Count == 0, $"the gate's blueprint loads {string.Join("; ", errors)}");
        if (s == null) yield break;
        s.DirtyChunks.Clear();
        float heading = game.Bunker.Heading;
        var gate = Slice.Vessels.Vessel.SpawnBase(game.Zone, s, ahead.ToNumerics(), heading);
        foreach (int x in drive.Wait(20)) yield return x;
        Vector3 door = gate.ToWorld(PhysicsSandbox.Bridges.StructureBridge.CellCenter(s, new GalHack.Structures.Int3(0, 0, 0)));
        var cell = new GalHack.Structures.Int3(0, 0, 0);
        Check(!Slice.Vessels.BlockDoors.IsOpen(gate, cell), "the built door stands shut with nobody near");
        Vector3 along = (gate.ToWorld(PhysicsSandbox.Bridges.StructureBridge.CellCenter(s, new GalHack.Structures.Int3(0, 0, 1))) - door) with { Y = 0f };
        along = along.Normalized();
        if (along.Dot(door - game.Survivor.WorldFeet) < 0f) along = -along;
        Vector3 before = door - along * 6f, past = door + along * 7f;
        foreach (int x in drive.WalkTo(before, 1f, 20f, sprint: false, dig: false, hop: false)) yield return x;
        foreach (int x in drive.WalkTo(door + along * 1.8f, 0.6f, 12f, sprint: false, dig: false, hop: false)) yield return x;
        Vector3 f = game.Survivor.WorldFeet;
        Check((f - door).Dot(along) > 0.5f && game.Doors.Opened >= 1, $"it opens as you walk up (your own structure), and you walk through ({(f - door).Dot(along):F1} m past it)");
        Check(game.Doors.OpenDoors(game.Places).Any(), "an open door is there to read (a force field's hook)");
        foreach (int x in drive.WalkTo(past, 1f, 12f, sprint: false, dig: false, hop: false)) yield return x;
        foreach (int x in drive.Wait(90)) yield return x;
        Vector3 walked = game.Survivor.WorldFeet;
        Check(!Slice.Vessels.BlockDoors.IsOpen(gate, cell), $"and it shuts behind you, solid again ({(walked + Vector3.Up * 0.9f).DistanceTo(door):F1} m off it, walking to {past}, feet {walked}, {game.Doors.Opened} opened, {game.Doors.Shut} shut)");
        var ray = PhysicsRayQueryParameters3D.Create(door - along * 1.5f, door + along * 1.5f, Layers.Hull);
        Check(game.Zone.GetWorld3D().DirectSpaceState.IntersectRay(ray).Count > 0, "the shut leaf collides");
        // E works it too, from reach: walk up, it opens; E shuts it; it stays shut while you stand there.
        foreach (int x in drive.WalkTo(door + along * 2.2f, 0.5f, 12f, sprint: false, dig: false, hop: false)) yield return x;
        foreach (int x in drive.LookAt(door, 8)) yield return x;
        string prompt = game.UsePrompt();
        foreach (int x in drive.Tap(Key.E)) yield return x;
        foreach (int x in drive.Wait(60)) yield return x;
        Check(prompt == "E  Shut the door" && !Slice.Vessels.BlockDoors.IsOpen(gate, cell), $"'{prompt}': E shuts it, with the same sliding leaves, and it stays shut beside you");
        foreach (int x in drive.Tap(Key.E)) yield return x;
        foreach (int x in drive.Wait(30)) yield return x;
        Check(Slice.Vessels.BlockDoors.IsOpen(gate, cell), "E opens it again");
        game.QueueFree();
        yield return 0;
    }

    /// <summary>
    /// The systems checks, with the focus moved by hand (printed): the place in view stands up
    /// within the frame budget, the far drawing gives way to it, its parts are all there.
    /// </summary>
    IEnumerable<int> Systems(WorldChoice world)
    {
        var game = new Game.Game { Interactive = false, World = world, SkipIntro = true };
        AddChild(game);
        var drive = new Driver(game);
        foreach (int x in drive.UntilPlaying()) yield return x;
        game.Zone.Weather.Bridge.Calm = true;
        PoiPlaces places = game.Places;
        var site = game.Bunker.Poi;
        // Far places are drawn only: what you can touch stands as a structure.
        Node3D far = null;
        for (int i = 0; i < 600 && (far = game.Zone.Pois.NodeOf(site)) == null; i++) yield return 0;
        Check(far != null && far.GetChildren().OfType<CollisionObject3D>().Any() == false, $"the far drawing of {site.Name} is only a drawing");
        GD.Print($"  (SYSTEMS: the focus is moved to the place by hand, not walked; the warm-up while loading took {places.WarmUpMs:F0} ms)");
        Vector3 centre = game.Zone.Planet.NearestCopy(new Vector3(site.Origin.X, site.Origin.Y, site.Origin.Z), game.Survivor.WorldFeet);
        places.Focus = () => centre + Vector3.Back * 250f;
        var watch = Stopwatch.StartNew();
        int frames = 0;
        while (places.PlaceOf(site) is not { Ready: true } && watch.Elapsed.TotalSeconds < 60) { frames++; yield return 0; }
        Place p = places.PlaceOf(site);
        Check(p is { Ready: true }, $"{site.Name} stands up as a structure when the focus comes within {places.BuildRadius} m ({frames} frames, {watch.Elapsed.TotalSeconds:F1} s)");
        if (p == null) yield break;
        var log = places.BuildLog.LastOrDefault(); // none yet if it stood before this count began
        Check(places.WorstFrameMs < places.BudgetMs + 12.0, $"built over frames: {log.Chunks} chunks; worst frame {places.WorstFrameMs:F1} ms of building, worst single step {places.WorstStepMs:F1} ms (by stage {string.Join(", ", places.StageWorst.Select(kv => $"{kv.Key}: {kv.Value:F1}"))}) (budget {places.BudgetMs} ms a frame)");
        for (int i = 0; i < 240; i++) yield return 0; // the creatures and lairs go in over these frames
        double views = game.Zone.Wildlife.Sites.Count > 0 ? game.Zone.Wildlife.Sites.Max(s => s.WorstViewMs) : 0;
        Check(views < 8.0, $"its creatures and lair marks go in a few a frame: worst {views:F1} ms a frame ({WorstViews(game)})");
        GD.Print($"  (longest collector pause by then: {WorstPause(game)})");
        GD.Print($"  slow steps: {string.Join("; ", places.SlowSteps.Select(s => $"{s.Stage} {s.Ms:F1}{(s.Collected ? " (a garbage collection)" : "")}"))}");
        // Up close the place is drawn in full, and the planet's merged cubes give way to it there and nowhere else.
        for (int i = 0; i < 180 && p.NearGroups == 0; i++)
        {
            Vector3 eye = centre + Vector3.Back * 12f;
            places.Eye = () => eye;
            yield return 0;
        }
        places.Eye = null;
        Check(p.NearGroups > 0 && p.NearGroups < p.Groups || p.Groups <= 8, $"the cubes near the eye are drawn in full ({p.NearGroups} of {p.Groups}), the rest stay merged");
        GD.Print($"  doors {p.Doors.Count} ({p.Doors.Count(d => d.Clearance != null)} locked, {p.Doors.Count(d => d.Jammed)} jammed), deco rooms {p.DecoRooms}, caches {p.Memory.Caches.Count}, creatures {p.Memory.Fauna?.Sim?.Creatures.Count}");
        game.QueueFree();
        yield return 0;
    }

    /// <summary>Lists worlds with a settlement near the start (to pin one for the settlement visit).</summary>
    IEnumerable<int> FindTown()
    {
        for (ulong seed = 1; seed <= 60; seed++)
        {
            var game = new Game.Game { Interactive = false, World = WorldChoice.Fresh("temperate", seed), SkipIntro = true };
            AddChild(game);
            var drive = new Driver(game);
            bool failed = false;
            var watch = Stopwatch.StartNew();
            while (game.Phase != Phase.Playing && watch.Elapsed.TotalSeconds < 40)
            {
                if (game.Zone != null && game.Zone.IsBuilt && game.Bunker == null) { failed = true; break; }
                yield return 0;
            }
            if (!failed && game.Phase == Phase.Playing)
            {
                Vector3 start = game.Start;
                foreach (var site in game.Zone.Planet.Pois.Where(s => s.Instance?.Townsfolk != null).OrderBy(s => game.Places.DistanceTo(s, start)).Take(3))
                {
                    float d = game.Places.DistanceTo(site, start);
                    if (d < 2500f) GD.Print($"seed {seed}: {d:F0} m  {site.Name} [{site.Family.Id}] {site.Instance.State}: {site.Instance.Townsfolk.People.Count} people, {site.Instance.Townsfolk.Animals.Count} animals, {site.Instance.Mood}");
                }
            }
            game.QueueFree();
            for (int i = 0; i < 5; i++) yield return 0;
        }
    }

    /// <summary>What each place within 1.5 km of the start holds.</summary>
    IEnumerable<int> Survey(WorldChoice world)
    {
        var game = new Game.Game { Interactive = false, World = world, SkipIntro = true };
        AddChild(game);
        var drive = new Driver(game);
        foreach (int x in drive.UntilPlaying()) yield return x;
        Vector3 start = game.Start;
        GD.Print($"-- {world}: start {start}, the place in view: {game.Bunker.Poi?.Name}");
        var deep = game.Places.Fauna;
        foreach (var site in game.Zone.Planet.Pois.OrderBy(s => game.Places.DistanceTo(s, start)))
        {
            float d = game.Places.DistanceTo(site, start);
            if (d > 1500f) break;
            PoiInstance inst = site.Instance;
            var layout = inst.Layout;
            int ladders = inst.Blocks.Blocks().Count(b => b.Block.Type == BlockType.Ladder);
            int stairs = inst.Blocks.Blocks().Count(b => b.Block.Type == BlockType.Stair);
            int doors = layout.Doorways.Count(dw => dw.Cells.Any(c => inst.Blocks.Get(c).Type == BlockType.Door && inst.Blocks.Get(c).Variant != DoorState.Open));
            var mem = new PlaceMemory(site);
            var fauna = PlaceFauna.Plan(mem, deep);
            mem.Rooms = Decor.Shared?.Plan(mem);
            GD.Print($"{d,6:F0} m  {site.Name} [{site.Family.Id}] {inst.State} {site.Family.Tier}: {layout.Modules.Count} modules, storeys {layout.Modules.Min(m => m.Storey)}..{layout.Modules.Max(m => m.Storey)}, " +
                     $"{doors} shut doors, {layout.Extras.Locks.Count} locks, {layout.Extras.Keys.Count} keys, {layout.Extras.Secrets.Count} secrets, {ladders} ladder cells, {stairs} stair cells, " +
                     $"{layout.Extras.Lifts.Count} lifts, {inst.Caches.Count} caches, town {(inst.Townsfolk != null ? $"{inst.Townsfolk.People.Count} people {inst.Mood}" : "none")}, " +
                     $"fauna {(fauna.Silent ? "silent" : string.Join(" ", fauna.Population.Spawns.GroupBy(s => s.Role).Select(g => $"{g.Key}x{g.Count()}")))}");
        }
        yield return 0;
    }
}
