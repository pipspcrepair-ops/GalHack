using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Planets.Gen;
using PoiGenerator.Gen;
using PlanetSurface.Infra;
using PlanetSurface.Terrain;
using CoverBuilder = PlanetSurface.Terrain.CoverBuilder;
using PlanetSurface.World;
using Wildlife;
using PoiSite = PlanetSurface.Poi.PoiSite;

namespace Slice.Tests;

/// <summary>
/// A war's craters: a war fall never makes a world cratered or airless (natural craters stay on barren,
/// airless bodies), and on fresh nuclear-fall worlds the blasts come down on what they aimed at: on or
/// near a target, almost never in open water, each crater in the ground with the dead land round it and
/// the places in its reach ruined by it. Fresh worlds every run.
/// </summary>
public partial class LayersTest
{
    /// <summary>A blast counts as on target this close to a target's edge, metres.</summary>
    const double OnTarget = 600;

    void WarFalls()
    {
        GD.Print("-- war falls and craters --");
        var war = _lib.Layers.Where(l => l.Kind == LayerKind.Fall && l.Group == "war").ToList();
        Check(war.Count > 5 && war.All(f => !f.Sets.Any(s => s.Fact == "air" && s.Value == "none")), $"no war fall ({war.Count}) takes the air away");
        Check(_lib.Strikes.Kinds.Count > 0 && _lib.Strikes.Kinds.All(k => k.Falls.All(id => war.Any(f => f.Id == id))) && _lib.Strikes.For("nuclear_war") != null,
            $"strikes come from war falls only: {string.Join(", ", _lib.Strikes.Kinds.SelectMany(k => k.Falls))}");
        // Fresh worlds of every type, a war forced on each: the type alone decides whether its rock is cratered and airless.
        int cratered = 0, wrong = 0, worlds = 0;
        var notes = new List<string>();
        foreach (Archetype a in Enum.GetValues<Archetype>())
            for (int i = 0; i < 60; i++)
            {
                ulong seed = Seed();
                string fall = war[_rng.Next(war.Count)].Id;
                PlanetSpec spec = PlanetSpec.Roll(seed, a, _rng.Next(PlanetSpec.VariantCount));
                WorldRoll roll = Roll(seed, a.ToString(), new WorldRoll.Given().Set(LayerKind.Fall, fall));
                worlds++;
                if (spec.Has(Modifier.Cratered)) cratered++;
                bool natural = PlanetSpec.Craters(a);
                // The fall never takes the air the body had (a lava world may have had none to begin with).
                bool tookAir = roll != null && roll.Fact("air") == "none" && roll.One(LayerKind.Air)?.Id != "none";
                bool bad = !natural && (spec.Has(Modifier.Cratered) || spec.Airless || PlanetFlora.Airless(spec) || WorldConditions.Of(spec).Vacuum || Planets.Gen.Archetypes.For(spec).CraterSpacing > 0f)
                           || tookAir;
                if (bad) { wrong++; notes.Add($"{a} {fall} seed {seed}: {spec.Modifiers}, air {roll?.One(LayerKind.Air)?.Id} to {roll?.Fact("air")}"); }
            }
        Check(wrong == 0 && cratered > 0, $"{worlds} fresh worlds with a war: none cratered or airless but barren rock ({cratered} cratered, all arid, ice or lava){(wrong > 0 ? "; " + string.Join("; ", notes.Take(3)) : "")}");
        // Natural craters only where they stay, even when asked for on a living world.
        PlanetSpec living = new(Seed(), Archetype.Temperate, 0, Modifier.Cratered);
        Check(Planets.Gen.Archetypes.For(living).CraterSpacing == 0f && !living.Airless && !WorldConditions.Of(living).Vacuum, "a temperate world told it's cratered keeps its air and grows no craters");
    }

    void StrikesOnTargets()
    {
        GD.Print("-- strikes on targets --");
        WorldForce before = WorldForce.Current;
        bool start = PlanetSurface.Poi.PoiSiting.StartWorld;
        PlanetSurface.Poi.PoiSiting.StartWorld = false;
        int strikes = 0, onTarget = 0, wet = 0, wetNaval = 0, bowls = 0, sunk = 0, dead = 0, ruined = 0, near = 0, built = 0;
        int sized = 0, townBursts = 0, within = 0, inTarget = 0, leftIn = 0, hot = 0, cool = 0, rads = 0, warned = 0;
        float least = float.MaxValue, largest = 0f;
        // The places round each ground zero, by how hard it came down where they stand: the tallest thing left on each.
        var nearRim = new List<int>(); var middle = new List<int>(); var farOut = new List<int>();
        List<string> off = new(), unblasted = new(), stood = new(), shallow = new();
        try
        {
            for (int tries = 0; built < 5 && tries < 12; tries++)
            {
                WorldForce.Current = new WorldForce { Fall = "nuclear_war", MidCollapse = false, Before = _rng.Next(2) == 0 ? TechLevel.Mid : TechLevel.High };
                PlanetSpec spec = PlanetSpec.Roll(Seed(), Archetype.Temperate, 0);
                PlanetWorld w = PlanetWorld.Build(spec, Slice.Surface.SurfaceZone.PoiCatalog);
                if (w.Roll == null) continue;
                built++;
                Check(!spec.Has(Modifier.Cratered) && Planets.Gen.Archetypes.For(spec).CraterSpacing == 0f && !PlanetFlora.Airless(spec), $"nuclear world {spec.Seed}: no natural craters, air kept");
                double W = w.Map.WidthMeters, H = w.Map.HeightMeters;
                double Dist(double ax, double az, double bx, double bz) => Math.Sqrt(Math.Pow(Mathx.WrapDelta(ax - bx, W), 2) + Math.Pow(Mathx.WrapDelta(az - bz, H), 2));
                // What could have been aimed at: the targets the war saw, and every town's heart.
                var targets = w.Infra.Targets.Select(t => (t.X, t.Z, (double)t.Reach)).ToList();
                targets.AddRange(w.Pois.Where(p => p.Planned?.Role == "hub").Select(p => (p.Center.X, p.Center.Z, Math.Max(p.Stamp.MaxX - p.Stamp.MinX, p.Stamp.MaxZ - p.Stamp.MinZ) * 0.5)));
                foreach (Strike s in w.Blasts.Where(s => !s.Field))
                {
                    strikes++;
                    double edge = targets.Count == 0 ? double.MaxValue : targets.Min(t => Dist(t.X, t.Z, s.X, s.Z) - t.Item3);
                    if (edge <= OnTarget) onTarget++;
                    else off.Add($"{s} ({edge:F0} m from a target's edge)");
                    if (w.Map.Has(w.Map.CellAt(s.X, s.Z), CellFlags.Water)) { wet++; if (s.Naval) wetNaval++; }
                    if (w.Palette.Deadness(s.X, s.Z) >= 0.99f) dead++;
                    if (s.Bowl && !s.Site)
                    {
                        bowls++;
                        sized++;
                        least = MathF.Min(least, s.Crater);
                        largest = MathF.Max(largest, s.Crater);
                        // A ground burst on a city or a town came down in it, and nothing it stood on is left in its bowl.
                        if (s.Target is "city" or "town" or "capital" && !s.Miss)
                        {
                            townBursts++;
                            if (s.Within) within++;
                            if (s.Within && Dist(s.X, s.Z, s.AimX, s.AimZ) <= s.AimReach + s.Crater * 0.3 + 1) inTarget++;
                        }
                        if (s.Within)
                            foreach (PoiSite p in w.Pois.Where(p => Dist(p.Center.X, p.Center.Z, s.X, s.Z) < s.Crater))
                            {
                                leftIn++;
                                stood.Add($"{p.Name} {Dist(p.Center.X, p.Center.Z, s.X, s.Z):F0} m from {s}");
                            }
                        // The crater is in the ground: its floor lies well below its rim.
                        float floor = w.Source.SurfaceHeight(s.X, s.Z), rim = w.Source.SurfaceHeight(Mathx.WrapX(s.X + s.Crater, W), s.Z);
                        if (rim - floor > s.Depth * 0.6f) sunk++;
                        else if (shallow.Count < 3) shallow.Add($"{s}{(s.Within ? " in its town" : "")}: floor {floor:F1}, rim {rim:F1}, depth {s.Depth:F1}, rim {s.Rim:F1}, sea {w.Map.Has(w.Map.CellAt(s.X, s.Z), CellFlags.Water)}");
                    }
                    // Hot ground: the crater past the damage line, nothing past the dead ring, warning posts at its edge.
                    if (s.Rad > 0f)
                    {
                        rads++;
                        if (w.RadiationAt(s.X, s.Z) >= 5f) hot++;
                        // Past its dead ring nothing (where no other's ring reaches).
                        double ox = Mathx.WrapX(s.X + s.Dead * 1.05, W);
                        if (w.RadiationAt(ox, s.Z) <= 0f || w.Blasts.Any(o => o != s && o.Rad > 0f && Dist(o.X, o.Z, ox, s.Z) < o.Dead)) cool++;
                        if (w.Infra.Ruins.Count(r => r.Family.Tags.Contains("warning") && Math.Abs(Dist(r.Center.X, r.Center.Z, s.X, s.Z) - s.Dead) < 40) >= 2) warned++;
                    }
                    // The ring round a nuclear ground zero grades by distance: by the rim nothing standing, then more and more.
                    if (s.Flatten && s.Bowl)
                        foreach (PoiSite p in w.Pois.Where(p => p.GroundZero == null))
                        {
                            float d = (float)Dist(p.Center.X, p.Center.Z, s.X, s.Z), k = s.Strength(d);
                            if (k <= 0.05f || d < s.Crater) continue;
                            int tallest = p.Instance.Blocks.Blocks().Where(b => !b.Block.IsAir && b.Cell.Y > 0).Select(b => b.Cell.Y).DefaultIfEmpty(0).Max();
                            (k >= StateTransforms.Rim ? nearRim : k >= StateTransforms.ShellFrom ? middle : farOut).Add(tallest);
                        }
                    // The places in its flattened ring took it.
                    foreach (PoiSite p in w.Pois.Where(p => p.GroundZero == null && Dist(p.Center.X, p.Center.Z, s.X, s.Z) < s.Ruin * 0.7))
                    {
                        near++;
                        if (p.Instance.Blasted > 0f) ruined++;
                        else unblasted.Add($"{p.Name} {Dist(p.Center.X, p.Center.Z, s.X, s.Z):F0} m from {s}");
                    }
                }
                GD.Print($"(world {spec.Seed}: {w.Blasts.Count} blasts on {w.Infra.Targets.Count} targets: {string.Join("; ", w.Blasts.Take(4))})");
            }
        }
        finally
        {
            WorldForce.Current = before;
            PlanetSurface.Poi.PoiSiting.StartWorld = start;
        }
        Check(built >= 3 && strikes >= built * 3, $"{strikes} blasts over {built} fresh nuclear worlds");
        Check(strikes > 0 && onTarget >= strikes * 0.8, $"{onTarget} of {strikes} blasts within {OnTarget:F0} m of a target{(off.Count > 0 ? "; off: " + string.Join("; ", off.Take(3)) : "")}");
        Check(strikes > 0 && wet < strikes * 0.05 && wet == wetNaval, $"{wet} of {strikes} in open water, {wetNaval} of those at ships");
        Check(bowls > 0 && sunk == bowls, $"every ground burst's crater is in the ground: {sunk} of {bowls}{(shallow.Count > 0 ? "; " + string.Join("; ", shallow) : "")}");
        Check(dead == strikes, $"the land is dead round every blast: {dead} of {strikes}");
        Check(near == 0 || ruined == near, $"every place in a blast's flattened ring took it: {ruined} of {near}{(ruined < near ? "; " + string.Join("; ", unblasted.Take(2)) : "")}");
        // The natural craters of the airless worlds run 65 to 270 m (planet-generator TerrainModel): a nuclear crater is on their scale.
        Check(sized > 0 && least >= 65f && largest <= 270f, $"{sized} nuclear craters {least:F0} to {largest:F0} m across their radius, within the natural craters' 65 to 270 m");
        Check(townBursts == 0 || within >= townBursts * 0.5 && inTarget == within, $"{within} of {townBursts} ground bursts on cities and towns came down in them ({inTarget} with ground zero inside the town)");
        Check(leftIn == 0, $"nothing stands in a crater punched into a town: {leftIn}{(stood.Count > 0 ? "; " + string.Join("; ", stood.Take(2)) : "")}");
        // The rule itself, column by column: slabs by the rim, stumps of a storey or two, then no cap at all.
        int Most(float k) => Enumerable.Range(0, 400).Max(h => StateTransforms.Cap(k, (uint)h));
        int Least(float k) => Enumerable.Range(0, 400).Min(h => StateTransforms.Cap(k, (uint)h));
        Check(Most(0.95f) <= 3 && Most(0.8f) <= 6 && Least(0.65f) >= 6 && Most(0.65f) <= 10 && Most(StateTransforms.StumpFrom - 0.01f) == int.MaxValue,
            $"the flattening by strength: by the rim at most {Most(0.95f)} m standing, at 0.8 {Most(0.8f)} m, at 0.65 {Least(0.65f)} to {Most(0.65f)} m, and below {StateTransforms.StumpFrom} no stumps (shells, then standing ruins)");
        static double Standing(List<int> l) => l.Count == 0 ? 0 : l.Count(t => t >= 6) / (double)l.Count;
        Check(nearRim.Count + middle.Count + farOut.Count >= 4 && Standing(nearRim) <= 0.1 && (farOut.Count == 0 || Standing(farOut) > Standing(nearRim))
              && (middle.Count == 0 || nearRim.Count == 0 || middle.Average() >= nearRim.Average()),
            $"round ground zero the ruins grade by distance: by the rim {Standing(nearRim):P0} of {nearRim.Count} stand over 6 m (tallest mean {(nearRim.Count > 0 ? nearRim.Average() : 0):F1}), " +
            $"stumps and shells {Standing(middle):P0} of {middle.Count} ({(middle.Count > 0 ? middle.Average() : 0):F1}), further out {Standing(farOut):P0} of {farOut.Count} ({(farOut.Count > 0 ? farOut.Average() : 0):F1})");
        Check(rads > 0 && hot == rads && cool == rads && warned >= rads * 0.8, $"{rads} nuclear blasts leave the ground hot: {hot} past the damage line at ground zero, {cool} clear past the dead ring, {warned} with warning posts at its edge");
    }

    /// <summary>
    /// A war's crater fields on fresh worlds of each fall that brings one: bombardments and rocks from space
    /// dozens to a few hundred craters on a 16x8 km world, most small and the odd one big; the ground wars
    /// shell-hole fields of small to middling holes; none in open water. An old fall's holes have grown back
    /// (grass and scrub in most of them, the flora's own); a fresh one's are bare.
    /// </summary>
    void WarFields()
    {
        GD.Print("-- crater fields --");
        WorldForce before = WorldForce.Current;
        bool start = PlanetSurface.Poi.PoiSiting.StartWorld;
        PlanetSurface.Poi.PoiSiting.StartWorld = false;
        int old = 0, oldGrown = 0, fresh = 0, freshBare = 0, wet = 0, holes = 0;
        var notes = new List<string>();
        try
        {
            foreach (string fall in new[] { "orbital_bombardment", "kinetic_strikes", "invasion", "civil_war", "drone_wars", "blockade", "mercenary_seizure" })
            {
                bool ground = fall is not ("orbital_bombardment" or "kinetic_strikes");
                int worlds = ground ? 1 : 2;
                for (int i = 0, tries = 0; i < worlds && tries < 4; tries++)
                {
                    // Half of them long ago, half within a generation: whatever else the world rolls.
                    bool longAgo = (i + _rng.Next(2)) % 2 == 0;
                    WorldForce.Current = new WorldForce { Fall = fall, MidCollapse = false, Before = TechLevel.Mid, Layers = longAgo ? "age.long" : "age.recent" };
                    PlanetSpec spec = PlanetSpec.Roll(Seed(), Archetype.Temperate, 0);
                    PlanetWorld w = PlanetWorld.Build(spec, Slice.Surface.SurfaceZone.PoiCatalog);
                    if (w.Roll == null || w.Roll.One(LayerKind.Fall)?.Id != fall) continue;
                    i++;
                    double W = w.Map.WidthMeters, H = w.Map.HeightMeters, scale = W * H / 128e6;
                    // This war's own (a world may carry another war's scar too).
                    string kind = _lib.Strikes.For(fall).Id;
                    var bowls = w.Blasts.Where(s => s.Bowl && !s.Site && s.Kind == kind).ToList();
                    holes += bowls.Count;
                    int small = bowls.Count(s => s.Crater <= 10), big = bowls.Count(s => s.Crater >= 20);
                    float most = bowls.Count > 0 ? bowls.Max(s => s.Crater) : 0f;
                    double per = bowls.Count / scale;
                    GD.Print($"({fall} world {spec.Seed}, age {w.Reading.Age:F2}, severity {w.Reading.Severity:F2}: {bowls.Count} craters on {w.Infra.Targets.Count} targets, {small} of 10 m or less, {big} of 20 m or more, largest {most:F0} m)");
                    if (ground)
                        Check(per >= 15 && per <= 400 && most <= 16f && small >= bowls.Count * 0.6, $"{fall}: {bowls.Count} shell holes ({per:F0} on a 16x8 km world), small to middling, the largest {most:F0} m");
                    else
                        Check(per >= 24 && per <= 450 && small >= bowls.Count * 0.5 && big >= 1 && big < small && most <= 60.5f,
                            $"{fall}: {bowls.Count} craters ({per:F0} on a 16x8 km world), {small} of 3 to 10 m, {big} of 20 to 60 m, the largest {most:F0} m");
                    wet += bowls.Count(s => w.Map.Has(w.Map.CellAt(s.X, s.Z), CellFlags.Water));
                    // Grown back or bare: the ground cover the flora lays in each hole (a sample of them).
                    foreach (Strike s in bowls.OrderBy(_ => _rng.Next()).Take(25))
                    {
                        bool grown = Grown(w, s);
                        if (w.Reading.Age >= 0.6f) { old++; if (grown) oldGrown++; else if (notes.Count < 3) notes.Add($"{s} bare at age {w.Reading.Age:F2}"); }
                        else if (w.Reading.Age <= 0.35f) { fresh++; if (w.Palette.Deadness(s.X, s.Z) >= 0.9f) freshBare++; }
                    }
                }
            }
        }
        finally
        {
            WorldForce.Current = before;
            PlanetSurface.Poi.PoiSiting.StartWorld = start;
        }
        Check(holes > 0 && wet == 0, $"{wet} of {holes} craters in open water");
        Check(old >= 10 && oldGrown >= old * 0.6, $"{oldGrown} of {old} craters of an old fall have plants growing in them{(notes.Count > 0 ? "; " + string.Join("; ", notes) : "")}");
        Check(fresh == 0 || freshBare >= fresh * 0.8, $"{freshBare} of {fresh} craters of a recent fall still bare and dead");
    }

    /// <summary>Whether the world's ground cover grows in a crater: any tuft of it inside the bowl.</summary>
    static bool Grown(PlanetWorld w, Strike s)
    {
        int c = CoverBuilder.CellSize;
        int x0 = (int)Math.Floor((s.X - s.Crater) / c), x1 = (int)Math.Floor((s.X + s.Crater) / c);
        int z0 = (int)Math.Floor((s.Z - s.Crater) / c), z1 = (int)Math.Floor((s.Z + s.Crater) / c);
        for (int cz = z0; cz <= z1; cz++)
            for (int cx = x0; cx <= x1; cx++)
            {
                CoverCell cell = CoverBuilder.Build(cx, cz, w.Source, w.Palette, new HashSet<long>());
                foreach (var (_, buffer, count) in cell.Plants)
                    for (int k = 0; k < count; k++)
                    {
                        double px = cx * c + buffer[k * 16 + 3], pz = cz * c + buffer[k * 16 + 11];
                        double dx = Mathx.WrapDelta(px - s.X, w.Map.WidthMeters), dz = Mathx.WrapDelta(pz - s.Z, w.Map.HeightMeters);
                        if (dx * dx + dz * dz < s.Crater * s.Crater) return true;
                    }
            }
        return false;
    }
}