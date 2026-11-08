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
        int MaxPlaces => 34;

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
                    if (_land.Wet(Wrap(sx, _land.Width), Wrap(sz, _land.Depth))) return -1;
                    float h = _land.Height(Wrap(sx, _land.Width), Wrap(sz, _land.Depth));
                    lo = MathF.Min(lo, h);
                    hi = MathF.Max(hi, h);
                }
            return hi - lo;
        }

        public void Run()
        {
            for (double z = Margin; z < _land.Depth - Margin; z += 240)
                for (double x = Margin; x < _land.Width - Margin; x += 240)
                {
                    float rough = Roughness(x, z, 240);
                    if (rough >= 0 && rough < 26) _open.Add((x, z));
                }
            if (_open.Count == 0) return;
            var towns = Hubs();
            if (_start) StartCountry();
            foreach (var t in towns) Serve(t);
            Ports();
            Wrecks();
            Fill();
            Breaks();
        }

        Region AddRegion(RegionKind kind, double x, double z, double radius, int town = -1)
        {
            var r = new Region { Index = _p.Regions.Count, Kind = kind, X = x, Z = z, Radius = radius, Town = town };
            _p.Regions.Add(r);
            return r;
        }

        PlannedPlace? Add(string family, double x, double z, Region? region, string role, string why, int serves = -1)
        {
            if (_p.Places.Count >= MaxPlaces) return null;
            var place = new PlannedPlace { Index = _p.Places.Count, Family = family, X = x, Z = z, Region = region?.Index ?? -1, Role = role, Why = why, Serves = serves };
            _p.Places.Add(place);
            return place;
        }

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
            var fit = table.Where(kv => Stands(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
            return _roll.Pick(fit, _r);
        }

        bool Clear(double x, double z, double half)
        {
            foreach (PlannedPlace o in _p.Places)
                if (_p.Dist(o.X, o.Z, x, z) < half + HalfOf(_lib.Family(o.Family)) + 200) return false;
            return _p.Dist(_landing.X, _landing.Z, x, z) > half + 260;
        }

        /// <summary>The best spot in a ring round a point for a family: level, clear, and however <paramref name="prefer"/> scores it (lower is better).</summary>
        (double X, double Z)? Spot(double cx, double cz, double r0, double r1, string family, Func<double, double, float>? prefer = null)
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
                if (rough < 0 || rough > 24 || !Clear(x, z, half)) continue;
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

        /// <summary>The towns: picked far apart, on the widest level ground, the hub at each one's heart.</summary>
        List<(Region Region, PlannedPlace Hub)> Hubs()
        {
            var made = new List<(Region, PlannedPlace)>();
            int towns = _roll.RegionCount("town"), hamlets = _roll.RegionCount("hamlet");
            var spots = new List<(double X, double Z)>();
            bool Far(double x, double z, double d) => spots.All(s => _p.Dist(s.X, s.Z, x, z) >= d) && _p.Dist(_landing.X, _landing.Z, x, z) >= (_start ? 1700 : 500);
            var order = _open.OrderBy(o => _r.Float()).ToList();
            // A big town wants big flat ground: the middle of the open land first.
            int Wanted(double x, double z) => _open.Count(o => _p.Dist(o.X, o.Z, x, z) < 720);
            order = order.OrderByDescending(o => Wanted(o.X, o.Z) + _r.Float() * 5f).ToList();
            for (int n = 0; n < towns + hamlets; n++)
            {
                (double X, double Z)? at = null;
                for (double spacing = 3200; spacing >= 1600 && at == null; spacing *= 0.84)
                    foreach (var o in order)
                        if (Far(o.X, o.Z, spacing)) { at = o; break; }
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
                int lived = 1 + (_roll.RegionCount("industry") > 0 ? _roll.RegionCount("industry") / Math.Max(1, _roll.RegionCount("town")) : 0);
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
                if (_roll.Power != "none" && Choose(_roll.Plant) is { } plant && Spot(town.X, town.Z, 1000, 1800, plant) is { } ps)
                {
                    PlannedPlace? pp = Serves(plant, ps, town, "plant", "feeds the town's lines", hub, "road");
                    if (pp != null) _p.Links.Add(new PlannedLink(pp.Index, hub.Index, "power", "the plant feeds its town"));
                }
                if (_roll.Rail != "none" && _roll.Depot is { } depot && _profile.Before != TechLevel.High && Stands(depot) && Spot(town.X, town.Z, 460, 800, depot) is { } ds)
                    Serves(depot, ds, town, "depot", "where the town's railway ends", hub, "road");
            }
            // Farmland lies out beyond the town, on the side with the most level ground.
            int fields = Math.Max(0, _roll.RegionCount("farmland"));
            if (fields > 0 && Choose(_roll.Farm) != null)
            {
                int perTown = Math.Max(1, (int)Math.Ceiling(fields / (double)Math.Max(1, _roll.RegionCount("town") + _roll.RegionCount("hamlet"))));
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
            if (_roll.Rail == "none") return;
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
            for (int k = 0, n = _roll.RegionCount("wreck_field"); k < n; k++)
            {
                var o = _open[_r.Range(0, _open.Count)];
                if (Spot(o.X, o.Z, 0, 1400, "crashed_freighter") is { } s)
                    Add("crashed_freighter", s.X, s.Z, null, "wreck", "it came down here");
            }
        }

        /// <summary>The rest of the theme's places, near what they'd have stood near.</summary>
        void Fill()
        {
            int want = Math.Max(2, _roll.RegionCount("town") + _roll.RegionCount("hamlet"));
            var hubs = _p.Places.Where(p => p.Role == "hub").ToList();
            for (int k = 0; k < want && hubs.Count > 0; k++)
            {
                if (Choose(_roll.Poi) is not { } id) break;
                PlannedPlace near = hubs[k % hubs.Count];
                if (Spot(near.X, near.Z, 700, 2400, id) is { } s && Add(id, s.X, s.Z, _p.Regions[near.Region], "fill", "part of the town's country", near.Index) is { } p)
                    _p.Links.Add(new PlannedLink(p.Index, near.Index, "track", "a track out to it"));
            }
        }

        /// <summary>A share of places from outside the theme, out in the empty land: something that doesn't belong.</summary>
        void Breaks()
        {
            int count = Math.Max(1, (int)Math.Round(_p.Places.Count * _roll.Break));
            var known = new HashSet<string>(_p.Places.Select(p => p.Family));
            var strangers = _lib.Families.Where(f => !f.Space && !f.Tags.Contains("flavour") && !f.Tags.Contains("scrappy") && !f.Tags.Contains("evacuation") && f.Tier != PoiTier.Spaceport
                                                     && !known.Contains(f.Id) && !f.Tags.Contains("grand") && Stands(f.Id) && (!_start || !ThemeDef.FactionOwned(f) || f.StateWeights[(int)PoiState.Ruined] > 0f)).ToList();
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
