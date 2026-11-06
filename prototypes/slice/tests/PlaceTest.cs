using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GalHack.Physics;
using GalHack.Physics.Engine;
using Godot;
using PoiGenerator.Gen;
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
        if (Array.IndexOf(args, "--find-town") >= 0)
        {
            foreach (int x in FindTown()) yield return x;
            yield break;
        }
        if (Array.IndexOf(args, "--walk-only") < 0)
        {
            foreach (int x in Systems(WorldChoice.FromArgs(args) ?? WorldChoice.Tested)) yield return x;
            foreach (int x in BuiltDoors(WorldChoice.FromArgs(args) ?? WorldChoice.Tested)) yield return x;
        }
        if (Array.IndexOf(args, "--doors-only") >= 0) yield break;
        foreach (int x in RealPlayer(WorldChoice.FromArgs(args) ?? WorldChoice.Tested)) yield return x;
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

        // Out of the bunker with the locker's kit.
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
        LootCache cache = inst.Caches.Where(c => !c.Secret && nav.AccessCell(c.Cell) is PoiGenerator.Gen.Int3)
            .OrderBy(c => nav.FindPath(walk.Here(place, nav), nav.AccessCell(c.Cell).Value, 200_000)?.Count ?? 9999).First();
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
        Check(walk.StairCells > 0 && game.Survivor.WorldFeet.Y < entryY - 3.5f,
            $"down a level by the stairs to get there ({walk.StairCells} stair cells walked, {entryY - game.Survivor.WorldFeet.Y:F1} m below the way in)");


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
            Check(ok && locked.IsShut && refusedPrompt.Contains("LOCKED"), $"at the {locked.Clearance} door without its card: '{refusedPrompt}', and E leaves it shut");
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

        // The creatures: up to one of the place's hunters, awake: its tell (grit, a shadow, a scratch,
        // a rattle) comes before it strikes, its wind-up is warned, and it can be shot down and looted.
        foreach (int x in Watched(MeetHunter(game, place, walk, drive))) yield return x;
        Check(_aimedTells > 0 && _untold == 0,
            $"met a {_hunter?.Species.CommonName} ({_hunter?.Species.Role}): {_aimedTells} tells aimed at you, the nearest {_nearestTell:F1} m off; {_bites} bites, every creature's first warned before it landed ({_untold} not), health {game.Suit.HealthShare * 100:F0}%");
        Check(_lastDown != null, $"they can be shot down in the place's rooms ({_shotDown} shot down, {_healed} patch kits used)");
        Slice.Items.Lootable body = game.Zone.Loot.All.FirstOrDefault(l => l.Creature == _lastDown);
        Check(body != null, $"a body lies where it fell, a container: {body?.Name} ({body?.Storage.Contents.Total} items)");
        game.Zone.Wildlife.Frozen = true;

        // And out the way we came.
        foreach (int x in Watched(walk.To(place, entry, r => ok = r))) yield return x;
        Vector3 outward = place.ToWorld(new Vector3(entry.X + 0.5f, entry.Y, entry.Z + 0.5f) + new Vector3(inst.Layout.EntranceFacing.Vec().X, 0, inst.Layout.EntranceFacing.Vec().Z) * 12f);
        foreach (int x in Watched(drive.WalkTo(outward, 1.5f, 30f, sprint: false, dig: false, hop: false))) yield return x;
        Vector3 out1 = game.Survivor.WorldFeet;
        Check(ok && !inst.Layout.Occupied[place.CellAt(out1)] && MathF.Abs(out1.Y - game.Zone.GroundAt(out1.X, out1.Z).Y) < 1.5f && !game.Suit.Dead,
            $"and walks out of the way in, onto the open ground again (health {game.Suit.HealthShare * 100:F0}%)");
        Check(rescues == 0 && worst < 1.5f, $"no teleports all the visit (largest move in a tick {worst:F2} m), never rescued");

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
    readonly HashSet<Wildlife.Creature> _warned = new(), _bitBy = new();

    /// <summary>Walks up to a hunter that gives tells (held still), wakes the creatures, and stays until it is dealt with.</summary>
    IEnumerable<int> MeetHunter(Game.Game game, Place place, PlaceWalk walk, Driver drive)
    {
        DeepSite s = game.Zone.Wildlife.Sites.FirstOrDefault(q => q.Sim == place.Memory.Fauna?.Sim);
        if (s == null) yield break;
        var nav = place.Nav();
        var reach = nav.Reachable(walk.Here(place, nav));
        // The hunter nearest the way in that has tells, on a level with a reachable floor near it.
        Vector3 way = place.ToWorld(new Vector3(place.Inst.Layout.EntranceCell.X, place.Inst.Layout.EntranceCell.Y, place.Inst.Layout.EntranceCell.Z));
        foreach (Wildlife.Creature c in s.Sim.Creatures
                     .Where(c => !c.Dead && !c.Removed && c.Species.Tells.Length > 0 && c.Species.Role is Wildlife.DeepRole.Ambusher or Wildlife.DeepRole.Lurker or Wildlife.DeepRole.Prowler)
                     .OrderBy(c => s.World(c.Position).DistanceTo(way) + Mathf.Abs(c.Position.Y - way.Y) * 3f))
        {
            var cell = new PoiGenerator.Gen.Int3(Mathf.FloorToInt(c.Position.X), Mathf.FloorToInt(c.Position.Y), Mathf.FloorToInt(c.Position.Z));
            PoiGenerator.Gen.Int3? near = null;
            float best = float.MaxValue;
            foreach (var q in reach)
            {
                float d = new Vector2(q.X - cell.X, q.Z - cell.Z).Length() + Mathf.Abs(q.Y - cell.Y) * 3f;
                if (d >= 5f && d < best) { best = d; near = q; }
            }
            if (near == null || best > 9f) continue;
            _hunter = c;
            GD.Print($"  (going to meet a {c.Species.CommonName} ({c.Species.Role}, {c.Species.Stats.MaxHealth:F0} health) near {cell}, from {near})");
            bool ok = false;
            foreach (int x in walk.To(place, near.Value, r => ok = r)) yield return x;
            break;
        }
        if (_hunter == null) yield break;
        game.Zone.Wildlife.Frozen = false;
        for (int t = 0; t < 3600 && _lastDown == null && !game.Suit.Dead; t++) yield return 0;
    }

    /// <summary>Goes looking for the place's hunters: to the nearest ambusher or lurker's lair, a few times, until one gives itself away.</summary>
    IEnumerable<int> SeekTell(Game.Game game, Place place, PlaceWalk walk)
    {
        DeepSite s = game.Zone.Wildlife.Sites.FirstOrDefault(q => q.Sim == place.Memory.Fauna?.Sim);
        if (s == null) yield break;
        var nav = place.Nav();
        var tried = new HashSet<Wildlife.Creature>();
        for (int tries = 0; tries < 6 && _aimedTells == 0 && !game.Suit.Dead; tries++)
        {
            Vector3 feet = game.Survivor.WorldFeet;
            int here = walk.Here(place, nav).Y;
            Wildlife.Creature hunter = s.Sim.Creatures
                .Where(c => !c.Dead && !c.Removed && !tried.Contains(c) && c.Species.Role is Wildlife.DeepRole.Ambusher or Wildlife.DeepRole.Lurker && Mathf.Abs(c.Position.Y - here) < 5f)
                .OrderBy(c => s.World(c.Position).DistanceTo(feet)).FirstOrDefault();
            if (hunter == null) yield break;
            tried.Add(hunter);
            var cell = new PoiGenerator.Gen.Int3(Mathf.FloorToInt(hunter.Position.X), Mathf.FloorToInt(hunter.Position.Y), Mathf.FloorToInt(hunter.Position.Z));
            PoiGenerator.Gen.Int3? near = null;
            float best = float.MaxValue;
            var reach = nav.Reachable(walk.Here(place, nav));
            foreach (var c in reach)
            {
                float d = new Vector2(c.X - cell.X, c.Z - cell.Z).Length() + Mathf.Abs(c.Y - cell.Y) * 2f;
                if (d < best && d > 2.5f) { best = d; near = c; }
            }
            if (near == null) continue;
            GD.Print($"  (looking for it: a {hunter.Species.CommonName} ({hunter.Species.Role}) near {cell}, going to {near})");
            bool ok = false;
            foreach (int x in walk.To(place, near.Value, r => ok = r, () => _aimedTells > 0)) yield return x;
            for (int t = 0; t < 600 && _aimedTells == 0 && !game.Suit.Dead; t++) yield return 0;
        }
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
        Vector3 ahead = game.Bunker.Ahead(14f);
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
        Vector3 along = (door - game.Survivor.WorldFeet) with { Y = 0f };
        along = along.Normalized();
        Vector3 before = door - along * 6f, past = door + along * 7f;
        foreach (int x in drive.WalkTo(before, 1f, 20f, sprint: false, dig: false, hop: false)) yield return x;
        foreach (int x in drive.WalkTo(door + along * 1.8f, 0.6f, 12f, sprint: false, dig: false, hop: false)) yield return x;
        Vector3 f = game.Survivor.WorldFeet;
        Check((f - door).Dot(along) > 0.5f && game.Doors.Opened >= 1, $"it opens as you walk up (your own structure), and you walk through ({(f - door).Dot(along):F1} m past it)");
        Check(game.Doors.OpenDoors(game.Places).Any(), "an open door is there to read (a force field's hook)");
        foreach (int x in drive.WalkTo(past, 1f, 12f, sprint: false, dig: false, hop: false)) yield return x;
        foreach (int x in drive.Wait(90)) yield return x;
        Check(!Slice.Vessels.BlockDoors.IsOpen(gate, cell), "and it shuts behind you, solid again");
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
        // Far places are solid on the world layer.
        Node3D far = null;
        for (int i = 0; i < 600 && (far = game.Zone.Pois.NodeOf(site)) == null; i++) yield return 0;
        var farBody = far?.GetNodeOrNull<StaticBody3D>("Collision");
        Check(farBody != null && farBody.CollisionLayer == Layers.World, $"the far drawing of {site.Name} collides on the world layer ({farBody?.CollisionLayer})");
        GD.Print($"  (SYSTEMS: the focus is moved to the place by hand, not walked; the warm-up while loading took {places.WarmUpMs:F0} ms)");
        Vector3 centre = game.Zone.Planet.NearestCopy(new Vector3(site.Origin.X, site.Origin.Y, site.Origin.Z), game.Survivor.WorldFeet);
        places.Focus = () => centre + Vector3.Back * 250f;
        var watch = Stopwatch.StartNew();
        int frames = 0;
        while (places.PlaceOf(site) is not { Ready: true } && watch.Elapsed.TotalSeconds < 60) { frames++; yield return 0; }
        Place p = places.PlaceOf(site);
        Check(p is { Ready: true }, $"{site.Name} stands up as a structure when the focus comes within {places.BuildRadius} m ({frames} frames, {watch.Elapsed.TotalSeconds:F1} s)");
        if (p == null) yield break;
        var log = places.BuildLog.Last();
        Check(places.WorstFrameMs < places.BudgetMs + 12.0, $"built over frames: {log.Chunks} chunks; worst frame {places.WorstFrameMs:F1} ms of building, worst single step {places.WorstStepMs:F1} ms (by stage {string.Join(", ", places.StageWorst.Select(kv => $"{kv.Key}: {kv.Value:F1}"))}) (budget {places.BudgetMs} ms a frame)");
        for (int i = 0; i < 240; i++) yield return 0; // the creatures and lairs go in over these frames
        double views = game.Zone.Wildlife.Sites.Count > 0 ? game.Zone.Wildlife.Sites.Max(s => s.WorstViewMs) : 0;
        Check(views < 8.0, $"its creatures and lair marks go in a few a frame: worst {views:F1} ms a frame");
        GD.Print($"  slow steps: {string.Join("; ", places.SlowSteps.Select(s => $"{s.Stage} {s.Ms:F1}{(s.Collected ? " (a garbage collection)" : "")}"))}");
        bool farHidden = far.GetChildren().OfType<MeshInstance3D>().Where(m => m.Name.ToString().StartsWith("Chunk")).All(m => !m.Visible) && farBody.ProcessMode == ProcessModeEnum.Disabled;
        Check(farHidden, "the far drawing's blocks give way to it in the same frame (hidden, its collision off); its lamps and smoke stay");
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
