using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GalHack.Deco;
using GalHack.Items;
using Godot;
using PlanetSurface.Poi;
using Slice.Game;
using Slice.Places;
using Layers = GalHack.Physics.Layers;
using PInt3 = PoiGenerator.Gen.Int3;

namespace Slice.Tests;

/// <summary>
/// Hazards go up: on a fresh world, the nearest place whose dressing has two hazards (blasting crates,
/// fuel drums, gas) within one's reach of the other is stood up round the survivor; a bolt's worth of
/// hurt through the first one's hit points sets it off. It goes, the place's blocks round it take the
/// blast, the survivor beside it is hurt, and a moment later (not the same instant) its neighbour follows.
///
/// Run: .\tools\test.ps1 -Project prototypes/slice -Scene res://tests/HazardTest.tscn
/// </summary>
public partial class HazardTest : Node
{
    const int WatchdogMinutes = 10;
    IEnumerator<int> _run;
    int _failures, _passes;
    readonly Stopwatch _clock = Stopwatch.StartNew();

    public override void _Ready()
    {
        new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(TimeSpan.FromMinutes(WatchdogMinutes));
            Console.Error.WriteLine($"FAIL  the run took over {WatchdogMinutes} minutes: watchdog");
            System.Environment.Exit(1);
        }) { IsBackground = true }.Start();
        ProcessPhysicsPriority = 100;
        _run = Scenario().GetEnumerator();
    }

    public override void _PhysicsProcess(double delta)
    {
        bool more;
        try { more = _run.MoveNext(); }
        catch (Exception e)
        {
            Check(false, $"scenario threw: {e}");
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
        GD.Print($"{(ok ? "PASS" : "FAIL")}  {what}");
    }

    /// <summary>A place's dressed room with two hazards in each other's reach: the room, the first and its neighbour.</summary>
    static (PlaceRoom Room, int First, int Next)? Pair(PlaceMemory memory)
    {
        var cat = Decor.Shared.Catalogue;
        foreach (PlaceRoom room in memory.Rooms.Values)
        {
            var taken = memory.DecoTaken.GetValueOrDefault(room.Module) ?? new HashSet<int>();
            var hazards = room.Solid.Where(i => !taken.Contains(i) && cat.Piece(room.Plan.Placements[i].Piece).Blast != null).ToList();
            foreach (int a in hazards)
                foreach (int b in hazards)
                {
                    if (a == b) continue;
                    DecoPlacement pa = room.Plan.Placements[a], pb = room.Plan.Placements[b];
                    float d = new Vector2(pa.X - pb.X, pa.Z - pb.Z).Length();
                    if (d > 1.2f && d < cat.Piece(pa.Piece).Blast.Radius * 0.8f) return (room, a, b);
                }
        }
        return null;
    }

    IEnumerable<int> Scenario()
    {
        for (int world = 0; world < 4; world++)
        {
            var game = new Game.Game { Interactive = false, World = WorldChoice.Fresh("random"), SkipIntro = true };
            AddChild(game);
            var drive = new Driver(game);
            foreach (int x in drive.UntilPlaying()) yield return x;
            Vector3 start = game.Start;
            PoiSite site = null;
            (PlaceRoom Room, int First, int Next) pair = default;
            foreach (PoiSite s in game.Zone.Planet.Pois.OrderBy(s => game.Places.DistanceTo(s, start)).Take(30))
            {
                var memory = new PlaceMemory(s);
                memory.Rooms = Decor.Shared.Plan(memory);
                if (Pair(memory) is not { } found) continue;
                // A floor to stand on well inside the first one's reach (a cramped room may leave none).
                DecoPlacement fp = found.Room.Plan.Placements[found.First];
                var rel = new Vector3(found.Room.Corner.X + fp.X, found.Room.Corner.Y + found.Room.Floor, found.Room.Corner.Z + fp.Z);
                PInt3 st = Free(s, found.Room, rel);
                if (new Vector3(st.X + 0.5f, st.Y + found.Room.Floor + 0.05f, st.Z + 0.5f).DistanceTo(rel) > Decor.Shared.Catalogue.Piece(fp.Piece).Blast.Radius - 1.2f) continue;
                site = s;
                pair = found;
                break;
            }
            if (site == null)
            {
                GD.Print($"  {game.World}: no place near the start has two hazards side by side: the next world");
                game.QueueFree();
                for (int i = 0; i < 5; i++) yield return 0;
                continue;
            }
            // Into the room, a couple of metres from the first hazard, on its floor.
            DecoPlacement first = pair.Room.Plan.Placements[pair.First];
            Vector3 origin = game.Zone.Planet.NearestCopy(new Vector3(site.Origin.X, site.Origin.Y, site.Origin.Z), start);
            Vector3 hazardAt = origin + new Vector3(pair.Room.Corner.X + first.X, pair.Room.Corner.Y + pair.Room.Floor, pair.Room.Corner.Z + first.Z);
            PInt3 stand = Free(site, pair.Room, hazardAt - origin);
            Vector3 feet = origin + new Vector3(stand.X + 0.5f, stand.Y + pair.Room.Floor + 0.05f, stand.Z + 0.5f);
            GD.Print($"  {game.World}: {site.Name} [{site.Family.Id}], {pair.Room.Room.RoomId}: {first.Piece} and {pair.Room.Plan.Placements[pair.Next].Piece}, standing {feet.DistanceTo(hazardAt):F1} m off");
            game.Survivor.PlaceOutside(new System.Numerics.Vector3(feet.X, feet.Y, feet.Z));
            Place place = null;
            DecoHit a = null, b = null;
            var watch = Stopwatch.StartNew();
            while (watch.Elapsed.TotalSeconds < 90)
            {
                place = game.Places.PlaceOf(site);
                if (place is { Ready: true })
                {
                    a = place.Pieces().FirstOrDefault(h => h.Room.Module == pair.Room.Module && h.Index == pair.First);
                    b = place.Pieces().FirstOrDefault(h => h.Room.Module == pair.Room.Module && h.Index == pair.Next);
                    if (a != null && b != null) break;
                }
                yield return 0;
            }
            Check(a != null && b != null, $"the place stands round the survivor with both hazards in their room ({watch.Elapsed.TotalSeconds:F1} s)");
            if (a == null || b == null) { game.QueueFree(); yield break; }
            // Set down before the place stood, the survivor had no floor there: into the room again now it stands.
            game.Survivor.PlaceOutside(new System.Numerics.Vector3(feet.X, feet.Y, feet.Z));
            foreach (int x in drive.Wait(30)) yield return x;

            // A bolt's hurt, as the tool's shot lands it: through the place's own resolution of what was hit.
            double health = game.Suit.Vitals.Health;
            float off = (game.Survivor.WorldFeet + Vector3.Up * 0.9f).DistanceTo(a.Node.GlobalPosition + Vector3.Up * (a.Piece.Height * 0.5f));
            int blocks = place.Memory.BlockDamage.Count, explosions = game.Places.Explosions;
            var bodyAt = a.Node.GlobalPosition + Vector3.Up * 0.3f;
            // The way the tool's ray meets it: down onto the piece from above, the body and shape that answer.
            var down = PhysicsRayQueryParameters3D.Create(bodyAt + Vector3.Up * (a.Piece.Height + 1f), bodyAt - Vector3.Up * 0.2f, Layers.World | Place.SceneryLayer);
            var met = game.Zone.GetWorld3D().DirectSpaceState.IntersectRay(down);
            PoiPlaces.Hit hit = default;
            Check(met.Count > 0 && game.Places.Resolve(met["collider"].AsGodotObject(), bodyAt, Vector3.Up, out hit, (int)met["shape"]) && hit.Piece?.Index == a.Index, $"a shot at the {a.Piece.Name} lands on it");
            float shots = 0f;
            while (game.Places.Explosions == explosions && shots < 40)
            {
                game.Places.Shot(hit, game.Zone.Data.Rules.Tool.ShotDamage);
                shots++;
            }
            Check(game.Places.Explosions == explosions + 1 && !a.Node.Visible, $"shot through its {a.Piece.Hp} hit points ({shots} shots) it goes up, and is gone");
            Check(b.Node.Visible, "its neighbour isn't set off in the same instant");
            Check(place.Memory.BlockDamage.Count > blocks || place.Memory.Modified, $"the place's blocks round it take the blast ({place.Memory.BlockDamage.Count - blocks} hurt)");
            // Game time, not the wall clock: under --fixed-fps a headless frame can take far less than its 1/60 s.
            ulong blastFrame = Engine.GetPhysicsFrames();
            double GameSecondsSince() => (Engine.GetPhysicsFrames() - blastFrame) / (double)Engine.PhysicsTicksPerSecond;
            foreach (int x in drive.Wait(3)) yield return x;
            Check(game.Suit.Vitals.Health < health, $"the survivor beside it is hurt ({health:F0} to {game.Suit.Vitals.Health:F0} health, {off:F1} m from it, reach {a.Piece.Blast.Radius:F1} m)");
            while (b.Node.Visible && GameSecondsSince() < 3) yield return 0;
            Check(!b.Node.Visible && game.Places.Explosions >= explosions + 2 && GameSecondsSince() >= PoiPlaces.ChainDelay * 0.8f,
                $"and a moment later its neighbour, the {b.Piece.Name}, follows ({GameSecondsSince():F2} s on)");
            game.QueueFree();
            yield break;
        }
        Check(false, "no world of four had a place with two hazards side by side near its start");
    }

    /// <summary>A free cell of the room near (but not on) a point in the place's grid: floor under it, room to stand.</summary>
    static PInt3 Free(PoiSite site, PlaceRoom room, Vector3 near)
    {
        var nav = site.Instance.Nav();
        PInt3 best = room.Corner;
        float bestD = float.MaxValue;
        for (int z = 0; z < room.Room.Depth; z++)
            for (int x = 0; x < room.Room.Width; x++)
            {
                var c = new PInt3(room.Corner.X + x, room.Corner.Y, room.Corner.Z + z);
                if (room.Cells.Contains(c) || !nav.Standable(c)) continue;
                float d = MathF.Abs(new Vector2(c.X + 0.5f - near.X, c.Z + 0.5f - near.Z).Length() - 2f);
                if (d < bestD) { bestD = d; best = c; }
            }
        return best;
    }
}
