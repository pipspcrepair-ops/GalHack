#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>
/// A part of a sketched place that stands as a place of its own: the sketch's item, its box on the ground (square to the
/// world, the sketch turned), the way its front looks, its plan (one large part, <see cref="Airfield"/>), and where its
/// plan's cell (0, 0) lies in the turned sketch's frame (metres from the sketch's middle).
/// </summary>
public sealed record TypePiece(SketchItem Item, SketchBox Box, Dir Front, SiteDef Plan, int OffsetX, int OffsetZ);

/// <summary>The sketch turned onto a world and cut into what is built: the places that stand on it, and the ground laid under them.</summary>
public static partial class PlaceTypes
{
    /// <summary>The kit the parts' plans name (their materials come from the age that built them).</summary>
    public const string Style = "concrete";

    /// <summary>The longest run of fence that is one place, m.</summary>
    const float FenceRun = 400f;

    /// <summary>
    /// A family's sketch for one spawn on a world: its first type built at the world's age whose sketch fits the world
    /// (an airport on a world too small for one gives way to the airstrip, never a shrunken airport), else its last.
    /// </summary>
    public static Sketch PlanFor(PoiLibrary lib, FamilyDef family, TechLevel? tech, ulong seed, double worldWidth, double worldHeight, Starport? port = null)
    {
        // (A spaceport's types by class, biggest first: none above the world's class.)
        var types = family.Types.Select(id => lib.Types.First(t => t.Id == id)).ToList();
        if (port is { } most && types.Any(t => t.Classes.Count > 0)) types = types.Where(t => t.Classes.Count == 0 || t.Classes.Min() <= most).DefaultIfEmpty(types[^1]).ToList();
        var fitting = types.Where(t => tech == null || t.Techs.Count == 0 || t.Techs.Any(x => WorldProfile.Parse(x) == tech)).DefaultIfEmpty(types[^1]).ToList();
        Sketch? last = null;
        foreach (PlaceType t in fitting)
        {
            last = Plan(t, seed, worldWidth, worldHeight);
            if (!last.TooBig) return last;
        }
        return types.Except(fitting).Select(t => Plan(t, seed, worldWidth, worldHeight)).FirstOrDefault(s => !s.TooBig) ?? last!;
    }

    /// <summary>A typed family's layout off a world (a test, the viewer, its landmark): its tallest building, as a fresh sketch of it rolls it.</summary>
    public static SiteDef Sample(PoiLibrary lib, FamilyDef family, ulong seed, TechLevel? tech) =>
        Pieces(PlanFor(lib, family, tech, seed, 16000, 8000), 0).Where(p => p.Item.Part.Kind == "building").MaxBy(p => p.Item.H)!.Plan;

    /// <summary>A point of the sketch turned by quarter turns about its middle.</summary>
    public static (float X, float Z) Turn(float x, float z, int turn) => (turn & 3) switch { 0 => (x, z), 1 => (-z, x), 2 => (-x, -z), _ => (z, -x) };

    /// <summary>A box of the sketch turned by quarter turns about its middle, on the 2 m lattice.</summary>
    public static SketchBox Turn(SketchBox b, int turn)
    {
        var (ax, az) = Turn(b.X0, b.Z0, turn);
        var (bx, bz) = Turn(b.X1, b.Z1, turn);
        float x0 = MathF.Floor(MathF.Min(ax, bx) / 2f) * 2f, z0 = MathF.Floor(MathF.Min(az, bz) / 2f) * 2f;
        float w = MathF.Max(2f, MathF.Round(MathF.Abs(bx - ax) / 2f) * 2f), d = MathF.Max(2f, MathF.Round(MathF.Abs(bz - az) / 2f) * 2f);
        return new SketchBox(x0, z0, x0 + w, z0 + d);
    }

    static Dir Way(float x, float z) => MathF.Abs(x) >= MathF.Abs(z) ? (x > 0 ? Dir.East : Dir.West) : (z > 0 ? Dir.South : Dir.North);

    /// <summary>Laid ground: the runway or the pads, taxiways, apron, car park, the access road, a siding's ballast.</summary>
    public static bool Laid(SketchItem i) => i.Part.Kind is "surface" or "road" or "pads" or "rail";

    /// <summary>The ground laid flat and surfaced, turned: a box on the 2 m lattice as its things stand, or its own shape.</summary>
    public static IEnumerable<(SketchItem Item, Shape Lay)> Grounds(Sketch s, int turn) =>
        s.Items.Where(Laid).Select(i => (i, i.Lay is { } lay ? lay.Turn(turn) : Shape.Of(Turn(i.Box, turn))));

    /// <summary>How far a recessed pad lies under its place's level, m (0 for anything else).</summary>
    public static float Sink(SketchItem i) => i.Part.Kind == "pads" ? i.Rolled.GetValueOrDefault("sink") : 0f;

    /// <summary>
    /// Everything that stands, each a place of its own, the main building first (it holds the place's grading, which the
    /// recessed pads' pits are cut after): the buildings, the fuel farm, the approach lights, a recessed pad's blast wall, a
    /// siding's rails, the fence in runs.
    /// </summary>
    public static List<TypePiece> Pieces(Sketch s, int turn)
    {
        var pieces = new List<TypePiece>();
        SketchBox runway = s.Box(s.Items[0].Part.Id), apron = s.Of("apron").FirstOrDefault()?.Box ?? runway;
        foreach (SketchItem i in s.Items.OrderBy(i => i.Part.Id == "terminal" ? 0 : 1))
        {
            if (i.Part.Kind == "fence") { pieces.AddRange(FenceRuns(s, i, turn)); continue; }
            string? what = i.Part.Kind switch
            {
                "building" => i.Part.What == "control_tower" ? "tower" : i.Part.What,
                "tanks" => "fuel",
                "lights" => "lights",
                "pads" when Sink(i) > 0 => "pad",
                "rail" => "siding",
                _ => null,
            };
            if (what == null) continue;
            // Its front: the way the type turns it, else toward the apron (the tower) or the runway (the fuel farm); the lights look back up the runway.
            float fx = i.FaceX, fz = i.FaceZ;
            if (what == "lights") (fx, fz) = (-i.FaceX, -i.FaceZ);
            else if (fx == 0 && fz == 0) fz = (what == "tower" ? apron : runway).Cz < i.Box.Cz ? -1f : 1f;
            var (tx, tz) = Turn(fx, fz, turn);
            Dir front = Way(tx, tz);
            var rolled = new Dictionary<string, float>(i.Rolled);
            if (what == "pad")
            {
                // Its ramp's way in its own frame (a hundredth of a step), and as wide as its link.
                var (gx, gz) = Turn(i.Rolled["gx"], i.Rolled["gz"], turn);
                var (gu, gv) = front switch { Dir.East => (gz, -gx), Dir.South => (-gx, -gz), Dir.West => (-gz, gx), _ => (gx, gz) };
                (rolled["gu"], rolled["gv"]) = (gu * 100f, gv * 100f);
                rolled["ramp"] = s.Of("link").FirstOrDefault()?.Rolled.GetValueOrDefault("w", 12f) ?? 12f;
            }
            pieces.Add(Piece(i, Turn(i.Box, turn), front, what, rolled));
        }
        return pieces;
    }

    static TypePiece Piece(SketchItem item, SketchBox box, Dir front, string what, IReadOnlyDictionary<string, float> rolled, string? gates = null)
    {
        bool ns = front is Dir.North or Dir.South;
        int L = (int)(ns ? box.W : box.D), D = (int)(ns ? box.D : box.W);
        var part = new SitePart { Kind = "grand" };
        string I(float v) => ((int)MathF.Round(v)).ToString(CultureInfo.InvariantCulture);
        part.Args["what"] = what;
        part.Args["fl"] = I(L);
        part.Args["fd"] = I(D);
        foreach (var (k, v) in rolled) if (k is not ("len" or "depth" or "w")) part.Args[k] = I(v);
        if (gates != null) part.Args["gates"] = gates;
        var plan = new SiteDef { Id = item.Part.Id, Name = item.Part.Id, Style = Style, Parts = { part } };
        // The plan's frame turned to its front (Plot.Cell): where its box's corner falls against its cell (0, 0).
        var (mx, mz) = front switch { Dir.East => (-D, 0), Dir.South => (-L, -D), Dir.West => (0, -L), _ => (0, 0) };
        return new TypePiece(item, box, front, plan, (int)box.X0 - mx, (int)box.Z0 - mz);
    }

    /// <summary>
    /// The fence round everything in runs of at most <see cref="FenceRun"/>, each looking out: a gate where the access road
    /// comes through, and now and then a service gate on a long side.
    /// </summary>
    static IEnumerable<TypePiece> FenceRuns(Sketch s, SketchItem fence, int turn)
    {
        SketchBox b = Turn(fence.Box, turn);
        var r = new Rng(Hash.Combine(s.Seed, 0xFE9CEUL));
        var (gx, gz) = Turn(s.GateX, s.GateZ, turn);
        var crossing = Grounds(s, turn).Where(g => g.Item.Part.Kind is "road" or "surface" or "rail" && g.Lay.HalfU > g.Lay.HalfV).Select(g => g.Lay).ToList();
        // The long sides run corner to corner, the short ones between them.
        var sides = new (float X0, float Z0, float X1, float Z1, Dir Out)[]
        {
            (b.X0, b.Z0, b.X1, b.Z0 + 2, Dir.North), (b.X0, b.Z1 - 2, b.X1, b.Z1, Dir.South),
            (b.X0, b.Z0 + 2, b.X0 + 2, b.Z1 - 2, Dir.West), (b.X1 - 2, b.Z0 + 2, b.X1, b.Z1 - 2, Dir.East),
        };
        foreach (var (x0, z0, x1, z1, way) in sides)
        {
            bool alongX = way is Dir.North or Dir.South;
            float len = alongX ? x1 - x0 : z1 - z0;
            var at = new List<float>();
            float line = way switch { Dir.North => z0, Dir.South => z1, Dir.West => x0, _ => x1 };
            if (MathF.Abs((alongX ? gz : gx) - line) < 3f) at.Add((alongX ? gx : gz) - (alongX ? x0 : z0));
            // (And wherever a strip of laid ground runs through its line: a taxiway, a road in, a siding.)
            foreach (Shape g in crossing)
            {
                float across = alongX ? g.UZ : g.UX;
                if (MathF.Abs(across) < 0.2f) continue;
                float t = (line - (alongX ? g.Z : g.X)) / across;
                if (MathF.Abs(t) > g.HalfU + 1f) continue;
                float a = (alongX ? g.X + g.UX * t : g.Z + g.UZ * t) - (alongX ? x0 : z0);
                if (a > 0 && a < len && at.All(o => MathF.Abs(o - a) > 10f)) at.Add(a);
            }
            if (len > 600f && r.Chance(0.7f)) at.Add(r.Float(0.2f, 0.8f) * len);
            int runs = (int)MathF.Ceiling(len / FenceRun);
            for (int k = 0; k < runs; k++)
            {
                float a0 = MathF.Round(len * k / runs / 2f) * 2f, a1 = MathF.Round(len * (k + 1) / runs / 2f) * 2f;
                SketchBox run = alongX ? new SketchBox(x0 + a0, z0, x0 + a1, z1) : new SketchBox(x0, z0 + a0, x1, z0 + a1);
                // Gates as the run's own frame reads them (u runs along +x facing north, -x south, +z east, -z west).
                bool reversed = way is Dir.South or Dir.West;
                var mine = at.Where(g => g >= a0 + 6 && g <= a1 - 6).Select(g => (int)MathF.Round(reversed ? a1 - g : g - a0)).ToList();
                var rolled = new Dictionary<string, float>(fence.Rolled);
                yield return Piece(fence, run, way, "fence", rolled, mine.Count > 0 ? string.Join(",", mine) : null);
            }
        }
    }
}
