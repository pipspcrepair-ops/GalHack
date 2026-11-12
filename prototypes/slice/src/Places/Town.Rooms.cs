using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Deco;
using GalHack.Items;
using Godot;
using PoiGenerator.Gen;
using Wildlife;
using PInt3 = PoiGenerator.Gen.Int3;

namespace Slice.Places;

/// <summary>
/// The town's spots as people use them, and the rooms they use: every spot stands where a person
/// can stand round the dressing actually placed (a spot the dressing covered moves to the nearest
/// floor beside it), and every room's furniture adds its own spots by what the piece is for (its
/// roles): beds and bunks to sleep in, chairs, stools, sofas and benches to sit on, benches, desks
/// and counters to stand and work at, a shop's counter to keep. Routes go by the walking grid
/// with the dressing standing in, through the doors; nobody is ever walked straight through
/// anything to a spot they can't reach.
/// </summary>
public sealed partial class Town
{
    /// <summary>Where people do things: the place's spots (moved onto floor where the dressing covered them), then the furniture's.</summary>
    public readonly List<ActivitySpot> Spots = new();
    /// <summary>Each spot's exact point (local feet), the way to face there (NaN: any), and a bed's lying point (feet, on the mattress).</summary>
    readonly List<Vector3> _point = new();
    readonly List<float> _face = new();
    readonly List<Vector3?> _lie = new();
    /// <summary>A furniture spot's own piece's cells: the last step of the way to a seat goes into the seat.</summary>
    readonly List<HashSet<PInt3>> _own = new();
    int[] _beds = Array.Empty<int>();

    /// <summary>A person farther than this from you keeps to their day as a schedule: they are where they are going, not walked there, m.</summary>
    const float FarSchedule = 80f;

    static readonly string[] Keep = { "stall", "work" }, Bench = { "work" }, Seat = { "sit" };

    /// <summary>Furniture and the spots it gives: what a piece is for, by its roles.</summary>
    static string UseOf(IReadOnlyList<string> roles, bool shop)
    {
        bool Has(string r) => roles.Contains(r);
        if (Has("bed") || Has("bunk")) return roles.Contains("lounger") ? "sit" : "bed";
        if (Has("chair") || Has("stool") || Has("sofa") || Has("seat") || Has("bench-seat") || Has("pew")) return "sit";
        if (Has("counter") || Has("stall") || Has("kiosk")) return shop ? "stall" : "work";
        if (Has("bench") || Has("desk") || Has("console") || Has("toolrack") || Has("engine-stand")) return "work";
        return null;
    }

    void Furnish(PlaceMemory m)
    {
        // The place's own spots, each on floor a person can stand on.
        foreach (ActivitySpot s in Data.Spots)
        {
            // (Out of doors the land may have taken a spot's whole stretch of yard: then the nearest ground that is the place's level.)
            PInt3 c = Stand(s.Cell) ?? Stand(s.Cell, 12) ?? s.Cell;
            Spots.Add(s with { Cell = c });
            _point.Add(Walk(c));
            _face.Add(float.NaN);
            _lie.Add(null);
            _own.Add(null);
        }
        if (m.Rooms == null || Decor.Shared is not { } decor) return;
        var tags = m.Inst.Family.Tags;
        bool shop = tags.Contains("shop") || tags.Contains("market") || tags.Contains("trade") || tags.Contains("settlement");
        var reach = Spots.Count > 0 ? _nav.Reachable(Spots[0].Cell) : new HashSet<PInt3>();
        var solid = m.Rooms.Values.SelectMany(r => r.Cells).ToHashSet();
        foreach (PlaceRoom room in m.Rooms.Values.OrderBy(r => r.Module))
        {
            var taken = m.DecoTaken.GetValueOrDefault(room.Module);
            for (int i = 0; i < room.Plan.Placements.Count; i++)
            {
                DecoPlacement pl = room.Plan.Placements[i];
                if (taken != null && taken.Contains(i) || pl.Support >= 0 || pl.Fall != DecoFall.None || pl.State is DecoState.Broken or DecoState.Burnt or DecoState.Buried) continue;
                DecoPiece piece;
                try { piece = decor.Forge.Catalogue.Piece(pl.Piece); }
                catch (KeyNotFoundException) { continue; }
                string use = UseOf(piece.Roles, shop);
                if (use == null) continue;
                float yaw = Mathf.DegToRad(pl.Yaw);
                var front = new Vector3(-MathF.Sin(yaw), 0f, -MathF.Cos(yaw));
                var mid = new Vector3(room.Corner.X + pl.X, room.Corner.Y + room.Floor, room.Corner.Z + pl.Z);
                HashSet<PInt3> own = room.Blocking.TryGetValue(i, out var b) ? b : new HashSet<PInt3>(Decor.CellsOf(DecoLayout.Footprint(decor.Forge, pl), room.Corner, 0.05f));
                // Stood at from the front (a bench, a desk, a counter), sat on (a seat), climbed into from its foot (a bed).
                Vector3 want = use == "sit" ? mid + front * MathF.Min(0.25f, piece.Depth * 0.25f) : mid + front * (piece.Depth * 0.5f + 0.45f);
                PInt3? cell = Beside(want, own, reach);
                if (cell is not { } c) continue;
                Vector3 point = use == "sit" ? want with { Y = Walk(c).Y } : Walk(c);
                // A seat that isn't solid is sat on where it stands; a solid one from its own cell (the last step goes in).
                if (use == "sit" && !own.Contains(Cell(point)) && !_nav.Standable(Cell(point))) point = Walk(c);
                // A seat tucked in under another piece (a chair at a table) is left be: nobody climbs through the table to it.
                if (use == "sit" && solid.Contains(Cell(point)) && !own.Contains(Cell(point))) continue;
                // And the last step into it is clear: over floor, or into the seat itself.
                if (!Clear(Walk(c), point, own)) continue;
                Spots.Add(new ActivitySpot(use, c, room.Module));
                _point.Add(point);
                _face.Add(use is "work" or "stall" ? yaw + MathF.PI : yaw);
                float top = piece.Surface > 0f ? piece.Surface : Math.Clamp(piece.Height * 0.4f, 0.3f, 0.55f);
                _lie.Add(use == "bed" ? mid + front * (piece.Depth * 0.5f - 0.2f) + Vector3.Up * top : null);
                _own.Add(use == "sit" ? own : null);
            }
        }
        _beds = Spots.Select((s, i) => (s, i)).Where(t => t.s.Tag == "bed").Select(t => t.i).ToArray();
    }

    static PInt3 Cell(Vector3 local) => new((int)MathF.Floor(local.X), (int)MathF.Floor(local.Y + 0.05f), (int)MathF.Floor(local.Z));

    /// <summary>Whether a straight step from one point to another goes over floor all the way (or into the piece's own cells).</summary>
    bool Clear(Vector3 from, Vector3 to, HashSet<PInt3> own)
    {
        int n = (int)MathF.Ceiling((to - from).Length() / 0.05f);
        for (int k = 1; k <= n; k++)
            for (int corner = 0; corner < 4; corner++)
            {
                // The feet have a width: a little to each side of the line as well.
                Vector3 at = from.Lerp(to, k / (float)n) + new Vector3((corner & 1) == 0 ? -0.12f : 0.12f, 0f, (corner & 2) == 0 ? -0.12f : 0.12f);
                if (own.Contains(Cell(at))) continue;
                if (!(Ground(at) is float g && MathF.Abs(g - at.Y) < 0.3f)) return false;
            }
        return true;
    }

    /// <summary>The nearest standable cell to a spot's own (itself where it is clear), within a few cells and a level up, or down to the floor under it (a spot left in the air over a pit or a drop).</summary>
    PInt3? Stand(PInt3 c, int reach = 3)
    {
        if (_nav.Standable(c)) return c;
        PInt3? best = null;
        int bestD = int.MaxValue;
        for (int dy = -3; dy <= 1; dy++)
            for (int dz = -reach; dz <= reach; dz++)
                for (int dx = -reach; dx <= reach; dx++)
                {
                    var q = new PInt3(c.X + dx, c.Y + dy, c.Z + dz);
                    int d = Math.Abs(dx) + Math.Abs(dz) + Math.Abs(dy) * 4;
                    if (d < bestD && _nav.Standable(q)) { best = q; bestD = d; }
                }
        return best;
    }

    /// <summary>The standable cell nearest a point beside a piece, not one of its own, on the reachable floor.</summary>
    PInt3? Beside(Vector3 want, HashSet<PInt3> own, HashSet<PInt3> reach)
    {
        PInt3 c = Cell(want);
        PInt3? best = null;
        float bestD = 2.3f * 2.3f;
        for (int dy = -1; dy <= 1; dy++)
            for (int dz = -2; dz <= 2; dz++)
                for (int dx = -2; dx <= 2; dx++)
                {
                    var q = new PInt3(c.X + dx, c.Y + dy, c.Z + dz);
                    if (own.Contains(q) || !reach.Contains(q) || !_nav.Standable(q)) continue;
                    float d = new Vector2(q.X + 0.5f - want.X, q.Z + 0.5f - want.Z).LengthSquared() + dy * dy * 2f;
                    if (d < bestD) { best = q; bestD = d; }
                }
        return best;
    }

    /// <summary>A spot's point: where a person stands, sits or works there (local feet).</summary>
    Vector3 At(int spot) => spot >= 0 && spot < _point.Count ? _point[spot] : Walk(Spots[spot].Cell);

    /// <summary>
    /// The way to a spot from wherever they are now, when that isn't the spot they were last at (called
    /// away halfway along a route): by the walking grid from their own cell, or null where there is none.
    /// </summary>
    List<Vector3> PathFromHere(Person p, int spot)
    {
        // Getting up out of a seat: out the way they came in, by its own spot.
        PInt3? here = InSeat(p, p.Spot) ? Spots[p.Spot].Cell : InSeat(p, p.Seat) ? Spots[p.Seat].Cell : Stand(Cell(p.Pos));
        if (here is not { } h || !_nav.Standable(Spots[spot].Cell)) return null;
        List<PInt3> cells = _nav.FindPath(h, Spots[spot].Cell, 30000);
        if (cells == null) return null;
        List<Vector3> path = Points(cells);
        if ((At(spot) - path[^1]).LengthSquared() > 0.01f) path.Add(At(spot));
        return path;
    }

    /// <summary>On the last step of the way to a seat, inside the seat's own cells (tests).</summary>
    public bool IntoSeat(Person p) => InSeat(p, p.Spot) || InSeat(p, p.Seat);

    bool InSeat(Person p, int spot) => spot >= 0 && spot < _own.Count && _own[spot] is { } own && own.Contains(Cell(p.Pos)) && MathF.Abs(p.Pos.Y - At(spot).Y) < 0.3f;

    /// <summary>Faces the way the spot says once there (a seat's front, a bench).</summary>
    void FaceSpot(Person p, float dt)
    {
        float face = p.Spot >= 0 && p.Spot < _face.Count ? _face[p.Spot] : float.NaN;
        if (!float.IsNaN(face) && p.Partner == null) p.Yaw = Mathf.LerpAngle(p.Yaw, face, MathF.Min(1f, dt * 5f));
    }

    /// <summary>The bed each person sleeps in at night (their own where there are enough), else their home spot.</summary>
    void Bed(Person p)
    {
        if (_beds.Length == 0) return;
        var used = new HashSet<int>(People.Where(o => o != p).Select(o => o.Home));
        int bed = _beds.Where(b => !used.Contains(b)).DefaultIfEmpty(-1).OrderBy(b => b < 0 ? int.MaxValue : Spots[b].Cell.Manhattan(p.Spawn.Cell)).First();
        if (bed < 0) bed = _beds.OrderBy(b => Spots[b].Cell.Manhattan(p.Spawn.Cell)).First();
        p.Home = bed;
    }

    /// <summary>The ground under a point (local), the nearest walkable surface to its height: a floor's top, or along a stair's slope.</summary>
    public float? Ground(Vector3 local)
    {
        if (_nav == null) return null;
        int x = (int)MathF.Floor(local.X), z = (int)MathF.Floor(local.Z), y0 = (int)MathF.Floor(local.Y);
        float? best = null;
        for (int y = y0 - 1; y <= y0 + 1; y++)
        {
            var c = new PInt3(x, y, z);
            if (!_nav.Standable(c)) continue;
            float h = y;
            if (_nav.RampAt(c, out Dir rises))
            {
                PInt3 v = rises.Vec();
                float fx = local.X - x, fz = local.Z - z;
                h += v.X > 0 ? fx : v.X < 0 ? 1f - fx : v.Z > 0 ? fz : 1f - fz;
            }
            if (best == null || MathF.Abs(h - local.Y) < MathF.Abs(best.Value - local.Y)) best = h;
        }
        return best;
    }

    /// <summary>
    /// The cells where a route takes a body into the furniture or the clutter (its real shape: a walking body
    /// swept along each step, but the step into the seat it ends in or out of the one it starts from), or off
    /// the floor; none, for a route as it should be. In the old whole-cell rule, the cells a piece stands in.
    /// </summary>
    public IEnumerable<PInt3> Blocked(List<Vector3> path, int to, int from = -1)
    {
        var solid = _fixed == null ? Memory.Rooms?.Values.SelectMany(r => r.Cells).ToHashSet() ?? new HashSet<PInt3>() : new HashSet<PInt3>();
        HashSet<PInt3> own = to >= 0 && to < _own.Count ? _own[to] : null;
        bool outOfSeat = from >= 0 && from < _own.Count && _own[from] != null;
        for (int i = 0; i < path.Count; i++)
        {
            bool seatStep = i == path.Count - 1 && own != null || i == 1 && outOfSeat;
            // (Up and down a flight the way is the stair's: the parts' test is for the level.)
            bool level = i > 0 && MathF.Abs(path[i].Y - path[i - 1].Y) < 0.05f && !_nav.RampAt(Cell(path[i]), out _) && !_nav.RampAt(Cell(path[i - 1]), out _);
            if (_fixed != null && level && !seatStep && !_fixed.Sweep(path[i - 1], path[i], PlaceParts.PersonRadius - 0.01f, PlaceParts.PersonHeight, 0, Place.WallsOf(_nav), PlaceParts.PersonRadius - 0.01f)) yield return Cell(path[i]);
            // Each point, and the middle of the way to it from the last.
            for (int k = 0; k < (i == 0 ? 1 : 2); k++)
            {
                Vector3 at = k == 0 ? path[i] : (path[i] + path[i - 1]) * 0.5f;
                PInt3 c = Cell(at);
                if (i == path.Count - 1 && own != null && own.Contains(c)) continue;
                if (solid.Contains(c) || !(Ground(at) is float g && MathF.Abs(g - at.Y) < 0.5f)) yield return c;
            }
        }
    }

    /// <summary>Every worked-out route, by its spots (tests).</summary>
    public IEnumerable<(int A, int B, List<Vector3> Path)> Routes => _paths.Where(kv => kv.Value != null).Select(kv => (kv.Key.Item1, kv.Key.Item2, kv.Value));

    // ---- A day without the game (tests) -----------------------------------------------------

    /// <summary>
    /// Their day and night walked through with no place drawn (tests): the same routine, routes and
    /// walking as in play, with <paramref name="probe"/> told where everyone is every step.
    /// </summary>
    public void Rehearse(float seconds, float dt, Func<float, bool> night, Func<Person, int, (Species Looks, PersonRole Job)> looks, Action<Person, float> probe)
    {
        Populate();
        for (int i = 0; i < People.Count; i++)
            if (People[i].Looks == null) (People[i].Looks, People[i].Job) = looks(People[i], i);
        for (float t = 0f; t < seconds; t += dt)
        {
            _clock += dt;
            TickWays(dt);
            bool dark = night(t);
            foreach (Person p in People)
            {
                if (p.Dead || p.Gone) continue;
                if (p.Away) { if ((p.AwayFor -= dt) <= 0f) p.Away = false; continue; }
                Day(p, dark, dt);
                if (p.Asleep) p.Pos = p.Pos.MoveToward(_lie[p.Home] ?? p.Pos, dt);
                probe(p, t);
            }
        }
    }

    /// <summary>
    /// The night and the morning: home to bed (or indoors where there are no beds), asleep there, and
    /// up and out again. True while that is what they are doing.
    /// </summary>
    bool Day(Person p, bool hide, float dt)
    {
        if (hide && !p.Indoors && !p.Asleep)
        {
            // Home, wherever they were going: only reaching their own bed or door takes them in.
            if (p.Spot != p.Home || p.Path == null) GoTo(p, p.Home);
            Arrive(p, dt, true);
            return true;
        }
        if (!hide) Wake(p);
        if (p.Indoors || p.Asleep) return true;
        if (Move(p, dt))
        {
            p.Wait -= dt;
            if (p.Wait <= 0f) Next(p);
            else Idle(p, dt);
        }
        return false;
    }

    /// <summary>On the way home: there, into bed (asleep, for the night), shut in (from a fright), or in at the door where there are no beds.</summary>
    void Arrive(Person p, float dt, bool sleep)
    {
        if (!Move(p, dt) || p.Spot != p.Home) return;
        if (p.Home < _lie.Count && _lie[p.Home] != null)
        {
            // Asleep counts as in for everything else (out of reach, not about the place); only their body shows, in bed.
            if (sleep) { p.Asleep = true; p.Indoors = true; p.Yaw = _face[p.Home]; p.Path = null; }
            else p.Indoors = true; // in from a fright: shut in their room, out of sight, as anyone else's door
        }
        else p.Indoors = _homes.Length > 0;
    }

    /// <summary>Morning (or the fright over): up out of bed or out of the door, at their usual pace.</summary>
    void Wake(Person p)
    {
        if (!p.Indoors && !p.Asleep) return;
        p.Indoors = false;
        if (p.Asleep) { p.Asleep = false; p.Pos = At(p.Home); p.Path = null; p.Step = 0; }
        p.Fleeing = false;
        p.Speed = p.Spawn.Gait == "hurry" ? 3.0f : p.Spawn.Gait == "amble" ? 1.1f : 1.3f;
    }
}
