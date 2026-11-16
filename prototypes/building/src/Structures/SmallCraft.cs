using System;
using System.Collections.Generic;
using System.Linq;

namespace GalHack.Structures;

/// <summary>What a small vessel is for: a trader goes place to place with cargo pods, a scout circles places with a sensor mast.</summary>
public enum CraftRole : byte { Trader, Scout }

/// <summary>
/// A faction's look for the small-vessel generator (design, "Ships and structures by faction"): the coats its hulls
/// come in, the marks painted on them, how filthy they get, how often a plate is patched in another coat or another
/// plating, how often a hull is painted with a band of livery, and whether its bolted-on modules may sit on one side only.
/// </summary>
public sealed record CraftStyle(string Owner, uint[] Coats, uint[] Marks, int GrimeLow, int GrimeHigh, float Patches, float Livery, bool Lopsided)
{
    /// <summary>Freeholders: patched, ramshackle converted haulers in sun-faded coats, hand-painted hull numbers, modules bolted on where they fit.</summary>
    public static readonly CraftStyle Freeholder = new("freeholders",
        new uint[] { 0xb8a27a, 0x7f9c96, 0xa86a4c, 0xd6cfb8, 0x8a8f6a, 0x8c99a8, 0x9c7d5a, 0xb0a48c },
        new uint[] { 0xe8e0cc, 0x2b2d42, 0xd9b54a },
        2, 3, 0.16f, 0.6f, true);
}

/// <summary>
/// What a generated craft must manage on the world it flies, by the player's own numbers (StructureStats, which
/// HandlingModel reads the same way): lift as a share of its weight, push in each direction (m/s²), turn (gyro torque
/// over mass, °/s), generation over full-load use (its core's capacitor is drawn only for what the generators can't
/// keep up with), and hours of generator fuel at full load, carried in core storage (power.json: there are no fuel tanks;
/// <see cref="FuelMJPerSu"/> is the energy its fuel packs into a storage unit). One place for the sizing.
/// </summary>
public sealed record CraftNeeds(float Lift = 1.5f, float Forward = 6f, float Back = 5f, float Side = 3f, float Down = 3f,
    float Turn = 25f, float Power = 1.25f, float FuelHours = 1.5f, float FuelMJPerSu = 54f)
{
    public static readonly CraftNeeds Default = new();

    /// <summary>The generator fuel it carries, MJ: its hours at full load.</summary>
    public double FuelMJ(StructureStats st) => st.PowerUse * 3600 * FuelHours;

    /// <summary>The storage that fuel takes, SU.</summary>
    public double FuelSu(StructureStats st) => FuelMJ(st) / FuelMJPerSu;
}

/// <summary>
/// Rolls a small vessel (a trader or a scout) from parts on the 1 m grid, in a faction's style: a cockpit up front, a
/// body behind it holding the core in its belly and the generator and gyros inside, then the role's modules
/// bolted on (cargo pods of real containers, or a sensor mast and engine booms), landing pads in the belly, and
/// thrusters wherever a hull face looks onto clear air in the direction they push, as many as its mass needs on
/// every axis. Lights go on its faces: landing lights under it, red to port, green to starboard, white aft and a beacon
/// on top. It is laid out, then built block by block as a player builds (each block placed against one already
/// there, the core first), so only what a player could place is in it, and it flies only by what it is made of.
/// </summary>
public static partial class SmallCraft
{
    const string Plate = "iron-plated", Patch = "base-block";

    /// <summary>A craft for this role and style, sized for this gravity; null if no roll met the needs (the caller tries another seed).</summary>
    public static Structure Roll(Catalogue cat, CraftRole role, CraftStyle style, int seed, float gravity, CraftNeeds needs = null) =>
        Roll(cat, role, style, seed, gravity, out _, needs);

    /// <param name="why">What the last roll fell short in, when none met the needs.</param>
    /// <param name="form">The body plan (null: one of the role's plans, rolled from the seed).</param>
    public static Structure Roll(Catalogue cat, CraftRole role, CraftStyle style, int seed, float gravity, out string why, CraftNeeds needs = null, CraftForm? form = null)
    {
        needs ??= CraftNeeds.Default;
        why = null;
        CraftForm[] plans = FormsOf[role];
        CraftForm body = form ?? plans[new Random(seed).Next(plans.Length)];
        for (int attempt = 0; attempt < 6; attempt++)
        {
            var rng = new Random(unchecked(seed + attempt * 7919));
            Structure plan = Lay(cat, role, style, rng, gravity, needs, body);
            Structure built = Assemble(plan, style.Owner);
            why = Shortfall(built, gravity, needs);
            Dropped = string.Join(", ", plan.Blocks().Where(b => !built.Has(b.pos)).Select(b => plan.TypeOf(b.block).Id + (plan.TypeOf(b.block).IsArmor ? "/" + plan.ShapeOf(b.block).Id : "")));
            if (why == null) return built;
            if (built.BlockCount < plan.BlockCount)
                why += $" ({plan.BlockCount - built.BlockCount} of {plan.BlockCount} blocks would not go on: {string.Join(", ", plan.Blocks().Where(b => !built.Has(b.pos)).Select(b => plan.TypeOf(b.block).Id + (plan.TypeOf(b.block).IsArmor ? "/" + plan.ShapeOf(b.block).Id : "")).Distinct())})";
        }
        return null;
    }

    /// <summary>Tests: the blocks of the last roll's plan that would not go on (each would need a block it could hang on).</summary>
    public static string Dropped { get; private set; } = "";

    /// <summary>Why a structure falls short of the needs here, or null when it meets them all.</summary>
    public static string Shortfall(Structure s, float gravity, CraftNeeds needs)
    {
        StructureStats st = StructureStats.Compute(s);
        double m = Math.Max(st.Mass, 1e-3), use = Math.Max(st.PowerUse, 1e-6);
        if (!st.HasCore) return "no core";
        if (!s.Blocks().Any(b => s.TypeOf(b.block).IsCommandChair)) return "no command chair";
        if (!st.Powered || st.PowerOut < needs.Power * use) return $"power {st.PowerOut:F2} of {needs.Power * use:F2} MW";
        if (st.StorageCapacity < needs.FuelSu(st) * 1.5) return $"room for fuel {st.StorageCapacity:0} of {needs.FuelSu(st) * 1.5:0} SU";
        if (st.Thrust[(int)Dir.PosY] < needs.Lift * m * gravity) return $"lift {st.Thrust[(int)Dir.PosY]:0} of {needs.Lift * m * gravity:0} kN";
        if (st.Thrust[(int)Dir.NegZ] / m < needs.Forward) return "forward thrust";
        if (st.Thrust[(int)Dir.PosZ] / m < needs.Back) return "braking thrust";
        if (st.Thrust[(int)Dir.PosX] / m < needs.Side || st.Thrust[(int)Dir.NegX] / m < needs.Side) return "side thrust";
        if (st.Thrust[(int)Dir.NegY] / m < needs.Down) return "down thrust";
        if (Torque(s) / m < needs.Turn) return "gyros";
        return null;
    }

    static float Torque(Structure s) => s.Blocks().Where(b => s.TypeOf(b.block).Function == "gyro").Sum(b => s.TypeOf(b.block).Torque);

    // ---- Laying it out ------------------------------------------------------------------------------------

    static Int3 P(int x, int y, int z) => new(x, y, z);

    sealed class Lay_
    {
        public Designer D;
        public Random Rng;
        public readonly HashSet<Int3> Reserved = new(); // exhaust lines, pad legs: nothing goes there
        public readonly List<Int3> Inside = new();      // interior cells, for devices
        public readonly HashSet<Int3> Cockpit = new();
        public Structure S => D.S;
        /// <summary>A coat by index (each module takes its own), and a dirt level within the style's.</summary>
        public Func<int, ushort> Coat;
        public Func<int> Grime;
        public uint[] Coats;
        /// <summary>Where the machinery sits (devices that don't fit inside go on the hull nearest it), and the thrust axis's height.</summary>
        public Int3 Heart;
        public float AxisY = 0.5f;
        /// <summary>Where the mains want to go, lower first (null: round the thrust axis at the stern).</summary>
        public Func<Int3, float> Mains;
        /// <summary>Runs of armour cells the hull number is painted along (start, step, the face it shows on), and boxes a band of livery goes round.</summary>
        public readonly List<(Int3 Start, Int3 Step, Dir Face)> Number = new();
        public readonly List<(Int3 Lo, Int3 Hi, Dir Face)> Band = new();
    }

    static Structure Lay(Catalogue cat, CraftRole role, CraftStyle style, Random rng, float gravity, CraftNeeds needs, CraftForm form)
    {
        int number = rng.Next(10, rng.Next(2) == 0 ? 100 : 1000);
        var d = new Designer(cat, number.ToString(), CoreMode.Flight);
        var l = new Lay_ { D = d, Rng = rng, Coats = style.Coats.OrderBy(_ => rng.Next()).ToArray() };
        l.Grime = () => rng.Next(style.GrimeLow, style.GrimeHigh + 1);
        l.Coat = i => d.Paint($"coat{i % l.Coats.Length}", Paint.Solid(l.Coats[i % l.Coats.Length]) with { Grime = (byte)l.Grime() });
        bool trader = role == CraftRole.Trader;

        // The body plan lays the form, its machinery room, where its number and band go, and (the box) its pads.
        bool pads = Forms[form](l, trader, style);
        Dress(l, style, number);
        if (!pads) LandingLegs(l);
        l.Inside.Sort((a, b) => Int3.Dot(a - l.Heart, a - l.Heart).CompareTo(Int3.Dot(b - l.Heart, b - l.Heart)));

        // The power and gyro inside, a hold for its fuel; then thrust and the rest, sized by its mass.
        Device(l, "generator-t1");
        Device(l, "gyro-t1");
        // A trader's hold fills its inside but for a few cells kept for what sizing adds; then it is sized with its hold aboard.
        if (trader) while (l.Inside.Count > 4 && Device(l, "container-t1")) { }
        else Device(l, "container-t1"); // a scout's locker
        Size(l, gravity, needs, trader);
        Lights(l, rng);
        return d.S;
    }

    /// <summary>The box: a cockpit up front, a body behind it on a belly with pads set in its corners, the role's pods or mast bolted on.</summary>
    static bool BoxForm(Lay_ l, bool trader, CraftStyle style)
    {
        Designer d = l.D;
        Random rng = l.Rng;
        ushort body = l.Coat(0), belly = l.Coat(1), flank = l.Coat(2);
        int hw = trader ? 2 : 1, top = trader ? 2 : 1;
        int len = trader ? rng.Next(7, 11) : rng.Next(5, 7);
        int zf = -(4 + len) / 2, zb0 = zf + 4, zb = zb0 + len - 1;

        // The belly under it all, the core in it under the body; the cockpit up front; the body behind it.
        d.Fill(P(-hw, -1, zf), P(hw, -1, zb), Plate, belly);
        d.Put("core", P(0, -1, zb0 + 1));
        Int3 cockpit = d.Put("cockpit-t1", P(-1, 0, zf));
        foreach (Int3 c in d.S.CellsOf(cockpit, d.S.Get(cockpit))) l.Cockpit.Add(c);
        if (hw > 1)
        {
            d.Fill(P(-hw, 0, zf + 1), P(-2, 1, zf + 3), Plate, flank).Fill(P(2, 0, zf + 1), P(hw, 1, zf + 3), Plate, flank);
            d.Chamfer(P(-hw, 0, zf), P(-hw, 1, zf), Plate, flank, Dir.NegX, Dir.NegZ).Chamfer(P(hw, 0, zf), P(hw, 1, zf), Plate, flank, Dir.PosX, Dir.NegZ);
        }
        d.Fill(P(-hw, 0, zb0), P(hw, top, zb), Plate, body);
        if (top > 1) d.Slope(P(-hw, top, zb0), P(hw, top, zb0), Plate, body, Dir.NegZ);
        for (int z = zb0 + 1; z < zb; z++)
            for (int y = 0; y < top; y++)
                for (int x = -hw + 1; x <= hw - 1; x++) l.Inside.Add(P(x, y, z));
        l.Heart = P(0, 0, zb0);
        l.Band.Add((P(-hw, 0, zb0), P(-hw, 0, zb), Dir.NegX));
        l.Band.Add((P(hw, 0, zb0), P(hw, 0, zb), Dir.PosX));
        l.Number.Add((P(hw, top, zb0 + 4), P(0, 0, -1), Dir.PosX));
        l.Number.Add((P(-hw, top, zb0 + 2), P(0, 0, 1), Dir.NegX));

        // The role's modules, bolted on.
        if (trader) CargoPods(l, hw, top, zb0, zb, () => l.Coat(rng.Next(l.Coats.Length)), style.Lopsided);
        else SensorMast(l, hw, top, zb0, zb, body, () => l.Coat(rng.Next(l.Coats.Length)), style.Lopsided);

        // Pads in the belly's corners, their legs' room kept clear below.
        foreach (int x in new[] { -hw, hw })
            foreach (int z in new[] { zf + 1, zb - 2 })
            {
                d.Put("pad-t1", P(x, -1, z));
                for (int k = 1; k <= 3; k++) { l.Reserved.Add(P(x, -1 - k, z)); l.Reserved.Add(P(x, -1 - k, z + 1)); }
            }
        return true;
    }

    /// <summary>Livery and patches on the plates as laid: a faded band, odd plates patched in another coat or plating, the hull number hand-painted.</summary>
    static void Dress(Lay_ l, CraftStyle style, int number)
    {
        Designer d = l.D;
        Random rng = l.Rng;
        if (rng.NextDouble() < style.Livery)
        {
            ushort band = d.Paint("band", Paint.Solid(l.Coats[0]).WithPattern(rng.Next(2) == 0 ? Pattern.Stripes : Pattern.Chevrons, l.Coats[3]) with { Grime = (byte)l.Grime() });
            foreach (var (lo, hi, face) in l.Band) d.Face(lo, hi, face, band);
        }
        foreach (var (p, b) in d.S.Blocks().ToList())
        {
            if (!d.S.TypeOf(b).IsArmor || d.S.ShapeOf(b) != d.S.Catalogue.Cube || rng.NextDouble() >= style.Patches) continue;
            d.Fill(p, p, rng.Next(3) == 0 ? Patch : Plate, l.Coat(3 + rng.Next(l.Coats.Length - 3)));
        }
        // Along each run, on the first stretch of plain plates long enough for it.
        uint mark = style.Marks[rng.Next(style.Marks.Length)];
        string digits = number.ToString();
        bool Plain(Int3 c) => d.S.TryGet(c, out Block b) && !b.IsFiller && d.S.TypeOf(b).IsArmor && d.S.ShapeOf(b) == d.S.Catalogue.Cube;
        foreach (var (start, step, face) in l.Number)
            for (int from = 0; from < 12; from++)
            {
                if (!Enumerable.Range(from, digits.Length).All(i => Plain(start + step * i))) continue;
                for (int i = 0; i < digits.Length; i++)
                {
                    ushort glyph = d.Paint($"n{i}", Paint.Solid(l.Coats[0]).WithStencil(digits[i], mark) with { Grime = (byte)l.Grime() });
                    d.Face(start + step * (from + i), start + step * (from + i), face, glyph);
                }
                break;
            }
    }

    /// <summary>Cargo pods of containers strapped along the flanks (one side only, sometimes), and maybe one on the roof; each pod's ends are plates in its own coat.</summary>
    static void CargoPods(Lay_ l, int hw, int top, int zb0, int zb, Func<ushort> coat, bool lopsided)
    {
        Designer d = l.D;
        int pods = 0;
        foreach (int side in new[] { 1, -1 })
        {
            if (side < 0 && lopsided && pods > 0 && l.Rng.NextDouble() < 0.35) continue;
            int z0 = zb0 + l.Rng.Next(0, 2), z1 = Math.Min(zb - l.Rng.Next(0, 2), z0 + l.Rng.Next(3, 6));
            int x0 = side * (hw + 1), x1 = side * (hw + 2);
            ushort c = coat();
            d.Fill(P(x0, 0, z0), P(x1, 1, z1), Plate, c);
            for (int z = z0 + 1; z < z1; z++)
                foreach (int x in new[] { x0, x1 })
                    for (int y = 0; y <= 1; y++) d.Put("container-t1", P(x, y, z));
            pods++;
        }
        if (l.Rng.NextDouble() < 0.5)
        {
            int z0 = zb0 + 1, z1 = Math.Min(zb - 1, z0 + l.Rng.Next(2, 5));
            ushort c = coat();
            d.Fill(P(-1, top + 1, z0), P(1, top + 1, z1), Plate, c);
            for (int z = z0 + 1; z < z1; z++) d.Put("container-t1", P(0, top + 1, z));
        }
    }

    /// <summary>A scout's sensor mast up off its back with a cross-arm, and engine booms out either side of its tail (or on one side and a sensor pod on the other).</summary>
    static void SensorMast(Lay_ l, int hw, int top, int zb0, int zb, ushort body, Func<ushort> coat, bool lopsided)
    {
        Designer d = l.D;
        int h = l.Rng.Next(3, 6), z = zb - 1;
        d.Fill(P(0, top + 1, z), P(0, top + h, z), Plate, body, "cylinder");
        d.Fill(P(-1, top + h + 1, z), P(1, top + h + 1, z), Plate, coat());
        ushort boom = coat();
        bool pod = lopsided && l.Rng.NextDouble() < 0.4;
        foreach (int side in new[] { 1, -1 })
        {
            int x = side * (hw + 1);
            if (pod && side < 0) d.Fill(P(x, 0, zb0 + 1), P(x, 0, zb0 + 2), Plate, coat());
            else d.Fill(P(x, 0, zb - 2), P(x, 0, zb), Plate, boom);
        }
    }

    /// <summary>A device in the next free interior cell (nearest the machinery's heart), or bolted onto the hull when the inside is full. False if there is no room at all.</summary>
    static bool Device(Lay_ l, string type)
    {
        if (l.Inside.Count > 0)
        {
            Int3 c = l.Inside[0];
            l.Inside.RemoveAt(0);
            l.D.Put(type, c);
            return true;
        }
        // On the hull: the free cell resting on a plate nearest the machinery, out of every exhaust line and pad leg's room.
        Structure s = l.S;
        var (lo, hi) = s.Bounds();
        Int3 best = default;
        int bestD = int.MaxValue;
        for (int y = lo.Y - 1; y <= hi.Y + 1; y++)
            for (int z = lo.Z - 1; z <= hi.Z + 1; z++)
                for (int x = lo.X - 1; x <= hi.X + 1; x++)
                {
                    Int3 c = P(x, y, z);
                    int dist = Int3.Dot(c - l.Heart, c - l.Heart);
                    if (dist >= bestD || s.Has(c) || l.Reserved.Contains(c)) continue;
                    if (!s.TryGet(c - Int3.UnitY, out Block under) || !s.TypeOf(under).IsArmor || s.ShapeOf(under) != s.Catalogue.Cube || l.Cockpit.Contains(c - Int3.UnitY)) continue;
                    (best, bestD) = (c, dist);
                }
        if (bestD == int.MaxValue) return false;
        l.D.Put(type, best);
        return true;
    }

    /// <summary>Adds what its mass lacks, one part at a time, until it meets the needs (or runs out of room).</summary>
    static void Size(Lay_ l, float gravity, CraftNeeds needs, bool trader)
    {
        for (int guard = 0; guard < 80; guard++)
        {
            StructureStats st = StructureStats.Compute(l.S);
            double m = st.Mass, use = Math.Max(st.PowerUse, 1e-6);
            bool added;
            if (st.PowerOut < needs.Power * use) added = Device(l, "generator-t1");
            else if (st.StorageCapacity < needs.FuelSu(st) * 1.5) added = Device(l, "container-t1");
            else if (Torque(l.S) / m < needs.Turn) added = Device(l, "gyro-t1");
            else if (st.Thrust[(int)Dir.PosY] < needs.Lift * m * gravity) added = Thrust(l, Dir.PosY, "thruster-t1", pair: true);
            else if (st.Thrust[(int)Dir.NegZ] / m < needs.Forward) added = Thrust(l, Dir.NegZ, l.Rng.Next(3) == 0 ? "thruster-t2" : "thruster-t1", pair: !trader || l.Rng.Next(2) == 0);
            else if (st.Thrust[(int)Dir.PosZ] / m < needs.Back) added = Thrust(l, Dir.PosZ, "thruster-t1", pair: true);
            else if (st.Thrust[(int)Dir.NegX] / m < needs.Side) added = Thrust(l, Dir.NegX, "thruster-t1", pair: false);
            else if (st.Thrust[(int)Dir.PosX] / m < needs.Side) added = Thrust(l, Dir.PosX, "thruster-t1", pair: false);
            else if (st.Thrust[(int)Dir.NegY] / m < needs.Down) added = Thrust(l, Dir.NegY, "thruster-t1", pair: false);
            else return;
            if (!added) return;
        }
    }

    /// <summary>
    /// A thruster pushing the hull toward <paramref name="push"/>: on a cell of open air against a plate or device (never
    /// the cockpit, a pad or another thruster) on the side away from the push, its exhaust line clear for five cells.
    /// Lift goes where it balances the centre of mass; the rest near the middle of their face. With <paramref name="pair"/>
    /// its mirror image across the keel goes too.
    /// </summary>
    static bool Thrust(Lay_ l, Dir push, string type, bool pair)
    {
        Structure s = l.S;
        BlockType t = s.Catalogue.Type(type);
        Dir exhaust = push.Opposite();
        int rot = Rot(Designer.Rot(push.Axis() == 1 ? Dir.PosZ : Dir.PosY, push));
        System.Numerics.Vector3 centre = MassCentre(s);
        bool Fits(Int3 corner)
        {
            Int3 anchor = Footprint.AnchorFor(t, rot, corner);
            var cells = Footprint.Cells(anchor, t, rot).ToList();
            if (cells.Any(c => s.Has(c) || l.Reserved.Contains(c))) return false;
            // Mounted: the cell on its push side is a plate or a device (not the cockpit, a pad, a light or a thruster).
            Int3 mount = cells.OrderBy(c => Int3.Dot(c, push.Vec())).Last() + push.Vec();
            if (!s.TryGet(mount, out Block m) || l.Cockpit.Contains(mount)) return false;
            BlockType mt = s.TypeOf(m);
            if (mt.IsThruster || mt.IsLight || mt.Function == "gear" || mt.IsCommandChair) return false;
            if (mt.IsArmor && s.ShapeOf(m) != s.Catalogue.Cube || s.AttachCoverage(m, push.Opposite()) == Coverage.None) return false;
            Int3 tail = cells.OrderBy(c => Int3.Dot(c, exhaust.Vec())).Last();
            for (int k = 1; k <= 5; k++)
                if (s.Has(tail + exhaust.Vec() * k) || l.Reserved.Contains(tail + exhaust.Vec() * k)) return false;
            return true;
        }
        var (lo, hi) = s.Bounds();
        var options = new List<Int3>();
        for (int y = lo.Y - 2; y <= hi.Y + 2; y++)
            for (int z = lo.Z - 2; z <= hi.Z + 2; z++)
                for (int x = lo.X - 2; x <= hi.X + 2; x++)
                    if (Fits(P(x, y, z))) options.Add(P(x, y, z));
        if (options.Count == 0) return false;
        float Score(Int3 c) => push switch
        {
            // Lift balances the mass: nearest the centre of mass along the keel, then out from it.
            Dir.PosY => MathF.Abs(c.Z - centre.Z) * 2f + MathF.Abs(MathF.Abs(c.X) - 1f),
            Dir.NegZ => l.Mains?.Invoke(c) ?? MathF.Abs(c.X) + MathF.Abs(c.Y - l.AxisY),
            Dir.PosZ => MathF.Abs(c.X) + c.Y * 2f,
            Dir.NegY => MathF.Abs(c.Z - centre.Z) + MathF.Abs(c.X),
            _ => MathF.Abs(c.Z - centre.Z) - c.Y * 0.5f,
        } + (float)l.Rng.NextDouble() * 0.6f;
        Int3 best = options.OrderBy(Score).First();
        Place(l, type, best, push, exhaust, t, rot);
        Int3 mirror = P(-best.X, best.Y, best.Z);
        if (pair && best.X != 0 && options.Contains(mirror) && Fits(mirror)) Place(l, type, mirror, push, exhaust, t, rot);
        return true;
    }

    static void Place(Lay_ l, string type, Int3 corner, Dir push, Dir exhaust, BlockType t, int rot)
    {
        l.D.Thruster(type, corner, push);
        Int3 anchor = Footprint.AnchorFor(t, rot, corner);
        foreach (Int3 c in Footprint.Cells(anchor, t, rot))
            for (int k = 1; k <= 5; k++) l.Reserved.Add(c + exhaust.Vec() * k);
    }

    static int Rot(string rot) => Orientations.TryParse(rot, out int r) ? r : Orientations.Identity;

    static System.Numerics.Vector3 MassCentre(Structure s)
    {
        System.Numerics.Vector3 sum = System.Numerics.Vector3.Zero;
        float total = 0f;
        foreach (var (p, b) in s.Blocks())
        {
            float m = s.StatsOf(b).Mass;
            sum += s.CentreOf(p, b) * m;
            total += m;
        }
        return total > 0f ? sum / total : System.Numerics.Vector3.Zero;
    }

    /// <summary>Lights on its faces: two landing lights under it, red to port, green to starboard, white aft, an amber beacon on top. None in an exhaust line.</summary>
    static void Lights(Lay_ l, Random rng)
    {
        Structure s = l.S;
        var (lo, hi) = s.Bounds();
        bool Free(Int3 c, Dir onto) => !s.Has(c) && !l.Reserved.Contains(c) && s.TryGet(c + onto.Vec(), out Block b)
                                        && !l.Cockpit.Contains(c + onto.Vec()) && s.TypeOf(b) is { IsThruster: false, IsLight: false } bt && bt.Function != "gear"
                                        && (!bt.IsArmor || s.ShapeOf(b) == s.Catalogue.Cube) && s.AttachCoverage(b, onto.Opposite()) != Coverage.None;
        void Light(IEnumerable<Int3> cells, Dir onto, LightSettings settings)
        {
            foreach (Int3 c in cells)
                if (Free(c, onto))
                {
                    l.D.Mount("light", c, onto);
                    s.SetLight(c, settings);
                    l.Reserved.Add(c);
                    return;
                }
        }
        IEnumerable<Int3> Box(int x0, int x1, int y0, int y1, int z0, int z1, Func<Int3, float> order)
        {
            var cells = new List<Int3>();
            for (int y = y0; y <= y1; y++)
                for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++) cells.Add(P(x, y, z));
            return cells.OrderBy(order);
        }
        var warm = LightSettings.Steady(0xffddaa);
        Light(Box(lo.X, hi.X, lo.Y - 1, hi.Y, lo.Z, hi.Z, c => c.Z + MathF.Abs(c.X) + c.Y * 8), Dir.PosY, warm);
        Light(Box(lo.X, hi.X, lo.Y - 1, hi.Y, lo.Z, hi.Z, c => -c.Z + MathF.Abs(c.X) + c.Y * 8), Dir.PosY, warm);
        Light(Box(lo.X - 1, hi.X, lo.Y, hi.Y, lo.Z, hi.Z, c => c.X * 4 + c.Z * 0.5f - c.Y), Dir.PosX, LightSettings.Steady(0xff3322));
        Light(Box(lo.X, hi.X + 1, lo.Y, hi.Y, lo.Z, hi.Z, c => -c.X * 4 + c.Z * 0.5f - c.Y), Dir.NegX, LightSettings.Steady(0x44ff88));
        Light(Box(lo.X, hi.X, lo.Y, hi.Y, lo.Z, hi.Z + 1, c => -c.Z * 4 + MathF.Abs(c.X) - c.Y), Dir.NegZ, LightSettings.Steady(0xffffff));
        Light(Box(lo.X, hi.X, lo.Y, hi.Y + 1, lo.Z, hi.Z, c => -c.Y * 4 + MathF.Abs(c.X)), Dir.NegY, LightSettings.Make(0xffaa44, LightAnim.Strobe, 0.5f, rng.Next(64)));
    }

    // ---- Building it as a player builds ---------------------------------------------------------------------

    /// <summary>
    /// The plan built block by block, as a player places them: the core first, then every block that a block already
    /// placed holds (Building.Place's own check), pass after pass until no more go. Paints and light styles go across.
    /// </summary>
    public static Structure Assemble(Structure plan, string owner)
    {
        var s = new Structure(plan.Catalogue) { Name = plan.Name, Mode = plan.Mode };
        var slot = new ushort[plan.Palette.Count];
        for (int i = 0; i < slot.Length; i++) slot[i] = s.Palette.Intern(plan.Palette[(ushort)i], plan.Palette.NameOf((ushort)i));
        ushort Map(ushort p) => p < slot.Length ? slot[p] : p;
        var left = plan.Blocks().ToList();
        left.Sort((a, b) => plan.TypeOf(b.block).IsCore.CompareTo(plan.TypeOf(a.block).IsCore));
        while (left.Count > 0)
        {
            int before = left.Count;
            left.RemoveAll(x =>
            {
                Block b = x.block;
                b.Paint = new FacePaint { PX = Map(b.Paint.PX), NX = Map(b.Paint.NX), PY = Map(b.Paint.PY), NY = Map(b.Paint.NY), PZ = Map(b.Paint.PZ), NZ = Map(b.Paint.NZ) };
                return Building.Place(s, x.pos, b, null, owner) == PlaceError.None;
            });
            if (left.Count == before) break;
        }
        foreach (var (p, settings) in plan.LightStyles)
            if (s.Has(p)) s.SetLight(p, settings);
        return s;
    }
}
