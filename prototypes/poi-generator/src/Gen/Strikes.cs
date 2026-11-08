#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>
/// One blast as the world has it: where it came down, in world metres, and how far its effects reach.
/// Everything a blast does reads this one record: the crater in the ground (a ground burst only), the
/// flattened and burnt places round it (<see cref="Strength"/>), the dead ash ground and its fade, the
/// thinned flora, and the map. So a map's craters and the ruined places always agree. A war's strikes
/// (<see cref="StrikePlan"/>) and a blast site's own blast (<see cref="FromSite"/>) are both strikes.
/// </summary>
public sealed class Strike
{
    /// <summary>Ground zero, world metres.</summary>
    public double X, Z;
    /// <summary>Radii, metres: the crater (or an airburst's core, where nothing stood), the flattened ring, the dead ash ground, and where it has faded back to the land's own.</summary>
    public float Crater, Ruin, Dead, Fade;
    /// <summary>Where the dead ground has faded back to the land's own colour and growth, metres (the years shrink it with the dead ground; 0: the land has all come back).</summary>
    public float Faded;
    /// <summary>A ground burst's bowl: its depth below the land and its thrown rim, metres. None for an airburst.</summary>
    public float Depth, Rim;
    /// <summary>Burst in the air (or on the water): no crater, the same flattening and burning.</summary>
    public bool Air;
    /// <summary>Came down on the water, at a ship target (a port, an anchorage).</summary>
    public bool Naval;
    /// <summary>Missed what it was aimed at, or aimed at nothing (over open country).</summary>
    public bool Miss, Wild;
    /// <summary>What it was aimed at (a target kind of data/strikes.txt), empty over open country, "site" for a blast site's own.</summary>
    public string Target = "";
    /// <summary>The target's middle and reach, metres.</summary>
    public double AimX, AimZ;
    public float AimReach;
    /// <summary>The strike record it came from (nuclear, bombardment...), or "site".</summary>
    public string Kind = "";
    /// <summary>One shell hole of a war's crater field (round its targets, along its fronts and approach roads), not a strike aimed at one target.</summary>
    public bool Field;
    /// <summary>Came down in the target itself (a nuclear ground burst in the city it hit), not beside it: whatever stood in its bowl is gone.</summary>
    public bool Within;
    /// <summary>Flattens by distance: foundations and tower stumps by the rim, then stumps a storey or two tall, then gutted shells, then standing ruins (<see cref="StateTransforms.Flattened"/>, by <see cref="Strength"/>).</summary>
    public bool Flatten;
    /// <summary>Radiation at ground zero, RAD: the crater and its ring raise the suit's reading, falling to nothing at the dead ring's edge (0: none).</summary>
    public float Rad;

    /// <summary>A crater in the ground (not an airburst, not on the water).</summary>
    public bool Bowl => !Air && !Naval && Depth > 0f;

    /// <summary>The radiation this blast leaves at a distance from ground zero, RAD: all of it in the crater, easing to none at the dead ring's edge.</summary>
    public float RadiationAt(float distance)
    {
        if (Rad <= 0f || distance >= Dead) return 0f;
        if (distance <= Crater) return Rad;
        float t = (distance - Crater) / Math.Max(1f, Dead - Crater);
        return Rad * (1f - t * t * (3f - 2f * t));
    }

    /// <summary>How much of the blast reaches a point this far from ground zero: 1 inside the crater, easing to 0 at the fade.</summary>
    public float Strength(float distance) =>
        distance <= Crater ? 1f : distance >= Fade ? 0f : 1f - (distance - Crater) / (Fade - Crater);

    /// <summary>How far round ground zero a crater's bowl and rim change the ground, metres.</summary>
    public float BowlReach => Crater * 1.5f + BowlBlend;

    /// <summary>How far past the rim the bowl eases back into the land (a shell hole's own few metres; a big crater's up to 36).</summary>
    public float BowlBlend => Math.Clamp(Crater * 0.6f, 3f, 36f);

    /// <summary>
    /// A blast site's own blast at world scale (its place is the crater and the inner ring; the land round
    /// it takes the rest): the reach the dead-ground tint and the ruin ring always had, now one record.
    /// </summary>
    public static Strike FromSite(double x, double z, BlastZone zone, string family, float depth = 6f) => new()
    {
        X = x, Z = z, AimX = x, AimZ = z, Crater = zone.Crater, Ruin = zone.Ruin * 3f, Dead = zone.Dead * 3f, Fade = zone.Fade * 4f, Faded = zone.Fade * 4f,
        Depth = depth, Target = family, Kind = "site", Site = true,
    };

    /// <summary>A blast site's own: its crater is the place's own stamp, already in the ground.</summary>
    public bool Site;

    public override string ToString() =>
        $"{Kind} {(Air ? Naval ? "water" : "air" : "ground")} burst on {(Target.Length == 0 ? "open country" : Target)}{(Miss ? " (missed)" : "")} at {X:F0},{Z:F0}, crater {Crater:F0} m, dead {Dead:F0} m, fade {Fade:F0} m";
}

/// <summary>How one kind of war came down (a <c>strike</c> record of data/strikes.txt).</summary>
public sealed class StrikeKind
{
    public string Id = "";
    public readonly List<string> Falls = new();
    public float CountLo = 4, CountHi = 10, CraterLo = 20, CraterHi = 40, Depth = 0.25f, Rim = 0.08f, Ruin = 5, Dead = 8, Fade = 14;
    /// <summary>Metres added to the ruin, dead and fade radii past their multiples of the crater (a big blast's rings in metres, not multiples of a 250 m bowl).</summary>
    public float RuinAdd, DeadAdd, FadeAdd;
    public float Air, Miss, SpreadLo = 200, SpreadHi = 500, Wild;
    public int SalvoLo = 1, SalvoHi = 1;
    public float SalvoStep = 60;
    public readonly float[] Era = { 1f, 1f, 1f };
    public float AgedDepth, AgedRim, AgedDead;
    /// <summary>Ground bursts come down in the target itself, not beside it.</summary>
    public bool Centre;
    /// <summary>Flattens the places round it by distance (<see cref="Strike.Flatten"/>).</summary>
    public bool Flatten;
    /// <summary>Radiation at ground zero, RAD, and how much of it a century takes away.</summary>
    public float Rad, AgedRad;
    /// <summary>Shell holes in its crater field on a 16x8 km world, mildest to hardest fall, and their radii, metres (most of them near the low end).</summary>
    public float FieldLo, FieldHi, ShellLo = 3, ShellHi = 10;

    public float RuinOf(float r) => r * Ruin + RuinAdd;
    public float DeadOf(float r) => r * Dead + DeadAdd;
    public float FadeOf(float r) => r * Fade + FadeAdd;
}

/// <summary>What a strike aims at (a <c>target</c> record of data/strikes.txt).</summary>
public sealed class StrikeRule
{
    public string Kind = "";
    /// <summary>Any one of these, every word of it present.</summary>
    public readonly List<string[]> Match = new();
    public float Value = 1f;
    public int Takes = 1;
    /// <summary>The most of a world's strikes this kind of target draws, as a share of them (a war's roads never take most of it).</summary>
    public float Share = 1f;
    public bool Hard, Naval;
    /// <summary>Lived in: a strike whose ground bursts come down in its target (<see cref="StrikeKind.Centre"/>) comes down in the middle of this one.</summary>
    public bool Within;
    public float? Air;
    public readonly float[] Era = { 1f, 1f, 1f };

    public bool Fits(ICollection<string> words) => Match.Any(all => all.All(words.Contains));
}

/// <summary>Something a strike could aim at: a place, the city, a bridge, a junction. World metres.</summary>
public sealed record StrikeTarget(string Kind, double X, double Z, float Reach, float Value, int Takes, bool Hard, float? Air, bool Naval, float Share = 1f, bool Within = false);

/// <summary>The land as the strike planner sees it, in metres. Plain questions, so a plan can be made without a planet.</summary>
public interface IStrikeLand
{
    double Width { get; }
    double Depth { get; }
    /// <summary>Open water: the sea or a lake, not a river.</summary>
    bool Sea(double x, double z);
    /// <summary>Dry land a blast can come down on.</summary>
    bool Dry(double x, double z);
    /// <summary>Ground a crater of this radius can be punched into here: dry and firm, clear of every place, road and ruin.</summary>
    bool Crater(double x, double z, float radius, float depth);
    /// <summary>Ground a crater of this radius can be punched into in the middle of what stands here (a ground burst in a city): dry and firm, out of the water, whatever is built on it.</summary>
    bool Within(double x, double z, float radius, float depth);
    /// <summary>Points along the roads that lead to the targets: where a war's shelling walks along its approaches.</summary>
    IReadOnlyList<(double X, double Z)> Approaches { get; }
}

/// <summary>The strikes and targets of data/strikes.txt.</summary>
public sealed class StrikeBook
{
    public readonly List<StrikeKind> Kinds = new();
    public readonly List<StrikeRule> Rules = new();

    /// <summary>The strikes a fall brings, or null for a fall that dropped nothing.</summary>
    public StrikeKind? For(string fall) => Kinds.FirstOrDefault(k => k.Falls.Contains(fall));

    /// <summary>The first rule a target's words fit (null: nothing aims at it).</summary>
    public StrikeRule? Rule(ICollection<string> words) => Rules.FirstOrDefault(r => r.Fits(words));

    /// <summary>A target for something with these words at a point, worth what its rule says on a world of this tech; null if nothing would aim at it.</summary>
    public StrikeTarget? Target(ICollection<string> words, double x, double z, float reach, TechLevel tech)
    {
        if (Rule(words) is not { } r || r.Value <= 0f) return null;
        return new StrikeTarget(r.Kind, x, z, reach, r.Value * r.Era[(int)tech], r.Takes, r.Hard, r.Air, r.Naval, r.Share, r.Within);
    }

    public static StrikeBook Parse(string text, string source)
    {
        var book = new StrikeBook();
        int n = 0;
        static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);
        foreach (string raw in text.Replace("\r", "").Split('\n'))
        {
            n++;
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            FormatException Err(string m) => new($"{source}:{n}: {m}");
            string[] fields = line.Split(" | ");
            string[] head = fields[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (head.Length != 2) throw Err($"'{fields[0]}' isn't a record");
            void Era(float[] era, string[] p)
            {
                for (int i = 1; i + 1 < p.Length; i += 2)
                    era[p[i] switch { "L" => 0, "M" => 1, "H" => 2, _ => throw Err($"'{p[i]}' isn't a tech level") }] = F(p[i + 1]);
            }
            if (head[0] == "strike")
            {
                var k = new StrikeKind { Id = head[1] };
                foreach (string field in fields.Skip(1))
                {
                    string[] p = field.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    switch (p[0])
                    {
                        case "falls": k.Falls.AddRange(p.Skip(1)); break;
                        case "count": k.CountLo = F(p[1]); k.CountHi = F(p[2]); break;
                        case "crater": k.CraterLo = F(p[1]); k.CraterHi = F(p[2]); break;
                        case "depth": k.Depth = F(p[1]); break;
                        case "rim": k.Rim = F(p[1]); break;
                        case "ruin": k.Ruin = F(p[1]); if (p.Length > 2) k.RuinAdd = F(p[2]); break;
                        case "dead": k.Dead = F(p[1]); if (p.Length > 2) k.DeadAdd = F(p[2]); break;
                        case "fade": k.Fade = F(p[1]); if (p.Length > 2) k.FadeAdd = F(p[2]); break;
                        case "centre": k.Centre = p[1] == "yes"; break;
                        case "flatten": k.Flatten = p[1] == "yes"; break;
                        case "rad": k.Rad = F(p[1]); if (p.Length > 2) k.AgedRad = F(p[2]); break;
                        case "field": k.FieldLo = F(p[1]); k.FieldHi = F(p[2]); break;
                        case "shell": k.ShellLo = F(p[1]); k.ShellHi = F(p[2]); break;
                        case "air": k.Air = F(p[1]); break;
                        case "miss": k.Miss = F(p[1]); break;
                        case "spread": k.SpreadLo = F(p[1]); k.SpreadHi = F(p[2]); break;
                        case "wild": k.Wild = F(p[1]); break;
                        case "salvo":
                            k.SalvoLo = int.Parse(p[1], CultureInfo.InvariantCulture);
                            k.SalvoHi = int.Parse(p[2], CultureInfo.InvariantCulture);
                            if (p.Length > 4 && p[3] == "step") k.SalvoStep = F(p[4]);
                            break;
                        case "era": Era(k.Era, p); break;
                        case "aged": k.AgedDepth = F(p[1]); k.AgedRim = F(p[2]); k.AgedDead = F(p[3]); break;
                        default: throw Err($"unknown keyword '{p[0]}'");
                    }
                }
                if (k.Falls.Count == 0) throw Err($"strike {k.Id} names no fall");
                book.Kinds.Add(k);
            }
            else if (head[0] == "target")
            {
                var r = new StrikeRule { Kind = head[1] };
                foreach (string field in fields.Skip(1))
                {
                    string[] p = field.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    switch (p[0])
                    {
                        case "match": r.Match.AddRange(p.Skip(1).Select(m => m.Split('+'))); break;
                        case "value": r.Value = F(p[1]); break;
                        case "takes": r.Takes = int.Parse(p[1], CultureInfo.InvariantCulture); break;
                        case "share": r.Share = F(p[1]); break;
                        case "hard": r.Hard = p[1] == "yes"; break;
                        case "naval": r.Naval = p[1] == "yes"; break;
                        case "within": r.Within = p[1] == "yes"; break;
                        case "air": r.Air = F(p[1]); break;
                        case "era": Era(r.Era, p); break;
                        default: throw Err($"unknown keyword '{p[0]}'");
                    }
                }
                if (r.Match.Count == 0) throw Err($"target {r.Kind} matches nothing");
                book.Rules.Add(r);
            }
            else throw Err($"'{head[0]}' isn't strike or target");
        }
        return book;
    }
}

/// <summary>
/// Where a war's blasts came down. A nuclear exchange or a bombardment aims at what was worth it: the
/// old capital and the cities, bases and airfields, ports, power, refineries and works, the bridges and
/// junctions of the main roads, each drawn by its worth (data/strikes.txt) on a world of its tech, the
/// bigger ones drawing more and bigger strikes. A few miss and come down near their target, the odd
/// one bursts over open country, and none comes down in open water unless it was aimed at ships.
/// How many: the roll's severity (how hard the world fell) on the fall's own range, more on a bigger
/// world, a third of that where the war is only a scar on one region. How old: a century since leaves
/// shallower bowls, worn rims and less dead ground. A ground burst's crater goes where the land takes
/// one (<see cref="IStrikeLand.Crater"/>): beside a place, never under it, off the roads; where none
/// fits near its target it burst in the air instead. Deterministic from the seed.
/// </summary>
public static class StrikePlan
{
    /// <summary>How far round a scar's region its strikes stay, metres.</summary>
    public const double ScarReach = 3000;

    /// <summary>Staging only (a screenshot): a war's first strike goes on the target of this kind (city, capital...) as a ground burst. Null in play.</summary>
    public static string? Force;

    /// <param name="spare">Ground no strike reaches: a start world's bunker and the town by it (x, z, radius; a strike's flattened ring stays outside).</param>
    public static List<Strike> Make(StrikeBook book, WorldRoll roll, TechLevel before, IReadOnlyList<StrikeTarget> targets, IStrikeLand land, ulong seed,
        IReadOnlyList<(double X, double Z, double R)>? spare = null)
    {
        var strikes = new List<Strike>();
        if (book.Kinds.Count == 0) return strikes;
        WorldReading reading = WorldReading.Of(roll);
        var falls = roll.All(LayerKind.Fall).ToList();
        for (int f = 0; f < falls.Count; f++)
            if (book.For(falls[f].Id) is { } kind)
                Strikes(kind, f > 0, reading, before, targets, land, Hash.Combine(seed, Hash.Of(kind.Id + (f > 0 ? ".scar" : ""))), spare ?? Array.Empty<(double, double, double)>(), strikes);
        return strikes;
    }

    static double Dist(IStrikeLand land, double ax, double az, double bx, double bz)
    {
        double dx = Math.Abs(Wrap(ax - bx, land.Width)), dz = Math.Abs(Wrap(az - bz, land.Depth));
        return Math.Sqrt(dx * dx + dz * dz);
    }

    static double Wrap(double d, double span)
    {
        d %= span;
        if (d > span / 2) d -= span;
        if (d < -span / 2) d += span;
        return d;
    }

    static double Canon(double v, double span) => (v % span + span) % span;

    static void Strikes(StrikeKind kind, bool scar, WorldReading reading, TechLevel before, IReadOnlyList<StrikeTarget> all, IStrikeLand land, ulong seed,
        IReadOnlyList<(double X, double Z, double R)> spare, List<Strike> strikes)
    {
        var rng = new Rng(seed);
        double w = land.Width, h = land.Depth;
        float area = (float)Math.Clamp(w * h / 128e6, 0.5, 2.5);
        float age = reading.Age;
        float share = area * kind.Era[(int)before] * (scar ? 0.35f : 1f);
        float wanted = (kind.CountLo + (kind.CountHi - kind.CountLo) * reading.Severity) * share;
        int count = kind.CountHi <= 0f ? 0 : Math.Max(1, (int)MathF.Round(wanted));
        // The start's ground is spared what a blast flattens (the spared radius itself keeps the faint fade off the landing).
        bool Spared(double x, double z, float reach) => spare.Any(s => Dist(land, s.X, s.Z, x, z) < s.R + reach);

        // Every bowl so far by 64 m cell, so a crater field of hundreds stays quick to keep apart.
        const double Cell = 64;
        int nx = Math.Max(1, (int)Math.Ceiling(w / Cell)), nz = Math.Max(1, (int)Math.Ceiling(h / Cell));
        var grid = new Dictionary<(int, int), List<Strike>>();
        (int, int) Key(double x, double z) => ((int)(Canon(x, w) / Cell) % nx, (int)(Canon(z, h) / Cell) % nz);
        float biggest = 0f;
        void Index(Strike s)
        {
            var k = Key(s.X, s.Z);
            if (!grid.TryGetValue(k, out var l)) grid[k] = l = new List<Strike>();
            l.Add(s);
            biggest = MathF.Max(biggest, s.BowlReach);
        }
        foreach (Strike s in strikes.Where(s => s.Bowl)) Index(s);
        void Add(Strike s)
        {
            strikes.Add(s);
            if (s.Bowl) Index(s);
        }
        // Craters never overlap: a big one keeps clear of every bowl's reach; a shell hole keeps out of the
        // big ones' reach and only out of the other holes' own bowls (a barrage's holes all but touch).
        bool Apart(double x, double z, float r, bool small)
        {
            double reach = biggest + r * 1.5 + 20;
            int span = (int)Math.Ceiling(reach / Cell);
            var (kx, kz) = Key(x, z);
            for (int dz = -span; dz <= span; dz++)
                for (int dx = -span; dx <= span; dx++)
                {
                    if (!grid.TryGetValue((((kx + dx) % nx + nx) % nx, ((kz + dz) % nz + nz) % nz), out var list)) continue;
                    foreach (Strike s in list)
                    {
                        double d = Dist(land, s.X, s.Z, x, z);
                        if (small && s.Field ? d <= (s.Crater + r) * 1.2 + 1 : d <= s.BowlReach + r * 1.5 + (small ? 4 : 20)) return false;
                    }
                }
            return true;
        }

        // Whatever lies wholly in the spared ground is never aimed at; a city reaching out of it may be hit on its far side.
        var pool = all.Where(t => !Spared(t.X, t.Z, -t.Reach)).ToList();
        StrikeTarget? focus = null;
        if (scar && pool.Count > 0)
        {
            // A scar is one region's war: its strikes stay round one target's country.
            focus = pool[Math.Max(0, rng.PickWeighted(pool.Select(t => t.Value).ToList()))];
            pool = pool.Where(t => Dist(land, t.X, t.Z, focus.X, focus.Z) < ScarReach).ToList();
        }
        // Worth counts twice over: a war spends its first and biggest on its capital and cities, not on the many small things.
        var weight = pool.Select(t => t.Value * t.Value).ToArray();
        var left = pool.Select(t => t.Takes).ToArray();
        var drawn = new Dictionary<string, int>();
        int Drawn(StrikeTarget t) => drawn.GetValueOrDefault(t.Kind);
        bool Open(int k) => left[k] > 0 && Drawn(pool[k]) < Math.Max(1, (int)MathF.Ceiling(pool[k].Share * count));

        Strike Make(double x, double z, float r, bool air, string target, StrikeTarget? t)
        {
            // The years take the dead ground back: what grows here comes back in from its edge, and where it
            // has all come back the land is its own again (a nuclear crater's barely; a shell hole's wholly).
            float life = Math.Max(0f, 1f - kind.AgedDead * age), dead = kind.DeadOf(r) * life;
            return new Strike
            {
                X = Canon(x, w), Z = Canon(z, h), Kind = kind.Id, Target = target, Air = air,
                Crater = r, Ruin = kind.RuinOf(r), Dead = dead, Fade = kind.FadeOf(r), Faded = life <= 0f ? 0f : MathF.Max(dead, kind.FadeOf(r) * MathF.Min(1f, life * 1.5f)),
                Depth = air ? 0f : r * kind.Depth * (1f - kind.AgedDepth * age), Rim = air ? 0f : r * kind.Rim * (1f - kind.AgedRim * age),
                AimX = t?.X ?? x, AimZ = t?.Z ?? z, AimReach = t?.Reach ?? 0f,
                Flatten = kind.Flatten, Rad = kind.Rad * Math.Max(0f, 1f - kind.AgedRad * age),
            };
        }
        // The nearest ground round a point a crater will take, out to a reach.
        (double X, double Z)? Ground(double x, double z, float r, float depth, double reach, bool within = false, double step = 0)
        {
            double start = rng.Float() * Math.Tau;
            if (step <= 0) step = Math.Max(18, r * 0.6);
            for (double ring = 0; ring <= reach; ring += step)
            {
                int around = ring == 0 ? 1 : Math.Max(8, (int)(ring * Math.Tau / step));
                for (int k = 0; k < around; k++)
                {
                    double a = start + k * Math.Tau / around;
                    double cx = Canon(x + Math.Cos(a) * ring, w), cz = Canon(z + Math.Sin(a) * ring, h);
                    if (Spared(cx, cz, kind.RuinOf(r)) || !Apart(cx, cz, r, false)) continue;
                    if (within ? land.Within(cx, cz, r, depth) : land.Crater(cx, cz, r, depth)) return (cx, cz);
                }
            }
            return null;
        }

        for (int n = 0; n < count; n++)
        {
            // The odd one over open country, aimed at nothing.
            if (rng.Chance(kind.Wild) && !(n == 0 && Force != null))
            {
                float r = kind.CraterLo;
                for (int tries = 0; tries < 40; tries++)
                {
                    double x = rng.Float() * w, z = rng.Float() * h;
                    if (!land.Dry(x, z) || Spared(x, z, kind.RuinOf(r)) || all.Any(t => Dist(land, t.X, t.Z, x, z) < t.Reach + 900)) continue;
                    // A war that bursts nothing in the air drops it on the ground; where the ground won't take it, the next spot.
                    bool overhead = kind.Air > 0f;
                    if (!overhead && !(Apart(x, z, r, false) && land.Crater(x, z, r, r * kind.Depth))) continue;
                    Strike s = Make(x, z, r, overhead, "", null);
                    s.Wild = true;
                    Add(s);
                    break;
                }
                continue;
            }
            bool forced = n == 0 && Force != null && pool.Any(t => t.Kind == Force);
            int i = forced ? pool.FindIndex(t => t.Kind == Force) : rng.PickWeighted(weight.Select((v, k) => Open(k) ? v : 0f).ToList());
            if (i < 0) break;
            left[i]--;
            drawn[pool[i].Kind] = Drawn(pool[i]) + 1;
            weight[i] *= 0.4f;
            StrikeTarget t = pool[i];
            // The bigger the target, the bigger what was thrown at it.
            float big = Math.Clamp(t.Value / 11f, 0f, 1f);
            float radius = kind.CraterLo + (kind.CraterHi - kind.CraterLo) * Math.Clamp(0.2f + 0.65f * big + 0.35f * (rng.Float() - 0.5f), 0f, 1f);
            float depth = radius * kind.Depth;
            bool air = !t.Hard && kind.Air > 0f && rng.Chance(t.Air ?? kind.Air) && !forced;
            bool miss = rng.Chance(kind.Miss) && !forced;
            double ax = t.X, az = t.Z;
            {
                // Over the target, a little off its middle.
                double a = rng.Float() * Math.Tau, d = rng.Float() * t.Reach * 0.25;
                ax += Math.Cos(a) * d;
                az += Math.Sin(a) * d;
            }
            if (miss)
            {
                // Off target, near it, on land.
                double off = t.Reach * 0.5 + kind.SpreadLo + (kind.SpreadHi - kind.SpreadLo) * rng.Float(), start = rng.Float() * Math.Tau;
                miss = false;
                for (int k = 0; k < 16 && !miss; k++)
                {
                    double a = start + k * Math.Tau / 16;
                    double mx = Canon(t.X + Math.Cos(a) * off, w), mz = Canon(t.Z + Math.Sin(a) * off, h);
                    if (!land.Dry(mx, mz)) continue;
                    (ax, az, miss) = (mx, mz, true);
                }
            }
            // A ship target's strike came down on the water by it, where there is water by it.
            if (t.Naval && !miss)
            {
                double start = rng.Float() * Math.Tau;
                bool wet = false;
                for (double ring = t.Reach + 60; ring <= t.Reach + 500 && !wet; ring += 60)
                    for (int k = 0; k < 16 && !wet; k++)
                    {
                        double a = start + k * Math.Tau / 16;
                        double sx = Canon(t.X + Math.Cos(a) * ring, w), sz = Canon(t.Z + Math.Sin(a) * ring, h);
                        if (!land.Sea(sx, sz)) continue;
                        Strike s = Make(sx, sz, radius, true, t.Kind, t);
                        s.Naval = true;
                        Add(s);
                        wet = true;
                    }
                if (wet) continue;
            }
            if (!air)
            {
                // A ground burst in the target itself (the city's streets, the town's heart), where the ground there takes a crater.
                if (kind.Centre && t.Within && !miss && Ground(t.X, t.Z, radius, depth, t.Reach + radius * 0.3, within: true, step: 30) is { } c)
                {
                    Strike s = Make(c.X, c.Z, radius, false, t.Kind, t);
                    s.Within = true;
                    Add(s);
                    continue;
                }
                // Else a crater beside the target (never under a place or on a road), or, near a miss, round where it fell.
                double reach = miss ? 220 : t.Reach + radius * 1.5 + 420;
                if (Ground(ax, az, radius, depth, reach) is { } g)
                {
                    Strike s = Make(g.X, g.Z, radius, false, t.Kind, t);
                    s.Miss = miss;
                    Add(s);
                    // A stick of them in a line across the target.
                    int stick = rng.Between(kind.SalvoLo, kind.SalvoHi);
                    double sa = rng.Float() * Math.Tau;
                    for (int k = 1; k < stick; k++)
                    {
                        double step = (kind.SalvoStep + radius * 3.5) * ((k + 1) / 2) * (k % 2 == 1 ? 1 : -1);
                        double sx = Canon(g.X + Math.Cos(sa) * step, w), sz = Canon(g.Z + Math.Sin(sa) * step, h);
                        float r2 = radius * (0.6f + 0.4f * rng.Float());
                        if (Spared(sx, sz, kind.RuinOf(r2)) || !Apart(sx, sz, r2, false) || !land.Crater(sx, sz, r2, r2 * kind.Depth)) continue;
                        Strike more = Make(sx, sz, r2, false, t.Kind, t);
                        more.Miss = miss;
                        Add(more);
                    }
                    continue;
                }
                // Nowhere near it would take a crater: it burst in the air over it.
                if (kind.Air <= 0f) continue;
            }
            if (!land.Dry(ax, az) || Spared(ax, az, kind.RuinOf(radius))) continue;
            Strike burst = Make(ax, az, radius, true, t.Kind, t);
            burst.Miss = miss;
            Add(burst);
        }

        Field(kind, reading, share, pool, focus, land, rng, w, h, Spared, (x, z, r) => Apart(x, z, r, true), Make, Add);
    }

    /// <summary>
    /// A war's crater field: shell holes by the dozen to the hundreds, most of them small, in barrages of a few
    /// to a dozen, walked in lines or scattered round one aim: round its targets, along the fronts between
    /// them, and down the roads that lead to them. Never in the water, on a road, a place or another hole.
    /// </summary>
    static void Field(StrikeKind kind, WorldReading reading, float share, List<StrikeTarget> pool, StrikeTarget? focus, IStrikeLand land, Rng rng, double w, double h,
        Func<double, double, float, bool> spared, Func<double, double, float, bool> apart, Func<double, double, float, bool, string, StrikeTarget?, Strike> make, Action<Strike> add)
    {
        int holes = (int)MathF.Round((kind.FieldLo + (kind.FieldHi - kind.FieldLo) * reading.Severity) * share);
        if (holes <= 0 || kind.ShellHi <= 0f) return;
        // The fronts: lines between targets near enough to have been fought over.
        var fronts = new List<(StrikeTarget A, StrikeTarget B)>();
        for (int a = 0; a < pool.Count; a++)
            for (int b = a + 1; b < pool.Count; b++)
            {
                double d = Dist(land, pool[a].X, pool[a].Z, pool[b].X, pool[b].Z);
                if (d > 600 && d < 6000) fronts.Add((pool[a], pool[b]));
            }
        var roads = focus == null ? land.Approaches : land.Approaches.Where(p => Dist(land, p.X, p.Z, focus.X, focus.Z) < ScarReach).ToList();
        var value = pool.Select(t => t.Value + 1f).ToList();
        // Most holes small, the odd one big: r = lo (hi / lo)^(u^2).
        float Shell() { float u = rng.Float(); return kind.ShellLo * MathF.Pow(kind.ShellHi / kind.ShellLo, u * u); }
        int placed = 0;
        for (int tries = 0; placed < holes && tries < holes + 60; tries++)
        {
            double cx, cz;
            float roll = rng.Float();
            if (roll < 0.25f && roads.Count > 0)
            {
                // Down an approach road, beside it.
                var p = roads[rng.Range(0, roads.Count)];
                (cx, cz) = (p.X, p.Z);
            }
            else if (roll < 0.55f && fronts.Count > 0)
            {
                // Along a front, either side of its line.
                var (a, b) = fronts[rng.Range(0, fronts.Count)];
                double f = rng.Float(), dx = Wrap(b.X - a.X, w), dz = Wrap(b.Z - a.Z, h), len = Math.Max(1, Math.Sqrt(dx * dx + dz * dz)), off = (rng.Float() - 0.5) * 2 * (60 + 200 * rng.Float());
                cx = a.X + dx * f - dz / len * off;
                cz = a.Z + dz * f + dx / len * off;
            }
            else if (pool.Count > 0)
            {
                // Round a target, the more it was worth the more it was shelled.
                StrikeTarget t = pool[Math.Max(0, rng.PickWeighted(value))];
                double a = rng.Float() * Math.Tau, d = t.Reach + 30 + 380 * rng.Float();
                cx = t.X + Math.Cos(a) * d;
                cz = t.Z + Math.Sin(a) * d;
            }
            else (cx, cz) = (rng.Float() * w, rng.Float() * h);
            cx = Canon(cx, w);
            cz = Canon(cz, h);
            if (!land.Dry(cx, cz)) continue;
            // A barrage: a walk of holes in a line, or a scatter round one aim point.
            int n = Math.Min(holes - placed, rng.Between(3, 12));
            bool line = rng.Chance(0.5f);
            double heading = rng.Float() * Math.Tau, pace = 7 + 14 * rng.Float();
            for (int k = 0; k < n; k++)
            {
                double px, pz;
                if (line)
                {
                    double along = (k - n * 0.5) * pace, aside = (rng.Float() - 0.5) * pace;
                    px = cx + Math.Cos(heading) * along - Math.Sin(heading) * aside;
                    pz = cz + Math.Sin(heading) * along + Math.Cos(heading) * aside;
                }
                else
                {
                    double a = rng.Float() * Math.Tau, d = Math.Sqrt(rng.Float()) * (20 + 7 * n);
                    px = cx + Math.Cos(a) * d;
                    pz = cz + Math.Sin(a) * d;
                }
                px = Canon(px, w);
                pz = Canon(pz, h);
                float r = Shell();
                if (spared(px, pz, kind.RuinOf(r)) || !apart(px, pz, r) || !land.Crater(px, pz, r, r * kind.Depth)) continue;
                Strike s = make(px, pz, r, false, "field", null);
                s.Field = true;
                add(s);
                placed++;
            }
        }
    }
}
