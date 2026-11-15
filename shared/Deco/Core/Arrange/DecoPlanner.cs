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
    DecoRaster _raster, _walk;
    /// <summary>Each two doors' walk line through the bare room, raster steps for the walk line's walker (-1: none).</summary>
    int[,] _routes;
    /// <summary>The one wall a narrow room is lined along (null: a room wide enough to furnish all round).</summary>
    DecoWall? _lining;
    DecoRng _rng;
    List<DecoRect> _reserved;
    /// <summary>How many of <see cref="_reserved"/> are the doorways' aprons and what the floor can't take (the rest: the walkways).</summary>
    int _aprons;
    int _groups;
    readonly Dictionary<string, DecoPiece> _chosen = new();

    public DecoPlanner(DecoForge forge, DecoArrangements arr)
    {
        _forge = forge;
        _arr = arr;
        _cat = forge.Catalogue;
    }

    public const float Cell = 0.25f;

    /// <summary>The walk line's walker, raster cells across (0.75 m), and how much longer furniture may make a line (raster steps: a metre).</summary>
    const int Walker = 3, Slack = 4;

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
    /// surface a few more, anything else four. A room is never one piece over and over; a big one holds as many
    /// again for every 40 m² of it (a ward's beds, a hall's tables).
    /// </summary>
    public static int CapOf(DecoPiece p, float area = 0f) =>
        (p.Roles.Any(r => r is "seat-row" or "pew" or "bench-seat" or "airline-seat" or "seat" or "chair" or "stool") ? 16 : p.Mount == DecoMount.Top ? 5 : 3) * Math.Max(1, (int)(area / 40f));

    readonly Dictionary<string, int> _count = new();

    bool Full(DecoPiece p) => _count.GetValueOrDefault(p.Id) >= CapOf(p, _room.Width * _room.Depth);

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
        // What its floor can't take stands in the way like furniture, so nothing set down shuts the only way round it (unless
        // it splits the room already: then the planner can't tell a way through, and only keeps clear of it).
        foreach (DecoRect r in room.Blocked) _raster.Add(r, 1);
        if (room.Blocked.Count > 0 && !_raster.DoorsJoined(room)) foreach (DecoRect r in room.Blocked) _raster.Add(r, -1);
        // The walk lines between its doors as they run through the bare room: nothing set down may lengthen one by more than a metre.
        _walk = new DecoRaster(room.Width, room.Depth, Cell);
        foreach (DecoRect r in room.Blocked) _walk.Add(r, 1);
        _routes = Routes();
        // A room under 2.5 m across is lined along one wall (DecoRules.Lining), never down its middle. One with nothing named to
        // be arranged round, or whose focal piece won't stand there with the walk past it, is a way through or a store: a few
        // things along that wall.
        string[] focus = _arr.FocusFor(room.RoomId, room.Recipe);
        _lining = DecoRules.Lining(room);
        bool corridor = _lining != null && focus == null;
        Supports(mix.Fill);

        // Pews and seat rows take the whole room, facing their focus.
        if (!corridor && _lining == null && mix.Island.Any(t => _arr.Templates[t.Template].Kind == "rows"))
            Rows(_arr.Templates[mix.Island.First(t => _arr.Templates[t.Template].Kind == "rows").Template]);

        // One focal point a room: once it stands, the other ways of making the same one (a single bed where a double went) are
        // off the walls and the floor; the rest of what the room is arranged round (a bathroom's basin and WC beside its bath) still
        // come, and so does more of what repeats (a ward's beds, a mess hall's tables, an office's desks).
        string first = corridor || focus == null ? null : Focal(mix.Wall.Concat(mix.Island).ToList(), focus);
        corridor |= _lining != null && first == null;
        if (first != null)
        {
            var made = FocusOf(_arr.Templates[first], focus);
            List<(string, float)> Off(List<(string Template, float Weight)> list) =>
                list.Select(t => _arr.Templates[t.Template].Max == 1 && FocusOf(_arr.Templates[t.Template], focus).Overlaps(made) ? (t.Template, 0f) : t).ToList();
            mix = new DecoSetPlan { Set = mix.Set, Wall = Off(mix.Wall), Island = Off(mix.Island), Zone = mix.Zone, Divider = mix.Divider, Fill = mix.Fill, Light = mix.Light };
        }
        // A big room's second zone before the rest fills it.
        if (!corridor && _lining == null) Zones(mix);
        if (!corridor) Walls(mix.Wall);
        if (!corridor && _lining == null) Islands(mix.Island.Where(t => _arr.Templates[t.Template].Kind != "rows").ToList());
        Fill(mix.Fill, corridor ? 0.25f : 0.45f);
        Ceiling(mix.Light);
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
        // A support (a mine's timber set) only ever spans a way, set by Supports: never a wall piece, a fill or a row.
        bool span = key == SupportKey;
        var own = _cat.OfSet(_room.Set).Where(x => roles.Any(x.Has) && x.Has("support") == span && Allowed(x) && (extra == null || extra(x)) && !Full(x)).ToList();
        var borrowed = _cat.InSet(_room.Set).Where(x => x.Set != _room.Set && roles.Any(x.Has) && x.Has("support") == span && Allowed(x) && (extra == null || extra(x)) && !Full(x)).ToList();
        // Pieces that play the first-named role come first.
        var all = own.Concat(borrowed).ToList();
        if (all.Count == 0) return null;
        // The room's own era first by far: a high world's room takes its high pieces where it has them, older ones only to fill in.
        var weights = all.Select(x => (x.Set == _room.Set ? 3f : 1f) * (x.Has(roles[0]) ? 2f : 1f) * Era(x)).ToList();
        p = all[Math.Max(0, _rng.PickWeighted(weights))];
        _chosen[key] = p;
        return p;
    }

    /// <summary>How much a piece suits the room's era: its own era four times over, one era older at par, two older a little.</summary>
    float Era(DecoPiece x) => _room.Tech is not DecoTech t ? 1f : x.Tech == t ? 4f : (int)t - (int)x.Tech == 1 ? 1f : 0.3f;

    // ---- templates in their own frame ------------------------------------------------------------

    /// <param name="length">A row's or line's count of anchors, or -1 for a roll of its count.</param>
    /// <param name="anchor">What its anchor must be, if anything (what the room is arranged round: a shelf, not a cabinet, in a store).</param>
    List<Member> Instance(DecoTemplate t, int length = -1, Func<DecoPiece, bool> anchor = null)
    {
        var m = new List<Member>();
        DecoPiece a = Pick(t.Anchor, t.Id + ":anchor", anchor);
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
                // Longer than every side (a sofa at a coffee table): one each side of its length, facing across it, none at its ends.
                if (cap.Sum() == 0) cap = a.Width >= a.Depth ? new[] { 1, 1, 0, 0 } : new[] { 0, 0, 1, 1 };
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
                        // Chairs as they were left: a little out, a little turned.
                        m.Add(new Member { Piece = c, At = dir * (sides[s].off + _rng.Range(0f, 0.1f)) + tangent * (along + _rng.Range(-0.04f, 0.04f)), Yaw = sides[s].yaw + _rng.Range(-8f, 8f), Role = c.Roles.First(t.Members.Contains) });
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
                int count = length >= 0 ? length : _rng.Between(t.CountMin, t.CountMax);
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
                            // Each stool as it was last pushed in or out, turned a little.
                            m.Add(new Member { Piece = stool, At = new Vector2(i * step + sx + _rng.Range(-0.08f, 0.08f), -(a.Depth / 2 + 0.12f + stool.Depth / 2) - _rng.Range(0f, 0.12f)), Yaw = 180 + _rng.Range(-15f, 15f), Role = "stool" });
                    DecoPiece tray = a.Surface > 0 && _rng.Chance(t.TopChance) ? Pick(t.Top, t.Id + ":top") : null;
                    if (tray != null) m.Add(new Member { Piece = tray, At = new Vector2(i * step + _rng.Range(-0.4f, 0.4f) * a.Width, -a.Depth / 2 + tray.Depth / 2 + 0.05f), Y = a.Surface, Support = idx, Role = "tray" });
                }
                // A line's middle piece (a sink in a worktop run), as wide as the rest.
                var anchors = m.Where(x => x.Role == "anchor").ToList();
                if (t.Middle.Length > 0 && anchors.Count >= 2 && Pick(t.Middle, t.Id + ":middle", p => p.Mount == DecoMount.Floor && MathF.Abs(p.Width - a.Width) < 0.01f) is { } middle)
                {
                    Member at = anchors[(anchors.Count - 1) / 2];
                    at.Piece = middle;
                    at.Role = middle.Roles.First(t.Middle.Contains);
                }
                DecoPiece end = Pick(t.Ends, t.Id + ":ends", p => p.Mount == DecoMount.Floor);
                if (end != null)
                {
                    float back = a.Depth / 2;
                    m.Add(new Member { Piece = end, At = new Vector2((count - 1) * step + a.Width / 2 + 0.1f + end.Width / 2, back - end.Depth / 2), Role = "end" });
                }
                if (m.Count == 0) return null; // (a line of none and no end: nothing)
                // Back to back with its mirror: a bank of desks facing each other, a range of shelves open both sides.
                if (t.Double)
                {
                    int n = m.Count;
                    for (int i = 0; i < n; i++)
                        m.Add(new Member { Piece = m[i].Piece, At = new Vector2(m[i].At.X, a.Depth - m[i].At.Y), Yaw = m[i].Yaw + 180, Y = m[i].Y, Support = m[i].Support >= 0 ? m[i].Support + n : -1, Role = m[i].Role });
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

    /// <summary>
    /// Places the members with the template's origin at (x, z) turned by yaw, if every piece fits, the doors stay joined and no
    /// walk line between two doors grows more than a metre. <paramref name="loose"/>: across the walkway and the aprons kept for those
    /// lines (never in a doorway), so long as the lines themselves hold: what a small room is arranged round, where nothing else fits.
    /// </summary>
    bool TryPlace(List<Member> members, float x, float z, float yaw, string template, bool loose = false, bool focal = false)
    {
        int group = _groups;
        int first = _plan.Placements.Count;
        // Never past a piece's cap in a room.
        foreach (var g in members.GroupBy(m => m.Piece))
            if (_count.GetValueOrDefault(g.Key.Id) + g.Count() > CapOf(g.Key, _room.Width * _room.Depth)) return false;
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
            if (pl.Support < 0 && (loose ? _reserved.Skip(_room.Doors.Count).Take(_aprons - _room.Doors.Count) : _reserved).Any(res => res.Overlaps(r.Shrink(0.01f))) && DecoLayout.Blocks(_forge, pl)) return false;
            // Nothing lies in a doorway either, not even a rug a person walks over.
            if (pl.Support < 0 && _room.Doors.Any(d => _room.Apron(d, 0.9f, 0).Overlaps(r.Shrink(0.02f)))) return false;
            if (_plan.Placements.Any(o => DecoLayout.Clash(_forge, o, pl))) return false;
            if (added.Any(o => o != pl && added.IndexOf(o) < added.IndexOf(pl) && DecoLayout.Clash(_forge, o, pl))) return false;
            // The room's rules (DecoRules): nothing taller than a sill before a window, a fire only at a chimney breast, what the room
            // is arranged round on a solid wall, a narrow room lined along its one wall.
            if (DecoRules.BeforeWindow(_forge, _room, pl) || DecoRules.OffFlue(_forge, _room, pl)) return false;
            // (Alone, under a window's sill if it is low enough.)
            bool behind = focal && DecoRules.OpeningBehind(_room, r, loose ? DecoLayout.Heights(_forge, pl).Hi : float.MaxValue);
            if (pl.Support < 0 && DecoLayout.Blocks(_forge, pl) && (behind || _lining is DecoWall only && DecoRules.Off(_room, r, only) > 0.15f)) return false;
        }
        // No more than half the floor stood on, and a clear walk to every piece's use side.
        var all = _plan.Placements.Concat(added).ToList();
        if (DecoRules.Share(_forge, _room, all) > DecoRules.MostFloor || DecoRules.Unreached(_forge, _room, all).Count > 0) return false;
        var blocking = added.Where(p => DecoLayout.Blocks(_forge, p)).Select(p => DecoLayout.Footprint(_forge, p)).ToList();
        foreach (DecoRect r in blocking) { _raster.Add(r, 1); _walk.Add(r, 1); }
        if (!_raster.DoorsJoined(_room) || !Lines())
        {
            foreach (DecoRect r in blocking) { _raster.Add(r, -1); _walk.Add(r, -1); }
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

    /// <summary>The focus roles a template's pieces in this room's set play (a double bed's bay: bed): empty if it makes no focal point.</summary>
    HashSet<string> FocusOf(DecoTemplate t, string[] focus)
    {
        var roles = t.Anchor.Concat(t.Ends).Concat(t.Middle).Concat(t.Members).ToHashSet();
        return _cat.InSet(_room.Set).Where(p => p.Roles.Any(roles.Contains)).SelectMany(p => p.Roles).Where(focus.Contains).ToHashSet();
    }

    /// <summary>
    /// What the room is arranged round goes first (planning practice: the hearth, the cooker's worktop run, the bed's
    /// head), on the wall a person faces coming in by the first doorway, else the side walls, longest first; centred on
    /// it where it fits. One that stands out in the room (a map table, an operating table, a ward's bank of desks) stands
    /// in its middle, along its length. Where none fits whole (a small room with two doors), the piece it is arranged round
    /// goes alone, against any wall, across the walkway kept for the room's walk lines if it must, so long as those lines hold.
    /// Returns the template it placed, or null.
    /// </summary>
    string Focal(List<(string Template, float Weight)> mix, string[] focus)
    {
        foreach (bool alone in new[] { false, true })
        foreach (var (id, _) in mix.OrderByDescending(t => t.Weight))
        {
            DecoTemplate t = _arr.Templates[id];
            // (Seat rows are laid by Rows, facing their own focus.)
            if (t.Kind == "rows" || FocusOf(t, focus).Count == 0) continue;
            DecoWall entry = _room.Doors.Count > 0 ? _room.Doors[0].Wall : DecoWall.South;
            DecoWall facing = entry switch { DecoWall.North => DecoWall.South, DecoWall.South => DecoWall.North, DecoWall.West => DecoWall.East, _ => DecoWall.West };
            var walls = new List<DecoWall> { facing };
            walls.AddRange(new[] { DecoWall.North, DecoWall.South, DecoWall.West, DecoWall.East }.Where(w => w != facing && w != entry).OrderByDescending(WallLength));
            walls.Add(entry);
            // A row or line as long as it goes, then shorter till it fits the room (a worktop run of three units, else two, else one).
            bool lengthens = t.Kind is "row" or "line";
            int most = lengthens ? t.CountMax : -1;
            for (int count = most; count >= (lengthens ? t.CountMin : -1) && (!alone || count == most); count--)
            {
                // (Its anchor what the room is arranged round, where the pick made it something else.)
                List<Member> inst = Instance(t, count);
                if (inst != null && !inst.Any(m => m.Piece.Roles.Any(focus.Contains))) inst = Instance(t, count, p => p.Roles.Any(focus.Contains));
                if (inst == null || !inst.Any(m => m.Piece.Roles.Any(focus.Contains))) break;
                if (alone) inst = inst.Where(m => m.Piece.Roles.Any(focus.Contains)).Take(1).Select(m => new Member { Piece = m.Piece, Yaw = m.Yaw, Role = m.Role }).ToList();
                var (lo, hi) = Extent(inst);
                if (!t.OnWall)
                {
                    // Out in the room: its middle on the room's, then a little off it, its long side along the room's.
                    Vector2 centre = (lo + hi) / 2, mid = new(_room.Width / 2, _room.Depth / 2);
                    float yaw = (hi.X - lo.X >= hi.Y - lo.Y) == (_room.Width >= _room.Depth) ? 0 : 90;
                    var spots = new List<Vector2>();
                    for (float dz = -1.5f; dz <= 1.5f; dz += 0.5f)
                        for (float dx = -1.5f; dx <= 1.5f; dx += 0.5f) spots.Add(mid + new Vector2(dx, dz));
                    foreach (Vector2 s in spots.OrderBy(s => Vector2.Distance(s, mid)))
                        foreach (float y in new[] { yaw, yaw + 180 })
                        {
                            Vector2 o = s - DecoGeo.Yaw(centre, y);
                            if (TryPlace(inst, o.X, o.Y, y, t.Id, alone, focal: true)) { Focused(t, y); return t.Id; }
                        }
                    // (Alone, it may stand against a wall instead: a sofa with its back to one.)
                    if (!alone) continue;
                }
                foreach (DecoWall w in walls)
                {
                    float len = WallLength(w), half = (hi.X - lo.X) / 2;
                    var along = new List<float>();
                    for (float s = half + 0.05f; s <= len - half - 0.05f; s += Cell) along.Add(s);
                    foreach (float s in along.OrderBy(s => MathF.Abs(s - len / 2)))
                    {
                        var (x, z) = WallOrigin(w, s - (lo.X + hi.X) / 2, hi.Y);
                        if (TryPlace(inst, x, z, WallYaw(w), t.Id, alone, focal: true)) { Focused(t, WallYaw(w)); return t.Id; }
                    }
                }
            }
        }
        return null;
    }

    /// <summary>The template use just placed is what the room is arranged round: the years leave it standing; a rug under it where the template lays one.</summary>
    void Focused(DecoTemplate t, float yaw)
    {
        _plan.FocalGroup = _groups - 1;
        Under(t, yaw);
    }

    /// <summary>A rug under the template use just placed, centred on its anchor (a lounge's, a dining table's): it marks the group's zone of the floor.</summary>
    void Under(DecoTemplate t, float yaw)
    {
        if (t.Under.Length == 0 || Pick(t.Under, t.Id + ":under", p => p.Mount == DecoMount.Floor && p.WalkOver) is not { } rug) return;
        DecoPlacement anchor = _plan.Placements.First(p => p.Group == _groups - 1);
        TryPlace(new List<Member> { new() { Piece = rug, Role = "under" } }, anchor.X, anchor.Z, yaw, t.Id);
    }

    /// <summary>A room of this many square metres or more is big: it gets a second zone.</summary>
    public const float ZoneArea = 24f;

    /// <summary>
    /// A big room's second zone (planning practice: a living room's seats by the hearth and its table by the window; an open
    /// office's desks and its meeting table): one of the recipe's zone templates in the half of the room away from what it is
    /// arranged round, two in a hall, and a divider between them where one stands clear of the way through.
    /// </summary>
    void Zones(DecoSetPlan mix)
    {
        float area = _room.Width * _room.Depth;
        if (mix.Zone.Count == 0 || area < ZoneArea) return;
        Vector2 mid = new(_room.Width / 2, _room.Depth / 2);
        var focal = _plan.Placements.Where(p => p.Group == _plan.FocalGroup).ToList();
        Vector2 from = focal.Count > 0 ? new Vector2(focal.Average(p => p.X), focal.Average(p => p.Z)) : _room.Doors.Count > 0 ? _room.DoorPoint(_room.Doors[0]).At : mid;
        Vector2 away = mid - from;
        away = away.LengthSquared() < 0.25f ? (_room.Width >= _room.Depth ? Vector2.UnitX : Vector2.UnitY) : Vector2.Normalize(away);
        int first = _plan.Placements.Count, most = Math.Min(4, 1 + (int)(area / 60f));
        // One of its zone templates, by weight (a hall takes one more for every 60 m²), the next where it found no room; anywhere
        // in the room if its far half had none.
        foreach (Func<Vector2, bool> where in new Func<Vector2, bool>[] { s => Vector2.Dot(s - mid, away) >= 0.25f, _ => true })
        {
            var weights = mix.Zone.Select(t => t.Weight).ToList();
            for (int pick; _plan.Placements.Count == first && (pick = _rng.PickWeighted(weights)) >= 0; weights[pick] = 0)
                Islands(new List<(string, float)> { mix.Zone[pick] }, where, most);
        }
        var zone = _plan.Placements.Skip(first).Where(p => p.Support < 0).ToList();
        if (zone.Count == 0 || mix.Divider.Length == 0) return;
        Vector2 to = new(zone.Average(p => p.X), zone.Average(p => p.Z)), across = Vector2.Normalize(new Vector2(-(to - from).Y, (to - from).X) + new Vector2(1e-4f, 0));
        if (Pick(mix.Divider, "divider", p => p.Mount == DecoMount.Floor) is not { } div) return;
        float yaw = MathF.Abs(across.X) >= MathF.Abs(across.Y) ? 0 : 90;
        Vector2 at = (from + to) / 2;
        for (float k = 0; k <= MathF.Max(_room.Width, _room.Depth) / 2; k += 0.5f)
            foreach (float s in k == 0 ? new[] { 0f } : new[] { k, -k })
                if (TryPlace(new List<Member> { new() { Piece = div, Role = "divider" } }, at.X + across.X * s, at.Y + across.Y * s, yaw, "divider")) return;
    }

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

    /// <param name="where">Where a use's middle may go (a zone's half of the room), or anywhere.</param>
    /// <param name="most">The most uses of each template (0: its own most, or as many as fit).</param>
    void Islands(List<(string Template, float Weight)> mix, Func<Vector2, bool> where = null, int most = 0)
    {
        if (mix.Count == 0) return;
        where ??= _ => true;
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
            spots = spots.Where(where).ToList();
            int max = most > 0 ? (t.Max > 0 ? Math.Min(most, t.Max) : most) : t.Max;
            if (max > 0) _rng.Shuffle(spots);
            int placed = 0;
            foreach (Vector2 spot in spots)
            {
                if (max > 0 && placed >= max) break;
                float y1 = yaw + _rng.Range(-turn, turn);
                Vector2 off = new(_rng.Range(-jitter, jitter), _rng.Range(-jitter, jitter));
                Vector2 origin = spot + off - DecoGeo.Yaw(centre, y1);
                bool turned = TryPlace(inst, origin.X, origin.Y, y1, t.Id);
                if (turned || TryPlace(inst, spot.X - DecoGeo.Yaw(centre, yaw).X, spot.Y - DecoGeo.Yaw(centre, yaw).Y, yaw, t.Id))
                {
                    placed++;
                    Under(t, turned ? y1 : yaw);
                    // The next spot's pieces picked afresh: a different one where the set has it.
                    Repick(t);
                    inst = Instance(t) ?? inst;
                    (lo, hi) = Extent(inst);
                    centre = (lo + hi) / 2;
                }
            }
            // The lattice found room for fewer than two (the walkway runs through its middle): try spots off it, slid towards the walls.
            // Every fourth try takes a fresh instance (a shorter line, perhaps); the tries go round all four ways.
            for (int k = 0; placed < (max > 0 ? Math.Min(2, max) : 2) && k < 24; k++)
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
                if (where(spot) && TryPlace(inst, origin.X, origin.Y, y2, t.Id)) { placed++; Under(t, y2); inst = Instance(t) ?? inst; (lo, hi) = Extent(inst); size = hi - lo; centre = (lo + hi) / 2; }
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

    // ---- supports across a way ---------------------------------------------------------------------

    const string SupportKey = "support";

    /// <summary>
    /// A set that fills with supports (a mine's timber sets) gets them only where they hold something
    /// up: across a way no wider than the set, a post at each wall and its cap under the roof, spaced
    /// along it. Never stood against a wall, where it would frame nothing. Walked through between its
    /// posts, so it never blocks the way; other pieces keep off its line.
    /// </summary>
    void Supports(string[] fill)
    {
        if (!fill.Contains("support")) return;
        DecoPiece p = Pick(new[] { "support" }, SupportKey);
        if (p == null) return;
        bool alongX = _room.Width >= _room.Depth;
        float across = alongX ? _room.Depth : _room.Width, length = alongX ? _room.Width : _room.Depth;
        // The posts stand at the walls (within a hand or so each side) and the cap is near the roof.
        if (p.Width > across + 0.001f || across - p.Width > 0.6f || _room.Height - p.Height > 0.7f || length < 2f) return;
        float step = _rng.Range(2.5f, 3.5f);
        for (float s = _rng.Range(0.8f, step); s < length - 0.6f && !Full(p); s += step)
        {
            var pl = new DecoPlacement
            {
                Piece = p.Id, State = DecoState.Intact, X = alongX ? s : across / 2, Z = alongX ? across / 2 : s, Yaw = alongX ? 90 : 0, Y = 0,
                Group = _groups, Role = "support", Palette = p.Palette,
            };
            if (!DecoLayout.Inside(_room, DecoLayout.Footprint(_forge, pl)) || _plan.Placements.Any(o => DecoLayout.Clash(_forge, o, pl))) continue;
            _plan.Placements.Add(pl);
            _count[p.Id] = _count.GetValueOrDefault(p.Id) + 1;
            _groups++;
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
                if (role == "support") continue; // set across the way by Supports, never against a wall
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

    /// <summary>The ceiling's fitting, of the room's age, hung in its middle (where its builder hung the lamp), along its length.</summary>
    void Ceiling(string[] roles)
    {
        if (roles.Length == 0 || Pick(roles, "ceiling", p => p.Mount == DecoMount.Ceiling) is not { } p) return;
        var pl = new DecoPlacement
        {
            Piece = p.Id, State = DecoState.Intact, X = _room.Width / 2, Z = _room.Depth / 2, Y = _room.Height - p.Height, Yaw = _room.Width >= _room.Depth ? 0 : 90,
            Group = _groups, Role = p.Roles.First(roles.Contains), Palette = p.Palette,
        };
        if (!DecoLayout.Inside(_room, DecoLayout.Footprint(_forge, pl)) || _plan.Placements.Any(o => DecoLayout.Clash(_forge, o, pl))) return;
        _plan.Placements.Add(pl);
        _count[p.Id] = _count.GetValueOrDefault(p.Id) + 1;
        _groups++;
    }

    // ---- reserving doorways and the walkway --------------------------------------------------------

    void Reserve()
    {
        _reserved = new List<DecoRect>();
        float w = _room.Agent + 0.35f;
        foreach (DecoDoor d in _room.Doors) _reserved.Add(_room.Apron(d, 1.2f, 0.2f));
        _reserved.AddRange(_room.Blocked.Concat(_room.Kept));
        _aprons = _reserved.Count;
        if (_room.Doors.Count == 0) return;
        // A walking line between every two doorways, an L along the first one's axis, then across. A room of four or more ways is
        // a hub: its lines join each way to its nearest already joined (a tree, not every pair), so its walls are left to furnish.
        var points = _room.Doors.Select(d => { var (p, i) = _room.DoorPoint(d); return (At: p + i * 0.6f, In: i); }).ToList();
        // One way in: the walkway runs as far in as the doorway check asks (a metre and a half, less in a small room), so the far walls are free.
        if (points.Count == 1) points.Add((points[0].At + points[0].In * MathF.Max(0f, MathF.Min(1.5f, MathF.Max(_room.Width, _room.Depth) * 0.4f) - 0.6f), points[0].In));
        var pairs = new List<(int, int)>();
        if (points.Count < 4)
            for (int i = 0; i < points.Count; i++) for (int j = i + 1; j < points.Count; j++) pairs.Add((i, j));
        else
            for (int j = 1; j < points.Count; j++)
                pairs.Add((Enumerable.Range(0, j).MinBy(i => MathF.Abs(points[i].At.X - points[j].At.X) + MathF.Abs(points[i].At.Y - points[j].At.Y)), j));
        foreach (var (i, j) in pairs)
        {
            var (a, i0) = points[i];
            Vector2 b = points[j].At, corner = MathF.Abs(i0.X) > 0.5f ? new Vector2(b.X, a.Y) : new Vector2(a.X, b.Y);
            _reserved.Add(Segment(a, corner, w));
            _reserved.Add(Segment(corner, b, w));
        }
    }

    /// <summary>Each two doors' walk line through the floor as it stands (<see cref="_walk"/>), raster steps; null for a room of fewer than two.</summary>
    int[,] Routes()
    {
        int n = _room.Doors.Count;
        if (n < 2) return null;
        bool[] fit = _walk.Fits(Walker);
        var routes = new int[n, n];
        for (int a = 0; a < n; a++)
        {
            var dist = new int[fit.Length];
            Array.Fill(dist, -1);
            var q = new Queue<int>();
            foreach (int s in _walk.DoorStarts(_room, _room.Doors[a], Walker))
                if (s >= 0 && s < fit.Length && fit[s] && dist[s] < 0) { dist[s] = 0; q.Enqueue(s); }
            while (q.Count > 0)
            {
                int c = q.Dequeue(), i = c % _walk.Nx, j = c / _walk.Nx;
                foreach (int nb in new[] { i + 1 < _walk.Nx ? c + 1 : -1, i > 0 ? c - 1 : -1, j + 1 < _walk.Nz ? c + _walk.Nx : -1, j > 0 ? c - _walk.Nx : -1 })
                    if (nb >= 0 && fit[nb] && dist[nb] < 0) { dist[nb] = dist[c] + 1; q.Enqueue(nb); }
            }
            for (int b = 0; b < n; b++)
                routes[a, b] = _walk.DoorStarts(_room, _room.Doors[b], Walker).Where(s => s >= 0 && s < fit.Length && dist[s] >= 0).Select(s => dist[s]).DefaultIfEmpty(-1).Min();
        }
        return routes;
    }

    /// <summary>No walk line between two doors shut or a metre longer than through the bare room.</summary>
    bool Lines()
    {
        if (_routes == null) return true;
        int[,] now = Routes();
        for (int a = 0; a < _room.Doors.Count; a++)
            for (int b = a + 1; b < _room.Doors.Count; b++)
                if (_routes[a, b] >= 0 && (now[a, b] < 0 || now[a, b] - _routes[a, b] > Slack)) return false;
        return true;
    }

    static DecoRect Segment(Vector2 a, Vector2 b, float width)
    {
        Vector2 d = b - a;
        float len = d.Length();
        Vector2 u = len < 1e-4f ? new Vector2(width / 2, 0) : d / len * (len / 2 + width / 2);
        Vector2 v = len < 1e-4f ? new Vector2(0, width / 2) : new Vector2(-d.Y, d.X) / len * (width / 2);
        return new DecoRect((a + b) / 2, u, v);
    }
}
