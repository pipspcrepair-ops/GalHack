#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>
/// The house rules: every house is put together from parts (a plan of wings round a hall, storeys, a roof form
/// over the plan, cladding, windows, a porch or verandah, a chimney, a balcony, a garage and its driveway, a fence)
/// chosen by <see cref="HouseStyle"/> from its culture and family. Nothing is a drawing: the plan is cut into
/// rooms, the rooms joined by doors along a tree from the hall, the stairs run up the hall, and the roof is one
/// surface over the whole plan, a height at every cell corner, laid in the player's slopes and corners.
/// </summary>
public static partial class Homes
{
    // ---- the plan ------------------------------------------------------------------------------

    sealed class PlanRoom
    {
        public string Purpose = "quarters";
        public ModuleKind Kind = ModuleKind.Room;
        public int U0, V0, U1, V1;
        public bool Garage, Court;
        public HashSet<(int U, int V)>? Shape;
        public PlanRoom(string purpose, int u0, int v0, int u1, int v1, ModuleKind kind = ModuleKind.Room) { Purpose = purpose; U0 = u0; V0 = v0; U1 = u1; V1 = v1; Kind = kind; }
        public int Area => (U1 - U0 + 1) * (V1 - V0 + 1);
    }

    sealed class HousePlan
    {
        public readonly List<Wing> Wings = new();
        public HashSet<(int U, int V)>? Round;
        public readonly List<PlanRoom> Rooms = new();
        public PlanRoom Hall = null!;
        public PlanRoom? Integral, Court;
        /// <summary>The front door: two cells along u from (DoorU, DoorV) in the front wall.</summary>
        public int DoorU, DoorV;
        public int GarageWide;
        public int Main = 0;
        /// <summary>Terrace units: each its hall, its door's u and its rooms.</summary>
        public readonly List<(PlanRoom Hall, int DoorU, List<PlanRoom> Rooms)> Units = new();
        public (int U0, int V0, int U1, int V1) Box => Round != null
            ? (Round.Min(c => c.U), Round.Min(c => c.V), Round.Max(c => c.U), Round.Max(c => c.V))
            : (Wings.Min(w => w.U0), Wings.Min(w => w.V0), Wings.Max(w => w.U1), Wings.Max(w => w.V1));
        public bool InFoot(int u, int v) => Round != null ? Round.Contains((u, v)) : Wings.Any(w => u >= w.U0 && u <= w.U1 && v >= w.V0 && v <= w.V1);
    }

    sealed class PlanFailed : Exception { public PlanFailed(string why) : base(why) { } }

    /// <summary>Hears why a house's plan was tried again (tests and tools).</summary>
    public static Action<string>? Trace;

    static int Ev(int x) => x / 2 * 2;

    /// <summary>The hall and the strips of rooms beside it, in one rectangle of interior cells. Optionally the integral garage in one strip's front.</summary>
    static void Strips(HousePlan p, Rng r, HouseStyle s, int u0, int v0, int u1, int v1, bool through, int garageWide)
    {
        int width = u1 - u0 + 1, len = v1 - v0 + 1;
        int hw = s.Storeys >= 3 ? 3 : s.Storeys == 2 ? 3 : r.Chance(0.5f) ? 3 : 4;
        // Strip widths either side of the hall: a garage strip exactly the door's width.
        int a, b;
        bool garageLeft = r.Chance(0.5f);
        if (garageWide > 0)
        {
            int rest = width - garageWide - 1 - hw;
            int other = rest >= 4 ? rest - 1 : 0;
            if (rest < 0 || rest > 0 && rest < 4) { hw += rest > 0 && rest < 4 ? rest : 0; other = 0; }
            if (width - garageWide - 1 - hw < 0) throw new PlanFailed("no room for a garage");
            (a, b) = garageLeft ? (garageWide, other) : (other, garageWide);
            if (other == 0) hw = width - garageWide - 1;
        }
        else
        {
            int rest = width - hw;
            if (rest >= 9 && r.Chance(0.6f)) { a = Math.Max(3, (rest - 2) / 2 + r.Range(-1, 2)); b = rest - 2 - a; }
            else if (rest >= 4) { if (r.Chance(0.5f)) (a, b) = (rest - 1, 0); else (a, b) = (0, rest - 1); }
            else { a = 0; b = 0; hw = width; }
            if (b > 0 && b < 3) { a += b + 1; b = 0; }
            if (a > 0 && a < 3) { b += a + 1; a = 0; }
            if (a == 0 && b == 0) hw = width;
            else hw = width - a - b - (a > 0 ? 1 : 0) - (b > 0 ? 1 : 0);
        }
        int hu0 = u0 + (a > 0 ? a + 1 : 0), hu1 = hu0 + hw - 1;
        // The hall: the length of the house when a stair runs up it, or the front with a kitchen behind.
        int hv1 = v1;
        PlanRoom? behind = null;
        if (!through && len >= 9)
        {
            int hl = Math.Clamp(3 + r.Range(0, 3), 3, len - 4);
            hv1 = v0 + hl - 1;
            behind = new PlanRoom("farm-kitchen", hu0, hv1 + 2, hu1, v1);
        }
        p.Hall = new PlanRoom("hall", hu0, v0, hu1, hv1, ModuleKind.Hall);
        p.Rooms.Add(p.Hall);
        if (behind != null) p.Rooms.Add(behind);
        p.DoorV = v0 - 1;
        p.DoorU = hw >= 4 ? hu0 + 1 : hu0;
        if (hw == 3 && r.Chance(0.5f)) p.DoorU = hu0 + 1;
        void Strip(int su0, int su1, bool garage)
        {
            if (su1 < su0) return;
            if (garage)
            {
                int gl = Math.Min(len, Math.Max(6, len - 4));
                var g = new PlanRoom("workshop", su0, v0, su1, v0 + gl - 1) { Garage = true };
                p.Rooms.Add(g);
                p.Integral = g;
                p.GarageWide = su1 - su0 + 1;
                if (len - gl - 1 >= 3) p.Rooms.Add(new PlanRoom("quarters", su0, v0 + gl + 1, su1, v1));
                return;
            }
            if (len >= 8 && r.Chance(0.85f))
            {
                int fl = Math.Clamp(len / 2 + r.Range(-1, 2), 3, len - 4);
                p.Rooms.Add(new PlanRoom("quarters", su0, v0, su1, v0 + fl - 1));
                p.Rooms.Add(new PlanRoom("quarters", su0, v0 + fl + 1, su1, v1));
            }
            else p.Rooms.Add(new PlanRoom("quarters", su0, v0, su1, v1));
        }
        Strip(u0, u0 + a - 1, garageWide > 0 && garageLeft);
        Strip(hu1 + 2, hu1 + 1 + b, garageWide > 0 && !garageLeft);
    }

    /// <summary>The rooms of a wing that hangs off the main block: one, or two along its length.</summary>
    static void WingRooms(HousePlan p, Rng r, int u0, int v0, int u1, int v1)
    {
        bool alongU = u1 - u0 >= v1 - v0;
        int len = alongU ? u1 - u0 + 1 : v1 - v0 + 1;
        if (len >= 9 && r.Chance(0.7f))
        {
            int cut = Math.Clamp(len / 2 + r.Range(-1, 2), 3, len - 4);
            if (alongU) { p.Rooms.Add(new PlanRoom("quarters", u0, v0, u0 + cut - 1, v1)); p.Rooms.Add(new PlanRoom("quarters", u0 + cut + 1, v0, u1, v1)); }
            else { p.Rooms.Add(new PlanRoom("quarters", u0, v0, u1, v0 + cut - 1)); p.Rooms.Add(new PlanRoom("quarters", u0, v0 + cut + 1, u1, v1)); }
        }
        else p.Rooms.Add(new PlanRoom("quarters", u0, v0, u1, v1));
    }

    static HousePlan MakePlan(HouseStyle s, Rng r, int garageWide)
    {
        var p = new HousePlan();
        int W = s.W, D = s.D;
        bool through = s.Storeys >= 2 || r.Chance(0.35f);
        switch (s.Plan)
        {
            case "court":
            {
                int fd = s.Storeys >= 2 ? 10 : 8, sw = 8, bd = r.Chance(0.5f) ? 8 : 0;
                W = Math.Max(W, 2 * sw + 6);
                D = Math.Max(D, fd + 6 + bd);
                p.Wings.Add(new Wing(0, 0, W - 1, fd - 1));
                p.Wings.Add(new Wing(0, fd - 1, sw - 1, D - 1, false));
                p.Wings.Add(new Wing(W - sw, fd - 1, W - 1, D - 1, false));
                if (bd > 0) p.Wings.Add(new Wing(sw - 1, D - bd, W - sw, D - 1));
                // The front block: a passage hall through to the court, rooms either side.
                int c = W / 2 - 1;
                p.Hall = new PlanRoom("hall", c, 1, c + 2, fd - 2, ModuleKind.Hall);
                p.Rooms.Add(p.Hall);
                p.DoorU = c;
                p.DoorV = 0;
                if (garageWide > 0 && c - 2 >= garageWide)
                {
                    p.Integral = new PlanRoom("workshop", c - 1 - garageWide, 1, c - 2, fd - 2) { Garage = true };
                    p.GarageWide = garageWide;
                    p.Rooms.Add(p.Integral);
                    if (c - 2 - garageWide >= 3) p.Rooms.Add(new PlanRoom("storage", 1, 1, c - 3 - garageWide, fd - 2));
                }
                else p.Rooms.Add(new PlanRoom("quarters", 1, 1, c - 2, fd - 2));
                p.Rooms.Add(new PlanRoom("quarters", c + 4, 1, W - 2, fd - 2));
                int back = bd > 0 ? D - bd - 1 : D - 2;
                WingRooms(p, r, 1, fd, sw - 2, D - 2);
                WingRooms(p, r, W - sw + 1, fd, W - 2, D - 2);
                if (bd > 0) WingRooms(p, r, sw, D - bd + 1, W - sw - 1, D - 2);
                p.Court = new PlanRoom("courtyard", sw, fd, W - sw - 1, back, ModuleKind.Outdoor) { Court = true };
                break;
            }
            case "round":
            case "oval":
            {
                int rx = s.Plan == "oval" ? Math.Max(5, s.W / 2) : Math.Max(4, s.W / 2), rz = s.Plan == "oval" ? Math.Max(4, s.D / 2) : rx;
                p.Round = new HashSet<(int, int)>();
                for (int v = -rz; v < rz; v++)
                    for (int u = -rx; u < rx; u++)
                    {
                        float du = (u + 0.5f) / rx, dv = (v + 0.5f) / rz;
                        if (du * du + dv * dv <= 1f) p.Round.Add((u + rx, v + rz));
                    }
                var inner = new HashSet<(int, int)>(p.Round.Where(c => p.Round.Contains((c.Item1 - 1, c.Item2)) && p.Round.Contains((c.Item1 + 1, c.Item2)) && p.Round.Contains((c.Item1, c.Item2 - 1)) && p.Round.Contains((c.Item1, c.Item2 + 1))));
                int iu = (int)(rx * 0.6f), iv = (int)(rz * 0.6f);
                var room = new PlanRoom("mess", rx - iu, rz - iv, rx + iu - 1, rz + iv - 1, ModuleKind.Hall) { Shape = inner };
                p.Rooms.Add(room);
                p.Hall = room;
                p.DoorU = rx - 1;
                p.DoorV = p.Round.Where(c => c.U == rx - 1 || c.U == rx).Min(c => c.V);
                break;
            }
            case "row":
            {
                int pitch = s.UnitW, n = s.Units;
                W = n * pitch + 1;
                p.Wings.Add(new Wing(0, 0, W - 1, D - 1));
                for (int k = 0; k < n; k++)
                {
                    int a0 = k * pitch + 1, a1 = k * pitch + pitch - 1;
                    bool mirror = k % 2 == 1;
                    int h0 = mirror ? a1 - 2 : a0, h1 = h0 + 2;
                    var hall = new PlanRoom("hall", h0, 1, h1, D - 2, ModuleKind.Hall);
                    p.Rooms.Add(hall);
                    var rooms = new List<PlanRoom>();
                    int r0 = mirror ? a0 : h1 + 2, r1 = mirror ? h0 - 2 : a1;
                    if (r1 - r0 + 1 >= 2)
                    {
                        int fl = Math.Clamp((D - 2) / 2, 3, D - 6);
                        rooms.Add(new PlanRoom("quarters", r0, 1, r1, fl));
                        rooms.Add(new PlanRoom("farm-kitchen", r0, fl + 2, r1, D - 2));
                    }
                    p.Rooms.AddRange(rooms);
                    p.Units.Add((hall, mirror ? h0 + 1 : h0, rooms));
                }
                p.Hall = p.Units[0].Hall;
                p.DoorU = p.Units[0].DoorU;
                p.DoorV = 0;
                break;
            }
            default:
            {
                p.Wings.Add(new Wing(0, 0, W - 1, D - 1, !s.GableFront));
                Strips(p, r, s, 1, 1, W - 2, D - 2, through, garageWide);
                p.DoorV = 0;
                int ww = r.Chance(0.5f) ? 6 : 8, deep = r.Chance(0.5f) ? 6 : 8;
                bool left = r.Chance(0.5f);
                switch (s.Plan)
                {
                    case "ell":
                    {
                        // A gabled wing forward of the front at one end, the door in the angle; where the door or the
                        // garage takes both ends, the wing goes out at the back instead.
                        bool Takes(int x0) => p.DoorU + 1 >= x0 - 1 && p.DoorU <= x0 + ww || p.Integral != null && p.Integral.U1 >= x0 - 1 && p.Integral.U0 <= x0 + ww;
                        int xf = left ? 0 : W - ww;
                        if (Takes(xf)) xf = left ? W - ww : 0;
                        if (r.Chance(0.55f) && !Takes(xf))
                        {
                            p.Wings.Add(new Wing(xf, -deep + 1, xf + ww - 1, 0, false));
                            WingRooms(p, r, xf + 1, -deep + 2, xf + ww - 2, -1);
                        }
                        else
                        {
                            int x0 = left ? 0 : W - ww;
                            p.Wings.Add(new Wing(x0, D - 1, x0 + ww - 1, D - 2 + deep, false));
                            WingRooms(p, r, x0 + 1, D, x0 + ww - 2, D - 3 + deep);
                        }
                        break;
                    }
                    case "tee":
                    {
                        int x0 = Ev((W - ww) / 2);
                        p.Wings.Add(new Wing(x0, D - 1, x0 + ww - 1, D - 2 + deep, false));
                        WingRooms(p, r, x0 + 1, D, x0 + ww - 2, D - 3 + deep);
                        break;
                    }
                    case "wing":
                    {
                        // A lower-ridged wing off one side, set back from the front.
                        int back = 2 * r.Range(1, 3), wd = Math.Min(D - back, 8);
                        if (left) { p.Wings.Add(new Wing(-ww + 1, back, 0, back + wd - 1)); WingRooms(p, r, -ww + 2, back + 1, -1, back + wd - 2); }
                        else { p.Wings.Add(new Wing(W - 1, back, W - 2 + ww, back + wd - 1)); WingRooms(p, r, W, back + 1, W - 3 + ww, back + wd - 2); }
                        break;
                    }
                }
                break;
            }
        }
        // Purposes: a kitchen (where the loot is kept), a living room, bedrooms, a washroom.
        var plain = p.Rooms.Where(x => x != p.Hall && !x.Garage && x.Purpose != "hall").ToList();
        if (p.Units.Count == 0 && p.Round == null)
        {
            if (plain.Count == 0) throw new PlanFailed("no rooms but the hall");
            if (!plain.Any(x => x.Purpose == "farm-kitchen"))
                plain.OrderByDescending(x => x.V0).ThenBy(x => x.Area).First().Purpose = "farm-kitchen";
            var rest = plain.Where(x => x.Purpose != "farm-kitchen").OrderByDescending(x => x.Area).ToList();
            if (rest.Count > 0) rest[0].Purpose = "mess";
            if (rest.Count > 2) rest[^1].Purpose = "washroom";
            if (rest.Count > 3 && r.Chance(0.5f)) rest[^2].Purpose = "storage";
        }
        if (p.Round == null && p.Rooms.Any(x => x.Shape == null && (x.U1 - x.U0 < 1 || x.V1 - x.V0 < 1))) throw new PlanFailed("a room too small");
        return p;
    }

    // ---- doors between rooms ---------------------------------------------------------------------

    /// <summary>Where two rooms meet across one wall cell, the stretch of it a door could take.</summary>
    static (int Wall, bool AlongU, int Lo, int Hi)? Meet(PlanRoom a, PlanRoom b)
    {
        if (a.U1 + 2 == b.U0 || b.U1 + 2 == a.U0)
        {
            int lo = Math.Max(a.V0, b.V0), hi = Math.Min(a.V1, b.V1);
            if (hi - lo >= 1) return (a.U1 + 2 == b.U0 ? a.U1 + 1 : b.U1 + 1, false, lo, hi);
        }
        if (a.V1 + 2 == b.V0 || b.V1 + 2 == a.V0)
        {
            int lo = Math.Max(a.U0, b.U0), hi = Math.Min(a.U1, b.U1);
            if (hi - lo >= 1) return (a.V1 + 2 == b.V0 ? a.V1 + 1 : b.V1 + 1, true, lo, hi);
        }
        return null;
    }

    /// <summary>Doors from the hall out to every room of a storey, along a tree, and a few more for a way round.</summary>
    static void Doors(Rng r, List<(PlanRoom Room, Hut Hut)> rooms, PlanRoom from, int storey, Func<int, int, bool> stair, Action<int, int, bool>? cut = null)
    {
        var done = new HashSet<PlanRoom> { from };
        var queue = new Queue<PlanRoom>();
        queue.Enqueue(from);
        var edges = new List<(PlanRoom A, PlanRoom B)>();
        bool Try(PlanRoom a, PlanRoom b)
        {
            if (Meet(a, b) is not var (wall, alongU, lo, hi)) return false;
            // The middle of the stretch first, clear of the stairs on both sides.
            int mid = (lo + hi - 1) / 2;
            foreach (int t in Enumerable.Range(lo, hi - lo).OrderBy(t => Math.Abs(t - mid)))
            {
                bool ok = true;
                for (int k = 0; k < 2 && ok; k++)
                    foreach (int side in new[] { -1, 1 })
                    {
                        var (u, v) = alongU ? (t + k, wall + side) : (wall + side, t + k);
                        if (stair(u, v)) ok = false;
                    }
                if (!ok) continue;
                if (cut != null) cut(alongU ? t : wall, alongU ? wall : t, alongU);
                else foreach (var (_, h) in rooms.Where(x => x.Room == a || x.Room == b).Take(1)) h.Gap(alongU ? t : wall, alongU ? wall : t, alongU, storey, DoorSize.Wide, DoorSize.Tall);
                foreach (var (_, h) in rooms.Where(x => x.Room == a || x.Room == b).Skip(1)) h.Mark(alongU ? t : wall, alongU ? wall : t, alongU, storey);
                return true;
            }
            return false;
        }
        while (queue.Count > 0)
        {
            PlanRoom a = queue.Dequeue();
            foreach (var (b, _) in rooms.OrderBy(x => x.Room.Court ? 0 : x.Room.Kind == ModuleKind.Hall ? 1 : 2))
            {
                if (done.Contains(b) || Meet(a, b) == null) continue;
                // The court is a room of its own: you go out to it from every room round it.
                if (Try(a, b)) { done.Add(b); queue.Enqueue(b); edges.Add((a, b)); }
            }
        }
        if (done.Count < rooms.Count) throw new PlanFailed($"{rooms.Count - done.Count} rooms of storey {storey} have no way in");
        // Rooms round a court open on to it, and now and then a second door makes a way round.
        foreach (var (a, _) in rooms)
            foreach (var (b, _) in rooms)
                if (a != b && !edges.Contains((a, b)) && !edges.Contains((b, a)) && (a.Court && !b.Garage || r.Chance(0.12f) && !a.Garage && !b.Garage) && Meet(a, b) != null)
                    if (Try(a, b)) edges.Add((a, b));
    }

    // ---- the build -------------------------------------------------------------------------------

    /// <summary>What a house left on the lot: where its front and its drive meet the lot's front edge, its door.</summary>
    sealed class Built
    {
        public int U0, U1, V0, V1;          // the house's bounds in the lot frame, garage and porch included
        public int DoorU, DoorV;            // its front door in the lot frame
        public int? DriveU0, DriveU1;       // its drive's cells along the lot front
        public int PathU;                   // where its path from the door meets the lot front
        public List<int> Paths = new();     // every front door's path (a terrace has one to each house)
    }

    /// <summary>A house of a style on a lot: its front frame turned to the road, the lot's front edge at v = 0 of <paramref name="lot"/>. Built at (side, front) in the lot.</summary>
    static Built Dwelling(Lot l, Frame lot, HouseStyle s, int side, int front, int? maxW = null)
    {
        Rng r = l.R;
        // Plans can fail (a door with nowhere to go): a few more tries, then a plain one that always works.
        for (int attempt = 0; ; attempt++)
        {
            if (attempt >= 2 && s.Garage == "front") s.Garage = "attached";
            if (attempt >= 6)
            {
                if (s.Plan is "round" or "oval" or "tower") { s.Roof = "hip"; s.Lift = false; s.Berm = false; }
                s.Plan = s.Plan is "row" ? "row" : "rect";
                s.Garage = s.Plan == "row" ? "none" : s.Garage == "front" ? "attached" : s.Garage;
                s.Storeys = Math.Min(s.Storeys, s.Plan == "row" ? 3 : 2);
                s.W = Math.Max(s.W, 12);
                s.D = Math.Max(s.D, 10);
            }
            var snap = Snapshot(l.C);
            try { return Draw(l, lot, s, side, front, attempt, maxW); }
            catch (Exception e) when (e is PlanFailed or InvalidOperationException && attempt < 8)
            {
                Trace?.Invoke($"{s.Family.Id} {s.Plan} {s.Roof}: {e.Message}");
                Restore(l.C, snap);
            }
        }
    }

    sealed record Snap(Dictionary<Int3, Block> Blocks, HashSet<Int3> Occ, int Modules, int Doorways, int Slots, int Edges, int Plain, HashSet<Int3> Lasting, int Service, int Homes, int Entrance, Int3 Cell, Dir Facing, Int3 Gate, int Levels);

    static Snap Snapshot(Canvas c) => new(new Dictionary<Int3, Block>(c.Blocks), new HashSet<Int3>(c.Occ), c.Modules.Count, c.Doorways.Count, c.Slots.Count, c.Edges.Count,
        c.X.Plain.Count, new HashSet<Int3>(c.X.Lasting), c.X.Service.Count, c.X.Homes.Count, c.EntranceModule, c.EntranceCell, c.EntranceFacing, c.X.Gate, c.X.Levels.Count);

    static void Restore(Canvas c, Snap s)
    {
        c.Blocks.Clear();
        foreach (var (k, v) in s.Blocks) c.Blocks[k] = v;
        c.Occ.Clear();
        c.Occ.UnionWith(s.Occ);
        c.Modules.RemoveRange(s.Modules, c.Modules.Count - s.Modules);
        c.Doorways.RemoveRange(s.Doorways, c.Doorways.Count - s.Doorways);
        c.Slots.RemoveRange(s.Slots, c.Slots.Count - s.Slots);
        c.Edges.RemoveRange(s.Edges, c.Edges.Count - s.Edges);
        c.X.Plain.RemoveRange(s.Plain, c.X.Plain.Count - s.Plain);
        c.X.Service.RemoveRange(s.Service, c.X.Service.Count - s.Service);
        c.X.Homes.RemoveRange(s.Homes, c.X.Homes.Count - s.Homes);
        c.X.Levels.RemoveRange(s.Levels, c.X.Levels.Count - s.Levels);
        c.X.Lasting.Clear();
        c.X.Lasting.UnionWith(s.Lasting);
        c.EntranceModule = s.Entrance;
        c.EntranceCell = s.Cell;
        c.EntranceFacing = s.Facing;
        c.X.Gate = s.Gate;
    }

    /// <summary>The plain materials of a house of this style, by its tech.</summary>
    static Stuff StuffOf(HouseStyle s, TechLevel tech, Rng r)
    {
        Stuff b = Stuff.For(tech, r);
        Material wallM = s.Wall switch
        {
            "brick" => Material.Ceramic, "board" or "timber" => Material.Timber, "stone" => Material.Rock, "panel" => tech == TechLevel.High ? Material.Resin : Material.Concrete,
            "glass" => Material.Glass, _ => Material.Concrete,
        };
        Material roofM = s.Cover switch
        {
            "tile" or "glazed" => Material.Ceramic, "slate" => Material.Rock, "metal" => tech == TechLevel.High ? Material.Alloy : Material.Steel, "thatch" or "shingle" => Material.Timber,
            "glass" => Material.Glass, "cap" => Material.Resin, _ => Material.Concrete,
        };
        Rgb stack = s.Wall is "brick" or "stone" ? s.WallC.Scale(0.85f) : b.Stack;
        return b with
        {
            WallM = wallM, Wall = s.WallC, RoofM = roofM, Roof = s.RoofC, Base = s.Wall == "stone" ? s.WallC.Scale(0.8f) : b.Base,
            StackM = s.Wall == "stone" ? Material.Rock : Material.Ceramic, Stack = stack, Door = s.TrimC,
        };
    }

    /// <summary>
    /// The outer walls' cladding: brick, boards, logs, coursed stone, render, panels, a timber frame, a stone ground floor
    /// under timber on some. Each wall one material in one colour, its face drawn by the place shader from the block's
    /// finish (<see cref="Surfaces.HouseVariant"/>): no colour stripes or jitter cell by cell, which read as a mosaic.
    /// Only what a builder sets apart stands out: board corner trims, log ends, stone quoins, a half-timbered frame, a
    /// glass wall's bands.
    /// </summary>
    static Func<int, int, int, Block> Cladding(HouseStyle s, Hut h, Stuff st, TechLevel tech)
    {
        var dark = new Rgb(0x3a, 0x2e, 0x26);
        int storeyH = h.Height + 1;
        byte cover = Surfaces.HouseVariant(s.Cover), frameFinish = Surfaces.HouseVariant("frame"), infill = Surfaces.HouseVariant("render");
        return (u, y, v) =>
        {
            int storey = Math.Max(0, y) / storeyH, inStorey = Math.Max(0, y) % storeyH;
            string wall = storey == 0 && s.Ground.Length > 0 ? s.Ground : storey >= 1 && s.Upper.Length > 0 ? s.Upper : s.Wall;
            Rgb c = storey == 0 && s.Ground.Length > 0 ? s.GroundC : storey >= 1 && s.Upper.Length > 0 ? s.UpperC : s.WallC;
            // A mansard's top storey is roof, its windows dormers in it.
            if (s.Roof == "mansard" && storey == h.Storeys - 1 && storey > 0) return new Block(BlockType.Cube, st.RoofM, st.Roof, Role.Wall, Dir.North, cover);
            byte finish = Surfaces.HouseVariant(wall);
            Block Clad(Material m, Rgb col) => new(BlockType.Cube, m, col, Role.Wall, Dir.North, finish);
            bool corner = !h.Foot.Contains((u - 1, v)) && !h.Foot.Contains((u, v - 1)) || !h.Foot.Contains((u + 1, v)) && !h.Foot.Contains((u, v - 1))
                || !h.Foot.Contains((u - 1, v)) && !h.Foot.Contains((u, v + 1)) || !h.Foot.Contains((u + 1, v)) && !h.Foot.Contains((u, v + 1));
            switch (wall)
            {
                case "brick": return Clad(Material.Ceramic, c);
                case "board": return Clad(Material.Timber, corner ? c.Scale(1.06f) : c);
                case "timber": return Clad(Material.Timber, corner ? c.Scale(0.85f) : c);
                case "stone": return Clad(Material.Rock, corner && ((y / 2) & 1) == 0 ? c.Scale(1.1f) : c);
                case "halftimber":
                {
                    bool frame = y == 0 || inStorey == 0 || inStorey == storeyH - 1 || corner || (u + v) % 4 == 0 || ((u + v) % 4 + inStorey) % 4 == 0 && inStorey is 1 or 2 && (u + v) % 4 != 2;
                    return frame ? new Block(BlockType.Cube, Material.Timber, dark, Role.Wall, Dir.North, frameFinish) : new Block(BlockType.Cube, Material.Concrete, s.WallC.Lerp(new Rgb(0xec, 0xe4, 0xcc), 0.6f), Role.Wall, Dir.North, infill);
                }
                case "adobe": return Clad(Material.Concrete, c);
                case "panel": return Clad(tech == TechLevel.High ? Material.Resin : Material.Concrete, c);
                case "glass":
                    return inStorey == storeyH - 1 || inStorey == 0 ? new Block(BlockType.Cube, Material.Alloy, new Rgb(0x8c, 0x98, 0xa0), Role.Wall)
                        : new Block(BlockType.Pane, Material.Glass, new Rgb(0x6a, 0x84, 0x8c), Role.Window);
                default: return Clad(Material.Concrete, c);
            }
        };
    }

    static Built Draw(Lot l, Frame lot, HouseStyle s, int side, int front, int attempt, int? maxW)
    {
        Rng r = l.R;
        TechLevel tech = l.Tech;
        // The garage: in the front of the house, against its side, apart from it or an open carport.
        int gw = s.Double ? (r.Chance(0.5f) ? 6 : 8) : 4;
        bool lifted = s.Lift, round = s.Plan is "round" or "oval";
        if (lifted || round || s.Plan is "row" or "tower") s.Garage = s.Garage == "carport" && !round && s.Plan != "row" ? "carport" : "none";
        // Earth heaped against a house leaves no room beside it for a garage: only one in its own front.
        if (s.Berm && s.Garage != "front") s.Garage = "none";
        if (s.Garage == "front" && (s.D < 10 || s.Plan == "court" && s.W < 2 * gw + 10)) s.Garage = "attached";
        if (s.Plan == "tower") return Tower(l, lot, s, side, front);
        HousePlan plan = MakePlan(s, r, s.Garage == "front" ? gw : 0);
        if (s.Garage == "front" && plan.Integral == null) s.Garage = "attached";
        var (bu0, bv0, bu1, bv1) = plan.Box;
        // The garage beside the house, on the side away from any wing.
        bool garageRight = plan.Wings.All(w => w.U0 >= bu0) ? r.Chance(0.5f) : plan.Wings.Any(w => w.U0 < 0);
        if (plan.Wings.Any(w => w.U1 > s.W - 1) && plan.Wings.All(w => w.U0 >= 0)) garageRight = false;
        int gd = 8 + 2 * r.Range(0, 2), gOuter = gw + 2;
        // A garage of its own stands clear of the house's eaves and its own.
        int gSet = s.Garage == "detached" ? 2 * s.Over + 2 + 2 * r.Range(0, 2) : 0;
        int gv0 = s.Garage switch { "attached" => Ev(r.Range(-2, 3)), "detached" => Ev(Math.Max(0, bv1 - gd - 2 * r.Range(0, 3))), "carport" => r.Chance(0.5f) ? 0 : 2, _ => 0 };
        if (s.Garage == "attached") gv0 = Math.Clamp(gv0, Math.Max(bv0, -2), Math.Max(bv0, s.D - 4));
        int gu0 = 0;
        bool hasGarage = s.Garage is "attached" or "detached" or "carport";
        if (hasGarage)
        {
            int mainEdge = garageRight ? plan.Wings.Where(w => w.V0 <= gv0 + gd - 1 && w.V1 >= gv0).DefaultIfEmpty(plan.Wings[0]).Max(w => w.U1) : plan.Wings.Where(w => w.V0 <= gv0 + gd - 1 && w.V1 >= gv0).DefaultIfEmpty(plan.Wings[0]).Min(w => w.U0);
            int across = s.Garage == "attached" ? 0 : s.Garage == "carport" ? 1 : gSet + 1;
            gu0 = garageRight ? mainEdge + across : mainEdge - across - gOuter + 1;
            if (s.Garage == "attached") gu0 = garageRight ? mainEdge : mainEdge - gOuter + 1;
        }
        int all0 = Math.Min(bu0, hasGarage ? gu0 : bu0), all1 = Math.Max(bu1, hasGarage ? gu0 + gOuter - 1 : bu1);
        if (maxW is int mw && all1 - all0 + 1 > mw)
        {
            if (attempt < 3) { s.W = Math.Max(10, s.W - 2); if (hasGarage && attempt >= 1) s.Garage = "front"; throw new PlanFailed("too wide for the lot"); }
            s.Garage = "none";
            hasGarage = false;
            all0 = bu0;
            all1 = bu1;
            if (all1 - all0 + 1 > mw) { s.W = Math.Max(10, mw - 2); s.Plan = "rect"; throw new PlanFailed("too wide for the lot"); }
        }
        // The house's frame: its plan's corner at (side, front) of the lot, with everything that stands in front of it.
        int porchDeep = s.Porch || s.Verandah || lifted ? 2 : 0;
        int frontV = Math.Min(bv0, hasGarage ? gv0 : bv0) - porchDeep - (lifted ? 4 : 0);
        Frame f = lot.Shift(side - all0, front - frontV);
        int toLot = front - frontV; // house v + toLot = lot v
        // The materials and the cladding.
        l.S = StuffOf(s, tech, r);
        var h = new Hut(l, f) { Storeys = s.Storeys, Lift = lifted ? 4 : 0, RoofFinish = Surfaces.HouseVariant(s.Cover) };
        h.Skin = Cladding(s, h, l.S, tech);
        if (plan.Round != null) foreach (var c in plan.Round) h.Foot.Add(c);
        h.Wings.AddRange(plan.Wings);
        // Rooms on every storey: the plan's below; above it, bedrooms over the rooms and the garage, a washroom.
        var storeyRooms = new List<List<(PlanRoom P, Room R)>>();
        for (int st = 0; st < s.Storeys; st++)
        {
            var list = new List<(PlanRoom, Room)>();
            foreach (PlanRoom pr in plan.Rooms)
            {
                string purpose = st == 0 ? pr.Purpose : pr == plan.Hall || pr.Kind == ModuleKind.Hall ? "hall" : pr.Purpose == "washroom" ? "washroom" : list.Count % 4 == 3 ? "storage" : "quarters";
                Room rm = h.Add(purpose, st, pr.U0, pr.V0, pr.U1, pr.V1, st == 0 ? pr.Kind : pr.Kind == ModuleKind.Hall ? ModuleKind.Hall : ModuleKind.Room);
                rm.Shape = pr.Shape;
                list.Add((pr, rm));
            }
            storeyRooms.Add(list);
        }
        h.Raise();
        // The court: open ground, paved, every room round it opening on to it.
        if (plan.Court is { } court)
            for (int v = court.V0; v <= court.V1; v++)
                for (int u = court.U0; u <= court.U1; u++) l.Plate(f, u, v, l.S.Base.Scale(1.15f), Role.Floor);
        // The stairs, up the hall, a flight a storey switching sides.
        var halls = new List<PlanRoom>();
        foreach (PlanRoom hall in plan.Units.Count > 0 ? plan.Units.Select(x => x.Hall) : new[] { plan.Hall }) halls.Add(hall);
        var flights = new Dictionary<(PlanRoom, int), int>();
        foreach (PlanRoom hall in halls)
            for (int st = 0; st + 1 < s.Storeys; st++)
            {
                if (hall.V1 - hall.V0 + 1 < 6 || hall.U1 - hall.U0 + 1 < (s.Storeys > 2 ? 3 : 2)) throw new PlanFailed("a hall too short for a stair");
                int col = st % 2 == 0 ? hall.U1 : hall.U0;
                if (hall.Shape != null) col = (hall.U0 + hall.U1) / 2 + (st % 2 == 0 ? 1 : -1);
                flights[(hall, st)] = h.Stair(col, hall.V1 - 4, st);
            }
        // The doors: the front door into the hall, then a tree of doors out from it on every storey.
        var gRooms = new List<(PlanRoom, Hut)>();
        for (int st = 0; st < s.Storeys; st++)
        {
            var on = plan.Rooms.Select(x => (x, h)).ToList();
            if (st == 0 && plan.Court != null) on.Add((plan.Court, h));
            int stLocal = st;
            if (plan.Units.Count > 0)
                foreach (var unit in plan.Units)
                {
                    var mine = on.Where(x => x.x == unit.Hall || unit.Rooms.Contains(x.x)).ToList();
                    Doors(r, mine, unit.Hall, st, (u, v) => h.StairAt(u, v, stLocal));
                }
            else
            {
                if (st > 0) on = on.Where(x => !x.x.Court).ToList();
                Doors(r, on, plan.Hall, st, (u, v) => h.StairAt(u, v, stLocal), plan.Court != null && st == 0 ? (u, v, alongU) =>
                {
                    h.Gap(u, v, alongU, 0, DoorSize.Wide, DoorSize.Tall);
                } : null);
            }
        }
        // The front doors in the front wall.
        var fronts = plan.Units.Count > 0 ? plan.Units.Select(x => (Hall: x.Hall, DoorU: x.DoorU)).ToList() : new List<(PlanRoom Hall, int DoorU)> { (plan.Hall, plan.DoorU) };
        // Every door a house has is the one standard door: a person walks through it upright, never stooping into a hatch.
        foreach (var (_, du) in fronts) h.Gap(du, plan.DoorV, true, 0, DoorSize.Wide, DoorSize.Tall);
        var doorCols = fronts.SelectMany(x => Enumerable.Range(x.DoorU, DoorSize.Wide)).ToHashSet();
        // An integral garage's door, the width of its room.
        int garageModule = -1;
        Int3 garageDoorCell = default;
        Dir garageFacing = Dir.North;
        int gDoorU = 0, gDoorV = 0, gWide = 0;
        if (plan.Integral is { } ig)
        {
            gWide = ig.U1 - ig.U0 + 1;
            gDoorU = ig.U0;
            gDoorV = ig.V0 - 1;
            h.Gap(gDoorU, gDoorV, true, 0, gWide, 3);
        }
        // A balcony's door from the room over the front; its slab is never over a front door's way in, so it moves aside or narrows.
        (int U, int Wd)? balcony = null;
        (int U, int V)? balconyDoor = null;
        if (s.Balcony && plan.Units.Count == 0 && plan.Round == null)
        {
            var over = storeyRooms[1].Where(x => x.P.V0 == plan.Hall.V0 && x.P.U1 - x.P.U0 >= 3 && plan.InFoot(x.P.U0, x.P.V0 - 1) && !plan.InFoot(x.P.U0, x.P.V0 - 2)).OrderByDescending(x => x.P.U1 - x.P.U0).FirstOrDefault();
            if (over.R != null)
            {
                int want = s.Family.Id == "chalet" ? over.P.U1 - over.P.U0 + 1 : 4;
                // Out over open ground the whole width: never over a door's way in, never into a wing or a garage standing forward.
                bool Garaged(int u, int v) => hasGarage && u >= gu0 && u < gu0 + gOuter && v >= gv0 && v < gv0 + gd;
                bool Clear(int bu, int bw) => Enumerable.Range(bu - 1, bw + 2).All(u => !doorCols.Contains(u - 1) && !doorCols.Contains(u) && !doorCols.Contains(u + 1)
                    && Enumerable.Range(1, 3).All(d => !plan.InFoot(u, over.P.V0 - 1 - d) && !Garaged(u, over.P.V0 - 1 - d)))
                    && Enumerable.Range(bu, bw).All(u => plan.InFoot(u, over.P.V0 - 1));
                var fit = Enumerable.Range(0, want - 2).Select(cut => want - cut)
                    .SelectMany(bw => Enumerable.Range(over.P.U0, Math.Max(0, over.P.U1 - over.P.U0 + 2 - bw)).OrderBy(bu => Math.Abs(bu + bw / 2 - (over.P.U0 + over.P.U1) / 2)).Select(bu => (bu, bw)))
                    .Where(x => x.bw >= 3 && Clear(x.bu, x.bw)).Take(1).ToList();
                if (fit.Count > 0)
                {
                    var (bu, bw) = fit[0];
                    int du = Math.Clamp(bu + bw / 2 - 1, over.P.U0, over.P.U1 - 1);
                    h.Gap(du, over.P.V0 - 1, true, 1, DoorSize.Wide, DoorSize.Tall);
                    balcony = (bu, bw);
                    balconyDoor = (du, over.P.V0 - 1);
                }
            }
        }
        h.Modules();
        // Rooms and the flights between storeys.
        foreach (PlanRoom hall in halls)
            for (int st = 0; st + 1 < s.Storeys; st++)
            {
                l.C.Link(storeyRooms[st].First(x => x.P == hall).R.Module, flights[(hall, st)]);
                l.C.Link(flights[(hall, st)], storeyRooms[st + 1].First(x => x.P == hall).R.Module);
            }
        for (int st = 1; st < s.Storeys; st++)
            if (l.C.X.Levels.All(x => x.Storey != st)) l.C.X.Levels.Add(new LevelInfo { Storey = st, WalkY = h.Walk(st) + h.Lift, Name = st == 1 ? "upper" : $"floor {st + 1}" });
        if (plan.Court != null)
        {
            var cells = new List<Int3>();
            for (int v = plan.Court.V0; v <= plan.Court.V1; v++)
                for (int u = plan.Court.U0; u <= plan.Court.U1; u++)
                    for (int y = 0; y <= 2; y++) cells.Add(f.At(u, y, v));
            int cm = l.C.AddModule(ModuleKind.Outdoor, "courtyard", 0, cells);
            foreach (var (pr, rm) in storeyRooms[0])
                if (Meet(pr, plan.Court) != null) l.C.Link(cm, rm.Module);
        }
        if (plan.Integral != null) garageModule = storeyRooms[0].First(x => x.P == plan.Integral).R.Module;
        // Windows, then shutters beside them.
        h.Windows(s.Window == "small" ? 0.5f : 0.85f);
        if (s.Window == "tall") h.TallWindows();
        if (s.Shutters) h.Shutters(s.ShutterC);
        if (plan.Round != null) h.Band(s.Wall == "glass" ? 0 : 1);
        // The roof over the whole plan.
        var huts = new List<Hut> { h };
        // The garage of its own: against the side or apart, its own roof, or one roof with the house where it is one storey.
        Hut? g = null;
        (int U, int V, Dir Out)? sideDoor = null;
        if (s.Garage is "attached" or "detached")
        {
            g = new Hut(l, f) { Storeys = 1, RoofFinish = Surfaces.HouseVariant(s.Cover) };
            g.Skin = Cladding(s, g, l.S, tech);
            g.Wings.Add(new Wing(gu0, gv0, gu0 + gOuter - 1, gv0 + gd - 1, s.Roof is "hip" or "pagoda" || !s.GableFront));
            Room gr = g.Add("workshop", 0, gu0 + 1, gv0 + 1, gu0 + gOuter - 2, gv0 + gd - 2, ModuleKind.Hall);
            g.Raise();
            gDoorU = gu0 + 1;
            gDoorV = gv0;
            gWide = gw;
            g.Gap(gDoorU, gDoorV, true, 0, gWide, 3);
            // A way in from the house, or a side door to the yard.
            bool inner = false;
            if (s.Garage == "attached")
            {
                var gp = new PlanRoom("workshop", gr.U0, gr.V0, gr.U1, gr.V1) { Garage = true };
                foreach (var (pr, rm) in storeyRooms[0].Where(x => !x.P.Garage).OrderBy(x => x.P.Kind == ModuleKind.Hall ? 0 : 1))
                {
                    if (Meet(pr, gp) is not var (wall, alongU, lo, hi) || alongU) continue;
                    int t = (lo + hi - 1) / 2;
                    if (h.StairAt(wall + 1, t, 0) || h.StairAt(wall - 1, t, 0) || h.StairAt(wall + 1, t + 1, 0) || h.StairAt(wall - 1, t + 1, 0)) continue;
                    h.Gap(wall, t, false, 0, DoorSize.Wide, DoorSize.Tall);
                    g.Mark(wall, t, false, 0);
                    inner = true;
                    g.Modules();
                    l.C.Link(rm.Module, gr.Module);
                    break;
                }
            }
            if (!inner)
            {
                // In its outer side wall, away from the house.
                int su = garageRight ? gu0 + gOuter - 1 : gu0, sv = gv0 + gd / 2 - 1;
                g.Gap(su, sv, false, 0, DoorSize.Wide, DoorSize.Tall);
                sideDoor = (su, sv, garageRight ? Dir.East : Dir.West);
                g.Modules();
                var cells = new List<Int3>();
                for (int k = 0; k < DoorSize.Wide; k++)
                    for (int y = 0; y < DoorSize.Tall; y++) cells.Add(f.At(su, y, sv + k));
                l.C.AddDoorway(cells, f.Turn(garageRight ? Dir.East : Dir.West), gr.Module, l.C.EntranceModule);
                l.C.Link(l.C.EntranceModule, gr.Module);
            }
            garageModule = gr.Module;
            g.Windows(0.4f);
            g.Furnish();
            huts.Add(g);
        }
        Roofs(l, s, h, g, plan, r);
        // The garage door: its leaves in the opening.
        if (gWide > 0)
        {
            Hut gh = g ?? h;
            gh.GarageDoor(gDoorU, gDoorV, gWide, s.GarageDoor, s.GarageC, r);
            garageDoorCell = f.At(gDoorU, 0, gDoorV);
            garageFacing = f.Turn(Dir.North);
        }
        // A carport: posts and a roof over the end of the drive.
        if (s.Garage == "carport")
        {
            gWide = gw;
            gDoorU = gu0 + 1;
            gDoorV = gv0;
            Carport(l, f, h, gu0, gv0, gOuter, gd, garageRight, s);
            garageDoorCell = f.At(gDoorU, 0, gDoorV);
            garageFacing = f.Turn(Dir.North);
        }
        // What stands in front: a porch or a verandah, bays, a balcony; a hoist beam, vigas; stilts and the steps up.
        var (fu0, fv0, fu1, fv1) = plan.Box;
        if (lifted) h.Stilts();
        // A verandah runs along the front wall either side of the door as far as nothing stands in front of it.
        int ru0 = plan.DoorU, ru1 = plan.DoorU + 1;
        bool Open(int u) => plan.InFoot(u, plan.DoorV) && !plan.InFoot(u, plan.DoorV - 1) && !plan.InFoot(u, plan.DoorV - 2) && (g == null || !g.Foot.Contains((u, plan.DoorV - 1)) && !g.Foot.Contains((u, plan.DoorV - 2)))
            && (plan.Integral == null || u < plan.Integral.U0 - 1 || u > plan.Integral.U1 + 1);
        while (Open(ru0 - 1)) ru0--;
        while (Open(ru1 + 1)) ru1++;
        if (s.Verandah || lifted) h.Verandah(ru0, ru1, plan.DoorV, lifted, s.Storeys, doorCols);
        else if (s.Porch)
            foreach (var (_, du) in fronts)
            {
                // A porch: the same lean roof on posts, just the door's width and a metre either side.
                int p0 = du - 1, p1 = du + 2;
                if (plan.Units.Count == 0) { p0 = Math.Max(p0, ru0); p1 = Math.Min(p1, ru1); }
                if (p1 - p0 >= 3) h.Verandah(p0, p1, plan.DoorV, false, s.Storeys, doorCols);
            }
        if (s.Bay) h.Bays(plan.DoorV, s.Storeys, r);
        if (balcony is var (bu2, bw2)) h.Balcony(bu2, bw2, plan.DoorV);
        if (s.Vigas) h.Vigas();
        if (s.Hoist) h.Hoist((fu0 + fu1) / 2, plan.DoorV);
        if (s.Berm) h.Berm(plan.DoorV, s.Family.Id == "hillside" ? 2 : 0, round);
        // Headroom at every door out: nothing that came down over a way in stays there.
        foreach (var (_, du) in fronts) h.Headroom(du, plan.DoorV, Dir.North, 0);
        if (balconyDoor is var (bdu, bdv)) h.Headroom(bdu, bdv, Dir.North, 1);
        if (g != null && sideDoor is var (sdu, sdv, sdo)) g.Headroom(sdu, sdv, sdo, 0);
        // A garage door keeps the same clear head over it and in front: no eave across the top of it.
        if (gWide > 0 && s.Garage != "carport") (g ?? h).Headroom(gDoorU, gDoorV, Dir.North, 0, gWide, exit: false);
        // Furniture by purpose, the loot in the kitchen.
        h.Furnish();
        Room kitchen = storeyRooms[0].Select(x => x.R).FirstOrDefault(x => x.Purpose == "farm-kitchen") ?? storeyRooms[0].Select(x => x.R).Where(x => x.Kind != ModuleKind.Hall || storeyRooms[0].Count == 1).OrderByDescending(x => (x.U1 - x.U0) * (x.V1 - x.V0)).First();
        h.Loot(kitchen);
        foreach (Hut x in huts) x.Box();
        // The way in, and the front doors.
        int stepV = plan.DoorV - (lifted ? 4 + h.Lift : Math.Max(3, s.Over + 2));
        int frontU = fronts[0].DoorU;
        l.Approach(f, frontU, stepV);
        if (lifted) h.Steps(frontU, plan.DoorV);
        // A door surround in the house's own trim, where its walls call for one: render, brick, stone, board.
        bool surround = s.Wall is "render" or "brick" or "stone" or "board" && r.Chance(0.5f);
        foreach (var (hall, du) in fronts)
        {
            Room hr = storeyRooms[0].First(x => x.P == hall).R;
            h.FrontDoor(du, plan.DoorV, hr, DoorSize.Tall);
            if (surround) h.Surround(du, plan.DoorV, s.TrimC);
            l.C.Link(l.C.EntranceModule, hr.Module);
        }
        if (garageModule >= 0) l.C.Link(l.C.EntranceModule, garageModule);
        // The service drop's fixing at the roof's corner on the street side, a meter box below it.
        h.Service(frontU, plan.DoorV, fronts.Count > 1 ? fronts.Select(x => x.DoorU).ToList() : null, s.UnitW);
        // The plot round it.
        var built = new Built
        {
            U0 = side, U1 = side + all1 - all0, V0 = front, V1 = Math.Max(bv1, hasGarage ? gv0 + gd - 1 : bv1) + toLot,
            DoorU = frontU + side - all0, DoorV = plan.DoorV + toLot, PathU = frontU + side - all0,
        };
        built.Paths.AddRange(fronts.Select(x => x.DoorU + side - all0));
        if (gWide > 0) { built.DriveU0 = gDoorU + side - all0; built.DriveU1 = built.DriveU0 + gWide - 1; }
        // The drive from the lot's front to the garage door, the path from the door.
        var drive = tech switch { TechLevel.Low => new Rgb(0x7a, 0x70, 0x60), TechLevel.High => new Rgb(0x3e, 0x40, 0x44), _ => new Rgb(0x9a, 0x96, 0x8c) };
        if (built.DriveU0 is int d0 && built.DriveU1 is int d1)
            for (int v = -toLot; v < gDoorV + (s.Garage == "carport" ? gd : 0); v++)
                for (int u = gDoorU; u < gDoorU + gWide; u++) l.Plate(f, u, v, drive, Role.Floor);
        var pale = l.S.Base.Scale(1.15f);
        foreach (var (_, du) in fronts)
            for (int v = -toLot; v < (lifted ? plan.DoorV - 2 - h.Lift : plan.DoorV); v++)
                for (int k = 0; k < 2; k++) l.Plate(f, du + k, v, pale, Role.Floor);
        l.C.X.Homes.Add(new HomeNote(s.Family.Id, Silhouette(s, plan, g != null, gWide), s.Storeys, s.Roof, s.Garage, gWide > 0 ? (s.Garage == "carport" ? "carport" : s.GarageDoor) : "",
            gWide, s.Garage == "carport" ? 3 : gWide > 0 ? 3 : 0, garageDoorCell, garageFacing, gWide > 0) { Exits = huts.SelectMany(x => x.ExitCells()).ToList() });
        return built;
    }

    /// <summary>The shape a house shows the street, in short: its plan, storeys, roof and what stands out from it.</summary>
    static string Silhouette(HouseStyle s, HousePlan p, bool garage, int gw)
    {
        var (u0, v0, u1, v1) = p.Box;
        return $"{s.Plan}/{u1 - u0 + 1}x{v1 - v0 + 1}/{s.Storeys}/{s.Roof}{(s.GableFront ? "-front" : "")}/{s.Cover}/{s.Wall}/{s.Garage}{gw}/{(s.Porch ? "p" : "")}{(s.Verandah ? "v" : "")}{(s.Bay ? "b" : "")}{(s.Dormer ? "d" : "")}{(s.Balcony ? "l" : "")}{(s.Chimney ? "c" : "")}";
    }

    // ---- roofs ---------------------------------------------------------------------------------

    /// <summary>The roof a style asks for, over the house and an attached one-storey garage where they share one.</summary>
    static void Roofs(Lot l, HouseStyle s, Hut h, Hut? g, HousePlan plan, Rng r)
    {
        int o = s.Over;
        bool attached = g != null && g.Foot.Any(c => h.Foot.Contains(c));
        bool shareGarage = attached && s.Storeys == 1 && s.Roof is "gable" or "hip" or "cross" or "pagoda";
        var both = shareGarage ? new List<Hut> { h, g! } : new List<Hut> { h };
        // A garage against the house under a roof of its own: the house's eaves stop short of it.
        if (attached && !shareGarage) h.Avoid = new HashSet<(int U, int V)>(g!.Foot.Where(c => !h.Foot.Contains(c)));
        var garageParts = shareGarage ? g!.Wings.Select(w => (w, "gable")).ToList() : new List<(Wing, string)>();
        switch (s.Roof)
        {
            case "flat":
                h.FlatRoof(1, s.Family.Id == "adobe" ? 1 : 2);
                break;
            case "turf" when plan.Round != null:
                h.Dome(o, false, false, true, plan.DoorU, plan.DoorV);
                break;
            case "turf":
                h.FlatRoof(1, 1);
                h.Turf();
                break;
            case "hip":
            case "pagoda":
                Hut.Hipped(both, o, s.Roof == "pagoda");
                if (s.Roof == "pagoda" && s.Storeys >= 2) h.Skirt(0, 2, true);
                break;
            case "mansard":
                h.Skirt(s.Storeys - 2, 1, false);
                Hut.Hipped(both, 1, false, 2);
                break;
            case "shed":
                h.Prisms(h.Wings.Select(w => (w, "shed")).ToList(), o, both);
                break;
            case "aframe":
                h.Prisms(new List<(Wing, string)> { (h.Wings[0], h.Wings[0].RidgeAlongU ? "gable" : "gable") }, h.Ring(h.Storeys - 1) + 1, both, aframe: true);
                break;
            case "wave":
                h.Prisms(h.Wings.Select(w => (w, "wave")).ToList(), 1, both);
                break;
            case "dome":
            case "cap":
            case "glass":
                h.Dome(o, s.Roof == "glass", s.Roof == "cap", s.Berm, plan.DoorU, plan.DoorV);
                break;
            case "cross":
            {
                // The main ridge along the front, a gable to the street over part of it.
                var parts = h.Wings.Select(w => (w, "gable")).Concat(garageParts).ToList();
                Wing m = h.Wings[0];
                if (m.RidgeAlongU && m.U1 - m.U0 >= 9)
                {
                    int cw = r.Chance(0.5f) ? 6 : 8, cu = r.Chance(0.5f) ? m.U0 : m.U1 - cw + 1;
                    parts.Add((new Wing(cu, m.V0, cu + cw - 1, (m.V0 + m.V1) / 2, false), "gable"));
                }
                h.Prisms(parts, o, both);
                break;
            }
            default:
                h.Prisms(h.Wings.Select(w => (w, "gable")).Concat(garageParts).ToList(), o, both);
                break;
        }
        if (g != null && !shareGarage)
        {
            if (s.Storeys > 1 && g.Foot.Any(c => h.Foot.Contains(c)) || s.Roof is "flat" or "turf" or "wave" or "shed" && s.Storeys == 1 && g.Foot.Any(c => h.Foot.Contains(c))) g.FlatRoof(1, 1);
            else if (s.Roof is "hip" or "pagoda" or "mansard") Hut.Hipped(new List<Hut> { g }, Math.Min(o, 2), s.Roof == "pagoda");
            else g.Prisms(g.Wings.Select(w => (w, s.Roof == "shed" ? "shed" : "gable")).ToList(), Math.Min(o, 2), new List<Hut> { g });
        }
        if (s.Dormer && s.Roof is "gable" or "cross" or "hip" && plan.Units.Count == 0) h.Dormers(plan.Wings[0], r);
        if (s.Dormer && plan.Units.Count > 0 && s.Roof is "gable" or "hip") h.Dormers(plan.Wings[0], r);
        if (s.Chimney && s.Roof is not ("dome" or "cap" or "glass" or "wave"))
        {
            if (plan.Units.Count > 1)
            {
                // A stack on each party wall, solid from the ground, up through the ridge.
                int mv = (plan.Wings[0].V0 + plan.Wings[0].V1) / 2 - 1;
                for (int k = 1; k < plan.Units.Count; k++) h.Chimney(k * s.UnitW, mv, 0, 1);
            }
            else
            {
                // An end wall or the back wall, wherever no door or window goes through it.
                Wing m = plan.Wings[0];
                var tries = new List<(int U, int V, int DU, int DV)>
                {
                    (m.U0, (m.V0 + m.V1) / 2 - 1, 0, 1), (m.U1, (m.V0 + m.V1) / 2 - 1, 0, 1),
                    ((m.U0 + m.U1) / 2 - 1 + 2 * r.Range(-1, 2), m.V1, 1, 0), (m.U0, m.V1 - 3, 0, 1), (m.U1, m.V1 - 3, 0, 1),
                };
                r.Shuffle(tries);
                foreach (var (cu, cv, du, dv) in tries)
                    if (h.ChimneyFits(cu, cv, du, dv)) { h.Chimney(cu, cv, du, dv); break; }
            }
        }
    }
}
