using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GalHack.Items;

namespace GalHack.Deco;

/// <summary>
/// Furnishes a room as it was used: wall templates packed along the walls in rows (desks facing
/// screens, beds head to the wall), island templates on a lattice out in the room (tables with
/// their chairs, pews facing the altar), then the set's fill pieces in the gaps along the walls.
/// Doorways and a walkway between every door stay clear: each template is only kept if a walker
/// can still get from door to door. Deterministic from the room's seed.
/// </summary>
public sealed class DecoPlanner
{
    readonly DecoForge _forge;
    readonly DecoArrangements _arr;
    readonly DecoCatalogue _cat;
    DecoRoom _room;
    DecoPlan _plan;
    DecoRaster _raster;
    DecoRng _rng;
    List<DecoRect> _reserved;
    int _groups;
    readonly Dictionary<string, DecoPiece> _chosen = new();

    public DecoPlanner(DecoForge forge, DecoArrangements arr)
    {
        _forge = forge;
        _arr = arr;
        _cat = forge.Catalogue;
    }

    public const float Cell = 0.25f;

    /// <summary>One piece of a template use, in the template's own frame (its back against +z when on a wall).</summary>
    sealed class Member
    {
        public DecoPiece Piece;
        public Vector2 At;
        public float Yaw, Y;
        public int Support = -1;
        public string Role;
    }

    /// <summary>
    /// How many of one piece a room may hold: seats in rows and round tables a good few, things set on a
    /// surface a few more, anything else four. A room is never one piece over and over.
    /// </summary>
    public static int CapOf(DecoPiece p) =>
        p.Roles.Any(r => r is "seat-row" or "pew" or "bench-seat" or "airline-seat" or "seat" or "chair" or "stool") ? 16 : p.Mount == DecoMount.Top ? 5 : 3;

    readonly Dictionary<string, int> _count = new();

    bool Full(DecoPiece p) => _count.GetValueOrDefault(p.Id) >= CapOf(p);

    /// <summary>Forgets a template's chosen pieces, so its next use picks afresh (a room isn't the same thing over and over).</summary>
    void Repick(DecoTemplate t)
    {
        foreach (string k in _chosen.Keys.Where(k => k.StartsWith(t.Id + ":", StringComparison.Ordinal)).ToList()) _chosen.Remove(k);
    }

    public DecoPlan Plan(DecoRoom room, DecoSetPlan mix = null)
    {
        _room = room;
        _plan = new DecoPlan { Room = room };
        _raster = new DecoRaster(room.Width, room.Depth, Cell);
        _rng = new DecoRng(DecoHash.Combine(room.Seed, DecoHash.Fnv(room.Set ?? "")));
        _groups = 0;
        _chosen.Clear();
        _count.Clear();
        string named = room.Recipe ?? room.RoomId;
        mix ??= (named != null && _arr.Recipes.TryGetValue(named, out var recipe)) ? recipe : _arr.Sets.TryGetValue(room.Set, out var sp) ? sp : new DecoSetPlan { Set = room.Set };
        Reserve();
        bool corridor = MathF.Min(room.Width, room.Depth) < 2.6f;

        // Pews and seat rows take the whole room, facing their focus.
        if (!corridor && mix.Island.Any(t => _arr.Templates[t.Template].Kind == "rows"))
            Rows(_arr.Templates[mix.Island.First(t => _arr.Templates[t.Template].Kind == "rows").Template]);

        if (!corridor) Walls(mix.Wall);
        if (!corridor) Islands(mix.Island.Where(t => _arr.Templates[t.Template].Kind != "rows").ToList());
        Fill(mix.Fill, corridor ? 0.25f : 0.45f);
        Access();
        return _plan;
    }

    // ---- piece choice ----------------------------------------------------------------------------

    bool Allowed(DecoPiece p) =>
        !p.Outdoor && (_room.Tech == null || p.Tech <= _room.Tech) &&
        (p.Mount == DecoMount.Ceiling || p.Height <= _room.Height - 0.3f || p.Mount == DecoMount.Wall && p.MountY + p.Height <= _room.Height - 0.1f);

    /// <summary>A piece of the room's set (or one it borrows) with one of the roles; the same one each time in a room, so rows match.</summary>
    DecoPiece Pick(string[] roles, string key = null, Func<DecoPiece, bool> extra = null)
    {
        if (roles.Length == 0) return null;
        key ??= string.Join(",", roles);
        if (_chosen.TryGetValue(key, out DecoPiece p) && (extra == null || extra(p)) && !Full(p)) return p;
        var own = _cat.OfSet(_room.Set).Where(x => roles.Any(x.Has) && Allowed(x) && (extra == null || extra(x)) && !Full(x)).ToList();
        var borrowed = _cat.InSet(_room.Set).Where(x => x.Set != _room.Set && roles.Any(x.Has) && Allowed(x) && (extra == null || extra(x)) && !Full(x)).ToList();
        // Pieces that play the first-named role come first.
        var all = own.Concat(borrowed).ToList();
        if (all.Count == 0) return null;
        var weights = all.Select(x => (x.Set == _room.Set ? 3f : 1f) * (x.Has(roles[0]) ? 2f : 1f) * (_room.Tech != null && x.Tech == _room.Tech ? 1.5f : 1f)).ToList();
        p = all[Math.Max(0, _rng.PickWeighted(weights))];
        _chosen[key] = p;
        return p;
    }

    // ---- templates in their own frame ------------------------------------------------------------

    List<Member> Instance(DecoTemplate t)
    {
        var m = new List<Member>();
        DecoPiece a = Pick(t.Anchor, t.Id + ":anchor");
        if (a == null) return null;
        m.Add(new Member { Piece = a, Role = a.Roles.First(t.Anchor.Contains) });
        switch (t.Kind)
        {
            case "around":
            {
                DecoPiece c = Pick(t.Members, t.Id + ":members");
                if (c == null) return m.Count > 0 && t.CountMin == 0 ? m : null;
                int want = _rng.Between(t.CountMin, t.CountMax);
                float cw = c.Width, gap = t.Gap;
                var sides = new List<(Vector2 dir, float len, float yaw, float off)>
                {
                    (new Vector2(0, -1), a.Width, 180, a.Depth / 2 + gap + c.Depth / 2),
                    (new Vector2(0, 1), a.Width, 0, a.Depth / 2 + gap + c.Depth / 2),
                    (new Vector2(-1, 0), a.Depth, 270, a.Width / 2 + gap + c.Depth / 2),
                    (new Vector2(1, 0), a.Depth, 90, a.Width / 2 + gap + c.Depth / 2),
                };
                int[] cap = sides.Select(s => s.len + 0.1f >= cw * 0.9f ? Math.Max(1, (int)((s.len + 0.15f) / (cw + 0.12f))) : 0).ToArray();
                int[] n = new int[4];
                for (int placed = 0, guard = 0; placed < want && guard < 40; guard++)
                {
                    int s = guard % 4;
                    if (n[s] < cap[s]) { n[s]++; placed++; }
                    if (n.Sum() >= cap.Sum()) break;
                }
                for (int s = 0; s < 4; s++)
                    for (int i = 0; i < n[s]; i++)
                    {
                        float along = -sides[s].len / 2 + sides[s].len / n[s] * (i + 0.5f);
                        Vector2 dir = sides[s].dir, tangent = new(-dir.Y, dir.X);
                        m.Add(new Member { Piece = c, At = dir * sides[s].off + tangent * along, Yaw = sides[s].yaw, Role = c.Roles.First(t.Members.Contains) });
                        DecoPiece tray = t.Top.Length > 0 && a.Surface > 0 && s < 2 && _rng.Chance(t.TopChance) ? Pick(t.Top, t.Id + ":top") : null;
                        if (tray != null)
                            m.Add(new Member { Piece = tray, At = dir * (a.Depth / 2 - tray.Depth / 2 - 0.03f) + tangent * along, Yaw = sides[s].yaw, Y = a.Surface, Support = 0, Role = "tray" });
                    }
                break;
            }
            case "station":
            {
                DecoPiece f = Pick(t.Front, t.Id + ":front");
                if (f != null) m.Add(new Member { Piece = f, At = new Vector2(0, -(a.Depth / 2 + 0.08f + f.Depth / 2)), Yaw = 180, Role = f.Roles.First(t.Front.Contains) });
                DecoPiece top = a.Surface > 0 ? Pick(t.Top, t.Id + ":top") : null;
                if (top != null && top.Width < a.Width && top.Depth < a.Depth)
                    m.Add(new Member { Piece = top, At = new Vector2(_rng.Range(-0.15f, 0.15f) * (a.Width - top.Width), a.Depth / 2 - top.Depth / 2 - 0.03f), Y = a.Surface, Support = 0, Role = "top" });
                DecoPiece side = _rng.Chance(0.6f) ? Pick(t.Beside, t.Id + ":beside", x => x.Mount == DecoMount.Floor) : null;
                if (side != null)
                {
                    float sx = (_rng.Chance(0.5f) ? 1 : -1) * (a.Width / 2 + 0.05f + side.Width / 2);
                    m.Add(new Member { Piece = side, At = new Vector2(sx, a.Depth / 2 - side.Depth / 2), Role = "beside" });
                }
                break;
            }
            case "bay":
            {
                float x = a.Width / 2 + 0.08f;
                DecoPiece b1 = Pick(t.Beside, t.Id + ":beside1", p => p.Mount == DecoMount.Floor && p.Depth <= 1f);
                if (b1 != null) m.Add(new Member { Piece = b1, At = new Vector2(x + b1.Width / 2, a.Depth / 2 - b1.Depth / 2), Role = "beside" });
                DecoPiece b2 = t.Beside.Length > 1 ? Pick(t.Beside.Reverse().ToArray(), t.Id + ":beside2", p => p.Mount == DecoMount.Floor && p.Depth <= 1f && p != b1) : null;
                float left = a.Width / 2 + 0.08f;
                if (b2 != null) { m.Add(new Member { Piece = b2, At = new Vector2(-(left + b2.Width / 2), a.Depth / 2 - b2.Depth / 2 - 0.15f), Role = "beside" }); left += b2.Width + 0.08f; }
                DecoPiece scr = Pick(t.Screen, t.Id + ":screen");
                if (scr != null) m.Add(new Member { Piece = scr, At = new Vector2(-(left + scr.Depth / 2), a.Depth / 2 - scr.Width / 2), Yaw = 90, Role = "curtain" });
                break;
            }
            case "row":
            case "line":
            {
                int count = _rng.Between(t.CountMin, t.CountMax);
                float step = a.Width + (t.Kind == "line" ? 0 : t.Spacing);
                m.Clear();
                for (int i = 0; i < count; i++)
                {
                    int idx = m.Count;
                    m.Add(new Member { Piece = a, At = new Vector2(i * step, 0), Role = "anchor" });
                    if (t.Kind != "line") continue;
                    DecoPiece stool = Pick(t.Front, t.Id + ":front");
                    if (stool != null)
                        for (float sx = -a.Width / 2 + 0.35f; sx < a.Width / 2 - 0.2f; sx += 0.7f)
                            m.Add(new Member { Piece = stool, At = new Vector2(i * step + sx, -(a.Depth / 2 + 0.12f + stool.Depth / 2)), Yaw = 180, Role = "stool" });
                    DecoPiece tray = a.Surface > 0 && _rng.Chance(t.TopChance) ? Pick(t.Top, t.Id + ":top") : null;
                    if (tray != null) m.Add(new Member { Piece = tray, At = new Vector2(i * step + _rng.Range(-0.4f, 0.4f) * a.Width, -a.Depth / 2 + tray.Depth / 2 + 0.05f), Y = a.Surface, Support = idx, Role = "tray" });
                }
                DecoPiece end = Pick(t.Ends, t.Id + ":ends", p => p.Mount == DecoMount.Floor);
                if (end != null)
                {
                    float back = a.Depth / 2;
                    m.Add(new Member { Piece = end, At = new Vector2((count - 1) * step + a.Width / 2 + 0.1f + end.Width / 2, back - end.Depth / 2), Role = "end" });
                }
                // Centre the row on its middle.
                float mid = (m.Min(x => x.At.X - x.Piece.Width / 2) + m.Max(x => x.At.X + x.Piece.Width / 2)) / 2;
                foreach (Member x in m) x.At.X -= mid;
                break;
            }
            case "cluster":
            {
                int count = _rng.Between(t.CountMin, t.CountMax);
                float x = 0;
                for (int i = 1; i < count; i++)
                {
                    DecoPiece more = _rng.Chance(0.5f) ? a : Pick(t.Anchor, t.Id + ":more" + i);
                    if (more == null) continue;
                    x += m[^1].Piece.Width / 2 + 0.06f + more.Width / 2;
                    m.Add(new Member { Piece = more, At = new Vector2(x, a.Depth / 2 - more.Depth / 2), Yaw = _rng.Range(-8, 8), Role = "more" });
                }
                float mid = x / 2;
                foreach (Member mm in m) mm.At.X -= mid;
                break;
            }
            case "conveyor":
            {
                int count = _rng.Between(t.CountMin, t.CountMax);
                m.Clear();
                DecoPiece robot = Pick(t.Side, t.Id + ":side");
                DecoPiece other = Pick(t.Other, t.Id + ":other");
                for (int i = 0; i < count; i++)
                {
                    m.Add(new Member { Piece = a, At = new Vector2(i * a.Width, 0), Role = "conveyor" });
                    if (robot != null && i % 2 == 0) m.Add(new Member { Piece = robot, At = new Vector2(i * a.Width, -(a.Depth / 2 + 0.15f + robot.Depth / 2)), Yaw = 180, Role = "robot" });
                    // Workstations alternate with the robots on the same side; the other side is the aisle.
                    if (other != null && i % 2 == 1) m.Add(new Member { Piece = other, At = new Vector2(i * a.Width, -(a.Depth / 2 + 0.15f + other.Depth / 2)), Yaw = 180, Role = "station" });
                }
                float mid = (count - 1) * a.Width / 2;
                foreach (Member mm in m) mm.At.X -= mid;
                break;
            }
        }
        return m;
    }

    // ---- placing a template use ------------------------------------------------------------------

    /// <summary>Places the members with the template's origin at (x, z) turned by yaw, if every piece fits and the doors stay joined.</summary>
    bool TryPlace(List<Member> members, float x, float z, float yaw, string template)
    {
        int group = _groups;
        int first = _plan.Placements.Count;
        // Never past a piece's cap in a room.
        foreach (var g in members.GroupBy(m => m.Piece))
            if (_count.GetValueOrDefault(g.Key.Id) + g.Count() > CapOf(g.Key)) return false;
        var added = new List<DecoPlacement>();
        foreach (Member mem in members)
        {
            Vector2 at = new Vector2(x, z) + DecoGeo.Yaw(mem.At, yaw);
            var pl = new DecoPlacement
            {
                Piece = mem.Piece.Id, State = DecoState.Intact, X = at.X, Z = at.Y, Yaw = Norm(yaw + mem.Yaw),
                Y = mem.Piece.Mount == DecoMount.Wall ? mem.Piece.MountY : mem.Piece.Mount == DecoMount.Ceiling ? _room.Height - mem.Piece.Height : mem.Y,
                Group = group, Role = mem.Role, Support = mem.Support >= 0 ? first + mem.Support : -1, Palette = mem.Piece.Palette,
            };
            added.Add(pl);
        }
        foreach (DecoPlacement pl in added)
        {
            DecoRect r = DecoLayout.Footprint(_forge, pl);
            if (!DecoLayout.Inside(_room, r)) return false;
            if (pl.Support < 0 && _reserved.Any(res => res.Overlaps(r.Shrink(0.01f))) && DecoLayout.Blocks(_forge, pl)) return false;
            if (_plan.Placements.Any(o => DecoLayout.Clash(_forge, o, pl))) return false;
            if (added.Any(o => o != pl && added.IndexOf(o) < added.IndexOf(pl) && DecoLayout.Clash(_forge, o, pl))) return false;
        }
        foreach (DecoPlacement pl in added.Where(p => DecoLayout.Blocks(_forge, p))) _raster.Add(DecoLayout.Footprint(_forge, pl), 1);
        if (!_raster.DoorsJoined(_room))
        {
            foreach (DecoPlacement pl in added.Where(p => DecoLayout.Blocks(_forge, p))) _raster.Add(DecoLayout.Footprint(_forge, pl), -1);
            return false;
        }
        _plan.Placements.AddRange(added);
        foreach (DecoPlacement pl in added) _count[pl.Piece] = _count.GetValueOrDefault(pl.Piece) + 1;
        _groups++;
        return true;
    }

    static float Norm(float yaw) => ((yaw % 360) + 360) % 360;

    static (Vector2 min, Vector2 max) Extent(List<Member> m)
    {
        Vector2 lo = new(float.MaxValue), hi = new(float.MinValue);
        foreach (Member x in m)
        {
            var r = DecoLayout.RectOf(new Vector3(-x.Piece.Width / 2, 0, -x.Piece.Depth / 2), new Vector3(x.Piece.Width / 2, 0, x.Piece.Depth / 2), x.At.X, x.At.Y, x.Yaw);
            var (a, b) = r.Bounds();
            lo = Vector2.Min(lo, a); hi = Vector2.Max(hi, b);
        }
        return (lo, hi);
    }

    // ---- walls: templates packed in rows along each wall -----------------------------------------

    static float WallYaw(DecoWall w) => w switch { DecoWall.North => 180, DecoWall.South => 0, DecoWall.West => 270, _ => 90 };

    float WallLength(DecoWall w) => w is DecoWall.North or DecoWall.South ? _room.Width : _room.Depth;

    /// <summary>The template's origin for a spot s along wall w, with its back (local +z) against the wall.</summary>
    (float x, float z) WallOrigin(DecoWall w, float s, float back) => w switch
    {
        DecoWall.North => (_room.Width - s, back),
        DecoWall.South => (s, _room.Depth - back),
        DecoWall.West => (back, s),
        _ => (_room.Width - back, _room.Depth - s),
    };

    void Walls(List<(string Template, float Weight)> mix)
    {
        if (mix.Count == 0) return;
        var weights = mix.Select(t => t.Weight).ToList();
        var walls = new List<DecoWall> { DecoWall.North, DecoWall.South, DecoWall.West, DecoWall.East };
        _rng.Shuffle(walls);
        int failures = 0;
        var uses = new int[mix.Count];
        for (int tries = 0; tries < 40 && failures < 8; tries++)
        {
            int pick = _rng.PickWeighted(weights);
            if (pick < 0) break;
            DecoTemplate t = _arr.Templates[mix[pick].Template];
            // A template with a most uses (a galley, an altar) stops at it.
            if (t.Max > 0 && uses[pick] >= t.Max) { weights[pick] = 0; continue; }
            // Each use picks its pieces afresh more often than not: a wall of desks isn't one desk again and again.
            if (_rng.Chance(0.6f)) Repick(t);
            List<Member> inst = Instance(t);
            if (inst == null) { weights[pick] = 0; failures++; continue; }
            var (lo, hi) = Extent(inst);
            bool placed = false;
            foreach (DecoWall w in walls)
            {
                float len = WallLength(w), half = (hi.X - lo.X) / 2;
                // Anywhere along the wall it fits, not packed from one end: the gaps between come out uneven.
                var along = new List<float>();
                for (float s = half + 0.05f; s <= len - half - 0.05f; s += Cell) along.Add(s);
                _rng.Shuffle(along);
                foreach (float s in along)
                {
                    var (x, z) = WallOrigin(w, s - (lo.X + hi.X) / 2, hi.Y);
                    if (placed = TryPlace(inst, x, z, WallYaw(w), t.Id)) break;
                }
                if (placed) break;
            }
            if (!placed) { weights[pick] *= 0.3f; failures++; }
            else uses[pick]++;
        }
    }

    // ---- islands: templates on a lattice out in the room ------------------------------------------

    void Islands(List<(string Template, float Weight)> mix)
    {
        if (mix.Count == 0) return;
        var weights = mix.Select(t => t.Weight).ToList();
        for (int tries = 0, failures = 0; tries < 30 && failures < 6; tries++)
        {
            int pick = _rng.PickWeighted(weights);
            if (pick < 0) break;
            DecoTemplate t = _arr.Templates[mix[pick].Template];
            Repick(t);
            List<Member> inst = Instance(t);
            if (inst == null) { weights[pick] = 0; failures++; continue; }
            var (lo, hi) = Extent(inst);
            Vector2 size = hi - lo, centre = (lo + hi) / 2;
            // Off the lattice by a little, and turned a little, each one: not stamped out in ranks.
            bool lineUp = t.Kind is "row" or "line" or "conveyor" or "rows";
            float jitter = MathF.Min(0.3f, 0.28f), turn = lineUp ? 3f : 12f;
            bool across = size.X > size.Y != _room.Width > _room.Depth;
            float yaw = across ? 90 : 0;
            if (_rng.Chance(0.5f)) yaw += 180;
            Vector2 world = across ? new Vector2(size.Y, size.X) : size;
            float aisle = 1.1f;
            int nx = Math.Max(1, (int)((_room.Width - 1.2f + aisle) / (world.X + aisle))), nz = Math.Max(1, (int)((_room.Depth - 1.2f + aisle) / (world.Y + aisle)));
            float x0 = (_room.Width - (nx * world.X + (nx - 1) * aisle)) / 2 + world.X / 2, z0 = (_room.Depth - (nz * world.Y + (nz - 1) * aisle)) / 2 + world.Y / 2;
            var spots = new List<Vector2>();
            for (int j = 0; j < nz; j++) for (int i = 0; i < nx; i++) spots.Add(new Vector2(x0 + i * (world.X + aisle), z0 + j * (world.Y + aisle)));
            if (t.Max > 0) _rng.Shuffle(spots);
            int placed = 0;
            foreach (Vector2 spot in spots)
            {
                if (t.Max > 0 && placed >= t.Max) break;
                float y1 = yaw + _rng.Range(-turn, turn);
                Vector2 off = new(_rng.Range(-jitter, jitter), _rng.Range(-jitter, jitter));
                Vector2 origin = spot + off - DecoGeo.Yaw(centre, y1);
                if (TryPlace(inst, origin.X, origin.Y, y1, t.Id) || TryPlace(inst, spot.X - DecoGeo.Yaw(centre, yaw).X, spot.Y - DecoGeo.Yaw(centre, yaw).Y, yaw, t.Id))
                {
                    placed++;
                    // The next spot's pieces picked afresh: a different one where the set has it.
                    Repick(t);
                    inst = Instance(t) ?? inst;
                    (lo, hi) = Extent(inst);
                    centre = (lo + hi) / 2;
                }
            }
            // The lattice found room for fewer than two (the walkway runs through its middle): try spots off it, slid towards the walls.
            // Every fourth try takes a fresh instance (a shorter line, perhaps); the tries go round all four ways.
            for (int k = 0; placed < (t.Max > 0 ? Math.Min(2, t.Max) : 2) && k < 24; k++)
            {
                if (k > 0 && k % 4 == 0)
                {
                    inst = Instance(t) ?? inst;
                    (lo, hi) = Extent(inst);
                    size = hi - lo;
                    centre = (lo + hi) / 2;
                }
                float y2 = yaw + 90 * (k % 4);
                Vector2 w2 = across != (k % 2 == 1) ? new Vector2(size.Y, size.X) : size;
                float hx = MathF.Max(0, (_room.Width - w2.X) / 2 - 0.3f), hz = MathF.Max(0, (_room.Depth - w2.Y) / 2 - 0.3f);
                var spot = new Vector2(_room.Width / 2 + _rng.Range(-hx, hx), _room.Depth / 2 + _rng.Range(-hz, hz));
                Vector2 origin = spot - DecoGeo.Yaw(centre, y2);
                if (TryPlace(inst, origin.X, origin.Y, y2, t.Id)) { placed++; inst = Instance(t) ?? inst; (lo, hi) = Extent(inst); size = hi - lo; centre = (lo + hi) / 2; }
            }
            weights[pick] = 0;
            if (placed == 0) failures++;
        }
    }

    // ---- seat rows facing a focus -------------------------------------------------------------------

    void Rows(DecoTemplate t)
    {
        DecoPiece seat = Pick(t.Anchor, t.Id + ":anchor");
        if (seat == null) return;
        // Facing away from the way in; with none, down the room's length (an aircraft's cabin, a long hall).
        DecoDoor door = _room.Doors.Count > 0 ? _room.Doors[0] : _room.Depth >= _room.Width ? new DecoDoor(DecoWall.North, _room.Width / 2, 1.2f) : new DecoDoor(DecoWall.West, _room.Depth / 2, 1.2f);
        DecoWall focusWall = door.Wall switch { DecoWall.North => DecoWall.South, DecoWall.South => DecoWall.North, DecoWall.West => DecoWall.East, _ => DecoWall.West };
        float len = WallLength(focusWall), depth = focusWall is DecoWall.North or DecoWall.South ? _room.Depth : _room.Width;
        DecoPiece focus = Pick(t.Focus, t.Id + ":focus", p => p.Mount == DecoMount.Floor);
        float start = 0.3f;
        if (focus != null)
        {
            var (fx, fz) = WallOrigin(focusWall, len / 2, focus.Depth / 2 + 0.3f);
            var fm = new List<Member> { new() { Piece = focus, Role = "focus" } };
            if (TryPlace(fm, fx, fz, WallYaw(focusWall), t.Id)) start = focus.Depth + 1.4f;
        }
        float aisle = t.Aisle, step = seat.Depth + 0.5f;
        int perSide = Math.Max(1, (int)((len / 2 - aisle / 2 - 0.5f) / (seat.Width + 0.05f)));
        for (float d = start + seat.Depth / 2; d < depth - 2.2f; d += step + _rng.Range(-0.05f, 0.15f))
        {
            // Each seat a little out of true, as people left them; a gap where one went.
            var row = new List<Member>();
            for (int i = 0; i < perSide; i++)
                foreach (float s in new[] { -1f, 1f })
                {
                    if (perSide > 1 && _rng.Chance(0.12f)) continue;
                    row.Add(new Member { Piece = seat, At = new Vector2(s * (aisle / 2 + seat.Width / 2 + i * (seat.Width + 0.05f)) + _rng.Range(-0.02f, 0.02f), _rng.Range(-0.05f, 0.05f)), Yaw = _rng.Range(-2.5f, 2.5f), Role = "seat" });
                }
            var (x, z) = WallOrigin(focusWall, len / 2, d);
            if (!TryPlace(row, x, z, WallYaw(focusWall), t.Id) && Pick(t.Anchor, t.Id + ":anchor") is { } other) seat = other;
        }
    }

    // ---- filling the walls between ---------------------------------------------------------------

    void Fill(string[] roles, float share)
    {
        if (roles.Length == 0) return;
        foreach (DecoWall w in new[] { DecoWall.North, DecoWall.East, DecoWall.South, DecoWall.West })
        {
            float len = WallLength(w);
            for (float s = 0.35f; s < len - 0.3f; s += 0.5f)
            {
                if (!_rng.Chance(share)) continue;
                string role = roles[_rng.Int(roles.Length)];
                DecoPiece p = Pick(new[] { role }, "fill:" + role + ":" + _rng.Int(2));
                if (p == null || p.Mount == DecoMount.Ceiling || p.Mount == DecoMount.Top) continue;
                var mem = new List<Member> { new() { Piece = p, Role = role } };
                var (x, z) = WallOrigin(w, s + p.Width / 2, p.Depth / 2 + 0.02f);
                // Set down a little out of true where there's room for it.
                var (x2, z2) = WallOrigin(w, s + p.Width / 2, p.Depth / 2 + 0.08f);
                if (TryPlace(mem, x2, z2, WallYaw(w) + _rng.Range(-6f, 6f), "fill") || TryPlace(mem, x, z, WallYaw(w), "fill")) s += p.Width + _rng.Range(0f, 0.4f);
            }
        }
    }

    // ---- reserving doorways and the walkway --------------------------------------------------------

    void Reserve()
    {
        _reserved = new List<DecoRect>();
        float w = _room.Agent + 0.35f;
        foreach (DecoDoor d in _room.Doors) _reserved.Add(_room.Apron(d, 1.2f, 0.2f));
        _reserved.AddRange(_room.Blocked);
        if (_room.Doors.Count == 0) return;
        var (p0, i0) = _room.DoorPoint(_room.Doors[0]);
        Vector2 a = p0 + i0 * 0.6f;
        var targets = _room.Doors.Skip(1).Select(d => { var (p, i) = _room.DoorPoint(d); return p + i * 0.6f; }).ToList();
        if (targets.Count == 0) targets.Add(new Vector2(_room.Width / 2, _room.Depth / 2));
        foreach (Vector2 b in targets)
        {
            // An L: along the first door's axis, then across.
            Vector2 corner = MathF.Abs(i0.X) > 0.5f ? new Vector2(b.X, a.Y) : new Vector2(a.X, b.Y);
            _reserved.Add(Segment(a, corner, w));
            _reserved.Add(Segment(corner, b, w));
        }
    }

    static DecoRect Segment(Vector2 a, Vector2 b, float width)
    {
        Vector2 d = b - a;
        float len = d.Length();
        Vector2 u = len < 1e-4f ? new Vector2(width / 2, 0) : d / len * (len / 2 + width / 2);
        Vector2 v = len < 1e-4f ? new Vector2(0, width / 2) : new Vector2(-d.Y, d.X) / len * (width / 2);
        return new DecoRect((a + b) / 2, u, v);
    }

    // ---- everything must be reachable -----------------------------------------------------------------

    /// <summary>Drops lone fill pieces nobody could walk up to (walled in behind tables).</summary>
    void Access()
    {
        if (_room.Doors.Count == 0) return;
        const int k = 2;
        bool[] fit = _raster.Fits(k);
        bool[] reach = _raster.Reach(_room.Doors.SelectMany(d => _raster.DoorStarts(_room, d, k)), k, fit);
        bool Reached(Vector2 p)
        {
            int i = (int)MathF.Floor(p.X / Cell) - 1, j = (int)MathF.Floor(p.Y / Cell) - 1;
            for (int dj = 0; dj <= 1; dj++)
                for (int di = 0; di <= 1; di++)
                {
                    int ci = i + di, cj = j + dj;
                    if (ci >= 0 && cj >= 0 && ci < _raster.Nx && cj < _raster.Nz && reach[cj * _raster.Nx + ci]) return true;
                }
            return false;
        }
        for (int n = _plan.Placements.Count - 1; n >= 0; n--)
        {
            DecoPlacement pl = _plan.Placements[n];
            if (pl.Support >= 0 || !DecoLayout.Blocks(_forge, pl)) continue;
            DecoRect r = DecoLayout.Footprint(_forge, pl);
            Vector2 nu = Vector2.Normalize(r.U), nv = Vector2.Normalize(r.V);
            bool ok = new[] { r.Centre + r.U + nu * 0.3f, r.Centre - r.U - nu * 0.3f, r.Centre + r.V + nv * 0.3f, r.Centre - r.V - nv * 0.3f }.Any(Reached);
            if (ok || _plan.Placements.Count(p => p.Group == pl.Group) > 1) continue;
            _raster.Add(r, -1);
            _plan.Placements.RemoveAt(n);
            foreach (DecoPlacement other in _plan.Placements) if (other.Support > n) other.Support--;
        }
    }
}
