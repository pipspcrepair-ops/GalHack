#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>Which network a stretch of the underground belongs to: what it's built of and what runs in it.</summary>
public enum UnderKind : byte { Sewer, Drain, Service, Metro }

/// <summary>What a space underground is, for creatures and tests.</summary>
public enum UnderSpaceKind : byte { Tunnel, Junction, Sump, Hall, Shaft, Stairs, Outfall }

/// <summary>A tunnel along a map axis, one column a metre, each with its own walk level (at most one metre from the next).</summary>
public sealed class UnderRun
{
    public UnderKind Kind;
    /// <summary>The first column, and the map-axis step to the next (one of them plus or minus one, the other zero).</summary>
    public int X, Z, DirX, DirZ;
    /// <summary>The cell your feet are in, column by column.</summary>
    public int[] Floors = Array.Empty<int>();
    public int HalfWidth = 2, Height = 3;
    /// <summary>Its top corners filled, for the arched look of a sewer or a tube.</summary>
    public bool Arched;
    /// <summary>A running tunnel: track down it, or a maglev's guideway.</summary>
    public bool Track;

    public int Length => Floors.Length;
    public (int X, int Z) Column(int i) => (X + DirX * i, Z + DirZ * i);
}

/// <summary>A room underground: a junction chamber, a sump, a station hall. Its open interior, inclusive.</summary>
public sealed class UnderRoom
{
    public UnderSpaceKind Kind;
    public UnderKind Network;
    public Int3 Min, Max;
    /// <summary>Standing water over its floor.</summary>
    public bool Flooded;
}

/// <summary>A way up: a ladder shaft to the surface (or from one level to another), or a flight of stairs.</summary>
public sealed class UnderShaft
{
    public UnderKind Network;
    /// <summary>The foot of the ladder, the cell your feet are in at the bottom.</summary>
    public Int3 Foot;
    /// <summary>First cell above the last rung: where you step off (on the surface, the ground's level).</summary>
    public int TopY;
    /// <summary>Which way the climber faces: the rungs sit on the wall behind that. Step off the top this way.</summary>
    public Dir Facing = Dir.North;
    /// <summary>Reaches the sky: the surface round its top is opened and it counts as a way in.</summary>
    public bool ToSurface = true;
}

/// <summary>A flight of stairs from a street down to a station: X, Z the column of its top tread, stepping down toward Down.</summary>
public sealed class UnderStairs
{
    public int X, Z;
    public Dir Down = Dir.East;
    /// <summary>The ground's level at the top, and the walk level at the foot.</summary>
    public int TopY, FootY;
    public int HalfWidth = 1;
}

/// <summary>A metro station: a hall with an island platform and two track beds along a map axis. Y is the track bed's walk level.</summary>
public sealed class UnderStation
{
    public int X, Z, DirX, DirZ, Y, HalfLength = 30;
    public const int HalfSpan = 8, PlatformHalf = 3, Height = 7, Track = 5;
}

/// <summary>Where a drain comes out: a culvert's headwall in a bank, its mouth facing along Dir from the pipe's last column.</summary>
public sealed class UnderOutfall
{
    public int X, Z, DirX, DirZ, Y;
    public const int Mouth = 1, Height = 3, Wall = 4, Apron = 6;
}

/// <summary>A dead end where the roof came in.</summary>
public sealed record UnderCollapse(int X, int Y, int Z, int DirX, int DirZ, int HalfWidth, int Height);

/// <summary>Everything planned under a town. <see cref="Underground.Build"/> turns it into blocks, a pure function of the plan.</summary>
public sealed class UnderPlan
{
    public TechLevel Tech;
    public ulong Seed;
    public readonly List<UnderRun> Runs = new();
    public readonly List<UnderRoom> Rooms = new();
    public readonly List<UnderShaft> Shafts = new();
    public readonly List<UnderStairs> Stairs = new();
    public readonly List<UnderStation> Stations = new();
    public readonly List<UnderOutfall> Outfalls = new();
    public readonly List<UnderCollapse> Collapses = new();
    /// <summary>Where the sewers and the drain gather (a sump), for the outfall pipe to leave from.</summary>
    public Int3 Sump;

    public bool HasMetro => Stations.Count > 0;
    /// <summary>Whether this world's builders ran a metro at all: none on a low-tech world.</summary>
    public static bool Metro(TechLevel t) => t != TechLevel.Low;
}

/// <summary>
/// A space a creature can use: a room, or a stretch of tunnel, as a box. Mirrors the wildlife
/// prototype's PoiRoom (see the planet surface's README) so its population function can run here.
/// </summary>
public sealed class UnderSpace
{
    public int Index;
    public UnderSpaceKind Kind;
    public UnderKind Network;
    /// <summary>The open interior, world cells inclusive.</summary>
    public Int3 Min, Max;
    /// <summary>Metres below the nearest way in.</summary>
    public int Depth;
    public bool Dark = true, Wet;
    public readonly List<int> Links = new();
}

/// <summary>The blocks of a plan, in world cells.</summary>
public sealed class UnderBuilt
{
    public readonly UnderPlan Plan;
    public readonly Dictionary<Int3, Block> Blocks = new();
    /// <summary>Cells inside the tunnels: air, or something you can walk through or stand on.</summary>
    public readonly HashSet<Int3> Open = new();
    /// <summary>Open cells above the ground or in front of a mouth: never lined.</summary>
    public readonly HashSet<Int3> Daylight = new();
    public readonly List<UnderSpace> Spaces = new();
    /// <summary>Cells to stand on at the surface end of each way in, for walk tests: outside the tunnels, in the open.</summary>
    public readonly List<Int3> WaysIn = new();
    /// <summary>Open cells with water standing over their floor (a sewer's run, a flooded drain): drawn as a surface, never a block.</summary>
    public readonly HashSet<Int3> Water = new();
    public Int3 Min, Max;

    public UnderBuilt(UnderPlan plan) => Plan = plan;

    /// <summary>Every cell the network takes out of the ground.</summary>
    public IEnumerable<Int3> Occupied => Blocks.Keys.Concat(Open.Where(c => !Blocks.ContainsKey(c))).Concat(Daylight.Where(c => !Blocks.ContainsKey(c) && !Open.Contains(c)));

    /// <summary>Whether a cell is rock to something walking in the network: nothing open there.</summary>
    public bool Solid(Int3 p) => Blocks.TryGetValue(p, out Block b) ? BlockInfo.Collides(b.Type) : !Open.Contains(p) && !Daylight.Contains(p);
}

/// <summary>A sparse window onto the blocks with y shifted below ground level, for the grid walker and tests (everything not occupied is rock there).</summary>
public sealed class UnderView : IBlockStructure
{
    readonly UnderBuilt _b;
    public readonly int Shift;
    public Int3 Min { get; }
    public Int3 Size { get; }

    public UnderView(UnderBuilt b)
    {
        _b = b;
        Shift = b.Max.Y + 3;
        Min = b.Min + new Int3(-1, -Shift - 1, -1);
        Size = b.Max - b.Min + new Int3(3, 3, 3);
    }

    public Int3 Local(Int3 world) => world + new Int3(0, -Shift, 0);
    public Int3 World(Int3 local) => local + new Int3(0, Shift, 0);
    public bool InBounds(Int3 p)
    {
        Int3 l = p - Min;
        return (uint)l.X < (uint)Size.X && (uint)l.Y < (uint)Size.Y && (uint)l.Z < (uint)Size.Z;
    }
    public Block Get(Int3 p) => _b.Blocks.TryGetValue(World(p), out Block c) ? c : Block.Air;
    public void Set(Int3 p, Block b) => _b.Blocks[World(p)] = b;
    public int CountBlocks() => _b.Blocks.Count;

    /// <summary>The mask of everything the network occupies, in the view's frame.</summary>
    public CellMask Mask()
    {
        var m = new CellMask(Min, Size);
        foreach (Int3 c in _b.Occupied) m[Local(c)] = true;
        return m;
    }
}

/// <summary>The street grid of a town, as the underground planner needs it: axes, sizes and the heights that matter.</summary>
public sealed class UnderSite
{
    /// <summary>The grid's centre cell and the main street's direction (a map axis).</summary>
    public int X, Z, AlongX = 1, AlongZ;
    public int MainHalf = 130, Block = 65, Arm = 85, BackSide = 1;
    public TechLevel Tech = TechLevel.Mid;
    public ulong Seed;
    /// <summary>The ground's height at a cell (any copy of the planet): the street's deck where one runs.</summary>
    public Func<int, int, float> Ground = (_, _) => 40f;
    /// <summary>Cells a pipe must keep out from under (a place's pad, water).</summary>
    public Func<int, int, bool> Avoid = (_, _) => false;

    public (int X, int Z) At(int along, int across) => (X + AlongX * along - AlongZ * across, Z + AlongZ * along + AlongX * across);
}

/// <summary>
/// What runs under a town (design doc "Infrastructure", underground networks): sewers under its
/// streets to a sump, a storm drain under the back street, a drainage pipe downhill to an outfall
/// culvert, a service gallery beside the main sewer, and on worlds that had one a metro station deep
/// under the main street with its running tunnels. Carved and walkable end to end, dark, with water
/// in the low parts, in the materials of the world's old tech level: brick sewers and no metro on a
/// low-tech world, concrete on a mid one, composite and maglev tubes on a high one.
/// Everything is on the map's axes, as a surveyed town's streets are, in one-metre blocks.
/// </summary>
public static class Underground
{
    /// <summary>Cover over a sewer's walk level under the deck, metres.</summary>
    public const int Cover = 7;
    const int SewerHalf = 2, SewerHeight = 4, DrainHalf = 2, DrainHeight = 3, JunctionHalf = 5, JunctionHeight = 5, SumpHalf = 6, SumpHeight = 6;
    /// <summary>How far off the main street's centre the service gallery and the station stairs run, metres.</summary>
    public const int GalleryAcross = 5, EntranceAcross = 11;
    /// <summary>The pipe to the outfall: how far it may reach, and its fall (one metre in this many).</summary>
    public const int OutfallReach = 700, OutfallFall = 80;

    // ---- planning ---------------------------------------------------------------------------

    /// <summary>Lots the town must leave free, as along and across ranges in street-grid metres: the station's stairs and its service passage come up through them.</summary>
    public static IEnumerable<(int A0, int A1, int U0, int U1)> Reserved(UnderSite s)
    {
        if (!UnderPlan.Metro(s.Tech)) yield break;
        int side = -s.BackSide;
        yield return (-s.Block - 1, -s.Block + 41, Math.Min(side * 6, side * 16), Math.Max(side * 6, side * 16));
        yield return (-3, 11, 9, 17);
    }

    public static UnderPlan Plan(UnderSite s)
    {
        var plan = new UnderPlan { Tech = s.Tech, Seed = s.Seed };
        int Deck(int along, int across)
        {
            var (x, z) = s.At(along, across);
            return (int)MathF.Floor(s.Ground(x, z));
        }

        // The main sewer: follows the street at its cover, a ramp at a time, level through each junction.
        int Hold = JunctionHalf + 2;
        bool Junction(int along) => Math.Abs(along) >= s.MainHalf - Hold || Math.Abs(Math.Abs(along) - s.Block) <= Hold || Math.Abs(along) <= Hold;
        int[] main = Track(s.MainHalf * 2 + 1, i => Deck(i - s.MainHalf, 0) - Cover, i => Junction(i - s.MainHalf));
        int Main(int along) => main[along + s.MainHalf];
        var first = s.At(-s.MainHalf, 0);
        plan.Runs.Add(new UnderRun { Kind = UnderKind.Sewer, X = first.X, Z = first.Z, DirX = s.AlongX, DirZ = s.AlongZ, Floors = main, HalfWidth = SewerHalf, Height = SewerHeight, Arched = true });

        // Cross sewers: from a junction under each crossing out along the arms, level at both ends.
        var tips = new Dictionary<(int C, int Side), int>();
        for (int c = -1; c <= 1; c++)
        {
            int along = c * s.Block, jy = Main(along);
            foreach (int side in new[] { -1, 1 })
            {
                int n = s.Arm + 1;
                int[] arm = Track(n, i => Deck(along, side * i) - Cover, i => i < 12 || i >= n - 10, jy);
                var from = s.At(along, 0);
                plan.Runs.Add(new UnderRun
                {
                    Kind = UnderKind.Sewer, X = from.X, Z = from.Z, DirX = -s.AlongZ * side, DirZ = s.AlongX * side, Floors = arm, HalfWidth = SewerHalf, Height = SewerHeight, Arched = true,
                });
                tips[(c, side)] = arm[^1];
            }
            AddJunction(plan, s, along, 0, jy, UnderSpaceKind.Junction, UnderKind.Sewer, JunctionHalf, JunctionHeight, false);
            // A manhole in a corner of the chamber: its rungs on the corner's wall, up to the kerb at the crossing.
            var (jx, jz) = s.At(along, 0);
            int mx = jx - JunctionHalf, mz = jz - JunctionHalf;
            float top = s.Ground(mx, mz);
            // Only where the ground round it is level enough to step off onto, or where this would be the only one.
            if (Math.Abs(s.Ground(mx - 1, mz) - top) <= 1f || (c == 1 && !plan.Shafts.Any()))
                plan.Shafts.Add(new UnderShaft { Network = UnderKind.Sewer, Foot = new Int3(mx, jy, mz), TopY = (int)MathF.Round(top), Facing = Dir.East });
        }

        // The storm drain under the back street, level where it joins the arms' tips.
        int back = s.BackSide * s.Arm;
        const int Flat = 6;
        int d0 = tips[(-1, s.BackSide)], d1 = tips[(0, s.BackSide)], d2 = tips[(1, s.BackSide)];
        var drain = new int[s.Block * 2 + 1];
        for (int i = 0; i <= s.Block; i++)
        {
            float t = Math.Clamp((i - Flat) / (float)(s.Block - Flat * 2), 0f, 1f);
            drain[i] = d0 + (int)MathF.Round((d1 - d0) * t);
            drain[s.Block + i] = d1 + (int)MathF.Round((d2 - d1) * t);
        }
        // Between its joins it keeps under the ground, however the street above goes.
        for (int i = Flat; i <= drain.Length - 1 - Flat; i++)
            if (Math.Abs(i - s.Block) > Flat) drain[i] = Math.Min(drain[i], Deck(i - s.Block, back) - DrainHeight - 2);
        for (int pass = 0; pass < 2; pass++)
        {
            for (int i = Flat; i <= drain.Length - 1 - Flat; i++) drain[i] = Math.Min(drain[i], drain[i - 1] + 1);
            for (int i = drain.Length - 1 - Flat; i >= Flat; i--) drain[i] = Math.Min(drain[i], drain[i + 1] + 1);
        }
        var dStart = s.At(-s.Block, back);
        plan.Runs.Add(new UnderRun { Kind = UnderKind.Drain, X = dStart.X, Z = dStart.Z, DirX = s.AlongX, DirZ = s.AlongZ, Floors = drain, HalfWidth = DrainHalf, Height = DrainHeight });
        foreach (int c in new[] { -1, 0, 1 })
            AddJunction(plan, s, c * s.Block, back, tips[(c, s.BackSide)], UnderSpaceKind.Junction, UnderKind.Drain, 3, 4, false);

        // The sump: the lowest end of the grid, where it all gathers; the outfall pipe leaves from it.
        var ends = new List<(int Along, int Across, int Y, int DirA, int DirU)>
        {
            (-s.MainHalf, 0, Main(-s.MainHalf), -1, 0),
            (s.MainHalf, 0, Main(s.MainHalf), 1, 0),
        };
        foreach (var ((c, side), y) in tips) ends.Add((c * s.Block, side * s.Arm, y, 0, side));
        ends = ends.OrderBy(e => e.Y).ThenBy(e => e.Along).ThenBy(e => e.Across).ToList();
        var sump = ends[0];
        AddJunction(plan, s, sump.Along, sump.Across, sump.Y, UnderSpaceKind.Sump, UnderKind.Sewer, SumpHalf, SumpHeight, true);
        var sumpAt = s.At(sump.Along, sump.Across);
        plan.Sump = new Int3(sumpAt.X, sump.Y, sumpAt.Z);
        AddOutfall(plan, s, sump);

        // The service gallery beside the main sewer on worlds that ran one, opening into each chamber.
        if (s.Tech != TechLevel.Low)
        {
            int[] floors = Enumerable.Range(0, s.Block * 2 + 1).Select(i => Main(i - s.Block)).ToArray();
            var g = s.At(-s.Block, s.BackSide * GalleryAcross);
            plan.Runs.Add(new UnderRun { Kind = UnderKind.Service, X = g.X, Z = g.Z, DirX = s.AlongX, DirZ = s.AlongZ, Floors = floors, HalfWidth = 1, Height = 3 });
        }

        // The metro, where the world's builders had one.
        if (UnderPlan.Metro(s.Tech)) AddMetro(plan, s, main);
        return plan;
    }

    /// <summary>
    /// A walk level along a run: as near a wanted level as the steps allow, a metre at a time, a ramp at
    /// most every other metre and none where it must stay level. It looks ahead and behind, keeping
    /// under the lowest level wanted nearby, so a run that can't follow a steep street stays in the ground.
    /// </summary>
    static int[] Track(int n, Func<int, int> want, Func<int, bool> level, int? start = null)
    {
        const int Look = 16;
        var raw = new int[n];
        for (int i = 0; i < n; i++) raw[i] = want(i);
        int Low(int i)
        {
            int lo = int.MaxValue;
            for (int k = Math.Max(0, i - Look); k <= Math.Min(n - 1, i + Look); k++) lo = Math.Min(lo, raw[k]);
            return lo;
        }
        var o = new int[n];
        int y = start ?? Low(0), since = 9;
        for (int i = 0; i < n; i++)
        {
            since++;
            if (since >= 2 && !level(i) && (i == 0 || !level(i - 1)))
            {
                int t = Low(i);
                if (t > y) { y++; since = 0; }
                else if (t < y) { y--; since = 0; }
            }
            o[i] = y;
        }
        return o;
    }

    static void AddJunction(UnderPlan plan, UnderSite s, int along, int across, int y, UnderSpaceKind kind, UnderKind network, int half, int height, bool flooded)
    {
        var (x, z) = s.At(along, across);
        plan.Rooms.Add(new UnderRoom { Kind = kind, Network = network, Min = new Int3(x - half, y, z - half), Max = new Int3(x + half, y + height - 1, z + half), Flooded = flooded });
    }

    static Dir FacingOf(int dx, int dz) => dx > 0 ? Dir.East : dx < 0 ? Dir.West : dz > 0 ? Dir.South : Dir.North;

    /// <summary>
    /// The drainage pipe: leaves the sump the shortest way out of town that the ground falls away to
    /// it, and comes out there through a culvert's headwall. If no way fits, it ends in a fall of rock.
    /// </summary>
    static void AddOutfall(UnderPlan plan, UnderSite s, (int Along, int Across, int Y, int DirA, int DirU) sump)
    {
        (int Len, int DirX, int DirZ, int X, int Z, int Y)? best = null;
        var (sx, sz) = s.At(sump.Along, sump.Across);
        foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
        {
            // Only ways leading out of the grid, not back under the town.
            if ((sx - s.X) * dx + (sz - s.Z) * dz <= 0) continue;
            for (int d = SumpHalf + 4; d <= OutfallReach; d++)
            {
                int cx = sx + dx * d, cz = sz + dz * d, y = sump.Y - 1 - d / OutfallFall;
                if (y < 1 || s.Avoid(cx, cz)) break;
                // Under the bank the pipe has its cover; where the ground comes down to it, it's out.
                if (s.Ground(cx, cz) > y + 2.5f) continue;
                if (d >= SumpHalf + 12 && (best == null || d < best.Value.Len)) best = (d, dx, dz, cx, cz, y);
                break;
            }
        }
        if (best == null)
        {
            // Nowhere to go: a short stub that ends where the roof came in.
            int dx = s.AlongX * sump.DirA - s.AlongZ * sump.DirU, dz = s.AlongZ * sump.DirA + s.AlongX * sump.DirU;
            var stub = new int[SumpHalf + 24];
            Array.Fill(stub, sump.Y);
            plan.Runs.Add(new UnderRun { Kind = UnderKind.Drain, X = sx, Z = sz, DirX = dx, DirZ = dz, Floors = stub, HalfWidth = 1, Height = 3 });
            plan.Collapses.Add(new UnderCollapse(sx + dx * (stub.Length - 1), sump.Y, sz + dz * (stub.Length - 1), dx, dz, 1, 3));
            return;
        }
        var b = best.Value;
        var floors = new int[b.Len + 1];
        for (int i = 0; i <= b.Len; i++) floors[i] = sump.Y - (i <= SumpHalf ? 0 : 1 + i / OutfallFall);
        plan.Runs.Add(new UnderRun { Kind = UnderKind.Drain, X = sx, Z = sz, DirX = b.DirX, DirZ = b.DirZ, Floors = floors, HalfWidth = UnderOutfall.Mouth, Height = UnderOutfall.Height });
        plan.Outfalls.Add(new UnderOutfall { X = b.X, Z = b.Z, DirX = b.DirX, DirZ = b.DirZ, Y = floors[^1] });
    }

    /// <summary>
    /// A station under the main street and its running tunnels, deep enough to pass under the sewers.
    /// Stairs come down beside the street to a doorway in the hall's wall; a service shaft joins it to
    /// the sewers; both tunnels run out until the sewers come too low, or past the town, and end where
    /// their roofs came in.
    /// </summary>
    static void AddMetro(UnderPlan plan, UnderSite s, int[] main)
    {
        const int Half = 30, Under = UnderStation.Height + 4;
        int MainAt(int along) => main[Math.Clamp(along + s.MainHalf, 0, main.Length - 1)];
        int y = int.MaxValue;
        for (int a = -Half - 2; a <= Half + 2; a++) y = Math.Min(y, MainAt(a) - Under);
        var c = s.At(0, 0);
        plan.Stations.Add(new UnderStation { X = c.X, Z = c.Z, DirX = s.AlongX, DirZ = s.AlongZ, Y = y, HalfLength = Half });

        // Running tunnels, one a track, from each end of the hall on out of town.
        foreach (int dir in new[] { -1, 1 })
            foreach (int track in new[] { -1, 1 })
            {
                var (ax, az) = s.At(dir * Half, track * UnderStation.Track);
                int n = 0;
                int reach = s.MainHalf + 60 - Half + (dir * track > 0 ? 12 : 0);
                while (n < reach && MainAt(dir * (Half + n)) >= y + 7) n++;
                if (n < 8) continue;
                var floors = new int[n];
                Array.Fill(floors, y);
                plan.Runs.Add(new UnderRun { Kind = UnderKind.Metro, X = ax, Z = az, DirX = s.AlongX * dir, DirZ = s.AlongZ * dir, Floors = floors, HalfWidth = 2, Height = 5, Arched = true, Track = true });
                plan.Collapses.Add(new UnderCollapse(ax + s.AlongX * dir * (n - 1), y, az + s.AlongZ * dir * (n - 1), s.AlongX * dir, s.AlongZ * dir, 2, 5));
            }

        // The stairs: down beside the street on the side away from the gallery, between the crossings,
        // to a level passage and a doorway in the hall's wall onto the track bed.
        int side = -s.BackSide, u = side * EntranceAcross;
        int TopAlong = -s.Block + 5;
        int topY = (int)MathF.Round(s.Ground(s.At(TopAlong, u).X, s.At(TopAlong, u).Z));
        int flight = topY - y;
        var top = s.At(TopAlong, u);
        plan.Stairs.Add(new UnderStairs { X = top.X, Z = top.Z, Down = FacingOf(s.AlongX, s.AlongZ), TopY = topY, FootY = y, HalfWidth = 1 });
        int foot = TopAlong + flight, door = Math.Clamp(foot + 3, -Half + 4, Half - 4);
        if (door <= foot + 1) door = foot + 2;
        var from = s.At(foot, u);
        var level = new int[door - foot + 2];
        Array.Fill(level, y);
        plan.Runs.Add(new UnderRun { Kind = UnderKind.Metro, X = from.X, Z = from.Z, DirX = s.AlongX, DirZ = s.AlongZ, Floors = level, HalfWidth = 1, Height = 3 });
        var at = s.At(door, u);
        var through = new int[EntranceAcross - UnderStation.HalfSpan + 1];
        Array.Fill(through, y);
        plan.Runs.Add(new UnderRun { Kind = UnderKind.Metro, X = at.X, Z = at.Z, DirX = s.AlongZ * side, DirZ = -s.AlongX * side, Floors = through, HalfWidth = 1, Height = 3 });

        // The service shaft: a passage off the arm of the middle crossing, a ladder down, a doorway into the hall.
        const int Passage = 13, Along = 7;
        UnderRun arm = plan.Runs.First(r => r.Kind == UnderKind.Sewer && r.DirX == -s.AlongZ && r.DirZ == s.AlongX && r.X == c.X && r.Z == c.Z);
        int passageY = arm.Floors[Passage];
        var start = s.At(0, Passage);
        var stub = new int[Along + 1];
        Array.Fill(stub, passageY);
        plan.Runs.Add(new UnderRun { Kind = UnderKind.Service, X = start.X, Z = start.Z, DirX = s.AlongX, DirZ = s.AlongZ, Floors = stub, HalfWidth = 1, Height = 3 });
        var shaftFoot = s.At(Along, Passage);
        var into = new int[Passage - UnderStation.HalfSpan + 1];
        Array.Fill(into, y);
        plan.Runs.Add(new UnderRun { Kind = UnderKind.Service, X = shaftFoot.X, Z = shaftFoot.Z, DirX = s.AlongZ, DirZ = -s.AlongX, Floors = into, HalfWidth = 1, Height = 3 });
        plan.Shafts.Add(new UnderShaft { Network = UnderKind.Service, Foot = new Int3(shaftFoot.X, y, shaftFoot.Z), TopY = passageY, Facing = Dir.East, ToSurface = false });
    }

    // ---- building ---------------------------------------------------------------------------

    static (Material Mat, Rgb Wall, Rgb Floor, Rgb Roof) Lining(UnderKind k, TechLevel t) => (k, t) switch
    {
        (UnderKind.Sewer or UnderKind.Drain, TechLevel.Low) => (Material.Ceramic, new Rgb(0x6e, 0x44, 0x36), new Rgb(0x4a, 0x38, 0x30), new Rgb(0x62, 0x3e, 0x32)),
        (UnderKind.Sewer or UnderKind.Drain, TechLevel.High) => (Material.Alloy, new Rgb(0x7a, 0x82, 0x86), new Rgb(0x56, 0x5c, 0x60), new Rgb(0x70, 0x78, 0x7c)),
        (UnderKind.Sewer or UnderKind.Drain, _) => (Material.Concrete, new Rgb(0x72, 0x72, 0x6a), new Rgb(0x56, 0x56, 0x50), new Rgb(0x68, 0x68, 0x62)),
        (UnderKind.Metro, TechLevel.High) => (Material.Alloy, new Rgb(0xa0, 0xaa, 0xb0), new Rgb(0x70, 0x76, 0x7a), new Rgb(0x96, 0xa0, 0xa6)),
        (UnderKind.Metro, _) => (Material.Concrete, new Rgb(0x86, 0x86, 0x7e), new Rgb(0x60, 0x60, 0x5a), new Rgb(0x7a, 0x7a, 0x72)),
        (_, TechLevel.High) => (Material.Alloy, new Rgb(0x88, 0x90, 0x96), new Rgb(0x60, 0x66, 0x6a), new Rgb(0x80, 0x88, 0x8e)),
        _ => (Material.Concrete, new Rgb(0x7a, 0x7a, 0x72), new Rgb(0x5c, 0x5c, 0x56), new Rgb(0x72, 0x72, 0x6a)),
    };

    sealed class B
    {
        public readonly UnderBuilt U;
        public readonly Dictionary<Int3, UnderKind> Owner = new();
        public B(UnderBuilt u) => U = u;
        public void Open(Int3 p, UnderKind k)
        {
            if (U.Open.Add(p)) Owner[p] = k;
        }
    }

    /// <param name="plan">What to build.</param>
    /// <param name="ground">The ground's height, if known: lining that stands clear of it (a sewer crossing a gully) gets open air round it, so it's drawn.</param>
    public static UnderBuilt Build(UnderPlan plan, Func<int, int, float>? ground = null)
    {
        var u = new UnderBuilt(plan);
        var b = new B(u);
        foreach (UnderRun r in plan.Runs) CarveRun(b, r);
        foreach (UnderRoom r in plan.Rooms)
            for (int y = r.Min.Y; y <= r.Max.Y; y++)
                for (int z = r.Min.Z; z <= r.Max.Z; z++)
                    for (int x = r.Min.X; x <= r.Max.X; x++)
                        b.Open(new Int3(x, y, z), r.Network);
        foreach (UnderStation s in plan.Stations) CarveStation(b, s);
        foreach (UnderStairs s in plan.Stairs) CarveStairs(b, s);
        foreach (UnderShaft s in plan.Shafts) CarveShaft(b, s);
        foreach (UnderOutfall o in plan.Outfalls) CarveOutfall(b, o);
        Line(b);
        foreach (UnderRun r in plan.Runs) DressRun(b, r);
        foreach (UnderRoom r in plan.Rooms) DressRoom(b, r);
        foreach (UnderStation s in plan.Stations) DressStation(b, s);
        foreach (UnderStairs s in plan.Stairs) DressStairs(b, s);
        foreach (UnderShaft s in plan.Shafts) DressShaft(b, s);
        foreach (UnderOutfall o in plan.Outfalls) DressOutfall(b, o);
        foreach (UnderCollapse c in plan.Collapses) DressCollapse(b, c);
        if (ground != null) Exposed(u, ground);
        Spaces(u);
        Bounds(u);
        return u;
    }

    /// <summary>Marks the air round any lining that stands above the ground, so the mesher draws its outside.</summary>
    static void Exposed(UnderBuilt u, Func<int, int, float> ground)
    {
        var heights = new Dictionary<(int, int), float>();
        float At(int x, int z)
        {
            if (!heights.TryGetValue((x, z), out float g)) heights[(x, z)] = g = ground(x, z);
            return g;
        }
        foreach (Int3 c in u.Blocks.Keys.ToList())
        {
            if (u.Open.Contains(c)) continue;
            foreach (Dir d in Dirs.All)
            {
                Int3 n = c + d.Vec();
                if (n.Y >= At(n.X, n.Z) && !u.Open.Contains(n) && !u.Blocks.ContainsKey(n)) u.Daylight.Add(n);
            }
        }
    }

    static void Bounds(UnderBuilt u)
    {
        Int3 lo = new(int.MaxValue, int.MaxValue, int.MaxValue), hi = new(int.MinValue, int.MinValue, int.MinValue);
        foreach (Int3 c in u.Occupied)
        {
            lo = Int3.Min(lo, c);
            hi = Int3.Max(hi, c);
        }
        u.Min = lo;
        u.Max = hi;
    }

    static (int X, int Z) Across(UnderRun r) => (Math.Abs(r.DirZ), Math.Abs(r.DirX));

    static void CarveRun(B b, UnderRun r)
    {
        var (lx, lz) = Across(r);
        for (int i = 0; i < r.Length; i++)
        {
            var (x, z) = r.Column(i);
            int y = r.Floors[i];
            for (int w = -r.HalfWidth; w <= r.HalfWidth; w++)
                for (int v = 0; v < r.Height; v++)
                {
                    if (r.Arched && v == r.Height - 1 && Math.Abs(w) == r.HalfWidth) continue;
                    b.Open(new Int3(x + lx * w, y + v, z + lz * w), r.Kind);
                }
        }
    }

    static void CarveStation(B b, UnderStation s)
    {
        int lx = Math.Abs(s.DirZ), lz = Math.Abs(s.DirX);
        for (int a = -s.HalfLength; a <= s.HalfLength; a++)
            for (int w = -UnderStation.HalfSpan; w <= UnderStation.HalfSpan; w++)
                for (int v = 0; v < UnderStation.Height; v++)
                    b.Open(new Int3(s.X + s.DirX * a + lx * w, s.Y + v, s.Z + s.DirZ * a + lz * w), UnderKind.Metro);
    }

    static void CarveStairs(B b, UnderStairs s)
    {
        Int3 d = s.Down.Vec();
        int lx = Math.Abs(d.Z), lz = Math.Abs(d.X);
        int n = s.TopY - s.FootY;
        // A tread a metre down each metre on, its head clear; the landing at the foot.
        for (int k = 0; k <= n + 1; k++)
            for (int w = -s.HalfWidth; w <= s.HalfWidth; w++)
            {
                int feet = Math.Max(s.FootY, s.TopY - 1 - k);
                for (int v = 0; v < 4; v++)
                {
                    Int3 c = new(s.X + d.X * k + lx * w, feet + v, s.Z + d.Z * k + lz * w);
                    if (c.Y >= s.TopY) b.U.Daylight.Add(c);
                    else b.Open(c, UnderKind.Metro);
                }
            }
        // Open air round the head of the flight, so what stands there is seen, and the approach clear.
        for (int k = -4; k <= 4; k++)
            for (int w = -s.HalfWidth - 3; w <= s.HalfWidth + 3; w++)
                for (int v = 0; v < 3; v++)
                    b.U.Daylight.Add(new Int3(s.X + d.X * k + lx * w, s.TopY + v, s.Z + d.Z * k + lz * w));
    }

    static void CarveShaft(B b, UnderShaft s)
    {
        for (int y = s.Foot.Y; y < s.TopY; y++)
            for (int dz = 0; dz < 2; dz++)
                for (int dx = 0; dx < 2; dx++)
                    b.Open(new Int3(s.Foot.X + dx, y, s.Foot.Z + dz), s.Network);
        if (!s.ToSurface) return;
        // The hole in the street and the air over it: you step off onto the ground beside it.
        for (int v = 0; v < 3; v++)
            for (int dz = -1; dz <= 2; dz++)
                for (int dx = -1; dx <= 2; dx++)
                    b.U.Daylight.Add(new Int3(s.Foot.X + dx, s.TopY + v, s.Foot.Z + dz));
    }

    static void CarveOutfall(B b, UnderOutfall o)
    {
        // The pit in front of the headwall, open to the sky: a slab with wing walls either side.
        int lx = Math.Abs(o.DirZ), lz = Math.Abs(o.DirX);
        for (int a = 1; a <= UnderOutfall.Apron + 2; a++)
            for (int w = -UnderOutfall.Wall - 1; w <= UnderOutfall.Wall + 1; w++)
                for (int v = 0; v < UnderOutfall.Height + 6; v++)
                    b.U.Daylight.Add(new Int3(o.X + o.DirX * a + lx * w, o.Y + v, o.Z + o.DirZ * a + lz * w));
        // The air over the headwall, so its top shows.
        for (int a = -1; a <= 0; a++)
            for (int w = -UnderOutfall.Wall - 1; w <= UnderOutfall.Wall + 1; w++)
                for (int v = UnderOutfall.Height + 3; v < UnderOutfall.Height + 5; v++)
                    b.U.Daylight.Add(new Int3(o.X + o.DirX * a + lx * w, o.Y + v, o.Z + o.DirZ * a + lz * w));
    }

    /// <summary>A one-block skin of wall, floor and roof round every open cell, its corners sealed.</summary>
    static void Line(B b)
    {
        UnderBuilt u = b.U;
        void Put(Int3 n, UnderKind k, Role role)
        {
            if (u.Open.Contains(n) || u.Daylight.Contains(n) || u.Blocks.ContainsKey(n)) return;
            var (mat, wall, floor, roof) = Lining(k, u.Plan.Tech);
            Rgb col = role switch { Role.Floor => floor, Role.Roof => roof, _ => wall };
            // Courses of brick or the shutter lines of poured concrete, and a century of damp.
            float course = 0.92f + 0.1f * ((n.Y + ((n.X + n.Z) & 1) * (mat == Material.Ceramic ? 1 : 0)) & 1) + 0.03f * (Hash.Cell(n, 71) % 4);
            u.Blocks[n] = new Block(BlockType.Cube, mat, col.Scale(course), role);
        }
        // Floors and roofs first, so a corner cell takes the role of what it mostly is.
        foreach (Int3 p in u.Open.ToList())
        {
            if (u.Daylight.Contains(p)) continue;
            UnderKind k = b.Owner[p];
            Put(p + Int3.Down, k, Role.Floor);
            Put(p + Int3.Up, k, Role.Roof);
        }
        foreach (Int3 p in u.Open.ToList())
        {
            if (u.Daylight.Contains(p)) continue;
            UnderKind k = b.Owner[p];
            for (int dy = -1; dy <= 1; dy++)
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                        if (dx != 0 || dy != 0 || dz != 0)
                            Put(p + new Int3(dx, dy, dz), k, Role.Wall);
        }
    }

    /// <summary>Water standing in a channel's floor: a surface over the cell (<see cref="UnderBuilt.Water"/>), never a block.</summary>
    static void Water(UnderBuilt u, Int3 p, UnderKind k)
    {
        if (!u.Blocks.ContainsKey(p)) u.Water.Add(p);
    }

    static void DressRun(B b, UnderRun r)
    {
        UnderBuilt u = b.U;
        var (lx, lz) = Across(r);
        Dir forward = FacingOf(r.DirX, r.DirZ);
        // Ramps at every step in the floor: rising toward whichever column is higher.
        var rampAt = new HashSet<int>();
        for (int i = 0; i + 1 < r.Length; i++)
        {
            int up = r.Floors[i + 1] - r.Floors[i];
            if (up == 0) continue;
            int col = up > 0 ? i : i + 1;
            int y = Math.Min(r.Floors[i], r.Floors[i + 1]);
            Dir rises = up > 0 ? forward : forward.Opposite();
            rampAt.Add(col);
            var (x, z) = r.Column(col);
            var (mat, _, floor, _) = Lining(r.Kind, u.Plan.Tech);
            for (int w = -r.HalfWidth; w <= r.HalfWidth; w++)
            {
                Int3 c = new(x + lx * w, y, z + lz * w);
                if (u.Open.Contains(c) && !u.Blocks.ContainsKey(c))
                    u.Blocks[c] = new Block(BlockType.Ramp, mat, floor.Scale(1.05f), Role.Traverse, rises);
            }
        }
        // Water along the invert, over the whole floor at the low points of a run; ribs and props.
        for (int i = 0; i < r.Length; i++)
        {
            if (rampAt.Contains(i)) continue;
            var (x, z) = r.Column(i);
            int y = r.Floors[i];
            bool low = r.Kind is UnderKind.Sewer or UnderKind.Drain && LowPoint(r, i);
            for (int w = -r.HalfWidth; w <= r.HalfWidth; w++)
            {
                Int3 c = new(x + lx * w, y, z + lz * w);
                if (!u.Open.Contains(c) || u.Blocks.ContainsKey(c)) continue;
                uint h = Hash.Cell(c, 73);
                if (r.Kind is UnderKind.Sewer or UnderKind.Drain)
                {
                    if (w == 0 || low || (Math.Abs(w) == 1 && h % 3 == 0)) Water(u, c, r.Kind);
                    else if (h % 37 == 0) u.Blocks[c] = new Block(BlockType.Scatter, Material.Scrap, new Rgb(0x4a, 0x42, 0x38), Role.Debris);
                    else if (Math.Abs(w) == r.HalfWidth && h % 29 == 0) u.Blocks[c] = new Block(BlockType.Rubble, Material.Concrete, new Rgb(0x56, 0x50, 0x48), Role.Debris);
                }
                else if (r.Track) DressTrackCell(u, c, w);
                else if (h % 31 == 0) u.Blocks[c] = new Block(BlockType.Scatter, Material.Scrap, new Rgb(0x50, 0x4a, 0x40), Role.Debris);
            }
            if (r.Track && u.Plan.Tech == TechLevel.High)
            {
                // A maglev tube: the guideway beam down the middle, a walkway either side of it.
                Int3 c = new(x, y, z);
                if (u.Open.Contains(c)) u.Blocks[c] = new Block(BlockType.Cube, Material.Alloy, new Rgb(0x6c, 0x74, 0x7a), Role.Floor);
            }
            // A rib every few metres, a brick ring or a cast segment joint; a cable tray or pipes in the gallery.
            if (i % 6 == 3 && r.Kind != UnderKind.Service) Ring(u, r, x, z, y, lx, lz);
            if (r.Kind == UnderKind.Service && r.HalfWidth == 1)
            {
                Int3 tray = new(x + lx * 1, y + 2, z + lz * 1);
                if (u.Open.Contains(tray) && !u.Blocks.ContainsKey(tray) && Hash.Cell(new Int3(i / 7, 0, r.X), 75) % 4 != 0)
                    u.Blocks[tray] = new Block(BlockType.Sign, Material.Steel, new Rgb(0x44, 0x46, 0x4a), Role.Decor, FacingOf(-lx, -lz));
                Int3 pipe = new(x - lx * 1, y + 2, z - lz * 1);
                if (u.Open.Contains(pipe) && !u.Blocks.ContainsKey(pipe))
                    u.Blocks[pipe] = new Block(BlockType.Sign, Material.Rust, new Rgb(0x6a, 0x46, 0x30), Role.Decor, FacingOf(lx, lz));
            }
        }
    }

    /// <summary>Whether a column lies at a low point of its run: nothing lower for ten metres either way.</summary>
    static bool LowPoint(UnderRun r, int i)
    {
        int y = r.Floors[i];
        int lo = Math.Max(0, i - 10), hi = Math.Min(r.Length - 1, i + 10);
        if (r.Floors[lo] <= y || r.Floors[hi] <= y) return false;
        for (int k = lo; k <= hi; k++) if (r.Floors[k] < y) return false;
        return true;
    }

    static void DressTrackCell(UnderBuilt u, Int3 c, int w)
    {
        if (u.Plan.Tech == TechLevel.High) return;
        // Rails and sleepers read from the floor beneath: rails run dark, sleepers cross them.
        Int3 f = c + Int3.Down;
        if (!u.Blocks.TryGetValue(f, out Block b) || b.Type != BlockType.Cube) return;
        bool rail = Math.Abs(w) == 1;
        bool sleeper = (c.X + c.Z) % 2 == 0;
        b.Color = rail ? new Rgb(0x3a, 0x34, 0x30) : sleeper ? new Rgb(0x3e, 0x34, 0x2a) : b.Color.Scale(0.8f);
        u.Blocks[f] = b;
    }

    static void Ring(UnderBuilt u, UnderRun r, int x, int z, int y, int lx, int lz)
    {
        for (int w = -r.HalfWidth - 1; w <= r.HalfWidth + 1; w++)
            for (int v = -1; v <= r.Height; v++)
            {
                Int3 c = new(x + lx * w, y + v, z + lz * w);
                if (u.Blocks.TryGetValue(c, out Block b) && b.Type == BlockType.Cube && !u.Open.Contains(c))
                {
                    b.Color = b.Color.Scale(0.82f);
                    u.Blocks[c] = b;
                }
            }
    }

    static void DressRoom(B b, UnderRoom r)
    {
        UnderBuilt u = b.U;
        // Four square piers in a big enough chamber, and the floor awash in a sump.
        int w = r.Max.X - r.Min.X, d = r.Max.Z - r.Min.Z;
        var (mat, wall, _, _) = Lining(r.Network, u.Plan.Tech);
        for (int z = r.Min.Z; z <= r.Max.Z; z++)
            for (int x = r.Min.X; x <= r.Max.X; x++)
            {
                Int3 c = new(x, r.Min.Y, z);
                bool edgeX = Math.Min(x - r.Min.X, r.Max.X - x) == 2, edgeZ = Math.Min(z - r.Min.Z, r.Max.Z - z) == 2;
                if (w >= 8 && d >= 8 && edgeX && edgeZ && !u.Blocks.ContainsKey(c))
                {
                    for (int y = r.Min.Y; y <= r.Max.Y; y++)
                        u.Blocks[new Int3(x, y, z)] = new Block(BlockType.Cube, mat, wall.Scale(0.95f - 0.08f * ((y - r.Min.Y) & 1)), Role.Wall);
                    continue;
                }
                if (u.Blocks.ContainsKey(c)) continue;
                uint h = Hash.Cell(c, 77);
                if (r.Flooded || h % 3 != 0) Water(u, c, r.Network);
            }
    }

    static void DressStation(B b, UnderStation s)
    {
        UnderBuilt u = b.U;
        int lx = Math.Abs(s.DirZ), lz = Math.Abs(s.DirX);
        var (mat, wall, floor, _) = Lining(UnderKind.Metro, u.Plan.Tech);
        Rgb tile = u.Plan.Tech == TechLevel.High ? new Rgb(0xc0, 0xc8, 0xcc) : new Rgb(0xb8, 0xb0, 0x98);
        Rgb edge = new(0xa8, 0x8a, 0x30);
        Dir forward = FacingOf(s.DirX, s.DirZ);
        Int3 At(int a, int w, int v) => new(s.X + s.DirX * a + lx * w, s.Y + v, s.Z + s.DirZ * a + lz * w);
        int top = s.HalfLength - 2;
        for (int a = -s.HalfLength; a <= s.HalfLength; a++)
        {
            // The platform: a metre above the track beds, its edges painted, ramped at each end.
            for (int w = -UnderStation.PlatformHalf; w <= UnderStation.PlatformHalf; w++)
            {
                Int3 c = At(a, w, 0);
                if (Math.Abs(a) < top)
                    u.Blocks[c] = new Block(BlockType.Cube, mat, Math.Abs(w) == UnderStation.PlatformHalf ? edge : floor.Scale(1.2f + 0.08f * ((a + w) & 1)), Role.Floor);
                else if (Math.Abs(a) == top)
                    u.Blocks[c] = new Block(BlockType.Ramp, mat, floor.Scale(1.2f), Role.Traverse, a < 0 ? forward : forward.Opposite());
            }
            // Tiled wall panels along both sides of the hall, a station's one bit of colour, blackened by age.
            foreach (int side in new[] { -1, 1 })
                for (int v = 1; v <= 3; v++)
                {
                    Int3 c = At(a, side * (UnderStation.HalfSpan + 1), v);
                    if (u.Blocks.TryGetValue(c, out Block w) && w.Type == BlockType.Cube)
                    {
                        uint h = Hash.Cell(c, 79);
                        w.Material = h % 5 == 0 ? mat : Material.Ceramic;
                        w.Color = h % 5 == 0 ? wall.Scale(0.8f) : tile.Scale(0.7f + 0.2f * (v == 2 ? 1 : 0) + 0.03f * (h % 4));
                        u.Blocks[c] = w;
                    }
                }
            // Rails on the track beds; piers down the platform's edge every eight metres.
            for (int w = UnderStation.PlatformHalf + 1; w <= UnderStation.HalfSpan; w++)
                foreach (int side in new[] { -1, 1 })
                    DressTrackCell(u, At(a, side * w, 0), w - UnderStation.Track);
            if (Math.Abs(a) < top - 2 && (a & 7) == 0)
                foreach (int side in new[] { -1, 1 })
                    for (int v = 1; v < UnderStation.Height; v++)
                        u.Blocks[At(a, side * (UnderStation.PlatformHalf - 1), v)] = new Block(BlockType.Cube, mat, wall.Scale(0.9f), Role.Wall);
            if (Math.Abs(a) < top - 1 && (a & 7) == 4)
                u.Blocks[At(a, 0, 1)] = new Block(BlockType.Bench, Material.Steel, new Rgb(0x5a, 0x4a, 0x3a), Role.Prop);
        }
        // A maglev's guideways on a high-tech world, and rubble where a century of damp brought the ceiling down.
        for (int a = -s.HalfLength; a <= s.HalfLength; a += 1)
            foreach (int side in new[] { -1, 1 })
            {
                Int3 c = At(a, side * UnderStation.Track, 0);
                // Stopped short of the hall's ends, so a walker can cross the beds round the platform's end.
                if (u.Plan.Tech == TechLevel.High && Math.Abs(a) < top) u.Blocks[c] = new Block(BlockType.Cube, Material.Alloy, new Rgb(0x6c, 0x74, 0x7a), Role.Floor);
                Int3 c2 = At(a, side * UnderStation.HalfSpan, 0);
                if (Hash.Cell(c2, 81) % 11 == 0 && !u.Blocks.ContainsKey(c2))
                    u.Blocks[c2] = new Block(BlockType.Rubble, Material.Concrete, wall.Scale(0.7f), Role.Debris);
            }
    }

    static void DressStairs(B b, UnderStairs s)
    {
        UnderBuilt u = b.U;
        Int3 d = s.Down.Vec();
        int lx = Math.Abs(d.Z), lz = Math.Abs(d.X);
        var (mat, wall, floor, _) = Lining(UnderKind.Metro, u.Plan.Tech);
        int n = s.TopY - s.FootY;
        // Treads: the top one's upper face level with the ground at the head of the flight.
        for (int k = 0; k < n; k++)
            for (int w = -s.HalfWidth; w <= s.HalfWidth; w++)
            {
                Int3 c = new(s.X + d.X * k + lx * w, s.TopY - 1 - k, s.Z + d.Z * k + lz * w);
                u.Blocks[c] = new Block(BlockType.Stair, mat, floor.Scale(1.1f), Role.Traverse, s.Down.Opposite());
            }
        // A low wall either side of the opening, so you see it coming.
        for (int k = 0; k <= 3; k++)
            foreach (int side in new[] { -1, 1 })
            {
                Int3 c = new(s.X + d.X * k + lx * side * (s.HalfWidth + 1), s.TopY, s.Z + d.Z * k + lz * side * (s.HalfWidth + 1));
                u.Blocks[c] = new Block(BlockType.Cube, mat, wall, Role.Wall);
            }
        u.WaysIn.Add(new Int3(s.X - d.X * 2, s.TopY, s.Z - d.Z * 2));
    }

    static void DressShaft(B b, UnderShaft s)
    {
        UnderBuilt u = b.U;
        Rgb steel = new(0x4e, 0x44, 0x3c);
        // Rungs from the foot to the top, against the wall behind the climber.
        for (int y = s.Foot.Y; y < s.TopY; y++)
            u.Blocks[new Int3(s.Foot.X, y, s.Foot.Z)] = new Block(BlockType.Ladder, Material.Steel, steel, Role.Traverse, s.Facing);
        if (!s.ToSurface) return;
        // The kerb round the hole on its two far sides, you come at it from the corner the rungs are in.
        Int3 f = new(s.Foot.X, s.TopY, s.Foot.Z);
        Rgb kerb = new(0x9a, 0x80, 0x34);
        foreach (Int3 k in new[] { new Int3(2, 0, 0), new Int3(2, 0, 1), new Int3(0, 0, 2), new Int3(1, 0, 2) })
            u.Blocks[f + k] = new Block(BlockType.Rail, Material.Steel, kerb, Role.Decor, k.X == 2 ? Dir.East : Dir.South);
        u.WaysIn.Add(f + new Int3(-1, 0, 0));
    }

    static void DressOutfall(B b, UnderOutfall o)
    {
        UnderBuilt u = b.U;
        int lx = Math.Abs(o.DirZ), lz = Math.Abs(o.DirX);
        var (mat, wall, floor, _) = Lining(UnderKind.Drain, u.Plan.Tech);
        Int3 At(int a, int w, int v) => new(o.X + o.DirX * a + lx * w, o.Y + v, o.Z + o.DirZ * a + lz * w);
        // The headwall: taller than the mouth, with a capping course.
        for (int w = -UnderOutfall.Wall; w <= UnderOutfall.Wall; w++)
            for (int v = -1; v < UnderOutfall.Height + 3; v++)
            {
                Int3 c = At(0, w, v);
                if (Math.Abs(w) <= UnderOutfall.Mouth && v >= 0 && v < UnderOutfall.Height) continue;
                u.Blocks[c] = new Block(BlockType.Cube, mat, wall.Scale(v == UnderOutfall.Height + 2 ? 1.15f : 0.95f), Role.Wall);
            }
        // The apron and its wing walls, stepping down as they run out.
        for (int a = 1; a <= UnderOutfall.Apron; a++)
        {
            for (int w = -UnderOutfall.Wall; w <= UnderOutfall.Wall; w++)
            {
                Int3 c = At(a, w, -1);
                u.Blocks[c] = new Block(BlockType.Cube, mat, floor.Scale(0.9f + 0.06f * ((a + w) & 1)), Role.Floor);
                if (Math.Abs(w) <= 1 || Hash.Cell(c, 83) % 4 == 0) Water(u, At(a, w, 0), UnderKind.Drain);
            }
            foreach (int side in new[] { -1, 1 })
                for (int v = 0; v < UnderOutfall.Height + 2 - a / 2; v++)
                {
                    Int3 c = At(a, side * UnderOutfall.Wall, v);
                    u.Blocks[c] = new Block(BlockType.Cube, mat, wall.Scale(0.9f), Role.Wall);
                }
        }
        // What's left of the bars across the mouth: the middle ones rusted through and gone.
        foreach (int side in new[] { -1, 1 })
            for (int v = 0; v < 2; v++)
                u.Blocks[At(0, side * UnderOutfall.Mouth, v)] = new Block(BlockType.Rail, Material.Rust, new Rgb(0x5a, 0x38, 0x24), Role.Decor, FacingOf(o.DirX, o.DirZ));
        u.WaysIn.Add(At(UnderOutfall.Apron - 1, 0, 0));
    }

    static void DressCollapse(B b, UnderCollapse c)
    {
        UnderBuilt u = b.U;
        int lx = Math.Abs(c.DirZ), lz = Math.Abs(c.DirX);
        var col = new Rgb(0x5a, 0x54, 0x4c);
        // A plug of fallen roof, sloping up across the last metres of the bore.
        for (int a = -3; a <= 0; a++)
            for (int w = -c.HalfWidth; w <= c.HalfWidth; w++)
                for (int v = 0; v < c.Height; v++)
                {
                    if (v > a + 3) continue;
                    Int3 p = new(c.X + c.DirX * a + lx * w, c.Y + v, c.Z + c.DirZ * a + lz * w);
                    if (u.Open.Contains(p)) u.Blocks[p] = new Block(BlockType.Rubble, Material.Rock, col.Scale(0.85f + 0.05f * (Hash.Cell(p, 85) % 4)), Role.Debris);
                }
    }

    // ---- spaces for creatures ----------------------------------------------------------------

    static void Spaces(UnderBuilt u)
    {
        UnderPlan plan = u.Plan;
        var list = u.Spaces;
        void Add(UnderSpaceKind kind, UnderKind net, Int3 lo, Int3 hi, bool wet) =>
            list.Add(new UnderSpace { Index = list.Count, Kind = kind, Network = net, Min = lo, Max = hi, Wet = wet });
        foreach (UnderRoom r in plan.Rooms) Add(r.Kind, r.Network, r.Min, r.Max, true);
        foreach (UnderStation s in plan.Stations)
        {
            int lx = Math.Abs(s.DirZ), lz = Math.Abs(s.DirX);
            Int3 a = new(s.X - s.DirX * s.HalfLength - lx * UnderStation.HalfSpan, s.Y, s.Z - s.DirZ * s.HalfLength - lz * UnderStation.HalfSpan);
            Int3 b = new(s.X + s.DirX * s.HalfLength + lx * UnderStation.HalfSpan, s.Y + UnderStation.Height - 1, s.Z + s.DirZ * s.HalfLength + lz * UnderStation.HalfSpan);
            Add(UnderSpaceKind.Hall, UnderKind.Metro, Int3.Min(a, b), Int3.Max(a, b), false);
        }
        // Each run, in stretches of about twenty metres.
        foreach (UnderRun r in plan.Runs)
        {
            var (lx, lz) = Across(r);
            for (int i = 0; i < r.Length; i += 20)
            {
                int j = Math.Min(r.Length - 1, i + 19);
                var (ax, az) = r.Column(i);
                var (bx, bz) = r.Column(j);
                int lo = int.MaxValue, hi = int.MinValue;
                for (int k = i; k <= j; k++) { lo = Math.Min(lo, r.Floors[k]); hi = Math.Max(hi, r.Floors[k]); }
                Int3 a = new(ax - lx * r.HalfWidth, lo, az - lz * r.HalfWidth), b = new(bx + lx * r.HalfWidth, hi + r.Height - 1, bz + lz * r.HalfWidth);
                Add(UnderSpaceKind.Tunnel, r.Kind, Int3.Min(a, b), Int3.Max(a, b), r.Kind is UnderKind.Sewer or UnderKind.Drain);
            }
        }
        foreach (UnderShaft s in plan.Shafts)
            Add(UnderSpaceKind.Shaft, s.Network, s.Foot, new Int3(s.Foot.X + 1, s.TopY - 1, s.Foot.Z + 1), false);
        foreach (UnderStairs s in plan.Stairs)
        {
            Int3 d = s.Down.Vec();
            int n = s.TopY - s.FootY;
            Int3 a = new(s.X, s.FootY, s.Z), b = new(s.X + d.X * n, s.TopY, s.Z + d.Z * n);
            Add(UnderSpaceKind.Stairs, UnderKind.Metro, Int3.Min(a, b), Int3.Max(a, b), false);
        }
        foreach (UnderOutfall o in plan.Outfalls)
        {
            Int3 at = new(o.X, o.Y, o.Z);
            Add(UnderSpaceKind.Outfall, UnderKind.Drain, at, at + new Int3(0, UnderOutfall.Height - 1, 0), true);
        }
        // Links: spaces whose boxes touch; depth: metres below the highest of the network's ways in.
        int top = plan.Shafts.Where(s => s.ToSurface).Select(s => s.TopY).DefaultIfEmpty(0).Max();
        foreach (UnderSpace s in list) s.Depth = Math.Max(0, top - s.Min.Y);
        for (int i = 0; i < list.Count; i++)
            for (int j = i + 1; j < list.Count; j++)
            {
                UnderSpace a = list[i], b = list[j];
                if (a.Max.X + 1 < b.Min.X || b.Max.X + 1 < a.Min.X || a.Max.Y + 1 < b.Min.Y || b.Max.Y + 1 < a.Min.Y || a.Max.Z + 1 < b.Min.Z || b.Max.Z + 1 < a.Min.Z) continue;
                a.Links.Add(j);
                b.Links.Add(i);
            }
    }
}
