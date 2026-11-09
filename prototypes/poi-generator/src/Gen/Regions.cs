#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>What a stretch of a world was for.</summary>
public enum RegionKind : byte { Town, Port, Hamlet, Industry, Farmland, Wilderness }

/// <summary>A region: where it is, how far it reaches, what it was for, and the town it belonged to.</summary>
public sealed class Region
{
    public int Index;
    public RegionKind Kind;
    public double X, Z, Radius;
    /// <summary>The town it served (its own index for a town), or -1.</summary>
    public int Town = -1;
}

/// <summary>A place the plan wants, with its reason: nothing stands anywhere without one.</summary>
public sealed class PlannedPlace
{
    public int Index;
    public string Family = "";
    public double X, Z;
    public int Region = -1;
    /// <summary>What it's there for: hub, need, industry, plant, depot, farm, camp, fill or break.</summary>
    public string Role = "";
    /// <summary>The place it served, or -1.</summary>
    public int Serves = -1;
    public string Why = "";
    /// <summary>One of the share of places that break from the theme.</summary>
    public bool Breaks;
    public bool Primary;
    public Starport? Port;
    /// <summary>The seated place, once the planet has set it down (the planet's own type).</summary>
    public object? Seated;
}

/// <summary>A connection the plan wants, and why: a road, a railway or a power line.</summary>
public sealed record PlannedLink(int A, int B, string Kind, string Why);

/// <summary>The land as the planner sees it, in metres. Plain numbers, so a plan can be made without a planet.</summary>
public interface ILand
{
    double Width { get; }
    double Depth { get; }
    float Height(double x, double z);
    bool Wet(double x, double z);
    /// <summary>Height and wetness together, where one look at the ground gives both.</summary>
    (float Height, bool Wet) Ground(double x, double z) => (Height(x, z), Wet(x, z));
    /// <summary>Rich ground: where a mine would go.</summary>
    bool Rich(double x, double z);
}

/// <summary>
/// The step before any place is set down: what each part of a world was for. Towns first, each with what
/// it needed to run and what it lived on: a sewage works downhill of it, water uphill, the plant that fed
/// its lines, the depot its railway ended at, farmland beyond; then the port, camps and wrecks; then a
/// share of places that break from the theme, out in the empty land. Every place has a reason and most
/// have a connection, so what's where can be read off the land, and everything else stays empty. From
/// the world's rolled layers, deterministic from its seed.
/// </summary>
public sealed class RegionPlan
{
    public readonly List<Region> Regions = new();
    public readonly List<PlannedPlace> Places = new();
    public readonly List<PlannedLink> Links = new();
    public WorldRoll Roll = null!;
    double _w, _h;

    /// <summary>How far edge to edge the placement grid keeps from a seam.</summary>
    const double Margin = 420;

    public PlannedPlace? ByRole(string role) => Places.FirstOrDefault(p => p.Role == role);

    public double Dist(double ax, double az, double bx, double bz)
    {
        double dx = Math.Abs(ax - bx), dz = Math.Abs(az - bz);
        dx = Math.Min(dx, _w - dx);
        dz = Math.Min(dz, _h - dz);
        return Math.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>The region at a point: the nearest whose reach holds it, or null out in the wilderness.</summary>
    public Region? RegionAt(double x, double z) =>
        Regions.Where(r => Dist(r.X, r.Z, x, z) <= r.Radius).OrderBy(r => Dist(r.X, r.Z, x, z) / r.Radius).FirstOrDefault();

    /// <summary>Half the width of the ground a family takes.</summary>
    public static double HalfOf(FamilyDef f) => f.Tags.Contains("grand") ? (f.Id == "works_complex" ? 200 : 165) : f.Tier >= PoiTier.Spaceport ? 240 : 70;

    /// <summary>Values of a world's roll that leave many with nothing: refugees on the roads, rationing, a people walking away.</summary>
    static readonly HashSet<string> Uprooted = new() { "road_camps", "soup_kitchens", "ration_shelves", "luggage", "queue_slips" };
    static readonly HashSet<string> Poor = new() { "subsistence", "famine", "economic_collapse", "slide", "supply_cutoff", "drought", "tide_of_refugees" };

    /// <summary>
    /// How many shanty towns a world wants: one where people survive, two more where its fall left many
    /// with nothing, one more where it is poor; none where nobody survives (tests read the rule too).
    /// </summary>
    public static int ShantiesWanted(WorldRoll roll, WorldProfile profile)
    {
        if (!profile.Inhabited || roll.Fact("people") == "none" || profile.Current == PopulationNow.None) return 0;
        bool uprooted = roll.Tags.Any(Uprooted.Contains), poor = profile.Current is PopulationNow.Survivors or PopulationNow.Outposts || roll.Rolled.Any(l => Poor.Contains(l.Id));
        return 1 + (uprooted ? 2 : 0) + (poor ? 1 : 0);
    }

    public static RegionPlan Make(PoiLibrary lib, ILand land, WorldRoll roll, WorldProfile profile, ulong seed, (double X, double Z) landing, bool start)
    {
        var plan = new RegionPlan { Roll = roll, _w = land.Width, _h = land.Depth };
        new Planner(plan, lib, land, roll, profile, new Rng(Hash.Combine(seed, 0x4E610AUL)), landing, start).Run();
        return plan;
    }

    sealed class Planner
    {
        readonly RegionPlan _p;
        readonly PoiLibrary _lib;
        readonly ILand _land;
        readonly WorldRoll _roll;
        readonly WorldProfile _profile;
        readonly Rng _r;
        readonly (double X, double Z) _landing;
        readonly bool _start;
        readonly List<(double X, double Z)> _open = new();
        /// <summary>
        /// How many places a world of this size and these people wants, against a 16 x 8 km world of
        /// a hundred thousand (the size the counts in the world-layer data are written for): its towns,
        /// hamlets, fields, wrecks and the cap all grow with it. Area raised to three quarters, so a
        /// big world is fuller than a small one but never dozens of times slower to build.
        /// </summary>
        double Scale
        {
            get
            {
                double area = _land.Width * _land.Depth / (16000.0 * 8000.0);
                double people = _profile.Inhabited ? Math.Clamp(0.4 + _profile.Population * 0.12, 0.5, 1.6) : 0.5;
                return Math.Pow(area, 0.75) * people;
            }
        }
        int MaxPlaces => Math.Max(34, (int)Math.Round(34 * Scale));

        /// <summary>A region count from the roll, for this world's size and people (never none where the roll had some).</summary>
        int Count(string kind)
        {
            float rolled = _roll.Regions.GetValueOrDefault(kind);
            int n = (int)Math.Round(rolled * Scale);
            return rolled >= 0.5f ? Math.Max(1, n) : n;
        }

        public Planner(RegionPlan p, PoiLibrary lib, ILand land, WorldRoll roll, WorldProfile profile, Rng r, (double, double) landing, bool start)
        {
            (_p, _lib, _land, _roll, _profile, _r, _landing, _start) = (p, lib, land, roll, profile, r, landing, start);
        }

        double Wrap(double v, double size) => (v % size + size) % size;

        /// <summary>How rough ground is across a square, or -1 if any of it is wet or runs into a seam.</summary>
        float Roughness(double x, double z, double half)
        {
            if (x < Margin + half * 0.4 || x > _land.Width - Margin - half * 0.4 || z < Margin + half * 0.4 || z > _land.Depth - Margin - half * 0.4) return -1;
            float lo = float.MaxValue, hi = float.MinValue;
            for (int a = -2; a <= 2; a++)
                for (int b = -2; b <= 2; b++)
                {
                    double sx = x + a * half / 2, sz = z + b * half / 2;
                    var (h, wet) = _land.Ground(Wrap(sx, _land.Width), Wrap(sz, _land.Depth));
                    if (wet) return -1;
                    lo = MathF.Min(lo, h);
                    hi = MathF.Max(hi, h);
                }
            return hi - lo;
        }

        public void Run()
        {
            // Read side by side (a big world has tens of thousands of spots), kept in the same order.
            int rows = (int)Math.Ceiling((_land.Depth - 2 * Margin) / 240), cols = (int)Math.Ceiling((_land.Width - 2 * Margin) / 240);
            var open = new bool[Math.Max(0, rows) * Math.Max(0, cols)];
            System.Threading.Tasks.Parallel.For(0, open.Length, i =>
            {
                double x = Margin + i % cols * 240, z = Margin + i / cols * 240;
                if (x >= _land.Width - Margin || z >= _land.Depth - Margin) return;
                float rough = Roughness(x, z, 240);
                open[i] = rough >= 0 && rough < 26;
            });
            for (int i = 0; i < open.Length; i++)
                if (open[i]) _open.Add((Margin + i % cols * 240, Margin + i / cols * 240));
            if (_open.Count == 0) return;
            var towns = Hubs();
            if (_start) StartCountry();
            foreach (var t in towns) Serve(t);
            Shanties(towns);
            Ports();
            Wrecks();
            Fill();
            Breaks();
            Cover();
        }

        Region AddRegion(RegionKind kind, double x, double z, double radius, int town = -1)
        {
            var r = new Region { Index = _p.Regions.Count, Kind = kind, X = x, Z = z, Radius = radius, Town = town };
            _p.Regions.Add(r);
            return r;
        }

        PlannedPlace? Add(string family, double x, double z, Region? region, string role, string why, int serves = -1, bool capped = true)
        {
            if (capped && _p.Places.Count >= MaxPlaces) return null;
            // A place whose tech the world never had gives way to its age's own (a launch field to a caravan camp).
            if (_profile.ForAge(_lib, _lib.Family(family), Hash.Combine((ulong)_p.Places.Count, (ulong)(x * 7 + z))) is not { } aged) return null;
            family = aged.Id;
            var place = new PlannedPlace { Index = _p.Places.Count, Family = family, X = x, Z = z, Region = region?.Index ?? -1, Role = role, Why = why, Serves = serves };
            _p.Places.Add(place);
            return place;
        }

        /// <summary>
        /// Whether the old world ran railways: where its roads and lines did (an infra layer's rail style), and where its
        /// industry needed one (a trade that names a depot: the mines and works shipped by rail).
        /// </summary>
        bool Railed => _roll.Rail != "none" || _roll.Depot != null;

        /// <summary>Could this family stand on this world at all? A plan never asks for a place the world's tech or past rules out.</summary>
        bool Stands(string id)
        {
            FamilyDef f = _lib.Family(id);
            if (f.Space) return false;
            float[] w = _profile.StateWeights(f);
            if (_roll.States is { } s)
                for (int i = 0; i < w.Length; i++) w[i] = f.StateWeights[i] > 0f && _profile.Fits(f, (PoiState)i) ? s[i] : 0f;
            return w.Any(v => v > 0f) || _profile.StateWeights(f).Any(v => v > 0f);
        }

        string? Choose(Dictionary<string, float> table)
        {
            // What the world's layers ask for, each in its age's own form where its tech doesn't fit.
            var fit = new Dictionary<string, float>();
            foreach (var (id, w) in table)
                if (_profile.ForAge(_lib, _lib.Family(id), Hash.Of(id)) is { } aged && Stands(aged.Id))
                    fit[aged.Id] = fit.GetValueOrDefault(aged.Id) + w;
            return _roll.Pick(fit, _r);
        }

        bool Clear(double x, double z, double half)
        {
            foreach (PlannedPlace o in _p.Places)
                if (_p.Dist(o.X, o.Z, x, z) < half + HalfOf(_lib.Family(o.Family)) + 200) return false;
            return _p.Dist(_landing.X, _landing.Z, x, z) > half + 260;
        }

        /// <summary>The best spot in a ring round a point for a family: level, clear, and however <paramref name="prefer"/> scores it (lower is better).</summary>
        (double X, double Z)? Spot(double cx, double cz, double r0, double r1, string family, Func<double, double, float>? prefer = null, float steepest = 24)
        {
            double half = HalfOf(_lib.Family(family));
            (double, double)? best = null;
            float bestScore = float.MaxValue;
            double start = _r.Float() * Math.PI * 2;
            for (int n = 0; n < 72; n++)
            {
                double a = start + n * 2.399963, t = (n + 0.5) / 72.0, r = r0 + (r1 - r0) * Math.Sqrt(t);
                double x = Wrap(cx + Math.Cos(a) * r, _land.Width), z = Wrap(cz + Math.Sin(a) * r, _land.Depth);
                float rough = Roughness(x, z, half);
                if (rough < 0 || rough > steepest || !Clear(x, z, half)) continue;
                float score = rough + (prefer?.Invoke(x, z) ?? 0f) + _r.Float() * 4f;
                if (score < bestScore) { bestScore = score; best = (x, z); }
            }
            return best;
        }

        /// <summary>
        /// A start world's first sight: the ruined town by the landing site (the planet lays that out itself) is a
        /// town too, and has what it lived on standing in view beyond it.
        /// </summary>
        void StartCountry()
        {
            Region home = AddRegion(RegionKind.Town, _landing.X, _landing.Z, 1300);
            home.Town = home.Index;
            var options = _roll.Industry.Concat(_roll.Farm).Concat(_roll.Poi)
                .Where(kv => !_lib.Family(kv.Key).Tags.Contains("settlement")).GroupBy(kv => kv.Key).ToDictionary(g => g.Key, g => g.First().Value);
            // One of the large works stands within a fair walk: the first big place of a game.
            // (A city's lots are built by its planner, lot by lot, never as works on their own.)
            var large = _lib.Families.Where(f => f.Tags.Contains("grand") && !f.Tags.Contains("city") && Stands(f.Id)).Select(f => f.Id).OrderBy(id => id, StringComparer.Ordinal).ToList();
            bool seated = false;
            foreach (string id in large.OrderBy(_ => _r.Float()))
                if (!seated && Spot(_landing.X, _landing.Z, 700, 1300, id) is { } near)
                    seated = Add(id, near.X, near.Z, home, "landmark", "the works the old town by the landing lived on") != null;
            // Rough country round the landing: the nearest level ground that will take one.
            if (!seated)
                foreach (string id in large)
                    if (!seated && Spot(_landing.X, _landing.Z, 1300, 2600, id) is { } further)
                        seated = Add(id, further.X, further.Z, home, "landmark", "the works the old town by the landing lived on") != null;
            for (int n = seated ? 1 : 0; n < 2; n++)
                if (Choose(options) is { } id && Spot(_landing.X, _landing.Z, 800, 1500, id) is { } s)
                    Add(id, s.X, s.Z, home, "landmark", "what the old town by the landing lived on");
        }

        /// <summary>
        /// Shanty towns: survivors' shacks of salvage (the survivor hamlet's kit, laid out by the architect)
        /// where survivors would gather: round an old town's edge, by water, on the way in. One on a world
        /// where people survive, more where its fall left many with nothing and where it is poor; none
        /// where nobody does.
        /// </summary>
        void Shanties(List<(Region Region, PlannedPlace Hub)> towns)
        {
            const string shack = "survivor_hamlet";
            int n = ShantiesWanted(_roll, _profile);
            if (n == 0 || !_lib.Families.Any(f => f.Id == shack) || !Stands(shack)) return;
            var near = towns.Select(t => (t.Region.X, t.Region.Z)).ToList();
            if (near.Count == 0) near.Add(_landing);
            // Water near: the lowest, wettest ground round a spot is where people draw it.
            float ByWater(double x, double z)
            {
                int wet = 0;
                for (int a = 0; a < 8; a++)
                    if (_land.Wet(Wrap(x + Math.Cos(a * Math.PI / 4) * 260, _land.Width), Wrap(z + Math.Sin(a * Math.PI / 4) * 260, _land.Depth))) wet++;
                return -4f * Math.Min(wet, 3);
            }
            for (int i = 0; i < n; i++)
            {
                var (cx, cz) = near[i % near.Count];
                if (Spot(cx, cz, 650, 1500, shack, ByWater) is not { } s) continue;
                Region region = AddRegion(RegionKind.Hamlet, s.X, s.Z, 350);
                region.Town = region.Index;
                Add(shack, s.X, s.Z, region, "shanty", "survivors' shacks at an old town's edge");
            }
        }

        /// <summary>The towns: picked far apart, on the widest level ground, the hub at each one's heart.</summary>
        List<(Region Region, PlannedPlace Hub)> Hubs()
        {
            var made = new List<(Region, PlannedPlace)>();
            int towns = Count("town"), hamlets = Count("hamlet");
            var spots = new List<(double X, double Z)>();
            bool Far(double x, double z, double d) => spots.All(s => _p.Dist(s.X, s.Z, x, z) >= d) && _p.Dist(_landing.X, _landing.Z, x, z) >= (_start ? 1700 : 500);
            var order = _open.OrderBy(o => _r.Float()).ToList();
            // A big town wants big flat ground: the middle of the open land first (counted by 720 m buckets: a big world has tens of thousands of open spots).
            var buckets = new Dictionary<(int, int), List<(double X, double Z)>>();
            int bx = Math.Max(1, (int)(_land.Width / 720)), bz = Math.Max(1, (int)(_land.Depth / 720));
            (int, int) Bucket(double x, double z) => ((int)(Wrap(x, _land.Width) / _land.Width * bx) % bx, (int)(Wrap(z, _land.Depth) / _land.Depth * bz) % bz);
            foreach (var o in _open)
            {
                var key = Bucket(o.X, o.Z);
                if (!buckets.TryGetValue(key, out var list)) buckets[key] = list = new List<(double, double)>();
                list.Add(o);
            }
            int Wanted(double x, double z)
            {
                var (cx, cz) = Bucket(x, z);
                int n = 0;
                for (int a = -1; a <= 1; a++)
                    for (int b = -1; b <= 1; b++)
                        if (buckets.TryGetValue((((cx + a) % bx + bx) % bx, ((cz + b) % bz + bz) % bz), out var list))
                            foreach (var o in list) if (_p.Dist(o.X, o.Z, x, z) < 720) n++;
                return n;
            }
            order = order.OrderByDescending(o => Wanted(o.X, o.Z) + _r.Float() * 5f).ToList();
            // The spacings tried, widest first; a spot too near the towns for one spacing stays too near (towns only
            // come), so each spacing's scan picks up where it left off: the same towns as scanning from the start.
            var spacings = new List<double>();
            for (double spacing = 3200; spacing >= 1600; spacing *= 0.84) spacings.Add(spacing);
            var from = new int[spacings.Count];
            for (int n = 0; n < towns + hamlets; n++)
            {
                (double X, double Z)? at = null;
                for (int l = 0; l < spacings.Count && at == null; l++)
                    for (; from[l] < order.Count; from[l]++)
                        if (Far(order[from[l]].X, order[from[l]].Z, spacings[l])) { at = order[from[l]]; break; }
                if (at is not { } s) break;
                spots.Add(s);
                bool town = n < towns;
                Region region = AddRegion(town ? RegionKind.Town : RegionKind.Hamlet, s.X, s.Z, town ? 900 : 500);
                region.Town = region.Index;
                // A world's old heart, or whatever of the theme's places its tech can stand, or anyone who stayed.
                string? hub = Choose(_roll.Hub) ?? Choose(_roll.Farm) ?? Choose(_roll.Poi) ?? Choose(_roll.Industry)
                              ?? _lib.Families.Where(f => (f.Tags.Contains("settlement") || f.Tags.Contains("farm")) && Stands(f.Id)).Select(f => f.Id).FirstOrDefault();
                if (hub == null) continue;
                PlannedPlace? place = Add(hub, s.X, s.Z, region, "hub", town ? "the heart of a town" : "a hamlet");
                if (place != null) made.Add((region, place));
            }
            return made;
        }

        /// <summary>What a town had: its needs, what it lived on, what fed it, where its railway ended, the land that fed it.</summary>
        void Serve((Region Region, PlannedPlace Hub) t)
        {
            var (town, hub) = t;
            float h0 = _land.Height(town.X, town.Z);
            bool isTown = town.Kind == RegionKind.Town;
            foreach (string need in isTown ? _roll.Needs.AsEnumerable() : Array.Empty<string>())
            {
                Func<double, double, float>? prefer = need switch
                {
                    // Sewage runs downhill, water comes from up the hill.
                    "sewage_works" => (x, z) => (_land.Height(x, z) - h0) * 0.8f,
                    "water_works" => (x, z) => (h0 - _land.Height(x, z)) * 0.5f,
                    _ => null,
                };
                var (r0, r1) = need == "substation" ? (420.0, 720.0) : (620.0, 1200.0);
                if (Stands(need) && Spot(town.X, town.Z, r0, r1, need, prefer) is { } s)
                    Serves(need, s, town, "need", need == "sewage_works" ? "takes the town's sewage" : need == "water_works" ? "the town's water" : "feeds the town's streets", hub, "road");
            }
            if (isTown)
            {
                int lived = 1 + (_roll.RegionCount("industry") > 0 ? Count("industry") / Math.Max(1, Count("town")) : 0);
                for (int n = 0; n < lived; n++)
                {
                    if (Choose(_roll.Industry) is not { } id) break;
                    FamilyDef f = _lib.Family(id);
                    bool mine = f.Tags.Contains("mine");
                    if (Spot(town.X, town.Z, 900, 1600, id, mine ? (x, z) => _land.Rich(x, z) ? -14f : 0f : null) is { } s)
                    {
                        Region works = AddRegion(RegionKind.Industry, s.X, s.Z, 500, town.Index);
                        Serves(id, s, works, "industry", "what the town lived on", hub, _profile.Before == TechLevel.Low ? "road" : "highway");
                    }
                }
                // Where only the port had power, no plant fed a town's lines (PowerLines strings none to it).
                if (_roll.Power is not ("none" or "port_only") && Choose(_roll.Plant) is { } plant && Spot(town.X, town.Z, 1000, 1800, plant) is { } ps)
                {
                    PlannedPlace? pp = Serves(plant, ps, town, "plant", "feeds the town's lines", hub, "road");
                    if (pp != null) _p.Links.Add(new PlannedLink(pp.Index, hub.Index, "power", "the plant feeds its town"));
                }
                // A world that ran railways has a depot at each town's end of the line: its trade's own, or else a rail yard
                // (a railway with nowhere built for it to end was never laid, so without this a rail world whose trade
                // named no depot had no rail at all).
                string? depot = _roll.Depot ?? _lib.Families.Where(f => f.Tags.Contains("rail") && Stands(f.Id)).Select(f => f.Id).OrderBy(id => id, StringComparer.Ordinal).FirstOrDefault();
                if (Railed && depot != null && _profile.Before != TechLevel.High && Stands(depot) && Spot(town.X, town.Z, 460, 800, depot) is { } ds)
                    Serves(depot, ds, town, "depot", "where the town's railway ends", hub, "road");
            }
            // Farmland lies out beyond the town, on the side with the most level ground.
            int fields = Count("farmland");
            if (fields > 0 && Choose(_roll.Farm) != null)
            {
                int perTown = Math.Max(1, (int)Math.Ceiling(fields / (double)Math.Max(1, Count("town") + Count("hamlet"))));
                for (int n = 0; n < perTown; n++)
                {
                    if (Choose(_roll.Farm) is not { } farm) break;
                    if (Spot(town.X, town.Z, 900, 1900, farm) is not { } s) continue;
                    Region land = AddRegion(RegionKind.Farmland, s.X, s.Z, 800, town.Index);
                    Serves(farm, s, land, "farm", "fed the town", hub, _profile.Before == TechLevel.Low ? "track" : "road");
                }
            }
        }

        PlannedPlace? Serves(string family, (double X, double Z) at, Region region, string role, string why, PlannedPlace hub, string link)
        {
            PlannedPlace? p = Add(family, at.X, at.Z, region, role, why, hub.Index);
            if (p != null) _p.Links.Add(new PlannedLink(p.Index, hub.Index, link, why));
            return p;
        }

        /// <summary>The port, and on a world that ran for it the camps and pads round it. Highways to it from every town, and the railway, if the world had one.</summary>
        void Ports()
        {
            FamilyDef? port = _lib.Families.FirstOrDefault(f => f.Tier == PoiTier.Spaceport && !f.Tags.Contains("scrappy"));
            bool any = _profile.Starport != Starport.None && port != null;
            var towns = _p.Places.Where(p => p.Role == "hub").ToList();
            PlannedPlace? main = null;
            if (any)
            {
                var spot = _open.Where(o => towns.All(t => _p.Dist(t.X, t.Z, o.X, o.Z) > 1500)).OrderBy(o => _r.Float()).FirstOrDefault();
                if (spot == default) spot = _open[0];
                (double X, double Z) at = Spot(spot.X, spot.Z, 0, 900, port!.Id) ?? spot;
                Region region = AddRegion(RegionKind.Port, at.X, at.Z, 1100);
                region.Town = region.Index;
                main = Add(port.Id, at.X, at.Z, region, "port", "the world's main port");
                if (main != null)
                {
                    main.Primary = true;
                    main.Port = _profile.Starport;
                }
                for (int k = 0; k < _profile.SecondaryPorts; k++)
                {
                    Starport lesser = (Starport)Math.Max((int)Starport.E, (int)_profile.Starport - _r.Range(1, 3));
                    if (Spot(at.X, at.Z, 2400, 7000, port.Id) is { } s && Add(port.Id, s.X, s.Z, null, "port", "a lesser port") is { } p)
                    {
                        p.Port = lesser;
                        if (main != null) _p.Links.Add(new PlannedLink(p.Index, main.Index, "road", "a road between ports"));
                    }
                }
                FamilyDef? scrappy = _lib.Families.FirstOrDefault(f => f.Tags.Contains("scrappy")), camp = _lib.Families.FirstOrDefault(f => f.Tags.Contains("evacuation"));
                int pads = Math.Max(_profile.ScrappyPorts, _roll.RegionCount("scrappy_pad")), camps = Math.Max(_profile.EvacuationCamps, _roll.RegionCount("camp"));
                void Crowd(FamilyDef? f, int count, string why)
                {
                    for (int k = 0; f != null && k < count; k++)
                        if (Spot(at.X, at.Z, 520, 1800, f.Id) is { } s && Add(f.Id, s.X, s.Z, region, "camp", why, main?.Index ?? -1) is { } p && main != null)
                            _p.Links.Add(new PlannedLink(p.Index, main.Index, "track", why));
                }
                Crowd(scrappy, pads, "a pad thrown up when the queue stopped moving");
                Crowd(camp, camps, "where the queue slept");
            }
            // Towns joined to each other, by the nearest, and to the port.
            var hubs = towns.ToList();
            if (main != null) hubs.Add(main);
            var joined = new List<PlannedPlace>();
            string big = _profile.Before == TechLevel.Low ? "road" : "highway";
            foreach (PlannedPlace h in hubs.OrderBy(h => h.Primary ? 0 : 1).ThenBy(h => h.Index))
            {
                if (joined.Count > 0)
                {
                    PlannedPlace near = joined.OrderBy(j => _p.Dist(j.X, j.Z, h.X, h.Z)).First();
                    // The port is the network's main node, whatever the world's roads are like.
                    bool rural = _roll.Road == "track" && !h.Primary && !near.Primary;
                    _p.Links.Add(new PlannedLink(near.Index, h.Index, rural ? "track" : big, "between towns"));
                }
                joined.Add(h);
            }
            // The railway: from a depot to the next depot, and on to the port. Every line ends somewhere that was built for it.
            if (!Railed) return;
            var depots = _p.Places.Where(p => p.Role == "depot").ToList();
            for (int k = 0; k + 1 < depots.Count; k++)
                _p.Links.Add(new PlannedLink(depots[k].Index, depots[k + 1].Index, "rail", "the line between the towns' depots"));
            if (main != null && depots.Count > 0)
                _p.Links.Add(new PlannedLink(depots.OrderBy(d => _p.Dist(d.X, d.Z, main.X, main.Z)).First().Index, main.Index, "rail", "the line to the port"));
            foreach (PlannedPlace works in _p.Places.Where(p => p.Role == "industry").ToList())
                if (depots.OrderBy(d => _p.Dist(d.X, d.Z, works.X, works.Z)).FirstOrDefault() is { } d)
                    _p.Links.Add(new PlannedLink(d.Index, works.Index, "rail", "a spur from the depot to the works"));
        }

        void Wrecks()
        {
            for (int k = 0, n = Count("wreck_field"); k < n; k++)
            {
                var o = _open[_r.Range(0, _open.Count)];
                if (Spot(o.X, o.Z, 0, 1400, "crashed_freighter") is { } s)
                    Add("crashed_freighter", s.X, s.Z, null, "wreck", "it came down here");
            }
        }

        /// <summary>A blast site: a crater with its ring of flattened ruins.</summary>
        bool Blast(string id) => _lib.Family(id) is { } f && f.Tier == PoiTier.Site && f.Tags.Contains("radiation");

        /// <summary>The rest of the theme's places, near what they'd have stood near.</summary>
        void Fill()
        {
            int want = Math.Max(2, Count("town") + Count("hamlet"));
            var hubs = _p.Places.Where(p => p.Role == "hub").ToList();
            for (int k = 0; k < want && hubs.Count > 0; k++)
            {
                if (Choose(_roll.Poi) is not { } id) break;
                PlannedPlace near = hubs[k % hubs.Count];
                // A blast site was the town's own strike: it lies just outside it, not out in its country.
                bool blast = Blast(id);
                if (Spot(near.X, near.Z, blast ? 360 : 700, blast ? 800 : 2400, id) is { } s
                    && Add(id, s.X, s.Z, _p.Regions[near.Region], "fill", blast ? "where the strike on the town came down" : "part of the town's country", near.Index) is { } p)
                    _p.Links.Add(new PlannedLink(p.Index, near.Index, "track", "a track out to it"));
            }
        }

        /// <summary>The planned spacing of places over the land: no dry ground lies further than this from one (a little more in rough country, less by water).</summary>
        public const double Reach = 3400;

        /// <summary>
        /// The land between: wherever dry ground lies further than a short flight from anything planned, something
        /// stood there once: one of the theme's places, a farm by the water, or a small place of the world's other kinds
        /// (a camp, an outpost, a wreck). Thinner in rough country, closer by water, never a hole. Out of any region,
        /// so the towns' country stays the towns' own.
        /// </summary>
        void Cover()
        {
            const double Step = 1000;
            var land = new List<(double X, double Z, float Rough, bool Wet)>();
            for (double z = Margin; z < _land.Depth - Margin; z += Step)
                for (double x = Margin; x < _land.Width - Margin; x += Step)
                {
                    if (_land.Wet(x, z)) continue;
                    float rough = Roughness(x, z, 400);
                    bool wet = false;
                    for (int a = 0; a < 6 && !wet; a++) wet = _land.Wet(Wrap(x + Math.Cos(a * Math.PI / 3) * 700, _land.Width), Wrap(z + Math.Sin(a * Math.PI / 3) * 700, _land.Depth));
                    land.Add((x, z, rough, wet));
                }
            if (land.Count == 0) return;
            var near = new double[land.Count];
            for (int i = 0; i < land.Count; i++)
            {
                near[i] = double.MaxValue;
                foreach (PlannedPlace p in _p.Places) near[i] = Math.Min(near[i], _p.Dist(p.X, p.Z, land[i].X, land[i].Z));
            }
            double Allow(int i) => Reach * (land[i].Wet ? 0.85 : land[i].Rough < 0 || land[i].Rough > 40 ? 1.2 : 1.0);
            // The small places of the world's other kinds, for wild country the theme has nothing for.
            var small = _lib.Families.Where(f => !f.Space && f.Tier <= PoiTier.Outpost && !f.Tags.Contains("grand") && !f.Tags.Contains("flavour") && !f.Tags.Contains("city")
                                                 && !f.Tags.Contains("scrappy") && !f.Tags.Contains("evacuation") && !Blast(f.Id) && Stands(f.Id)
                                                 && (!_start || !ThemeDef.FactionOwned(f) || f.StateWeights[(int)PoiState.Ruined] > 0f)).Select(f => f.Id).ToList();
            for (int guard = 0; guard < land.Count; guard++)
            {
                int worst = -1;
                double most = 0;
                for (int i = 0; i < land.Count; i++)
                    if (near[i] / Allow(i) > most) { most = near[i] / Allow(i); worst = i; }
                if (worst < 0 || most <= 1) break;
                var (cx, cz, _, wet) = land[worst];
                PlannedPlace? placed = null;
                for (int k = 0; k < 6 && placed == null; k++)
                {
                    float roll = _r.Float();
                    string? id = roll < 0.45f ? Choose(_roll.Poi) : roll < 0.6f && wet ? Choose(_roll.Farm) : null;
                    if (id != null && Blast(id)) id = null;
                    if (id == null && small.Count > 0) id = small[_r.Range(0, small.Count)];
                    if (id == null) break;
                    double r1 = k < 3 ? 1200 : 2200;
                    if (Spot(cx, cz, 0, r1, id) is { } s)
                        placed = Add(id, s.X, s.Z, null, "cover", wet ? "by the water, out between the towns" : "out in the country between the towns", capped: false);
                }
                // Rough country: a small place on the least rough ground going.
                for (int k = 0; k < 3 && placed == null && small.Count > 0; k++)
                {
                    string id = small[_r.Range(0, small.Count)];
                    if (Spot(cx, cz, 0, 2400, id, null, 40) is { } s)
                        placed = Add(id, s.X, s.Z, null, "cover", "up in the rough country", capped: false);
                }
                if (placed == null)
                {
                    // Nowhere here will take one: let the ground round it go rather than ask again.
                    for (int i = 0; i < land.Count; i++)
                        if (_p.Dist(land[i].X, land[i].Z, cx, cz) < 1500) near[i] = 0;
                    continue;
                }
                for (int i = 0; i < land.Count; i++) near[i] = Math.Min(near[i], _p.Dist(placed.X, placed.Z, land[i].X, land[i].Z));
            }
        }

        /// <summary>A share of places from outside the theme, out in the empty land: something that doesn't belong.</summary>
        void Breaks()
        {
            int count = Math.Max(1, (int)Math.Round(_p.Places.Count * _roll.Break));
            var known = new HashSet<string>(_p.Places.Select(p => p.Family));
            // On a world a war struck, its blasts came down on what they aimed at: never one out in the empty land.
            bool war = _roll.All(LayerKind.Fall).Any(f => _lib.Strikes.For(f.Id) != null);
            var strangers = _lib.Families.Where(f => !f.Space && !f.Tags.Contains("flavour") && !f.Tags.Contains("scrappy") && !f.Tags.Contains("evacuation") && f.Tier != PoiTier.Spaceport
                                                     && !known.Contains(f.Id) && !f.Tags.Contains("grand") && Stands(f.Id) && (!_start || !ThemeDef.FactionOwned(f) || f.StateWeights[(int)PoiState.Ruined] > 0f)
                                                     && !(war && Blast(f.Id))).ToList();
            if (strangers.Count == 0) return;
            for (int k = 0; k < count; k++)
            {
                var o = _open[_r.Range(0, _open.Count)];
                var w = strangers.Select(f => 1f + (int)f.Rarity * 0.8f + f.Ominous * 2f).ToList();
                FamilyDef f = strangers[Math.Max(0, _r.PickWeighted(w))];
                if (_p.RegionAt(o.X, o.Z) != null) continue;
                if (Spot(o.X, o.Z, 0, 700, f.Id) is { } s && Add(f.Id, s.X, s.Z, null, "break", "nobody planned this") is { } p) p.Breaks = true;
            }
        }
    }
}
