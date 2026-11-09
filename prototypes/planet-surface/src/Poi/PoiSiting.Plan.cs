using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GalHack.Terrain;
using Planets.Gen;
using PoiGenerator.Gen;
using PRng = PoiGenerator.Gen.Rng;
using GHash = Planets.Gen.Hash;

namespace PlanetSurface.Poi;

/// <summary>
/// Setting a world down from its plan: what the world rolled (<see cref="WorldRoll"/>) decides what each
/// region was for (<see cref="RegionPlan"/>), and each place the plan wants is built, turned to face what
/// it served, given its people, and fitted to the ground near where the plan put it.
/// </summary>
public static partial class PoiSiting
{
    /// <summary>The planet's land as the region planner sees it.</summary>
    sealed class PlanetLand : ILand
    {
        readonly PlanetTerrainSource _bare;
        readonly PlanetMap _map;
        public PlanetLand(WorldMap map, PlanetTerrainSource bare, PlanetMap placement)
        {
            _bare = bare;
            _map = placement;
            Width = map.WidthMeters;
            Depth = map.HeightMeters;
        }
        public double Width { get; }
        public double Depth { get; }
        public float Height(double x, double z) => _bare.Sampler.Sample(x, z, 2).Height;
        public (float Height, bool Wet) Ground(double x, double z)
        {
            GroundSample g = _bare.Sampler.Sample(x, z, 2);
            return (g.Height, g.InRiver || g.Height < 2.5f);
        }
        public bool Wet(double x, double z)
        {
            GroundSample g = _bare.Sampler.Sample(x, z, 2);
            return g.InRiver || g.Height < 2.5f;
        }
        public bool Rich(double x, double z)
        {
            int cx = Math.Clamp((int)(x / _map.CellSize), 0, _map.Width - 1), cz = Math.Clamp((int)(z / _map.CellSize), 0, _map.Height - 1);
            return _map.Deposit[_map.Index(cx, cz)];
        }
    }

    /// <summary>Tests only: lay places out the old way (most families from their variant's one authored seed), to time against.</summary>
    public static bool FixedLayouts;

    /// <summary>How long the last world's places took to lay out and spawn, side by side (tests read it).</summary>
    public static double LastLayoutMs;

    /// <summary>The last world's planned places that never stood: no layout (skipped with a warning), or no ground found to seat them (tests read it).</summary>
    public static int LastSkipped, LastUnseated, LastPlanned;

    /// <summary>The last world's place timings, ms: its region plan, setting it down, and the seating part of that (tests print them).</summary>
    public static double LastPlanMs, LastSetDownMs, LastSeatMs;

    static Dir Toward(double dx, double dz) => Math.Abs(dx) >= Math.Abs(dz) ? (dx > 0 ? Dir.East : Dir.West) : (dz > 0 ? Dir.South : Dir.North);

    /// <summary>The sites a plan asks for, built side by side and seated in order. Empty if the plan found no land.</summary>
    static List<PoiSite> SetDown(PoiLibrary lib, RegionPlan plan, WorldRoll roll, WorldMap map, PlanetTerrainSource bare, LandingSite landing, WorldProfile profile, bool start, List<PoiSite> sites)
    {
        var rng = new PRng(GHash.Of(map.Spec.Seed, 0x5E7D0));
        double Away(PlannedPlace p) => Dist(p.X, p.Z, landing.X, landing.Y, map.WidthMeters, map.HeightMeters);
        var jobs = new List<(PlannedPlace Place, FamilyDef Family, PoiState State, int Variant, ulong Spawn, OccupancyRoll Occ)>();
        int living = 0, cap = profile.LivingCap;
        // Where people survive, a home standing whole is usually lived in.
        bool peopled = OccupancyLayer.Peopled(roll, profile);
        foreach (PlannedPlace p in plan.Places)
        {
            FamilyDef f = lib.Family(p.Family);
            if (start && f.Tags.Contains("radiation") && Away(p) < 3000) continue;
            if (start && f.Tags.Contains("settlement") && Away(p) < 3000) continue;
            // The world's odds for its places, where the family can be in that state at all.
            float[] w = profile.StateWeights(f);
            if (roll.States is { } s)
                for (int i = 0; i < w.Length; i++) w[i] = w[i] > 0f ? s[i] : 0f;
            if (w.All(v => v <= 0f)) w = profile.StateWeights(f);
            if (start && ThemeDef.FactionOwned(f))
                for (int i = 0; i < w.Length; i++) if (i != (int)PoiState.Ruined) w[i] = 0f;
            if (w.All(v => v <= 0f)) continue;
            var state = (PoiState)Math.Max(0, rng.PickWeighted(w.ToList()));
            // A shanty town is where the survivors live: lived in wherever people survive, the few as much as the many.
            bool shanty = p.Role == "shanty" && peopled && f.StateWeights[(int)PoiState.Active] > 0f;
            if (shanty) state = PoiState.Active;
            if (!shanty && WorldProfile.Living(f, state) && !WorldProfile.Unbuilt(f))
            {
                if (living >= cap)
                {
                    for (int i = 0; i < w.Length; i++) if (WorldProfile.Living(f, (PoiState)i)) w[i] = 0f;
                    if (w.All(v => v <= 0f)) continue;
                    state = (PoiState)Math.Max(0, rng.PickWeighted(w.ToList()));
                }
                else living++;
            }
            int variant = profile.Variant(f, state, rng, p.Port);
            ulong spawn = rng.NextU64();
            if (start && f.Tags.Contains("settlement") && Away(p) < 4000)
                for (ulong k = 1; k < 32 && Dressing.MoodFor(spawn) == Mood.Hostile; k++) spawn = PoiGenerator.Gen.Hash.Combine(spawn, k);
            OccupancyRoll occ = OccupancyLayer.Applies(f, state) ? OccupancyLayer.Roll(roll.Occupancy, f, spawn, null, peopled && OccupancyLayer.Livable(f, state)) with { Tags = roll.Tags } : null;
            jobs.Add((p, f, state, variant, spawn, occ));
        }
        // Whole works take a moment each: they're built side by side, then seated in order.
        var made = new PoiInstance[jobs.Count];
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Parallel.For(0, jobs.Count, i =>
        {
            var j = jobs[i];
            PlannedPlace served = j.Place.Serves >= 0 ? plan.Places[j.Place.Serves] : null;
            Dir front = served == null ? Dir.North
                : Toward(Mathx.WrapDelta(served.X - j.Place.X, map.WidthMeters), Mathx.WrapDelta(served.Z - j.Place.Z, map.HeightMeters));
            // Built in its age: the old world's places at the pre-collapse level, the living's (and what survivors
            // built) at the post-collapse one; a wreck or an alien place keeps its makers' own.
            TechLevel? age = WorldProfile.Unbuilt(j.Family) ? null : profile.LevelFor(j.Family, j.State);
            Rarity rarity = Deeper(j.Family.Rarity, roll.Depth, j.Spawn);
            var hints = new LayoutHints(front, age, Ruin: profile.Ruin);
            // Every spawn its own layout, from its own seed: the family's variants are what it draws from, never one building.
            string why;
            Layout layout = FixedLayouts
                ? PoiFactory.TryLayout(lib, j.Family, j.Variant, rarity, j.Family.Tags.Contains("grand") || j.Family.Tags.Contains("underground") ? PoiGenerator.Gen.Hash.Combine(j.Spawn, 0x6A11UL) : null, new LayoutHints(front), out why)
                : PoiFactory.SpawnLayout(lib, j.Family, j.Variant, rarity, j.Spawn, hints, out why);
            // A place that won't lay out is left off the map with a warning, never a crash.
            if (layout == null) { Godot.GD.PushWarning($"place skipped: {why}"); return; }
            layout.Name = j.Family.Variants[j.Variant].Name;
            made[i] = PoiFactory.Spawn(lib, layout, j.State, j.Spawn, profile, j.Occ);
        });
        LastLayoutMs = clock.Elapsed.TotalMilliseconds;
        LastPlanned = jobs.Count;
        LastSkipped = made.Count(m => m == null);
        LastUnseated = 0;
        var seatClock = System.Diagnostics.Stopwatch.StartNew();
        int mark = sites.Count;
        for (int i = 0; i < jobs.Count; i++)
        {
            var j = jobs[i];
            if (made[i] == null) continue;
            double x = Mathx.WrapX(j.Place.X, map.WidthMeters), z = Mathx.Wrap(j.Place.Z, map.HeightMeters);
            PoiSite site = Seat(made[i], j.Family, bare, map, x, z, landing, sites);
            int retries = j.Place.Primary ? 64 : j.Place.Role is "hub" or "port" or "need" or "industry" or "plant" ? 12 : 4;
            for (int k = 0; site == null && k < retries; k++)
            {
                double a = k * 2.399963, r = 300 + (j.Place.Primary ? 90 : 60) * k;
                site = Seat(made[i], j.Family, bare, map, Mathx.WrapX(x + Math.Cos(a) * r, map.WidthMeters), Mathx.Wrap(z + Math.Sin(a) * r, map.HeightMeters), landing, sites);
            }
            if (site == null) { LastUnseated++; continue; }
            site.Primary = j.Place.Primary;
            site.Planned = j.Place;
            site.Plan = plan;
            j.Place.Seated = site;
            sites.Add(site);
        }
        LastSeatMs = seatClock.Elapsed.TotalMilliseconds;
        return sites.Skip(mark).ToList();
    }
}
