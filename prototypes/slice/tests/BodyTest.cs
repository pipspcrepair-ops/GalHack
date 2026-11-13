using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GalHack.Deco;
using GalHack.Deco.Engine;
using GalHack.Physics.Engine;
using Godot;
using PlanetSurface.Poi;
using Slice.Game;
using Slice.Places;
using Layers = GalHack.Physics.Layers;
using PInt3 = PoiGenerator.Gen.Int3;

namespace Slice.Tests;

/// <summary>
/// A place's dressing shares bodies (one a room and layer) and collision is what it always was: in a standing
/// place a table, a chair and a locker stop a ray exactly where their boxes are and stop a person-sized capsule and
/// a creature-sized one a body's radius off the face, each within a centimetre of the boxes worked out here; a
/// piece salvaged alone takes only its own shapes (and what stood on it) with it, and the pieces beside it still
/// stop the same ray at the same point. And no body failed to be created while it ran.
///
/// Run: .\tools\test.ps1 -Project prototypes/slice -Scene res://tests/BodyTest.tscn
/// </summary>
public partial class BodyTest : Node
{
    const int WatchdogMinutes = 12;
    const float Tolerance = 0.01f;
    // A table, a seat and a locker (or the nearest kind the dressing has that stops walking).
    static readonly string[][] Kinds = { new[] { "table", "desk" }, new[] { "chair", "stool", "bench", "seat" }, new[] { "locker", "cabinet", "shelf", "crate" } };
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
        JoltWatch.Install();
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
        Check(JoltWatch.Failed == 0, $"no body failed to be created ({JoltWatch.Failed})");
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

    /// <summary>One solid piece of each kind in one room of a place, none with another solid piece within 1.2 m, in the order of <see cref="Kinds"/>.</summary>
    static List<(PlaceRoom Room, int Index)> Targets(PlaceMemory memory)
    {
        foreach (PlaceRoom room in memory.Rooms.Values)
        {
            var taken = memory.DecoTaken.GetValueOrDefault(room.Module) ?? new HashSet<int>();
            var all = room.Plan.Placements;
            var found = new List<(PlaceRoom, int)>();
            foreach (string[] kind in Kinds)
            {
                int at = room.Solid.OrderBy(i => i).FirstOrDefault(i => !taken.Contains(i) && all[i].Support < 0 && kind.Any(w => all[i].Piece.Contains(w, StringComparison.OrdinalIgnoreCase))
                    && !room.Solid.Any(k => k != i && !taken.Contains(k) && new Vector2(all[k].X - all[i].X, all[k].Z - all[i].Z).Length() < 1.2f), -1);
                if (at < 0) break;
                found.Add((room, at));
            }
            if (found.Count == Kinds.Length) return found;
        }
        return null;
    }

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
                float d = MathF.Abs(new Vector2(c.X + 0.5f - near.X, c.Z + 0.5f - near.Z).Length() - 3f);
                if (d < bestD) { bestD = d; best = c; }
            }
        return best;
    }

    /// <summary>How far along a segment it first meets a box (its transform and half size); null for a miss.</summary>
    static float? RayBox(Transform3D box, Vector3 half, Vector3 from, Vector3 to)
    {
        Transform3D inv = box.AffineInverse();
        Vector3 o = inv * from, d = inv.Basis * (to - from);
        float t0 = 0f, t1 = 1f;
        for (int a = 0; a < 3; a++)
        {
            if (MathF.Abs(d[a]) < 1e-9f) { if (MathF.Abs(o[a]) > half[a]) return null; continue; }
            float u = (-half[a] - o[a]) / d[a], v = (half[a] - o[a]) / d[a];
            if (u > v) (u, v) = (v, u);
            t0 = MathF.Max(t0, u);
            t1 = MathF.Min(t1, v);
            if (t0 > t1) return null;
        }
        return t0 * (to - from).Length();
    }

    /// <summary>Where a segment first meets any of the piece's boxes, by the boxes alone (the distance from its start).</summary>
    static float? RayPiece(DecoNode node, Vector3 from, Vector3 to)
    {
        float? best = null;
        foreach (var (shape, at) in Decor.Shared.Meshes.Shapes(node.Look))
            if (RayBox(node.GlobalTransform * at, shape.Size * 0.5f, from, to) is float t && (best == null || t < best)) best = t;
        return best;
    }

    static Godot.Collections.Dictionary Ray(Game.Game game, Vector3 from, Vector3 to) =>
        game.Zone.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(from, to, Layers.World));

    /// <summary>Whether a ray's hit is this piece (by the place's own resolution of the body and shape).</summary>
    static bool Meets(Game.Game game, Godot.Collections.Dictionary ray, DecoHit h) =>
        ray.Count > 0 && game.Places.Resolve(ray["collider"].AsGodotObject(), (Vector3)ray["position"], (Vector3)ray["normal"], out PoiPlaces.Hit r, (int)ray["shape"]) && r.Piece?.Index == h.Index && r.Piece.Room == h.Room;

    /// <summary>A short ray in at a piece's middle from the first side that meets the piece itself.</summary>
    static (Vector3 From, Vector3 To) Aim(Game.Game game, DecoHit h)
    {
        Vector3 to = h.Node.GlobalPosition + Vector3.Up * 0.3f;
        for (int side = 0; side < 4; side++)
            if (Meets(game, Ray(game, to + Side(h.Node, side) * 2.5f, to), h)) return (to + Side(h.Node, side) * 2.5f, to);
        return (to + Side(h.Node, 0) * 2.5f, to);
    }

    static Vector3 Side(DecoNode node, int side) => node.GlobalTransform.Basis * (side switch { 0 => Vector3.Right, 1 => Vector3.Left, 2 => Vector3.Forward, _ => Vector3.Back });

    IEnumerable<int> Scenario()
    {
        for (int world = 0; world < 5; world++)
        {
            var game = new Game.Game { Interactive = false, World = WorldChoice.Fresh("random"), SkipIntro = true };
            AddChild(game);
            var drive = new Driver(game);
            foreach (int x in drive.UntilPlaying()) yield return x;
            PoiSite site = null;
            List<(PlaceRoom Room, int Index)> picks = null;
            foreach (PoiSite s in game.Zone.Planet.Pois.OrderBy(s => game.Places.DistanceTo(s, game.Start)).Take(150))
            {
                var memory = new PlaceMemory(s);
                memory.Rooms = Decor.Shared.Plan(memory);
                if (Targets(memory) is not { } found) continue;
                picks = found;
                site = s;
                break;
            }
            if (site == null)
            {
                GD.Print($"  {game.World}: no place near the start has a table, a chair and a locker standing clear: the next world");
                game.QueueFree();
                for (int i = 0; i < 5; i++) yield return 0;
                continue;
            }
            // Walk to each piece in turn (a large place dresses its rooms as you near them).
            Place place = null;
            var watch = Stopwatch.StartNew();
            var hits = new List<DecoHit>();
            int moved = -1;
            for (int k = 0; k < Kinds.Length; k++)
                while (hits.Count == k && watch.Elapsed.TotalSeconds < 240)
                {
                    place = game.Places.PlaceOf(site);
                    var (room, index) = picks[k];
                    DecoPlacement pl = room.Plan.Placements[index];
                    if (moved != k)
                    {
                        // To the piece first: a place stands up round the survivor.
                        moved = k;
                        Vector3 origin = game.Zone.Planet.NearestCopy(new Vector3(site.Origin.X, site.Origin.Y, site.Origin.Z), game.Start);
                        PInt3 st = Free(site, room, new Vector3(room.Corner.X + pl.X, room.Corner.Y + room.Floor, room.Corner.Z + pl.Z));
                        game.Survivor.PlaceOutside((origin + new Vector3(st.X + 0.5f, st.Y + room.Floor + 0.05f, st.Z + 0.5f)).ToNumerics());
                    }
                    else if (place is { Ready: true } && place.Pieces().FirstOrDefault(h => h.Room.Module == room.Module && h.Index == index) is { } hit) { hits.Add(hit); break; }
                    yield return 0;
                }
            Check(hits.Count == Kinds.Length, $"{game.World}: {site.Name} stands with a table, a chair and a locker dressed ({watch.Elapsed.TotalSeconds:F1} s)");
            if (hits.Count != Kinds.Length) { game.QueueFree(); yield break; }
            foreach (int x in drive.Wait(60)) yield return x;
            var (bodies, shapes) = JoltWatch.Census(place);
            GD.Print($"  the place holds {bodies} bodies and {shapes} shapes, {place.DecoPieces} dressing pieces standing");

            // Rays: from each side of a piece, at each of its boxes' middle heights, into the box's middle.
            int rayMatched = 0, rayTried = 0;
            float rayWorst = 0f;
            foreach (DecoHit h in hits)
            {
                int matched = 0, tried = 0;
                float worst = 0f;
                for (int side = 0; side < 4; side++)
                    foreach (var (shape, at) in Decor.Shared.Meshes.Shapes(h.Node.Look).Where(b => b.Shape.Size.Length() > 0.1f && MathF.Min(b.Shape.Size.X, MathF.Min(b.Shape.Size.Y, b.Shape.Size.Z)) >= 0.06f)) // (a rod thinner than the engine's box margin is skipped)
                    {
                        Vector3 mid = (h.Node.GlobalTransform * at).Origin, from = mid + Side(h.Node, side) * 2.5f;
                        float? expect = RayPiece(h.Node, from, mid);
                        var met = Ray(game, from, mid);
                        // Another piece or a wall in the way answers for itself: only rays that reach this piece count.
                        if (met.Count > 0 && !Meets(game, met, h)) continue;
                        tried++;
                        float got = met.Count > 0 ? from.DistanceTo((Vector3)met["position"]) : -1f;
                        if (expect == null ? met.Count == 0 : met.Count > 0 && MathF.Abs(got - expect.Value) <= Tolerance) matched++;
                        else worst = MathF.Max(worst, expect == null ? 9f : MathF.Abs(got - expect.Value));
                    }
                (rayMatched, rayTried, rayWorst) = (rayMatched + matched, rayTried + tried, MathF.Max(rayWorst, worst));
            }
            Check(rayTried > 0 && rayMatched == rayTried, $"rays at the table, chair and locker meet them where their boxes are ({rayMatched} of {rayTried} rays within a centimetre{(rayWorst > 0 ? $", worst off by {rayWorst:F3} m" : "")})");

            // Capsules (the survivor's, a creature's) standing on the floor and walked at each piece along its own axes
            // stop where they stopped against that piece's own body of boxes, one a piece, as dressing was before.
            const uint Reference = 1u << 21;
            var refs = new List<StaticBody3D>();
            foreach (DecoHit h in hits)
            {
                var body = new StaticBody3D { CollisionLayer = Reference, CollisionMask = 0 };
                place.AddChild(body);
                body.GlobalTransform = h.Node.GlobalTransform;
                foreach (var (shape, at) in Decor.Shared.Meshes.Shapes(h.Node.Look)) body.AddChild(new CollisionShape3D { Shape = shape, Transform = at });
                refs.Add(body);
            }
            foreach (int x in drive.Wait(3)) yield return x;
            foreach (var (radius, height, who) in new[] { (Slice.Player.Survivor.Radius, Slice.Player.Survivor.Height, "the player"), (0.3f, 1.0f, "a creature") })
            {
                var capsule = new CapsuleShape3D { Radius = radius, Height = height };
                var space = game.Zone.GetWorld3D().DirectSpaceState;
                int matched = 0, tried = 0;
                float worst = 0f;
                foreach (DecoHit h in hits)
                {
                    Vector3 centre = (h.Node.GlobalTransform * Decor.Shared.Meshes.Shapes(h.Node.Look).OrderByDescending(s => s.Shape.Size.X * s.Shape.Size.Z).First().At).Origin;
                    float bottom = h.Node.GlobalPosition.Y + 0.06f;
                    centre.Y = bottom + height * 0.5f;
                    for (int side = 0; side < 4; side++)
                    {
                        Vector3 from = centre + Side(h.Node, side) * 2.5f;
                        var q = new PhysicsShapeQueryParameters3D { Shape = capsule, Transform = new Transform3D(Basis.Identity, from), Motion = centre - from, CollisionMask = Layers.World };
                        if (space.GetRestInfo(q).Count > 0) continue; // something else already touching the capsule where it starts
                        float[] cast = space.CastMotion(q);
                        q.CollisionMask = Reference;
                        float[] want = space.CastMotion(q);
                        if (cast.Length == 0 || want.Length == 0 || cast[1] >= 1f || want[1] >= 1f) continue;
                        // What it stopped against must be this piece (a wall or another piece answers for itself).
                        q.CollisionMask = Layers.World;
                        q.Transform = new Transform3D(Basis.Identity, from + q.Motion * cast[1]);
                        var rest = space.GetRestInfo(q);
                        if (rest.Count == 0 || GodotObject.InstanceFromId((ulong)rest["collider_id"]) is not CollisionObject3D body
                            || !(game.Places.Resolve(body, q.Transform.Origin, Vector3.Up, out PoiPlaces.Hit r, (int)rest["shape"]) && r.Piece?.Index == h.Index && r.Piece.Room == h.Room)) continue;
                        tried++;
                        float off = MathF.Abs((cast[0] - want[0]) * q.Motion.Length());
                        if (off <= Tolerance) matched++;
                        else worst = MathF.Max(worst, off);
                    }
                }
                Check(tried >= 1 && matched == tried, $"{who} is stopped by the table, chair and locker where their own boxes stop it ({matched} of {tried} approaches within a centimetre{(worst > 0 ? $", worst off by {worst:F3} m" : "")})");
            }
            foreach (StaticBody3D body in refs) body.QueueFree();

            // The locker salvaged alone: its shapes go (and what stood on it), nothing else's.
            DecoHit gone = hits[2], keep = hits[0];
            var (keepFrom, keepTo) = Aim(game, keep);
            var (goneFrom, goneTo) = Aim(game, gone);
            var keepBefore = Ray(game, keepFrom, keepTo);
            bool keepMet = Meets(game, keepBefore, keep), goneMet = Meets(game, Ray(game, goneFrom, goneTo), gone);
            var goneBefore = Ray(game, goneFrom, goneTo);
            var standing = place.Pieces().Where(p => p.Room.Module == gone.Room.Module).Select(p => (p.Room.Module, p.Index, Boxes: Decor.Shared.Meshes.Shapes(p.Node.Look).Count)).ToList();
            Node roomNode = gone.Node.GetParent(); // (other rooms may be dressing meanwhile: count this one)
            int shapesBefore = JoltWatch.Census(roomNode).Shapes;
            place.TakePiece(gone);
            foreach (int x in drive.Wait(5)) yield return x;
            var after = place.Pieces().Where(p => p.Room.Module == gone.Room.Module).Select(p => (p.Room.Module, p.Index)).ToHashSet();
            int removed = standing.Where(p => !after.Contains((p.Module, p.Index))).Sum(p => p.Boxes);
            var goneAfter = Ray(game, goneFrom, goneTo);
            var keepAfter = Ray(game, keepFrom, keepTo);
            Check(!after.Contains((gone.Room.Module, gone.Index)), "the salvaged locker is gone from the room");
            Check(goneMet && (goneAfter.Count == 0 || goneFrom.DistanceTo((Vector3)goneAfter["position"]) > goneFrom.DistanceTo((Vector3)goneBefore["position"]) + 0.05f), "a ray where the locker stood goes through it now");
            Check(keepMet && keepAfter.Count > 0 && ((Vector3)keepBefore["position"]).DistanceTo((Vector3)keepAfter["position"]) < 0.001f, "the table in the same room stops its ray at the same point");
            int shapesAfter = JoltWatch.Census(roomNode).Shapes;
            Check(removed >= 1 && shapesBefore - shapesAfter == removed, $"the shapes that went are the locker's and what stood on it ({shapesBefore - shapesAfter} shapes gone, {removed} boxes in the pieces taken)");
            game.QueueFree();
            for (int i = 0; i < 5; i++) yield return 0;
            yield break;
        }
        Check(false, "no world in five had a place with a table, a chair and a locker");
    }
}
