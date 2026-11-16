using System;
using System.Collections.Generic;
using System.Linq;

namespace GalHack.Structures;

/// <summary>A small craft's body plan: the outer form it is laid out as before anything goes in it (docs/ship-forms.md).</summary>
public enum CraftForm : byte { Box, Spine, Lifter, TwinBoom, Tug, Needle, Tripod, Dish }

/// <summary>
/// The body plans (docs/ship-forms.md: shape first, built round a thrust axis with the drives aft, the machinery showing,
/// lopsided). Each is a rule that rolls its own lengths, module counts, sides and offsets; the cockpit it carries is rolled
/// too (an enclosed cockpit of either size, or a glazed cab round a command chair). What sizing adds afterwards (thrust,
/// power, gyros, the hold's fill) and the lights are the same for every plan.
/// </summary>
public static partial class SmallCraft
{
    /// <summary>The body plans each role can be laid out as (the box first: the game rolls it until the owner picks).</summary>
    public static readonly IReadOnlyDictionary<CraftRole, CraftForm[]> FormsOf = new Dictionary<CraftRole, CraftForm[]>
    {
        [CraftRole.Trader] = new[] { CraftForm.Box, CraftForm.Spine, CraftForm.Lifter, CraftForm.TwinBoom, CraftForm.Tug },
        [CraftRole.Scout] = new[] { CraftForm.Box, CraftForm.Needle, CraftForm.Tripod, CraftForm.Dish },
    };

    /// <summary>Each plan's layout; true when it set its own pads (the box), else landing legs are added to it.</summary>
    static readonly Dictionary<CraftForm, Func<Lay_, bool, CraftStyle, bool>> Forms = new()
    {
        [CraftForm.Box] = BoxForm,
        [CraftForm.Spine] = SpineForm,
        [CraftForm.Lifter] = LifterForm,
        [CraftForm.TwinBoom] = TwinBoomForm,
        [CraftForm.Tug] = TugForm,
        [CraftForm.Needle] = NeedleForm,
        [CraftForm.Tripod] = TripodForm,
        [CraftForm.Dish] = DishForm,
    };

    static int R(Lay_ l, int lo, int hi) => l.Rng.Next(lo, hi + 1);

    // ---- Parts every plan is made of ---------------------------------------------------------------------

    /// <summary>
    /// The cockpit, its nose at <paramref name="zFront"/> and its floor at <paramref name="yBottom"/>, centred on x
    /// <paramref name="cx"/>: an enclosed cockpit (3x2x4 or 3x3x5), or a glazed cab of window plating round a command chair.
    /// Returns its box.
    /// </summary>
    static (Int3 Lo, Int3 Hi) Cockpit(Lay_ l, int cx, int yBottom, int zFront)
    {
        Designer d = l.D;
        int kind = l.Rng.Next(3);
        (Int3 lo, Int3 hi) box;
        if (kind < 2)
        {
            Int3 at = d.Put(kind == 0 ? "cockpit-t1" : "cockpit-t2", P(cx - 1, yBottom, zFront));
            foreach (Int3 c in d.S.CellsOf(at, d.S.Get(at))) l.Cockpit.Add(c);
            box = kind == 0 ? (P(cx - 1, yBottom, zFront), P(cx + 1, yBottom + 1, zFront + 3)) : (P(cx - 1, yBottom, zFront), P(cx + 1, yBottom + 2, zFront + 4));
        }
        else
        {
            // A cab: an iron floor and back, glass all round the chair, which fills the space inside.
            box = (P(cx - 1, yBottom, zFront), P(cx + 1, yBottom + 3, zFront + 4));
            d.Fill(box.lo, box.hi, "glass-plated");
            d.Fill(P(cx - 1, yBottom, zFront), P(cx + 1, yBottom, zFront + 4), Plate, l.Coat(1));
            d.Fill(P(cx - 1, yBottom, zFront + 4), P(cx + 1, yBottom + 3, zFront + 4), Plate, l.Coat(1));
            d.Carve(P(cx, yBottom + 1, zFront + 1), P(cx, yBottom + 2, zFront + 3));
            d.Put("command-chair", P(cx, yBottom + 1, zFront + 1));
            foreach (Int3 c in Designer.Cells(box.lo, box.hi)) l.Cockpit.Add(c);
        }
        return box;
    }

    /// <summary>A block along z, its four long edges cut at 45 degrees where it is 3 or more across (a can, a tank, an engine block); its inside cells are the machinery's room.</summary>
    static void Can(Lay_ l, Int3 lo, Int3 hi, ushort paint, bool machinery = false)
    {
        Designer d = l.D;
        d.Fill(lo, hi, Plate, paint);
        if (hi.X - lo.X >= 2 && hi.Y - lo.Y >= 2)
            foreach (var (x, dx) in new[] { (lo.X, Dir.NegX), (hi.X, Dir.PosX) })
            foreach (var (y, dy) in new[] { (lo.Y, Dir.NegY), (hi.Y, Dir.PosY) })
                d.Fill(P(x, y, lo.Z), P(x, y, hi.Z), Plate, paint, "slope", Designer.Rot(dx, dy));
        if (!machinery) return;
        for (int z = lo.Z + 1; z < hi.Z; z++)
            for (int y = lo.Y + 1; y < hi.Y; y++)
                for (int x = lo.X + 1; x < hi.X; x++) l.Inside.Add(P(x, y, z));
    }

    /// <summary>An open truss along z, its section 3x3 round (cx, cy): four rails at the corners, a frame round them every few cells.</summary>
    static void Truss(Lay_ l, int cx, int cy, int z0, int z1, ushort paint, int every)
    {
        Designer d = l.D;
        foreach (int x in new[] { cx - 1, cx + 1 })
            foreach (int y in new[] { cy - 1, cy + 1 })
                d.Fill(P(x, y, z0), P(x, y, z1), Plate, paint);
        for (int z = z0; z <= z1; z += every)
        {
            d.Fill(P(cx - 1, cy - 1, z), P(cx + 1, cy + 1, z), Plate, paint);
            d.Carve(P(cx, cy, z), P(cx, cy, z));
        }
        d.Fill(P(cx - 1, cy - 1, z1), P(cx + 1, cy + 1, z1), Plate, paint);
        d.Carve(P(cx, cy, z1), P(cx, cy, z1));
    }

    /// <summary>A rack of containers in a frame: its two end faces plates, every cell between them a container.</summary>
    static void Rack(Lay_ l, Int3 lo, Int3 hi, ushort paint)
    {
        Designer d = l.D;
        d.Fill(lo, hi, Plate, paint);
        for (int z = lo.Z + 1; z < hi.Z; z++)
            for (int y = lo.Y; y <= hi.Y; y++)
                for (int x = lo.X; x <= hi.X; x++) d.Put("container-t1", P(x, y, z));
    }

    /// <summary>A round boom along z (cylinder plates) from z0 to z1 at (x, y).</summary>
    static void Boom(Lay_ l, int x, int y, int z0, int z1, ushort paint) =>
        l.D.Fill(P(x, y, Math.Min(z0, z1)), P(x, y, Math.Max(z0, z1)), Plate, paint, "cylinder", Designer.Rot(Dir.PosZ, Dir.NegY));

    /// <summary>A mast of cylinders straight up from (x, y0, z), <paramref name="h"/> high, a cross-arm and a scanner on top.</summary>
    static void Mast(Lay_ l, int x, int y0, int z, int h, ushort paint)
    {
        Designer d = l.D;
        d.Fill(P(x, y0, z), P(x, y0 + h - 1, z), Plate, paint, "cylinder");
        d.Fill(P(x - 1, y0 + h, z), P(x + 1, y0 + h, z), Plate, l.Coat(2));
        d.Put("scanner-t1", P(x + (l.Rng.Next(2) == 0 ? -1 : 1), y0 + h + 1, z));
    }

    /// <summary>
    /// Landing legs: at each corner of its plan, the column nearest it whose lowest block can hold one gets a strut of plates
    /// down to its lowest level and a pad under that, so it stands level whatever hangs lowest.
    /// </summary>
    static void LandingLegs(Lay_ l)
    {
        Structure s = l.S;
        var (lo, hi) = s.Bounds();
        var lowest = new Dictionary<(int X, int Z), int>();
        foreach (var (p, _) in s.Cells())
            if (!lowest.TryGetValue((p.X, p.Z), out int y) || p.Y < y) lowest[(p.X, p.Z)] = p.Y;
        bool Holds((int X, int Z) k)
        {
            if (!s.TryGetBlock(P(k.X, lowest[k], k.Z), out _, out Block b) || l.Cockpit.Contains(P(k.X, lowest[k], k.Z))) return false;
            BlockType t = s.TypeOf(b);
            return t.IsArmor ? s.ShapeOf(b) == s.Catalogue.Cube : t.IsContainer || t.IsCore;
        }
        var used = new List<(int X, int Z)>();
        foreach (var (cx, cz) in new[] { (lo.X, lo.Z), (hi.X, lo.Z), (lo.X, hi.Z), (hi.X, hi.Z) })
        {
            var pick = lowest.Keys.Where(k => Holds(k) && used.All(u => u.X != k.X || Math.Abs(u.Z - k.Z) >= 2))
                .OrderBy(k => Math.Abs(k.X - cx) + Math.Abs(k.Z - cz)).Cast<(int X, int Z)?>().FirstOrDefault();
            if (pick is not (int x, int z)) continue;
            used.Add((x, z));
            for (int y = lowest[(x, z)] - 1; y >= lo.Y; y--) l.D.Fill(P(x, y, z), P(x, y, z), Plate, l.Coat(1));
            l.D.Put("pad-t1", P(x, lo.Y - 1, z));
            for (int k = 1; k <= 3; k++) { l.Reserved.Add(P(x, lo.Y - 1 - k, z)); l.Reserved.Add(P(x, lo.Y - 1 - k, z + 1)); }
        }
    }

    /// <summary>The hull number down both sides of a box's middle row, and a band of livery along them.</summary>
    static void Marks(Lay_ l, Int3 lo, Int3 hi)
    {
        int y = (lo.Y + hi.Y + 1) / 2;
        l.Number.Add((P(hi.X, y, Math.Min(hi.Z - 1, lo.Z + 3)), P(0, 0, -1), Dir.PosX));
        l.Number.Add((P(lo.X, y, Math.Max(lo.Z + 1, Math.Min(hi.Z - 1, lo.Z + 3) - 2)), P(0, 0, 1), Dir.NegX));
        l.Band.Add((P(lo.X, lo.Y + 1 > hi.Y - 1 ? y : lo.Y + 1, lo.Z), P(lo.X, lo.Y + 1 > hi.Y - 1 ? y : lo.Y + 1, hi.Z), Dir.NegX));
        l.Band.Add((P(hi.X, lo.Y + 1 > hi.Y - 1 ? y : lo.Y + 1, lo.Z), P(hi.X, lo.Y + 1 > hi.Y - 1 ? y : lo.Y + 1, hi.Z), Dir.PosX));
    }

    /// <summary>Whether a box is clear of every block laid so far.</summary>
    static bool Clear(Lay_ l, Int3 lo, Int3 hi) => Designer.Cells(lo, hi).All(c => !l.S.Has(c));

    // ---- Traders ------------------------------------------------------------------------------------------------

    /// <summary>The Freeholder plan: a command pod on the nose of an open truss spine, mismatched modules clamped on at odd sides and offsets (container racks, cans, tanks), the drive block at the stern.</summary>
    static bool SpineForm(Lay_ l, bool trader, CraftStyle style)
    {
        Designer d = l.D;
        int len = R(l, 9, 15), zf = -(len + 6) / 2;
        var (_, chi) = Cockpit(l, 0, -1, zf);
        int zs = chi.Z + 1, zt = zs + len - 1;
        Truss(l, 0, 0, zs, zt - 4, l.Coat(4), R(l, 2, 4));
        Int3 elo = P(-2, -1, zt - 3), ehi = P(2, 1 + R(l, 0, 1), zt);
        Can(l, elo, ehi, l.Coat(0), machinery: true);
        l.Heart = P(0, 0, zt - 2);
        d.Put("core", l.Heart);
        l.Inside.Remove(l.Heart);
        l.AxisY = 0;
        Marks(l, elo, ehi);

        // Modules clamped round the spine, each its own size, side and coat; a few tries each to find room.
        int modules = R(l, 2, 4), placed = 0;
        for (int tries = 0; tries < 40 && placed < modules; tries++)
        {
            int kind = l.Rng.Next(3), mlen = R(l, 3, 6), w = R(l, 2, 3), h = R(l, 2, 3);
            if (kind > 0) (w, h) = (3, 3); // a can or a tank is round: 3 across
            int z0 = R(l, zs, Math.Max(zs, zt - 4 - mlen)), z1 = Math.Min(z0 + mlen - 1, zt - 4);
            if (z1 - z0 < 2) continue;
            Dir side = new[] { Dir.PosX, Dir.NegX, Dir.PosY, Dir.NegY }[l.Rng.Next(4)];
            int off = R(l, -1, 1);
            Int3 lo, hi;
            switch (side)
            {
                case Dir.PosX: lo = P(2, off - h / 2, z0); hi = P(1 + w, off - h / 2 + h - 1, z1); break;
                case Dir.NegX: lo = P(-1 - w, off - h / 2, z0); hi = P(-2, off - h / 2 + h - 1, z1); break;
                case Dir.PosY: lo = P(off - w / 2, 2, z0); hi = P(off - w / 2 + w - 1, 1 + h, z1); break;
                default: lo = P(off - w / 2, -1 - h, z0); hi = P(off - w / 2 + w - 1, -2, z1); break;
            }
            if (!Clear(l, lo - P(0, 0, 1), hi + P(0, 0, 1))) continue;
            ushort coat = l.Coat(R(l, 1, 7));
            if (kind == 0) Rack(l, lo, hi, coat);
            else Can(l, lo, hi, coat);
            placed++;
        }
        return false;
    }

    /// <summary>A lifter: a cab and engine body up top, a cradle of containers slung under it on four hangers, drive pods out to the sides, one longer than the other.</summary>
    static bool LifterForm(Lay_ l, bool trader, CraftStyle style)
    {
        Designer d = l.D;
        int len = R(l, 7, 10), zf = -(len + 5) / 2;
        var (_, chi) = Cockpit(l, 0, 2, zf);
        int zb0 = chi.Z + 1, zb = zb0 + len - 1;
        Int3 blo = P(-2, 2, zb0), bhi = P(2, 4, zb);
        Can(l, blo, bhi, l.Coat(0), machinery: true);
        l.Heart = P(0, 3, zb0 + 1);
        d.Put("core", l.Heart);
        l.Inside.Remove(l.Heart);
        l.AxisY = 3;
        Marks(l, blo, bhi);
        // The cradle under it, hung on four hangers.
        int drop = R(l, 2, 4), cz0 = zb0 + R(l, -2, 0), cz1 = zb - R(l, 0, 2);
        int top = 1 - drop, cw = R(l, 1, 2);
        foreach (int x in new[] { -1, 1 })
            foreach (int z in new[] { zb0 + 1, zb - 1 })
                d.Fill(P(x, top + 1, z), P(x, 1, z), Plate, l.Coat(4));
        Rack(l, P(-cw, top - 1, cz0), P(cw, top, cz1), l.Coat(R(l, 1, 7)));
        d.Fill(P(-2, top + 1, cz0), P(2, top + 1, cz0), Plate, l.Coat(4)).Fill(P(-2, top + 1, cz1), P(2, top + 1, cz1), Plate, l.Coat(4));
        // Drive pods out on either side, their lengths and heights unequal.
        foreach (int side in new[] { 1, -1 })
        {
            int plen = R(l, 3, len), y = 3;
            d.Fill(P(side * 3, y, zb - 1), P(side * 3, y, zb - 1), Plate, l.Coat(4));
            Can(l, P(side > 0 ? 4 : -5, y - 1 + (side > 0 ? 0 : 1), zb - plen + 1), P(side > 0 ? 5 : -4, y + (side > 0 ? 0 : 1), zb), l.Coat(R(l, 1, 7)));
        }
        return false;
    }

    /// <summary>Twin booms: the cockpit up front between two long booms of unequal length, a drive bridge across their sterns, a cargo pod slung between them.</summary>
    static bool TwinBoomForm(Lay_ l, bool trader, CraftStyle style)
    {
        Designer d = l.D;
        int w = R(l, 3, 4), len = R(l, 13, 17), zf = -len / 2;
        var (_, chi) = Cockpit(l, 0, -1, zf);
        int zb = zf + len - 1;
        foreach (int side in new[] { 1, -1 })
        {
            int z0 = zf + R(l, 0, 3), z1 = zb + R(l, 0, 2), x0 = side > 0 ? w : -w - 1;
            d.Fill(P(x0, -1, z0), P(x0 + 1, 0, z1), Plate, l.Coat(4));
            d.Fill(P(x0, -1, z0 - 1), P(x0 + 1, -1, z0 - 1), Plate, l.Coat(2), "slope", Designer.Rot(Dir.NegZ, Dir.PosY));
        }
        d.Fill(P(-w, -1, chi.Z + 1), P(w, -1, chi.Z + 1), Plate, l.Coat(4));
        Int3 elo = P(-w + 1, -1, zb - 3), ehi = P(w - 1, 1, zb);
        Can(l, elo, ehi, l.Coat(0), machinery: true);
        l.Heart = P(0, 0, zb - 2);
        d.Put("core", l.Heart);
        l.Inside.Remove(l.Heart);
        l.AxisY = 0;
        Marks(l, P(-w - 1, -1, zf), P(w + 1, 0, zb));
        // The cargo pod between the booms, or two, a gap between.
        int pz0 = chi.Z + 2, pz1 = zb - 4;
        if (pz1 - pz0 >= 6 && l.Rng.Next(2) == 0)
        {
            int mid = (pz0 + pz1) / 2;
            Rack(l, P(-w + 1, -1, pz0), P(w - 1, 0, mid - 1), l.Coat(R(l, 1, 7)));
            Rack(l, P(-w + 1, -2, mid + 1), P(w - 1, 0, pz1), l.Coat(R(l, 1, 7)));
        }
        else if (pz1 - pz0 >= 2) Rack(l, P(-w + 1, -1 - R(l, 0, 1), pz0), P(w - 1, 0, pz1), l.Coat(R(l, 1, 7)));
        return false;
    }

    /// <summary>A tug: a stubby cab and engine block with big drive pods out on outriggers, towing a short string of container racks on a coupling bar.</summary>
    static bool TugForm(Lay_ l, bool trader, CraftStyle style)
    {
        Designer d = l.D;
        int zf = -12;
        var (_, chi) = Cockpit(l, 0, -1, zf);
        int zb0 = chi.Z + 1, zb = zb0 + R(l, 3, 4);
        Int3 blo = P(-2, -1, zb0), bhi = P(2, 2, zb);
        Can(l, blo, bhi, l.Coat(0), machinery: true);
        l.Heart = P(0, 0, zb0 + 1);
        d.Put("core", l.Heart);
        l.Inside.Remove(l.Heart);
        l.AxisY = 0.5f;
        Marks(l, blo, bhi);
        // The drive pods on their outriggers, wider than anything they tow: the mains go on them.
        int reach = R(l, 4, 5);
        foreach (int side in new[] { 1, -1 })
        {
            int y = R(l, 0, 1);
            d.Fill(P(side * 3, y, zb0 + 1), P(side * (reach - 1), y, zb0 + 1), Plate, l.Coat(4));
            Can(l, P(side > 0 ? reach : -reach - 1, y - 1, zb0 - 1), P(side > 0 ? reach + 1 : -reach, y, zb), l.Coat(R(l, 1, 7)));
        }
        l.Mains = c => (Math.Abs(c.X) >= reach ? 0f : 60f) + c.Z * 0.3f;
        // The string: a coupling bar, then a rack, again and again, each its own size.
        int z = zb + 1, racks = R(l, 2, 4);
        for (int i = 0; i < racks; i++)
        {
            d.Fill(P(0, 0, z), P(0, 0, z + 1), Plate, l.Coat(4));
            z += 2;
            int rl = R(l, 3, 5), rw = R(l, 0, 1);
            Rack(l, P(-1 - rw, -1, z), P(1, 1, z + rl - 1), l.Coat(R(l, 1, 7)));
            z += rl;
        }
        return false;
    }

    // ---- Scouts -------------------------------------------------------------------------------------------------

    /// <summary>A needle: a probe out ahead of the cockpit, a slim machinery body, a tail block for the drives, thin outrigger pods on pylons and a sensor mast.</summary>
    static bool NeedleForm(Lay_ l, bool trader, CraftStyle style)
    {
        Designer d = l.D;
        int zf = -8;
        var (_, chi) = Cockpit(l, 0, -1, zf);
        Boom(l, 0, 0, zf - R(l, 3, 6), zf - 1, l.Coat(4));
        int bl = R(l, 3, 5), zb0 = chi.Z + 1, zb = zb0 + bl - 1;
        Int3 blo = P(-1, -1, zb0), bhi = P(1, 1, zb);
        Can(l, blo, bhi, l.Coat(0), machinery: true);
        int tail = R(l, 2, 5);
        Boom(l, 0, 0, zb + 1, zb + tail, l.Coat(4));
        Can(l, P(-1, -1, zb + tail + 1), P(1, 1, zb + tail + 2), l.Coat(R(l, 1, 7)));
        l.Heart = P(0, 0, zb0 + 1);
        d.Put("core", l.Heart);
        l.Inside.Remove(l.Heart);
        l.AxisY = 0;
        Marks(l, blo, bhi);
        foreach (int side in new[] { 1, -1 })
        {
            int z = R(l, zb0, zb), plen = R(l, 3, 6), y = 0;
            d.Fill(P(side * 2, y, z), P(side * 2, y, z), Plate, l.Coat(4));
            d.Fill(P(side * 3, y, z - plen / 2), P(side * 3, y, z - plen / 2 + plen - 1), Plate, l.Coat(R(l, 1, 7)));
        }
        Mast(l, 0, 2, R(l, zb0, zb), R(l, 3, 5), l.Coat(4));
        return false;
    }

    /// <summary>A tripod: the cockpit and a short body on three drive pods held out on struts, two low and one high, each its own length.</summary>
    static bool TripodForm(Lay_ l, bool trader, CraftStyle style)
    {
        Designer d = l.D;
        int zf = -6;
        var (_, chi) = Cockpit(l, 0, -1, zf);
        int zb0 = chi.Z + 1, zb = zb0 + R(l, 2, 4);
        Int3 blo = P(-1, -1, zb0), bhi = P(1, 1, zb);
        Can(l, blo, bhi, l.Coat(0), machinery: true);
        l.Heart = P(0, 0, zb0 + 1);
        d.Put("core", l.Heart);
        l.Inside.Remove(l.Heart);
        l.AxisY = -1;
        Marks(l, blo, bhi);
        int zm = (zb0 + zb) / 2, spread = R(l, 3, 4);
        foreach (int side in new[] { 1, -1 })
        {
            int plen = R(l, 4, 7), x0 = side > 0 ? spread : -spread - 1;
            d.Fill(P(side * 2, 0, zm), P(side * spread, 0, zm), Plate, l.Coat(4));
            Can(l, P(x0, -3, zm - plen / 2), P(x0 + 1, -2, zm - plen / 2 + plen - 1), l.Coat(R(l, 1, 7)));
            d.Fill(P(side * spread, -1, zm), P(side * spread, -1, zm), Plate, l.Coat(4));
        }
        int tx = R(l, -1, 0), tlen = R(l, 4, 7);
        d.Fill(P(0, 2, zm), P(0, 2, zm), Plate, l.Coat(4));
        Can(l, P(tx, 3, zm - tlen / 2), P(tx + 1, 4, zm - tlen / 2 + tlen - 1), l.Coat(R(l, 1, 7)));
        Mast(l, tx, 5, zm + R(l, -1, 1), R(l, 3, 4), l.Coat(4));
        return false;
    }

    /// <summary>A dish boat: a long boom off the hull, out ahead or to one side, carrying a big round dish facing forward, the drives on a body aft.</summary>
    static bool DishForm(Lay_ l, bool trader, CraftStyle style)
    {
        Designer d = l.D;
        int zf = -5;
        var (clo, chi) = Cockpit(l, 0, -1, zf);
        int zb0 = chi.Z + 1, zb = zb0 + R(l, 3, 5);
        Int3 blo = P(-2, -1, zb0), bhi = P(1 + R(l, 0, 1), 1, zb);
        Can(l, blo, bhi, l.Coat(0), machinery: true);
        l.Heart = P(0, 0, zb0 + 1);
        d.Put("core", l.Heart);
        l.Inside.Remove(l.Heart);
        l.AxisY = 0;
        Marks(l, blo, bhi);
        // The boom: from the body's top (or its flank), out past the nose; the dish on its tip.
        bool flank = l.Rng.Next(2) == 0;
        int bx = flank ? bhi.X + 1 : R(l, -1, 0), by = flank ? 0 : Math.Max(bhi.Y, chi.Y) + 1;
        int tip = zf - R(l, 3, 7);
        if (!flank) d.Fill(P(bx, bhi.Y + 1, zb0), P(bx, by - 1, zb0), Plate, l.Coat(4));
        Boom(l, bx, by, tip, zb0 + 1, l.Coat(4));
        int r = R(l, 2, 3);
        for (int y = -r; y <= r; y++)
            for (int x = -r; x <= r; x++)
                if (x * x + y * y <= r * r + 1 && (x != 0 || y != 0))
                    d.Fill(P(bx + x, by + y, tip - 1), P(bx + x, by + y, tip - 1), Plate, l.Coat(3), x * x + y * y >= r * r - 1 ? "slope" : null,
                        x * x + y * y >= r * r - 1 ? Designer.Rot(Dir.NegZ, Math.Abs(x) >= Math.Abs(y) ? (x > 0 ? Dir.PosX : Dir.NegX) : (y > 0 ? Dir.PosY : Dir.NegY)) : null);
        d.Fill(P(bx, by, tip - 1), P(bx, by, tip - 1), Plate, l.Coat(4));
        d.Put("scanner-t1", P(0, bhi.Y + 1, zb));
        return false;
    }
}
